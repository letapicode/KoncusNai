using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using DictateAnywhere.Core.Contracts;
using PDFtoImage;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace DictateAnywhere.App.Workbench;

/// <summary>Extracts readable body text locally. It deliberately does not upload documents or retain their contents.</summary>
internal static class ReadableDocumentTextExtractor
{
  private const string OcrUnavailableMessage =
    "Scanned-page OCR is not available for the selected reading language. Choose a language with OCR support or import a document with searchable text.";

  public const string SupportedFileDialogFilter =
    "Readable documents|*.pdf;*.epub;*.docx;*.txt;*.md;*.rtf;*.html;*.htm;*.csv;*.json;*.xml;*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tif;*.tiff|"
    + "Books|*.epub;*.pdf|Scans and images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tif;*.tiff|Word documents|*.docx|Text files|*.txt;*.md;*.rtf;*.html;*.htm;*.csv;*.json;*.xml";

  private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff"];

  public static string Extract(string path)
    => ExtractStructured(path).Text;

  internal static ReadableDocumentContent ExtractStructured(string path, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
    {
      throw new FileNotFoundException("The selected document was not found.", path);
    }

    DocumentImportBudget budget = new(cancellationToken);
    budget.CheckFile(path);
    ReadableDocumentContent content = Path.GetExtension(path).ToLowerInvariant() switch
    {
      ".pdf" => ExtractPdfDocument(path),
      ".epub" => ReadableDocumentContent.FromPlainText(ExtractEpub(path, budget)),
      ".docx" => ExtractDocxDocument(path, budget),
      ".rtf" => ReadableDocumentContent.FromPlainText(ExtractRtf(budget.ReadFile(path))),
      ".txt" or ".md" or ".csv" or ".json" or ".xml" => ReadableDocumentContent.FromPlainText(budget.ReadFile(path)),
      ".html" or ".htm" => ReadableDocumentContent.FromPlainText(ExtractHtml(budget.ReadFile(path))),
      ".doc" => throw new NotSupportedException("Legacy .doc files are not supported yet. Save the file as .docx or PDF, then import it."),
      _ => throw new NotSupportedException("This file type cannot be read aloud. Choose EPUB, PDF, DOCX, an image, or a text-based document."),
    };
    budget.CheckCancellation();
    return content;
  }

  public static async Task<string> ExtractAsync(
    string path,
    string language,
    IDocumentOcrService ocrService,
    IProgress<DocumentImportProgress>? progress = null,
    CancellationToken cancellationToken = default)
  {
    ReadableDocumentContent document = await ExtractStructuredAsync(
      path,
      language,
      ocrService,
      progress,
      cancellationToken).ConfigureAwait(false);
    return document.Text;
  }

  public static async Task<ReadableDocumentContent> ExtractStructuredAsync(
    string path,
    string language,
    IDocumentOcrService ocrService,
    IProgress<DocumentImportProgress>? progress = null,
    CancellationToken cancellationToken = default)
    => await ExtractStructuredAsync(
      path,
      language,
      ocrService,
      allowOcr: true,
      progress,
      cancellationToken).ConfigureAwait(false);

  internal static async Task<ReadableDocumentContent> ExtractStructuredAsync(
    string path,
    string language,
    IDocumentOcrService ocrService,
    bool allowOcr,
    IProgress<DocumentImportProgress>? progress = null,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(ocrService);
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
    {
      throw new FileNotFoundException("The selected document was not found.", path);
    }

    string extension = Path.GetExtension(path).ToLowerInvariant();
    if (ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
    {
      EnsureOcrAvailable(allowOcr);
      progress?.Report(new DocumentImportProgress("Reading image text", 0.1d, "Recognizing text locally with RapidOCR."));
      DocumentOcrResult result = await ocrService.RecognizeAsync(new DocumentOcrRequest(path, language), cancellationToken).ConfigureAwait(false);
      if (string.IsNullOrWhiteSpace(result.Text))
      {
        throw new InvalidOperationException("No readable text was found in that image.");
      }

      progress?.Report(new DocumentImportProgress("Image text ready", 1d, "The recognized text is ready to review."));
      return ReadableDocumentContent.FromPlainText(result.Text);
    }

    if (!extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
    {
      progress?.Report(new DocumentImportProgress("Opening document", 0.4d, "Extracting the document's existing text layer."));
      ReadableDocumentContent content = await Task.Run(
        () => ExtractStructured(path, cancellationToken), cancellationToken).ConfigureAwait(false);
      progress?.Report(new DocumentImportProgress("Document ready", 1d, "The document text is ready to review."));
      return content;
    }

    return await ExtractPdfWithOcrFallbackAsync(path, language, ocrService, allowOcr, progress, cancellationToken).ConfigureAwait(false);
  }

  internal static bool NeedsOcr(string text)
  {
    if (string.IsNullOrWhiteSpace(text))
    {
      return true;
    }

    return text.Count(char.IsLetterOrDigit) < 32 || ContainsUnreadableGlyphs(text);
  }

  private static bool ContainsUnreadableGlyphs(string text) => text.Any(character =>
    char.IsControl(character) && character is not '\r' and not '\n' and not '\t');

  private static async Task<ReadableDocumentContent> ExtractPdfWithOcrFallbackAsync(
    string path,
    string language,
    IDocumentOcrService ocrService,
    bool allowOcr,
    IProgress<DocumentImportProgress>? progress,
    CancellationToken cancellationToken)
  {
    PdfPageExtraction[] nativePages = await Task.Run(() =>
    {
      using PdfDocument pdf = PdfDocument.Open(path);
      return pdf.GetPages().Select(ExtractPdfPage).ToArray();
    }, cancellationToken).ConfigureAwait(false);

    if (nativePages.Length == 0)
    {
      throw new InvalidOperationException("The PDF contains no pages.");
    }

    List<string> pages = new(nativePages.Length);
    List<ReadableDocumentElement> elements = [];
    string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"notype-ocr-{Guid.NewGuid():N}");
    Directory.CreateDirectory(temporaryDirectory);
    try
    {
      using FileStream pdfStream = File.OpenRead(path);
      for (int pageIndex = 0; pageIndex < nativePages.Length; pageIndex++)
      {
        cancellationToken.ThrowIfCancellationRequested();
        PdfPageExtraction page = nativePages[pageIndex];
        string pageText = page.Text;
        IReadOnlyList<ReadableDocumentElement> pageElements = page.Elements;
        double fraction = (double)pageIndex / nativePages.Length;
        if (NeedsOcr(pageText))
        {
          EnsureOcrAvailable(allowOcr);
          progress?.Report(new DocumentImportProgress(
            "Scanning document",
            fraction,
            $"Recognizing page {pageIndex + 1:N0} of {nativePages.Length:N0} locally."));
          string imagePath = Path.Combine(temporaryDirectory, $"page-{pageIndex + 1:D6}.png");
          pdfStream.Position = 0;
          await Task.Run(() => Conversion.SavePng(imagePath, pdfStream, pageIndex), cancellationToken).ConfigureAwait(false);
          DocumentOcrResult ocr = await ocrService.RecognizeAsync(
            new DocumentOcrRequest(imagePath, language),
            cancellationToken).ConfigureAwait(false);
          if (!string.IsNullOrWhiteSpace(ocr.Text))
          {
            if (pageElements.Any(element => ContainsUnreadableGlyphs(element.Text)))
            {
              pageElements = RepairUnreadablePdfElements(pageElements, ocr.Text);
              pageText = string.Join(Environment.NewLine, pageElements.Select(element => element.Text));
            }
            else
            {
              pageText = ocr.Text;
              pageElements = [new ReadableDocumentElement(pageText.Trim(), IsHeading: false, StartsParagraph: true)];
            }
          }
        }
        else
        {
          progress?.Report(new DocumentImportProgress(
            "Opening document",
            fraction,
            $"Using the searchable text on page {pageIndex + 1:N0} of {nativePages.Length:N0}."));
        }

        if (!string.IsNullOrWhiteSpace(pageText))
        {
          pages.Add(pageText.Trim());
          elements.AddRange(pageElements);
        }
      }
    }
    finally
    {
      TryDeleteDirectory(temporaryDirectory);
    }

    if (pages.Count == 0)
    {
      throw new InvalidOperationException("No readable text was found in this PDF.");
    }

    progress?.Report(new DocumentImportProgress("Document ready", 1d, "Searchable text and scanned pages are ready to review."));
    return ReadableDocumentContent.FromElements(
      string.Join(Environment.NewLine + Environment.NewLine, pages),
      elements);
  }

  private static void EnsureOcrAvailable(bool allowOcr)
  {
    if (!allowOcr)
    {
      throw new NotSupportedException(OcrUnavailableMessage);
    }
  }

  private static void TryDeleteDirectory(string path)
  {
    try
    {
      if (Directory.Exists(path))
      {
        Directory.Delete(path, recursive: true);
      }
    }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
  }

  private static ReadableDocumentContent ExtractPdfDocument(string path)
  {
    using PdfDocument document = PdfDocument.Open(path);
    PdfPageExtraction[] pages = document.GetPages().Select(ExtractPdfPage).ToArray();
    return ReadableDocumentContent.FromElements(
      string.Join(Environment.NewLine + Environment.NewLine, pages.Select(page => page.Text)),
      pages.SelectMany(page => page.Elements));
  }

  /// <summary>
  /// Prefers the PDF text layer, but repairs files whose text stream omits every
  /// word separator. PdfPig's geometric word extractor preserves those gaps.
  /// </summary>
  private static string ExtractPdfPageText(UglyToad.PdfPig.Content.Page page)
    => ExtractPdfPage(page).Text;

  private static PdfPageExtraction ExtractPdfPage(UglyToad.PdfPig.Content.Page page)
  {
    string nativeText = NormalizePdfText(page.Text);
    PdfWordPosition[] words = page.GetWords()
      .Where(word => !string.IsNullOrWhiteSpace(word.Text))
      .Select(word => new PdfWordPosition(
        NormalizePdfText(word.Text),
        word.BoundingBox.Left,
        word.BoundingBox.Bottom,
        Math.Max(1d, word.BoundingBox.Height),
        word.FontName))
      .ToArray();
    string geometricWordText = ReconstructPdfText(words);
    return new PdfPageExtraction(
      SelectReadablePdfText(nativeText, geometricWordText),
      ReconstructPdfElements(words));
  }

  private static string NormalizePdfText(string text) => text.Normalize(NormalizationForm.FormKC);

  internal static IReadOnlyList<ReadableDocumentElement> RepairUnreadablePdfElements(
    IReadOnlyList<ReadableDocumentElement> elements,
    string ocrText)
  {
    if (string.IsNullOrWhiteSpace(ocrText))
    {
      return elements;
    }

    return elements.Select(element => ContainsUnreadableGlyphs(element.Text)
      ? element with { Text = FindOcrReplacement(element.Text, ocrText) }
      : element).ToArray();
  }

  private static string FindOcrReplacement(string damagedText, string ocrText)
  {
    string[] readableParts = Regex.Split(damagedText, @"[\p{Cc}]+")
      .Select(part => part.Trim())
      .Where(part => part.Length > 0)
      .ToArray();
    if (readableParts.Length < 2)
    {
      return damagedText;
    }

    string pattern = string.Join(
      @"(?<recovered>\S{1,8})",
      readableParts.Select(part => Regex.Replace(Regex.Escape(part), @"\\ ", @"\s+")));
    Match match = Regex.Match(ocrText, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    return match.Success
      ? Regex.Replace(match.Value, @"\s+", " ").Trim()
      : damagedText;
  }

  internal static string SelectReadablePdfText(string nativeText, string geometricWordText)
  {
    if (string.IsNullOrWhiteSpace(nativeText))
    {
      return geometricWordText;
    }

    if (string.IsNullOrWhiteSpace(geometricWordText))
    {
      return nativeText;
    }

    int nativeLetters = nativeText.Count(char.IsLetterOrDigit);
    int geometricLetters = geometricWordText.Count(char.IsLetterOrDigit);
    int nativeWords = Regex.Matches(nativeText, @"\p{L}[\p{L}\p{M}\p{N}'’\-]*").Count;
    int geometricWords = Regex.Matches(geometricWordText, @"\p{L}[\p{L}\p{M}\p{N}'’\-]*").Count;
    int meaningfulWordGain = Math.Max(1, (int)Math.Ceiling(nativeWords * 0.01d));
    bool geometricHasMoreCompleteWords = geometricWords >= nativeWords + meaningfulWordGain
      && geometricLetters >= nativeLetters * 0.98d;
    bool geometricHasBetterCoverage = geometricLetters >= nativeLetters * 1.04d
      && geometricWords >= nativeWords;
    bool repairsCollapsedBoundaries = HasCollapsedWordBoundaries(nativeText)
      && geometricLetters >= nativeLetters * 0.85d;
    return geometricHasMoreCompleteWords || geometricHasBetterCoverage || repairsCollapsedBoundaries
      ? geometricWordText
      : nativeText;
  }

  internal static string ReconstructPdfText(IEnumerable<PdfWordPosition> sourceWords)
  {
    List<PdfWordPosition> words = sourceWords.Where(word => !string.IsNullOrWhiteSpace(word.Text)).ToList();
    if (words.Count == 0)
    {
      return string.Empty;
    }

    double medianHeight = GetMedianWordHeight(words);
    IReadOnlyList<PdfTextLine> lines = BuildPdfLines(words, medianHeight);

    StringBuilder result = new();
    PdfTextLine? previous = null;
    foreach (PdfTextLine line in lines.OrderByDescending(line => line.Baseline))
    {
      if (previous is not null)
      {
        double baselineGap = previous.Baseline - line.Baseline;
        result.AppendLine();
        if (baselineGap > medianHeight * 2.2d)
        {
          result.AppendLine();
        }
      }

      result.Append(string.Join(' ', line.Words.OrderBy(word => word.Left).Select(word => word.Text.Trim())));
      previous = line;
    }

    return result.ToString();
  }

  internal static IReadOnlyList<ReadableDocumentElement> ReconstructPdfElements(IEnumerable<PdfWordPosition> sourceWords)
  {
    List<PdfWordPosition> words = sourceWords.Where(word => !string.IsNullOrWhiteSpace(word.Text)).ToList();
    if (words.Count == 0)
    {
      return [];
    }

    double medianHeight = GetMedianWordHeight(words);
    IReadOnlyList<PdfTextLine> lines = BuildPdfLines(words, medianHeight);
    List<ReadableDocumentElement> elements = new(lines.Count);
    PdfTextLine? previous = null;
    foreach (PdfTextLine line in lines)
    {
      bool startsParagraph = previous is null || previous.Baseline - line.Baseline > medianHeight * 2.2d;
      string text = string.Join(' ', line.Words.OrderBy(word => word.Left).Select(word => word.Text.Trim()));
      elements.Add(new ReadableDocumentElement(text, IsLikelyPdfHeading(line, text), startsParagraph));
      previous = line;
    }
    return elements;
  }

  private static double GetMedianWordHeight(IReadOnlyList<PdfWordPosition> words)
    => words.Select(word => word.Height).Order().ElementAt(words.Count / 2);

  private static IReadOnlyList<PdfTextLine> BuildPdfLines(IReadOnlyList<PdfWordPosition> words, double medianHeight)
  {
    double baselineTolerance = Math.Max(1.5d, medianHeight * 0.45d);
    List<PdfTextLine> lines = [];
    foreach (PdfWordPosition word in words.OrderByDescending(word => word.Baseline).ThenBy(word => word.Left))
    {
      PdfTextLine? line = lines
        .Where(candidate => Math.Abs(candidate.Baseline - word.Baseline) <= baselineTolerance)
        .OrderBy(candidate => Math.Abs(candidate.Baseline - word.Baseline))
        .FirstOrDefault();
      if (line is null)
      {
        line = new PdfTextLine(word.Baseline);
        lines.Add(line);
      }
      line.Add(word);
    }
    return lines.OrderByDescending(line => line.Baseline).ToArray();
  }

  private static bool IsLikelyPdfHeading(PdfTextLine line, string text)
  {
    if (!IsPlausibleHeadingText(text))
    {
      return false;
    }

    int totalCharacters = line.Words.Sum(word => word.Text.Count(char.IsLetterOrDigit));
    int emphasizedCharacters = line.Words
      .Where(word => IsEmphasizedFont(word.FontName))
      .Sum(word => word.Text.Count(char.IsLetterOrDigit));
    return totalCharacters > 0 && emphasizedCharacters >= totalCharacters * 0.8d;
  }

  private static bool IsEmphasizedFont(string? fontName) => !string.IsNullOrWhiteSpace(fontName)
    && (fontName.Contains("Bold", StringComparison.OrdinalIgnoreCase)
      || fontName.Contains("SemiBold", StringComparison.OrdinalIgnoreCase)
      || fontName.Contains("Demi", StringComparison.OrdinalIgnoreCase)
      || fontName.Contains("Heavy", StringComparison.OrdinalIgnoreCase)
      || fontName.Contains("Black", StringComparison.OrdinalIgnoreCase));

  private static bool IsPlausibleHeadingText(string text)
  {
    string value = text.Trim();
    return value.Length is >= 2 and <= 160
      && value.Any(char.IsLetterOrDigit)
      && !value.EndsWith('.')
      && !value.EndsWith('!')
      && !value.EndsWith('?');
  }

  private static bool HasCollapsedWordBoundaries(string text)
  {
    int latinLetters = text.Count(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
    if (latinLetters < 32)
    {
      return false;
    }

    int whitespace = text.Count(char.IsWhiteSpace);
    return whitespace * 30 < latinLetters;
  }

  private static ReadableDocumentContent ExtractDocxDocument(string path, DocumentImportBudget budget)
  {
    using FileStream stream = budget.OpenFile(path);
    using ZipArchive archive = new(stream, ZipArchiveMode.Read);
    ZipArchiveEntry? entry = archive.GetEntry("word/document.xml");
    if (entry is null)
    {
      throw new InvalidOperationException("The DOCX file has no readable document body.");
    }

    XDocument document = budget.ReadXml(entry);
    XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    List<ReadableDocumentElement> elements = document.Descendants(word + "p")
      .Select(paragraph => { budget.CheckCancellation(); return CreateDocxElement(paragraph, word); })
      .Where(element => element is not null)
      .Select(element => element!)
      .ToList();
    string text = string.Join(Environment.NewLine + Environment.NewLine, elements.Select(element => element.Text));
    return ReadableDocumentContent.FromElements(text, elements);
  }

  private static ReadableDocumentElement? CreateDocxElement(XElement paragraph, XNamespace word)
  {
    string text = string.Concat(paragraph.Descendants(word + "t").Select(value => value.Value)).Trim();
    if (string.IsNullOrWhiteSpace(text))
    {
      return null;
    }

    XElement? properties = paragraph.Element(word + "pPr");
    string style = properties?.Element(word + "pStyle")?.Attribute(word + "val")?.Value ?? string.Empty;
    bool explicitHeading = style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
      || style.Equals("Title", StringComparison.OrdinalIgnoreCase)
      || properties?.Element(word + "outlineLvl") is not null;
    int totalCharacters = 0;
    int emphasizedCharacters = 0;
    foreach (XElement run in paragraph.Elements(word + "r"))
    {
      int runCharacters = string.Concat(run.Descendants(word + "t").Select(value => value.Value)).Count(char.IsLetterOrDigit);
      totalCharacters += runCharacters;
      XElement? bold = run.Element(word + "rPr")?.Element(word + "b");
      string? boldValue = bold?.Attribute(word + "val")?.Value;
      if (bold is not null && !string.Equals(boldValue, "0", StringComparison.OrdinalIgnoreCase)
          && !string.Equals(boldValue, "false", StringComparison.OrdinalIgnoreCase))
      {
        emphasizedCharacters += runCharacters;
      }
    }

    bool stronglyEmphasized = totalCharacters > 0 && emphasizedCharacters >= totalCharacters * 0.8d;
    return new ReadableDocumentElement(
      text,
      explicitHeading || stronglyEmphasized && IsPlausibleHeadingText(text),
      StartsParagraph: true);
  }

  private static string ExtractEpub(string path, DocumentImportBudget budget)
  {
    using FileStream stream = budget.OpenFile(path);
    using ZipArchive archive = new(stream, ZipArchiveMode.Read);
    ZipArchiveEntry? containerEntry = archive.GetEntry("META-INF/container.xml");
    if (containerEntry is null)
    {
      throw new InvalidOperationException("The EPUB package has no container definition.");
    }

    XDocument container = budget.ReadXml(containerEntry);
    string? packagePath = container.Descendants().FirstOrDefault(element => element.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value;
    if (string.IsNullOrWhiteSpace(packagePath))
    {
      throw new InvalidOperationException("The EPUB package has no readable book manifest.");
    }

    ZipArchiveEntry? packageEntry = archive.GetEntry(packagePath.Replace('\\', '/'));
    if (packageEntry is null)
    {
      throw new InvalidOperationException("The EPUB package manifest could not be opened.");
    }

    XDocument package = budget.ReadXml(packageEntry);
    string packageDirectory = Path.GetDirectoryName(packagePath)?.Replace('\\', '/') ?? string.Empty;
    Dictionary<string, string> manifest = package
      .Descendants()
      .Where(element => element.Name.LocalName == "item")
      .Select(element => new { Id = element.Attribute("id")?.Value, Href = element.Attribute("href")?.Value, MediaType = element.Attribute("media-type")?.Value })
      .Where(item => !string.IsNullOrWhiteSpace(item.Id)
        && !string.IsNullOrWhiteSpace(item.Href)
        && (string.Equals(item.MediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase)
          || string.Equals(item.MediaType, "text/html", StringComparison.OrdinalIgnoreCase)))
      .ToDictionary(item => item.Id!, item => item.Href!, StringComparer.Ordinal);

    List<string> chapters = [];
    foreach (string idRef in package.Descendants().Where(element => element.Name.LocalName == "itemref")
      .Select(element => element.Attribute("idref")?.Value)
      .Where(id => !string.IsNullOrWhiteSpace(id))
      .Select(id => id!))
    {
      if (!manifest.TryGetValue(idRef!, out string? href))
      {
        continue;
      }

      string entryPath = CombineArchivePath(packageDirectory, href);
      ZipArchiveEntry? chapterEntry = archive.GetEntry(entryPath);
      if (chapterEntry is null)
      {
        continue;
      }

      string chapter = ExtractHtml(budget.ReadEntry(chapterEntry)).Trim();
      budget.CheckCancellation();
      if (!string.IsNullOrWhiteSpace(chapter))
      {
        chapters.Add(chapter);
      }
    }

    if (chapters.Count == 0)
    {
      throw new InvalidOperationException("No readable book chapters were found in this EPUB.");
    }

    return string.Join(Environment.NewLine + Environment.NewLine, chapters);
  }

  private static string CombineArchivePath(string directory, string relativePath)
  {
    string normalizedRelative = relativePath.Split('#')[0].Replace('\\', '/');
    if (string.IsNullOrWhiteSpace(directory))
    {
      return normalizedRelative;
    }

    return string.Join('/', directory.Split('/', StringSplitOptions.RemoveEmptyEntries)
      .Concat(normalizedRelative.Split('/', StringSplitOptions.RemoveEmptyEntries))
      .Aggregate(new Stack<string>(), (segments, segment) =>
      {
        if (segment == ".." && segments.Count > 0) { segments.Pop(); }
        else if (segment != ".") { segments.Push(segment); }
        return segments;
      })
      .Reverse());
  }

  private static string ExtractHtml(string html)
  {
    string withoutScript = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(2));
    string spaced = Regex.Replace(withoutScript, "<[^>]+>", " ", RegexOptions.NonBacktracking, TimeSpan.FromSeconds(2));
    return System.Net.WebUtility.HtmlDecode(spaced);
  }

  private static string ExtractRtf(string rtf)
  {
    string withoutHexEscapes = Regex.Replace(rtf, @"\\'[0-9a-fA-F]{2}", " ", RegexOptions.NonBacktracking, TimeSpan.FromSeconds(2));
    string withoutControlWords = Regex.Replace(withoutHexEscapes, @"\\[a-zA-Z]+-?\d* ?", " ", RegexOptions.NonBacktracking, TimeSpan.FromSeconds(2));
    return withoutControlWords.Replace("{", " ", StringComparison.Ordinal).Replace("}", " ", StringComparison.Ordinal);
  }
}

internal sealed record DocumentImportProgress(string Phase, double Fraction, string Detail);

internal sealed record ReadableDocumentSection(string Title, string BodyText)
{
  public string FullText => string.IsNullOrWhiteSpace(BodyText)
    ? Title
    : string.Join(Environment.NewLine + Environment.NewLine, Title, BodyText);
}

internal sealed record ReadableDocumentContent(string Text, IReadOnlyList<ReadableDocumentSection> Sections)
{
  public static ReadableDocumentContent FromPlainText(string text) => new(text, []);

  public static ReadableDocumentContent FromElements(string text, IEnumerable<ReadableDocumentElement> sourceElements)
  {
    ReadableDocumentElement[] elements = sourceElements
      .Where(element => !string.IsNullOrWhiteSpace(element.Text))
      .ToArray();
    List<ReadableDocumentSection> sections = [];
    string? currentTitle = null;
    List<ReadableDocumentElement> currentBody = [];
    List<ReadableDocumentElement> preamble = [];

    foreach (ReadableDocumentElement element in elements)
    {
      if (!element.IsHeading)
      {
        (currentTitle is null ? preamble : currentBody).Add(element);
        continue;
      }

      if (currentTitle is not null)
      {
        sections.Add(new ReadableDocumentSection(currentTitle, ComposeBody(currentBody)));
        currentBody.Clear();
      }
      currentTitle = element.Text.Trim();
      if (preamble.Count > 0)
      {
        currentBody.AddRange(preamble);
        preamble.Clear();
      }
    }

    if (currentTitle is not null)
    {
      sections.Add(new ReadableDocumentSection(currentTitle, ComposeBody(currentBody)));
    }

    string readableText = sections.Count == 0
      ? text
      : string.Join(Environment.NewLine + Environment.NewLine, sections.Select(section => section.FullText));
    return new ReadableDocumentContent(readableText, sections);
  }

  private static string ComposeBody(IReadOnlyList<ReadableDocumentElement> elements)
  {
    StringBuilder result = new();
    foreach (ReadableDocumentElement element in elements)
    {
      if (result.Length > 0)
      {
        result.Append(element.StartsParagraph ? Environment.NewLine + Environment.NewLine : " ");
      }
      result.Append(element.Text.Trim());
    }
    return result.ToString();
  }
}

internal sealed record ReadableDocumentElement(string Text, bool IsHeading, bool StartsParagraph);

internal sealed record PdfPageExtraction(string Text, IReadOnlyList<ReadableDocumentElement> Elements);

internal sealed record PdfWordPosition(string Text, double Left, double Baseline, double Height, string? FontName = null);

internal sealed class PdfTextLine(double baseline)
{
  private double baselineTotal = baseline;

  public double Baseline => baselineTotal / Words.Count;

  public List<PdfWordPosition> Words { get; } = [];

  public void Add(PdfWordPosition word)
  {
    if (Words.Count > 0)
    {
      baselineTotal += word.Baseline;
    }
    Words.Add(word);
  }
}

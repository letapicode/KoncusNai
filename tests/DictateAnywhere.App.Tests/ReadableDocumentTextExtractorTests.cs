using System.IO.Compression;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class ReadableDocumentTextExtractorTests
{
  [Xunit.Theory]
  [Xunit.InlineData("", true)]
  [Xunit.InlineData("12", true)]
  [Xunit.InlineData("A searchable page with a damaged Sa\0ron glyph in its otherwise valid text layer.", true)]
  [Xunit.InlineData("This searchable PDF page already contains enough real text to use directly.", false)]
  [Xunit.InlineData("यो खोज्न मिल्ने पृष्ठमा OCR नगरी प्रयोग गर्न पर्याप्त पाठ पहिले नै छ।", false)]
  public void NeedsOcr_OnlySelectsPagesWithoutMeaningfulText(string text, bool expected)
  {
    Xunit.Assert.Equal(expected, ReadableDocumentTextExtractor.NeedsOcr(text));
  }

  [Xunit.Fact]
  public void Extract_ReadsPlainTextLocally()
  {
    using TempFileScope file = new(".txt");
    File.WriteAllText(file.Path, "Local text only.");

    Xunit.Assert.Equal("Local text only.", ReadableDocumentTextExtractor.Extract(file.Path));
  }

  [Xunit.Fact]
  public async Task ExtractStructuredAsync_RejectsImageOcrWhenTheLanguageDoesNotSupportIt()
  {
    using TempFileScope file = new(".png");
    await File.WriteAllBytesAsync(file.Path, [0x89, 0x50, 0x4E, 0x47]);
    await using TrackingOcrService ocrService = new();

    NotSupportedException exception = await Xunit.Assert.ThrowsAsync<NotSupportedException>(() =>
      ReadableDocumentTextExtractor.ExtractStructuredAsync(
        file.Path,
        "ta",
        ocrService,
        allowOcr: false));

    Xunit.Assert.Contains("OCR is not available", exception.Message, StringComparison.Ordinal);
    Xunit.Assert.False(ocrService.WasCalled);
  }

  [Xunit.Fact]
  public void SelectReadablePdfText_UsesGeometricWordsWhenNativeTextHasNoWordBoundaries()
  {
    string native = "TheCleverFoxandtheVainCrowlivedintheheartofthesunjungle.";
    string geometric = "The Clever Fox and the Vain Crow lived in the heart of the sun jungle.";

    Xunit.Assert.Equal(geometric, ReadableDocumentTextExtractor.SelectReadablePdfText(native, geometric));
  }

  [Xunit.Fact]
  public void SelectReadablePdfText_KeepsNativeTextWhenItAlreadyHasWordBoundaries()
  {
    string native = "The Clever Fox and the Vain Crow lived in the heart of the sun jungle.";
    string geometric = "The Clever Fox and the Vain Crow lived in the heart of the sun jungle.";

    Xunit.Assert.Equal(native, ReadableDocumentTextExtractor.SelectReadablePdfText(native, geometric));
  }

  [Xunit.Fact]
  public void SelectReadablePdfText_PrefersGeometricTextWhenNativeLayerOmitsWords()
  {
    string native = "The fox crossed the forest and rested beside the river.";
    string geometric = "The quick brown fox crossed the quiet forest and rested beside the silver river.";

    Xunit.Assert.Equal(geometric, ReadableDocumentTextExtractor.SelectReadablePdfText(native, geometric));
  }

  [Xunit.Fact]
  public void SelectReadablePdfText_PrefersGeometricTextForASmallMissingWord()
  {
    string native = "A traveler stopped beside the river and listened to the evening birds.";
    string geometric = "A tired traveler stopped beside the river and listened to the evening birds.";

    Xunit.Assert.Equal(geometric, ReadableDocumentTextExtractor.SelectReadablePdfText(native, geometric));
  }

  [Xunit.Fact]
  public void ReconstructPdfText_PreservesLinesAndParagraphGaps()
  {
    PdfWordPosition[] words =
    [
      new("Story", 10, 100, 10), new("Title", 50, 100.4, 10),
      new("First", 10, 82, 10), new("line", 45, 82.2, 10),
      new("New", 10, 55, 10), new("paragraph", 38, 55.1, 10),
    ];

    string result = ReadableDocumentTextExtractor.ReconstructPdfText(words);

    Xunit.Assert.Equal($"Story Title{Environment.NewLine}First line{Environment.NewLine}{Environment.NewLine}New paragraph", result);
  }

  [Xunit.Theory]
  [Xunit.InlineData("book-file.pdf", "THE MOONLIT PATH\n\nBy An Author\n\nOnce upon a time.", "THE MOONLIT PATH")]
  [Xunit.InlineData("scan.pdf", "1\nCopyright 2025\n\nA Small Beginning\nThe story starts here.", "A Small Beginning")]
  [Xunit.InlineData("fallback-name.pdf", "", "fallback-name")]
  public void TitleResolver_UsesTheFirstMeaningfulContentLine(string path, string text, string expected)
  {
    Xunit.Assert.Equal(expected, ReadableDocumentTitleResolver.Resolve(path, text));
  }

  [Xunit.Fact]
  public void Extract_ReadsWordDocumentParagraphs()
  {
    using TempFileScope file = new(".docx");
    using (FileStream stream = new(file.Path, FileMode.Create))
    using (ZipArchive archive = new(stream, ZipArchiveMode.Create))
    using (StreamWriter writer = new(archive.CreateEntry("word/document.xml").Open()))
    {
      writer.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>First paragraph.</w:t></w:r></w:p><w:p><w:r><w:t>Second paragraph.</w:t></w:r></w:p></w:body></w:document>");
    }

    string result = ReadableDocumentTextExtractor.Extract(file.Path);

    Xunit.Assert.Equal($"First paragraph.{Environment.NewLine}{Environment.NewLine}Second paragraph.", result);
  }

  [Xunit.Fact]
  public void ExtractStructured_UsesStronglyEmphasizedDocxParagraphsAsStorySections()
  {
    using TempFileScope file = new(".docx");
    using (FileStream stream = new(file.Path, FileMode.Create))
    using (ZipArchive archive = new(stream, ZipArchiveMode.Create))
    using (StreamWriter writer = new(archive.CreateEntry("word/document.xml").Open()))
    {
      writer.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>"
        + "<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>The First Story</w:t></w:r></w:p>"
        + "<w:p><w:r><w:t>Its first paragraph.</w:t></w:r></w:p>"
        + "<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>The Second Story</w:t></w:r></w:p>"
        + "<w:p><w:r><w:t>Its second paragraph.</w:t></w:r></w:p>"
        + "</w:body></w:document>");
    }

    ReadableDocumentContent result = ReadableDocumentTextExtractor.ExtractStructured(file.Path);

    Xunit.Assert.Equal(2, result.Sections.Count);
    Xunit.Assert.Equal("The First Story", result.Sections[0].Title);
    Xunit.Assert.Equal("Its first paragraph.", result.Sections[0].BodyText);
    Xunit.Assert.Equal("The Second Story", result.Sections[1].Title);
  }

  [Xunit.Fact]
  public void ReconstructPdfElements_UsesBoldFontMetadataAsAStoryHeading()
  {
    PdfWordPosition[] words =
    [
      new("The", 10, 100, 12, "Lexend-Bold"),
      new("Moon's", 40, 100, 12, "Lexend-Bold"),
      new("Reﬂection", 95, 100, 12, "Lexend-Bold"),
      new("Long", 10, 80, 12, "Lexend-Regular"),
      new("ago", 45, 80, 12, "Lexend-Regular"),
    ];

    IReadOnlyList<ReadableDocumentElement> elements = ReadableDocumentTextExtractor.ReconstructPdfElements(words);

    Xunit.Assert.True(elements[0].IsHeading);
    Xunit.Assert.False(elements[1].IsHeading);
  }

  [Xunit.Fact]
  public void RepairUnreadablePdfElements_UsesOcrForOnlyTheDamagedLineAndKeepsHeadingMetadata()
  {
    ReadableDocumentElement[] elements =
    [
      new("The Jackal and the Sa\0ron Robe", IsHeading: true, StartsParagraph: true),
      new("The body remains native searchable text.", IsHeading: false, StartsParagraph: true),
    ];

    IReadOnlyList<ReadableDocumentElement> repaired = ReadableDocumentTextExtractor.RepairUnreadablePdfElements(
      elements,
      "The Jackal and the Saffron Robe\nThe body remains native searchable text.");

    Xunit.Assert.Equal("The Jackal and the Saffron Robe", repaired[0].Text);
    Xunit.Assert.True(repaired[0].IsHeading);
    Xunit.Assert.Same(elements[1], repaired[1]);
  }

  [Xunit.Fact]
  public void Extract_ReadsEpubSpineInBookOrder()
  {
    using TempFileScope file = new(".epub");
    using (FileStream stream = new(file.Path, FileMode.Create))
    using (ZipArchive archive = new(stream, ZipArchiveMode.Create))
    {
      WriteEntry(archive, "META-INF/container.xml", "<container><rootfiles><rootfile full-path=\"OEBPS/content.opf\" /></rootfiles></container>");
      WriteEntry(archive, "OEBPS/content.opf", "<package><manifest><item id=\"first\" href=\"Text/one.xhtml\" media-type=\"application/xhtml+xml\" /><item id=\"second\" href=\"Text/two.xhtml\" media-type=\"application/xhtml+xml\" /></manifest><spine><itemref idref=\"first\" /><itemref idref=\"second\" /></spine></package>");
      WriteEntry(archive, "OEBPS/Text/one.xhtml", "<html><body><h1>First chapter</h1><p>Opening words.</p></body></html>");
      WriteEntry(archive, "OEBPS/Text/two.xhtml", "<html><body><p>Second chapter.</p></body></html>");
    }

    string result = ReadableDocumentTextExtractor.Extract(file.Path);

    Xunit.Assert.Contains("First chapter", result);
    Xunit.Assert.Contains("Opening words.", result);
    Xunit.Assert.Contains("Second chapter.", result);
    Xunit.Assert.True(result.IndexOf("First chapter", StringComparison.Ordinal) < result.IndexOf("Second chapter.", StringComparison.Ordinal));
  }

  private static void WriteEntry(ZipArchive archive, string path, string content)
  {
    using StreamWriter writer = new(archive.CreateEntry(path).Open());
    writer.Write(content);
  }

  private sealed class TempFileScope : IDisposable
  {
    public TempFileScope(string extension) => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"DictateAnywhere-{Guid.NewGuid():N}{extension}");

    public string Path { get; }

    public void Dispose()
    {
      if (File.Exists(Path))
      {
        File.Delete(Path);
      }
    }
  }

  private sealed class TrackingOcrService : IDocumentOcrService
  {
    public bool WasCalled { get; private set; }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task<DocumentOcrResult> RecognizeAsync(
      DocumentOcrRequest request,
      CancellationToken cancellationToken = default)
    {
      WasCalled = true;
      return Task.FromResult(new DocumentOcrResult([], "test"));
    }
  }
}

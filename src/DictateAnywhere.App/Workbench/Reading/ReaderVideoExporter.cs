using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>Renders a stable-layout, precisely timed reading video and encodes it as a high-quality MP4.</summary>
internal static class ReaderVideoExporter
{
  private const int FrameWidth = 2560;
  private const int FrameHeight = 1440;
  private const double TextLeft = 300d;
  private const double TextTop = 170d;
  private const double TextWidth = 1960d;
  private const double TextHeight = 1100d;
  private const double BaseLandscapeFontSize = 52d;
  private const int ShortFrameWidth = 1080;
  private const int ShortFrameHeight = 1920;
  private const double ShortTextWidth = 880d;
  private const double BaseShortFontSize = 92d;

  public static async Task ExportAsync(
    ReaderVideoExportRequest request,
    IProgress<ReaderVideoExportProgress>? progress = null,
    CancellationToken cancellationToken = default)
  {
    ReaderVideoExportRequest normalized = request.Normalize();
    string ffmpegPath = await ReaderFfmpegRuntime.EnsureAvailableAsync(progress, cancellationToken).ConfigureAwait(true);
    string workDirectory = Path.Combine(Path.GetTempPath(), $"notype-video-{Guid.NewGuid():N}");
    Directory.CreateDirectory(workDirectory);
    try
    {
      string audioPath = Path.Combine(workDirectory, "audiobook.wav");
      _ = await ReaderAudioExporter.ExportAsync(
        normalized.Sections.Select(section => section.AudioPath).ToArray(),
        audioPath,
        cancellationToken).ConfigureAwait(true);

      ReaderVideoTimeline timeline = BuildTimeline(normalized);
      IReadOnlyList<ReaderVideoPage> pages = Paginate(normalized, timeline.Words);
      IReadOnlyList<ReaderVideoCue> cues = BuildCues(normalized, timeline, pages);
      if (cues.Count == 0)
      {
        throw new InvalidOperationException("The selected reading has no timed video frames.");
      }

      List<string> framePaths = new(cues.Count);
      for (int index = 0; index < cues.Count; index++)
      {
        cancellationToken.ThrowIfCancellationRequested();
        string framePath = Path.Combine(workDirectory, $"frame-{index:D7}.png");
        RenderFrame(normalized, pages[cues[index].PageIndex], cues[index], framePath);
        framePaths.Add(framePath);
        progress?.Report(new ReaderVideoExportProgress(
          ReaderVideoExportPhase.RenderingFrames,
          (double)(index + 1) / cues.Count,
          $"Rendered {index + 1:N0} of {cues.Count:N0} synchronized moments."));
        if ((index & 3) == 3)
        {
          await Task.Yield();
        }
      }

      string concatPath = Path.Combine(workDirectory, "frames.txt");
      WriteConcatManifest(concatPath, framePaths, cues);
      await EncodeAsync(
        ffmpegPath,
        concatPath,
        audioPath,
        normalized.OutputPath,
        timeline.Duration,
        progress,
        cancellationToken).ConfigureAwait(true);
    }
    catch
    {
      TryDeleteFile(normalized.OutputPath);
      throw;
    }
    finally
    {
      TryDeleteDirectory(workDirectory);
    }
  }

  internal static ReaderVideoTimeline BuildTimeline(ReaderVideoExportRequest request)
  {
    List<ReaderVideoWord> words = [];
    TimeSpan sectionOffset = TimeSpan.Zero;
    int globalOffset = 0;
    for (int sectionIndex = 0; sectionIndex < request.Sections.Count; sectionIndex++)
    {
      ReaderVideoExportSection section = request.Sections[sectionIndex];
      int paragraphIndex = 0;
      for (int localIndex = 0; localIndex < section.Words.Count; localIndex++)
      {
        bool paragraphStart = localIndex == 0 || section.ParagraphStartWordIndices.Contains(localIndex);
        if (localIndex > 0 && paragraphStart)
        {
          paragraphIndex++;
        }

        (int SentenceStart, int SentenceEnd) sentence = ReadingPlaybackTiming.GetSentenceRange(section.Words, localIndex);
        ReaderTimedWord timing = section.Timings[localIndex];
        words.Add(new ReaderVideoWord(
          globalOffset + localIndex,
          section.Words[localIndex],
          sectionOffset + timing.Start,
          sectionOffset + timing.End,
          globalOffset + sentence.SentenceStart,
          globalOffset + sentence.SentenceEnd,
          paragraphStart,
          section.GetParagraphDirection(paragraphIndex)));
      }

      globalOffset += section.Words.Count;
      sectionOffset += section.Duration;
    }

    return new ReaderVideoTimeline(words, sectionOffset);
  }

  internal static IReadOnlyList<ReaderVideoPage> Paginate(ReaderVideoExportRequest request, IReadOnlyList<ReaderVideoWord> words)
  {
    if (request.Format == ReaderVideoFormat.YouTubeShort)
    {
      return BuildKineticCaptionPages(words);
    }

    List<ReaderVideoPage> pages = [];
    int start = 0;
    while (start < words.Count)
    {
      int low = start + 1;
      int high = GetDirectionRunEnd(words, start);
      int best = low;
      while (low <= high)
      {
        int candidate = low + ((high - low) / 2);
        ReaderVideoPage page = BuildPage(words, start, candidate);
        FormattedText formatted = CreateFormattedText(request, page);
        if (formatted.Height <= TextHeight)
        {
          best = candidate;
          low = candidate + 1;
        }
        else
        {
          high = candidate - 1;
        }
      }

      pages.Add(BuildPage(words, start, best));
      start = best;
    }

    return pages;
  }

  private static IReadOnlyList<ReaderVideoPage> BuildKineticCaptionPages(IReadOnlyList<ReaderVideoWord> words)
  {
    List<ReaderVideoPage> pages = [];
    int start = 0;
    while (start < words.Count)
    {
      int sentenceEnd = Math.Clamp(words[start].SentenceEnd + 1, start + 1, words.Count);
      int end = Math.Min(Math.Min(sentenceEnd, start + 7), GetDirectionRunEnd(words, start));
      pages.Add(BuildPage(words, start, end));
      start = end;
    }

    return pages;
  }

  internal static IReadOnlyList<ReaderVideoCue> BuildCues(
    ReaderVideoExportRequest request,
    ReaderVideoTimeline timeline,
    IReadOnlyList<ReaderVideoPage> pages)
  {
    List<ReaderVideoCue> cues = [];
    if (timeline.Words.Count == 0 || timeline.Duration <= TimeSpan.Zero)
    {
      return cues;
    }

    if (timeline.Words[0].Start > TimeSpan.FromMilliseconds(5))
    {
      cues.Add(new ReaderVideoCue(0, -1, -1, TimeSpan.Zero, timeline.Words[0].Start));
    }

    for (int index = 0; index < timeline.Words.Count; index++)
    {
      ReaderVideoWord word = timeline.Words[index];
      TimeSpan start = word.Start;
      TimeSpan end = index + 1 < timeline.Words.Count ? timeline.Words[index + 1].Start : timeline.Duration;
      if (end <= start)
      {
        continue;
      }

      int highlightStart = request.HighlightMode == ReadingHighlightMode.Sentence ? word.SentenceStart : word.GlobalIndex;
      int highlightEnd = request.HighlightMode == ReadingHighlightMode.Sentence ? word.SentenceEnd : word.GlobalIndex;
      int pageIndex = FindPage(pages, word.GlobalIndex);
      if (cues.Count > 0)
      {
        ReaderVideoCue previous = cues[^1];
        if (previous.PageIndex == pageIndex
            && previous.HighlightStart == highlightStart
            && previous.HighlightEnd == highlightEnd
            && previous.End == start)
        {
          cues[^1] = previous with { End = end };
          continue;
        }
      }

      cues.Add(new ReaderVideoCue(pageIndex, highlightStart, highlightEnd, start, end));
    }

    return cues;
  }

  private static ReaderVideoPage BuildPage(IReadOnlyList<ReaderVideoWord> words, int start, int endExclusive)
  {
    StringBuilder text = new();
    List<ReaderVideoWordSpan> spans = [];
    for (int index = start; index < endExclusive; index++)
    {
      ReaderVideoWord word = words[index];
      if (text.Length > 0)
      {
        if (word.ParagraphStart)
        {
          text.AppendLine();
          text.AppendLine();
        }
        else if (ReadingTextLayout.ShouldInsertSpace(words[index - 1].Text, word.Text))
        {
          text.Append(' ');
        }
      }

      int characterStart = text.Length;
      text.Append(word.Text);
      spans.Add(new ReaderVideoWordSpan(word.GlobalIndex, characterStart, word.Text.Length));
    }

    return new ReaderVideoPage(start, endExclusive, text.ToString(), spans, words[start].Direction);
  }

  internal static FormattedText CreateFormattedText(ReaderVideoExportRequest request, ReaderVideoPage page)
  {
    double fontSize = GetLandscapeFontSize(request);
    ReaderTypographyMetrics metrics = ReaderTypographyCatalog.GetMetrics(request.LineMetricsProfile);
    FormattedText formatted = new(
      page.Text,
      CultureInfo.CurrentUICulture,
      ToFlowDirection(page.Direction),
      new Typeface(request.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
      fontSize,
      CreateBrush(request.Theme.InkColor),
      1d)
    {
      MaxTextWidth = TextWidth,
      LineHeight = fontSize * metrics.LandscapeVideoLineHeightScale,
      TextAlignment = page.Direction == ReaderTextDirection.RightToLeft
        ? TextAlignment.Right
        : TextAlignment.Left,
      Trimming = TextTrimming.None,
    };
    return formatted;
  }

  internal static void RenderFrame(
    ReaderVideoExportRequest request,
    ReaderVideoPage page,
    ReaderVideoCue cue,
    string outputPath)
  {
    if (request.Format == ReaderVideoFormat.YouTubeShort)
    {
      RenderShortFrame(request, page, cue, outputPath);
      return;
    }

    DrawingVisual visual = new();
    using (DrawingContext drawing = visual.RenderOpen())
    {
      drawing.DrawRectangle(CreateBrush(request.Theme.PageColor), null, new Rect(0, 0, FrameWidth, FrameHeight));
      FormattedText formatted = CreateFormattedText(request, page);
      Point origin = new(TextLeft, TextTop);
      ReaderHighlightPlan? highlightPlan = null;
      IReadOnlyList<Rect> highlightBands = [];
      if (cue.HighlightStart >= 0)
      {
        highlightPlan = CreateHighlightPlan(request);
        highlightBands = BuildStableHighlightBands(formatted, page, cue.HighlightStart, cue.HighlightEnd, origin);
        if (highlightPlan.Surface != ReaderHighlightSurfaceTreatment.None)
        {
          Color accentColor = (Color)ColorConverter.ConvertFromString(request.HighlightColor);
          foreach (Rect band in highlightBands)
          {
            double radius = request.HighlightMode == ReadingHighlightMode.Sentence ? 12d : 10d;
            (Brush? fill, Pen? pen) = CreateHighlightSurface(highlightPlan.Surface, accentColor, outlineWidth: 4d);
            drawing.DrawRoundedRectangle(fill, pen, band, radius, radius);
          }
        }
      }

      if (highlightPlan?.DimUnhighlightedWords is true)
      {
        formatted.SetForegroundBrush(CreateTranslucentBrush(request.Theme.InkColor, 100));
      }
      drawing.DrawText(formatted, origin);
      if (highlightPlan is not null && NeedsTextOverlay(highlightPlan) && highlightBands.Count > 0)
      {
        FormattedText overlay = CreateFormattedText(request, page);
        ApplyTextHighlightStyle(overlay, page, cue, request, highlightPlan);
        drawing.PushClip(CreateHighlightClip(highlightBands));
        drawing.DrawText(overlay, origin);
        drawing.Pop();
      }
    }

    RenderTargetBitmap bitmap = new(FrameWidth, FrameHeight, 96d, 96d, PixelFormats.Pbgra32);
    bitmap.Render(visual);
    bitmap.Freeze();
    PngBitmapEncoder encoder = new();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using FileStream stream = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
    encoder.Save(stream);
  }

  private static void ApplyTextHighlightStyle(
    FormattedText formatted,
    ReaderVideoPage page,
    ReaderVideoCue cue,
    ReaderVideoExportRequest request,
    ReaderHighlightPlan plan)
  {
    formatted.SetForegroundBrush(Brushes.Transparent);
    foreach (ReaderVideoWordSpan span in page.WordSpans.Where(span => span.GlobalIndex >= cue.HighlightStart && span.GlobalIndex <= cue.HighlightEnd))
    {
      formatted.SetForegroundBrush(
        ResolveHighlightTextBrush(plan.TextTone, request),
        span.CharacterStart,
        span.CharacterLength);
      switch (plan.TextEmphasis)
      {
        case ReaderHighlightTextEmphasis.Normal:
          break;
        case ReaderHighlightTextEmphasis.Underline:
          formatted.SetTextDecorations(TextDecorations.Underline, span.CharacterStart, span.CharacterLength);
          break;
        case ReaderHighlightTextEmphasis.Bold:
          formatted.SetFontWeight(FontWeights.Bold, span.CharacterStart, span.CharacterLength);
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(plan), plan.TextEmphasis, "Unknown reader highlight text emphasis.");
      }
    }
  }

  private static bool NeedsTextOverlay(ReaderHighlightPlan plan) => plan.TextTone != ReaderHighlightTextTone.Base
    || plan.TextEmphasis != ReaderHighlightTextEmphasis.Normal
    || plan.DimUnhighlightedWords;

  private static Geometry CreateHighlightClip(IReadOnlyList<Rect> bands)
  {
    GeometryGroup clip = new();
    foreach (Rect band in bands)
    {
      clip.Children.Add(new RectangleGeometry(band));
    }

    clip.Freeze();
    return clip;
  }

  private static ReaderHighlightPlan CreateHighlightPlan(ReaderVideoExportRequest request)
  {
    ReaderTypographyMetrics metrics = ReaderTypographyCatalog.GetMetrics(request.LineMetricsProfile);
    return ReaderHighlightPlan.Resolve(
      request.HighlightStyle,
      request.HighlightMode,
      metrics.PreferWeightOverUnderline);
  }

  private static Brush ResolveHighlightTextBrush(
    ReaderHighlightTextTone tone,
    ReaderVideoExportRequest request) => tone switch
    {
      ReaderHighlightTextTone.Base => CreateBrush(request.Theme.InkColor),
      ReaderHighlightTextTone.Accent => CreateBrush(request.HighlightColor),
      ReaderHighlightTextTone.Contrast => new SolidColorBrush(Color.FromRgb(15, 23, 42)),
      _ => throw new ArgumentOutOfRangeException(nameof(tone), tone, "Unknown reader highlight text tone."),
    };

  private static (Brush? Fill, Pen? Pen) CreateHighlightSurface(
    ReaderHighlightSurfaceTreatment treatment,
    Color accent,
    double outlineWidth) => treatment switch
    {
      ReaderHighlightSurfaceTreatment.None => (null, null),
      ReaderHighlightSurfaceTreatment.SoftAccent => (
        new SolidColorBrush(Color.FromArgb(96, accent.R, accent.G, accent.B)),
        null),
      ReaderHighlightSurfaceTreatment.SolidAccent => (new SolidColorBrush(accent), null),
      ReaderHighlightSurfaceTreatment.AccentOutline => (
        null,
        new Pen(new SolidColorBrush(Color.FromArgb(230, accent.R, accent.G, accent.B)), outlineWidth)),
      _ => throw new ArgumentOutOfRangeException(nameof(treatment), treatment, "Unknown reader highlight surface treatment."),
    };

  private static void RenderShortFrame(
    ReaderVideoExportRequest request,
    ReaderVideoPage page,
    ReaderVideoCue cue,
    string outputPath)
  {
    DrawingVisual visual = new();
    using (DrawingContext drawing = visual.RenderOpen())
    {
      Color background = (Color)ColorConverter.ConvertFromString(request.Theme.PageColor);
      drawing.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, ShortFrameWidth, ShortFrameHeight));

      // A restrained vignette keeps the caption readable on OLED-black themes
      // without adding a watermark, title card, or visual clutter.
      RadialGradientBrush vignette = new(
        Color.FromArgb(26, 255, 255, 255),
        Color.FromArgb(0, 255, 255, 255))
      {
        RadiusX = 0.72,
        RadiusY = 0.42,
        Center = new Point(0.5, 0.5),
        GradientOrigin = new Point(0.5, 0.5),
      };
      drawing.DrawRectangle(vignette, null, new Rect(0, 0, ShortFrameWidth, ShortFrameHeight));

      FormattedText caption = CreateShortFormattedText(request, page, cue);
      Point origin = new(
        (ShortFrameWidth - caption.Width) / 2d,
        (ShortFrameHeight - caption.Height) / 2d);

      ReaderHighlightPlan highlightPlan = CreateHighlightPlan(request);
      Geometry? highlightGeometry = CreateShortHighlightGeometry(caption, page, cue, origin);
      if (highlightGeometry is not null && request.CaptionStyle == ReaderVideoCaptionStyle.FocusPill)
      {
        Rect pill = highlightGeometry.Bounds;
        pill.Inflate(18d, 12d);
        Color accent = (Color)ColorConverter.ConvertFromString(request.HighlightColor);
        drawing.DrawRoundedRectangle(
          new SolidColorBrush(Color.FromArgb(70, accent.R, accent.G, accent.B)),
          new Pen(new SolidColorBrush(Color.FromArgb(210, accent.R, accent.G, accent.B)), 3d),
          pill,
          18d,
          18d);
      }
      else if (highlightGeometry is not null
        && highlightPlan.Surface != ReaderHighlightSurfaceTreatment.None)
      {
        Color accent = (Color)ColorConverter.ConvertFromString(request.HighlightColor);
        (Brush? fill, Pen? pen) = CreateHighlightSurface(highlightPlan.Surface, accent, outlineWidth: 3d);
        drawing.DrawGeometry(fill, pen, highlightGeometry);
      }

      FormattedText shadow = CreateShortFormattedText(request, page, cue, shadow: true);
      drawing.DrawText(shadow, new Point(origin.X + 4d, origin.Y + 7d));
      drawing.DrawText(caption, origin);
      if (highlightGeometry is not null && NeedsTextOverlay(highlightPlan))
      {
        FormattedText overlay = CreateShortFormattedText(request, page, cue, highlightOverlay: true);
        drawing.PushClip(CreatePaddedClip(highlightGeometry, horizontalPadding: 10d, verticalPadding: 6d));
        drawing.DrawText(overlay, origin);
        drawing.Pop();
      }
    }

    RenderTargetBitmap bitmap = new(ShortFrameWidth, ShortFrameHeight, 96d, 96d, PixelFormats.Pbgra32);
    bitmap.Render(visual);
    bitmap.Freeze();
    PngBitmapEncoder encoder = new();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using FileStream stream = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
    encoder.Save(stream);
  }

  internal static FormattedText CreateShortFormattedText(
    ReaderVideoExportRequest request,
    ReaderVideoPage page,
    ReaderVideoCue cue,
    bool shadow = false,
    bool highlightOverlay = false)
  {
    ReaderHighlightPlan highlightPlan = CreateHighlightPlan(request);
    Brush baseBrush = shadow
      ? new SolidColorBrush(Color.FromArgb(155, 0, 0, 0))
      : !highlightOverlay && highlightPlan.DimUnhighlightedWords
        ? CreateTranslucentBrush(request.Theme.InkColor, 100)
        : CreateBrush(request.Theme.InkColor);
    double fontSize = GetShortFontSize(request);
    ReaderTypographyMetrics metrics = ReaderTypographyCatalog.GetMetrics(request.LineMetricsProfile);
    FormattedText formatted = new(
      page.Text,
      CultureInfo.CurrentUICulture,
      ToFlowDirection(page.Direction),
      new Typeface(request.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
      fontSize,
      baseBrush,
      1d)
    {
      MaxTextWidth = ShortTextWidth,
      LineHeight = fontSize * metrics.ShortVideoLineHeightScale,
      TextAlignment = TextAlignment.Center,
      Trimming = TextTrimming.None,
    };

    if (highlightOverlay)
    {
      ReaderHighlightPlan overlayPlan = request.CaptionStyle == ReaderVideoCaptionStyle.FocusPill
        && highlightPlan.TextTone == ReaderHighlightTextTone.Contrast
          ? highlightPlan with { TextTone = ReaderHighlightTextTone.Accent }
          : highlightPlan;
      ApplyTextHighlightStyle(formatted, page, cue, request, overlayPlan);
    }

    return formatted;
  }

  internal static Geometry? CreateShortHighlightGeometry(
    FormattedText caption,
    ReaderVideoPage page,
    ReaderVideoCue cue,
    Point origin)
  {
    ReaderVideoWordSpan[] highlightedSpans = page.WordSpans
      .Where(span => span.GlobalIndex >= cue.HighlightStart && span.GlobalIndex <= cue.HighlightEnd)
      .ToArray();
    if (highlightedSpans.Length == 0)
    {
      return null;
    }

    int characterStart = highlightedSpans[0].CharacterStart;
    ReaderVideoWordSpan last = highlightedSpans[^1];
    int characterLength = (last.CharacterStart + last.CharacterLength) - characterStart;
    Geometry? geometry = caption.BuildHighlightGeometry(origin, characterStart, characterLength);
    return geometry is null || geometry.Bounds.IsEmpty ? null : geometry;
  }

  private static Geometry CreatePaddedClip(Geometry geometry, double horizontalPadding, double verticalPadding)
  {
    Rect bounds = geometry.Bounds;
    bounds.Inflate(horizontalPadding, verticalPadding);
    return new RectangleGeometry(bounds);
  }

  private static SolidColorBrush CreateTranslucentBrush(string hex, byte alpha)
  {
    Color color = (Color)ColorConverter.ConvertFromString(hex);
    return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
  }

  private static int GetDirectionRunEnd(IReadOnlyList<ReaderVideoWord> words, int start)
  {
    ReaderTextDirection direction = words[start].Direction;
    int end = start + 1;
    while (end < words.Count && words[end].Direction == direction)
    {
      end++;
    }

    return end;
  }

  private static FlowDirection ToFlowDirection(ReaderTextDirection direction)
    => direction == ReaderTextDirection.RightToLeft
      ? FlowDirection.RightToLeft
      : FlowDirection.LeftToRight;

  internal static (int Width, int Height) GetOutputSize(ReaderVideoExportRequest request) =>
    request.Format == ReaderVideoFormat.YouTubeShort
      ? (ShortFrameWidth, ShortFrameHeight)
      : (FrameWidth, FrameHeight);

  internal static double GetLandscapeFontSize(ReaderVideoExportRequest request) =>
    Math.Clamp(BaseLandscapeFontSize * request.ReaderFontSize / 23d, 40d, 82d);

  internal static double GetShortFontSize(ReaderVideoExportRequest request) =>
    Math.Clamp(BaseShortFontSize * request.ReaderFontSize / 23d, 72d, 132d);

  internal static IReadOnlyList<Rect> BuildStableHighlightBands(
    FormattedText formatted,
    ReaderVideoPage page,
    int highlightStart,
    int highlightEnd,
    Point origin)
  {
    List<(int Line, Rect Bounds)> wordBounds = [];
    foreach (ReaderVideoWordSpan span in page.WordSpans.Where(span => span.GlobalIndex >= highlightStart && span.GlobalIndex <= highlightEnd))
    {
      Geometry? geometry = formatted.BuildHighlightGeometry(origin, span.CharacterStart, span.CharacterLength);
      if (geometry is null || geometry.Bounds.IsEmpty)
      {
        continue;
      }

      Rect bounds = geometry.Bounds;
      double lineHeight = formatted.LineHeight;
      int line = Math.Max(0, (int)Math.Round((bounds.Top - origin.Y) / lineHeight));
      wordBounds.Add((line, bounds));
    }

    List<Rect> bands = [];
    foreach (IGrouping<int, (int Line, Rect Bounds)> line in wordBounds.GroupBy(item => item.Line).OrderBy(group => group.Key))
    {
      // Highlight cells can be narrower than the glyph overhang for joined and combining scripts.
      // A stable full-line band keeps every paint-only change inside the isolated highlight layer.
      double left = line.Min(item => item.Bounds.Left) - 18d;
      double right = line.Max(item => item.Bounds.Right) + 18d;
      double lineHeight = formatted.LineHeight;
      double top = origin.Y + (line.Key * lineHeight);
      bands.Add(new Rect(left, top, Math.Max(8d, right - left), lineHeight));
    }

    return bands;
  }

  private static int FindPage(IReadOnlyList<ReaderVideoPage> pages, int wordIndex)
  {
    for (int index = 0; index < pages.Count; index++)
    {
      if (wordIndex >= pages[index].Start && wordIndex < pages[index].EndExclusive)
      {
        return index;
      }
    }

    return Math.Max(0, pages.Count - 1);
  }

  private static void WriteConcatManifest(string path, IReadOnlyList<string> framePaths, IReadOnlyList<ReaderVideoCue> cues)
  {
    using StreamWriter writer = new(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    for (int index = 0; index < framePaths.Count; index++)
    {
      writer.WriteLine($"file '{EscapeConcatPath(framePaths[index])}'");
      writer.WriteLine($"duration {Math.Max(0.01d, cues[index].Duration.TotalSeconds).ToString("0.0000000", CultureInfo.InvariantCulture)}");
    }

    writer.WriteLine($"file '{EscapeConcatPath(framePaths[^1])}'");
  }

  private static string EscapeConcatPath(string path) => path.Replace('\\', '/').Replace("'", "'\\''", StringComparison.Ordinal);

  private static async Task EncodeAsync(
    string ffmpegPath,
    string manifestPath,
    string audioPath,
    string outputPath,
    TimeSpan duration,
    IProgress<ReaderVideoExportProgress>? progress,
    CancellationToken cancellationToken)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException("Video output path has no directory."));
    ProcessStartInfo startInfo = new(ffmpegPath)
    {
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };
    string[] arguments =
    [
      "-hide_banner", "-y",
      "-f", "concat", "-safe", "0", "-i", manifestPath,
      "-i", audioPath,
      "-map", "0:v:0", "-map", "1:a:0",
      "-c:v", "libx264", "-preset", "slow", "-crf", "16",
      "-pix_fmt", "yuv420p", "-fps_mode", "vfr",
      "-video_track_timescale", "90000",
      "-c:a", "aac", "-b:a", "320k",
      "-movflags", "+faststart", "-shortest",
      "-progress", "pipe:1", "-nostats",
      outputPath,
    ];
    foreach (string argument in arguments)
    {
      startInfo.ArgumentList.Add(argument);
    }

    using Process process = new() { StartInfo = startInfo };
    if (!process.Start())
    {
      throw new InvalidOperationException("The high-quality video encoder could not be started.");
    }

    using CancellationTokenRegistration registration = cancellationToken.Register(() =>
    {
      try
      {
        if (!process.HasExited)
        {
          process.Kill(entireProcessTree: true);
        }
      }
      catch (InvalidOperationException)
      {
        // The encoder completed while cancellation was being delivered.
      }
    });
    Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
    while (await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
    {
      if (line.StartsWith("out_time_us=", StringComparison.Ordinal)
          && long.TryParse(line.AsSpan("out_time_us=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out long microseconds))
      {
        double fraction = duration > TimeSpan.Zero
          ? Math.Clamp(microseconds / 1_000_000d / duration.TotalSeconds, 0d, 0.995d)
          : 0d;
        progress?.Report(new ReaderVideoExportProgress(ReaderVideoExportPhase.Encoding, fraction, "Compressing the finished pages and lossless source audio."));
      }
    }

    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    string error = await errorTask.ConfigureAwait(false);
    if (process.ExitCode != 0)
    {
      throw new InvalidOperationException($"The video encoder failed: {CondenseError(error)}");
    }

    progress?.Report(new ReaderVideoExportProgress(ReaderVideoExportPhase.Encoding, 1d, "The high-quality MP4 is ready."));
  }

  private static string CondenseError(string error)
  {
    string[] lines = error.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return lines.LastOrDefault() ?? "Unknown encoder error.";
  }

  private static SolidColorBrush CreateBrush(string hex)
  {
    SolidColorBrush brush = new((Color)ColorConverter.ConvertFromString(hex));
    brush.Freeze();
    return brush;
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
    catch (IOException)
    {
      // Temporary render files can be reclaimed by the operating system later.
    }
    catch (UnauthorizedAccessException)
    {
      // Export completion must not be converted into a cleanup failure.
    }
  }

  private static void TryDeleteFile(string path)
  {
    try
    {
      if (File.Exists(path))
      {
        File.Delete(path);
      }
    }
    catch (IOException)
    {
      // Preserve the original export failure.
    }
    catch (UnauthorizedAccessException)
    {
      // Preserve the original export failure.
    }
  }
}

internal sealed record ReaderVideoExportRequest(
  IReadOnlyList<ReaderVideoExportSection> Sections,
  string OutputPath,
  FontFamily FontFamily,
  ReadingHighlightMode HighlightMode,
  ReaderHighlightVisualStyle HighlightStyle,
  string HighlightColor,
  ReaderThemeOption Theme,
  ReaderVideoFormat Format = ReaderVideoFormat.Landscape,
  ReaderVideoCaptionStyle CaptionStyle = ReaderVideoCaptionStyle.ReaderPage,
  double ReaderFontSize = 23d,
  ReaderLineMetricsProfile LineMetricsProfile = ReaderLineMetricsProfile.Standard)
{
  public ReaderVideoExportRequest Normalize()
  {
    ArgumentNullException.ThrowIfNull(Sections);
    if (Sections.Count == 0 || Sections.Any(section => !section.HasCompleteLayoutMetadata()))
    {
      throw new InvalidOperationException("Every video section must contain complete timing and paragraph metadata.");
    }

    string output = Path.ChangeExtension(OutputPath.Trim(), ".mp4");
    return this with
    {
      OutputPath = output,
      HighlightColor = string.IsNullOrWhiteSpace(HighlightColor) ? "#D4A94F" : HighlightColor,
      ReaderFontSize = Math.Clamp(ReaderFontSize, 18d, 38d),
    };
  }
}

internal sealed record ReaderVideoExportSection(
  IReadOnlyList<string> Words,
  IReadOnlyList<int> ParagraphStartWordIndices,
  IReadOnlyList<ReaderTimedWord> Timings,
  string AudioPath,
  TimeSpan Duration,
  IReadOnlyList<ReaderTextDirection>? ParagraphDirections = null)
{
  public bool HasCompleteLayoutMetadata()
  {
    if (Words.Count == 0 || Words.Count != Timings.Count)
    {
      return false;
    }

    int[] normalizedStarts = ParagraphStartWordIndices
      .Distinct()
      .OrderBy(index => index)
      .ToArray();
    if (!ParagraphStartWordIndices.SequenceEqual(normalizedStarts)
        || normalizedStarts.Any(index => index <= 0 || index >= Words.Count))
    {
      return false;
    }

    return ParagraphDirections is null
      || ParagraphDirections.Count == normalizedStarts.Length + 1;
  }

  public ReaderTextDirection GetParagraphDirection(int paragraphIndex)
  {
    return ParagraphDirections is not null
      && paragraphIndex >= 0
      && paragraphIndex < ParagraphDirections.Count
      ? ParagraphDirections[paragraphIndex]
      : ReaderTextDirection.LeftToRight;
  }
}

internal enum ReaderVideoExportPhase
{
  AcquiringRuntime,
  RenderingFrames,
  Encoding,
}

internal sealed record ReaderVideoExportProgress(ReaderVideoExportPhase Phase, double Fraction, string Detail);

internal sealed record ReaderVideoTimeline(IReadOnlyList<ReaderVideoWord> Words, TimeSpan Duration);

internal sealed record ReaderVideoWord(
  int GlobalIndex,
  string Text,
  TimeSpan Start,
  TimeSpan End,
  int SentenceStart,
  int SentenceEnd,
  bool ParagraphStart,
  ReaderTextDirection Direction);

internal sealed record ReaderVideoPage(
  int Start,
  int EndExclusive,
  string Text,
  IReadOnlyList<ReaderVideoWordSpan> WordSpans,
  ReaderTextDirection Direction);

internal sealed record ReaderVideoWordSpan(int GlobalIndex, int CharacterStart, int CharacterLength);

internal sealed record ReaderVideoCue(
  int PageIndex,
  int HighlightStart,
  int HighlightEnd,
  TimeSpan Start,
  TimeSpan End)
{
  public TimeSpan Duration => End - Start;
}

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DictateAnywhere.App.Workbench.Reading;

namespace DictateAnywhere.App.Tests;

public sealed class ReaderVideoExporterTests
{
  [VideoExportIntegrationFact]
  [Xunit.Trait("Category", "ProcessIntegration")]
  public async Task VideoExport_EndToEnd_WhenExplicitlyEnabled()
  {
    string directory = Path.Combine(Path.GetTempPath(), $"notype-video-integration-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      string audioPath = Path.Combine(directory, "hindi.wav");
      string outputPath = Path.Combine(directory, "hindi-reading.mp4");
      WriteSineWave(audioPath, TimeSpan.FromSeconds(4), 24_000);
      ReaderVideoExportRequest request = CreateHindiRequest(ReadingHighlightMode.Sentence, audioPath) with { OutputPath = outputPath };

      await ReaderVideoExporter.ExportAsync(request);

      FileInfo output = new(outputPath);
      Xunit.Assert.True(output.Exists);
      Xunit.Assert.True(output.Length > 10_000);
      byte[] header = File.ReadAllBytes(outputPath)[..12];
      Xunit.Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(header, 4, 4));
    }
    finally
    {
      Directory.Delete(directory, recursive: true);
    }
  }

  [VideoExportIntegrationFact]
  [Xunit.Trait("Category", "ProcessIntegration")]
  public async Task YouTubeShortExport_EndToEnd_WhenExplicitlyEnabled()
  {
    string directory = Path.Combine(Path.GetTempPath(), $"notype-short-integration-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      string audioPath = Path.Combine(directory, "short.wav");
      string outputPath = Path.Combine(directory, "reading-short.mp4");
      WriteSineWave(audioPath, TimeSpan.FromSeconds(4), 24_000);
      ReaderVideoExportRequest request = CreateHindiRequest(ReadingHighlightMode.Word, audioPath) with
      {
        OutputPath = outputPath,
        Format = ReaderVideoFormat.YouTubeShort,
        CaptionStyle = ReaderVideoCaptionStyle.FocusPill,
      };

      await ReaderVideoExporter.ExportAsync(request);

      FileInfo output = new(outputPath);
      Xunit.Assert.True(output.Exists);
      Xunit.Assert.True(output.Length > 10_000);
      byte[] header = File.ReadAllBytes(outputPath)[..12];
      Xunit.Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(header, 4, 4));
    }
    finally
    {
      Directory.Delete(directory, recursive: true);
    }
  }

  [Xunit.Fact]
  public void ReaderVideoExportRequest_ThrowsInvalidOperationException_WhenSectionsEmptyOrIncomplete()
  {
    ReaderVideoExportRequest emptyRequest = CreateHindiRequest(ReadingHighlightMode.Sentence) with
    {
      Sections = [],
    };

    Xunit.Assert.Throws<InvalidOperationException>(() => emptyRequest.Normalize());

    ReaderVideoExportRequest mismatchedRequest = CreateHindiRequest(ReadingHighlightMode.Sentence) with
    {
      Sections =
      [
        new ReaderVideoExportSection(
          Words: ["one", "two"],
          ParagraphStartWordIndices: [],
          Timings: [new ReaderTimedWord(0, TimeSpan.Zero, TimeSpan.FromSeconds(1))], // Only 1 timing for 2 words
          AudioPath: "test.wav",
          Duration: TimeSpan.FromSeconds(2)),
      ],
    };

    Xunit.Assert.Throws<InvalidOperationException>(() => mismatchedRequest.Normalize());
  }

  [Xunit.Fact]
  public void ReaderFfmpegRuntime_ExecutablePath_IsIsolatedUnderAppMediaRuntime()
  {
    string runtimeRoot = ReaderFfmpegRuntime.RuntimeRoot;
    string executablePath = ReaderFfmpegRuntime.ExecutablePath;

    Xunit.Assert.Contains("media-runtime", runtimeRoot, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Contains("DictateAnywhere", runtimeRoot, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.StartsWith(runtimeRoot, executablePath, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.EndsWith("ffmpeg.exe", executablePath, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Contains("ffmpeg-8.1.2-essentials_build.zip", ReaderFfmpegRuntime.DownloadUrl, StringComparison.Ordinal);
    Xunit.Assert.Matches("^[a-f0-9]{64}$", ReaderFfmpegRuntime.DownloadSha256);
  }

  [Xunit.Fact]
  public void ReaderFfmpegRuntime_RejectsContentThatDoesNotMatchPinnedHash()
  {
    string path = Path.Combine(Path.GetTempPath(), $"notype-ffmpeg-integrity-{Guid.NewGuid():N}.zip");
    try
    {
      File.WriteAllText(path, "not an approved archive");
      Xunit.Assert.Throws<InvalidDataException>(() =>
        ReaderFfmpegRuntime.VerifySha256(path, ReaderFfmpegRuntime.DownloadSha256));
    }
    finally
    {
      File.Delete(path);
    }
  }

  [Xunit.Fact]
  public async Task ExportAsync_ThrowsOperationCanceledException_WhenCancelledBeforeStart()
  {
    ReaderVideoExportRequest request = CreateHindiRequest(ReadingHighlightMode.Sentence, "dummy.wav");
    using CancellationTokenSource cts = new();
    cts.Cancel();

    await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => ReaderVideoExporter.ExportAsync(request, cancellationToken: cts.Token));
  }

  [Xunit.Fact]
  public void HindiHighlightGeometry_UsesStableBandsAndNeverHighlightsSpacesSeparately()
  {
    ReaderVideoExportRequest request = CreateHindiRequest(ReadingHighlightMode.Sentence);
    ReaderVideoTimeline timeline = ReaderVideoExporter.BuildTimeline(request);
    ReaderVideoPage page = Xunit.Assert.Single(ReaderVideoExporter.Paginate(request, timeline.Words));
    FormattedText formatted = ReaderVideoExporter.CreateFormattedText(request, page);

    IReadOnlyList<Rect> sentenceBands = ReaderVideoExporter.BuildStableHighlightBands(
      formatted,
      page,
      highlightStart: 0,
      highlightEnd: 2,
      new Point(300, 170));
    IReadOnlyList<Rect> firstWord = ReaderVideoExporter.BuildStableHighlightBands(formatted, page, 0, 0, new Point(300, 170));
    IReadOnlyList<Rect> secondWord = ReaderVideoExporter.BuildStableHighlightBands(formatted, page, 1, 1, new Point(300, 170));

    Xunit.Assert.Single(sentenceBands);
    Xunit.Assert.Single(firstWord);
    Xunit.Assert.Single(secondWord);
    Xunit.Assert.Equal(firstWord[0].Height, secondWord[0].Height);
    Xunit.Assert.Contains("नमस्ते", page.Text, StringComparison.Ordinal);
    Xunit.Assert.Contains("मिलेंगे", page.Text, StringComparison.Ordinal);
    Xunit.Assert.All(page.WordSpans, span => Xunit.Assert.Equal(timeline.Words[span.GlobalIndex].Text.Length, span.CharacterLength));
    Xunit.Assert.Equal(firstWord[0].Left, sentenceBands[0].Left);
    Xunit.Assert.True(sentenceBands[0].Right >= secondWord[0].Right);
  }

  [Xunit.Theory]
  [Xunit.InlineData((int)ReaderHighlightVisualStyle.AccentFill)]
  [Xunit.InlineData((int)ReaderHighlightVisualStyle.BoldFocus)]
  [Xunit.InlineData((int)ReaderHighlightVisualStyle.Underline)]
  [Xunit.InlineData((int)ReaderHighlightVisualStyle.Spotlight)]
  public void HindiVideoFrames_KeepEveryPixelOutsideTheHighlightLayerFixed(int styleValue)
  {
    ReaderVideoExportRequest request = CreateHindiRequest(ReadingHighlightMode.Word) with
    {
      HighlightStyle = (ReaderHighlightVisualStyle)styleValue,
      LineMetricsProfile = ReaderLineMetricsProfile.ComplexScript,
    };

    AssertPixelsOutsideHighlightLayerRemainFixed(request);
  }

  [Xunit.Theory]
  [Xunit.InlineData((int)ReaderHighlightVisualStyle.FocusType)]
  [Xunit.InlineData((int)ReaderHighlightVisualStyle.BoldFocus)]
  [Xunit.InlineData((int)ReaderHighlightVisualStyle.Underline)]
  [Xunit.InlineData((int)ReaderHighlightVisualStyle.Spotlight)]
  public void LatinVideoFrames_KeepEveryPixelOutsideTheHighlightLayerFixed(int styleValue)
  {
    ReaderVideoExportRequest request = CreateLatinRequest() with
    {
      HighlightStyle = (ReaderHighlightVisualStyle)styleValue,
    };

    AssertPixelsOutsideHighlightLayerRemainFixed(request);
  }

  [Xunit.Theory]
  [Xunit.InlineData((int)ReaderHighlightVisualStyle.AccentFill)]
  [Xunit.InlineData((int)ReaderHighlightVisualStyle.BoldFocus)]
  [Xunit.InlineData((int)ReaderHighlightVisualStyle.Underline)]
  [Xunit.InlineData((int)ReaderHighlightVisualStyle.Spotlight)]
  public void ShortVideoFrames_KeepEveryPixelOutsideTheHighlightLayerFixed(int styleValue)
  {
    string directory = Path.Combine(Path.GetTempPath(), $"notype-short-highlight-regression-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      ReaderVideoExportRequest request = CreateHindiRequest(ReadingHighlightMode.Word) with
      {
        Format = ReaderVideoFormat.YouTubeShort,
        CaptionStyle = ReaderVideoCaptionStyle.ReaderPage,
        HighlightStyle = (ReaderHighlightVisualStyle)styleValue,
        LineMetricsProfile = ReaderLineMetricsProfile.ComplexScript,
      };
      ReaderVideoPage page = ReaderVideoExporter
        .Paginate(request, ReaderVideoExporter.BuildTimeline(request).Words)[0];
      ReaderVideoCue firstCue = new(0, 0, 0, TimeSpan.Zero, TimeSpan.FromSeconds(0.7));
      ReaderVideoCue secondCue = new(0, 1, 1, TimeSpan.FromSeconds(0.7), TimeSpan.FromSeconds(1.4));
      string firstPath = Path.Combine(directory, "first.png");
      string secondPath = Path.Combine(directory, "second.png");
      ReaderVideoExporter.RenderFrame(request, page, firstCue, firstPath);
      ReaderVideoExporter.RenderFrame(request, page, secondCue, secondPath);

      FormattedText caption = ReaderVideoExporter.CreateShortFormattedText(request, page, firstCue);
      Point origin = new(
        (1080d - caption.Width) / 2d,
        (1920d - caption.Height) / 2d);
      Geometry firstGeometry = Xunit.Assert.IsAssignableFrom<Geometry>(
        ReaderVideoExporter.CreateShortHighlightGeometry(caption, page, firstCue, origin));
      Geometry secondGeometry = Xunit.Assert.IsAssignableFrom<Geometry>(
        ReaderVideoExporter.CreateShortHighlightGeometry(caption, page, secondCue, origin));
      Rect firstBounds = firstGeometry.Bounds;
      Rect secondBounds = secondGeometry.Bounds;
      firstBounds.Inflate(24d, 18d);
      secondBounds.Inflate(24d, 18d);

      AssertPixelsOutsideRegionsEqual(firstPath, secondPath, [firstBounds, secondBounds]);
    }
    finally
    {
      Directory.Delete(directory, recursive: true);
    }
  }

  private static void AssertPixelsOutsideHighlightLayerRemainFixed(ReaderVideoExportRequest request)
  {
    string directory = Path.Combine(Path.GetTempPath(), $"notype-highlight-regression-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      ReaderVideoTimeline timeline = ReaderVideoExporter.BuildTimeline(request);
      ReaderVideoPage page = Xunit.Assert.Single(ReaderVideoExporter.Paginate(request, timeline.Words));
      ReaderVideoCue firstCue = new(0, 0, 0, TimeSpan.Zero, TimeSpan.FromSeconds(0.7));
      ReaderVideoCue secondCue = new(0, 1, 1, TimeSpan.FromSeconds(0.7), TimeSpan.FromSeconds(1.4));
      string firstPath = Path.Combine(directory, "first.png");
      string secondPath = Path.Combine(directory, "second.png");
      ReaderVideoExporter.RenderFrame(request, page, firstCue, firstPath);
      ReaderVideoExporter.RenderFrame(request, page, secondCue, secondPath);
      FormattedText formatted = ReaderVideoExporter.CreateFormattedText(request, page);
      Rect firstBand = Xunit.Assert.Single(ReaderVideoExporter.BuildStableHighlightBands(formatted, page, 0, 0, new Point(300, 170)));
      Rect secondBand = Xunit.Assert.Single(ReaderVideoExporter.BuildStableHighlightBands(formatted, page, 1, 1, new Point(300, 170)));
      firstBand.Inflate(6, 6);
      secondBand.Inflate(6, 6);

      AssertPixelsOutsideRegionsEqual(firstPath, secondPath, [firstBand, secondBand]);
    }
    finally
    {
      Directory.Delete(directory, recursive: true);
    }
  }

  private static void AssertPixelsOutsideRegionsEqual(
    string firstPath,
    string secondPath,
    IReadOnlyList<Rect> excludedRegions)
  {
    (byte[] firstPixels, int width, int height, int stride) = ReadPixels(firstPath);
    (byte[] secondPixels, int secondWidth, int secondHeight, int secondStride) = ReadPixels(secondPath);
    Xunit.Assert.Equal((width, height, stride), (secondWidth, secondHeight, secondStride));
    bool foundHighlightDifference = false;
    for (int y = 0; y < height; y++)
    {
      for (int x = 0; x < width; x++)
      {
        int offset = (y * stride) + (x * 4);
        if (firstPixels.AsSpan(offset, 4).SequenceEqual(secondPixels.AsSpan(offset, 4)))
        {
          continue;
        }

        if (excludedRegions.Any(region => region.Contains(x, y)))
        {
          foundHighlightDifference = true;
          continue;
        }

        throw new Xunit.Sdk.XunitException($"Text moved outside the isolated highlight layer at pixel {x},{y}.");
      }
    }

    Xunit.Assert.True(foundHighlightDifference, "The rendered frames did not contain a visible highlight change.");
  }

  [Xunit.Fact]
  public void SentenceVideoTimeline_MergesWordsIntoExactSentenceCues()
  {
    ReaderVideoExportRequest request = CreateHindiRequest(ReadingHighlightMode.Sentence);
    ReaderVideoTimeline timeline = ReaderVideoExporter.BuildTimeline(request);
    IReadOnlyList<ReaderVideoPage> pages = ReaderVideoExporter.Paginate(request, timeline.Words);

    IReadOnlyList<ReaderVideoCue> cues = ReaderVideoExporter.BuildCues(request, timeline, pages);

    Xunit.Assert.Equal(2, cues.Count);
    Xunit.Assert.Equal((0, 2), (cues[0].HighlightStart, cues[0].HighlightEnd));
    Xunit.Assert.Equal((3, 5), (cues[1].HighlightStart, cues[1].HighlightEnd));
    Xunit.Assert.Equal(TimeSpan.FromSeconds(4), cues.Aggregate(TimeSpan.Zero, (total, cue) => total + cue.Duration));
  }

  [Xunit.Fact]
  public void VideoRequest_NormalizesToMp4AndRetainsSelectedColor()
  {
    ReaderVideoExportRequest request = CreateHindiRequest(ReadingHighlightMode.Word) with
    {
      OutputPath = "C:\\Temp\\reading.mkv",
      HighlightColor = "#4D9FFF",
    };

    ReaderVideoExportRequest normalized = request.Normalize();

    Xunit.Assert.EndsWith("reading.mp4", normalized.OutputPath, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Equal("#4D9FFF", normalized.HighlightColor);
  }

  [Xunit.Fact]
  public void YouTubeShort_UsesVerticalCanvasAndKineticCaptionDefault()
  {
    ReaderVideoExportRequest request = CreateHindiRequest(ReadingHighlightMode.Word) with
    {
      Format = ReaderVideoFormat.YouTubeShort,
      CaptionStyle = ReaderVideoCaptionStyle.ReaderPage,
    };

    ReaderVideoExportRequest normalized = request.Normalize();

    Xunit.Assert.Equal((1080, 1920), ReaderVideoExporter.GetOutputSize(normalized));
    Xunit.Assert.Equal(ReaderVideoCaptionStyle.ReaderPage, normalized.CaptionStyle);
    Xunit.Assert.All(ReaderVideoExporter.Paginate(normalized, ReaderVideoExporter.BuildTimeline(normalized).Words),
      page => Xunit.Assert.InRange(page.WordSpans.Count, 1, 7));
  }

  [Xunit.Fact]
  public void MixedDirectionVideo_UsesSeparatePagesWithMatchingFlowDirection()
  {
    ReaderTimedWord[] timings =
    [
      new(0, TimeSpan.Zero, TimeSpan.FromSeconds(1)),
      new(1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)),
      new(2, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3)),
      new(3, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4)),
    ];
    ReaderVideoExportSection section = new(
      ["اردو", "متن۔", "English", "text."],
      [2],
      timings,
      "sample.wav",
      TimeSpan.FromSeconds(4),
      [ReaderTextDirection.RightToLeft, ReaderTextDirection.LeftToRight]);
    ReaderVideoExportRequest request = CreateHindiRequest(ReadingHighlightMode.Word) with
    {
      Sections = [section],
    };

    IReadOnlyList<ReaderVideoPage> pages = ReaderVideoExporter.Paginate(
      request,
      ReaderVideoExporter.BuildTimeline(request).Words);

    Xunit.Assert.Equal(2, pages.Count);
    Xunit.Assert.Equal(ReaderTextDirection.RightToLeft, pages[0].Direction);
    Xunit.Assert.Equal(ReaderTextDirection.LeftToRight, pages[1].Direction);
    Xunit.Assert.Equal(TextAlignment.Right, ReaderVideoExporter.CreateFormattedText(request, pages[0]).TextAlignment);
    Xunit.Assert.Equal(TextAlignment.Left, ReaderVideoExporter.CreateFormattedText(request, pages[1]).TextAlignment);
  }

  [Xunit.Fact]
  public void VideoRequest_RejectsIncompleteParagraphDirectionMetadata()
  {
    ReaderVideoExportRequest request = CreateHindiRequest(ReadingHighlightMode.Word);
    ReaderVideoExportSection source = request.Sections[0];
    request = request with
    {
      Sections =
      [
        source with
        {
          ParagraphStartWordIndices = [3],
          ParagraphDirections = [ReaderTextDirection.LeftToRight],
        },
      ],
    };

    Xunit.Assert.Throws<InvalidOperationException>(() => request.Normalize());
  }

  [Xunit.Fact]
  public void VideoTypography_ScalesFromTheSelectedReaderSizeWithinSafeBounds()
  {
    ReaderVideoExportRequest small = CreateHindiRequest(ReadingHighlightMode.Word) with { ReaderFontSize = 18 };
    ReaderVideoExportRequest large = small with { ReaderFontSize = 38 };

    Xunit.Assert.True(ReaderVideoExporter.GetLandscapeFontSize(large) > ReaderVideoExporter.GetLandscapeFontSize(small));
    Xunit.Assert.True(ReaderVideoExporter.GetShortFontSize(large) > ReaderVideoExporter.GetShortFontSize(small));
  }

  [Xunit.Fact]
  public void VideoTypography_UsesTheDeclaredLineMetricsProfile()
  {
    ReaderVideoExportRequest standardRequest = CreateHindiRequest(ReadingHighlightMode.Word);
    ReaderVideoPage page = Xunit.Assert.Single(
      ReaderVideoExporter.Paginate(
        standardRequest,
        ReaderVideoExporter.BuildTimeline(standardRequest).Words));
    ReaderVideoExportRequest complexRequest = standardRequest with
    {
      LineMetricsProfile = ReaderLineMetricsProfile.ComplexScript,
    };

    FormattedText standard = ReaderVideoExporter.CreateFormattedText(standardRequest, page);
    FormattedText complex = ReaderVideoExporter.CreateFormattedText(complexRequest, page);

    Xunit.Assert.True(complex.LineHeight > standard.LineHeight);
  }

  [Xunit.Fact]
  public void AbyssTheme_IsNearBlackWithWarmHighContrastInk()
  {
    ReaderThemeOption theme = Xunit.Assert.Single(ReaderThemeOption.Defaults, option => option.DisplayName == "Abyss Black");

    Xunit.Assert.Equal("#030405", theme.PageColor);
    Xunit.Assert.NotEqual("#000000", theme.PageColor);
    Xunit.Assert.Equal("#F4F1EA", theme.InkColor);
  }

  private static ReaderVideoExportRequest CreateHindiRequest(ReadingHighlightMode mode, string audioPath = "sample.wav")
  {
    string[] words = ["नमस्ते", "दुनिया", "।", "फिर", "मिलेंगे", "।"];
    ReaderTimedWord[] timings =
    [
      new(0, TimeSpan.Zero, TimeSpan.FromSeconds(0.7)),
      new(1, TimeSpan.FromSeconds(0.7), TimeSpan.FromSeconds(1.4)),
      new(2, TimeSpan.FromSeconds(1.4), TimeSpan.FromSeconds(2.0)),
      new(3, TimeSpan.FromSeconds(2.0), TimeSpan.FromSeconds(2.6)),
      new(4, TimeSpan.FromSeconds(2.6), TimeSpan.FromSeconds(3.5)),
      new(5, TimeSpan.FromSeconds(3.5), TimeSpan.FromSeconds(4.0)),
    ];
    ReaderVideoExportSection section = new(words, [], timings, audioPath, TimeSpan.FromSeconds(4));
    return new ReaderVideoExportRequest(
      [section],
      "C:\\Temp\\reading.mp4",
      new FontFamily("Nirmala UI"),
      mode,
      ReaderHighlightVisualStyle.AccentFill,
      "#D4A94F",
      ReaderThemeOption.Defaults[0]);
  }

  private static ReaderVideoExportRequest CreateLatinRequest()
  {
    string[] words = ["Reading", "remains", "stable", "."];
    ReaderTimedWord[] timings =
    [
      new(0, TimeSpan.Zero, TimeSpan.FromSeconds(0.7)),
      new(1, TimeSpan.FromSeconds(0.7), TimeSpan.FromSeconds(1.4)),
      new(2, TimeSpan.FromSeconds(1.4), TimeSpan.FromSeconds(2.1)),
      new(3, TimeSpan.FromSeconds(2.1), TimeSpan.FromSeconds(2.4)),
    ];
    ReaderVideoExportSection section = new(words, [], timings, "sample.wav", TimeSpan.FromSeconds(2.4));
    return new ReaderVideoExportRequest(
      [section],
      "C:\\Temp\\reading.mp4",
      new FontFamily("Segoe UI"),
      ReadingHighlightMode.Word,
      ReaderHighlightVisualStyle.FocusType,
      "#4D9FFF",
      ReaderThemeOption.Defaults[0]);
  }

  private static void WriteSineWave(string path, TimeSpan duration, int sampleRate)
  {
    int sampleCount = checked((int)Math.Round(duration.TotalSeconds * sampleRate));
    using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
    using BinaryWriter writer = new(stream);
    int dataLength = sampleCount * sizeof(short);
    writer.Write("RIFF"u8.ToArray());
    writer.Write(36 + dataLength);
    writer.Write("WAVEfmt "u8.ToArray());
    writer.Write(16);
    writer.Write((short)1);
    writer.Write((short)1);
    writer.Write(sampleRate);
    writer.Write(sampleRate * sizeof(short));
    writer.Write((short)sizeof(short));
    writer.Write((short)16);
    writer.Write("data"u8.ToArray());
    writer.Write(dataLength);
    for (int index = 0; index < sampleCount; index++)
    {
      double sample = Math.Sin(2d * Math.PI * 220d * index / sampleRate) * 0.12d;
      writer.Write((short)Math.Round(sample * short.MaxValue));
    }
  }

  private static (byte[] Pixels, int Width, int Height, int Stride) ReadPixels(string path)
  {
    using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    PngBitmapDecoder decoder = new(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
    FormatConvertedBitmap bitmap = new(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
    int stride = bitmap.PixelWidth * 4;
    byte[] pixels = new byte[stride * bitmap.PixelHeight];
    bitmap.CopyPixels(pixels, stride, 0);
    return (pixels, bitmap.PixelWidth, bitmap.PixelHeight, stride);
  }
}

internal sealed class VideoExportIntegrationFactAttribute : Xunit.FactAttribute
{
  public VideoExportIntegrationFactAttribute()
  {
    if (!string.Equals(Environment.GetEnvironmentVariable("NOTYPE_VIDEO_EXPORT_INTEGRATION"), "1", StringComparison.Ordinal))
    {
      Skip = "Set NOTYPE_VIDEO_EXPORT_INTEGRATION=1 after provisioning FFmpeg to run the end-to-end video export tests.";
    }
  }
}

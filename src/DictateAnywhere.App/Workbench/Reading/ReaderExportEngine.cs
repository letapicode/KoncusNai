using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>Adapts the WPF video renderer and WAV writer to the UI-independent export command owner.</summary>
internal sealed class ReaderExportEngine : IReaderExportEngine
{
  public async Task ExportAudioAsync(
    IReadOnlyList<string> sourcePaths,
    string outputPath,
    CancellationToken cancellationToken) =>
    _ = await ReaderAudioExporter.ExportAsync(sourcePaths, outputPath, cancellationToken).ConfigureAwait(false);

  public Task ExportVideoAsync(
    IReadOnlyList<ReaderVideoExportSection> sections,
    ReaderVideoVisualSettings settings,
    string outputPath,
    IProgress<ReaderVideoExportProgress>? progress,
    CancellationToken cancellationToken) =>
    ReaderVideoExporter.ExportAsync(new ReaderVideoExportRequest(
      sections,
      outputPath,
      new FontFamily(settings.FontFamilyName),
      settings.HighlightMode,
      settings.HighlightStyle,
      settings.HighlightColor,
      settings.Theme,
      settings.Format,
      settings.CaptionStyle,
      settings.FontSize,
      settings.LineMetricsProfile), progress, cancellationToken);
}

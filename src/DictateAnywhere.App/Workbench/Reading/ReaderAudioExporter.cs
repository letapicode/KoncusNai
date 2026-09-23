using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Inference;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>Creates one lossless audiobook without resampling or lossy encoding.</summary>
internal static class ReaderAudioExporter
{
  public static Task<TimeSpan> ExportAsync(
    IReadOnlyList<string> sourcePaths,
    string outputPath,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(sourcePaths);
    ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
    if (!string.Equals(Path.GetExtension(outputPath), ".wav", StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidOperationException("Audiobooks are exported as lossless WAV files.");
    }

    cancellationToken.ThrowIfCancellationRequested();
    return Task.Run(() =>
    {
      cancellationToken.ThrowIfCancellationRequested();
      TimeSpan duration = WaveFileConcatenator.Combine(sourcePaths, outputPath);
      cancellationToken.ThrowIfCancellationRequested();
      return duration;
    }, cancellationToken);
  }
}

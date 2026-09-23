using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench;

internal enum WorkbenchAudioImportStatus
{
  Transcribed,
  NoAudio,
  NoSpeech,
  Failed,
}

internal sealed record WorkbenchAudioImportItem(
  string FilePath,
  WorkbenchAudioImportStatus Status,
  string Text);

/// <summary>Streams sequential audio-file transcription so batches stay bounded and partial progress is durable.</summary>
internal sealed class WorkbenchAudioImportController
{
  private readonly IDiagnostics diagnostics;
  private readonly Func<string, CancellationToken, Task<AudioCaptureResult>> decodeAsync;
  private readonly Func<AudioCaptureResult, CancellationToken, Task<TranscriptionResult>> transcribeAsync;

  public WorkbenchAudioImportController(
    IDiagnostics diagnostics,
    Func<string, CancellationToken, Task<AudioCaptureResult>> decodeAsync,
    Func<AudioCaptureResult, CancellationToken, Task<TranscriptionResult>> transcribeAsync)
  {
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    this.decodeAsync = decodeAsync ?? throw new ArgumentNullException(nameof(decodeAsync));
    this.transcribeAsync = transcribeAsync ?? throw new ArgumentNullException(nameof(transcribeAsync));
  }

  public async IAsyncEnumerable<WorkbenchAudioImportItem> ImportAsync(
    IEnumerable<string> files,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(files);
    string[] selectedFiles = files
      .Where(file => File.Exists(file) && AudioFileTranscriptionImporter.IsSupportedAudioFile(file))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToArray();
    foreach (string file in selectedFiles)
    {
      cancellationToken.ThrowIfCancellationRequested();
      WorkbenchAudioImportItem item;
      try
      {
        AudioCaptureResult audio = await decodeAsync(file, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        if (audio.Pcm16Mono.Length == 0)
        {
          item = new WorkbenchAudioImportItem(file, WorkbenchAudioImportStatus.NoAudio, string.Empty);
        }
        else
        {
          TranscriptionResult transcription = await transcribeAsync(audio, cancellationToken).ConfigureAwait(true);
          string text = (transcription.Text ?? string.Empty).Trim();
          item = string.IsNullOrWhiteSpace(text)
            ? new WorkbenchAudioImportItem(file, WorkbenchAudioImportStatus.NoSpeech, string.Empty)
            : new WorkbenchAudioImportItem(file, WorkbenchAudioImportStatus.Transcribed, text);
        }
      }
      catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
      {
        diagnostics.Warning($"File transcription failed for '{file}': {ex.Message}");
        item = new WorkbenchAudioImportItem(file, WorkbenchAudioImportStatus.Failed, string.Empty);
      }

      yield return item;
    }
  }
}

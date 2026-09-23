using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using DictateAnywhere.App.Composition;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench;

internal sealed record WorkbenchReadAloudState(
  string StatusMessage,
  bool IsPreparationVisible,
  string PreparationMessage,
  bool IsPreparing,
  bool CanStop);

/// <summary>Owns the Workbench read-aloud command, user-facing outcome policy, and session lifetime.</summary>
internal sealed class WorkbenchReadAloudController : IAsyncDisposable
{
  private readonly WorkbenchSpeechSession session;
  private readonly IDiagnostics diagnostics;
  private Task activeRead = Task.CompletedTask;
  private bool disposed;

  public WorkbenchReadAloudController(WorkbenchSpeechSession session, IDiagnostics diagnostics)
  {
    this.session = session ?? throw new ArgumentNullException(nameof(session));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    session.PlaybackEnded += OnPlaybackEnded;
    session.PlaybackFailed += OnPlaybackFailed;
  }

  public event EventHandler<WorkbenchReadAloudState>? StateChanged;

  public bool IsPreparing => session.IsPreparing;

  public bool CanStop => session.CanStop;

  public void StartRead(string text, string sourceName)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    if (session.IsPreparing)
    {
      Publish("Speech is already being prepared.");
      return;
    }

    activeRead = ReadAsync(text, sourceName);
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "This command boundary observes background read-aloud failures and maps them to diagnostics and presentation state.")]
  internal async Task ReadAsync(string text, string sourceName)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    string normalizedSourceName = string.IsNullOrWhiteSpace(sourceName) ? "this text" : sourceName.Trim();
    if (string.IsNullOrWhiteSpace(text))
    {
      Publish("There is no readable text in that selection.");
      return;
    }

    if (session.IsPreparing)
    {
      Publish("Speech is already being prepared.");
      return;
    }

    try
    {
      Publish(
        $"Preparing {normalizedSourceName} for local read-aloud. The first Kokoro use downloads and warms the voice model once.",
        isPreparationVisible: true,
        preparationMessage: $"Preparing {normalizedSourceName} locally. This can take a moment.",
        isPreparingOverride: true,
        canStopOverride: true);
      Task<TextToSpeechResult> preparation = session.ReadAsync(text);
      TextToSpeechResult result = await preparation.ConfigureAwait(false);
      Publish($"Reading {normalizedSourceName} aloud ({result.SegmentCount} segment{(result.SegmentCount == 1 ? string.Empty : "s")}).");
    }
    catch (OperationCanceledException)
    {
      Publish("Speech preparation cancelled.");
    }
    catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException)
    {
      diagnostics.Warning($"Local speech synthesis failed: {ex.Message}");
      Publish("Could not prepare speech. See Diagnostics.");
    }
    catch (Exception ex)
    {
      diagnostics.Error("Unexpected Workbench read-aloud failure.", ex);
      Publish("Could not prepare speech. The error was recorded in Diagnostics.");
    }
  }

  public void Stop()
  {
    if (disposed)
    {
      return;
    }

    session.Stop();
    Publish("Reading stopped.");
  }

  public async Task ResetAsync()
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    await session.ResetAsync().ConfigureAwait(false);
    await activeRead.ConfigureAwait(false);
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    session.PlaybackEnded -= OnPlaybackEnded;
    session.PlaybackFailed -= OnPlaybackFailed;
    await session.DisposeAsync().ConfigureAwait(false);
    await activeRead.ConfigureAwait(false);
  }

  private void OnPlaybackEnded(object? sender, EventArgs eventArgs) => Publish("Reading finished.");

  private void OnPlaybackFailed(object? sender, WavAudioPlaybackFailedEventArgs eventArgs)
  {
    diagnostics.Warning($"Workbench speech playback failed: {eventArgs.Exception.Message}");
    Publish("Could not play speech. Check the audio device or see Diagnostics.");
  }

  private void Publish(
    string statusMessage,
    bool isPreparationVisible = false,
    string preparationMessage = "",
    bool? isPreparingOverride = null,
    bool? canStopOverride = null)
  {
    if (disposed)
    {
      return;
    }

    StateChanged?.Invoke(this, new WorkbenchReadAloudState(
      statusMessage,
      isPreparationVisible,
      preparationMessage,
      isPreparingOverride ?? session.IsPreparing,
      canStopOverride ?? session.CanStop));
  }
}

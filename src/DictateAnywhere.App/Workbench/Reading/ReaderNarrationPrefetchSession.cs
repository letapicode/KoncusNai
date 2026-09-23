using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>
/// Owns the single next-section narration buffer for a Reading Studio window.
/// Prepared narration and timing remain cached by <see cref="ReaderNarrationSession"/>.
/// </summary>
internal sealed class ReaderNarrationPrefetchSession : IAsyncDisposable
{
  private readonly ReaderNarrationSession narrationSession;
  private CancellationTokenSource? cancellation;
  private ReadingSection? bufferedSection;
  private ReaderNarrationProfile? bufferedProfile;
  private Task<TextToSpeechResult>? bufferedSpeechTask;
  private Task observationTask = Task.CompletedTask;
  private Task? disposalTask;
  private bool disposed;

  public ReaderNarrationPrefetchSession(ReaderNarrationSession narrationSession)
  {
    this.narrationSession = narrationSession ?? throw new ArgumentNullException(nameof(narrationSession));
  }

  public bool IsFor(ReadingSection section, ReaderNarrationProfile profile)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(section);
    ArgumentNullException.ThrowIfNull(profile);
    return Matches(section, profile);
  }

  [SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "The session stores the cancellation source and releases it through Cancel or Dispose.")]
  public bool Begin(ReadingSection section, ReaderNarrationProfile profile)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(section);
    ArgumentNullException.ThrowIfNull(profile);
    if (Matches(section, profile))
    {
      return false;
    }

    CancelCore();
    cancellation = new CancellationTokenSource();
    bufferedSection = section;
    bufferedProfile = profile;
    bufferedSpeechTask = PrepareAsync(section, profile, cancellation.Token);
    observationTask = Task.WhenAll(
      observationTask,
      ObserveAsync(bufferedSpeechTask, cancellation));
    return true;
  }

  public bool TryTake(
    ReadingSection section,
    ReaderNarrationProfile profile,
    out Task<TextToSpeechResult>? speechTask)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(section);
    ArgumentNullException.ThrowIfNull(profile);
    if (bufferedSpeechTask is null || !Matches(section, profile))
    {
      speechTask = null;
      return false;
    }

    speechTask = bufferedSpeechTask;
    bufferedSpeechTask = null;
    bufferedSection = null;
    bufferedProfile = null;
    return true;
  }

  public void Cancel()
  {
    ThrowIfDisposed();
    CancelCore();
  }

  public ValueTask DisposeAsync()
  {
    if (disposalTask is not null)
    {
      return new ValueTask(disposalTask);
    }

    disposed = true;
    CancelCore();
    disposalTask = observationTask;
    return new ValueTask(disposalTask);
  }

  private async Task<TextToSpeechResult> PrepareAsync(
    ReadingSection section,
    ReaderNarrationProfile profile,
    CancellationToken cancellationToken)
  {
    TextToSpeechResult speech = await narrationSession
      .GetSpeechAsync(section, profile, cancellationToken)
      .ConfigureAwait(false);
    _ = await narrationSession
      .GetTimingAsync(section, profile, speech, cancellationToken)
      .ConfigureAwait(false);
    return speech;
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Background prefetch is opportunistic; the foreground preparation path reports actionable failures.")]
  private static async Task ObserveAsync(
    Task<TextToSpeechResult> task,
    CancellationTokenSource source)
  {
    try
    {
      await task.ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      // Moving to another section or changing narration invalidates this buffer.
    }
    catch
    {
      // Foreground preparation retries and reports a useful error if this section is requested.
    }
    finally
    {
      source.Dispose();
    }
  }

  private void CancelCore()
  {
    TryCancel(cancellation);
    cancellation = null;
    bufferedSection = null;
    bufferedProfile = null;
    bufferedSpeechTask = null;
  }

  private static void TryCancel(CancellationTokenSource? source)
  {
    if (source is null)
    {
      return;
    }

    try
    {
      source.Cancel();
    }
    catch (ObjectDisposedException)
    {
      // The background preparation completed while cancellation was requested.
    }
  }

  private bool Matches(ReadingSection section, ReaderNarrationProfile profile) =>
    ReferenceEquals(bufferedSection, section) && bufferedProfile == profile;

  private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}

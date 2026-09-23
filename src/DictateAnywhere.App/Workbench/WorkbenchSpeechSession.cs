using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Composition;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench;

/// <summary>Owns Workbench local read-aloud preparation, playback, reset, and disposal.</summary>
internal sealed class WorkbenchSpeechSession : IAsyncDisposable
{
  private readonly object sync = new();
  private readonly Func<ITextToSpeechService> speechServiceFactory;
  private readonly IWavAudioPlaybackService playbackService;
  private ITextToSpeechService? speechService;
  private CancellationTokenSource? preparationCancellation;
  private TaskCompletionSource? preparationCompletion;
  private int operationVersion;
  private bool isPreparing;
  private bool isPlaying;
  private bool isResetting;
  private bool disposed;

  public event EventHandler? PlaybackEnded;

  public event EventHandler<WavAudioPlaybackFailedEventArgs>? PlaybackFailed;

  [SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "This session takes ownership of the WAV playback service and disposes it with the Workbench.")]
  public WorkbenchSpeechSession(Func<ITextToSpeechService> speechServiceFactory)
    : this(speechServiceFactory, new WavAudioPlaybackService())
  {
  }

  internal WorkbenchSpeechSession(
    Func<ITextToSpeechService> speechServiceFactory,
    IWavAudioPlaybackService playbackService)
  {
    this.speechServiceFactory = speechServiceFactory ?? throw new ArgumentNullException(nameof(speechServiceFactory));
    this.playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
    this.playbackService.PlaybackEnded += OnPlaybackEnded;
    this.playbackService.PlaybackFailed += OnPlaybackFailed;
  }

  public bool IsPreparing
  {
    get
    {
      lock (sync)
      {
        return isPreparing;
      }
    }
  }

  public bool IsPlaying
  {
    get
    {
      lock (sync)
      {
        return isPlaying;
      }
    }
  }

  public bool CanStop
  {
    get
    {
      lock (sync)
      {
        return isPreparing || isPlaying;
      }
    }
  }

  public async Task<TextToSpeechResult> ReadAsync(string text)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(text);
    CancellationTokenSource currentCancellation;
    TaskCompletionSource currentCompletion;
    int version;
    lock (sync)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      if (isResetting)
      {
        throw new InvalidOperationException("Workbench speech is resetting.");
      }

      if (isPreparing)
      {
        throw new InvalidOperationException("Workbench speech is already being prepared.");
      }

      currentCancellation = new CancellationTokenSource();
      currentCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      preparationCancellation = currentCancellation;
      preparationCompletion = currentCompletion;
      version = ++operationVersion;
      isPreparing = true;
    }

    try
    {
      ITextToSpeechService service = GetOrCreateSpeechService();
      TextToSpeechResult result = await service
        .SynthesizeAsync(new TextToSpeechRequest(text), currentCancellation.Token)
        .ConfigureAwait(false);

      playbackService.Stop();
      lock (sync)
      {
        if (disposed || version != operationVersion || currentCancellation.IsCancellationRequested)
        {
          throw new OperationCanceledException(currentCancellation.Token);
        }

        isPlaying = true;
      }

      try
      {
        playbackService.Play(result.AudioPath);
      }
      catch
      {
        lock (sync)
        {
          if (version == operationVersion)
          {
            isPlaying = false;
          }
        }

        throw;
      }

      bool playbackWasCancelled;
      lock (sync)
      {
        playbackWasCancelled = disposed || version != operationVersion || currentCancellation.IsCancellationRequested;
        if (playbackWasCancelled)
        {
          isPlaying = false;
        }
      }

      if (playbackWasCancelled)
      {
        playbackService.Stop();
        throw new OperationCanceledException(currentCancellation.Token);
      }

      return result;
    }
    finally
    {
      lock (sync)
      {
        if (ReferenceEquals(preparationCompletion, currentCompletion))
        {
          preparationCancellation = null;
          preparationCompletion = null;
          isPreparing = false;
        }
      }

      currentCancellation.Dispose();
      currentCompletion.TrySetResult();
    }
  }

  public void Stop()
  {
    CancellationTokenSource? cancellation;
    lock (sync)
    {
      if (disposed)
      {
        return;
      }

      operationVersion++;
      cancellation = preparationCancellation;
      isPlaying = false;
    }

    CancelIfActive(cancellation);
    playbackService.Stop();
  }

  public async Task ResetAsync()
  {
    CancellationTokenSource? cancellation;
    Task preparation;
    lock (sync)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      if (isResetting)
      {
        throw new InvalidOperationException("Workbench speech is already resetting.");
      }

      isResetting = true;
      operationVersion++;
      cancellation = preparationCancellation;
      preparation = preparationCompletion?.Task ?? Task.CompletedTask;
      isPlaying = false;
    }

    CancelIfActive(cancellation);
    playbackService.Stop();
    try
    {
      await preparation.ConfigureAwait(false);
      ITextToSpeechService? service;
      lock (sync)
      {
        service = speechService;
        speechService = null;
      }

      await DisposeSpeechServiceAsync(service).ConfigureAwait(false);
    }
    finally
    {
      lock (sync)
      {
        isResetting = false;
      }
    }
  }

  public async ValueTask DisposeAsync()
  {
    CancellationTokenSource? cancellation;
    Task preparation;
    lock (sync)
    {
      if (disposed)
      {
        return;
      }

      disposed = true;
      isResetting = true;
      operationVersion++;
      cancellation = preparationCancellation;
      preparation = preparationCompletion?.Task ?? Task.CompletedTask;
      isPlaying = false;
    }

    CancelIfActive(cancellation);
    playbackService.Stop();
    await preparation.ConfigureAwait(false);
    ITextToSpeechService? service;
    lock (sync)
    {
      service = speechService;
      speechService = null;
    }

    try
    {
      await DisposeSpeechServiceAsync(service).ConfigureAwait(false);
    }
    finally
    {
      playbackService.PlaybackEnded -= OnPlaybackEnded;
      playbackService.PlaybackFailed -= OnPlaybackFailed;
      playbackService.Dispose();
    }
  }

  private void OnPlaybackEnded(object? sender, EventArgs eventArgs)
  {
    lock (sync)
    {
      if (disposed || !isPlaying)
      {
        return;
      }

      isPlaying = false;
    }

    PlaybackEnded?.Invoke(this, EventArgs.Empty);
  }

  private void OnPlaybackFailed(object? sender, WavAudioPlaybackFailedEventArgs eventArgs)
  {
    lock (sync)
    {
      if (disposed || !isPlaying)
      {
        return;
      }

      isPlaying = false;
    }

    PlaybackFailed?.Invoke(this, eventArgs);
  }

  private ITextToSpeechService GetOrCreateSpeechService()
  {
    lock (sync)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      return speechService ??= speechServiceFactory();
    }
  }

  private static async ValueTask DisposeSpeechServiceAsync(ITextToSpeechService? service)
  {
    if (service is IAsyncDisposable asyncDisposable)
    {
      await asyncDisposable.DisposeAsync().ConfigureAwait(false);
    }
    else if (service is IDisposable disposable)
    {
      disposable.Dispose();
    }
  }

  private static void CancelIfActive(CancellationTokenSource? cancellation)
  {
    if (cancellation is null)
    {
      return;
    }

    try
    {
      cancellation.Cancel();
    }
    catch (ObjectDisposedException)
    {
      // The preparation owner completed and disposed the source after cancellation was requested.
    }
  }
}

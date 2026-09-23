using System;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>Owns voice-preview resolution, cancellation, and lifecycle state.</summary>
internal sealed class ReaderVoicePreviewSession : IDisposable
{
  private readonly object sync = new();
  private readonly ITextToSpeechService speechService;
  private readonly ReaderVoicePreviewCache cache;
  private CancellationTokenSource? preparationCancellation;
  private ReaderVoicePreviewState state;
  private int operationVersion;
  private bool disposed;

  public ReaderVoicePreviewSession(
    ITextToSpeechService speechService,
    ReaderVoicePreviewCache? cache = null)
  {
    ArgumentNullException.ThrowIfNull(speechService);
    this.speechService = speechService;
    this.cache = cache ?? new ReaderVoicePreviewCache();
  }

  public ReaderVoicePreviewState State
  {
    get
    {
      lock (sync)
      {
        return state;
      }
    }
  }

  public bool CanStop => State != ReaderVoicePreviewState.Idle;

  public bool HasBundledPreview(ReaderLanguageOption language, ReaderVoiceOption voice)
  {
    ArgumentNullException.ThrowIfNull(language);
    ArgumentNullException.ThrowIfNull(voice);
    lock (sync)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
    }

    return cache.HasBundledPreview(language, voice);
  }

  public async Task<string> PrepareAsync(
    ReaderLanguageOption language,
    ReaderVoiceOption voice)
  {
    ArgumentNullException.ThrowIfNull(language);
    ArgumentNullException.ThrowIfNull(voice);
    CancellationTokenSource currentCancellation;
    CancellationTokenSource? previousCancellation;
    int version;
    lock (sync)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      previousCancellation = preparationCancellation;
      currentCancellation = new CancellationTokenSource();
      preparationCancellation = currentCancellation;
      version = ++operationVersion;
      state = ReaderVoicePreviewState.Preparing;
    }

    CancelIfActive(previousCancellation);
    try
    {
      string path = await cache.GetOrCreateAsync(
        speechService,
        language,
        voice,
        currentCancellation.Token).ConfigureAwait(false);
      lock (sync)
      {
        if (disposed || version != operationVersion || currentCancellation.IsCancellationRequested)
        {
          throw new OperationCanceledException(currentCancellation.Token);
        }

        state = ReaderVoicePreviewState.Playing;
      }

      return path;
    }
    catch
    {
      lock (sync)
      {
        if (!disposed && version == operationVersion)
        {
          state = ReaderVoicePreviewState.Idle;
        }
      }

      throw;
    }
    finally
    {
      lock (sync)
      {
        if (ReferenceEquals(preparationCancellation, currentCancellation))
        {
          preparationCancellation = null;
        }
      }

      currentCancellation.Dispose();
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
      preparationCancellation = null;
      state = ReaderVoicePreviewState.Idle;
    }

    CancelIfActive(cancellation);
  }

  public bool FinishPlayback()
  {
    lock (sync)
    {
      if (disposed || state != ReaderVoicePreviewState.Playing)
      {
        return false;
      }

      state = ReaderVoicePreviewState.Idle;
      return true;
    }
  }

  public void Dispose()
  {
    CancellationTokenSource? cancellation;
    lock (sync)
    {
      if (disposed)
      {
        return;
      }

      disposed = true;
      operationVersion++;
      cancellation = preparationCancellation;
      preparationCancellation = null;
      state = ReaderVoicePreviewState.Idle;
    }

    CancelIfActive(cancellation);
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
      // The asynchronous owner completed and disposed the source after it was handed off for cancellation.
    }
  }
}

internal enum ReaderVoicePreviewState
{
  Idle,
  Preparing,
  Playing,
}

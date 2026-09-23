using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>
/// Prepares and caches section narration and its display-token timing for one Reading Studio window.
/// The speech service is borrowed because the window also uses it for voice previews; the alignment
/// service is owned and disposed with this session.
/// </summary>
internal sealed class ReaderNarrationSession : IAsyncDisposable
{
  private readonly ITextToSpeechService speechService;
  private readonly ReaderTimingResolver timingResolver;
  private readonly SemaphoreSlim synthesisGate = new(1, 1);
  private readonly SemaphoreSlim timingGate = new(1, 1);
  private readonly CancellationTokenSource lifetimeCancellation = new();
  private readonly object cacheSync = new();
  private readonly ReadingAudioCache audioCache = new();
  private readonly Dictionary<ReaderTimingCacheKey, ReaderWordTimingMap> timingCache = [];
  private int cacheGeneration;
  private bool disposed;

  public ReaderNarrationSession(
    ITextToSpeechService speechService,
    ISpeechAlignmentService alignmentService)
  {
    this.speechService = speechService ?? throw new ArgumentNullException(nameof(speechService));
    timingResolver = new ReaderTimingResolver(
      alignmentService ?? throw new ArgumentNullException(nameof(alignmentService)));
  }

  public bool TryGetSpeech(
    ReadingSection section,
    ReaderNarrationProfile profile,
    out TextToSpeechResult? speech)
  {
    ThrowIfDisposed();
    lock (cacheSync)
    {
      return audioCache.TryGet(CreateSpeechKey(section, profile), out speech);
    }
  }

  public bool TryGetTiming(
    ReadingSection section,
    ReaderNarrationProfile profile,
    TextToSpeechResult speech,
    out ReaderWordTimingMap? timing)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(speech);
    lock (cacheSync)
    {
      return timingCache.TryGetValue(CreateTimingKey(section, profile, speech), out timing);
    }
  }

  public async Task<TextToSpeechResult> GetSpeechAsync(
    ReadingSection section,
    ReaderNarrationProfile profile,
    CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    ReaderSpeechCacheKey key = CreateSpeechKey(section, profile);
    int generation;
    lock (cacheSync)
    {
      if (audioCache.TryGet(key, out TextToSpeechResult? cached) && cached is not null)
      {
        return cached;
      }

      generation = cacheGeneration;
    }

    using CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
      cancellationToken,
      lifetimeCancellation.Token);
    await synthesisGate.WaitAsync(operationCancellation.Token).ConfigureAwait(false);
    try
    {
      ThrowIfDisposed();
      lock (cacheSync)
      {
        if (audioCache.TryGet(key, out TextToSpeechResult? cached) && cached is not null)
        {
          return cached;
        }

        generation = cacheGeneration;
      }

      TextToSpeechResult speech = await speechService
        .SynthesizeAsync(profile.CreateSpeechRequest(key.NarrationText), operationCancellation.Token)
        .ConfigureAwait(false);
      lock (cacheSync)
      {
        if (generation == cacheGeneration)
        {
          audioCache.Store(key, speech);
        }
      }

      return speech;
    }
    finally
    {
      synthesisGate.Release();
    }
  }

  public async Task<ReaderWordTimingMap> GetTimingAsync(
    ReadingSection section,
    ReaderNarrationProfile profile,
    TextToSpeechResult speech,
    CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(speech);
    ReaderTimingCacheKey key = CreateTimingKey(section, profile, speech);
    int generation;
    lock (cacheSync)
    {
      if (timingCache.TryGetValue(key, out ReaderWordTimingMap? cached))
      {
        if (ReadingAudioCache.IsValidAudioFile(key.AudioPath))
        {
          return cached;
        }

        timingCache.Remove(key);
      }

      generation = cacheGeneration;
    }

    using CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
      cancellationToken,
      lifetimeCancellation.Token);
    await timingGate.WaitAsync(operationCancellation.Token).ConfigureAwait(false);
    try
    {
      ThrowIfDisposed();
      lock (cacheSync)
      {
        if (timingCache.TryGetValue(key, out ReaderWordTimingMap? cached))
        {
          if (ReadingAudioCache.IsValidAudioFile(key.AudioPath))
          {
            return cached;
          }

          timingCache.Remove(key);
        }

        generation = cacheGeneration;
      }

      ReaderWordTimingMap timing = await timingResolver.ResolveAsync(
        section,
        key.Speech.NarrationText,
        speech,
        profile.LanguageCode,
        profile.WordTimingStrategy,
        operationCancellation.Token).ConfigureAwait(false);
      lock (cacheSync)
      {
        if (generation == cacheGeneration)
        {
          timingCache[key] = timing;
        }
      }

      return timing;
    }
    finally
    {
      timingGate.Release();
    }
  }

  public void Clear()
  {
    ThrowIfDisposed();
    lock (cacheSync)
    {
      cacheGeneration++;
      audioCache.Clear();
      timingCache.Clear();
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    lifetimeCancellation.Cancel();
    await synthesisGate.WaitAsync().ConfigureAwait(false);
    await timingGate.WaitAsync().ConfigureAwait(false);
    try
    {
      lock (cacheSync)
      {
        cacheGeneration++;
        audioCache.Clear();
        timingCache.Clear();
      }

      await timingResolver.DisposeAsync().ConfigureAwait(false);
    }
    finally
    {
      timingGate.Release();
      synthesisGate.Release();
      lifetimeCancellation.Dispose();
      synthesisGate.Dispose();
      timingGate.Dispose();
    }
  }

  private static ReaderSpeechCacheKey CreateSpeechKey(
    ReadingSection section,
    ReaderNarrationProfile profile)
  {
    ArgumentNullException.ThrowIfNull(section);
    ArgumentNullException.ThrowIfNull(profile);
    return new ReaderSpeechCacheKey(
      section.Index,
      ReaderNarrationText.Create(section),
      profile.LanguageCode,
      profile.ProviderId,
      profile.VoiceId,
      profile.Description);
  }

  private static ReaderTimingCacheKey CreateTimingKey(
    ReadingSection section,
    ReaderNarrationProfile profile,
    TextToSpeechResult speech) => new(
      CreateSpeechKey(section, profile),
      speech.AudioPath,
      speech.Duration,
      profile.WordTimingStrategy);

  private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

  private readonly record struct ReaderTimingCacheKey(
    ReaderSpeechCacheKey Speech,
    string AudioPath,
    TimeSpan Duration,
    ReaderWordTimingStrategy Strategy);
}

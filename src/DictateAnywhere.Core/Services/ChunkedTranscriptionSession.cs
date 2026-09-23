using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Core.Services;

internal sealed class ChunkedTranscriptionSession : IAsyncDisposable
{
  internal const int MaximumPendingAudioBytes = 4 * 1024 * 1024;
  private const int MaximumTranscriptCharacters = 1_000_000;
  private readonly IChunkedAudioCaptureService capture;
  private readonly ITranscriptionService transcription;
  private readonly string modelId;
  private readonly IDiagnostics diagnostics;
  private readonly CancellationTokenSource cancellation;
  private readonly object sync = new();
  private readonly Channel<AudioCaptureChunk> queue = Channel.CreateBounded<AudioCaptureChunk>(new BoundedChannelOptions(32)
  {
    SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait,
  });
  private readonly Task worker;
  private readonly List<TranscriptionChunkResult> results = [];
  private int pendingBytes;
  private Exception? failure;
  private bool accepting = true;
  private bool started;
  private Task? disposal;

  public ChunkedTranscriptionSession(IChunkedAudioCaptureService audioCaptureService,
    ITranscriptionService transcriptionService, string modelId, IDiagnostics diagnostics, CancellationToken cancellationToken)
  {
    capture = audioCaptureService ?? throw new ArgumentNullException(nameof(audioCaptureService));
    transcription = transcriptionService ?? throw new ArgumentNullException(nameof(transcriptionService));
    this.modelId = modelId;
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    worker = Task.Run(ConsumeAsync);
  }

  public void Start()
  {
    lock (sync)
    {
      ObjectDisposedException.ThrowIf(disposal is not null, this);
      if (started) return;
      started = true;
      capture.ChunkAvailable += OnChunkAvailable;
    }
  }

  public void QueueChunk(AudioCaptureChunk? chunk)
  {
    if (chunk is null || chunk.Audio.Pcm16Mono.Length == 0) return;
    lock (sync)
    {
      if (!accepting) return; // A callback captured before unsubscribe is harmless.
      int bytes = chunk.Audio.Pcm16Mono.Length;
      if (bytes > MaximumPendingAudioBytes - pendingBytes || !queue.Writer.TryWrite(chunk))
      {
        failure = new InvalidOperationException("Dictation exceeded the pending audio limit because transcription could not keep up. Use a shorter recording.");
        accepting = false;
        queue.Writer.TryComplete();
        diagnostics.Warning("Dictation pending audio limit reached; this session cannot be completed without missing audio.");
        return;
      }
      pendingBytes += bytes;
    }
  }

  public async Task<IReadOnlyList<TranscriptionChunkResult>> CompleteAsync(CancellationToken completionCancellationToken)
  {
    CloseAdmission();
    using CancellationTokenRegistration registration = completionCancellationToken.Register(cancellation.Cancel);
    await worker.ConfigureAwait(false);
    completionCancellationToken.ThrowIfCancellationRequested();
    if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    return results.OrderBy(result => result.SequenceNumber).ToArray();
  }

  public ValueTask DisposeAsync()
  {
    lock (sync) return new ValueTask(disposal ??= DisposeCoreAsync());
  }

  private async Task DisposeCoreAsync()
  {
    await Task.Yield();
    CloseAdmission();
    try { cancellation.Cancel(); }
    catch (AggregateException)
    {
      diagnostics.Warning("A transcription cancellation callback failed; waiting for the provider to release the session.");
    }
    finally
    {
      // A provider ignoring cancellation retains ownership until its call ends;
      // the next session must never reuse its resources in the meantime.
      await worker.ConfigureAwait(false);
      cancellation.Dispose();
      results.Clear();
    }
  }

  private void CloseAdmission()
  {
    lock (sync)
    {
      accepting = false;
      capture.ChunkAvailable -= OnChunkAvailable;
      queue.Writer.TryComplete();
    }
  }

  private void OnChunkAvailable(object? sender, AudioCaptureChunkAvailableEventArgs e) => QueueChunk(e.Chunk);

  [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "The single worker retains its failure for CompleteAsync and is always awaited during disposal.")]
  private async Task ConsumeAsync()
  {
    int characters = 0;
    try
    {
      await foreach (AudioCaptureChunk chunk in queue.Reader.ReadAllAsync(cancellation.Token).ConfigureAwait(false))
      {
        try
        {
          TranscriptionResult result = await transcription.TranscribeAsync(chunk.Audio, modelId, cancellation.Token).ConfigureAwait(false);
          characters = checked(characters + result.Text.Length);
          if (characters > MaximumTranscriptCharacters || results.Count >= 10_000)
            throw new InvalidOperationException("Dictation transcript limit reached. Use a shorter recording.");
          results.Add(new TranscriptionChunkResult(chunk.SequenceNumber, result));
        }
        finally { lock (sync) pendingBytes -= chunk.Audio.Pcm16Mono.Length; }
      }
    }
    catch (Exception ex)
    {
      lock (sync) failure ??= ex;
      CloseAdmission();
    }
    finally
    {
      while (queue.Reader.TryRead(out _)) { }
      lock (sync) pendingBytes = 0;
    }
  }
}

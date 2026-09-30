using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Runtime;

/// <summary>Prevents the global hotkey and Workbench from opening competing microphone sessions.</summary>
internal sealed class ExclusiveAudioCaptureService(IChunkedAudioCaptureService inner) : IChunkedAudioCaptureService, IAsyncDisposable
{
  private static object? owner;

  internal static IDisposable ReserveForRuntimePreparation()
  {
    PreparationReservation reservation = new();
    if (Interlocked.CompareExchange(ref owner, reservation, null) is not null)
      throw new InvalidOperationException("Stop the active dictation recording before preparing acceleration.");
    return reservation;
  }

  private sealed class PreparationReservation : IDisposable
  {
    public void Dispose() => Interlocked.CompareExchange(ref owner, null, this);
  }

  public bool IsCapturing => inner.IsCapturing;
  public event EventHandler<AudioCaptureChunkAvailableEventArgs>? ChunkAvailable
  {
    add => inner.ChunkAvailable += value;
    remove => inner.ChunkAvailable -= value;
  }

  public Task StartAsync(CancellationToken cancellationToken = default) => StartAsync(false, cancellationToken);
  public Task StartChunkedAsync(CancellationToken cancellationToken = default) => StartAsync(true, cancellationToken);

  private async Task StartAsync(bool chunked, CancellationToken token)
  {
    token.ThrowIfCancellationRequested();
    if (Interlocked.CompareExchange(ref owner, this, null) is not null)
      throw new InvalidOperationException("Another recording or acceleration preparation is active. Stop or cancel it before recording.");
    try
    {
      if (chunked) await inner.StartChunkedAsync(token).ConfigureAwait(false);
      else await inner.StartAsync(token).ConfigureAwait(false);
    }
    catch
    {
      if (!inner.IsCapturing) Release();
      throw;
    }
  }

  public async Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken = default)
  {
    try { return await inner.StopAsync(cancellationToken).ConfigureAwait(false); }
    finally { if (!inner.IsCapturing) Release(); }
  }

  public async Task<AudioCaptureChunk?> StopAndFlushChunkAsync(CancellationToken cancellationToken = default)
  {
    try { return await inner.StopAndFlushChunkAsync(cancellationToken).ConfigureAwait(false); }
    finally { if (!inner.IsCapturing) Release(); }
  }

  public async ValueTask DisposeAsync()
  {
    try
    {
      if (inner is IAsyncDisposable disposable) await disposable.DisposeAsync().ConfigureAwait(false);
    }
    finally { Release(); }
  }

  private void Release() => Interlocked.CompareExchange(ref owner, null, this);
}

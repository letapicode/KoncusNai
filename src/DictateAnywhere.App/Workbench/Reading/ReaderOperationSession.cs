using System;
using System.Threading;

namespace DictateAnywhere.App.Workbench.Reading;

internal enum ReaderOperationKind
{
  SectionPreparation,
  RangePreparation,
  DocumentImport,
  AudioExport,
  VideoExport,
  YouTubePublish,
}

/// <summary>Owns cancellation and mutual exclusion for long-running Reading Studio operations.</summary>
internal sealed class ReaderOperationSession : IDisposable
{
  private readonly object sync = new();
  private ReaderOperation? activeOperation;
  private bool disposed;

  public ReaderOperationKind? ActiveKind
  {
    get
    {
      lock (sync)
      {
        return activeOperation?.Kind;
      }
    }
  }

  public bool IsPreparing => ActiveKind is ReaderOperationKind.SectionPreparation
    or ReaderOperationKind.RangePreparation
    or ReaderOperationKind.DocumentImport;

  public bool IsExporting => ActiveKind is ReaderOperationKind.AudioExport
    or ReaderOperationKind.VideoExport
    or ReaderOperationKind.YouTubePublish;

  public ReaderOperation? TryBegin(ReaderOperationKind kind)
  {
    lock (sync)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      if (activeOperation is not null)
      {
        return null;
      }

      activeOperation = new ReaderOperation(this, kind, new CancellationTokenSource());
      return activeOperation;
    }
  }

  public void CancelPreparation()
  {
    lock (sync)
    {
      if (activeOperation?.Kind is ReaderOperationKind.SectionPreparation
          or ReaderOperationKind.RangePreparation
          or ReaderOperationKind.DocumentImport)
      {
        activeOperation.Cancel();
      }
    }
  }

  public void Dispose()
  {
    ReaderOperation? operation;
    lock (sync)
    {
      if (disposed)
      {
        return;
      }

      disposed = true;
      operation = activeOperation;
      activeOperation = null;
    }

    operation?.CancelAndDispose();
  }

  private void Complete(ReaderOperation operation)
  {
    lock (sync)
    {
      if (ReferenceEquals(activeOperation, operation))
      {
        activeOperation = null;
      }
    }
  }

  internal sealed class ReaderOperation : IDisposable
  {
    private readonly ReaderOperationSession owner;
    private readonly CancellationTokenSource cancellationSource;
    private int disposed;

    internal ReaderOperation(
      ReaderOperationSession owner,
      ReaderOperationKind kind,
      CancellationTokenSource cancellationSource)
    {
      this.owner = owner;
      Kind = kind;
      this.cancellationSource = cancellationSource;
    }

    public ReaderOperationKind Kind { get; }
    public CancellationToken CancellationToken => cancellationSource.Token;

    public void Cancel() => CancelCore();

    public void Dispose()
    {
      if (Interlocked.Exchange(ref disposed, 1) != 0)
      {
        return;
      }

      owner.Complete(this);
      cancellationSource.Dispose();
    }

    private void CancelCore()
    {
      try
      {
        cancellationSource.Cancel();
      }
      catch (ObjectDisposedException)
      {
      }
    }

    internal void CancelAndDispose()
    {
      CancelCore();
      Dispose();
    }
  }
}

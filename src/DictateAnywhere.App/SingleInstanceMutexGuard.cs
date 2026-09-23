using System;
using System.Threading;

namespace DictateAnywhere.App;

public sealed class SingleInstanceMutexGuard : IDisposable
{
  public const string DefaultMutexName = @"Local\DictateAnywhere.App";

  private readonly Mutex mutex;
  private readonly bool createdNew;
  private bool ownsHandle;
  private bool disposed;

  public SingleInstanceMutexGuard(string mutexName)
  {
    if (string.IsNullOrWhiteSpace(mutexName))
    {
      throw new ArgumentException("Mutex name must not be empty.", nameof(mutexName));
    }

    mutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);
    this.createdNew = createdNew;
    ownsHandle = createdNew;
  }

  public bool TryAcquire()
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    if (ownsHandle)
    {
      return true;
    }

    // Named mutex acquisition is thread-reentrant. For single-instance guarantees we
    // only treat first-creation ownership as a successful acquire.
    return createdNew;
  }

  public void Dispose()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    if (ownsHandle)
    {
      mutex.ReleaseMutex();
      ownsHandle = false;
    }

    mutex.Dispose();
  }
}

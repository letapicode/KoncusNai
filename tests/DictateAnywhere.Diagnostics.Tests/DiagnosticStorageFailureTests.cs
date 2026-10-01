using DictateAnywhere.Core.Services;
using Xunit;

namespace DictateAnywhere.Diagnostics.Tests;

public sealed class DiagnosticStorageFailureTests
{
  [Theory]
  [InlineData("directory")]
  [InlineData("open")]
  [InlineData("retention")]
  [InlineData("retention-stat")]
  [InlineData("writer-initial-flush")]
  public void PartialInitializationReleasesResourcesWithoutRetryStorm(string fault)
  {
    Storage storage = new() { Fault = fault };
    using StructuredLocalDiagnostics sink = Create(storage);
    for (int i = 0; i < 20; i++) sink.Info("operation succeeded");
    Assert.True(storage.Opens <= 1);
    Assert.Equal(storage.Opens == 1 && fault != "open" ? 1 : 0, storage.Stream.Disposals);
  }

  [Theory]
  [InlineData("write")]
  [InlineData("flush")]
  [InlineData("length")]
  [InlineData("rotation")]
  public void WriteFailureDisablesIoAndReleasesBothWriterAndStream(string fault)
  {
    Storage storage = new();
    using StructuredLocalDiagnostics sink = Create(storage);
    storage.Stream.Fault = fault;
    if (fault == "rotation") { storage.Stream.SetLength(2048); storage.Fault = "open"; }
    long before = DiagnosticBoundary.FailureCount;
    sink.Warning("private transcript=do not copy this to fallback");
    int opens = storage.Opens;
    storage.Stream.Fault = null; storage.Fault = null; // Storage recovers; this instance does not retry.
    sink.Error("later private error", new IOException("sensitive user path"));
    Assert.Equal(opens, storage.Opens);
    Assert.Equal(1, storage.Stream.Disposals);
    Assert.True(DiagnosticBoundary.FailureCount > before);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void FlushAndDisposeFailuresDoNotSkipIndependentStreamRelease(bool disposeFails)
  {
    Storage storage = new();
    StructuredLocalDiagnostics sink = Create(storage);
    storage.Stream.Fault = "flush";
    storage.Stream.DisposeFails = disposeFails;
    sink.Dispose(); sink.Dispose(); sink.Info("late call");
    Assert.Equal(1, storage.Stream.Disposals);
    Assert.Equal(1, storage.Opens);
  }

  [Fact]
  public async Task ConcurrentDisposeWaitsForActiveWriterAndLateCallsCannotReopen()
  {
    await Task.Run(async () =>
    {
      Storage storage = new();
      StructuredLocalDiagnostics sink = Create(storage);
      using ManualResetEventSlim release = new();
      TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
      bool callbackDeadlineExpired = false;
      storage.Stream.OnWrite = () => { entered.TrySetResult(); callbackDeadlineExpired = !release.Wait(TimeSpan.FromSeconds(30)); };
      Task write = Task.Run(() => sink.Info("owned write"));
      Task? dispose = null;
      try
      {
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        dispose = Task.Run(sink.Dispose);
        Assert.Equal(0, storage.Stream.Disposals);
        release.Set();
        await Task.WhenAll(write, dispose).WaitAsync(TimeSpan.FromSeconds(30));
        sink.Info("late write");
        Assert.Equal(1, storage.Stream.Disposals);
        Assert.Equal(1, storage.Opens);
        Assert.False(callbackDeadlineExpired);
      }
      finally
      {
        release.Set();
        await write.WaitAsync(TimeSpan.FromSeconds(30));
        if (dispose is not null) await dispose.WaitAsync(TimeSpan.FromSeconds(30));
        sink.Dispose();
      }
    });
  }

  [Fact]
  public void ReentrantDisposeWaitsUntilWriteUnwinds()
  {
    Storage storage = new();
    using StructuredLocalDiagnostics sink = Create(storage);
    int? disposalsDuringCallback = null;
    storage.Stream.OnWrite = () =>
    {
      sink.Dispose(); sink.Info("reentrant late write");
      disposalsDuringCallback = storage.Stream.Disposals;
    };
    sink.Info("owned write");
    Assert.Equal(0, disposalsDuringCallback);
    Assert.Equal(1, storage.Stream.Disposals);
    Assert.Equal(1, storage.Opens);
  }

  [Fact]
  public void BadRecordsAreDroppedWithoutPoisoningHealthyStorageOrDumpingRawFallback()
  {
    Storage storage = new();
    using StructuredLocalDiagnostics sink = Create(storage);
    object[] cycle = new object[1]; cycle[0] = cycle;
    sink.Info("secret transcript=private", new Dictionary<string, object?> { ["nested"] = cycle });
    sink.Error("secret", new ThrowingMessage());
    sink.Info(new string('x', 3000));
    sink.Info("valid record", new Dictionary<string, object?> { ["durationMs"] = 12 });
    string records = System.Text.Encoding.UTF8.GetString(storage.Stream.ToArray());
    Assert.DoesNotContain("secret", records);
    Assert.DoesNotContain("private", records);
    Assert.Contains("valid record", records);
    Assert.Equal(1, storage.Opens);
  }

  [Fact]
  public void InvalidConfigurationRemainsAnErrorBeforeAnyIo()
  {
    Storage storage = new();
    Assert.Throws<ArgumentOutOfRangeException>(() => new StructuredLocalDiagnostics(Options with { RetainedFileCount = 0 }, storage));
    Assert.Throws<ArgumentException>(() => new StructuredLocalDiagnostics(Options with { FileNamePrefix = "../escape" }, storage));
    Assert.Equal(0, storage.Opens);
    storage.Fault = "writer-construction";
    Assert.Throws<ArgumentException>(() => Create(storage));
    Assert.Equal(1, storage.Stream.Disposals);
  }

  [Fact]
  public void RetentionDeleteFailureDoesNotSkipWritingOrRetryPerMessage()
  {
    Storage storage = new() { Fault = "delete" };
    long before = DiagnosticBoundary.FailureCount;
    using StructuredLocalDiagnostics sink = Create(storage);
    sink.Info("valid record");
    Assert.Equal(1, storage.Opens);
    Assert.True(DiagnosticBoundary.FailureCount > before);
  }

  [Fact]
  public void ReentrantMetadataAccessorCannotRecursivelyWriteOrDisposeUnderFormatting()
  {
    Storage storage = new();
    using StructuredLocalDiagnostics sink = Create(storage);
    sink.Error("outer", new ReentrantMessage(() => sink.Info("nested private input")));
    Assert.DoesNotContain("nested", System.Text.Encoding.UTF8.GetString(storage.Stream.ToArray()));
  }

  private sealed class ReentrantMessage(Action callback) : Exception
  {
    public override string Message { get { callback(); return "controlled error"; } }
  }

  private static StructuredDiagnosticsOptions Options => StructuredDiagnosticsOptions.Default with
    { LogsDirectoryPath = Path.Combine(Path.GetTempPath(), "koncus-fake-storage"), MaxFileSizeBytes = 2048 };
  private static StructuredLocalDiagnostics Create(Storage storage) => new(Options, storage);
  private sealed class ThrowingMessage : Exception { public override string Message => throw new InvalidOperationException("secret accessor"); }

  private sealed class Storage : IDiagnosticLogStorage
  {
    internal string? Fault { get; set; }
    internal int Opens { get; private set; }
    internal FaultStream Stream { get; } = new();
    public void CreateDirectory(string path) { if (Fault == "directory") throw new UnauthorizedAccessException(); }
    public Stream Open(string path)
    {
      Opens++;
      if (Fault == "open") throw new IOException("controlled open failure");
      Stream.Fault = Fault;
      return Stream;
    }
    public string[] GetFiles(string directory, string pattern)
    {
      if (Fault == "retention") throw new IOException("controlled enumeration failure");
      return Fault is "delete" or "retention-stat" ? Enumerable.Range(0, 8).Select(i => "owned-fake-" + i).ToArray() : [];
    }
    public DateTime GetLastWriteTimeUtc(string path) => Fault == "retention-stat" ? throw new IOException("controlled stat failure") : DateTime.UnixEpoch;
    public void Delete(string path) => throw new IOException("controlled retention failure");
  }

  private sealed class FaultStream : MemoryStream
  {
    internal string? Fault { get; set; }
    internal bool DisposeFails { get; set; }
    internal int Disposals { get; private set; }
    internal Action? OnWrite { get; set; }
    public override bool CanWrite => Fault != "writer-construction" && base.CanWrite;
    public override long Length => Fault == "length" ? throw new IOException("controlled stat failure") : base.Length;
    public override void Flush() { if (Fault is "flush" or "writer-initial-flush") throw new IOException("controlled disk full"); base.Flush(); }
    public override void Write(byte[] buffer, int offset, int count)
    {
      OnWrite?.Invoke();
      if (Fault == "write") throw new IOException("controlled sharing failure");
      base.Write(buffer, offset, count);
    }
    public override void Write(ReadOnlySpan<byte> buffer)
    {
      OnWrite?.Invoke();
      if (Fault == "write") throw new IOException("controlled write failure");
      base.Write(buffer);
    }
    protected override void Dispose(bool disposing)
    {
      if (disposing) Disposals++;
      base.Dispose(disposing);
      if (DisposeFails) throw new IOException("controlled close failure");
    }
  }
}

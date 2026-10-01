using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;
using Xunit;

namespace DictateAnywhere.Core.Tests;

public sealed class DiagnosticBoundaryTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void AllOverloadsAreIsolatedAndCapabilityAndOwnershipArePreserved(bool structured)
  {
    Sink sink = structured ? new StructuredSink() : new Sink();
    IDiagnostics guarded = DiagnosticBoundary.Wrap(sink);
    Assert.Same(guarded, DiagnosticBoundary.Wrap(guarded));
    Assert.Equal(structured, guarded is IStructuredDiagnostics);
    Assert.False(guarded is IDisposable);
    Exception original = new IOException("original");
    guarded.Info("info"); guarded.Warning("warning"); guarded.Error("error", original);
    if (guarded is IStructuredDiagnostics metadata)
    {
      Dictionary<string, object?> properties = new() { ["stage"] = "test", ["durationMs"] = 2 };
      metadata.Info("info", properties); metadata.Warning("warning", properties); metadata.Error("error", original, properties);
      Assert.Same(properties, ((StructuredSink)sink).Properties);
    }
    Assert.Equal(structured ? 6 : 3, sink.Calls);
    Assert.Same(original, sink.Exception);
  }

  [Fact]
  public void ReentrantSinkDoesNotRecursivelyDispatchAndCanRecoverOnNextCall()
  {
    Sink sink = new() { Throw = false };
    IDiagnostics guarded = DiagnosticBoundary.Wrap(sink);
    sink.Callback = () => guarded.Warning("nested");
    guarded.Info("outer");
    Assert.Equal(1, sink.Calls);
    sink.Callback = null;
    guarded.Info("recovered");
    Assert.Equal(2, sink.Calls);
  }

  [Fact]
  public async Task ConcurrentCallsAndCancellationReportingPreserveTheirOwners()
  {
    Sink sink = new();
    IDiagnostics guarded = DiagnosticBoundary.Wrap(sink);
    await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() => guarded.Info("test"))));
    using CancellationTokenSource cancellation = new();
    using CancellationTokenRegistration registration = cancellation.Token.Register(() => guarded.Error("canceled"));
    cancellation.Cancel();
    Assert.True(cancellation.IsCancellationRequested);
    Assert.Equal(41, sink.Calls);
  }

  [Fact]
  public void ArgumentPreparationFailureDoesNotInvokeSinkOrReplaceOriginalError()
  {
    Sink sink = new();
    IDiagnostics guarded = DiagnosticBoundary.Wrap(sink);
    long before = DiagnosticBoundary.FailureCount;
    DiagnosticBoundary.Report(() => guarded.Info(new ThrowingText().ToString()));
    Assert.Equal(0, sink.Calls);
    Assert.True(DiagnosticBoundary.FailureCount > before);
    Assert.Throws<ArgumentNullException>(() => DiagnosticBoundary.Report(null!));
    Assert.Null(DiagnosticBoundary.Wrap(null));
  }

  private sealed class ThrowingText { public override string ToString() => throw new IOException("private payload"); }
  private class Sink : IDiagnostics
  {
    private int calls;
    internal int Calls => Volatile.Read(ref calls);
    internal bool Throw { get; set; } = true;
    internal Action? Callback { get; set; }
    internal Exception? Exception { get; private set; }
    protected void Record(Exception? exception = null)
    {
      Interlocked.Increment(ref calls); Exception = exception; Callback?.Invoke();
      if (Throw) throw new IOException("private diagnostic fault");
    }
    public void Info(string message) => Record();
    public void Warning(string message) => Record();
    public void Error(string message, Exception? exception = null) => Record(exception);
  }
  private sealed class StructuredSink : Sink, IStructuredDiagnostics
  {
    internal IReadOnlyDictionary<string, object?>? Properties { get; private set; }
    public void Info(string message, IReadOnlyDictionary<string, object?> properties) { Properties = properties; Record(); }
    public void Warning(string message, IReadOnlyDictionary<string, object?> properties) { Properties = properties; Record(); }
    public void Error(string message, Exception? exception, IReadOnlyDictionary<string, object?> properties) { Properties = properties; Record(exception); }
  }
}

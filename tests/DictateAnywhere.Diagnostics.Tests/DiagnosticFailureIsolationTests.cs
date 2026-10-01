using DictateAnywhere.Core.Contracts;
using Xunit;

namespace DictateAnywhere.Diagnostics.Tests;

public sealed class DiagnosticFailureIsolationTests
{
  [Theory]
  [InlineData("complete")]
  [InlineData("cancel")]
  [InlineData("dispose")]
  public void ScopeFormattingFailureCannotChangeTerminalOutcome(string terminal)
  {
    OperationDiagnosticScope scope = new(new ThrowingSink(), "test");
    System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentCulture;
    Exception? reportingFailure;
    try
    {
      System.Globalization.CultureInfo.CurrentCulture = new ThrowingFormatCulture();
      reportingFailure = Record.Exception((Action)(() =>
      {
        if (terminal == "cancel") scope.Cancel();
        else if (terminal == "dispose") scope.Dispose();
        else scope.Complete();
      }));
    }
    finally { System.Globalization.CultureInfo.CurrentCulture = previous; scope.Dispose(); }
    Assert.Null(reportingFailure);
    Assert.Equal(terminal == "cancel" ? OperationOutcome.Cancelled : OperationOutcome.Completed, scope.Outcome);
  }

  private sealed class ThrowingFormatCulture : System.Globalization.CultureInfo
  {
    internal ThrowingFormatCulture() : base("en-US") { }
    public override object? GetFormat(Type? formatType) => throw new IOException("private formatting fault");
  }

  [Fact]
  public void FailedUiClassificationRetainsGenericFailureWithoutReadingRawFallback()
  {
    UserFacingDiagnosticError error = DiagnosticErrorClassifier.Describe("test", "failure", new ThrowingMessage());
    Assert.Equal(DiagnosticFailureCategory.Unknown, error.Category);
    Assert.Equal(DiagnosticRemediationCodes.Unexpected, error.RemediationCode);
    Assert.DoesNotContain("private", error.Message);
  }
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void ScopeReportingCannotReplaceOperationFailure(bool structured)
  {
    IOException original = new("controlled operation failure");
    Exception? result = Record.Exception((Action)(() =>
    {
      using OperationDiagnosticScope scope = OperationDiagnosticScope.Begin(structured ? new StructuredThrowingSink() : new ThrowingSink(), "test");
      scope.Stage("work");
      scope.Fail(original);
      throw original;
    }));
    Assert.Same(original, result);
  }

  [Fact]
  public void EnvironmentalInitializationFailureDoesNotAbortProductStartup()
  {
    string root = Path.Combine(Path.GetTempPath(), "koncus-diagnostic-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      string file = Path.Combine(root, "blocked");
      File.WriteAllText(file, "owned test fixture");
      using StructuredLocalDiagnostics sink = new(StructuredDiagnosticsOptions.Default with { LogsDirectoryPath = file });
      sink.Info("test operation succeeded");
      Assert.Equal("owned test fixture", File.ReadAllText(file));
    }
    finally { Directory.Delete(root, recursive: true); }
  }

  [Theory]
  [InlineData("complete")]
  [InlineData("cancel")]
  [InlineData("fail")]
  [InlineData("dispose")]
  public void ScopeRetainsOutcomeAndRepeatedTerminalCallsDoNotReportAgain(string terminal)
  {
    ThrowingSink sink = new();
    using OperationDiagnosticScope scope = OperationDiagnosticScope.Begin(sink, "test");
    using OperationDiagnosticScope child = scope.BeginChild("child");
    scope.Stage("stage");
    switch (terminal)
    {
      case "complete": scope.Complete(); break;
      case "cancel": scope.Cancel(); break;
      case "fail": scope.Fail(new ThrowingMessage()); break;
      default: scope.Dispose(); break;
    }
    int calls = sink.Calls;
    scope.Complete(); scope.Cancel(); scope.Fail(new IOException()); scope.Dispose(); scope.Stage("late");
    Assert.Equal(calls, sink.Calls);
    Assert.Equal(terminal == "cancel" ? OperationOutcome.Cancelled : terminal == "fail" ? OperationOutcome.Failed : OperationOutcome.Completed, scope.Outcome);
  }

  private sealed class ThrowingMessage : Exception { public override string Message => throw new InvalidOperationException("private accessor"); }

  [Fact]
  public async Task ConcurrentTerminalReportingRetainsOneOutcome()
  {
    ConcurrentSink sink = new();
    OperationDiagnosticScope scope = OperationDiagnosticScope.Begin(sink, "test");
    await Task.WhenAll(Task.Run(() => scope.Complete()), Task.Run(() => scope.Cancel()), Task.Run(scope.Dispose));
    Assert.Equal(2, sink.Calls); // Begin plus exactly one terminal report.
    Assert.Contains(scope.Outcome, new[] { OperationOutcome.Completed, OperationOutcome.Cancelled });
  }

  private sealed class ConcurrentSink : IDiagnostics
  {
    private int calls;
    internal int Calls => Volatile.Read(ref calls);
    public void Info(string message) { Interlocked.Increment(ref calls); throw new IOException(); }
    public void Warning(string message) => throw new IOException();
    public void Error(string message, Exception? exception = null) => throw new IOException();
  }

  private sealed class StructuredThrowingSink : ThrowingSink, IStructuredDiagnostics
  {
    public void Info(string message, IReadOnlyDictionary<string, object?> properties) => throw new IOException();
    public void Warning(string message, IReadOnlyDictionary<string, object?> properties) => throw new IOException();
    public void Error(string message, Exception? exception, IReadOnlyDictionary<string, object?> properties) => throw new IOException();
  }

  private class ThrowingSink : IDiagnostics
  {
    internal int Calls { get; private set; }
    public void Info(string message) { Calls++; throw new IOException("sink failed"); }
    public void Warning(string message) => throw new IOException("sink failed");
    public void Error(string message, Exception? exception = null) => throw new IOException("sink failed");
  }
}

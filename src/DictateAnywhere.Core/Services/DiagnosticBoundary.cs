using System.Diagnostics.CodeAnalysis;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Core.Services;

/// <summary>Best-effort reporting only; never wrap product operations such as export or cleanup.</summary>
[SuppressMessage("Design", "CA1031:Do not catch general exception types",
  Justification = "Reporting faults must not replace an operational result. No failed payload is sent to another sink.")]
public static class DiagnosticBoundary
{
  private static long failureCount;
  [ThreadStatic] private static bool dispatching;

  /// <summary>Process-local saturated counter. Contains no message, exception, path, or user data.</summary>
  public static long FailureCount => Interlocked.Read(ref failureCount);

  [return: NotNullIfNotNull(nameof(sink))]
  public static IDiagnostics? Wrap(IDiagnostics? sink) => sink switch
  {
    null => null,
    Plain => sink,
    IStructuredDiagnostics structured => new Structured(structured),
    _ => new Plain(sink),
  };

  /// <summary>Includes argument preparation inside the reporting boundary. Remains synchronous.</summary>
  public static void Report(Action reporting)
  {
    ArgumentNullException.ThrowIfNull(reporting);
    try { reporting(); }
    catch (Exception) { RecordFailure(); }
  }

  public static void RecordFailure()
  {
    long observed;
    do { observed = FailureCount; if (observed == long.MaxValue) return; }
    while (Interlocked.CompareExchange(ref failureCount, observed + 1, observed) != observed);
  }

  private static void Dispatch(Action reporting)
  {
    if (dispatching) { RecordFailure(); return; }
    dispatching = true;
    try { Report(reporting); }
    finally { dispatching = false; }
  }

  // The caller continues to own/dispose the original sink. These facades own no resources.
  private class Plain(IDiagnostics sink) : IDiagnostics
  {
    public void Info(string message) => Dispatch(() => sink.Info(message));
    public void Warning(string message) => Dispatch(() => sink.Warning(message));
    public void Error(string message, Exception? exception = null) => Dispatch(() => sink.Error(message, exception));
  }

  private sealed class Structured(IStructuredDiagnostics sink) : Plain(sink), IStructuredDiagnostics
  {
    public void Info(string message, IReadOnlyDictionary<string, object?> properties) => Dispatch(() => sink.Info(message, properties));
    public void Warning(string message, IReadOnlyDictionary<string, object?> properties) => Dispatch(() => sink.Warning(message, properties));
    public void Error(string message, Exception? exception, IReadOnlyDictionary<string, object?> properties) =>
      Dispatch(() => sink.Error(message, exception, properties));
  }
}

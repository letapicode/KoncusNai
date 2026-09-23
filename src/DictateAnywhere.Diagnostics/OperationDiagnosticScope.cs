using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Diagnostics;

public sealed class OperationDiagnosticScope : IDisposable
{
  private readonly IDiagnostics diagnostics;
  private readonly Stopwatch stopwatch;
  private bool completed;
  private bool disposed;

  public OperationDiagnosticScope(
    IDiagnostics diagnostics,
    string operationName,
    string? operationId = null,
    string? parentOperationId = null,
    string? provider = null,
    string? model = null,
    string? runtime = null)
  {
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    OperationName = string.IsNullOrWhiteSpace(operationName) ? "operation" : operationName;
    OperationId = string.IsNullOrWhiteSpace(operationId) ? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) : operationId;
    ParentOperationId = parentOperationId;
    Provider = provider;
    Model = model;
    Runtime = runtime;
    CurrentStage = "init";
    Outcome = OperationOutcome.Started;
    stopwatch = Stopwatch.StartNew();
  }

  public string OperationId { get; }

  public string? ParentOperationId { get; }

  public string OperationName { get; }

  public string? Provider { get; }

  public string? Model { get; }

  public string? Runtime { get; }

  public string CurrentStage { get; private set; }

  public string Outcome { get; private set; }

  public TimeSpan Elapsed => stopwatch.Elapsed;

  public static OperationDiagnosticScope Begin(
    IDiagnostics diagnostics,
    string operationName,
    string? parentOperationId = null,
    string? provider = null,
    string? model = null,
    string? runtime = null,
    string? operationId = null,
    IReadOnlyDictionary<string, object?>? initialProperties = null)
  {
    OperationDiagnosticScope scope = new(
      diagnostics,
      operationName,
      operationId,
      parentOperationId,
      provider,
      model,
      runtime);

    scope.LogEvent("INFO", $"{scope.OperationName} started.", OperationOutcome.Started, exception: null, remediationCode: null, initialProperties);
    return scope;
  }

  public OperationDiagnosticScope BeginChild(
    string childOperationName,
    string? provider = null,
    string? model = null,
    string? runtime = null,
    IReadOnlyDictionary<string, object?>? initialProperties = null)
  {
    return Begin(
      diagnostics,
      childOperationName,
      parentOperationId: OperationId,
      provider: provider ?? Provider,
      model: model ?? Model,
      runtime: runtime ?? Runtime,
      operationId: null,
      initialProperties);
  }

  public void Stage(string stageName, IReadOnlyDictionary<string, object?>? properties = null)
  {
    if (completed || disposed)
    {
      return;
    }

    CurrentStage = string.IsNullOrWhiteSpace(stageName) ? "stage" : stageName;
    LogEvent("INFO", $"{OperationName} stage '{CurrentStage}' entered.", Outcome, exception: null, remediationCode: null, properties);
  }

  public void Complete(string? message = null, IReadOnlyDictionary<string, object?>? properties = null)
  {
    if (completed)
    {
      return;
    }

    stopwatch.Stop();
    completed = true;
    Outcome = OperationOutcome.Completed;

    string logMessage = string.IsNullOrWhiteSpace(message)
      ? $"{OperationName} completed in {Elapsed.TotalMilliseconds:F1} ms."
      : message;

    LogEvent("INFO", logMessage, Outcome, exception: null, remediationCode: null, properties);
  }

  public void Cancel(string? reason = null, IReadOnlyDictionary<string, object?>? properties = null)
  {
    if (completed)
    {
      return;
    }

    stopwatch.Stop();
    completed = true;
    Outcome = OperationOutcome.Cancelled;

    string logMessage = string.IsNullOrWhiteSpace(reason)
      ? $"{OperationName} cancelled after {Elapsed.TotalMilliseconds:F1} ms."
      : $"{OperationName} cancelled after {Elapsed.TotalMilliseconds:F1} ms: {reason}";

    LogEvent("INFO", logMessage, Outcome, exception: null, remediationCode: DiagnosticRemediationCodes.OperationCancelled, properties);
  }

  public void Fail(
    Exception? exception,
    string? message = null,
    string? remediationCode = null,
    string level = "ERROR",
    IReadOnlyDictionary<string, object?>? properties = null)
  {
    if (completed)
    {
      return;
    }

    stopwatch.Stop();
    completed = true;
    Outcome = OperationOutcome.Failed;

    string effectiveCode = remediationCode ?? DiagnosticRemediationCodes.GetRemediationCode(
      DiagnosticErrorClassifier.Classify(message ?? exception?.Message ?? string.Empty, exception),
      exception);

    string logMessage = string.IsNullOrWhiteSpace(message)
      ? $"{OperationName} failed after {Elapsed.TotalMilliseconds:F1} ms: {exception?.Message ?? "unspecified error"}"
      : message;

    LogEvent(level, logMessage, Outcome, exception, effectiveCode, properties);
  }

  public void Dispose()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    if (!completed)
    {
      Complete();
    }
  }

  private void LogEvent(
    string level,
    string message,
    string outcome,
    Exception? exception,
    string? remediationCode,
    IReadOnlyDictionary<string, object?>? additionalProperties)
  {
    Dictionary<string, object?> payload = new(StringComparer.Ordinal)
    {
      [DiagnosticPropertyKeys.OperationId] = OperationId,
      [DiagnosticPropertyKeys.Stage] = CurrentStage,
      [DiagnosticPropertyKeys.Outcome] = outcome,
      [DiagnosticPropertyKeys.DurationMs] = Math.Round(Elapsed.TotalMilliseconds, 2),
    };

    if (!string.IsNullOrWhiteSpace(ParentOperationId))
    {
      payload[DiagnosticPropertyKeys.ParentOperationId] = ParentOperationId;
    }

    if (!string.IsNullOrWhiteSpace(Provider))
    {
      payload[DiagnosticPropertyKeys.Provider] = Provider;
    }

    if (!string.IsNullOrWhiteSpace(Model))
    {
      payload[DiagnosticPropertyKeys.Model] = Model;
    }

    if (!string.IsNullOrWhiteSpace(Runtime))
    {
      payload[DiagnosticPropertyKeys.Runtime] = Runtime;
    }

    if (!string.IsNullOrWhiteSpace(remediationCode))
    {
      payload[DiagnosticPropertyKeys.RemediationCode] = remediationCode;
    }

    if (additionalProperties is not null)
    {
      foreach ((string key, object? value) in additionalProperties)
      {
        if (!payload.ContainsKey(key))
        {
          payload[key] = value;
        }
      }
    }

    if (diagnostics is IStructuredDiagnostics structuredDiagnostics)
    {
      if (string.Equals(level, "ERROR", StringComparison.OrdinalIgnoreCase))
      {
        structuredDiagnostics.Error(message, exception, payload);
      }
      else if (string.Equals(level, "WARN", StringComparison.OrdinalIgnoreCase))
      {
        structuredDiagnostics.Warning(message, payload);
      }
      else
      {
        structuredDiagnostics.Info(message, payload);
      }

      return;
    }

    if (string.Equals(level, "ERROR", StringComparison.OrdinalIgnoreCase))
    {
      diagnostics.Error(message, exception);
    }
    else if (string.Equals(level, "WARN", StringComparison.OrdinalIgnoreCase))
    {
      diagnostics.Warning(message);
    }
    else
    {
      diagnostics.Info(message);
    }
  }
}

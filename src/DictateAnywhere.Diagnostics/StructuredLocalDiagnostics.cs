using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;

namespace DictateAnywhere.Diagnostics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
  Justification = "Formatting/write/release failures are diagnostics-only; invalid configuration remains a constructor error.")]
public sealed class StructuredLocalDiagnostics : IStructuredDiagnostics, IDisposable
{
  private readonly object sync = new();
  private readonly StructuredDiagnosticsOptions options;
  private readonly JsonSerializerOptions jsonOptions;

  private readonly IDiagnosticLogStorage storage;
  private Stream? logStream;
  private StreamWriter? logWriter;
  private string currentLogPath = string.Empty;
  private int fileSequence;
  private bool disposed;
  private bool degraded;
  private bool writing;
  [ThreadStatic] private static bool preparing;

  public StructuredLocalDiagnostics()
    : this(StructuredDiagnosticsOptions.Default)
  {
  }

  public StructuredLocalDiagnostics(StructuredDiagnosticsOptions options)
    : this(options, new DiagnosticLogStorage()) { }

  internal StructuredLocalDiagnostics(StructuredDiagnosticsOptions options, IDiagnosticLogStorage storage)
  {
    this.options = options ?? throw new ArgumentNullException(nameof(options));
    ValidateOptions(this.options);
    _ = Path.GetFullPath(this.options.LogsDirectoryPath);
    if (this.options.FileNamePrefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
      throw new ArgumentException("Invalid log file prefix.", nameof(options));

    this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
    jsonOptions = new JsonSerializerOptions
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    try { storage.CreateDirectory(this.options.LogsDirectoryPath); RotateCore(); }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
    { DisableWriter(); }
    catch { ReleaseWriter(); throw; }
  }

  public string LogsDirectoryPath => options.LogsDirectoryPath;

  public string CurrentLogPath
  {
    get
    {
      lock (sync)
      {
        return currentLogPath;
      }
    }
  }

  public void Info(string message)
  {
    Write("INFO", message, exception: null, properties: null);
  }

  public void Info(string message, IReadOnlyDictionary<string, object?> properties)
  {
    Write("INFO", message, exception: null, properties);
  }

  public void Warning(string message)
  {
    Write("WARN", message, exception: null, properties: null);
  }

  public void Warning(string message, IReadOnlyDictionary<string, object?> properties)
  {
    Write("WARN", message, exception: null, properties);
  }

  public void Error(string message, Exception? exception = null)
  {
    Write("ERROR", message, exception, properties: null);
  }

  public void Error(
    string message,
    Exception? exception,
    IReadOnlyDictionary<string, object?> properties)
  {
    Write("ERROR", message, exception, properties);
  }

  public void Dispose()
  {
    lock (sync)
    {
      disposed = true;
      // Reentrant disposal from an owned writer callback is deferred until its write unwinds.
      if (!writing) ReleaseWriter();
    }
  }

  private void ReleaseWriter()
  {
    StreamWriter? writer = logWriter;
    Stream? stream = logStream;
    logWriter = null;
    logStream = null; // Detach before external release: reentrant/late calls cannot resurrect ownership.
    DiagnosticBoundary.Report(() => writer?.Dispose());
    DiagnosticBoundary.Report(() => stream?.Dispose());
  }

  private void DisableWriter()
  {
    degraded = true; // No automatic retry per message, even if the filesystem recovers.
    DiagnosticBoundary.RecordFailure();
    ReleaseWriter();
  }

  private static void ValidateOptions(StructuredDiagnosticsOptions options)
  {
    if (string.IsNullOrWhiteSpace(options.LogsDirectoryPath))
    {
      throw new ArgumentException("Logs directory must not be empty.", nameof(options));
    }

    if (string.IsNullOrWhiteSpace(options.FileNamePrefix))
    {
      throw new ArgumentException("File name prefix must not be empty.", nameof(options));
    }

    if (options.MaxFileSizeBytes < 1024)
    {
      throw new ArgumentOutOfRangeException(nameof(options), "Max file size must be at least 1024 bytes.");
    }

    if (options.RetainedFileCount < 1)
    {
      throw new ArgumentOutOfRangeException(nameof(options), "Retained file count must be at least 1.");
    }
  }

  private void Write(
    string level,
    string message,
    Exception? exception,
    IReadOnlyDictionary<string, object?>? properties)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(message);
    lock (sync) { if (disposed || degraded) return; }
    if (preparing) { DiagnosticBoundary.RecordFailure(); return; }
    string? jsonLine = null;
    preparing = true;
    try { DiagnosticBoundary.Report(() => jsonLine = PrepareRecord(level, message, exception, properties)); }
    finally { preparing = false; }
    if (jsonLine is null) return; // Fail closed: never emit a partially sanitized/raw record.
    lock (sync)
    {
      if (disposed || degraded) return;
      if (writing) { DiagnosticBoundary.RecordFailure(); return; }
      writing = true;
      try
      {
        EnsureWriter();
        if (logStream!.Length >= options.MaxFileSizeBytes) RotateCore();
        logWriter!.WriteLine(jsonLine);
      }
      catch (Exception) { DisableWriter(); }
      finally { writing = false; if (disposed) ReleaseWriter(); }
    }
  }

  private string PrepareRecord(string level, string message, Exception? exception, IReadOnlyDictionary<string, object?>? properties)
  {
    if (message.Length > options.MaxFileSizeBytes) throw new InvalidDataException("Oversized diagnostic record.");
    properties = SanitizeProperties(properties, sanitize: false); // Snapshot/validate outside the writer lock.
    ExceptionDiagnosticMetadata exceptionMetadata = ExceptionDiagnosticMetadataExtractor.Extract(exception);
    DiagnosticFailureCategory category = DiagnosticErrorClassifier.Classify(message, exception);
    string safeMessage = Sanitize(message);
    string? exceptionText = null;
    if (exception is not null)
    {
      exceptionText = ExceptionDiagnosticMetadataExtractor.ShouldPreferDiagnosticSummary(exceptionMetadata)
        ? exceptionMetadata.DiagnosticSummary
        : exceptionMetadata.Message;
    }

    string? safeExceptionMessage = string.IsNullOrWhiteSpace(exceptionText) ? null : Sanitize(exceptionText);
    string? safeExceptionStackTrace = string.IsNullOrWhiteSpace(exception?.StackTrace)
      ? null
      : Sanitize(exception.StackTrace!);

    string? operationId = TryGetStringProperty(properties, DiagnosticPropertyKeys.OperationId)
      ?? TryGetStringProperty(properties, "correlationId")
      ?? TryGetStringProperty(properties, "operation_id");

    string? parentOperationId = TryGetStringProperty(properties, DiagnosticPropertyKeys.ParentOperationId)
      ?? TryGetStringProperty(properties, "parent_operation_id");

    string? stage = TryGetStringProperty(properties, DiagnosticPropertyKeys.Stage);
    string? outcome = TryGetStringProperty(properties, DiagnosticPropertyKeys.Outcome);
    double? durationMs = TryGetDoubleProperty(properties, DiagnosticPropertyKeys.DurationMs)
      ?? TryGetDoubleProperty(properties, "totalMs")
      ?? TryGetDoubleProperty(properties, "duration_ms");

    string? remediationCode = TryGetStringProperty(properties, DiagnosticPropertyKeys.RemediationCode);
    if (string.IsNullOrWhiteSpace(remediationCode) && (category != DiagnosticFailureCategory.Unknown || exception is not null || string.Equals(level, "ERROR", StringComparison.OrdinalIgnoreCase)))
    {
      remediationCode = DiagnosticRemediationCodes.GetRemediationCode(category, exception);
    }

    LogEntry entry = new(
      TimestampUtc: DateTimeOffset.UtcNow,
      Level: level,
      Category: category.ToString(),
      Message: safeMessage,
      ExceptionType: string.IsNullOrWhiteSpace(exceptionMetadata.TypeName) ? null : exceptionMetadata.TypeName,
      ExceptionMessage: safeExceptionMessage,
      ExceptionStackTrace: safeExceptionStackTrace,
      Properties: SanitizeProperties(properties),
      OperationId: operationId,
      ParentOperationId: parentOperationId,
      Stage: stage,
      Outcome: outcome,
      DurationMs: durationMs,
      RemediationCode: remediationCode);

    string jsonLine = JsonSerializer.Serialize(entry, jsonOptions);
    if (Encoding.UTF8.GetByteCount(jsonLine) > options.MaxFileSizeBytes) throw new InvalidDataException("Oversized diagnostic record.");
    return jsonLine;
  }

  private string Sanitize(string text)
  {
    return options.IncludeSensitiveData
      ? text
      : SensitiveDiagnosticsRedactor.Redact(text);
  }

  private IReadOnlyDictionary<string, object?>? SanitizeProperties(
    IReadOnlyDictionary<string, object?>? properties, bool sanitize = true)
  {
    if (properties is null || properties.Count == 0)
    {
      return null;
    }

    if (properties.Count > 256) throw new InvalidDataException("Too many diagnostic properties.");
    Dictionary<string, object?> sanitized = new(StringComparer.Ordinal);
    foreach ((string key, object? value) in properties)
    {
      if (sanitized.Count >= 256 || key.Length > 1024 || value is string large && large.Length > options.MaxFileSizeBytes)
        throw new InvalidDataException("Oversized diagnostic property.");
      if (value is not (null or string or bool or char or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or DateTime or DateTimeOffset or Guid or Enum))
        throw new InvalidDataException("Only scalar diagnostic properties are supported.");
      if (!sanitize) { sanitized[key] = value; }
      else if (!options.IncludeSensitiveData && SensitiveDiagnosticsRedactor.IsSensitiveKey(key))
      {
        sanitized[key] = IsSafeTimingMeasurement(key, value)
          ? value
          : "[REDACTED]";
      }
      else
      {
        sanitized[key] = value is string text
          ? Sanitize(text)
          : value;
      }
    }

    return sanitized;
  }

  private static bool IsSafeTimingMeasurement(string key, object? value)
  {
    return key.Equals("transcriptionWallMs", StringComparison.OrdinalIgnoreCase) &&
           value is double or float or decimal or byte or sbyte or short or ushort or int or uint or long or ulong;
  }

  private void EnsureWriter()
  {
    if (logWriter is null || logStream is null)
    {
      RotateCore();
    }
  }

  private void RotateCore()
  {
    ReleaseWriter();
    currentLogPath = BuildNextLogPath();
    logStream = storage.Open(currentLogPath);
    logWriter = new StreamWriter(logStream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true);
    logWriter.AutoFlush = true;
    ApplyRetentionPolicy();
  }

  private string BuildNextLogPath()
  {
    fileSequence++;
    string fileName = string.Concat(
      options.FileNamePrefix,
      "-",
      DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture),
      "-",
      fileSequence.ToString("D4", CultureInfo.InvariantCulture),
      ".log");
    return Path.Combine(options.LogsDirectoryPath, fileName);
  }

  private void ApplyRetentionPolicy()
  {
    string pattern = string.Concat(options.FileNamePrefix, "-*.log");
    string[] files = storage.GetFiles(options.LogsDirectoryPath, pattern);
    if (files.Length <= options.RetainedFileCount)
    {
      return;
    }

    // Read filesystem metadata before sorting: List.Sort wraps comparator I/O faults as contract errors.
    List<(string Path, DateTime LastWrite)> timestamps = new(files.Length);
    foreach (string file in files) timestamps.Add((file, storage.GetLastWriteTimeUtc(file)));
    timestamps.Sort(static (left, right) => right.LastWrite.CompareTo(left.LastWrite));
    List<string> sorted = timestamps.ConvertAll(static item => item.Path);

    sorted.RemoveAll(candidate =>
      string.Equals(candidate, currentLogPath, StringComparison.OrdinalIgnoreCase));

    int retainedArchiveCount = Math.Max(options.RetainedFileCount - 1, 0);
    for (int i = retainedArchiveCount; i < sorted.Count; i++)
    {
      string candidate = sorted[i];
      try
      {
        storage.Delete(candidate);
      }
      catch (IOException)
      {
        DiagnosticBoundary.RecordFailure();
      }
      catch (UnauthorizedAccessException)
      {
        DiagnosticBoundary.RecordFailure();
      }
    }
  }

  private static string? TryGetStringProperty(IReadOnlyDictionary<string, object?>? properties, string key)
  {
    if (properties is not null && properties.TryGetValue(key, out object? value) && value is not null)
    {
      return value.ToString();
    }

    return null;
  }

  private static double? TryGetDoubleProperty(IReadOnlyDictionary<string, object?>? properties, string key)
  {
    if (properties is not null && properties.TryGetValue(key, out object? value) && value is not null)
    {
      if (value is double d) return d;
      if (value is float f) return (double)f;
      if (value is int i) return (double)i;
      if (value is long l) return (double)l;
      if (double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
      {
        return parsed;
      }
    }

    return null;
  }

  private sealed record LogEntry(
    DateTimeOffset TimestampUtc,
    string Level,
    string Category,
    string Message,
    string? ExceptionType,
    string? ExceptionMessage,
    string? ExceptionStackTrace,
    IReadOnlyDictionary<string, object?>? Properties,
    string? OperationId = null,
    string? ParentOperationId = null,
    string? Stage = null,
    string? Outcome = null,
    double? DurationMs = null,
    string? RemediationCode = null);
}

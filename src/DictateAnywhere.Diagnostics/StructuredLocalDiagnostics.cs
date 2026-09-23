using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Diagnostics;

public sealed class StructuredLocalDiagnostics : IStructuredDiagnostics, IDisposable
{
  private readonly object sync = new();
  private readonly StructuredDiagnosticsOptions options;
  private readonly JsonSerializerOptions jsonOptions;

  private FileStream? logStream;
  private StreamWriter? logWriter;
  private string currentLogPath = string.Empty;
  private int fileSequence;
  private bool disposed;

  public StructuredLocalDiagnostics()
    : this(StructuredDiagnosticsOptions.Default)
  {
  }

  public StructuredLocalDiagnostics(StructuredDiagnosticsOptions options)
  {
    this.options = options ?? throw new ArgumentNullException(nameof(options));
    ValidateOptions(this.options);

    Directory.CreateDirectory(this.options.LogsDirectoryPath);
    jsonOptions = new JsonSerializerOptions
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    RotateCore();
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
    if (disposed)
    {
      return;
    }

    disposed = true;
    lock (sync)
    {
      logWriter?.Flush();
      logWriter?.Dispose();
      logWriter = null;

      logStream?.Dispose();
      logStream = null;
    }
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
    ObjectDisposedException.ThrowIf(disposed, this);

    lock (sync)
    {
      EnsureWriter();

      if (logStream is not null && logStream.Length >= options.MaxFileSizeBytes)
      {
        RotateCore();
      }

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
      logWriter!.WriteLine(jsonLine);
    }
  }

  private string Sanitize(string text)
  {
    return options.IncludeSensitiveData
      ? text
      : SensitiveDiagnosticsRedactor.Redact(text);
  }

  private IReadOnlyDictionary<string, object?>? SanitizeProperties(
    IReadOnlyDictionary<string, object?>? properties)
  {
    if (properties is null || properties.Count == 0)
    {
      return null;
    }

    Dictionary<string, object?> sanitized = new(properties.Count, StringComparer.Ordinal);
    foreach ((string key, object? value) in properties)
    {
      if (!options.IncludeSensitiveData && SensitiveDiagnosticsRedactor.IsSensitiveKey(key))
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
    logWriter?.Flush();
    logWriter?.Dispose();
    logWriter = null;

    logStream?.Dispose();
    logStream = null;

    currentLogPath = BuildNextLogPath();
    logStream = new FileStream(currentLogPath, FileMode.Create, FileAccess.Write, FileShare.Read);
    logWriter = new StreamWriter(logStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
    {
      AutoFlush = true,
    };

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
    string[] files = Directory.GetFiles(options.LogsDirectoryPath, pattern, SearchOption.TopDirectoryOnly);
    if (files.Length <= options.RetainedFileCount)
    {
      return;
    }

    List<string> sorted = new(files);
    sorted.Sort(static (left, right) =>
      File.GetLastWriteTimeUtc(right).CompareTo(File.GetLastWriteTimeUtc(left)));

    sorted.RemoveAll(candidate =>
      string.Equals(candidate, currentLogPath, StringComparison.OrdinalIgnoreCase));

    int retainedArchiveCount = Math.Max(options.RetainedFileCount - 1, 0);
    for (int i = retainedArchiveCount; i < sorted.Count; i++)
    {
      string candidate = sorted[i];
      try
      {
        File.Delete(candidate);
      }
      catch (IOException)
      {
      }
      catch (UnauthorizedAccessException)
      {
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

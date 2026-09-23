using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace DictateAnywhere.Diagnostics;

public sealed record StageTimelineEvent(
  DateTimeOffset TimestampUtc,
  string Stage,
  string Outcome,
  double? DurationMs,
  string Level,
  string Message,
  string? RemediationCode,
  IReadOnlyDictionary<string, string?> Properties);

public sealed record ReconstructedOperationTimeline(
  string OperationId,
  string? ParentOperationId,
  string? Provider,
  string? Model,
  string? Runtime,
  string FinalOutcome,
  string? FinalRemediationCode,
  double? TotalDurationMs,
  IReadOnlyList<StageTimelineEvent> Stages,
  IReadOnlyList<string> LogMessages);

public sealed class DiagnosticTimelineReconstructor
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
  };

  public static IReadOnlyList<ReconstructedOperationTimeline> ReconstructFromBundle(string zipFilePath)
  {
    if (string.IsNullOrWhiteSpace(zipFilePath) || !File.Exists(zipFilePath))
    {
      throw new FileNotFoundException("Diagnostics bundle zip file was not found.", zipFilePath);
    }

    List<string> lines = new();
    using ZipArchive archive = ZipFile.OpenRead(zipFilePath);
    foreach (ZipArchiveEntry entry in archive.Entries)
    {
      if (entry.FullName.StartsWith("logs/", StringComparison.OrdinalIgnoreCase) &&
          entry.FullName.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
      {
        using StreamReader reader = new(entry.Open());
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
          if (!string.IsNullOrWhiteSpace(line))
          {
            lines.Add(line);
          }
        }
      }
    }

    return ReconstructFromLogLines(lines);
  }

  public static IReadOnlyList<ReconstructedOperationTimeline> ReconstructFromDirectory(string logsDirectoryPath)
  {
    if (string.IsNullOrWhiteSpace(logsDirectoryPath) || !Directory.Exists(logsDirectoryPath))
    {
      return Array.Empty<ReconstructedOperationTimeline>();
    }

    List<string> lines = new();
    string[] files = Directory.GetFiles(logsDirectoryPath, "*.log", SearchOption.TopDirectoryOnly);
    Array.Sort(files, StringComparer.OrdinalIgnoreCase);

    foreach (string file in files)
    {
      foreach (string line in File.ReadLines(file))
      {
        if (!string.IsNullOrWhiteSpace(line))
        {
          lines.Add(line);
        }
      }
    }

    return ReconstructFromLogLines(lines);
  }

  public static IReadOnlyList<ReconstructedOperationTimeline> ReconstructFromLogLines(IEnumerable<string> jsonLines)
  {
    ArgumentNullException.ThrowIfNull(jsonLines);

    Dictionary<string, List<ParsedLogRecord>> grouped = new(StringComparer.Ordinal);
    List<ParsedLogRecord> unassociated = new();

    foreach (string line in jsonLines)
    {
      if (string.IsNullOrWhiteSpace(line))
      {
        continue;
      }

      ParsedLogRecord? record = TryParseLine(line);
      if (record is null)
      {
        continue;
      }

      if (!string.IsNullOrWhiteSpace(record.OperationId))
      {
        if (!grouped.TryGetValue(record.OperationId, out List<ParsedLogRecord>? list))
        {
          list = new List<ParsedLogRecord>();
          grouped[record.OperationId] = list;
        }

        list.Add(record);
      }
      else
      {
        unassociated.Add(record);
      }
    }

    List<ReconstructedOperationTimeline> timelines = new(grouped.Count);
    foreach ((string operationId, List<ParsedLogRecord> records) in grouped)
    {
      records.Sort(static (a, b) => a.TimestampUtc.CompareTo(b.TimestampUtc));

      string? parentOperationId = records.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.ParentOperationId))?.ParentOperationId;
      string? provider = records.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Provider))?.Provider;
      string? model = records.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Model))?.Model;
      string? runtime = records.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Runtime))?.Runtime;

      ParsedLogRecord lastRecord = records[^1];
      string finalOutcome = records.LastOrDefault(r => !string.IsNullOrWhiteSpace(r.Outcome))?.Outcome ?? OperationOutcome.Completed;
      string? finalRemediationCode = records.LastOrDefault(r => !string.IsNullOrWhiteSpace(r.RemediationCode))?.RemediationCode;

      double? maxDuration = null;
      foreach (ParsedLogRecord r in records)
      {
        if (r.DurationMs.HasValue)
        {
          maxDuration = Math.Max(maxDuration ?? 0, r.DurationMs.Value);
        }
      }

      List<StageTimelineEvent> stages = new(records.Count);
      List<string> messages = new(records.Count);

      foreach (ParsedLogRecord r in records)
      {
        messages.Add(r.Message);
        stages.Add(new StageTimelineEvent(
          r.TimestampUtc,
          r.Stage ?? "unknown",
          r.Outcome ?? OperationOutcome.Started,
          r.DurationMs,
          r.Level,
          r.Message,
          r.RemediationCode,
          r.Properties));
      }

      timelines.Add(new ReconstructedOperationTimeline(
        operationId,
        parentOperationId,
        provider,
        model,
        runtime,
        finalOutcome,
        finalRemediationCode,
        maxDuration,
        stages,
        messages));
    }

    return timelines;
  }

  public static void AssertNoSensitiveData(string bundleZipPath, IReadOnlyList<string> forbiddenSubstrings)
  {
    ArgumentNullException.ThrowIfNull(forbiddenSubstrings);
    using ZipArchive archive = ZipFile.OpenRead(bundleZipPath);
    foreach (ZipArchiveEntry entry in archive.Entries)
    {
      using StreamReader reader = new(entry.Open());
      string content = reader.ReadToEnd();
      foreach (string forbidden in forbiddenSubstrings)
      {
        if (string.IsNullOrWhiteSpace(forbidden))
        {
          continue;
        }

        if (content.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0)
        {
          throw new InvalidOperationException(
            $"Diagnostics bundle entry '{entry.FullName}' contained unredacted sensitive token: '{forbidden}'.");
        }
      }
    }
  }

  private static ParsedLogRecord? TryParseLine(string line)
  {
    try
    {
      using JsonDocument doc = JsonDocument.Parse(line);
      JsonElement root = doc.RootElement;

      DateTimeOffset timestamp = root.TryGetProperty("timestampUtc", out JsonElement ts) && ts.TryGetDateTimeOffset(out DateTimeOffset dto)
        ? dto
        : DateTimeOffset.UtcNow;

      string level = root.TryGetProperty("level", out JsonElement lvl) ? lvl.GetString() ?? "INFO" : "INFO";
      string message = root.TryGetProperty("message", out JsonElement msg) ? msg.GetString() ?? string.Empty : string.Empty;

      string? operationId = GetStringOrNull(root, "operationId");
      string? parentOperationId = GetStringOrNull(root, "parentOperationId");
      string? stage = GetStringOrNull(root, "stage");
      string? outcome = GetStringOrNull(root, "outcome");
      double? durationMs = root.TryGetProperty("durationMs", out JsonElement dur) && dur.ValueKind == JsonValueKind.Number && dur.TryGetDouble(out double d) ? d : null;
      string? remediationCode = GetStringOrNull(root, "remediationCode");

      Dictionary<string, string?> properties = new(StringComparer.OrdinalIgnoreCase);
      string? provider = null;
      string? model = null;
      string? runtime = null;

      if (root.TryGetProperty("properties", out JsonElement propsElement) && propsElement.ValueKind == JsonValueKind.Object)
      {
        foreach (JsonProperty prop in propsElement.EnumerateObject())
        {
          string valStr = prop.Value.ToString();
          properties[prop.Name] = valStr;

          if (string.Equals(prop.Name, DiagnosticPropertyKeys.OperationId, StringComparison.OrdinalIgnoreCase) && operationId is null)
          {
            operationId = valStr;
          }
          else if (string.Equals(prop.Name, "correlationId", StringComparison.OrdinalIgnoreCase) && operationId is null)
          {
            operationId = valStr;
          }
          else if (string.Equals(prop.Name, DiagnosticPropertyKeys.ParentOperationId, StringComparison.OrdinalIgnoreCase) && parentOperationId is null)
          {
            parentOperationId = valStr;
          }
          else if (string.Equals(prop.Name, DiagnosticPropertyKeys.Stage, StringComparison.OrdinalIgnoreCase) && stage is null)
          {
            stage = valStr;
          }
          else if (string.Equals(prop.Name, DiagnosticPropertyKeys.Outcome, StringComparison.OrdinalIgnoreCase) && outcome is null)
          {
            outcome = valStr;
          }
          else if (string.Equals(prop.Name, DiagnosticPropertyKeys.RemediationCode, StringComparison.OrdinalIgnoreCase) && remediationCode is null)
          {
            remediationCode = valStr;
          }
          else if (string.Equals(prop.Name, DiagnosticPropertyKeys.Provider, StringComparison.OrdinalIgnoreCase))
          {
            provider = valStr;
          }
          else if (string.Equals(prop.Name, DiagnosticPropertyKeys.Model, StringComparison.OrdinalIgnoreCase))
          {
            model = valStr;
          }
          else if (string.Equals(prop.Name, DiagnosticPropertyKeys.Runtime, StringComparison.OrdinalIgnoreCase))
          {
            runtime = valStr;
          }
        }
      }

      return new ParsedLogRecord(
        timestamp,
        level,
        message,
        operationId,
        parentOperationId,
        stage,
        outcome,
        durationMs,
        remediationCode,
        provider,
        model,
        runtime,
        properties);
    }
    catch (JsonException)
    {
      return null;
    }
  }

  private static string? GetStringOrNull(JsonElement element, string propertyName)
  {
    if (element.TryGetProperty(propertyName, out JsonElement prop) && prop.ValueKind == JsonValueKind.String)
    {
      return prop.GetString();
    }

    return null;
  }

  private sealed record ParsedLogRecord(
    DateTimeOffset TimestampUtc,
    string Level,
    string Message,
    string? OperationId,
    string? ParentOperationId,
    string? Stage,
    string? Outcome,
    double? DurationMs,
    string? RemediationCode,
    string? Provider,
    string? Model,
    string? Runtime,
    IReadOnlyDictionary<string, string?> Properties);
}

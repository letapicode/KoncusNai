using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DictateAnywhere.Diagnostics;

/// <summary>Projects application logs to fields whose values do not come from free-form user content.</summary>
internal static class SafeLogExportProjector
{
  private static readonly string[] RetryOutcomes =
  [
    "None", "NoHistory", "Expired", "EmptyText", "Inserted", "InsertionFailed"
  ];

  internal static string? Project(string line)
  {
    try
    {
      using JsonDocument document = JsonDocument.Parse(line);
      JsonElement root = document.RootElement;
      if (root.ValueKind != JsonValueKind.Object) return null;

      JsonObject safe = new();
      CopyDate(root, safe, "timestampUtc");
      CopyEnum(root, safe, "level", ["INFO", "WARN", "ERROR"]);
      CopyEnum(root, safe, "category", Enum.GetNames<DiagnosticFailureCategory>());
      CopyNumber(root, safe, "durationMs");

      if (root.TryGetProperty("message", out JsonElement message)
          && message.ValueKind == JsonValueKind.String
          && message.GetString() == "Retry last dictation completed.")
      {
        safe["event"] = "retryLastDictation";
        if (root.TryGetProperty("properties", out JsonElement properties)
            && properties.ValueKind == JsonValueKind.Object)
        {
          JsonObject retry = new();
          CopyEnum(properties, retry, "outcome", RetryOutcomes);
          if (properties.TryGetProperty("succeeded", out JsonElement succeeded)
              && succeeded.ValueKind is JsonValueKind.True or JsonValueKind.False)
          {
            retry["succeeded"] = succeeded.GetBoolean();
          }
          safe["properties"] = retry;
        }
      }

      return safe.ToJsonString();
    }
    catch (JsonException)
    {
      return null;
    }
  }

  private static void CopyDate(JsonElement source, JsonObject target, string name)
  {
    if (source.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
        && value.TryGetDateTimeOffset(out DateTimeOffset timestamp))
    {
      target[name] = timestamp;
    }
  }

  private static void CopyEnum(JsonElement source, JsonObject target, string name, string[] allowed)
  {
    if (!source.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String) return;
    string? text = value.GetString();
    if (text is not null && Array.IndexOf(allowed, text) >= 0) target[name] = text;
  }

  private static void CopyNumber(JsonElement source, JsonObject target, string name)
  {
    if (source.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out double number)
        && double.IsFinite(number) && number >= 0)
    {
      target[name] = number;
    }
  }
}

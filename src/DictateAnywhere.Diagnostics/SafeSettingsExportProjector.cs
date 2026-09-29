using System;
using System.Text.Json.Nodes;

namespace DictateAnywhere.Diagnostics;

/// <summary>Exports only known non-identifying settings and drops unknown future fields.</summary>
internal static class SafeSettingsExportProjector
{
  internal static JsonObject Project(JsonObject input)
  {
    JsonObject safe = new();
    CopyInteger(input, safe, "schemaVersion", 0, 100);
    CopyInteger(input, safe, "lastDictationRetryWindowSeconds", 0, 86400);
    CopyInteger(input, safe, "workbenchZoomPercent", 50, 300);
    CopyBoolean(input, safe, "hasCompletedFirstRun");
    CopyBoolean(input, safe, "overlayEnabled");
    CopyBoolean(input, safe, "enableDictationCommands");
    CopyEnum(input, safe, "themePreference", ["Dark", "Light", "System"]);
    return safe;
  }

  private static void CopyInteger(JsonObject source, JsonObject target, string name, int minimum, int maximum)
  {
    if (source.TryGetPropertyValue(name, out JsonNode? node)
        && node is JsonValue value && value.TryGetValue<int>(out int number)
        && number >= minimum && number <= maximum)
    {
      target[name] = number;
    }
  }

  private static void CopyBoolean(JsonObject source, JsonObject target, string name)
  {
    if (source.TryGetPropertyValue(name, out JsonNode? node)
        && node is JsonValue value && value.TryGetValue<bool>(out bool enabled))
    {
      target[name] = enabled;
    }
  }

  private static void CopyEnum(JsonObject source, JsonObject target, string name, string[] allowed)
  {
    if (source.TryGetPropertyValue(name, out JsonNode? node)
        && node is JsonValue value && value.TryGetValue<string>(out string? text)
        && text is not null && Array.IndexOf(allowed, text) >= 0)
    {
      target[name] = text;
    }
  }
}

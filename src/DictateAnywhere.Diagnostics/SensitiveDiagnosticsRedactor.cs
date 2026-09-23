using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DictateAnywhere.Diagnostics;

public static class SensitiveDiagnosticsRedactor
{
  private static readonly string[] TranscriptMarkers =
  {
    "transcript",
    "transcription",
    "dictated text",
    "recognized text",
    "prompt",
    "chat prompt",
    "user prompt",
    "user input",
    "user message",
    "assistant message",
    "completion text",
    "document text",
    "section text",
    "chat transcript",
    "raw transcript",
    "dictation text",
    "history record",
  };

  private static readonly string[] AudioMarkers =
  {
    "audio",
    "pcm",
    "wav",
    "wave",
    "samples",
    "bytes",
    "audio chunk",
    "audio payload",
    "audio data",
    "audio buffer",
  };

  private static readonly string[] AuthMarkers =
  {
    "bearer",
    "authorization",
    "api_key",
    "apikey",
    "api-key",
    "password",
    "secret",
    "credential",
    "client_secret",
    "clientsecret",
    "access_token",
    "refresh_token",
    "private_key",
    "privatekey",
    "auth_key",
    "authkey",
    "token",
    "verifier",
    "salt",
  };

  public static bool IsSensitiveKey(string name)
  {
    if (string.IsNullOrWhiteSpace(name))
    {
      return false;
    }

    foreach (string marker in AuthMarkers)
    {
      if (name.Contains(marker, StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }

    foreach (string marker in TranscriptMarkers)
    {
      if (name.Contains(marker, StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }

    foreach (string marker in AudioMarkers)
    {
      if (name.Contains(marker, StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }

    return false;
  }

  public static string Redact(string input)
  {
    if (string.IsNullOrWhiteSpace(input))
    {
      return input;
    }

    string trimmed = input.Trim();
    if ((trimmed.StartsWith('{') && trimmed.EndsWith('}')) || (trimmed.StartsWith('[') && trimmed.EndsWith(']')))
    {
      try
      {
        JsonNode? node = JsonNode.Parse(input);
        if (node is not null)
        {
          SanitizeJsonNode(node);
          return node.ToJsonString();
        }
      }
      catch (JsonException)
      {
        // Fall back to plain text redaction if JSON parsing fails
      }
    }

    return RedactPlainText(input);
  }

  private static void SanitizeJsonNode(JsonNode node)
  {
    if (node is JsonObject jsonObject)
    {
      foreach ((string key, JsonNode? value) in jsonObject.ToArray())
      {
        if (IsSensitiveKey(key))
        {
          jsonObject[key] = "[REDACTED]";
        }
        else if (value is JsonValue jsonVal && jsonVal.TryGetValue(out string? strVal))
        {
          jsonObject[key] = RedactPlainText(strVal);
        }
        else if (value is not null)
        {
          SanitizeJsonNode(value);
        }
      }
    }
    else if (node is JsonArray jsonArray)
    {
      for (int i = 0; i < jsonArray.Count; i++)
      {
        JsonNode? item = jsonArray[i];
        if (item is JsonValue jsonVal && jsonVal.TryGetValue(out string? strVal))
        {
          jsonArray[i] = RedactPlainText(strVal);
        }
        else if (item is not null)
        {
          SanitizeJsonNode(item);
        }
      }
    }
  }

  public static string RedactPlainText(string input)
  {
    if (string.IsNullOrWhiteSpace(input))
    {
      return input;
    }

    if (LooksLikeBinaryPayload(input))
    {
      return "[REDACTED SENSITIVE PAYLOAD]";
    }

    string current = input;

    current = RedactBearerTokens(current);
    current = RedactAllMarkers(current, AuthMarkers);
    current = RedactAllMarkers(current, TranscriptMarkers);
    current = RedactAllMarkers(current, AudioMarkers);

    return current;
  }

  private static string RedactBearerTokens(string input)
  {
    const string bearerPrefix = "Bearer ";
    int searchStart = 0;
    StringBuilder? sb = null;

    while (searchStart < input.Length)
    {
      int index = input.IndexOf(bearerPrefix, searchStart, StringComparison.OrdinalIgnoreCase);
      if (index < 0)
      {
        break;
      }

      sb ??= new StringBuilder();
      sb.Append(input, searchStart, index - searchStart);
      sb.Append("Bearer [REDACTED]");

      int tokenStart = index + bearerPrefix.Length;
      int tokenEnd = tokenStart;
      while (tokenEnd < input.Length && !char.IsWhiteSpace(input[tokenEnd]) && input[tokenEnd] != ',' && input[tokenEnd] != ';' && input[tokenEnd] != '"')
      {
        tokenEnd++;
      }

      searchStart = tokenEnd;
    }

    if (sb is null)
    {
      return input;
    }

    sb.Append(input, searchStart, input.Length - searchStart);
    return sb.ToString();
  }

  private static string RedactAllMarkers(string input, string[] markers)
  {
    string result = input;
    foreach (string marker in markers)
    {
      result = RedactMarker(result, marker);
    }
    return result;
  }

  private static string RedactMarker(string input, string marker)
  {
    int searchStart = 0;
    StringBuilder? sb = null;

    while (searchStart < input.Length)
    {
      int markerIndex = input.IndexOf(marker, searchStart, StringComparison.OrdinalIgnoreCase);
      if (markerIndex < 0)
      {
        break;
      }

      int separatorIndex = FindImmediateSeparatorIndex(input, markerIndex + marker.Length);
      if (separatorIndex >= 0)
      {
        sb ??= new StringBuilder();
        sb.Append(input, searchStart, separatorIndex + 1 - searchStart);
        sb.Append(" [REDACTED]");

        int valStart = separatorIndex + 1;
        while (valStart < input.Length && char.IsWhiteSpace(input[valStart]))
        {
          valStart++;
        }

        if (valStart < input.Length && input.AsSpan(valStart).StartsWith("[REDACTED]", StringComparison.OrdinalIgnoreCase))
        {
          valStart += 10;
        }

        int valEnd = FindSensitiveValueEnd(input, valStart);
        searchStart = valEnd;
      }
      else
      {
        searchStart = markerIndex + marker.Length;
      }
    }

    if (sb is null)
    {
      return input;
    }

    sb.Append(input, searchStart, input.Length - searchStart);
    return sb.ToString();
  }

  private static int FindSensitiveValueEnd(string input, int startIndex)
  {
    for (int i = startIndex; i < input.Length; i++)
    {
      char c = input[i];
      if (c == ',' || c == ';' || c == '\r' || c == '\n')
      {
        return i;
      }
    }
    return input.Length;
  }

  private static int FindImmediateSeparatorIndex(string input, int startIndex)
  {
    for (int i = Math.Max(startIndex, 0); i < input.Length; i++)
    {
      char c = input[i];
      if (c == ':' || c == '=')
      {
        return i;
      }

      if (!char.IsWhiteSpace(c))
      {
        return -1;
      }
    }

    return -1;
  }

  private static bool LooksLikeBinaryPayload(string input)
  {
    int contiguousHexRun = 0;
    foreach (char c in input)
    {
      if (IsHex(c))
      {
        contiguousHexRun++;
        if (contiguousHexRun >= 32)
        {
          return true;
        }
      }
      else if (!char.IsWhiteSpace(c))
      {
        contiguousHexRun = 0;
      }
    }

    return false;
  }

  private static bool IsHex(char value)
  {
    return (value >= '0' && value <= '9') ||
           (value >= 'a' && value <= 'f') ||
           (value >= 'A' && value <= 'F');
  }
}

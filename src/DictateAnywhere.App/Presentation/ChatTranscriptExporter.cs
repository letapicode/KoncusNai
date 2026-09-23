using System;
using System.Collections.Generic;
using System.Text;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Presentation;

/// <summary>Converts the persisted chat records into portable text formats.</summary>
internal static class ChatTranscriptExporter
{
  public static string ToMarkdown(string title, IEnumerable<ChatMessage> messages)
  {
    StringBuilder builder = new();
    builder.Append("# ").AppendLine(string.IsNullOrWhiteSpace(title) ? AppBrand.ChatTitle : title.Trim());
    builder.AppendLine();

    foreach (ChatMessage message in messages)
    {
      ChatMessage normalized = message.Normalize();
      if (string.Equals(normalized.Role, ChatMessageRoles.System, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      builder.Append("## ")
        .Append(string.Equals(normalized.Role, ChatMessageRoles.Assistant, StringComparison.OrdinalIgnoreCase) ? AppBrand.Name : "You")
        .AppendLine();
      builder.AppendLine(normalized.Content);
      builder.AppendLine();
    }

    return builder.ToString();
  }

  public static string ToRtf(string title, IEnumerable<ChatMessage> messages)
  {
    StringBuilder builder = new("{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Segoe UI;}{\\f1 Cascadia Code;}}\\viewkind4\\uc1\n");
    AppendRtfLine(builder, string.IsNullOrWhiteSpace(title) ? AppBrand.ChatTitle : title.Trim(), "\\b\\fs32 ");
    builder.Append("\\par\\par\n");

    foreach (ChatMessage message in messages)
    {
      ChatMessage normalized = message.Normalize();
      if (string.Equals(normalized.Role, ChatMessageRoles.System, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      string speaker = string.Equals(normalized.Role, ChatMessageRoles.Assistant, StringComparison.OrdinalIgnoreCase)
        ? AppBrand.Name
        : "You";
      AppendRtfLine(builder, speaker, "\\b\\fs24 ");
      foreach (string line in normalized.Content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
      {
        AppendRtfLine(builder, line, string.Empty);
      }
      builder.Append("\\par\n");
    }

    return builder.Append('}').ToString();
  }

  private static void AppendRtfLine(StringBuilder builder, string text, string prefix)
  {
    builder.Append(prefix);
    foreach (char character in text)
    {
      switch (character)
      {
        case '\\': builder.Append("\\\\"); break;
        case '{': builder.Append("\\{"); break;
        case '}': builder.Append("\\}"); break;
        case '\t': builder.Append("\\tab "); break;
        default:
          if (character > 127)
          {
            builder.Append("\\u").Append((short)character).Append('?');
          }
          else
          {
            builder.Append(character);
          }
          break;
      }
    }
    builder.Append("\\par\n");
  }
}

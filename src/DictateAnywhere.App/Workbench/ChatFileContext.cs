using System.IO;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench;

internal sealed record ChatFileAttachment(
  string Id,
  string SourcePath,
  string DisplayName,
  string ContextText,
  bool WasTruncated)
{
  public static ChatFileAttachment Create(string sourcePath, string extractedText)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
    ArgumentException.ThrowIfNullOrWhiteSpace(extractedText);

    const int maximumCharactersPerFile = 3_500;
    string normalized = extractedText.Trim();
    bool truncated = normalized.Length > maximumCharactersPerFile;
    string context = truncated
      ? normalized[..maximumCharactersPerFile].TrimEnd() + "\n[File excerpt truncated for the local model context.]"
      : normalized;
    return new ChatFileAttachment(
      Guid.NewGuid().ToString("N"),
      sourcePath,
      Path.GetFileName(sourcePath),
      context,
      truncated);
  }
}

internal static class ChatFileContext
{
  internal const string SystemMessagePrefix = "Local file context for this private chat:";
  private const int MaximumContextCharacters = 6_000;

  public static IReadOnlyList<ChatMessage> Merge(
    IReadOnlyList<ChatMessage> messages,
    IReadOnlyList<ChatFileAttachment> attachments,
    DateTimeOffset createdUtc)
  {
    if (attachments.Count == 0)
    {
      return messages;
    }

    ChatMessage? existingContext = messages.LastOrDefault(IsFileContextMessage);
    string existingBody = existingContext is null
      ? string.Empty
      : existingContext.Content[SystemMessagePrefix.Length..].Trim();
    string newBody = BuildAttachmentBody(attachments);
    string combinedBody = CombineWithinLimit(existingBody, newBody);
    ChatMessage contextMessage = new(
      ChatMessageRoles.System,
      $"{SystemMessagePrefix}\n{combinedBody}",
      existingContext?.CreatedUtc ?? createdUtc);

    return messages
      .Where(message => !IsFileContextMessage(message))
      .Append(contextMessage.Normalize())
      .ToArray();
  }

  public static bool IsFileContextMessage(ChatMessage message)
  {
    return string.Equals(message.Role, ChatMessageRoles.System, StringComparison.OrdinalIgnoreCase)
           && message.Content.StartsWith(SystemMessagePrefix, StringComparison.Ordinal);
  }

  private static string BuildAttachmentBody(IReadOnlyList<ChatFileAttachment> attachments)
  {
    const string separator = "\n\n";
    const string truncationMarker = "\n[File excerpt shortened to fit the local model context.]";
    List<string> blocks = [];
    int remaining = MaximumContextCharacters;
    for (int index = attachments.Count - 1; index >= 0 && remaining > 0; index--)
    {
      ChatFileAttachment attachment = attachments[index];
      string header = $"--- File: {attachment.DisplayName} ---\n";
      int separatorLength = blocks.Count == 0 ? 0 : separator.Length;
      int blockBudget = remaining - separatorLength;
      if (blockBudget <= header.Length)
      {
        break;
      }

      string block;
      if (header.Length + attachment.ContextText.Length <= blockBudget)
      {
        block = header + attachment.ContextText;
      }
      else
      {
        int excerptBudget = blockBudget - header.Length - truncationMarker.Length;
        if (excerptBudget <= 0)
        {
          break;
        }

        block = header + attachment.ContextText[..excerptBudget].TrimEnd() + truncationMarker;
      }

      blocks.Insert(0, block);
      remaining -= block.Length + separatorLength;
    }

    return string.Join(separator, blocks);
  }

  private static string CombineWithinLimit(string existingBody, string newBody)
  {
    if (newBody.Length >= MaximumContextCharacters)
    {
      return newBody[..MaximumContextCharacters];
    }

    int existingBudget = MaximumContextCharacters - newBody.Length - Environment.NewLine.Length * 2;
    if (existingBudget <= 0 || string.IsNullOrWhiteSpace(existingBody))
    {
      return newBody;
    }

    const string truncationMarker = "[Earlier file context truncated.]\n";
    if (existingBody.Length > existingBudget && existingBudget <= truncationMarker.Length)
    {
      return newBody;
    }

    string retainedExisting = existingBody.Length <= existingBudget
      ? existingBody
      : truncationMarker + existingBody[^(existingBudget - truncationMarker.Length)..];
    return string.Join(Environment.NewLine + Environment.NewLine, retainedExisting, newBody);
  }
}

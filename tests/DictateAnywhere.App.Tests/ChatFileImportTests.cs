using System;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class ChatFileImportTests
{
  [Xunit.Theory]
  [Xunit.InlineData("meeting.mp3", "Media")]
  [Xunit.InlineData("demo.mp4", "Media")]
  [Xunit.InlineData("brief.pdf", "Document")]
  [Xunit.InlineData("notes.md", "Document")]
  [Xunit.InlineData("scan.png", "Document")]
  [Xunit.InlineData("archive.zip", "Unsupported")]
  public void Catalog_ClassifiesSupportedFiles(string fileName, string expected)
  {
    Xunit.Assert.Equal(expected, ChatFileImportCatalog.Classify(fileName).ToString());
  }

  [Xunit.Fact]
  public void Attachment_BoundsExtractedTextBeforeItReachesChatContext()
  {
    ChatFileAttachment attachment = ChatFileAttachment.Create(
      "C:\\files\\long-notes.txt",
      new string('a', 5_000));

    Xunit.Assert.True(attachment.WasTruncated);
    Xunit.Assert.Equal("long-notes.txt", attachment.DisplayName);
    Xunit.Assert.Contains("excerpt truncated", attachment.ContextText, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.True(attachment.ContextText.Length < 3_600);
  }

  [Xunit.Fact]
  public void ContextMerge_PreservesConversationAndMaintainsOneHiddenContextMessage()
  {
    DateTimeOffset now = DateTimeOffset.UtcNow;
    ChatMessage userMessage = new(ChatMessageRoles.User, "Summarize these notes.", now);
    ChatFileAttachment first = ChatFileAttachment.Create("C:\\files\\first.txt", "First document body.");
    ChatFileAttachment second = ChatFileAttachment.Create("C:\\files\\second.txt", "Second document body.");

    IReadOnlyList<ChatMessage> once = ChatFileContext.Merge([userMessage], [first], now);
    IReadOnlyList<ChatMessage> twice = ChatFileContext.Merge(once, [second], now.AddSeconds(1));

    Xunit.Assert.Contains(twice, message => ReferenceEquals(message, userMessage) || message.Content == userMessage.Content);
    ChatMessage context = Xunit.Assert.Single(twice.Where(ChatFileContext.IsFileContextMessage));
    Xunit.Assert.Equal(ChatMessageRoles.System, context.Role);
    Xunit.Assert.Contains("first.txt", context.Content, StringComparison.Ordinal);
    Xunit.Assert.Contains("second.txt", context.Content, StringComparison.Ordinal);
    Xunit.Assert.True(context.Content.Length <= ChatFileContext.SystemMessagePrefix.Length + 6_002);
  }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Workbench.Publishing;
using DictateAnywhere.Core.Contracts;
using Xunit;

namespace DictateAnywhere.App.Tests;

public sealed class BrandCompatibilityTests
{
  [Fact]
  public async Task LegacyCredentialFixtures_RemainReadableWithoutRewritingTheirFiles()
  {
    string directory = Path.Combine(Path.GetTempPath(), "koncus-nai-brand-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
      // Synthetic credentials using the pre-rebrand format; never read real user credentials.
      const string key = "notype-youtube-user";
      byte[] token = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes("fixture-token"), "Notype.YouTube.OAuth.v1"u8.ToArray(), DataProtectionScope.CurrentUser);
      string tokenPath = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".bin");
      await File.WriteAllBytesAsync(tokenPath, token);
      Assert.Equal("fixture-token", await new ProtectedLocalDataStore(directory).GetAsync<string>(key));
      Assert.Equal(token, await File.ReadAllBytesAsync(tokenPath));

      YouTubeOAuthConfiguration expected = new("fixture-client", "fixture-secret");
      byte[] client = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(expected), "Notype.YouTube.Client.v1"u8.ToArray(), DataProtectionScope.CurrentUser);
      string clientPath = Path.Combine(directory, "youtube-client.bin");
      await File.WriteAllBytesAsync(clientPath, client);
      Assert.Equal(expected, new YouTubeOAuthConfigurationStore(clientPath).Load());
      Assert.Equal(client, await File.ReadAllBytesAsync(clientPath));
    }
    finally { Directory.Delete(directory, recursive: true); }
  }

  [Fact]
  public void ChatExports_RebrandGeneratedLabels_PreserveOriginalTitleAndContent()
  {
    const string title = "My Nilo and Notype notes";
    const string content = "Nilo, Notype and Dictate Anywhere were the old names.";
    ChatMessage[] messages = [new(ChatMessageRoles.Assistant, content, DateTimeOffset.UtcNow)];
    string markdown = ChatTranscriptExporter.ToMarkdown(title, messages);
    Assert.StartsWith("# " + title, markdown, StringComparison.Ordinal);
    Assert.Contains("## Koncus Nai", markdown, StringComparison.Ordinal);
    Assert.Contains(content, markdown, StringComparison.Ordinal);
    Assert.StartsWith("# Koncus Nai chat", ChatTranscriptExporter.ToMarkdown("", messages), StringComparison.Ordinal);
    string rtf = ChatTranscriptExporter.ToRtf(title, messages);
    Assert.Contains(title, rtf, StringComparison.Ordinal);
    Assert.Contains(content, rtf, StringComparison.Ordinal);
    Assert.Contains("Koncus Nai", rtf, StringComparison.Ordinal);
  }
}

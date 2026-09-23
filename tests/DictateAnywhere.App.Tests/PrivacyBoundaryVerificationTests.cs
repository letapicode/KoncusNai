using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;
using Xunit;

namespace DictateAnywhere.App.Tests;

public sealed class PrivacyBoundaryVerificationTests
{
  [Fact]
  public void AboutWindow_PrivacyCopyMatchesLocalPlaintextHistoryBoundary()
  {
    string solutionDir = FindSolutionRoot();
    string aboutWindowPath = Path.Combine(
      solutionDir,
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "AboutWindow.xaml");
    XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XDocument document = XDocument.Load(aboutWindowPath);
    XElement privacyTitle = document
      .Descendants(presentation + "TextBlock")
      .Single(element => string.Equals(
        element.Attribute("Text")?.Value,
        "Your data, on your device",
        StringComparison.Ordinal));
    XElement privacyCopyElement = privacyTitle.Parent?
      .Elements(presentation + "TextBlock")
      .Single(element => !ReferenceEquals(element, privacyTitle))
      ?? throw new InvalidOperationException("About privacy copy was not found beside its heading.");
    string privacyCopy = privacyCopyElement.Attribute("Text")?.Value
      ?? throw new InvalidOperationException("About privacy copy must use the Text attribute.");

    Assert.Contains("always saved as local plaintext", privacyCopy, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("Windows account and file permissions", privacyCopy, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("never stores raw microphone audio", privacyCopy, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("encrypted", privacyCopy, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("when enabled", privacyCopy, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void DictationHistoryStore_DefaultFilePath_ResidesUnderLocalAppDataHistoryFolder()
  {
    string defaultPath = LocalDictationHistoryStore.DefaultHistoryFilePath;
    string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    string expectedPrefix = Path.Combine(localAppData, "DictateAnywhere", "history");

    Assert.StartsWith(expectedPrefix, defaultPath, StringComparison.OrdinalIgnoreCase);
    Assert.EndsWith("dictation-history.local.jsonl", defaultPath, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void ChatHistoryStore_DefaultFilePath_ResidesUnderLocalAppDataHistoryFolder()
  {
    string defaultPath = LocalChatHistoryStore.DefaultHistoryFilePath;
    string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    string expectedPrefix = Path.Combine(localAppData, "DictateAnywhere", "history");

    Assert.StartsWith(expectedPrefix, defaultPath, StringComparison.OrdinalIgnoreCase);
    Assert.EndsWith("chat-history.local.jsonl", defaultPath, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task HistoryFiles_AreStoredAsPlaintextJsonLines()
  {
    using TempDirectoryScope tempDir = new();
    string dictationPath = Path.Combine(tempDir.DirectoryPath, "dictation-test.local.jsonl");
    string chatPath = Path.Combine(tempDir.DirectoryPath, "chat-test.local.jsonl");

    LocalDictationHistoryStore dictationStore = new(dictationPath);
    DictationHistoryRecord dictationRecord = new(
      CreatedUtc: DateTimeOffset.UtcNow,
      ProfileId: DictationHistoryRecord.DefaultProfileId,
      TranscriptionProviderId: "whisper",
      TranscriptionModelId: "whisper-base",
      RawTranscript: "This is raw transcript",
      FinalText: "This is a private dictation transcript.",
      TranscriptionDuration: TimeSpan.FromMilliseconds(100),
      TextTransformationDuration: TimeSpan.FromMilliseconds(50),
      TotalPipelineDuration: TimeSpan.FromMilliseconds(150),
      EntryId: "entry-001",
      SessionId: "session-001",
      Title: "Private Dictation",
      Source: "dictation");

    await dictationStore.RecordAsync(dictationRecord);

    LocalChatHistoryStore chatStore = new(chatPath);
    ChatHistoryRecord chatRecord = new(
      ConversationId: "conv-001",
      Title: "Privacy Discussion",
      CreatedUtc: DateTimeOffset.UtcNow,
      UpdatedUtc: DateTimeOffset.UtcNow,
      ProviderId: "ollama",
      ModelId: "gemma4:e4b",
      Messages:
      [
        new ChatMessage(ChatMessageRoles.User, "Hello assistant", DateTimeOffset.UtcNow),
        new ChatMessage(ChatMessageRoles.Assistant, "Hello user", DateTimeOffset.UtcNow)
      ]);

    await chatStore.SaveAsync(chatRecord);

    // Assert files exist and can be read directly as plaintext JSON lines without decryption
    Assert.True(File.Exists(dictationPath));
    string[] dictationLines = await File.ReadAllLinesAsync(dictationPath);
    Assert.Single(dictationLines);

    using (JsonDocument doc = JsonDocument.Parse(dictationLines[0]))
    {
      JsonElement root = doc.RootElement;
      Assert.Equal(1, root.GetProperty("version").GetInt32());
      JsonElement record = root.GetProperty("record");
      Assert.Equal("entry-001", record.GetProperty("entryId").GetString());
      Assert.Equal("session-001", record.GetProperty("sessionId").GetString());
      Assert.Equal("This is a private dictation transcript.", record.GetProperty("finalText").GetString());
    }

    Assert.True(File.Exists(chatPath));
    string[] chatLines = await File.ReadAllLinesAsync(chatPath);
    Assert.Single(chatLines);

    using (JsonDocument doc = JsonDocument.Parse(chatLines[0]))
    {
      JsonElement root = doc.RootElement;
      Assert.Equal(1, root.GetProperty("version").GetInt32());
      JsonElement record = root.GetProperty("record");
      Assert.Equal("conv-001", record.GetProperty("conversationId").GetString());
      Assert.Equal("Privacy Discussion", record.GetProperty("title").GetString());
      JsonElement messages = record.GetProperty("messages");
      Assert.Equal(2, messages.GetArrayLength());
    }
  }

  [Fact]
  public async Task HistoryDeletion_PermanentlyRemovesRecordsFromDisk()
  {
    using TempDirectoryScope tempDir = new();
    string dictationPath = Path.Combine(tempDir.DirectoryPath, "dictation-purge.local.jsonl");
    LocalDictationHistoryStore store = new(dictationPath);

    DictationHistoryRecord record1 = new(
      CreatedUtc: DateTimeOffset.UtcNow,
      ProfileId: DictationHistoryRecord.DefaultProfileId,
      TranscriptionProviderId: "whisper",
      TranscriptionModelId: "base",
      RawTranscript: "Keep this text",
      FinalText: "Keep this text",
      TranscriptionDuration: TimeSpan.FromMilliseconds(10),
      TextTransformationDuration: TimeSpan.Zero,
      TotalPipelineDuration: TimeSpan.FromMilliseconds(10),
      EntryId: "e1",
      SessionId: "s1");

    DictationHistoryRecord record2 = new(
      CreatedUtc: DateTimeOffset.UtcNow,
      ProfileId: DictationHistoryRecord.DefaultProfileId,
      TranscriptionProviderId: "whisper",
      TranscriptionModelId: "base",
      RawTranscript: "Delete this secret text",
      FinalText: "Delete this secret text",
      TranscriptionDuration: TimeSpan.FromMilliseconds(10),
      TextTransformationDuration: TimeSpan.Zero,
      TotalPipelineDuration: TimeSpan.FromMilliseconds(10),
      EntryId: "e2",
      SessionId: "s2");

    await store.RecordAsync(record1);
    await store.RecordAsync(record2);

    int deleted = await store.DeleteSessionAsync("s2");
    Assert.Equal(1, deleted);

    string fileContent = await File.ReadAllTextAsync(dictationPath);
    Assert.Contains("Keep this text", fileContent, StringComparison.Ordinal);
    Assert.DoesNotContain("Delete this secret text", fileContent, StringComparison.Ordinal);
    Assert.DoesNotContain("s2", fileContent, StringComparison.Ordinal);
  }

  [Fact]
  public void TranscriptionProviders_AllConfiguredAsLocalOffline()
  {
    var registry = LocalTranscriptionProviderRegistry.CreateDefault();
    var definitions = registry.GetDefinitions();
    Assert.NotEmpty(definitions);

    foreach (var def in definitions)
    {
      Assert.Equal(ModelProviderOperationalKind.LocalOffline, def.OperationalMetadata.Kind);
      Assert.False(def.OperationalMetadata.RequiresNetwork, $"Provider '{def.ProviderId}' requires network.");
      Assert.False(def.OperationalMetadata.RequiresCredentials, $"Provider '{def.ProviderId}' requires credentials.");
      Assert.False(def.OperationalMetadata.ProcessesUserAudioOffDevice, $"Provider '{def.ProviderId}' processes audio off-device.");
      Assert.False(def.OperationalMetadata.RequiresExplicitUserOptIn, $"Provider '{def.ProviderId}' requires online opt-in.");
    }
  }

  [Fact]
  public void ChatProviders_AllConfiguredAsLocalOffline()
  {
    var registry = LocalChatProviderRegistry.CreateDefault();
    var definitions = registry.GetDefinitions();
    Assert.NotEmpty(definitions);

    foreach (var def in definitions)
    {
      Assert.Equal(ModelProviderOperationalKind.LocalOffline, def.OperationalMetadata.Kind);
      Assert.False(def.OperationalMetadata.RequiresNetwork, $"Provider '{def.ProviderId}' requires network.");
      Assert.False(def.OperationalMetadata.RequiresCredentials, $"Provider '{def.ProviderId}' requires credentials.");
      Assert.False(def.OperationalMetadata.ProcessesUserAudioOffDevice, $"Provider '{def.ProviderId}' processes audio off-device.");
      Assert.False(def.OperationalMetadata.RequiresExplicitUserOptIn, $"Provider '{def.ProviderId}' requires online opt-in.");
    }
  }

  [Fact]
  public void SourceTree_AllExternalNetworkEndpoints_MatchApprovedAllowlist()
  {
    string solutionDir = FindSolutionRoot();
    string srcDir = Path.Combine(solutionDir, "src");
    Assert.True(Directory.Exists(srcDir), "src directory not found");

    string[] approvedPrefixes =
    [
      "http://127.0.0.1",
      "http://localhost",
      "http://schemas.openxmlformats.org",
      "https://huggingface.co/",
      "https://github.com/ggml-org/llama.cpp/releases/download/b10823/",
      "https://ollama.com/download/OllamaSetup.exe",
      "https://www.gyan.dev/ffmpeg/builds/",
      "https://console.cloud.google.com/apis/credentials",
      "https://youtu.be/"
    ];

    Regex urlRegex = new(@"https?://[^\s""'><\)]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    List<string> unauthorizedUrls = new();

    foreach (string csFile in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
    {
      string[] lines = File.ReadAllLines(csFile);
      for (int i = 0; i < lines.Length; i++)
      {
        string line = lines[i].Trim();
        if (line.StartsWith("///", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal))
        {
          continue;
        }

        foreach (Match match in urlRegex.Matches(line))
        {
          string url = match.Value.TrimEnd('.', ';', ',', '"', '\'');
          bool isApproved = approvedPrefixes.Any(prefix => url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
          if (!isApproved)
          {
            unauthorizedUrls.Add($"{Path.GetRelativePath(solutionDir, csFile)}:{i + 1} -> {url}");
          }
        }
      }
    }

    Assert.True(
      unauthorizedUrls.Count == 0,
      $"Discovered unauthorized network endpoints:\n{string.Join("\n", unauthorizedUrls)}");
  }

  private static string FindSolutionRoot()
  {
    string? current = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(current))
    {
      if (File.Exists(Path.Combine(current, "DictateAnywhere.sln")))
      {
        return current;
      }

      current = Path.GetDirectoryName(current);
    }

    return Directory.GetCurrentDirectory();
  }

  private sealed class TempDirectoryScope : IDisposable
  {
    public TempDirectoryScope()
    {
      DirectoryPath = Path.Combine(Path.GetTempPath(), "DictateAnywhere.PrivacyTests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public void Dispose()
    {
      if (Directory.Exists(DirectoryPath))
      {
        Directory.Delete(DirectoryPath, recursive: true);
      }
    }
  }
}

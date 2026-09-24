using System.Text.Json;
using DictateAnywhere.App.Runtime;

namespace DictateAnywhere.App.Tests;

public sealed class OllamaListenerTrustTests
{
  [Xunit.Fact]
  public void ForgedModelDigestDoesNotApproveUnknownListener()
  {
    using JsonDocument tags = JsonDocument.Parse("{\"models\":[{\"name\":\"gemma4:e4b\",\"digest\":\""
      + LocalOllamaRuntimeReadiness.Gemma4E4BDigest + "\"}]}");
    OllamaListenerIdentity unknown = new(42, DateTime.UtcNow, @"C:\Unknown\ollama.exe");
    Xunit.Assert.True(LocalOllamaRuntimeReadiness.IsPinnedModelPresent(tags.RootElement, "gemma4:e4b"));
    Xunit.Assert.False(OllamaListenerTrust.IsApproved(unknown, null, null));
  }

  [Xunit.Fact]
  public void ReplacementProcessInvalidatesExternalAndManagedApproval()
  {
    DateTime started = DateTime.UtcNow;
    OllamaListenerIdentity approved = new(42, started, @"C:\Ollama\ollama.exe");
    OllamaListenerIdentity replacement = new(43, started.AddSeconds(1), @"C:\Ollama\ollama.exe");
    Xunit.Assert.True(OllamaListenerTrust.IsApproved(approved, approved, null));
    Xunit.Assert.False(OllamaListenerTrust.IsApproved(replacement, approved, null));
    Xunit.Assert.True(OllamaListenerTrust.IsApproved(approved, null, (42, started)));
    Xunit.Assert.False(OllamaListenerTrust.IsApproved(replacement, null, (42, started)));
  }
}

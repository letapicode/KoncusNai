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
    Xunit.Assert.True(OllamaListenerTrust.IsApproved(approved, null, approved));
    Xunit.Assert.False(OllamaListenerTrust.IsApproved(replacement, null, approved));
  }

  [Xunit.Theory]
  [Xunit.InlineData(43, 0, @"C:\Ollama\ollama.exe")]
  [Xunit.InlineData(42, 1, @"C:\Ollama\ollama.exe")]
  [Xunit.InlineData(42, 0, @"C:\Other\ollama.exe")]
  public void EveryIdentityFieldInvalidatesBothKindsOfApproval(int pid, int ticks, string path)
  {
    DateTime start = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    OllamaListenerIdentity approved = new(42, start, @"C:\Ollama\ollama.exe");
    OllamaListenerIdentity changed = new(pid, start.AddTicks(ticks), path);
    Xunit.Assert.False(OllamaListenerTrust.IsApproved(changed, approved, null));
    Xunit.Assert.False(OllamaListenerTrust.IsApproved(changed, null, approved));
  }

  [Xunit.Fact]
  public void ConsentReplacementUnavailableOrAmbiguousIdentityRequiresFreshApproval()
  {
    OllamaListenerIdentity original = new(42, DateTime.UtcNow, @"C:\Ollama\ollama.exe");
    OllamaListenerIdentity? current = original;
    OllamaListenerTrust.Session trust = new(() => current);
    Xunit.Assert.True(trust.ApproveExternal(original));
    Xunit.Assert.True(trust.IsCurrentTrusted());
    current = original with { ProcessId = 43 };
    Xunit.Assert.False(trust.ApproveExternal(original));
    current = original;
    Xunit.Assert.False(trust.IsCurrentTrusted());
    Xunit.Assert.True(trust.ApproveExternal(original));
    current = null; // The capture boundary rejects unavailable and multiple listener owners.
    Xunit.Assert.False(trust.IsCurrentTrusted());
    current = original;
    Xunit.Assert.False(trust.IsCurrentTrusted());
    trust.ApproveManaged(original);
    Xunit.Assert.True(trust.IsCurrentTrusted());
    current = original with { ExecutablePath = @"C:\Other\ollama.exe" };
    Xunit.Assert.False(trust.IsCurrentTrusted());
    current = original;
    Xunit.Assert.False(trust.IsCurrentTrusted());
  }
}

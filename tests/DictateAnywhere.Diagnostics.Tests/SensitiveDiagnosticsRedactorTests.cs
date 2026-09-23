using System;
using System.Text.Json;
using Xunit;

namespace DictateAnywhere.Diagnostics.Tests;

public sealed class SensitiveDiagnosticsRedactorTests
{
  [Theory]
  [InlineData(null, null)]
  [InlineData("", "")]
  [InlineData("   ", "   ")]
  public void Redact_ReturnsEmptyOrWhitespaceUnmodified(string? input, string? expected)
  {
    Assert.Equal(expected, SensitiveDiagnosticsRedactor.Redact(input!));
  }

  [Fact]
  public void Redact_RedactsTranscriptMarkers()
  {
    string input = "transcript: hello this is my secret transcript";
    string redacted = SensitiveDiagnosticsRedactor.Redact(input);

    Assert.Equal("transcript: [REDACTED]", redacted);
    Assert.DoesNotContain("secret transcript", redacted, StringComparison.Ordinal);
  }

  [Fact]
  public void Redact_RedactsPromptMarkers()
  {
    string input = "user prompt: generate code for me";
    string redacted = SensitiveDiagnosticsRedactor.Redact(input);

    Assert.Equal("user prompt: [REDACTED]", redacted);
    Assert.DoesNotContain("generate code", redacted, StringComparison.Ordinal);
  }

  [Fact]
  public void Redact_RedactsAudioMarkers()
  {
    string input = "audio chunk: 1204910249102";
    string redacted = SensitiveDiagnosticsRedactor.Redact(input);

    Assert.Equal("audio chunk: [REDACTED]", redacted);
  }

  [Fact]
  public void Redact_RedactsBearerToken()
  {
    string input = "Authorization header Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9 present";
    string redacted = SensitiveDiagnosticsRedactor.Redact(input);

    Assert.Contains("Bearer [REDACTED]", redacted, StringComparison.Ordinal);
    Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", redacted, StringComparison.Ordinal);
    Assert.EndsWith("present", redacted, StringComparison.Ordinal);
  }

  [Fact]
  public void Redact_RedactsPasswordAndSecretMarkers()
  {
    string input = "password: supersecretpassword123";
    string redacted = SensitiveDiagnosticsRedactor.Redact(input);

    Assert.Equal("password: [REDACTED]", redacted);
    Assert.DoesNotContain("supersecretpassword123", redacted, StringComparison.Ordinal);
  }

  [Fact]
  public void Redact_RedactsApiKeyWithEqualsOrColon()
  {
    string colon = SensitiveDiagnosticsRedactor.Redact("api_key: abc12345");
    string equals = SensitiveDiagnosticsRedactor.Redact("apiKey=def67890");

    Assert.Equal("api_key: [REDACTED]", colon);
    Assert.Equal("apiKey= [REDACTED]", equals);
  }

  [Fact]
  public void Redact_MultiMarker_RedactsBothPasswordAndPrompt_RegardlessOfOrder()
  {
    string order1 = "password: secret123, prompt: write a novel";
    string redacted1 = SensitiveDiagnosticsRedactor.Redact(order1);

    Assert.Contains("password: [REDACTED]", redacted1, StringComparison.Ordinal);
    Assert.Contains("prompt: [REDACTED]", redacted1, StringComparison.Ordinal);
    Assert.DoesNotContain("secret123", redacted1, StringComparison.Ordinal);
    Assert.DoesNotContain("write a novel", redacted1, StringComparison.Ordinal);

    string order2 = "prompt: write a novel, password: secret123";
    string redacted2 = SensitiveDiagnosticsRedactor.Redact(order2);

    Assert.Contains("password: [REDACTED]", redacted2, StringComparison.Ordinal);
    Assert.Contains("prompt: [REDACTED]", redacted2, StringComparison.Ordinal);
    Assert.DoesNotContain("secret123", redacted2, StringComparison.Ordinal);
    Assert.DoesNotContain("write a novel", redacted2, StringComparison.Ordinal);
  }

  [Fact]
  public void Redact_IsIdempotentForAlreadyRedactedAndEmptySensitiveValues()
  {
    const string input = "password: [REDACTED]; audio chunk: ; Bearer ";

    string once = SensitiveDiagnosticsRedactor.Redact(input);
    string twice = SensitiveDiagnosticsRedactor.Redact(once);

    Assert.Equal(
      "password: [REDACTED]; audio chunk: [REDACTED]; Bearer [REDACTED]",
      once);
    Assert.Equal(once, twice);
  }

  [Fact]
  public void Redact_JsonStructure_RedactsSensitiveKeysAndMaintainsValidJson()
  {
    string json = "{\"user\":\"alice\",\"apiKey\":\"secret-key-12345\",\"password\":\"p@ssword\",\"status\":\"active\"}";
    string redacted = SensitiveDiagnosticsRedactor.Redact(json);

    using JsonDocument doc = JsonDocument.Parse(redacted);
    JsonElement root = doc.RootElement;

    Assert.Equal("alice", root.GetProperty("user").GetString());
    Assert.Equal("active", root.GetProperty("status").GetString());
    Assert.Equal("[REDACTED]", root.GetProperty("apiKey").GetString());
    Assert.Equal("[REDACTED]", root.GetProperty("password").GetString());
    Assert.DoesNotContain("secret-key-12345", redacted, StringComparison.Ordinal);
    Assert.DoesNotContain("p@ssword", redacted, StringComparison.Ordinal);
  }

  [Fact]
  public void Redact_JsonStructure_RedactsNestedMessageStringWithMarker()
  {
    string json = "{\"level\":\"INFO\",\"message\":\"recognized text: private words spoken\",\"code\":200}";
    string redacted = SensitiveDiagnosticsRedactor.Redact(json);

    using JsonDocument doc = JsonDocument.Parse(redacted);
    JsonElement root = doc.RootElement;

    Assert.Equal("INFO", root.GetProperty("level").GetString());
    Assert.Equal(200, root.GetProperty("code").GetInt32());
    Assert.Equal("recognized text: [REDACTED]", root.GetProperty("message").GetString());
    Assert.DoesNotContain("private words spoken", redacted, StringComparison.Ordinal);
  }

  [Fact]
  public void Redact_RedactsContiguousHexBinaryPayload()
  {
    string binary = "0123456789abcdef0123456789abcdef";
    string redacted = SensitiveDiagnosticsRedactor.Redact(binary);

    Assert.Equal("[REDACTED SENSITIVE PAYLOAD]", redacted);
  }

  [Theory]
  [InlineData("password", true)]
  [InlineData("client_secret", true)]
  [InlineData("clientSecret", true)]
  [InlineData("api_key", true)]
  [InlineData("api-key", true)]
  [InlineData("apiKey", true)]
  [InlineData("access_token", true)]
  [InlineData("userPrompt", true)]
  [InlineData("chatTranscript", true)]
  [InlineData("audioPayload", true)]
  [InlineData("status", false)]
  [InlineData("durationMs", false)]
  [InlineData("level", false)]
  [InlineData("operationId", false)]
  public void IsSensitiveKey_IdentifiesSensitiveNames(string name, bool expected)
  {
    Assert.Equal(expected, SensitiveDiagnosticsRedactor.IsSensitiveKey(name));
  }
}

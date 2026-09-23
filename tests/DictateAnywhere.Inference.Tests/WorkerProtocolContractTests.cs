using System;
using System.Text.Json;
using DictateAnywhere.Inference;
using Xunit;

namespace DictateAnywhere.Inference.Tests;

public sealed class WorkerProtocolContractTests
{
  [Fact]
  public void SerializeRequest_FormatsAllPropertiesAsSnakeCaseLower()
  {
    SampleWorkerRequest request = new(
      AudioPath: "C:\\data\\test.wav",
      SampleRateHz: 16000,
      EnablePunctuation: true,
      TargetLanguage: "hi");

    string json = PersistentPythonWorkerClient.SerializeRequest(request);

    using JsonDocument document = JsonDocument.Parse(json);
    JsonElement root = document.RootElement;

    Assert.True(root.TryGetProperty("audio_path", out JsonElement audioPath));
    Assert.Equal("C:\\data\\test.wav", audioPath.GetString());

    Assert.True(root.TryGetProperty("sample_rate_hz", out JsonElement sampleRate));
    Assert.Equal(16000, sampleRate.GetInt32());

    Assert.True(root.TryGetProperty("enable_punctuation", out JsonElement punctuation));
    Assert.True(punctuation.GetBoolean());

    Assert.True(root.TryGetProperty("target_language", out JsonElement targetLang));
    Assert.Equal("hi", targetLang.GetString());

    Assert.False(root.TryGetProperty("AudioPath", out _));
    Assert.False(root.TryGetProperty("SampleRateHz", out _));
    Assert.False(root.TryGetProperty("EnablePunctuation", out _));
  }

  [Fact]
  public void SerializeRequest_NullThrowsArgumentNullException()
  {
    Assert.Throws<ArgumentNullException>(() =>
      PersistentPythonWorkerClient.SerializeRequest(null!));
  }

  [Fact]
  public void DeserializePayload_WhenStatusOk_ExtractsTypedResponse()
  {
    string responseLine = "{\"status\":\"ok\",\"payload\":{\"transcribed_text\":\"Namaste world\",\"confidence_score\":0.98,\"elapsed_seconds\":1.25}}";

    SampleWorkerResponse response = PersistentPythonWorkerClient.DeserializePayload<SampleWorkerResponse>(responseLine);

    Assert.Equal("Namaste world", response.TranscribedText);
    Assert.Equal(0.98, response.ConfidenceScore);
    Assert.Equal(1.25, response.ElapsedSeconds);
  }

  [Fact]
  public void DeserializePayload_WhenStatusError_ThrowsInvalidOperationException()
  {
    string responseLine = "{\"status\":\"error\",\"error\":\"CUDA out of memory\"}";

    InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
      PersistentPythonWorkerClient.DeserializePayload<SampleWorkerResponse>(responseLine));

    Assert.Contains("Python worker response did not contain an ok payload", ex.Message);
  }

  [Fact]
  public void DeserializePayload_WhenPayloadMissing_ThrowsInvalidOperationException()
  {
    string responseLine = "{\"status\":\"ok\"}";

    InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
      PersistentPythonWorkerClient.DeserializePayload<SampleWorkerResponse>(responseLine));

    Assert.Contains("empty payload", ex.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void DeserializePayload_WhenMalformedJson_ThrowsJsonException()
  {
    string responseLine = "{\"status\":\"ok\",\"payload\":{invalid json}}";

    Assert.Throws<JsonException>(() =>
      PersistentPythonWorkerClient.DeserializePayload<SampleWorkerResponse>(responseLine));
  }

  [Fact]
  public void DeserializePayload_IsCaseInsensitiveForKeys()
  {
    string responseLine = "{\"STATUS\":\"ok\",\"PAYLOAD\":{\"Transcribed_Text\":\"Hello\",\"Confidence_Score\":1.0,\"Elapsed_Seconds\":0.5}}";

    SampleWorkerResponse response = PersistentPythonWorkerClient.DeserializePayload<SampleWorkerResponse>(responseLine);

    Assert.Equal("Hello", response.TranscribedText);
    Assert.Equal(1.0, response.ConfidenceScore);
  }

  private sealed record SampleWorkerRequest(
    string AudioPath,
    int SampleRateHz,
    bool EnablePunctuation,
    string TargetLanguage);

  private sealed record SampleWorkerResponse(
    string TranscribedText,
    double ConfidenceScore,
    double ElapsedSeconds);
}
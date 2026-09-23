using System;
using System.Text.Json;
using DictateAnywhere.Inference;

namespace DictateAnywhere.Inference.Tests;

public sealed class PersistentPythonWorkerClientTests
{
  [Xunit.Fact]
  public void SerializeRequest_UsesSnakeCaseForWorkerPayloads()
  {
    string json = PersistentPythonWorkerClient.SerializeRequest(
      new WorkerRequest("C:\\temp\\audio.wav", "batch", "en"));

    using JsonDocument document = JsonDocument.Parse(json);
    JsonElement root = document.RootElement;

    Xunit.Assert.True(root.TryGetProperty("audio_path", out JsonElement audioPath));
    Xunit.Assert.Equal("C:\\temp\\audio.wav", audioPath.GetString());
    Xunit.Assert.True(root.TryGetProperty("processing_mode", out JsonElement processingMode));
    Xunit.Assert.Equal("batch", processingMode.GetString());
    Xunit.Assert.True(root.TryGetProperty("language", out JsonElement language));
    Xunit.Assert.Equal("en", language.GetString());
    Xunit.Assert.False(root.TryGetProperty("AudioPath", out _));
    Xunit.Assert.False(root.TryGetProperty("ProcessingMode", out _));
  }

  [Xunit.Fact]
  public void DeserializePayload_UsesSnakeCaseForWorkerResponses()
  {
    WorkerResponse response = PersistentPythonWorkerClient.DeserializePayload<WorkerResponse>(
      "{\"status\":\"ok\",\"payload\":{\"text\":\"hello world\",\"duration_ms\":1234.5}}");

    Xunit.Assert.Equal("hello world", response.Text);
    Xunit.Assert.Equal(1234.5, response.DurationMs);
  }

  private sealed record WorkerRequest(string AudioPath, string ProcessingMode, string Language);

  private sealed record WorkerResponse(string Text, double DurationMs);
}

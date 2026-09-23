using System;
using System.IO;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.Inference.Tests;

public sealed class ProviderWorkerContractSmokeTests
{
  [Xunit.Fact]
  public async Task CohereWorker_FixtureMode_HealthCheckRequiresNoAudioAndRejectsUnknownOperations()
  {
    using TempDirectoryScope temp = new();
    string scriptPath = LocalModelScriptPathResolver.Resolve("cohere_transcribe_worker.py");
    await using PersistentPythonWorkerClient client = new(
      ResolvePythonExecutable(),
      scriptPath,
      $"--model-dir \"{temp.DirectoryPath}\" --fixture-mode",
      TimeSpan.FromSeconds(10));

    CohereWorkerHealthCheckResponse response = await client.InvokeAsync<CohereWorkerHealthCheckResponse>(
      new CohereWorkerHealthCheckRequest(),
      TimeSpan.FromSeconds(10));
    Xunit.Assert.Equal("model_ready", response.HealthCheck);

    await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => client.InvokeAsync<CohereWorkerHealthCheckResponse>(
      new { Operation = "unknown" },
      TimeSpan.FromSeconds(10)));

    response = await client.InvokeAsync<CohereWorkerHealthCheckResponse>(
      new CohereWorkerHealthCheckRequest(),
      TimeSpan.FromSeconds(10));
    Xunit.Assert.Equal("model_ready", response.HealthCheck);
  }

  [Xunit.Fact]
  public async Task CohereWorker_FixtureMode_UsesSnakeCaseRequestAndResponseContract()
  {
    using TempDirectoryScope temp = new();
    string scriptPath = LocalModelScriptPathResolver.Resolve("cohere_transcribe_worker.py");
    string audioPath = Path.Combine(temp.DirectoryPath, "sample.wav");
    File.WriteAllBytes(audioPath, CreateMinimalWaveFile());

    await using PersistentPythonWorkerClient client = new(
      ResolvePythonExecutable(),
      scriptPath,
      $"--model-dir \"{temp.DirectoryPath}\" --fixture-mode",
      TimeSpan.FromSeconds(10));

    CohereWorkerResponse response = await client.InvokeAsync<CohereWorkerResponse>(
      new CohereWorkerRequest(audioPath, "es", Punctuation: false),
      TimeSpan.FromSeconds(10));

    Xunit.Assert.Equal("fixture cohere transcript in es", response.Text);
    Xunit.Assert.Equal("es", response.Language);
    Xunit.Assert.False(response.Punctuation);
    Xunit.Assert.Equal(16_000, response.SampleRateHz);
    Xunit.Assert.True(response.DurationMs >= 0);
  }

  private static string ResolvePythonExecutable()
  {
    string? configured = Environment.GetEnvironmentVariable("DICTATEANYWHERE_LOCAL_MODEL_PYTHON");
    return string.IsNullOrWhiteSpace(configured) ? "python" : configured.Trim();
  }

  private static byte[] CreateMinimalWaveFile()
  {
    return
    [
      0x52, 0x49, 0x46, 0x46, 0x2C, 0x00, 0x00, 0x00,
      0x57, 0x41, 0x56, 0x45, 0x66, 0x6D, 0x74, 0x20,
      0x10, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00,
      0x80, 0x3E, 0x00, 0x00, 0x00, 0x7D, 0x00, 0x00,
      0x02, 0x00, 0x10, 0x00, 0x64, 0x61, 0x74, 0x61,
      0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
      0x00, 0x00, 0x00, 0x00,
    ];
  }

  private sealed record CohereWorkerRequest(string AudioPath, string Language, bool Punctuation);

  private sealed record CohereWorkerResponse(
    string Text,
    double DurationMs,
    string Language,
    bool Punctuation,
    int SampleRateHz,
    double AudioSeconds);

  private sealed class TempDirectoryScope : IDisposable
  {
    public TempDirectoryScope()
    {
      DirectoryPath = Path.Combine(
        Path.GetTempPath(),
        "DictateAnywhere.ProviderWorkerContractSmokeTests",
        Guid.NewGuid().ToString("N"));
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

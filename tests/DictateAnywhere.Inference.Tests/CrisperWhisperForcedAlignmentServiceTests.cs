using System.Reflection;
using System.Text.Json;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.Inference.Tests;

public sealed class CrisperWhisperForcedAlignmentServiceTests
{
  [Xunit.Fact]
  public void Defaults_PreferTheCpuPracticalTurboAlignerAndBoundRecoveryTime()
  {
    CrisperWhisperForcedAlignmentOptions options = CrisperWhisperForcedAlignmentOptions.Default;

    Xunit.Assert.Equal("crisperwhisper-2-turbo", options.PreferredModelIds[0]);
    Xunit.Assert.Equal(TimeSpan.FromMinutes(4), options.RequestTimeout);
  }

  [Xunit.Fact]
  public async Task AlignAsync_UsesKnownTranscriptAndReturnsAudioGroundedWords()
  {
    using TempDirectoryScope scope = new();
    string scriptName = $"alignment-test-{Guid.NewGuid():N}.py";
    string scriptPath = Path.Combine(AppContext.BaseDirectory, "local-models", scriptName);
    File.WriteAllText(scriptPath, "# test worker");
    string modelPath = Path.Combine(scope.DirectoryPath, "crisper-whisper-local", "crisperwhisper-2-turbo");
    Directory.CreateDirectory(modelPath);
    File.WriteAllText(Path.Combine(modelPath, "config.json"), "{}");
    string audioPath = Path.Combine(scope.DirectoryPath, "speech.wav");
    File.WriteAllBytes(audioPath, []);
    try
    {
      await using FakeWorkerClient worker = new();
      await using CrisperWhisperForcedAlignmentService service = new(
        new CrisperWhisperForcedAlignmentOptions(
          "python",
          scope.DirectoryPath,
          ["crisperwhisper-2-turbo"],
          scriptName,
          TimeSpan.FromSeconds(1),
          TimeSpan.FromSeconds(1)),
        new FakeWorkerClientFactory(worker));

      SpeechAlignmentResult result = await service.AlignAsync(new SpeechAlignmentRequest(
        audioPath,
        "Hello world.",
        "en-gb"));

      Xunit.Assert.Equal(CrisperWhisperForcedAlignmentService.ProviderId, result.ProviderId);
      Xunit.Assert.Equal(["Hello", "world"], result.Words.Select(word => word.Text));
      Xunit.Assert.Equal(TimeSpan.FromSeconds(0.8), result.Words[1].Start);
      Xunit.Assert.Equal("Hello world.", GetProperty<string>(worker.Requests.Single(), "Transcript"));
      Xunit.Assert.Equal("en", GetProperty<string>(worker.Requests.Single(), "Language"));
    }
    finally
    {
      if (File.Exists(scriptPath))
      {
        File.Delete(scriptPath);
      }
    }
  }

  private static T GetProperty<T>(object source, string name)
  {
    PropertyInfo property = source.GetType().GetProperty(name)
      ?? throw new InvalidOperationException($"Missing test request property '{name}'.");
    return (T)(property.GetValue(source) ?? throw new InvalidOperationException("Test request property was null."));
  }

  private sealed class FakeWorkerClientFactory(FakeWorkerClient worker) : IPersistentWorkerClientFactory
  {
    public IPersistentWorkerClient Create(string pythonExecutablePath, string scriptPath, string arguments, TimeSpan startupTimeout) => worker;
  }

  private sealed class FakeWorkerClient : IPersistentWorkerClient
  {
    public List<object> Requests { get; } = [];

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<TResponse> InvokeAsync<TResponse>(object request, TimeSpan requestTimeout, CancellationToken cancellationToken = default)
    {
      Requests.Add(request);
      string envelope = JsonSerializer.Serialize(new
      {
        status = "ok",
        payload = new
        {
          words = new[]
          {
            new { text = "Hello", start_seconds = 0.2, end_seconds = 0.6 },
            new { text = "world", start_seconds = 0.8, end_seconds = 1.3 },
          },
        },
      });
      return Task.FromResult(PersistentPythonWorkerClient.DeserializePayload<TResponse>(envelope));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private sealed class TempDirectoryScope : IDisposable
  {
    public TempDirectoryScope()
    {
      DirectoryPath = Path.Combine(Path.GetTempPath(), "DictateAnywhere.Alignment.Tests", Guid.NewGuid().ToString("N"));
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

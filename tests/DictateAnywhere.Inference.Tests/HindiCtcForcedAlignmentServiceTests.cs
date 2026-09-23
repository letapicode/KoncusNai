using System.Reflection;
using System.Text.Json;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.Inference.Tests;

public sealed class HindiCtcForcedAlignmentServiceTests
{
  [Xunit.Fact]
  public async Task AlignAsync_UsesKnownHindiTextAndReturnsDirectWordTimings()
  {
    using TempDirectoryScope scope = new();
    string scriptName = $"hindi-alignment-test-{Guid.NewGuid():N}.py";
    string scriptPath = Path.Combine(AppContext.BaseDirectory, "local-models", scriptName);
    File.WriteAllText(scriptPath, "# test worker");
    string audioPath = Path.Combine(scope.DirectoryPath, "speech.wav");
    File.WriteAllBytes(audioPath, []);
    try
    {
      await using FakeWorkerClient worker = new();
      await using HindiCtcForcedAlignmentService service = new(
        new HindiCtcForcedAlignmentOptions(
          "python",
          Path.Combine(scope.DirectoryPath, "indicwav2vec-hindi"),
          "Harveenchadha/vakyansh-wav2vec2-hindi-him-4200",
          scriptName,
          TimeSpan.FromSeconds(1),
          TimeSpan.FromSeconds(1)),
        new FakeWorkerClientFactory(worker));

      SpeechAlignmentResult result = await service.AlignAsync(new SpeechAlignmentRequest(
        audioPath,
        "नमस्ते दुनिया",
        "hi"));

      Xunit.Assert.Equal(HindiCtcForcedAlignmentService.ProviderId, result.ProviderId);
      Xunit.Assert.Equal(["नमस्ते", "दुनिया"], result.Words.Select(word => word.Text));
      Xunit.Assert.Equal("नमस्ते दुनिया", GetProperty<string>(worker.Requests.Single(), "Transcript"));
    }
    finally
    {
      if (File.Exists(scriptPath))
      {
        File.Delete(scriptPath);
      }
    }
  }

  [Xunit.Fact]
  public async Task Router_UsesHindiEngineOnlyForHindi()
  {
    await using FakeAlignmentService general = new("general");
    await using FakeAlignmentService hindi = new("hindi");
    await using ReaderSpeechAlignmentService router = new(general, hindi);

    await router.AlignAsync(new SpeechAlignmentRequest("a.wav", "Hello", "en"));
    await router.AlignAsync(new SpeechAlignmentRequest("b.wav", "नमस्ते", "hi"));

    Xunit.Assert.Equal(1, general.Requests);
    Xunit.Assert.Equal(1, hindi.Requests);
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
            new { text = "नमस्ते", start_seconds = 0.1, end_seconds = 0.5 },
            new { text = "दुनिया", start_seconds = 0.6, end_seconds = 1.0 },
          },
        },
      });
      return Task.FromResult(PersistentPythonWorkerClient.DeserializePayload<TResponse>(envelope));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private sealed class FakeAlignmentService(string providerId) : ISpeechAlignmentService
  {
    public int Requests { get; private set; }

    public Task<SpeechAlignmentResult> AlignAsync(SpeechAlignmentRequest request, CancellationToken cancellationToken = default)
    {
      Requests++;
      return Task.FromResult(new SpeechAlignmentResult([], providerId, TimeSpan.Zero));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private sealed class TempDirectoryScope : IDisposable
  {
    public TempDirectoryScope()
    {
      DirectoryPath = Path.Combine(Path.GetTempPath(), "DictateAnywhere.HindiAlignment.Tests", Guid.NewGuid().ToString("N"));
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

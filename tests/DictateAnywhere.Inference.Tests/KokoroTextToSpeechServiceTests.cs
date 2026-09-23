using System.Reflection;
using System.Text.Json;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.Inference.Tests;

public sealed class KokoroTextToSpeechServiceTests
{
  [Xunit.Fact]
  public async Task SynthesizeAsync_SplitsLongTextAndUsesTheConfiguredKokoroVoice()
  {
    using TempDirectoryScope scope = new();
    string scriptName = $"kokoro-test-{Guid.NewGuid():N}.py";
    string scriptPath = Path.Combine(AppContext.BaseDirectory, "local-models", scriptName);
    File.WriteAllText(scriptPath, "# test worker");
    try
    {
      await using FakeWorkerClient worker = new();
      await using KokoroTextToSpeechService service = new(
        KokoroTextToSpeechOptions.Default with
        {
          ScriptFileName = scriptName,
          OutputRootPath = scope.DirectoryPath,
          ModelCacheRootPath = scope.DirectoryPath,
          MaximumSegmentCharacters = 80,
          DefaultVoiceId = "af_heart",
        },
        new FakeWorkerClientFactory(worker));

      TextToSpeechResult result = await service.SynthesizeAsync(new TextToSpeechRequest(
        "A short first sentence for the model. A second sentence that makes this request span more than one segment."));

      Xunit.Assert.Equal(2, result.SegmentCount);
      Xunit.Assert.True(File.Exists(result.AudioPath));
      Xunit.Assert.Equal(KokoroTextToSpeechService.ProviderId, result.ProviderId);
      Xunit.Assert.Equal(2, worker.Requests.Count);
      Xunit.Assert.NotNull(result.WordTimings);
      Xunit.Assert.Equal(2, result.WordTimings!.Count);
      Xunit.Assert.Equal("en", GetProperty<string>(worker.Requests[0], "Language"));
      Xunit.Assert.Equal("af_heart", GetProperty<string>(worker.Requests[0], "VoiceId"));
      Xunit.Assert.Contains("af_heart", worker.LastArguments, StringComparison.Ordinal);
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
  public async Task SynthesizeAsync_PreservesHindiSentencesForThePhonemeAwareWorker()
  {
    using TempDirectoryScope scope = new();
    string scriptName = $"kokoro-hindi-test-{Guid.NewGuid():N}.py";
    string scriptPath = Path.Combine(AppContext.BaseDirectory, "local-models", scriptName);
    File.WriteAllText(scriptPath, "# test worker");
    try
    {
      await using FakeWorkerClient worker = new();
      await using KokoroTextToSpeechService service = new(
        KokoroTextToSpeechOptions.Default with
        {
          ScriptFileName = scriptName,
          OutputRootPath = scope.DirectoryPath,
          ModelCacheRootPath = scope.DirectoryPath,
          MaximumSegmentCharacters = 700,
        },
        new FakeWorkerClientFactory(worker));
      string longHindiText = string.Join(' ', Enumerable.Repeat(
        "यह हिंदी पाठ पूरा सुनाई देना चाहिए और बीच में कभी नहीं रुकना चाहिए।",
        12));

      TextToSpeechResult result = await service.SynthesizeAsync(new TextToSpeechRequest(
        longHindiText,
        Language: "hi",
        VoiceId: "hf_alpha"));

      Xunit.Assert.Equal(1, result.SegmentCount);
      Xunit.Assert.Single(worker.Requests);
      Xunit.Assert.Equal(longHindiText, GetProperty<string>(worker.Requests[0], "Text"));
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
  public async Task KalaSynthesizeAsync_UsesTheSelectedNativeNepaliVoice()
  {
    using TempDirectoryScope scope = new();
    string scriptName = $"kala-nepali-test-{Guid.NewGuid():N}.py";
    string scriptPath = Path.Combine(AppContext.BaseDirectory, "local-models", scriptName);
    File.WriteAllText(scriptPath, "# test worker");
    try
    {
      await using FakeWorkerClient worker = new();
      await using KalaNepaliTextToSpeechService service = new(
        KalaNepaliTextToSpeechOptions.Default with
        {
          ScriptFileName = scriptName,
          OutputRootPath = scope.DirectoryPath,
          ModelCacheRootPath = scope.DirectoryPath,
        },
        new FakeWorkerClientFactory(worker));

      TextToSpeechResult result = await service.SynthesizeAsync(new TextToSpeechRequest(
        "नेपाल सुन्दर देश हो।",
        Language: "ne",
        VoiceId: "slr143_F"));

      Xunit.Assert.Equal(KalaNepaliTextToSpeechService.ProviderId, result.ProviderId);
      Xunit.Assert.Equal(KalaNepaliTextToSpeechService.ModelId, result.ModelId);
      Xunit.Assert.Equal("slr143_f", GetProperty<string>(worker.Requests[0], "VoiceId"));
      Xunit.Assert.True(File.Exists(result.AudioPath));
      Xunit.Assert.NotNull(result.WordTimings);
      Xunit.Assert.Single(result.WordTimings!);
      Xunit.Assert.Equal("नेपाल", result.WordTimings![0].Text);
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
  public async Task ReaderService_UsesExplicitProviderHintsAndKeepsKokoroAsTheCompatibleDefault()
  {
    RecordingSpeechService kokoro = new("kokoro");
    RecordingSpeechService indicParler = new("indic-parler");
    await using ReaderTextToSpeechService service = new(kokoro, indicParler);

    _ = await service.SynthesizeAsync(new TextToSpeechRequest("Default English remains Kokoro."));
    _ = await service.SynthesizeAsync(new TextToSpeechRequest("नमस्ते", Language: "hi", VoiceId: "hf_alpha", ProviderId: KokoroTextToSpeechService.ProviderId));
    _ = await service.SynthesizeAsync(new TextToSpeechRequest("नमस्कार", Language: "ne", ProviderId: IndicParlerTextToSpeechService.ProviderId));
    _ = await service.SynthesizeAsync(new TextToSpeechRequest("नमस्ते", Language: "hi", ProviderId: IndicParlerTextToSpeechService.ProviderId));
    _ = await service.SynthesizeAsync(new TextToSpeechRequest("नमः", Language: "san", ProviderId: IndicParlerTextToSpeechService.ProviderId));
    _ = await service.SynthesizeAsync(new TextToSpeechRequest("こんにちは", Language: "ja"));

    Xunit.Assert.Equal(3, indicParler.CallCount);
    Xunit.Assert.Equal(3, kokoro.CallCount);
  }

  private static T GetProperty<T>(object source, string name)
  {
    PropertyInfo property = source.GetType().GetProperty(name)
      ?? throw new InvalidOperationException($"Missing test request property '{name}'.");
    return (T)(property.GetValue(source) ?? throw new InvalidOperationException("Test request property was null."));
  }

  private sealed class FakeWorkerClientFactory(FakeWorkerClient worker) : IPersistentWorkerClientFactory
  {
    public IPersistentWorkerClient Create(string pythonExecutablePath, string scriptPath, string arguments, TimeSpan startupTimeout)
    {
      worker.LastArguments = arguments;
      return worker;
    }
  }

  private sealed class RecordingSpeechService(string providerId) : ITextToSpeechService
  {
    public int CallCount { get; private set; }

    public Task<TextToSpeechResult> SynthesizeAsync(TextToSpeechRequest request, CancellationToken cancellationToken = default)
    {
      CallCount++;
      return Task.FromResult(new TextToSpeechResult("unused.wav", TimeSpan.Zero, 1, providerId, "test"));
    }
  }

  private sealed class FakeWorkerClient : IPersistentWorkerClient
  {
    public List<object> Requests { get; } = [];

    public string LastArguments { get; set; } = string.Empty;

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<TResponse> InvokeAsync<TResponse>(object request, TimeSpan requestTimeout, CancellationToken cancellationToken = default)
    {
      Requests.Add(request);
      string outputPath = GetProperty<string>(request, "OutputPath");
      WriteMinimalWave(outputPath);
      string envelope = JsonSerializer.Serialize(new
      {
        status = "ok",
        payload = new
        {
          audio_path = outputPath,
          duration_seconds = 0.00008,
          word_timings = new[] { new { text = "नेपाल", start_seconds = 0.0, end_seconds = 0.00004 } },
        },
      });
      return Task.FromResult(PersistentPythonWorkerClient.DeserializePayload<TResponse>(envelope));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static void WriteMinimalWave(string path)
    {
      Directory.CreateDirectory(Path.GetDirectoryName(path)!);
      using FileStream stream = new(path, FileMode.Create, FileAccess.Write);
      using BinaryWriter writer = new(stream);
      writer.Write("RIFF"u8.ToArray());
      writer.Write((uint)40);
      writer.Write("WAVEfmt "u8.ToArray());
      writer.Write((uint)16);
      writer.Write((ushort)1);
      writer.Write((ushort)1);
      writer.Write((uint)24000);
      writer.Write((uint)48000);
      writer.Write((ushort)2);
      writer.Write((ushort)16);
      writer.Write("data"u8.ToArray());
      writer.Write((uint)4);
      writer.Write(new byte[] { 0, 0, 0, 0 });
    }
  }

  private sealed class TempDirectoryScope : IDisposable
  {
    public TempDirectoryScope()
    {
      DirectoryPath = Path.Combine(Path.GetTempPath(), "DictateAnywhere.Kokoro.Tests", Guid.NewGuid().ToString("N"));
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

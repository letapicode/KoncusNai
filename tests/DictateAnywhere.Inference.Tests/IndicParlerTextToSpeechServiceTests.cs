using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.Inference.Tests;

public sealed class IndicParlerTextToSpeechServiceTests
{
  [Xunit.Fact]
  public void Worker_PreservesStandardHuggingFaceCredentialHome()
  {
    string scriptPath = Path.Combine(
      AppContext.BaseDirectory,
      "local-models",
      "indic_parler_tts_worker.py");
    string script = File.ReadAllText(scriptPath);

    Xunit.Assert.DoesNotContain("os.environ[\"HF_HOME\"] =", script, StringComparison.Ordinal);
    Xunit.Assert.Contains("cache_dir=arguments.cache_dir", script, StringComparison.Ordinal);
    Xunit.Assert.Contains("\"tokenizer_class\": AutoTokenizer", script, StringComparison.Ordinal);
    Xunit.Assert.Contains("description_tokenizer = runtime[\"tokenizer_class\"].from_pretrained(", script, StringComparison.Ordinal);
    Xunit.Assert.Contains("model.config.text_encoder", script, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("description_tokenizer = prompt_tokenizer", script, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public async Task SynthesizeAsync_UsesOneWorkerSequentiallyAndReturnsRuntimeMetadata()
  {
    using TempDirectoryScope temp = new();
    string scriptName = await CreatePlaceholderWorkerAsync();
    try
    {
      await using FakeWorkerClient worker = new();
      FakeWorkerClientFactory factory = new(worker);
      FakeRuntimeProvisioner provisioner = new();
      await using IndicParlerTextToSpeechService service = new(CreateOptions(temp, scriptName) with
      {
        MaximumSegmentCharacters = 80,
        EnableCache = false,
      }, factory, provisioner);

      Task<TextToSpeechResult> first = service.SynthesizeAsync(new TextToSpeechRequest(
        "संस्कृतस्य प्रथमं वाक्यम् अस्ति। द्वितीयं वाक्यम् दीर्घतरं भवति यतः सुरक्षितः खण्डः आवश्यकः।",
        "san",
        "Aryan"));
      Task<TextToSpeechResult> second = service.SynthesizeAsync(new TextToSpeechRequest(
        "नेपाली वाक्य स्पष्ट रूपमा सुनिन्छ।",
        "npi",
        "Amrita"));
      TextToSpeechResult[] results = await Task.WhenAll(first, second);

      Xunit.Assert.Equal(1, factory.CreateCount);
      Xunit.Assert.Equal(1, provisioner.EnsureCount);
      Xunit.Assert.Equal(1, worker.MaximumConcurrentInvocations);
      Xunit.Assert.True(worker.Requests.Count >= 3);
      Xunit.Assert.All(results, result => Xunit.Assert.Equal(IndicParlerTextToSpeechService.ProviderId, result.ProviderId));
      Xunit.Assert.All(results, result => Xunit.Assert.NotNull(result.RuntimeMetadata));
      Xunit.Assert.Equal("cpu", results[0].RuntimeMetadata!.Backend);
      Xunit.Assert.Equal("float32", results[0].RuntimeMetadata!.DataType);
    }
    finally
    {
      DeletePlaceholderWorker(scriptName);
    }
  }

  [Xunit.Fact]
  public async Task SynthesizeAsync_ReusesContentHashCache()
  {
    using TempDirectoryScope temp = new();
    string scriptName = await CreatePlaceholderWorkerAsync();
    try
    {
      await using FakeWorkerClient worker = new();
      await using IndicParlerTextToSpeechService service = new(
        CreateOptions(temp, scriptName) with { EnableCache = true },
        new FakeWorkerClientFactory(worker));
      TextToSpeechRequest request = new("नेपाल सुन्दर देश हो।", "ne", "Amrita", Seed: 42);

      TextToSpeechResult first = await service.SynthesizeAsync(request);
      TextToSpeechResult second = await service.SynthesizeAsync(request);

      Xunit.Assert.Single(worker.Requests);
      Xunit.Assert.Equal(first.AudioPath, second.AudioPath);
      Xunit.Assert.True(File.Exists(first.AudioPath));
      Xunit.Assert.Equal(first.RuntimeMetadata, second.RuntimeMetadata);
    }
    finally
    {
      DeletePlaceholderWorker(scriptName);
    }
  }

  [Xunit.Fact]
  public async Task SynthesizeAsync_RejectsEmptyTextLanguageAndUnknownSpeaker()
  {
    using TempDirectoryScope temp = new();
    string scriptName = await CreatePlaceholderWorkerAsync();
    try
    {
      await using FakeWorkerClient worker = new();
      await using IndicParlerTextToSpeechService service = new(
        CreateOptions(temp, scriptName),
        new FakeWorkerClientFactory(worker));

      await Xunit.Assert.ThrowsAsync<ArgumentException>(() => service.SynthesizeAsync(new TextToSpeechRequest("", "sa")));
      await Xunit.Assert.ThrowsAsync<ArgumentException>(() => service.SynthesizeAsync(new TextToSpeechRequest("नमः", "")));
      await Xunit.Assert.ThrowsAsync<ArgumentException>(() => service.SynthesizeAsync(new TextToSpeechRequest("नमः", "sa", "Invented")));
    }
    finally
    {
      DeletePlaceholderWorker(scriptName);
    }
  }

  [Xunit.Fact]
  public async Task SynthesizeAsync_RejectsOutputOutsideConfiguredRoot()
  {
    using TempDirectoryScope temp = new();
    string scriptName = await CreatePlaceholderWorkerAsync();
    try
    {
      await using FakeWorkerClient worker = new();
      await using IndicParlerTextToSpeechService service = new(
        CreateOptions(temp, scriptName),
        new FakeWorkerClientFactory(worker));
      string outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.wav");

      await Xunit.Assert.ThrowsAsync<ArgumentException>(() => service.SynthesizeAsync(
        new TextToSpeechRequest("नमः", "sa", "Aryan", OutputPath: outside)));
    }
    finally
    {
      DeletePlaceholderWorker(scriptName);
    }
  }

  [Xunit.Fact]
  public void SanskritChunking_UsesASmallerPronunciationSafeBudget()
  {
    Xunit.Assert.Equal(180, IndicParlerTextToSpeechService.ResolveMaximumSegmentCharacters("sa", 450));
    Xunit.Assert.Equal(260, IndicParlerTextToSpeechService.ResolveMaximumSegmentCharacters("ne", 450));
    Xunit.Assert.Equal(450, IndicParlerTextToSpeechService.ResolveMaximumSegmentCharacters("ta", 450));
  }

  [Xunit.Fact]
  public void EstimatedIndicTiming_CoversEverySubmittedWordAndHonorsTheSegmentOffset()
  {
    IReadOnlyList<SpeechWordTiming> timings = IndicParlerTextToSpeechService.CreateEstimatedWordTimings(
      "संस्कृतस्य वाक्यम् अस्ति।",
      TimeSpan.FromSeconds(3),
      TimeSpan.FromSeconds(2));

    Xunit.Assert.Equal(3, timings.Count);
    Xunit.Assert.Equal(TimeSpan.FromSeconds(2), timings[0].Start);
    Xunit.Assert.Equal(TimeSpan.FromSeconds(5), timings[^1].End);
    Xunit.Assert.All(timings, timing => Xunit.Assert.True(timing.End >= timing.Start));
  }

  private static IndicParlerTextToSpeechOptions CreateOptions(TempDirectoryScope temp, string scriptName)
  {
    return IndicParlerTextToSpeechOptions.Default with
    {
      ScriptFileName = scriptName,
      ModelCacheRootPath = Path.Combine(temp.DirectoryPath, "models"),
      OutputRootPath = Path.Combine(temp.DirectoryPath, "output"),
      Device = "cpu",
      DataType = "float32",
      SilenceBetweenSegmentsMilliseconds = 200,
    };
  }

  private static async Task<string> CreatePlaceholderWorkerAsync()
  {
    string scriptName = $"indic-parler-test-{Guid.NewGuid():N}.py";
    string scriptPath = Path.Combine(AppContext.BaseDirectory, "local-models", scriptName);
    await File.WriteAllTextAsync(scriptPath, "# fake worker");
    return scriptName;
  }

  private static void DeletePlaceholderWorker(string scriptName)
  {
    string scriptPath = Path.Combine(AppContext.BaseDirectory, "local-models", scriptName);
    if (File.Exists(scriptPath))
    {
      File.Delete(scriptPath);
    }
  }

  private static T GetProperty<T>(object source, string name)
  {
    PropertyInfo property = source.GetType().GetProperty(name)
      ?? throw new InvalidOperationException($"Missing property '{name}'.");
    return (T)(property.GetValue(source) ?? throw new InvalidOperationException($"Property '{name}' was null."));
  }

  private sealed class FakeWorkerClientFactory(FakeWorkerClient worker) : IPersistentWorkerClientFactory
  {
    public int CreateCount { get; private set; }

    public IPersistentWorkerClient Create(string pythonExecutablePath, string scriptPath, string arguments, TimeSpan startupTimeout)
    {
      CreateCount++;
      return worker;
    }
  }

  private sealed class FakeRuntimeProvisioner : IIndicParlerRuntimeProvisioner
  {
    public int EnsureCount { get; private set; }

    public Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
      EnsureCount++;
      return Task.CompletedTask;
    }
  }

  private sealed class FakeWorkerClient : IPersistentWorkerClient
  {
    private int activeInvocations;

    public List<object> Requests { get; } = [];

    public int MaximumConcurrentInvocations { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task<TResponse> InvokeAsync<TResponse>(object request, TimeSpan requestTimeout, CancellationToken cancellationToken = default)
    {
      int active = Interlocked.Increment(ref activeInvocations);
      MaximumConcurrentInvocations = Math.Max(MaximumConcurrentInvocations, active);
      try
      {
        Requests.Add(request);
        await Task.Delay(15, cancellationToken);
        string outputPath = GetProperty<string>(request, "OutputPath");
        WriteMinimalWave(outputPath);
        string envelope = JsonSerializer.Serialize(new
        {
          status = "ok",
          payload = new
          {
            audio_path = outputPath,
            sample_rate = 24000,
            duration_seconds = 0.01,
            selected_device = "cpu",
            backend = "cpu",
            data_type = "float32",
            gpu_name = (string?)null,
            fallback_occurred = false,
            fallback_reason = (string?)null,
            generation_seconds = 0.02,
            real_time_factor = 2.0,
            peak_memory_bytes = 100_000_000L,
          },
        });
        return PersistentPythonWorkerClient.DeserializePayload<TResponse>(envelope);
      }
      finally
      {
        Interlocked.Decrement(ref activeInvocations);
      }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static void WriteMinimalWave(string path)
    {
      Directory.CreateDirectory(Path.GetDirectoryName(path)!);
      using FileStream stream = new(path, FileMode.Create, FileAccess.Write);
      using BinaryWriter writer = new(stream);
      writer.Write("RIFF"u8.ToArray());
      writer.Write((uint)516);
      writer.Write("WAVEfmt "u8.ToArray());
      writer.Write((uint)16);
      writer.Write((ushort)1);
      writer.Write((ushort)1);
      writer.Write((uint)24000);
      writer.Write((uint)48000);
      writer.Write((ushort)2);
      writer.Write((ushort)16);
      writer.Write("data"u8.ToArray());
      writer.Write((uint)480);
      writer.Write(new byte[480]);
    }
  }

  private sealed class TempDirectoryScope : IDisposable
  {
    public TempDirectoryScope()
    {
      DirectoryPath = Path.Combine(Path.GetTempPath(), "DictateAnywhere.IndicParlerServiceTests", Guid.NewGuid().ToString("N"));
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

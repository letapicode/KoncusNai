using System;
using System.IO;
using System.Threading.Tasks;
using DictateAnywhere.Inference;

namespace DictateAnywhere.Inference.Tests;

public sealed class IndicParlerWorkerFixtureTests
{
  [Xunit.Fact]
  public async Task Worker_ForcedCpu_UsesFloat32AndReusesOneModel()
  {
    using TempDirectoryScope temp = new();
    await using PersistentPythonWorkerClient client = CreateClient(temp, "--device cpu --dtype auto --fixture-mode");

    FixtureResponse first = await InvokeAsync(client, temp, "first.wav");
    FixtureResponse second = await InvokeAsync(client, temp, "second.wav");

    Xunit.Assert.Equal("cpu", first.SelectedDevice);
    Xunit.Assert.Equal("cpu", first.Backend);
    Xunit.Assert.Equal("float32", first.DataType);
    Xunit.Assert.Equal(1, first.ModelLoadCount);
    Xunit.Assert.Equal(1, second.ModelLoadCount);
  }

  [Xunit.Fact]
  public async Task Worker_Auto_SelectsMockedCudaAndBfloat16()
  {
    using TempDirectoryScope temp = new();
    await using PersistentPythonWorkerClient client = CreateClient(
      temp,
      "--device auto --dtype auto --fixture-mode --fixture-cuda cuda --fixture-bfloat16");

    FixtureResponse response = await InvokeAsync(client, temp, "cuda.wav");

    Xunit.Assert.Equal("cuda:0", response.SelectedDevice);
    Xunit.Assert.Equal("cuda", response.Backend);
    Xunit.Assert.Equal("bfloat16", response.DataType);
    Xunit.Assert.Equal("Fixture GPU", response.GpuName);
  }

  [Xunit.Fact]
  public async Task Worker_Auto_ReportsRocmThroughPyTorchCudaBackend()
  {
    using TempDirectoryScope temp = new();
    await using PersistentPythonWorkerClient client = CreateClient(
      temp,
      "--device auto --dtype auto --fixture-mode --fixture-cuda rocm");

    FixtureResponse response = await InvokeAsync(client, temp, "rocm.wav");

    Xunit.Assert.Equal("cuda:0", response.SelectedDevice);
    Xunit.Assert.Equal("rocm", response.Backend);
  }

  [Xunit.Fact]
  public async Task Worker_Auto_SelectsMockedMpsWhenCudaIsUnavailable()
  {
    using TempDirectoryScope temp = new();
    await using PersistentPythonWorkerClient client = CreateClient(
      temp,
      "--device auto --dtype auto --fixture-mode --fixture-mps");

    FixtureResponse response = await InvokeAsync(client, temp, "mps.wav");

    Xunit.Assert.Equal("mps", response.SelectedDevice);
    Xunit.Assert.Equal("mps", response.Backend);
    Xunit.Assert.Equal("float16", response.DataType);
  }

  [Xunit.Fact]
  public async Task Worker_Auto_FallsBackWhenNoAcceleratorExists()
  {
    using TempDirectoryScope temp = new();
    await using PersistentPythonWorkerClient client = CreateClient(temp, "--device auto --dtype auto --fixture-mode");

    FixtureResponse response = await InvokeAsync(client, temp, "fallback.wav");

    Xunit.Assert.Equal("cpu", response.SelectedDevice);
    Xunit.Assert.True(response.FallbackOccurred);
    Xunit.Assert.Contains("No compatible", response.FallbackReason, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public async Task Worker_Auto_AvoidsAnUndersizedAccelerator()
  {
    using TempDirectoryScope temp = new();
    await using PersistentPythonWorkerClient client = CreateClient(
      temp,
      "--device auto --dtype auto --fixture-mode --fixture-cuda cuda --fixture-gpu-bytes 2147483648");

    FixtureResponse response = await InvokeAsync(client, temp, "low-memory.wav");

    Xunit.Assert.Equal("cpu", response.SelectedDevice);
    Xunit.Assert.True(response.FallbackOccurred);
    Xunit.Assert.Contains("2.0 GiB", response.FallbackReason, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public async Task Worker_ForcedCuda_RejectsAnUndersizedAccelerator()
  {
    using TempDirectoryScope temp = new();
    await using PersistentPythonWorkerClient client = CreateClient(
      temp,
      "--device cuda --dtype auto --fixture-mode --fixture-cuda cuda --fixture-gpu-bytes 2147483648");

    InvalidOperationException exception = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => client.StartAsync());

    Xunit.Assert.Contains("requires at least", exception.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task Worker_Auto_RecoversOnceFromSimulatedAcceleratorOutOfMemory()
  {
    using TempDirectoryScope temp = new();
    await using PersistentPythonWorkerClient client = CreateClient(
      temp,
      "--device auto --dtype auto --fixture-mode --fixture-cuda cuda --simulate-oom-once");

    FixtureResponse response = await InvokeAsync(client, temp, "oom.wav");

    Xunit.Assert.Equal("cpu", response.SelectedDevice);
    Xunit.Assert.True(response.FallbackOccurred);
    Xunit.Assert.Contains("out of memory", response.FallbackReason, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Equal(2, response.ModelLoadCount);
  }

  [Xunit.Fact]
  public async Task Worker_RejectsUnsafeForcedCpuPrecision()
  {
    using TempDirectoryScope temp = new();
    await using PersistentPythonWorkerClient client = CreateClient(temp, "--device cpu --dtype float16 --fixture-mode");

    InvalidOperationException exception = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => client.StartAsync());

    Xunit.Assert.Contains("unsupported", exception.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task Worker_DoesNotFallbackForForcedCudaOutOfMemory()
  {
    using TempDirectoryScope temp = new();
    await using PersistentPythonWorkerClient client = CreateClient(
      temp,
      "--device cuda --dtype auto --fixture-mode --fixture-cuda cuda --simulate-oom-once");

    InvalidOperationException exception = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() =>
      InvokeAsync(client, temp, "forced-oom.wav"));

    Xunit.Assert.Contains("out of memory", exception.Message, StringComparison.OrdinalIgnoreCase);
  }

  private static PersistentPythonWorkerClient CreateClient(TempDirectoryScope temp, string arguments)
  {
    string scriptPath = LocalModelScriptPathResolver.Resolve("indic_parler_tts_worker.py");
    return new PersistentPythonWorkerClient(
      "python",
      scriptPath,
      $"--cache-dir \"{temp.DirectoryPath}\" {arguments}",
      TimeSpan.FromSeconds(10));
  }

  private static Task<FixtureResponse> InvokeAsync(PersistentPythonWorkerClient client, TempDirectoryScope temp, string fileName)
  {
    return client.InvokeAsync<FixtureResponse>(
      new FixtureRequest(
        "संस्कृतं स्पष्टं श्रूयते।",
        "sa",
        "Aryan",
        "Aryan speaks clearly with natural expression.",
        42,
        Path.Combine(temp.DirectoryPath, fileName)),
      TimeSpan.FromSeconds(10));
  }

  private sealed record FixtureRequest(
    string Text,
    string Language,
    string Speaker,
    string Description,
    int Seed,
    string OutputPath);

  private sealed record FixtureResponse(
    string AudioPath,
    int SampleRate,
    double DurationSeconds,
    string SelectedDevice,
    string Backend,
    string DataType,
    string? GpuName,
    bool FallbackOccurred,
    string? FallbackReason,
    double GenerationSeconds,
    double RealTimeFactor,
    long? PeakMemoryBytes,
    int ModelLoadCount);

  private sealed class TempDirectoryScope : IDisposable
  {
    public TempDirectoryScope()
    {
      DirectoryPath = Path.Combine(Path.GetTempPath(), "DictateAnywhere.IndicParlerWorkerFixtureTests", Guid.NewGuid().ToString("N"));
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

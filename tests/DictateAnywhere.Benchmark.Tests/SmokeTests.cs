using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Benchmark.Tests;

public sealed class SmokeTests
{
  [Xunit.Fact]
  public void RecommendModel_ReturnsCurrentFastModel_WhenBalancedLatencyIsTooHigh()
  {
    string recommendation = CpuCalibrationBenchmarkService.RecommendModel(
      fastLatency: TimeSpan.FromSeconds(2.0),
      balancedLatency: TimeSpan.FromSeconds(5.0),
      accurateLatency: TimeSpan.FromSeconds(8.0));

    Xunit.Assert.Equal("crisperwhisper-2-turbo", recommendation);
  }

  [Xunit.Fact]
  public void RecommendModel_ReturnsCurrentAccurateModel_WhenSystemIsFastEnough()
  {
    string recommendation = CpuCalibrationBenchmarkService.RecommendModel(
      fastLatency: TimeSpan.FromSeconds(0.9),
      balancedLatency: TimeSpan.FromSeconds(2.1),
      accurateLatency: TimeSpan.FromSeconds(3.9));

    Xunit.Assert.Equal("crisperwhisper-2-large", recommendation);
  }

  [Xunit.Fact]
  public void RecommendModel_ReturnsCohere_ForBalancedPerformance()
  {
    string recommendation = CpuCalibrationBenchmarkService.RecommendModel(
      fastLatency: TimeSpan.FromSeconds(1.2),
      balancedLatency: TimeSpan.FromSeconds(2.9),
      accurateLatency: TimeSpan.FromSeconds(4.9));

    Xunit.Assert.Equal("cohere-transcribe-03-2026", recommendation);
  }

  [Xunit.Fact]
  public async Task RunAsync_PersistsCurrentProviderCandidates()
  {
    using TemporaryDirectoryScope scope = new();
    BenchmarkServiceOptions options = BenchmarkServiceOptions.Default with
    {
      TargetRoutineDuration = TimeSpan.FromMilliseconds(120),
      ResultFilePath = Path.Combine(scope.DirectoryPath, "benchmark-latest.json"),
      PersistResults = true,
    };

    CpuCalibrationBenchmarkService service = new(options);
    BenchmarkResult result = await service.RunAsync("en-us");
    BenchmarkResult? loaded = await service.LoadLastResultAsync();

    Xunit.Assert.NotNull(loaded);
    Xunit.Assert.Equal(result.RecommendedProviderId, loaded!.RecommendedProviderId);
    Xunit.Assert.Equal(result.RecommendedModelId, loaded.RecommendedModelId);
    Xunit.Assert.Equal("en-us", result.RequestedLanguageScope);
    Xunit.Assert.Equal("en", result.EvaluatedLanguageScope);
    Xunit.Assert.Collection(
      result.CandidateMeasurements,
      candidate =>
      {
        Xunit.Assert.Equal(TranscriptionProviderIds.CrisperWhisperLocal, candidate.ProviderId);
        Xunit.Assert.Equal("crisperwhisper-2-turbo", candidate.ModelId);
      },
      candidate =>
      {
        Xunit.Assert.Equal(TranscriptionProviderIds.CohereLocal, candidate.ProviderId);
        Xunit.Assert.Equal("cohere-transcribe-03-2026", candidate.ModelId);
      },
      candidate =>
      {
        Xunit.Assert.Equal(TranscriptionProviderIds.CrisperWhisperLocal, candidate.ProviderId);
        Xunit.Assert.Equal("crisperwhisper-2-large", candidate.ModelId);
      });
    Xunit.Assert.All(result.CandidateMeasurements, candidate => Xunit.Assert.True(candidate.RealtimeThroughput > 0d));
  }

  [Xunit.Fact]
  public async Task LoadLastResultAsync_IgnoresRetiredBenchmarkSchemas()
  {
    using TemporaryDirectoryScope scope = new();
    string resultPath = Path.Combine(scope.DirectoryPath, "retired-benchmark.json");
    await File.WriteAllTextAsync(
      resultPath,
      """
      {
        "schemaVersion": 3,
        "recommendedProviderId": "cohere-local",
        "recommendedModelId": "cohere-transcribe-03-2026"
      }
      """);
    CpuCalibrationBenchmarkService service = new(BenchmarkServiceOptions.Default with
    {
      ResultFilePath = resultPath,
      PersistResults = true,
    });

    BenchmarkResult? loaded = await service.LoadLastResultAsync();

    Xunit.Assert.Null(loaded);
  }

  [Xunit.Fact]
  public async Task RunAsync_ThrowsOperationCanceled_WhenCancellationTriggered()
  {
    BenchmarkServiceOptions options = BenchmarkServiceOptions.Default with
    {
      TargetRoutineDuration = TimeSpan.FromSeconds(2),
      PersistResults = false,
    };
    CpuCalibrationBenchmarkService service = new(options);
    using CancellationTokenSource cancellationSource = new();
    cancellationSource.CancelAfter(TimeSpan.FromMilliseconds(30));

    await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => service.RunAsync(cancellationToken: cancellationSource.Token));
  }

  private sealed class TemporaryDirectoryScope : IDisposable
  {
    public TemporaryDirectoryScope()
    {
      DirectoryPath = Path.Combine(
        Path.GetTempPath(),
        "DictateAnywhere.Tests.Benchmark",
        Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public void Dispose()
    {
      try
      {
        if (Directory.Exists(DirectoryPath))
        {
          Directory.Delete(DirectoryPath, recursive: true);
        }
      }
      catch (IOException)
      {
      }
      catch (UnauthorizedAccessException)
      {
      }
    }
  }
}

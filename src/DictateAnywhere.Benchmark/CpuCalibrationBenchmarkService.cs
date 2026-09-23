using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Benchmark;

public sealed class CpuCalibrationBenchmarkService : IBenchmarkService
{
  private readonly BenchmarkServiceOptions options;
  private readonly JsonBenchmarkResultStore resultStore;

  public CpuCalibrationBenchmarkService()
    : this(BenchmarkServiceOptions.Default)
  {
  }

  public CpuCalibrationBenchmarkService(BenchmarkServiceOptions options)
    : this(options, new JsonBenchmarkResultStore(options.ResultFilePath))
  {
  }

  internal CpuCalibrationBenchmarkService(BenchmarkServiceOptions options, JsonBenchmarkResultStore resultStore)
  {
    this.options = options ?? throw new ArgumentNullException(nameof(options));
    this.resultStore = resultStore ?? throw new ArgumentNullException(nameof(resultStore));

    if (this.options.TargetRoutineDuration <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(options), "Benchmark routine duration must be positive.");
    }

    if (this.options.BaselineOperationsPerSecond <= 0d)
    {
      throw new ArgumentOutOfRangeException(nameof(options), "Baseline operations per second must be positive.");
    }
  }

  public async Task<BenchmarkResult> RunAsync(string? languageScope = null, CancellationToken cancellationToken = default)
  {
    BenchmarkResult result = await Task.Run(
      () => RunCore(languageScope, cancellationToken),
      cancellationToken).ConfigureAwait(false);

    if (options.PersistResults)
    {
      await resultStore.SaveAsync(result, cancellationToken).ConfigureAwait(false);
    }

    return result;
  }

  public Task<BenchmarkResult?> LoadLastResultAsync(CancellationToken cancellationToken = default)
  {
    if (!options.PersistResults)
    {
      return Task.FromResult<BenchmarkResult?>(null);
    }

    return resultStore.LoadAsync(cancellationToken);
  }

  public static string RecommendModel(TimeSpan fastLatency, TimeSpan balancedLatency, TimeSpan accurateLatency)
  {
    Dictionary<BenchmarkCalibrationTier, BenchmarkModelMeasurement> measurements = new()
    {
      [BenchmarkCalibrationTier.Fast] = new(TranscriptionProviderIds.CrisperWhisperLocal, "crisperwhisper-2-turbo", BenchmarkCalibrationTier.Fast, "Fastest response", fastLatency, 1d),
      [BenchmarkCalibrationTier.Balanced] = new(TranscriptionProviderIds.CohereLocal, "cohere-transcribe-03-2026", BenchmarkCalibrationTier.Balanced, "Balanced accuracy", balancedLatency, 1d),
      [BenchmarkCalibrationTier.Accurate] = new(TranscriptionProviderIds.CrisperWhisperLocal, "crisperwhisper-2-large", BenchmarkCalibrationTier.Accurate, "Highest accuracy", accurateLatency, 1d),
    };

    return RecommendCandidateSet(measurements, BenchmarkScoringMetrics.Default).ModelId;
  }

  private BenchmarkResult RunCore(string? languageScope, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();

    DateTimeOffset startedAt = DateTimeOffset.UtcNow;
    BenchmarkCalibrationSet calibrationSet = BenchmarkStandards.ResolveCalibrationSet(languageScope);

    TimeSpan perModelWindow = TimeSpan.FromTicks(options.TargetRoutineDuration.Ticks / calibrationSet.Calibrations.Count);
    if (perModelWindow <= TimeSpan.Zero)
    {
      perModelWindow = TimeSpan.FromMilliseconds(10);
    }

    IReadOnlyDictionary<BenchmarkCalibrationTier, BenchmarkModelMeasurement> measurements = RunAllMeasurements(
      calibrationSet.Calibrations,
      perModelWindow,
      cancellationToken);

    BenchmarkRecommendationDecision recommendation = RecommendCandidateSet(measurements, options.ScoringMetrics);

    List<BenchmarkCandidateMeasurement> candidates = calibrationSet.Calibrations
      .OrderBy(calibration => calibration.Tier)
      .Select(calibration =>
      {
        BenchmarkModelMeasurement measurement = measurements[calibration.Tier];
        return new BenchmarkCandidateMeasurement(
          ProviderId: calibration.ProviderId,
          ModelId: calibration.ModelId,
          TradeoffLabel: calibration.TradeoffLabel,
          Latency: measurement.Latency,
          RealtimeThroughput: measurement.RealtimeThroughput);
      })
      .ToList();

    return new BenchmarkResult(
      RequestedLanguageScope: calibrationSet.RequestedLanguageScope,
      EvaluatedLanguageScope: calibrationSet.EvaluatedLanguageScope,
      RecommendedProviderId: recommendation.ProviderId,
      RecommendedModelId: recommendation.ModelId,
      RecommendationNotes: BuildRecommendationNotes(calibrationSet, recommendation, options.ScoringMetrics),
      CandidateMeasurements: candidates,
      BenchmarkClipId: options.Clip.ClipId,
      BenchmarkClipDuration: options.Clip.Duration,
      RoutineDuration: DateTimeOffset.UtcNow - startedAt,
      ExecutedAtUtc: DateTimeOffset.UtcNow);
  }

  private IReadOnlyDictionary<BenchmarkCalibrationTier, BenchmarkModelMeasurement> RunAllMeasurements(
    IReadOnlyList<BenchmarkModelCalibration> calibrations,
    TimeSpan perModelWindow,
    CancellationToken cancellationToken)
  {
    Dictionary<BenchmarkCalibrationTier, BenchmarkModelMeasurement> results = new();

    foreach (BenchmarkModelCalibration calibration in calibrations)
    {
      cancellationToken.ThrowIfCancellationRequested();
      BenchmarkModelMeasurement measurement = MeasureModel(calibration, perModelWindow, cancellationToken);
      results[calibration.Tier] = measurement;
    }

    BenchmarkCalibrationTier[] requiredTiers =
    [
      BenchmarkCalibrationTier.Fast,
      BenchmarkCalibrationTier.Balanced,
      BenchmarkCalibrationTier.Accurate,
    ];

    foreach (BenchmarkCalibrationTier tier in requiredTiers)
    {
      if (!results.ContainsKey(tier))
      {
        throw new InvalidOperationException($"Benchmark candidate set is missing required tier '{tier}'.");
      }
    }

    return results;
  }

  private BenchmarkModelMeasurement MeasureModel(
    BenchmarkModelCalibration calibration,
    TimeSpan window,
    CancellationToken cancellationToken)
  {
    double operationsPerSecond = MeasureOperationsPerSecond(window, cancellationToken);
    double normalizedCpu = Math.Max(operationsPerSecond / options.BaselineOperationsPerSecond, 0.05d);

    double realtimeFactor = calibration.BaselineRealtimeFactor / normalizedCpu;
    realtimeFactor = Math.Clamp(realtimeFactor, calibration.MinRealtimeFactor, calibration.MaxRealtimeFactor);

    TimeSpan latency = TimeSpan.FromSeconds(Math.Max(options.Clip.Duration.TotalSeconds * realtimeFactor, 0.01d));
    double realtimeThroughput = options.Clip.Duration.TotalSeconds / latency.TotalSeconds;

    return new BenchmarkModelMeasurement(
      ProviderId: calibration.ProviderId,
      ModelId: calibration.ModelId,
      Tier: calibration.Tier,
      TradeoffLabel: calibration.TradeoffLabel,
      Latency: latency,
      RealtimeThroughput: realtimeThroughput);
  }

  private static BenchmarkRecommendationDecision RecommendCandidateSet(
    IReadOnlyDictionary<BenchmarkCalibrationTier, BenchmarkModelMeasurement> measurements,
    BenchmarkScoringMetrics thresholds)
  {
    BenchmarkModelMeasurement fast = measurements[BenchmarkCalibrationTier.Fast];
    BenchmarkModelMeasurement balanced = measurements[BenchmarkCalibrationTier.Balanced];
    BenchmarkModelMeasurement accurate = measurements[BenchmarkCalibrationTier.Accurate];

    if (balanced.Latency > thresholds.DowngradeToFastWhenBalancedLatencyExceeds)
    {
      return new BenchmarkRecommendationDecision(fast.ProviderId, fast.ModelId, fast.TradeoffLabel, BenchmarkCalibrationTier.Fast);
    }

    bool shouldUpgradeToAccurate =
      accurate.Latency <= thresholds.UpgradeToAccurateWhenAccurateLatencyAtOrBelow
      && balanced.Latency <= thresholds.UpgradeToAccurateWhenBalancedLatencyAtOrBelow;

    return shouldUpgradeToAccurate
      ? new BenchmarkRecommendationDecision(accurate.ProviderId, accurate.ModelId, accurate.TradeoffLabel, BenchmarkCalibrationTier.Accurate)
      : new BenchmarkRecommendationDecision(balanced.ProviderId, balanced.ModelId, balanced.TradeoffLabel, BenchmarkCalibrationTier.Balanced);
  }

  private static string BuildRecommendationNotes(
    BenchmarkCalibrationSet calibrationSet,
    BenchmarkRecommendationDecision recommendation,
    BenchmarkScoringMetrics scoringMetrics)
  {
    string scopeNote = string.Empty;
    if (calibrationSet.UsedCrossLanguageFallback)
    {
      scopeNote =
        $"No calibrated models are available for '{calibrationSet.RequestedLanguageScope}'. " +
        $"Benchmarked '{calibrationSet.EvaluatedLanguageScope}' candidates instead. ";
    }
    else if (!string.Equals(
      calibrationSet.RequestedLanguageScope,
      calibrationSet.EvaluatedLanguageScope,
      StringComparison.OrdinalIgnoreCase))
    {
      scopeNote =
        $"Requested language '{calibrationSet.RequestedLanguageScope}' maps to calibrated scope " +
        $"'{calibrationSet.EvaluatedLanguageScope}'. ";
    }

    string recommendationNote = recommendation.Tier switch
    {
      BenchmarkCalibrationTier.Fast =>
        $"Recommend {recommendation.ModelId}: {recommendation.TradeoffLabel}. " +
        $"The balanced candidate exceeded the {scoringMetrics.DowngradeToFastWhenBalancedLatencyExceeds.TotalSeconds:F1}s latency budget, " +
        "so responsiveness takes priority over accuracy.",
      BenchmarkCalibrationTier.Accurate =>
        $"Recommend {recommendation.ModelId}: {recommendation.TradeoffLabel}. " +
        $"The higher-accuracy candidate stayed within the {scoringMetrics.UpgradeToAccurateWhenAccurateLatencyAtOrBelow.TotalSeconds:F1}s upgrade budget " +
        $"while the balanced candidate remained within {scoringMetrics.UpgradeToAccurateWhenBalancedLatencyAtOrBelow.TotalSeconds:F1}s, " +
        "so the benchmark favors accuracy without sacrificing responsiveness.",
      _ =>
        $"Recommend {recommendation.ModelId}: {recommendation.TradeoffLabel}. " +
        "The fastest candidate is quicker but lower accuracy, while the highest-accuracy candidate does not clear the upgrade budget.",
    };

    return scopeNote + recommendationNote;
  }

  private static double MeasureOperationsPerSecond(TimeSpan duration, CancellationToken cancellationToken)
  {
    Stopwatch stopwatch = Stopwatch.StartNew();
    long operations = 0;
    ulong state = 0x9E3779B97F4A7C15UL;

    while (stopwatch.Elapsed < duration)
    {
      cancellationToken.ThrowIfCancellationRequested();
      for (int i = 0; i < 50_000; i++)
      {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
      }

      operations += 50_000;
    }

    GC.KeepAlive(state);
    double elapsedSeconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001d);
    return operations / elapsedSeconds;
  }

  private sealed record BenchmarkModelMeasurement(
    string ProviderId,
    string ModelId,
    BenchmarkCalibrationTier Tier,
    string TradeoffLabel,
    TimeSpan Latency,
    double RealtimeThroughput);

  private sealed record BenchmarkRecommendationDecision(
    string ProviderId,
    string ModelId,
    string TradeoffLabel,
    BenchmarkCalibrationTier Tier);
}

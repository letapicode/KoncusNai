using System;
using System.Linq;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Benchmarking;

internal static class BenchmarkResultSummaryFormatter
{
  public static string Format(string label, BenchmarkResult result)
  {
    ArgumentNullException.ThrowIfNull(result);

    string scope = string.Equals(
      result.RequestedLanguageScope,
      result.EvaluatedLanguageScope,
      StringComparison.OrdinalIgnoreCase)
      ? result.EvaluatedLanguageScope
      : $"{result.RequestedLanguageScope}->{result.EvaluatedLanguageScope}";

    string candidates = string.Join(
      "; ",
      result.CandidateMeasurements.Select(candidate =>
        $"{FormatModelIdentity(candidate.ProviderId, candidate.ModelId)} [{candidate.TradeoffLabel}] {candidate.Latency.TotalSeconds:F1}s/{candidate.RealtimeThroughput:F2}x"));

    return
      $"{label}: scope={scope}, clip={result.BenchmarkClipId} ({result.BenchmarkClipDuration.TotalSeconds:F1}s), " +
      $"recommend={FormatModelIdentity(result.RecommendedProviderId, result.RecommendedModelId)}, candidates={candidates}. " +
      $"Notes: {result.RecommendationNotes}";
  }

  private static string FormatModelIdentity(string providerId, string modelId)
  {
    return $"{providerId}/{modelId}";
  }
}

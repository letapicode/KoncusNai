using System;
using System.Collections.Generic;
using System.Linq;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Benchmark;

public sealed record BenchmarkScoringMetrics(
  TimeSpan DowngradeToFastWhenBalancedLatencyExceeds,
  TimeSpan UpgradeToAccurateWhenAccurateLatencyAtOrBelow,
  TimeSpan UpgradeToAccurateWhenBalancedLatencyAtOrBelow)
{
  public static BenchmarkScoringMetrics Default { get; } = new(
    DowngradeToFastWhenBalancedLatencyExceeds: TimeSpan.FromSeconds(4.5),
    UpgradeToAccurateWhenAccurateLatencyAtOrBelow: TimeSpan.FromSeconds(4.2),
    UpgradeToAccurateWhenBalancedLatencyAtOrBelow: TimeSpan.FromSeconds(2.8));
}

public enum BenchmarkCalibrationTier
{
  Fast,
  Balanced,
  Accurate,
}

public sealed record BenchmarkModelCalibration(
  string ProviderId,
  string ModelId,
  BenchmarkCalibrationTier Tier,
  string TradeoffLabel,
  double BaselineRealtimeFactor,
  double MinRealtimeFactor,
  double MaxRealtimeFactor,
  IReadOnlyList<string> SupportedLanguages);

internal sealed record BenchmarkCalibrationSet(
  string RequestedLanguageScope,
  string EvaluatedLanguageScope,
  bool UsedCrossLanguageFallback,
  IReadOnlyList<BenchmarkModelCalibration> Calibrations);

public static class BenchmarkStandards
{
  public static IReadOnlyList<BenchmarkClipDefinition> StandardClips { get; } =
  [
    new BenchmarkClipDefinition(
      ClipId: "dictation-office-v1-short",
      DisplayName: "Office Dictation Short",
      Duration: TimeSpan.FromSeconds(6.0),
      EstimatedWordCount: 19),
    new BenchmarkClipDefinition(
      ClipId: "dictation-office-v1-main",
      DisplayName: "Office Dictation Main",
      Duration: TimeSpan.FromSeconds(10.0),
      EstimatedWordCount: 33),
  ];

  public static BenchmarkClipDefinition PrimaryClip => StandardClips[1];

  public static IReadOnlyList<BenchmarkModelCalibration> ModelCalibrations { get; } =
  [
    new BenchmarkModelCalibration(
      ProviderId: TranscriptionProviderIds.CrisperWhisperLocal,
      ModelId: "crisperwhisper-2-turbo",
      Tier: BenchmarkCalibrationTier.Fast,
      TradeoffLabel: "Fastest response",
      BaselineRealtimeFactor: 0.33,
      MinRealtimeFactor: 0.12,
      MaxRealtimeFactor: 2.2,
      SupportedLanguages: ["en"]),
    new BenchmarkModelCalibration(
      ProviderId: TranscriptionProviderIds.CohereLocal,
      ModelId: "cohere-transcribe-03-2026",
      Tier: BenchmarkCalibrationTier.Balanced,
      TradeoffLabel: "Balanced accuracy",
      BaselineRealtimeFactor: 0.45,
      MinRealtimeFactor: 0.18,
      MaxRealtimeFactor: 2.8,
      SupportedLanguages: ["en"]),
    new BenchmarkModelCalibration(
      ProviderId: TranscriptionProviderIds.CrisperWhisperLocal,
      ModelId: "crisperwhisper-2-large",
      Tier: BenchmarkCalibrationTier.Accurate,
      TradeoffLabel: "Highest accuracy",
      BaselineRealtimeFactor: 0.90,
      MinRealtimeFactor: 0.32,
      MaxRealtimeFactor: 4.8,
      SupportedLanguages: ["en"]),
  ];

  internal static BenchmarkCalibrationSet ResolveCalibrationSet(string? requestedLanguageScope)
  {
    string requested = TranscriptionLanguageSettings.NormalizeGlobal(requestedLanguageScope);
    string primaryRequested = GetPrimarySubtag(requested);

    IReadOnlyList<BenchmarkModelCalibration> exactMatches = ResolveSupportedCalibrations(requested);
    if (exactMatches.Count > 0)
    {
      return new BenchmarkCalibrationSet(
        RequestedLanguageScope: requested,
        EvaluatedLanguageScope: requested,
        UsedCrossLanguageFallback: false,
        Calibrations: exactMatches);
    }

    IReadOnlyList<BenchmarkModelCalibration> primaryMatches = ResolveSupportedCalibrations(primaryRequested);
    if (primaryMatches.Count > 0)
    {
      return new BenchmarkCalibrationSet(
        RequestedLanguageScope: requested,
        EvaluatedLanguageScope: primaryRequested,
        UsedCrossLanguageFallback: false,
        Calibrations: primaryMatches);
    }

    IReadOnlyList<BenchmarkModelCalibration> fallbackMatches = ResolveSupportedCalibrations(TranscriptionLanguageSettings.DefaultLanguage);
    if (fallbackMatches.Count == 0)
    {
      throw new InvalidOperationException("Benchmark calibration set is missing the default language candidate set.");
    }

    return new BenchmarkCalibrationSet(
      RequestedLanguageScope: requested,
      EvaluatedLanguageScope: TranscriptionLanguageSettings.DefaultLanguage,
      UsedCrossLanguageFallback: !string.Equals(primaryRequested, TranscriptionLanguageSettings.DefaultLanguage, StringComparison.OrdinalIgnoreCase),
      Calibrations: fallbackMatches);
  }

  private static IReadOnlyList<BenchmarkModelCalibration> ResolveSupportedCalibrations(string languageScope)
  {
    List<BenchmarkModelCalibration> matches = ModelCalibrations
      .Where(calibration => calibration.SupportedLanguages.Any(language =>
        string.Equals(language, languageScope, StringComparison.OrdinalIgnoreCase)))
      .OrderBy(calibration => calibration.Tier)
      .ToList();

    if (matches.Count == 0)
    {
      return Array.Empty<BenchmarkModelCalibration>();
    }

    ValidateRecommendationTiers(matches, languageScope);
    return matches;
  }

  private static void ValidateRecommendationTiers(
    IReadOnlyList<BenchmarkModelCalibration> calibrations,
    string languageScope)
  {
    BenchmarkCalibrationTier[] requiredTiers =
    [
      BenchmarkCalibrationTier.Fast,
      BenchmarkCalibrationTier.Balanced,
      BenchmarkCalibrationTier.Accurate,
    ];

    foreach (BenchmarkCalibrationTier tier in requiredTiers)
    {
      if (!calibrations.Any(calibration => calibration.Tier == tier))
      {
        throw new InvalidOperationException(
          $"Benchmark calibration set for '{languageScope}' is missing required tier '{tier}'.");
      }
    }
  }

  private static string GetPrimarySubtag(string languageTag)
  {
    int separatorIndex = languageTag.IndexOf('-');
    return separatorIndex > 0
      ? languageTag[..separatorIndex]
      : languageTag;
  }
}

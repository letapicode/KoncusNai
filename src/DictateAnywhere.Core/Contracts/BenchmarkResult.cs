using System;
using System.Collections.Generic;

namespace DictateAnywhere.Core.Contracts;

public sealed record BenchmarkResult(
  string RequestedLanguageScope,
  string EvaluatedLanguageScope,
  string RecommendedProviderId,
  string RecommendedModelId,
  string RecommendationNotes,
  IReadOnlyList<BenchmarkCandidateMeasurement> CandidateMeasurements,
  string BenchmarkClipId,
  TimeSpan BenchmarkClipDuration,
  TimeSpan RoutineDuration,
  DateTimeOffset ExecutedAtUtc);

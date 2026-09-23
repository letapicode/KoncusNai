using System;

namespace DictateAnywhere.Benchmark;

public sealed record BenchmarkClipDefinition(
  string ClipId,
  string DisplayName,
  TimeSpan Duration,
  int EstimatedWordCount);

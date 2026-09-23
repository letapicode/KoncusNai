using System;
using System.IO;

namespace DictateAnywhere.Benchmark;

public sealed record BenchmarkServiceOptions(
  TimeSpan TargetRoutineDuration,
  double BaselineOperationsPerSecond,
  BenchmarkClipDefinition Clip,
  BenchmarkScoringMetrics ScoringMetrics,
  string ResultFilePath,
  bool PersistResults)
{
  public static BenchmarkServiceOptions Default { get; } = new(
    TargetRoutineDuration: TimeSpan.FromSeconds(12),
    BaselineOperationsPerSecond: 80_000_000d,
    Clip: BenchmarkStandards.PrimaryClip,
    ScoringMetrics: BenchmarkScoringMetrics.Default,
    ResultFilePath: Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DictateAnywhere",
      "benchmark",
      "latest-benchmark.json"),
    PersistResults: true);
}

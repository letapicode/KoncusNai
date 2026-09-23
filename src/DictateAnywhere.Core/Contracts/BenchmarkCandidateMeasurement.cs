using System;

namespace DictateAnywhere.Core.Contracts;

public sealed record BenchmarkCandidateMeasurement(
  string ProviderId,
  string ModelId,
  string TradeoffLabel,
  TimeSpan Latency,
  double RealtimeThroughput);

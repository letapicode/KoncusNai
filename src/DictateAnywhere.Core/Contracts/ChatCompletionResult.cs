using System;

namespace DictateAnywhere.Core.Contracts;

public sealed record ChatCompletionResult(
  string Text,
  string ProviderId,
  string ModelId,
  TimeSpan Duration,
  bool WasTruncated = false);

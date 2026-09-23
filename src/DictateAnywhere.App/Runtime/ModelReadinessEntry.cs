using System;

namespace DictateAnywhere.App.Runtime;

public sealed record ModelReadinessEntry(
  string ProviderId,
  string ModelId,
  string DisplayName,
  ModelReadinessState State,
  TimeSpan? WarmupDuration,
  string? ErrorMessage);

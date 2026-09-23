using System;
using System.Collections.Generic;

namespace DictateAnywhere.App.Runtime;

public sealed record ModelReadinessSnapshot(
  IReadOnlyList<ModelReadinessEntry> Entries,
  DateTimeOffset UpdatedUtc)
{
  public static ModelReadinessSnapshot Empty { get; } = new(Array.Empty<ModelReadinessEntry>(), DateTimeOffset.UtcNow);
}

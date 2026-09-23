using System.Collections.Generic;

namespace DictateAnywhere.Core.Contracts;

public sealed record ModelInfo(
  string ProviderId,
  string ModelId,
  string DisplayName,
  bool IsInstalled,
  bool IsActive,
  IReadOnlyList<string> SupportedLanguages);

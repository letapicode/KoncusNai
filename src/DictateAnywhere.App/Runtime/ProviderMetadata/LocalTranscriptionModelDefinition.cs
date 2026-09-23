using System.Collections.Generic;

namespace DictateAnywhere.App.Runtime;

internal sealed record LocalTranscriptionModelDefinition(
  string ModelId,
  string DisplayName,
  string RepositoryId,
  string Revision,
  IReadOnlyList<string> SupportedLanguages,
  IReadOnlyList<string> IgnorePatterns,
  bool RequiresAuthentication = false);

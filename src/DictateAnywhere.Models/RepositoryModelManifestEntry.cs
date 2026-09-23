using System.Collections.Generic;

namespace DictateAnywhere.Models;

public sealed record RepositoryModelManifestEntry(
  string ModelId,
  string DisplayName,
  string RepositoryId,
  IReadOnlyList<string> SupportedLanguages,
  string? Revision = null,
  IReadOnlyList<string>? AllowPatterns = null,
  IReadOnlyList<string>? IgnorePatterns = null,
  bool RequiresAuthentication = false,
  IReadOnlyList<RepositoryModelAuxiliaryEntry>? AuxiliaryRepositories = null);

public sealed record RepositoryModelAuxiliaryEntry(
  string DirectoryName,
  string DisplayName,
  string RepositoryId,
  string? Revision = null,
  IReadOnlyList<string>? AllowPatterns = null,
  IReadOnlyList<string>? IgnorePatterns = null,
  bool RequiresAuthentication = false,
  string RequiredFileName = "config.json");

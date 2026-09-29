namespace DictateAnywhere.Diagnostics;

public sealed record DiagnosticsBundleExportOptions(
  bool IncludeSensitiveData = false,
  bool IncludeSettings = true,
  bool IncludeHistory = false,
  bool IncludeAudio = false,
  string? ApplicationVersion = null,
  string? LogFilePrefix = null,
  bool MetadataOnlyLogs = false,
  bool AllowlistedSettings = false)
{
  public static DiagnosticsBundleExportOptions Default { get; } = new();
}

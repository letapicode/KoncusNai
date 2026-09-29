namespace DictateAnywhere.Diagnostics;

public sealed record DiagnosticsBundleExportOptions(
  bool IncludeSensitiveData = false,
  bool IncludeSettings = true,
  bool IncludeHistory = false,
  bool IncludeAudio = false)
{
  public string? ApplicationVersion { get; init; }
  public string? LogFilePrefix { get; init; }
  public bool MetadataOnlyLogs { get; init; }
  public bool AllowlistedSettings { get; init; }
  public static DiagnosticsBundleExportOptions Default { get; } = new();
}

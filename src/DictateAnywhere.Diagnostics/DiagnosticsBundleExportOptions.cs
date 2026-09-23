namespace DictateAnywhere.Diagnostics;

public sealed record DiagnosticsBundleExportOptions(
  bool IncludeSensitiveData = false,
  bool IncludeSettings = true,
  bool IncludeHistory = false,
  bool IncludeAudio = false)
{
  public static DiagnosticsBundleExportOptions Default { get; } = new();
}

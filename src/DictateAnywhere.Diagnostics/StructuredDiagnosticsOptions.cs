using System;
using System.IO;

namespace DictateAnywhere.Diagnostics;

public sealed record StructuredDiagnosticsOptions(
  string LogsDirectoryPath,
  string FileNamePrefix,
  int MaxFileSizeBytes,
  int RetainedFileCount,
  bool IncludeSensitiveData)
{
  public static StructuredDiagnosticsOptions Default { get; } = new(
    LogsDirectoryPath: Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DictateAnywhere",
      "logs"),
    FileNamePrefix: "app",
    MaxFileSizeBytes: 1_048_576,
    RetainedFileCount: 5,
    IncludeSensitiveData: false);
}

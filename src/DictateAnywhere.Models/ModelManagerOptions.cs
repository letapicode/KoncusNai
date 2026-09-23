using System;
using System.IO;

namespace DictateAnywhere.Models;

public sealed record ModelManagerOptions(
  string ModelsRootPath,
  string ActiveModelStateFileName,
  string PartialDownloadExtension,
  bool VerifyChecksumsWhenListing,
  string? BundledModelsRootPath = null)
{
  public static ModelManagerOptions Default { get; } = new(
    ModelsRootPath: Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DictateAnywhere",
      "models"),
    ActiveModelStateFileName: "active-model.txt",
    PartialDownloadExtension: ".download",
    VerifyChecksumsWhenListing: true,
    BundledModelsRootPath: Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
      "DictateAnywhere",
      "models",
      "bundled"));
}

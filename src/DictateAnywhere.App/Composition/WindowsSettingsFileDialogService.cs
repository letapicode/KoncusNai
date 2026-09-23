using System;
using System.Windows;
using Microsoft.Win32;

namespace DictateAnywhere.App.Composition;

public sealed class WindowsSettingsFileDialogService : ISettingsFileDialogService
{
  public bool TryGetImportPath(Window owner, out string path)
  {
    ArgumentNullException.ThrowIfNull(owner);

    OpenFileDialog dialog = new()
    {
      Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
      CheckFileExists = true,
      Multiselect = false,
    };

    if (dialog.ShowDialog(owner) != true)
    {
      path = string.Empty;
      return false;
    }

    path = dialog.FileName;
    return true;
  }

  public bool TryGetExportPath(Window owner, out string path)
  {
    ArgumentNullException.ThrowIfNull(owner);

    SaveFileDialog dialog = new()
    {
      Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
      AddExtension = true,
      DefaultExt = ".json",
      FileName = "dictate-anywhere-settings.json",
      OverwritePrompt = true,
    };

    if (dialog.ShowDialog(owner) != true)
    {
      path = string.Empty;
      return false;
    }

    path = dialog.FileName;
    return true;
  }
}

using System;
using System.Collections.Generic;
using System.Windows;
using DictateAnywhere.App.Workbench;
using Microsoft.Win32;

namespace DictateAnywhere.App.Composition;

public sealed class WindowsChatFileDialogService : IChatFileDialogService
{
  public bool TryGetFilePaths(Window owner, out IReadOnlyList<string> paths)
  {
    ArgumentNullException.ThrowIfNull(owner);

    OpenFileDialog dialog = new()
    {
      Filter = ChatFileImportCatalog.SupportedFileDialogFilter,
      CheckFileExists = true,
      Multiselect = true,
    };

    if (dialog.ShowDialog(owner) != true)
    {
      paths = Array.Empty<string>();
      return false;
    }

    paths = dialog.FileNames;
    return true;
  }
}

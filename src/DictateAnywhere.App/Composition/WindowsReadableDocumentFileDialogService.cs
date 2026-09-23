using System;
using System.Windows;
using DictateAnywhere.App.Workbench;
using Microsoft.Win32;

namespace DictateAnywhere.App.Composition;

internal sealed class WindowsReadableDocumentFileDialogService : IReadableDocumentFileDialogService
{
  public bool TryGetDocumentPath(Window owner, out string path)
  {
    ArgumentNullException.ThrowIfNull(owner);
    OpenFileDialog dialog = new()
    {
      Filter = ReadableDocumentTextExtractor.SupportedFileDialogFilter,
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
}

using System;
using System.Windows;
using Microsoft.Win32;

namespace DictateAnywhere.App.Composition;

internal sealed class WindowsReaderVideoExportFileDialogService : IReaderVideoExportFileDialogService
{
  public bool TryGetExportPath(Window owner, string suggestedFileName, out string path)
  {
    ArgumentNullException.ThrowIfNull(owner);
    SaveFileDialog dialog = new()
    {
      Filter = "High-quality MP4 video (*.mp4)|*.mp4",
      AddExtension = true,
      DefaultExt = ".mp4",
      FileName = suggestedFileName,
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

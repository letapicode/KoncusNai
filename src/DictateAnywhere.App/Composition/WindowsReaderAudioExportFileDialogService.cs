using System;
using System.Windows;
using Microsoft.Win32;

namespace DictateAnywhere.App.Composition;

internal sealed class WindowsReaderAudioExportFileDialogService : IReaderAudioExportFileDialogService
{
  public bool TryGetExportPath(Window owner, string suggestedFileName, out string path)
  {
    ArgumentNullException.ThrowIfNull(owner);
    SaveFileDialog dialog = new()
    {
      Filter = "Lossless WAV audiobook (*.wav)|*.wav",
      AddExtension = true,
      DefaultExt = ".wav",
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

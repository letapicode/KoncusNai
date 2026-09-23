using DictateAnywhere.App.Presentation;
using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace DictateAnywhere.App.Composition;

public sealed class WindowsChatExportFileDialogService : IChatExportFileDialogService
{
  public bool TryGetExportPath(Window owner, string chatTitle, out string path)
  {
    ArgumentNullException.ThrowIfNull(owner);
    string safeTitle = string.Concat((chatTitle ?? AppBrand.ChatTitle).Split(Path.GetInvalidFileNameChars())).Trim();
    if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = AppBrand.ChatTitle;

    SaveFileDialog dialog = new()
    {
      Filter = "Markdown document (*.md)|*.md|Rich Text document - opens in Word (*.rtf)|*.rtf",
      AddExtension = true,
      DefaultExt = ".md",
      FileName = safeTitle,
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

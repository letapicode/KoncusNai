using System.Windows;

namespace DictateAnywhere.App.Composition;

public interface IChatExportFileDialogService
{
  bool TryGetExportPath(Window owner, string chatTitle, out string path);
}

using System.Windows;

namespace DictateAnywhere.App.Composition;

public interface ISettingsFileDialogService
{
  bool TryGetImportPath(Window owner, out string path);

  bool TryGetExportPath(Window owner, out string path);
}

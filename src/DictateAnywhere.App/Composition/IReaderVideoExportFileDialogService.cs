using System.Windows;

namespace DictateAnywhere.App.Composition;

/// <summary>Chooses the destination for a high-quality locally rendered reading video.</summary>
public interface IReaderVideoExportFileDialogService
{
  bool TryGetExportPath(Window owner, string suggestedFileName, out string path);
}

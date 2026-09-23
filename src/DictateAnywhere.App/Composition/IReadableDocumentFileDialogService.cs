using System.Windows;

namespace DictateAnywhere.App.Composition;

internal interface IReadableDocumentFileDialogService
{
  bool TryGetDocumentPath(Window owner, out string path);
}

using System.Collections.Generic;
using System.Windows;

namespace DictateAnywhere.App.Composition;

public interface IChatFileDialogService
{
  bool TryGetFilePaths(Window owner, out IReadOnlyList<string> paths);
}

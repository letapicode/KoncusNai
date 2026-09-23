using System.Windows;

namespace DictateAnywhere.App.Composition;

/// <summary>Chooses the destination for a lossless, locally exported audiobook.</summary>
public interface IReaderAudioExportFileDialogService
{
  bool TryGetExportPath(Window owner, string suggestedFileName, out string path);
}

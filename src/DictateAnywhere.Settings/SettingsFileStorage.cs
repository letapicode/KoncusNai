using System.IO;

namespace DictateAnywhere.Settings;

// An instance seam, never a global filesystem override. Tests own all paths and streams.
internal class SettingsFileStorage
{
  internal virtual Stream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
  internal virtual Stream Create(string path) => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
  internal virtual void Commit(string temporary, string destination) => File.Move(temporary, destination, overwrite: true);
  internal virtual void Delete(string path) => File.Delete(path);
}

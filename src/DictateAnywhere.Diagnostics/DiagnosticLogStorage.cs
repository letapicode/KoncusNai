namespace DictateAnywhere.Diagnostics;

internal interface IDiagnosticLogStorage
{
  void CreateDirectory(string path);
  Stream Open(string path);
  string[] GetFiles(string directory, string pattern);
  DateTime GetLastWriteTimeUtc(string path);
  void Delete(string path);
}

internal sealed class DiagnosticLogStorage : IDiagnosticLogStorage
{
  public void CreateDirectory(string path) => Directory.CreateDirectory(path);
  public Stream Open(string path) => new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
  public string[] GetFiles(string directory, string pattern) => Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
  public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);
  public void Delete(string path) => File.Delete(path);
}

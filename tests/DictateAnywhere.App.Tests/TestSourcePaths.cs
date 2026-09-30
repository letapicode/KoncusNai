namespace DictateAnywhere.App.Tests;

internal static class TestSourcePaths
{
  internal static string RepositoryRoot()
  {
    for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
      if (File.Exists(Path.Combine(directory.FullName, "DictateAnywhere.sln"))) return directory.FullName;
    throw new InvalidOperationException("Unable to find the test checkout.");
  }
}

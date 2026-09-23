using System;
using System.IO;

namespace DictateAnywhere.Inference;

internal static class LocalModelScriptPathResolver
{
  public static string Resolve(string scriptFileName)
  {
    if (string.IsNullOrWhiteSpace(scriptFileName))
    {
      throw new ArgumentException("Script file name must not be empty.", nameof(scriptFileName));
    }

    string deployedPath = Path.Combine(AppContext.BaseDirectory, "local-models", scriptFileName);
    if (File.Exists(deployedPath))
    {
      return deployedPath;
    }

    string? repoPath = TryResolveFromAncestorTree(scriptFileName);
    if (repoPath is not null)
    {
      return repoPath;
    }

    throw new InvalidOperationException(
      $"Bundled local-model script '{scriptFileName}' was not found in the application output.");
  }

  private static string? TryResolveFromAncestorTree(string scriptFileName)
  {
    DirectoryInfo? current = new(AppContext.BaseDirectory);
    while (current is not null)
    {
      string candidate = Path.Combine(current.FullName, "scripts", "local-models", scriptFileName);
      if (File.Exists(candidate))
      {
        return candidate;
      }

      current = current.Parent;
    }

    return null;
  }
}

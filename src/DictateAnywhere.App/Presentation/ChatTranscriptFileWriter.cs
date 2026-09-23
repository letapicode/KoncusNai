using System;
using System.IO;
using System.Text;

namespace DictateAnywhere.App.Presentation;

/// <summary>Commits a complete chat export without exposing a partially written destination.</summary>
internal static class ChatTranscriptFileWriter
{
  private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

  public static void WriteAtomically(string path, string contents) =>
    WriteAtomically(path, contents, static (temporaryPath, text) =>
      File.WriteAllText(temporaryPath, text, Utf8WithoutBom));

  internal static void WriteAtomically(
    string path,
    string contents,
    Action<string, string> writeTemporaryFile)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    ArgumentNullException.ThrowIfNull(contents);
    ArgumentNullException.ThrowIfNull(writeTemporaryFile);

    string destinationPath = Path.GetFullPath(path);
    string? directory = Path.GetDirectoryName(destinationPath);
    if (string.IsNullOrWhiteSpace(directory))
    {
      throw new ArgumentException("The export path must include a directory.", nameof(path));
    }

    string temporaryPath = Path.Combine(
      directory,
      $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
    try
    {
      writeTemporaryFile(temporaryPath, contents);
      File.Move(temporaryPath, destinationPath, overwrite: true);
    }
    finally
    {
      TryDeleteTemporaryFile(temporaryPath);
    }
  }

  private static void TryDeleteTemporaryFile(string path)
  {
    try
    {
      File.Delete(path);
    }
    catch (IOException)
    {
      // The primary write or move failure is more useful than best-effort cleanup failure.
    }
    catch (UnauthorizedAccessException)
    {
      // The primary write or move failure is more useful than best-effort cleanup failure.
    }
  }
}

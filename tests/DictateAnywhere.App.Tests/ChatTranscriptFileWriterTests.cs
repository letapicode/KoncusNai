using System;
using System.IO;
using DictateAnywhere.App.Presentation;

namespace DictateAnywhere.App.Tests;

public sealed class ChatTranscriptFileWriterTests
{
  [Xunit.Fact]
  public void WriteAtomically_ReplacesExistingDestinationOnlyAfterCompleteWrite()
  {
    string directory = CreateTemporaryDirectory();
    try
    {
      string destination = Path.Combine(directory, "chat.md");
      File.WriteAllText(destination, "old contents");

      ChatTranscriptFileWriter.WriteAtomically(destination, "new contents");

      Xunit.Assert.Equal("new contents", File.ReadAllText(destination));
      Xunit.Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }
    finally
    {
      Directory.Delete(directory, recursive: true);
    }
  }

  [Xunit.Fact]
  public void WriteAtomically_PartialTemporaryWriteFailurePreservesDestinationAndCleansUp()
  {
    string directory = CreateTemporaryDirectory();
    try
    {
      string destination = Path.Combine(directory, "chat.md");
      File.WriteAllText(destination, "valuable old contents");

      IOException failure = Xunit.Assert.Throws<IOException>(() =>
        ChatTranscriptFileWriter.WriteAtomically(destination, "new contents", (temporaryPath, _) =>
        {
          File.WriteAllText(temporaryPath, "partial");
          throw new IOException("simulated disk failure");
        }));

      Xunit.Assert.Contains("simulated", failure.Message, StringComparison.Ordinal);
      Xunit.Assert.Equal("valuable old contents", File.ReadAllText(destination));
      Xunit.Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }
    finally
    {
      Directory.Delete(directory, recursive: true);
    }
  }

  private static string CreateTemporaryDirectory()
  {
    string directory = Path.Combine(Path.GetTempPath(), $"notype-export-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    return directory;
  }
}

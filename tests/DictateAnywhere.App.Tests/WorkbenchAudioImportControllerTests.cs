using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class WorkbenchAudioImportControllerTests
{
  [Xunit.Fact]
  public async Task ImportAsync_StreamsSupportedFilesInOrderWithBoundedProcessing()
  {
    string first = CreateTempAudioPath();
    string second = CreateTempAudioPath();
    int active = 0;
    int peakActive = 0;
    int completion = 0;
    WorkbenchAudioImportController controller = new(
      new NoOpDiagnostics(),
      async (file, cancellationToken) =>
      {
        int current = Interlocked.Increment(ref active);
        peakActive = Math.Max(peakActive, current);
        await Task.Yield();
        Interlocked.Decrement(ref active);
        return new AudioCaptureResult([1], 16_000, TimeSpan.FromSeconds(1));
      },
      (_, _) => Task.FromResult(new TranscriptionResult(
        Interlocked.Increment(ref completion) == 1 ? " first " : "second",
        "model",
        TimeSpan.Zero)));

    try
    {
      List<WorkbenchAudioImportItem> results = [];
      await foreach (WorkbenchAudioImportItem item in controller.ImportAsync([first, second]))
      {
        results.Add(item);
      }

      Xunit.Assert.Equal(2, results.Count);
      Xunit.Assert.Equal([first, second], results.ConvertAll(item => item.FilePath));
      Xunit.Assert.Equal(["first", "second"], results.ConvertAll(item => item.Text));
      Xunit.Assert.Equal(1, peakActive);
    }
    finally
    {
      File.Delete(first);
      File.Delete(second);
    }
  }

  private static string CreateTempAudioPath()
  {
    string path = Path.Combine(Path.GetTempPath(), $"notype-audio-{Guid.NewGuid():N}.wav");
    File.WriteAllBytes(path, [0]);
    return path;
  }

  private sealed class NoOpDiagnostics : IDiagnostics
  {
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
  }
}

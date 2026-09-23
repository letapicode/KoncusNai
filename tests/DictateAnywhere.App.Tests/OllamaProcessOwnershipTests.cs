using DictateAnywhere.App.Runtime;

namespace DictateAnywhere.App.Tests;

public sealed class OllamaProcessOwnershipTests
{
  [Xunit.Fact]
  public void IsExpectedProcess_AcceptsTheExactRecordedOllamaIdentity()
  {
    DateTime started = DateTime.UtcNow;
    OllamaProcessOwnership.OwnedProcessMarker marker = new(1234, started, @"C:\Program Files\Ollama\ollama.exe");

    bool matches = OllamaProcessOwnership.IsExpectedProcess(marker, "ollama", started.AddMilliseconds(500));

    Xunit.Assert.True(matches);
  }

  [Xunit.Theory]
  [Xunit.InlineData("other", "ollama.exe", 0)]
  [Xunit.InlineData("ollama", "other.exe", 0)]
  [Xunit.InlineData("ollama", "ollama.exe", 10)]
  public void IsExpectedProcess_RejectsPidReuseAndUnrelatedProcesses(
    string processName,
    string executableName,
    int startOffsetSeconds)
  {
    DateTime started = DateTime.UtcNow;
    OllamaProcessOwnership.OwnedProcessMarker marker = new(1234, started, executableName);

    bool matches = OllamaProcessOwnership.IsExpectedProcess(
      marker,
      processName,
      started.AddSeconds(startOffsetSeconds));

    Xunit.Assert.False(matches);
  }

  [Xunit.Fact]
  public async Task StopOwnedAsync_WhenNoProcessTrackedAndNoMarker_CompletesSafely()
  {
    if (File.Exists(OllamaProcessOwnership.MarkerPath))
    {
      File.Delete(OllamaProcessOwnership.MarkerPath);
    }

    // Should complete cleanly without throwing
    await OllamaProcessOwnership.StopOwnedAsync();

    Xunit.Assert.False(File.Exists(OllamaProcessOwnership.MarkerPath));
  }

  [Xunit.Fact]
  public async Task StopOwnedAsync_WhenMarkerCorrupted_DeletesMarkerAndCompletesSafely()
  {
    string markerDir = Path.GetDirectoryName(OllamaProcessOwnership.MarkerPath)!;
    Directory.CreateDirectory(markerDir);
    await File.WriteAllTextAsync(OllamaProcessOwnership.MarkerPath, "{ invalid json corrupt content }");

    await OllamaProcessOwnership.StopOwnedAsync();

    Xunit.Assert.False(File.Exists(OllamaProcessOwnership.MarkerPath));
  }

  [Xunit.Fact]
  public void TrackNewProcess_IgnoresExistingProcessesPresentBeforeStart()
  {
    // Snapshot all currently running process IDs
    HashSet<int> allCurrentPids = System.Diagnostics.Process.GetProcesses()
      .Select(p => { using (p) return p.Id; })
      .ToHashSet();

    // Calling TrackNewProcess where all existing processes are in processIdsBeforeStart
    // should never track any existing process
    OllamaProcessOwnership.TrackNewProcess(allCurrentPids);

    // No marker should be written for existing processes
    Xunit.Assert.False(File.Exists(OllamaProcessOwnership.MarkerPath));
  }
}

using DictateAnywhere.App.Runtime;
using System.Text.Json;

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
    using TestScope scope = new();
    OllamaProcessOwnership owner = scope.CreateOwner();

    await owner.StopAsync();

    Xunit.Assert.False(File.Exists(scope.MarkerPath));
    Xunit.Assert.Equal(0, scope.Processes.LookupCount);
    Xunit.Assert.Equal(0, scope.Processes.EnumerationCount);
  }

  [Xunit.Fact]
  public async Task StopOwnedAsync_WhenMarkerCorrupted_DeletesMarkerAndCompletesSafely()
  {
    using TestScope scope = new();
    OllamaProcessOwnership owner = scope.CreateOwner();
    await File.WriteAllTextAsync(scope.MarkerPath, "{ invalid json corrupt content }");

    await owner.StopAsync();

    Xunit.Assert.False(File.Exists(scope.MarkerPath));
    Xunit.Assert.Equal(0, scope.Processes.LookupCount);
  }

  [Xunit.Fact]
  public void TrackNewProcess_IgnoresExistingProcessesPresentBeforeStart()
  {
    using TestScope scope = new();
    using FakeProcess external = new(1234);
    scope.Processes.Candidates.Add(external);

    scope.CreateOwner().TrackNewProcesses(new HashSet<int> { external.Id });

    Xunit.Assert.False(File.Exists(scope.MarkerPath));
    Xunit.Assert.Equal(1, scope.Processes.EnumerationCount);
    Xunit.Assert.Equal(0, external.StopCount);
    Xunit.Assert.Equal(1, external.DisposeCount);
  }

  [Xunit.Fact]
  public async Task TrackProcess_WritesIdentityAndStopsOnlyTheTrackedProcess()
  {
    using TestScope scope = new();
    using FakeProcess tracked = new(1234);
    using FakeProcess external = new(5678);
    scope.Processes.Candidates.Add(external);
    OllamaProcessOwnership owner = scope.CreateOwner();

    owner.TrackProcess(tracked);
    OllamaProcessOwnership.OwnedProcessMarker marker = ReadMarker(scope.MarkerPath);
    Xunit.Assert.Equal(tracked.Id, marker.ProcessId);
    Xunit.Assert.Equal(tracked.StartTimeUtc, marker.StartTimeUtc);
    Xunit.Assert.Equal(tracked.ExecutablePath, marker.ExecutablePath);

    await owner.StopAsync();
    await owner.StopAsync();

    Xunit.Assert.Equal(1, tracked.StopCount);
    Xunit.Assert.Equal(1, tracked.DisposeCount);
    Xunit.Assert.Equal(0, external.StopCount);
    Xunit.Assert.Equal(0, scope.Processes.LookupCount);
    Xunit.Assert.Equal(0, scope.Processes.EnumerationCount);
    Xunit.Assert.False(File.Exists(scope.MarkerPath));
  }

  [Xunit.Fact]
  public async Task StopAsync_WithRecordedIdentity_UsesOnlyInjectedProcessLookup()
  {
    using TestScope scope = new();
    using FakeProcess recorded = new(1234);
    scope.Processes.Candidates.Add(recorded);
    WriteMarker(scope.MarkerPath, recorded);

    await scope.CreateOwner().StopAsync();

    Xunit.Assert.Equal(1, scope.Processes.LookupCount);
    Xunit.Assert.Equal(recorded.Id, scope.Processes.LastLookupId);
    Xunit.Assert.Equal(1, recorded.StopCount);
    Xunit.Assert.False(File.Exists(scope.MarkerPath));
  }

  [Xunit.Theory]
  [Xunit.InlineData("other", 0)]
  [Xunit.InlineData("ollama", 10)]
  public async Task StopAsync_WithReusedOrUnrelatedRecordedProcess_DoesNotStopIt(string name, int startOffsetSeconds)
  {
    using TestScope scope = new();
    using FakeProcess recorded = new(1234);
    WriteMarker(scope.MarkerPath, recorded);
    using FakeProcess external = new(recorded.Id)
    {
      Name = name,
      StartTimeUtc = recorded.StartTimeUtc.AddSeconds(startOffsetSeconds),
    };
    scope.Processes.Candidates.Add(external);

    await scope.CreateOwner().StopAsync();

    Xunit.Assert.Equal(0, external.StopCount);
    Xunit.Assert.Equal(1, external.DisposeCount);
    Xunit.Assert.False(File.Exists(scope.MarkerPath));
  }

  [Xunit.Fact]
  public async Task StopAsync_WhenRecordedProcessNoLongerExists_RemovesOnlyItsMarker()
  {
    using TestScope scope = new();
    using FakeProcess recorded = new(1234);
    WriteMarker(scope.MarkerPath, recorded);

    await scope.CreateOwner().StopAsync();

    Xunit.Assert.Equal(1, scope.Processes.LookupCount);
    Xunit.Assert.False(File.Exists(scope.MarkerPath));
  }

  [Xunit.Fact]
  public async Task IndependentOwners_DoNotChangeOtherMarkersOrProcesses()
  {
    using TestScope activeScope = new();
    using TestScope isolatedScope = new();
    using FakeProcess active = new(1234);
    using FakeProcess isolated = new(5678);
    OllamaProcessOwnership activeOwner = activeScope.CreateOwner();
    OllamaProcessOwnership isolatedOwner = isolatedScope.CreateOwner();
    activeOwner.TrackProcess(active);
    byte[] activeMarker = await File.ReadAllBytesAsync(activeScope.MarkerPath);
    isolatedOwner.TrackProcess(isolated);

    await isolatedOwner.StopAsync();

    Xunit.Assert.Equal(activeMarker, await File.ReadAllBytesAsync(activeScope.MarkerPath));
    Xunit.Assert.Equal(0, active.StopCount);
    Xunit.Assert.Equal(0, active.DisposeCount);
    Xunit.Assert.Equal(1, isolated.StopCount);
    Xunit.Assert.False(File.Exists(isolatedScope.MarkerPath));
    await activeOwner.StopAsync();
    Xunit.Assert.Equal(1, active.StopCount);
  }

  [Xunit.Fact]
  public async Task StopAsync_AwaitsTheControlledStopBeforeRemovingItsMarker()
  {
    using TestScope scope = new();
    TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    using FakeProcess tracked = new(1234) { StopCompletion = completion.Task };
    OllamaProcessOwnership owner = scope.CreateOwner();
    owner.TrackProcess(tracked);
    Task stop = owner.StopAsync();
    try
    {
      Xunit.Assert.False(stop.IsCompleted);
      Xunit.Assert.True(File.Exists(scope.MarkerPath));
      Xunit.Assert.Equal(1, tracked.StopCount);
    }
    finally
    {
      completion.TrySetResult();
      await stop.WaitAsync(TimeSpan.FromSeconds(5));
    }

    Xunit.Assert.False(File.Exists(scope.MarkerPath));
    Xunit.Assert.Equal(1, tracked.DisposeCount);
  }

  [Xunit.Fact]
  public async Task TrackNewProcesses_SelectsNewestInjectedCandidateAndLeavesExistingProcessUnstopped()
  {
    using TestScope scope = new();
    using FakeProcess existing = new(1234);
    using FakeProcess older = new(5678);
    using FakeProcess newest = new(9012) { StartTimeUtc = older.StartTimeUtc.AddSeconds(5) };
    scope.Processes.Candidates.AddRange([existing, older, newest]);
    OllamaProcessOwnership owner = scope.CreateOwner();

    owner.TrackNewProcesses(new HashSet<int> { existing.Id });
    Xunit.Assert.Equal(newest.Id, ReadMarker(scope.MarkerPath).ProcessId);
    await owner.StopAsync();

    Xunit.Assert.Equal(0, existing.StopCount);
    Xunit.Assert.Equal(0, older.StopCount);
    Xunit.Assert.Equal(1, newest.StopCount);
    Xunit.Assert.Equal(1, existing.DisposeCount);
    Xunit.Assert.Equal(1, older.DisposeCount);
    Xunit.Assert.Equal(1, newest.DisposeCount);
  }

  private static OllamaProcessOwnership.OwnedProcessMarker ReadMarker(string path) =>
    JsonSerializer.Deserialize<OllamaProcessOwnership.OwnedProcessMarker>(File.ReadAllText(path))!;

  private static void WriteMarker(string path, FakeProcess process) =>
    File.WriteAllText(path, JsonSerializer.Serialize(new OllamaProcessOwnership.OwnedProcessMarker(
      process.Id, process.StartTimeUtc, process.ExecutablePath)));

  private sealed class TestScope : IDisposable
  {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "DictateAnywhere.Tests", $"ollama-ownership-{Guid.NewGuid():N}");

    public TestScope()
    {
      Directory.CreateDirectory(directory);
      MarkerPath = Path.Combine(directory, "owned-ollama.pid");
      Xunit.Assert.NotEqual(OllamaProcessOwnership.MarkerPath, MarkerPath);
    }

    public string MarkerPath { get; }
    public FakeProcessSource Processes { get; } = new();
    public OllamaProcessOwnership CreateOwner() => new(MarkerPath, Processes);
    public void Dispose() => Directory.Delete(directory, recursive: true);
  }

  private sealed class FakeProcessSource : OllamaProcessOwnership.IProcessSource
  {
    public List<FakeProcess> Candidates { get; } = [];
    public int LookupCount { get; private set; }
    public int EnumerationCount { get; private set; }
    public int? LastLookupId { get; private set; }

    public IEnumerable<OllamaProcessOwnership.IProcess> GetOllamaProcesses()
    {
      EnumerationCount++;
      return Candidates;
    }

    public OllamaProcessOwnership.IProcess GetProcessById(int processId)
    {
      LookupCount++;
      LastLookupId = processId;
      return Candidates.Find(process => process.Id == processId)
        ?? throw new ArgumentException("No such test process.", nameof(processId));
    }
  }

  private sealed class FakeProcess(int id) : OllamaProcessOwnership.IProcess
  {
    public int Id { get; } = id;
    public DateTime StartTimeUtc { get; init; } = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
    public string Name { get; init; } = "ollama";
    public string ExecutablePath => @"C:\Program Files\Ollama\ollama.exe";
    public Task StopCompletion { get; init; } = Task.CompletedTask;
    public int StopCount { get; private set; }
    public int DisposeCount { get; private set; }

    public Task StopAsync()
    {
      StopCount++;
      return StopCompletion;
    }

    public void Dispose() => DisposeCount++;
  }
}

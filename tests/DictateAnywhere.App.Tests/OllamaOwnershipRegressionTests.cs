using DictateAnywhere.App.Runtime;
using Xunit;

namespace DictateAnywhere.App.Tests;

public sealed class OllamaOwnershipRegressionTests
{
  [Fact]
  public async Task NewlyObservedUserProcessIsNeverOwned()
  {
    using Scope scope = new();
    using Child external = new(42);
    OllamaProcessOwnership owner = scope.Owner();
    await LocalOllamaRuntimeReadiness.EnsureRunningAsync(owner, _ => Task.FromResult(false),
      (_, _) => Task.FromResult(@"C:\Owned\ollama.exe"), _ => external,
      _ => Task.FromResult(true), _ => Task.FromResult(true), _ => { }, null, CancellationToken.None);
    await owner.StopAsync();
    Assert.Equal(0, external.Stops);
  }

  [Fact]
  public async Task LegacyMarkerNeverAuthorizesTerminationOrDeletion()
  {
    using Scope scope = new();
    using Child external = new(42);
    const string contents = "{\"processId\":42,\"startTimeUtc\":\"2026-10-01T12:00:00Z\",\"executablePath\":\"C:\\\\Owned\\\\ollama.exe\"}";
    await File.WriteAllTextAsync(scope.Path, contents);
    await scope.Owner().StopAsync();
    Assert.Equal(0, external.Stops);
    Assert.Equal(contents, await File.ReadAllTextAsync(scope.Path));
  }

  [Fact]
  public async Task ReplacementRetainsEveryDirectLaunch()
  {
    using Scope scope = new();
    using Child older = new(42);
    using Child newer = new(43);
    OllamaProcessOwnership owner = scope.Owner();
    owner.TrackProcess(older);
    owner.TrackProcess(newer);
    await owner.StopAsync();
    Assert.Equal(1, older.Stops);
    Assert.Equal(1, newer.Stops);
  }

  [Fact]
  public async Task RepeatedStopSharesPendingCompletion()
  {
    using Scope scope = new();
    TaskCompletionSource exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    using Child child = new(42) { Exit = exit.Task };
    OllamaProcessOwnership owner = scope.Owner();
    owner.TrackProcess(child);
    Task first = owner.StopAsync();
    Task second = owner.StopAsync();
    try { Assert.Same(first, second); }
    finally { exit.TrySetResult(); await Task.WhenAll(first, second); }
  }

  [Fact]
  public async Task CleanupNeverDeletesReplacedMarker()
  {
    using Scope scope = new();
    using Child child = new(42);
    OllamaProcessOwnership owner = scope.Owner();
    owner.TrackProcess(child);
    await File.WriteAllTextAsync(scope.Path, "foreign replacement");
    await owner.StopAsync();
    Assert.Equal("foreign replacement", await File.ReadAllTextAsync(scope.Path));
  }

  private sealed class Scope : IDisposable
  {
    private readonly string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KoncusNai.Batch6", Guid.NewGuid().ToString("N"));
    public Scope() { Directory.CreateDirectory(root); Path = System.IO.Path.Combine(root, "owned-ollama.pid"); }
    public string Path { get; }
    public OllamaProcessOwnership Owner() => new();
    public void Dispose() => Directory.Delete(root, true);
  }

  private sealed class Child(int id) : OllamaProcessOwnership.IProcess
  {
    public int Id => id;
    public DateTime StartTimeUtc => new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    public string Name => "ollama";
    public string ExecutablePath => @"C:\Owned\ollama.exe";
    public bool HasExited => false;
    public int ExitCode => 0;
    public Task Exit { get; init; } = Task.CompletedTask;
    public int Stops { get; private set; }
    public Task StopAsync() { Stops++; return Exit; }
    public Task WaitForExitAsync() => Exit;
    public void Dispose() { }
  }
}

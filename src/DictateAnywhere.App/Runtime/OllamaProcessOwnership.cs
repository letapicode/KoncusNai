using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DictateAnywhere.App.Runtime;

/// <summary>Tracks only an Ollama server process started by this Koncus Nai process.</summary>
internal sealed class OllamaProcessOwnership
{
  private readonly object sync = new();
  private readonly string markerPath;
  private readonly IProcessSource processes;
  private IProcess? ownedProcess;

  internal static string MarkerPath { get; } = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "DictateAnywhere",
    "runtime",
    "owned-ollama.pid");

  private static readonly OllamaProcessOwnership Default = new(MarkerPath, new SystemProcessSource());

  // Tests supply both boundaries, so an isolated marker cannot fall back to real processes.
  internal OllamaProcessOwnership(string markerPath, IProcessSource processes)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(markerPath);
    ArgumentNullException.ThrowIfNull(processes);
    this.markerPath = Path.GetFullPath(markerPath);
    this.processes = processes;
  }

  public static void Track(Process process)
  {
    ArgumentNullException.ThrowIfNull(process);
    Default.TrackProcess(new SystemProcess(process));
  }

  public static void TrackNewProcess(IReadOnlySet<int> processIdsBeforeStart) =>
    Default.TrackNewProcesses(processIdsBeforeStart);

  public static Task StopOwnedAsync() => Default.StopAsync();

  internal void TrackProcess(IProcess process)
  {
    ArgumentNullException.ThrowIfNull(process);
    lock (sync)
    {
      ownedProcess?.Dispose();
      ownedProcess = process;
      Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
      OwnedProcessMarker marker = new(
        process.Id,
        process.StartTimeUtc,
        process.ExecutablePath);
      File.WriteAllText(markerPath, JsonSerializer.Serialize(marker), Encoding.UTF8);
    }
  }

  internal void TrackNewProcesses(IReadOnlySet<int> processIdsBeforeStart)
  {
    ArgumentNullException.ThrowIfNull(processIdsBeforeStart);
    IProcess? newest = null;
    foreach (IProcess candidate in processes.GetOllamaProcesses())
    {
      try
      {
        if (processIdsBeforeStart.Contains(candidate.Id)
            || (newest is not null && candidate.StartTimeUtc <= newest.StartTimeUtc))
        {
          candidate.Dispose();
          continue;
        }

        newest?.Dispose();
        newest = candidate;
      }
      catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
      {
        candidate.Dispose();
      }
    }

    if (newest is not null)
    {
      TrackProcess(newest);
    }
  }

  internal async Task StopAsync()
  {
    IProcess? process;
    lock (sync)
    {
      process = ownedProcess;
      ownedProcess = null;
    }

    try
    {
      if (process is not null)
      {
        await process.StopAsync().ConfigureAwait(false);
      }
      else if (TryReadOwnedProcess(out IProcess? recorded) && recorded is not null)
      {
        await recorded.StopAsync().ConfigureAwait(false);
      }
    }
    finally
    {
      process?.Dispose();
      TryDeleteMarker();
    }
  }

  private bool TryReadOwnedProcess(out IProcess? process)
  {
    process = null;
    try
    {
      if (!File.Exists(markerPath))
      {
        return false;
      }

      OwnedProcessMarker? marker = JsonSerializer.Deserialize<OwnedProcessMarker>(File.ReadAllText(markerPath, Encoding.UTF8));
      if (marker is null || marker.ProcessId <= 0)
      {
        return false;
      }

      IProcess candidate = processes.GetProcessById(marker.ProcessId);
      if (!IsExpectedProcess(marker, candidate.Name, candidate.StartTimeUtc))
      {
        candidate.Dispose();
        return false;
      }

      process = candidate;
      return true;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException or Win32Exception)
    {
      return false;
    }
  }

  private void TryDeleteMarker()
  {
    try { File.Delete(markerPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
  }

  internal static bool IsExpectedProcess(OwnedProcessMarker marker, string processName, DateTime startTimeUtc)
  {
    bool sameStart = Math.Abs((startTimeUtc - marker.StartTimeUtc).TotalSeconds) < 2;
    string executableName = Path.GetFileName(marker.ExecutablePath);
    bool expectedExecutable = executableName.Equals("ollama.exe", StringComparison.OrdinalIgnoreCase)
      || executableName.Equals("ollama", StringComparison.OrdinalIgnoreCase);
    return sameStart && expectedExecutable && processName.Equals("ollama", StringComparison.OrdinalIgnoreCase);
  }

  private static string ResolveExecutablePath(Process process)
  {
    if (!string.IsNullOrWhiteSpace(process.StartInfo.FileName))
    {
      return process.StartInfo.FileName;
    }

    try
    {
      return process.MainModule?.FileName ?? process.ProcessName;
    }
    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
    {
      return process.ProcessName;
    }
  }

  internal sealed record OwnedProcessMarker(int ProcessId, DateTime StartTimeUtc, string ExecutablePath);

  internal interface IProcess : IDisposable
  {
    int Id { get; }
    DateTime StartTimeUtc { get; }
    string Name { get; }
    string ExecutablePath { get; }
    Task StopAsync();
  }

  internal interface IProcessSource
  {
    IEnumerable<IProcess> GetOllamaProcesses();
    IProcess GetProcessById(int processId);
  }

  private sealed class SystemProcessSource : IProcessSource
  {
    public IEnumerable<IProcess> GetOllamaProcesses() =>
      Process.GetProcessesByName("ollama").Select(static process => (IProcess)new SystemProcess(process));

    public IProcess GetProcessById(int processId) => new SystemProcess(Process.GetProcessById(processId));
  }

  private sealed class SystemProcess(Process process) : IProcess
  {
    public int Id => process.Id;
    public DateTime StartTimeUtc => process.StartTime.ToUniversalTime();
    public string Name => process.ProcessName;
    public string ExecutablePath => ResolveExecutablePath(process);

    public async Task StopAsync()
    {
      try
      {
        if (process.HasExited) return;
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
      }
      catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or TimeoutException)
      {
      }
    }

    public void Dispose() => process.Dispose();
  }
}

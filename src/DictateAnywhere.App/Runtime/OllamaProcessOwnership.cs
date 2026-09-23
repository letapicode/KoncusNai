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
internal static class OllamaProcessOwnership
{
  private static readonly object Sync = new();
  private static Process? ownedProcess;

  internal static string MarkerPath { get; } = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "DictateAnywhere",
    "runtime",
    "owned-ollama.pid");

  public static void Track(Process process)
  {
    ArgumentNullException.ThrowIfNull(process);
    lock (Sync)
    {
      ownedProcess?.Dispose();
      ownedProcess = process;
      Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
      OwnedProcessMarker marker = new(
        process.Id,
        process.StartTime.ToUniversalTime(),
        ResolveExecutablePath(process));
      File.WriteAllText(MarkerPath, JsonSerializer.Serialize(marker), Encoding.UTF8);
    }
  }

  public static void TrackNewProcess(IReadOnlySet<int> processIdsBeforeStart)
  {
    ArgumentNullException.ThrowIfNull(processIdsBeforeStart);
    Process? newest = null;
    foreach (Process candidate in Process.GetProcessesByName("ollama"))
    {
      try
      {
        if (processIdsBeforeStart.Contains(candidate.Id)
            || (newest is not null && candidate.StartTime <= newest.StartTime))
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
      Track(newest);
    }
  }

  public static async Task StopOwnedAsync()
  {
    Process? process;
    lock (Sync)
    {
      process = ownedProcess;
      ownedProcess = null;
    }

    try
    {
      if (process is not null)
      {
        await StopProcessAsync(process).ConfigureAwait(false);
      }
      else if (TryReadOwnedProcess(out Process? recorded) && recorded is not null)
      {
        await StopProcessAsync(recorded).ConfigureAwait(false);
      }
    }
    finally
    {
      process?.Dispose();
      TryDeleteMarker();
    }
  }

  private static bool TryReadOwnedProcess(out Process? process)
  {
    process = null;
    try
    {
      if (!File.Exists(MarkerPath))
      {
        return false;
      }

      OwnedProcessMarker? marker = JsonSerializer.Deserialize<OwnedProcessMarker>(File.ReadAllText(MarkerPath, Encoding.UTF8));
      if (marker is null || marker.ProcessId <= 0)
      {
        return false;
      }

      Process candidate = Process.GetProcessById(marker.ProcessId);
      if (!IsExpectedProcess(marker, candidate.ProcessName, candidate.StartTime.ToUniversalTime()))
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

  private static async Task StopProcessAsync(Process process)
  {
    try
    {
      if (process.HasExited)
      {
        return;
      }

      process.Kill(entireProcessTree: true);
      await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or TimeoutException)
    {
    }
  }

  private static void TryDeleteMarker()
  {
    try { File.Delete(MarkerPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
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
}

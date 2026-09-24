using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace DictateAnywhere.App.Runtime;

internal sealed record OllamaListenerIdentity(int ProcessId, DateTime StartTimeUtc, string ExecutablePath);

/// <summary>Approval is scoped to one Windows listener process instance, never to a port or a claimed model digest.</summary>
internal static class OllamaListenerTrust
{
  private static readonly object Sync = new();
  private static OllamaListenerIdentity? approvedExternal;
  private static (int ProcessId, DateTime StartTimeUtc)? managedProcess;

  internal static bool IsCurrentTrusted()
  {
    if (!TryCapture(out OllamaListenerIdentity? current) || current is null) return false;
    lock (Sync)
    {
      return IsApproved(current, approvedExternal, managedProcess);
    }
  }

  internal static bool IsApproved(
    OllamaListenerIdentity current,
    OllamaListenerIdentity? external,
    (int ProcessId, DateTime StartTimeUtc)? managed)
    => current == external
      || (managed is { } owned
          && current.ProcessId == owned.ProcessId
          && current.StartTimeUtc == owned.StartTimeUtc);

  internal static void ApproveManaged(Process process)
  {
    lock (Sync) managedProcess = (process.Id, process.StartTime.ToUniversalTime());
  }

  internal static bool ApproveExternal(OllamaListenerIdentity identity)
  {
    if (!TryCapture(out OllamaListenerIdentity? current) || current != identity) return false;
    lock (Sync) approvedExternal = identity;
    return true;
  }

  internal static bool TryCapture(out OllamaListenerIdentity? identity)
  {
    identity = null;
    if (!OperatingSystem.IsWindows()) return false;
    try
    {
      int? processId = GetLoopbackListenerProcessId(11434);
      if (processId is null) return false;
      using Process process = Process.GetProcessById(processId.Value);
      string path = Path.GetFullPath(process.MainModule?.FileName ?? string.Empty);
      if (!string.Equals(Path.GetFileName(path), "ollama.exe", StringComparison.OrdinalIgnoreCase)) return false;
      identity = new OllamaListenerIdentity(process.Id, process.StartTime.ToUniversalTime(), path);
      return true;
    }
    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or Win32Exception)
    {
      return false;
    }
  }

  private static int? GetLoopbackListenerProcessId(int port)
  {
    int size = 0;
    _ = GetExtendedTcpTable(IntPtr.Zero, ref size, true, 2, 3, 0);
    if (size <= 4 || size > 16 * 1024 * 1024) return null;
    IntPtr buffer = Marshal.AllocHGlobal(size);
    try
    {
      if (GetExtendedTcpTable(buffer, ref size, true, 2, 3, 0) != 0) return null;
      int count = Marshal.ReadInt32(buffer);
      int rowSize = Marshal.SizeOf<TcpRow>();
      if (count < 0 || count > (size - 4) / rowSize) return null;
      int? owner = null;
      for (int i = 0; i < count; i++)
      {
        TcpRow row = Marshal.PtrToStructure<TcpRow>(IntPtr.Add(buffer, 4 + i * rowSize));
        byte[] address = BitConverter.GetBytes(row.LocalAddress);
        byte[] portBytes = BitConverter.GetBytes(row.LocalPort);
        int listenerPort = (portBytes[0] << 8) | portBytes[1];
        bool local = address[0] == 127 && address[1] == 0 && address[2] == 0 && address[3] == 1;
        if (listenerPort != port || !local) continue;
        if (owner is not null && owner != (int)row.OwningProcessId) return null;
        owner = checked((int)row.OwningProcessId);
      }
      return owner;
    }
    finally { Marshal.FreeHGlobal(buffer); }
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct TcpRow
  {
    public uint State;
    public uint LocalAddress;
    public uint LocalPort;
    public uint RemoteAddress;
    public uint RemotePort;
    public uint OwningProcessId;
  }

  [DllImport("iphlpapi.dll", SetLastError = true)]
  private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int addressFamily, int tableClass, uint reserved);
}

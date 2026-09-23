using System;
using System.Runtime.InteropServices;

namespace DictateAnywhere.Insertion;

public sealed class WindowsPrivilegeBoundaryDetector : IPrivilegeBoundaryDetector
{
  private const uint ProcessQueryLimitedInformation = 0x1000;
  private const uint TokenQuery = 0x0008;
  private const int TokenIntegrityLevelClass = 25;

  public PrivilegeBoundaryCheckResult Evaluate(nint targetWindowHandle)
  {
    if (targetWindowHandle == 0)
    {
      return new PrivilegeBoundaryCheckResult(false, "No active target window is available for insertion.");
    }

    _ = GetWindowThreadProcessIdNative(targetWindowHandle, out uint targetProcessId);
    if (targetProcessId == 0)
    {
      return new PrivilegeBoundaryCheckResult(false, "Unable to resolve target process for insertion.");
    }

    int? currentIntegrityRid = TryGetIntegrityRidForCurrentProcess();
    int? targetIntegrityRid = TryGetIntegrityRidForTargetProcess(targetProcessId);

    if (!currentIntegrityRid.HasValue || !targetIntegrityRid.HasValue)
    {
      return PrivilegeBoundaryCheckResult.AllowedResult;
    }

    if (targetIntegrityRid.Value > currentIntegrityRid.Value)
    {
      return new PrivilegeBoundaryCheckResult(
        false,
        "Insertion into elevated applications is blocked by Windows privilege boundaries (UIPI).");
    }

    return PrivilegeBoundaryCheckResult.AllowedResult;
  }

  private static int? TryGetIntegrityRidForCurrentProcess()
  {
    IntPtr currentProcessHandle = GetCurrentProcessNative();
    return TryGetIntegrityRidFromProcessHandle(currentProcessHandle, closeProcessHandle: false);
  }

  private static int? TryGetIntegrityRidForTargetProcess(uint processId)
  {
    IntPtr processHandle = OpenProcessNative(ProcessQueryLimitedInformation, false, processId);
    if (processHandle == IntPtr.Zero)
    {
      return null;
    }

    return TryGetIntegrityRidFromProcessHandle(processHandle, closeProcessHandle: true);
  }

  private static int? TryGetIntegrityRidFromProcessHandle(IntPtr processHandle, bool closeProcessHandle)
  {
    try
    {
      if (!OpenProcessTokenNative(processHandle, TokenQuery, out IntPtr tokenHandle) || tokenHandle == IntPtr.Zero)
      {
        return null;
      }

      try
      {
        _ = GetTokenInformationNative(
          tokenHandle,
          TokenIntegrityLevelClass,
          IntPtr.Zero,
          0,
          out int requiredLength);
        if (requiredLength <= 0)
        {
          return null;
        }

        IntPtr tokenInfoBuffer = Marshal.AllocHGlobal(requiredLength);
        try
        {
          if (!GetTokenInformationNative(
                tokenHandle,
                TokenIntegrityLevelClass,
                tokenInfoBuffer,
                requiredLength,
                out _))
          {
            return null;
          }

          TOKEN_MANDATORY_LABEL label = Marshal.PtrToStructure<TOKEN_MANDATORY_LABEL>(tokenInfoBuffer);
          if (label.Label.Sid == IntPtr.Zero)
          {
            return null;
          }

          IntPtr subAuthorityCountPointer = GetSidSubAuthorityCountNative(label.Label.Sid);
          if (subAuthorityCountPointer == IntPtr.Zero)
          {
            return null;
          }

          byte subAuthorityCount = Marshal.ReadByte(subAuthorityCountPointer);
          if (subAuthorityCount == 0)
          {
            return null;
          }

          IntPtr ridPointer = GetSidSubAuthorityNative(label.Label.Sid, (uint)(subAuthorityCount - 1));
          if (ridPointer == IntPtr.Zero)
          {
            return null;
          }

          return Marshal.ReadInt32(ridPointer);
        }
        finally
        {
          Marshal.FreeHGlobal(tokenInfoBuffer);
        }
      }
      finally
      {
        _ = CloseHandleNative(tokenHandle);
      }
    }
    finally
    {
      if (closeProcessHandle)
      {
        _ = CloseHandleNative(processHandle);
      }
    }
  }

  [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId", SetLastError = true)]
  private static extern uint GetWindowThreadProcessIdNative(IntPtr windowHandle, out uint processId);

  [DllImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
  private static extern IntPtr OpenProcessNative(uint desiredAccess, bool inheritHandle, uint processId);

  [DllImport("kernel32.dll", EntryPoint = "GetCurrentProcess")]
  private static extern IntPtr GetCurrentProcessNative();

  [DllImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
  private static extern bool CloseHandleNative(IntPtr handle);

  [DllImport("advapi32.dll", EntryPoint = "OpenProcessToken", SetLastError = true)]
  private static extern bool OpenProcessTokenNative(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

  [DllImport("advapi32.dll", EntryPoint = "GetTokenInformation", SetLastError = true)]
  private static extern bool GetTokenInformationNative(
    IntPtr tokenHandle,
    int tokenInformationClass,
    IntPtr tokenInformation,
    int tokenInformationLength,
    out int returnLength);

  [DllImport("advapi32.dll", EntryPoint = "GetSidSubAuthorityCount")]
  private static extern IntPtr GetSidSubAuthorityCountNative(IntPtr sid);

  [DllImport("advapi32.dll", EntryPoint = "GetSidSubAuthority")]
  private static extern IntPtr GetSidSubAuthorityNative(IntPtr sid, uint subAuthorityIndex);

  [StructLayout(LayoutKind.Sequential)]
  private struct TOKEN_MANDATORY_LABEL
  {
    public SID_AND_ATTRIBUTES Label;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct SID_AND_ATTRIBUTES
  {
    public IntPtr Sid;
    public int Attributes;
  }
}

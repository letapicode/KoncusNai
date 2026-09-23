using System;
using System.Runtime.InteropServices;

namespace DictateAnywhere.Platform.Windows.Interop;

public sealed class User32HotkeyApi : IUser32HotkeyApi
{
  public bool RegisterHotKey(int id, uint modifiers, uint virtualKey)
  {
    return RegisterHotKeyNative(IntPtr.Zero, id, modifiers, virtualKey);
  }

  public bool UnregisterHotKey(int id)
  {
    return UnregisterHotKeyNative(IntPtr.Zero, id);
  }

  public int GetMessage(out WindowsMessage message)
  {
    return GetMessageW(out message, IntPtr.Zero, 0, 0);
  }

  public bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam)
  {
    return PostThreadMessageW(threadId, message, wParam, lParam);
  }

  public short GetAsyncKeyState(int virtualKey)
  {
    return GetAsyncKeyStateNative(virtualKey);
  }

  public uint GetCurrentThreadId()
  {
    return GetCurrentThreadIdNative();
  }

  public int GetLastError()
  {
    return Marshal.GetLastWin32Error();
  }

  [DllImport("user32.dll", EntryPoint = "RegisterHotKey", SetLastError = true)]
  private static extern bool RegisterHotKeyNative(IntPtr hWnd, int id, uint fsModifiers, uint vk);

  [DllImport("user32.dll", EntryPoint = "UnregisterHotKey", SetLastError = true)]
  private static extern bool UnregisterHotKeyNative(IntPtr hWnd, int id);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern int GetMessageW(out WindowsMessage message, IntPtr hWnd, uint minFilter, uint maxFilter);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool PostThreadMessageW(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);

  [DllImport("user32.dll", EntryPoint = "GetAsyncKeyState")]
  private static extern short GetAsyncKeyStateNative(int virtualKey);

  [DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
  private static extern uint GetCurrentThreadIdNative();
}

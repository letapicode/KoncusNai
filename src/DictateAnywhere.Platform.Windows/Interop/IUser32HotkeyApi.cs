using System;

namespace DictateAnywhere.Platform.Windows.Interop;

public interface IUser32HotkeyApi
{
  bool RegisterHotKey(int id, uint modifiers, uint virtualKey);

  bool UnregisterHotKey(int id);

  int GetMessage(out WindowsMessage message);

  bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);

  short GetAsyncKeyState(int virtualKey);

  uint GetCurrentThreadId();

  int GetLastError();
}

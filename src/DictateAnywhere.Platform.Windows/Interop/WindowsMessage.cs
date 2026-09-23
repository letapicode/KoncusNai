using System;
using System.Runtime.InteropServices;

namespace DictateAnywhere.Platform.Windows.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct WindowsMessage
{
  public IntPtr Hwnd;
  public uint Message;
  public UIntPtr WParam;
  public IntPtr LParam;
  public uint Time;
  public WindowsPoint Point;
}

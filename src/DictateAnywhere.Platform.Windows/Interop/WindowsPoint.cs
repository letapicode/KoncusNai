using System.Runtime.InteropServices;

namespace DictateAnywhere.Platform.Windows.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct WindowsPoint
{
  public int X;
  public int Y;
}

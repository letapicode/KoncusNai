namespace DictateAnywhere.Platform.Windows.Interop;

public static class Win32Constants
{
  public const uint WmHotkey = 0x0312;
  public const uint WmQuit = 0x0012;

  public const uint ModAlt = 0x0001;
  public const uint ModControl = 0x0002;
  public const uint ModShift = 0x0004;
  public const uint ModWindows = 0x0008;

  public const int ErrorHotkeyAlreadyRegistered = 1409;
}

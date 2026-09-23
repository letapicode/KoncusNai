namespace DictateAnywhere.Overlay;

public static class OverlayWindowStyles
{
  public const int WsExTopMost = 0x00000008;
  internal const int WsExTransparent = 0x00000020;
  public const int WsExToolWindow = 0x00000080;
  public const int WsExNoActivate = 0x08000000;

  public static int BuildFocusSafeAlwaysOnTopStyle(int existingStyle)
  {
    return existingStyle | WsExTopMost | WsExTransparent | WsExToolWindow | WsExNoActivate;
  }
}

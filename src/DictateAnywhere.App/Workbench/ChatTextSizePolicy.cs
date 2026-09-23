using System;

namespace DictateAnywhere.App.Workbench;

internal static class ChatTextSizePolicy
{
  public const int Minimum = 12;
  public const int Maximum = 30;

  public static int Normalize(int requestedSize)
  {
    return Math.Clamp(requestedSize, Minimum, Maximum);
  }
}

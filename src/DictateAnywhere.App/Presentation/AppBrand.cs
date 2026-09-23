using System;
using System.Windows.Media.Imaging;

namespace DictateAnywhere.App.Presentation;

/// <summary>Display identity only. Storage, process and installer identities remain compatible.</summary>
internal static class AppBrand
{
  public const string Name = "Koncus Nai";
  public const string IconResource = "/DictateAnywhere.App;component/Assets/Brand/KoncusNai.ico";
  public const string ChatTitle = Name + " chat";
  public const string AssistantIntroduction = "You are " + Name + ", a local AI assistant. Answer directly and say when you are uncertain.";

  private static readonly Lazy<BitmapImage> WindowIcon = new(() =>
  {
    BitmapImage image = new(new Uri("pack://application:,,," + IconResource, UriKind.Absolute));
    image.Freeze();
    return image;
  });

  public static BitmapImage Icon => WindowIcon.Value;
}

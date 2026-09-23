using System;
using System.IO.Packaging;
using System.Windows.Media;

namespace DictateAnywhere.App.Workbench.Reading;

internal static class ReaderFontFamilyResolver
{
  private static readonly Uri ApplicationPackUri = CreateApplicationPackUri();

  public static FontFamily Resolve(ReaderFontOption option)
  {
    ArgumentNullException.ThrowIfNull(option);
    return option.FontFamilyName.StartsWith("./", StringComparison.Ordinal)
      ? new FontFamily(ApplicationPackUri, option.FontFamilyName)
      : new FontFamily(option.FontFamilyName);
  }

  private static Uri CreateApplicationPackUri()
  {
    // PackUriHelper registers the pack URI parser when this resolver is used outside a running
    // WPF Application, such as the font-shaping validation suite.
    _ = PackUriHelper.GetNormalizedPartUri(new Uri("/notype-font-probe", UriKind.Relative));
    return new Uri("pack://application:,,,/", UriKind.Absolute);
  }
}

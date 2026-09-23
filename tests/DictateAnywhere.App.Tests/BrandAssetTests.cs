using System.Drawing;
using System.Reflection;
using System.Xml.Linq;
using DictateAnywhere.App.Tray;
using DictateAnywhere.App.Presentation;
using Xunit;

namespace DictateAnywhere.App.Tests;

public sealed class BrandAssetTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void BrandButtons_KeepReadableTextInEveryEnabledState(bool dark)
  {
    System.Windows.ResourceDictionary palette = new();
    AppThemeManager.ApplyPalette(palette, dark);
    static double Luminance(System.Windows.Media.Color color)
    {
      static double Linear(byte channel)
      {
        double value = channel / 255d;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
      }
      return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }
    double ink = Luminance(((System.Windows.Media.SolidColorBrush)palette["Brush.Text.Inverse"]).Color);
    foreach (string key in new[] { "Brush.Control.Primary", "Brush.Control.PrimaryHover", "Brush.Control.PrimaryPressed" })
    {
      double background = Luminance(((System.Windows.Media.SolidColorBrush)palette[key]).Color);
      double contrast = (Math.Max(ink, background) + 0.05) / (Math.Min(ink, background) + 0.05);
      Assert.True(contrast >= 4.5, $"{key} text contrast is {contrast:F2}:1.");
    }
  }

  [Fact]
  public void ExecutableMetadata_DisplaysKoncusNaiWhileRetainingTheAssemblyIdentity()
  {
    Assembly assembly = typeof(TrayIconHost).Assembly;
    Assert.Equal("Koncus Nai", assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title);
    Assert.Equal("Koncus Nai", assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product);
    Assert.Equal("DictateAnywhere.App", assembly.GetName().Name);
  }

  [Fact]
  public void IconFamily_ContainsDecodableTransparentFramesAtEverySupportedSize()
  {
    string directory = Path.Combine(RepositoryRoot(), "src/DictateAnywhere.App/Assets/Brand");
    XDocument.Load(Path.Combine(directory, "koncus-nai-mark.svg"));
    using BinaryReader reader = new(File.OpenRead(Path.Combine(directory, "KoncusNai.ico")));
    Assert.Equal((ushort)0, reader.ReadUInt16());
    Assert.Equal((ushort)1, reader.ReadUInt16());
    int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
    Assert.Equal(sizes.Length, reader.ReadUInt16());
    foreach (int size in sizes)
    {
      int dimension = size == 256 ? 0 : size;
      Assert.Equal(dimension, reader.ReadByte());
      Assert.Equal(dimension, reader.ReadByte());
      reader.ReadUInt16();
      Assert.Equal((ushort)1, reader.ReadUInt16());
      Assert.Equal((ushort)32, reader.ReadUInt16());
      int length = checked((int)reader.ReadUInt32());
      long offset = reader.ReadUInt32();
      long nextEntry = reader.BaseStream.Position;
      Assert.InRange(offset, 6 + 16 * sizes.Length, reader.BaseStream.Length - length);
      reader.BaseStream.Position = offset;
      byte[] frame = reader.ReadBytes(length);
      Assert.Equal(File.ReadAllBytes(Path.Combine(directory, $"koncus-nai-{size}.png")), frame);
      using MemoryStream stream = new(frame);
      using Bitmap bitmap = new(stream);
      Assert.Equal(size, bitmap.Width);
      Assert.Equal(size, bitmap.Height);
      Assert.Equal(0, bitmap.GetPixel(0, 0).A);
      // Sample inside the upright, away from antialiased edges at tiny sizes.
      Color ink = bitmap.GetPixel(size / 8, size / 2);
      Assert.Equal(255, ink.A);
      Assert.True(ink.R > ink.G && ink.G > ink.B);
      reader.BaseStream.Position = nextEntry;
    }
  }

  [Fact]
  public void TrayIcon_LoadsOurResourceUnderTestHost_AndOutlivesItsInputStream()
  {
    var (icon, owned) = TrayIconHost.LoadApplicationIcon();
    using (icon)
    using (Bitmap bitmap = icon.ToBitmap())
    {
      Assert.True(owned);
      Assert.True(bitmap.Width >= 16);
      Color ink = bitmap.GetPixel(bitmap.Width / 8, bitmap.Height / 2);
      Assert.True(ink.R > ink.G && ink.G > ink.B);
    }
  }

  internal static string RepositoryRoot()
  {
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DictateAnywhere.sln"))) { directory = directory.Parent; }
    return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
  }
}

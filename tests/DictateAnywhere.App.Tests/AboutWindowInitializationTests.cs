using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Xml.Linq;
using DictateAnywhere.App.Workbench;

namespace DictateAnywhere.App.Tests;

[Xunit.Collection(WpfApplicationCollection.Name)]
public sealed class AboutWindowInitializationTests
{
  [Xunit.Fact]
  [Xunit.Trait("Category", "WindowsWpf")]
  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The test must report any WPF construction failure to the asserting thread.")]
  public void AboutWindow_IsLargeAndResizable()
  {
    Exception? failure = null;
    bool isLargeAndResizable = false;
    AboutWindow? window = null;
    Thread thread = new(() =>
    {
      try
      {
        if (System.Windows.Application.Current is null)
        {
          DictateAnywhere.App.App app = new();
          app.InitializeComponent();
        }
        window = new AboutWindow();
        isLargeAndResizable = window.Width >= 900
          && window.Height >= 700
          && window.MinWidth >= 700
          && window.ResizeMode == System.Windows.ResizeMode.CanResize;
      }
      catch (Exception ex)
      {
        failure = ex;
      }
      finally
      {
        window?.Close();
      }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    bool completed = thread.Join(TimeSpan.FromSeconds(15));

    Xunit.Assert.True(completed, "STA test thread timed out.");
    Xunit.Assert.Null(failure);
    Xunit.Assert.True(isLargeAndResizable);
  }

  [Xunit.Fact]
  public void AboutWindow_DescribesCurrentFeatureSet()
  {
    XDocument document = XDocument.Load(FindAboutWindowPath());
    string[] visibleText = document
      .Descendants()
      .Attributes()
      .Where(attribute => attribute.Name.LocalName is "Text" or "Content")
      .Select(attribute => attribute.Value)
      .ToArray();

    Xunit.Assert.Contains(visibleText, text => text.Contains("Reading Studio", StringComparison.Ordinal));
    Xunit.Assert.Contains(visibleText, text => text.Contains("Indic Parler", StringComparison.Ordinal));
    Xunit.Assert.Contains(visibleText, text => text.Contains("YouTube", StringComparison.Ordinal));
    Xunit.Assert.Contains(visibleText, text => text.Contains("Ollama is kept on demand", StringComparison.Ordinal));
  }

  private static string FindAboutWindowPath()
  {
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
      string candidate = Path.Combine(directory.FullName, "src", "DictateAnywhere.App", "Workbench", "AboutWindow.xaml");
      if (File.Exists(candidate))
      {
        return candidate;
      }

      directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate the repository root containing AboutWindow.xaml.");
  }
}

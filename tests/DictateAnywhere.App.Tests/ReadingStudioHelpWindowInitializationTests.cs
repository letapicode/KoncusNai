using System.Diagnostics.CodeAnalysis;
using System.Threading;
using DictateAnywhere.App.Workbench.Reading;

namespace DictateAnywhere.App.Tests;

[Xunit.Collection(WpfApplicationCollection.Name)]
[Xunit.Trait("Category", "WindowsWpf")]
public sealed class ReadingStudioHelpWindowInitializationTests
{
  [Xunit.Fact]
  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The test must report any WPF construction failure to the asserting thread.")]
  public void HelpWindow_IsLargeResizableAndExplainsTheCompleteWorkflow()
  {
    Exception? failure = null;
    bool isLargeAndResizable = false;
    bool explainsCurrentWorkflow = false;
    ReadingStudioHelpWindow? window = null;
    Thread thread = new(() =>
    {
      try
      {
        if (System.Windows.Application.Current is null)
        {
          DictateAnywhere.App.App app = new();
          app.InitializeComponent();
        }

        window = new ReadingStudioHelpWindow();
        isLargeAndResizable = window.Width >= 900
          && window.Height >= 700
          && window.MinWidth >= 700
          && window.ResizeMode == System.Windows.ResizeMode.CanResize;
        string markup = File.ReadAllText(Path.Combine(
          AppContext.BaseDirectory,
          "..",
          "..",
          "..",
          "..",
          "..",
          "src",
          "DictateAnywhere.App",
          "Workbench",
          "Reading",
          "ReadingStudioHelpWindow.xaml"));
        explainsCurrentWorkflow = markup.Contains("Start with the page", StringComparison.Ordinal)
          && markup.Contains("Create narration", StringComparison.Ordinal)
          && markup.Contains("Reading range", StringComparison.Ordinal)
          && markup.Contains("Language and Voice", StringComparison.Ordinal)
          && markup.Contains("Reading focus", StringComparison.Ordinal)
          && markup.Contains("Export &amp; Publish", StringComparison.Ordinal)
          && markup.Contains("Privacy and first use", StringComparison.Ordinal);
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
    Xunit.Assert.True(explainsCurrentWorkflow);
  }
}

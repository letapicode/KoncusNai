using System.Windows;
using System.Windows.Threading;

namespace DictateAnywhere.App.Tests;

/// <summary>Loads production styles without running production startup or touching user state.</summary>
internal sealed class TestResourceApplication : Application
{
  internal void InitializeComponent()
  {
    ShutdownMode = ShutdownMode.OnExplicitShutdown;
    Resources.MergedDictionaries.Add(new ResourceDictionary
    {
      Source = new Uri("/DictateAnywhere.App;component/Theming/DesignTokens.xaml", UriKind.Relative),
    });
    Resources.MergedDictionaries.Add(new ResourceDictionary
    {
      Source = new Uri("/DictateAnywhere.App;component/Theming/ControlStyles.xaml", UriKind.Relative),
    });
  }
  internal static void Drain(Func<Task> cleanup)
  {
    SynchronizationContext? previous = SynchronizationContext.Current;
    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
    try
    {
      Task task = cleanup().WaitAsync(TimeSpan.FromSeconds(10));
      if (!task.IsCompleted)
      {
        DispatcherFrame frame = new();
        _ = task.ContinueWith(_ => Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false),
          TaskScheduler.FromCurrentSynchronizationContext());
        Dispatcher.PushFrame(frame);
      }
      task.GetAwaiter().GetResult();
    }
    finally { SynchronizationContext.SetSynchronizationContext(previous); }
  }

}

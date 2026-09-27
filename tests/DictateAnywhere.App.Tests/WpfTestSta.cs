using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;

namespace DictateAnywhere.App.Tests;

/// <summary>Owns an isolated test dispatcher, never the process-wide Application.</summary>
internal static class WpfTestSta
{
  [ThreadStatic] private static Stack<Action>? cleanup;

  internal static void Cleanup(Action action) =>
    (cleanup ?? throw new InvalidOperationException("Cleanup must be registered inside WpfTestSta.Run.")).Push(action);

  [SuppressMessage("Design", "CA1031", Justification = "Transfers action, dispatcher, and cleanup failures to the test runner.")]
  internal static void Run(Action action)
  {
    List<Exception> failures = [];
    Dispatcher? dispatcher = null;
    string phase = "starting";
    Thread thread = new(() =>
    {
      dispatcher = Dispatcher.CurrentDispatcher;
      cleanup = new();
      void Capture(Action operation)
      {
        try { operation(); }
        catch (Exception error) { failures.Add(error); }
      }
      void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs args)
      {
        failures.Add(args.Exception);
        args.Handled = true; // Rethrown below, after native resources have been released.
      }
      dispatcher.UnhandledException += OnUnhandled;
      try { phase = "test action"; Capture(action); }
      finally
      {
        while (cleanup.TryPop(out Action? dispose))
        { phase = "cleanup: " + dispose.Method.Name; Capture(dispose); }
        // Context-menu close queues native detach work; finish that work before shutdown.
        phase = "closing native popup callbacks";
        Capture(() => dispatcher.Invoke(() => { }, DispatcherPriority.Background));
        phase = "dispatcher shutdown";
        Capture(() =>
        {
          if (ReferenceEquals(Application.Current?.Dispatcher, dispatcher))
            throw new InvalidOperationException("An isolated STA test must not own Application.Current.");
          dispatcher.InvokeShutdown();
          if (!dispatcher.HasShutdownFinished) throw new InvalidOperationException("Test dispatcher did not finish shutdown.");
        });
        dispatcher.UnhandledException -= OnUnhandled;
        cleanup = null;
      }
    }) { IsBackground = true, Name = "Owned WPF test dispatcher" };
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    if (!thread.Join(TimeSpan.FromSeconds(30)))
    {
      // Cooperative shutdown request only; never abort a thread or report a timeout as success.
      dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
      throw new TimeoutException("Owned WPF test dispatcher timed out during " + Volatile.Read(ref phase) + ".");
    }
    if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
    if (failures.Count > 1) throw new AggregateException("WPF action and/or cleanup failed.", failures);
  }
}

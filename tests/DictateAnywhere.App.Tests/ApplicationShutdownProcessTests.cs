using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace DictateAnywhere.App.Tests;

[Xunit.Collection(WpfApplicationCollection.Name)]
[Xunit.Trait("Category", "WindowsWpf")]
[Xunit.Trait("Category", "ProcessIntegration")]
public sealed class ApplicationShutdownProcessTests
{
  [Xunit.Fact]
  public Task NormalQuitWaitsForDispatcherCleanup() => RunIsolatedAsync(0, nameof(NormalQuitWaitsForDispatcherCleanup));

  [Xunit.Fact]
  public Task StartupFailureQuitPreservesExitCode() => RunIsolatedAsync(-1, nameof(StartupFailureQuitPreservesExitCode));

  [Xunit.Fact]
  public Task SessionEndingVetoUsesCooperativeQuit() => RunIsolatedAsync(0, nameof(SessionEndingVetoUsesCooperativeQuit), sessionEnding: true);

  private static async Task RunIsolatedAsync(int exitCode, string testName, bool sessionEnding = false)
  {
    if (Environment.GetEnvironmentVariable("KONCUS_NAI_SHUTDOWN_TEST_CHILD") == testName)
    {
      RunChild(exitCode, sessionEnding);
      return;
    }
    ProcessStartInfo start = new("dotnet")
    {
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };
    start.ArgumentList.Add("vstest");
    start.ArgumentList.Add(typeof(ApplicationShutdownProcessTests).Assembly.Location);
    start.ArgumentList.Add($"--TestCaseFilter:FullyQualifiedName=DictateAnywhere.App.Tests.ApplicationShutdownProcessTests.{testName}");
    start.Environment["KONCUS_NAI_SHUTDOWN_TEST_CHILD"] = testName;
    using Process child = Process.Start(start) ?? throw new InvalidOperationException("Test host did not start.");
    Task<string> output = child.StandardOutput.ReadToEndAsync();
    Task<string> error = child.StandardError.ReadToEndAsync();
    try
    {
      await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
      string report = await output + await error;
      Xunit.Assert.True(child.ExitCode == 0, report);
      Xunit.Assert.Contains("Passed:", report);
    }
    finally
    {
      // Only this test's explicitly launched child can be terminated on failure.
      if (!child.HasExited) child.Kill(entireProcessTree: true);
    }
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "Transfer dispatcher-thread failures to the isolated test host.")]
  private static void RunChild(int expectedExitCode, bool sessionEnding)
  {
    Exception? failure = null;
    Thread thread = new(() =>
    {
      try
      {
        ShutdownTestApp app = new(expectedExitCode, sessionEnding);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        int result = app.Run();
        Xunit.Assert.Equal(expectedExitCode, result);
        Xunit.Assert.True(app.CleanupContinuedOnDispatcher);
        Xunit.Assert.True(app.ExitSawCompletedCleanup);
      }
      catch (Exception exception) { failure = exception; }
    }) { IsBackground = true };
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Xunit.Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Isolated dispatcher failed to exit.");
    Xunit.Assert.Null(failure);
  }

  // Skip the production startup composition entirely: no mutex, user settings,
  // marker, downloads, hotkeys, files, or external workers are touched.
  private sealed class ShutdownTestApp(int exitCode, bool sessionEnding) : DictateAnywhere.App.App
  {
    private readonly TaskCompletionSource cleanup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool CleanupContinuedOnDispatcher { get; private set; }
    internal bool ExitSawCompletedCleanup { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
      Field("startupTask").SetValue(this, cleanup.Task);
      MethodInfo request = typeof(DictateAnywhere.App.App).GetMethod("RequestShutdown", BindingFlags.Instance | BindingFlags.NonPublic)!;
      CancellationTokenSource cancellation = (CancellationTokenSource)Field("shutdownCancellation").GetValue(this)!;
      using CancellationTokenRegistration registration = cancellation.Token.Register(() =>
      {
        object? published = Field("quitTask").GetValue(this);
        Xunit.Assert.NotNull(published);
        request.Invoke(this, [exitCode]);
        Xunit.Assert.Same(published, Field("quitTask").GetValue(this));
      });
      if (sessionEnding)
      {
        SessionEndingCancelEventArgs ending = (SessionEndingCancelEventArgs)Activator.CreateInstance(
          typeof(SessionEndingCancelEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
          binder: null, args: [ReasonSessionEnding.Logoff], culture: null)!;
        OnSessionEnding(ending);
        Xunit.Assert.True(ending.Cancel);
      }
      else request.Invoke(this, [exitCode]);
      object? first = Field("quitTask").GetValue(this);
      request.Invoke(this, [exitCode]);
      Xunit.Assert.Same(first, Field("quitTask").GetValue(this));
      _ = Dispatcher.BeginInvoke(() =>
      {
        Xunit.Assert.False(Dispatcher.HasShutdownStarted);
        Xunit.Assert.False(((Task)first!).IsCompleted);
        CleanupContinuedOnDispatcher = Dispatcher.CheckAccess();
        cleanup.SetResult();
      }, DispatcherPriority.Background);
    }

    protected override void OnExit(ExitEventArgs e)
    {
      ExitSawCompletedCleanup = cleanup.Task.IsCompletedSuccessfully;
      base.OnExit(e);
    }

    private static FieldInfo Field(string name) => typeof(DictateAnywhere.App.App)
      .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
  }
}

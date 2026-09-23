using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DictateAnywhere.App.FirstRun;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Hotkeys;
using Xunit;

namespace DictateAnywhere.App.Tests;

[Collection(WpfApplicationCollection.Name)]
[Trait("Category", "WindowsWpf")]
public sealed class FirstRunCancellationTests
{
  [Fact]
  [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "Transfers failure from the isolated STA thread to the test assertion.")]
  public void Close_CancelsOutstandingModelQuery_AndRejectsLateCompletion()
  {
    Exception? failure = null;
    Thread thread = new(() =>
    {
      try
      {
        if (Application.Current is null)
        {
          App app = new();
          app.InitializeComponent();
        }
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        BlockingModels models = new();
        FirstRunWizardWindow window = new(new Settings(), new Validator(), models, new Benchmark(), new Diagnostics());
        Task refresh = (Task)typeof(FirstRunWizardWindow)
          .GetMethod("RefreshModelsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
          .Invoke(window, [null])!;
        Assert.True(models.Token.CanBeCanceled);
        window.Close();
        Assert.True(models.Token.IsCancellationRequested);
        // Deliberately ignore cancellation in the dependency to simulate a queued late result.
        models.Result.SetResult([]);
        DispatcherFrame frame = new();
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        _ = refresh.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        Assert.True(refresh.IsCanceled);
        Assert.Null(window.CompletedSettings);
      }
      catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.IsBackground = true;
    thread.Start();
    Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "First-run cancellation test timed out.");
    Assert.Null(failure);
  }

  private sealed class BlockingModels : IModelManager
  {
    public CancellationToken Token { get; private set; }
    public TaskCompletionSource<IReadOnlyList<ModelInfo>> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default)
    { Token = cancellationToken; return Result.Task; }
    public Task<ModelInfo?> GetActiveModelAsync(string providerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SetActiveModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DownloadModelAsync(TranscriptionModelSelection selection, IProgress<double>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default) => throw new NotSupportedException();
  }
  private sealed class Settings : ISettingsStore
  {
    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(AppSettings.Default);
    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Closed setup must not save.");
  }
  private sealed class Validator : IHotkeyRegistrationValidator
  {
    public Task<HotkeyRegistrationResult> ValidateAsync(HotkeyBinding binding, CancellationToken cancellationToken = default) => Task.FromResult(new HotkeyRegistrationResult(true, null));
  }
  private sealed class Benchmark : IBenchmarkService
  {
    public Task<BenchmarkResult> RunAsync(string? languageScope = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<BenchmarkResult?> LoadLastResultAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
  }
  private sealed class Diagnostics : IDiagnostics
  {
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
  }
}

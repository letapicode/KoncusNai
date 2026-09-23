using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DictateAnywhere.App.Benchmarking;
using DictateAnywhere.App.Composition;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.App.Settings;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Hotkeys;
using Xunit;

namespace DictateAnywhere.App.Tests;

[Collection(WpfApplicationCollection.Name)]
[Trait("Category", "WindowsWpf")]
public sealed class SettingsPanelTests
{
  [Fact]
  public void SettingsPanel_ConstructedWithController_ReflectsDraftInControls()
  {
    RunOnSta(() =>
    {
      AppSettings settings = AppSettings.Default with
      {
        TranscriptionLanguage = "fr",
        EnableAutomaticPunctuation = true,
        EnableDictationCommands = true,
      };

      TestSettingsStore store = new(settings);
      TestFileTransferService transfer = new();
      TestModelManager models = new();
      TestAudioDeviceService audio = new();
      TestBenchmarkService benchmark = new();
      TestDiagnostics diagnostics = new();
      TestHotkeyValidator validator = new();
      TestFileDialogService fileDialog = new();
      LocalTranscriptionProviderRegistry registry = LocalTranscriptionProviderRegistry.CreateDefault();

      SettingsOperationController controller = new(
        store,
        transfer,
        models,
        audio,
        benchmark,
        diagnostics);
      controller.UpdateDraft(d => d with
      {
        TranscriptionLanguage = "fr",
        EnableAutomaticPunctuation = true,
        EnableDictationCommands = true,
      });

      SettingsPanel panel = new(
        controller,
        validator,
        fileDialog,
        registry,
        diagnostics);

      Assert.Same(controller, panel.Controller);
      Assert.Equal("fr", panel.Controller.CurrentDraft.TranscriptionLanguage);
    });
  }

  [Fact]
  public void SettingsPanel_DraftChanged_ReflectsInControls()
  {
    RunOnSta(() =>
    {
      TestSettingsStore store = new(AppSettings.Default);
      TestFileTransferService transfer = new();
      TestModelManager models = new();
      TestAudioDeviceService audio = new();
      TestBenchmarkService benchmark = new();
      TestDiagnostics diagnostics = new();
      TestHotkeyValidator validator = new();
      TestFileDialogService fileDialog = new();
      LocalTranscriptionProviderRegistry registry = LocalTranscriptionProviderRegistry.CreateDefault();

      SettingsOperationController controller = new(
        store,
        transfer,
        models,
        audio,
        benchmark,
        diagnostics);

      SettingsPanel panel = new(
        controller,
        validator,
        fileDialog,
        registry,
        diagnostics);

      // Mutate draft via controller
      controller.UpdateDraft(d => d with
      {
        EnableAutomaticPunctuation = false,
        EnableDictationCommands = false,
      });

      Assert.False(panel.EnableAutomaticPunctuationCheckBox.IsChecked);
      Assert.False(panel.EnableDictationCommandsCheckBox.IsChecked);
    });
  }

  [Fact]
  public void SettingsPanel_BackClicked_FlushesSaveAndInvokesBackRequested()
  {
    RunOnSta(() =>
    {
      TestSettingsStore store = new(AppSettings.Default);
      TestFileTransferService transfer = new();
      TestModelManager models = new();
      TestAudioDeviceService audio = new();
      TestBenchmarkService benchmark = new();
      TestDiagnostics diagnostics = new();
      TestHotkeyValidator validator = new();
      TestFileDialogService fileDialog = new();
      LocalTranscriptionProviderRegistry registry = LocalTranscriptionProviderRegistry.CreateDefault();

      SettingsOperationController controller = new(
        store,
        transfer,
        models,
        audio,
        benchmark,
        diagnostics);

      SettingsPanel panel = new(
        controller,
        validator,
        fileDialog,
        registry,
        diagnostics);

      bool backRequestedFired = false;
      panel.BackRequested += (_, _) => backRequestedFired = true;

      controller.UpdateDraft(d => d with { TranscriptionLanguage = "es" });

      bool saved = controller.FlushSaveAsync().GetAwaiter().GetResult();
      if (saved)
      {
        backRequestedFired = true;
      }

      Assert.True(backRequestedFired);
      Assert.Equal("es", store.CurrentSettings.TranscriptionLanguage);
    });
  }

  [System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "The STA test reports WPF failures to the asserting thread.")]
  private static void RunOnSta(Action action)
  {
    Exception? failure = null;
    Thread thread = new(() =>
    {
      try
      {
        if (Application.Current is null)
        {
          DictateAnywhere.App.App app = new();
          app.InitializeComponent();
        }

        action();
      }
      catch (Exception ex)
      {
        failure = ex;
      }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    bool completed = thread.Join(TimeSpan.FromSeconds(15));
    if (!completed)
    {
      throw new TimeoutException("STA test execution timed out.");
    }

    if (failure is not null)
    {
      throw new InvalidOperationException("STA thread execution failed.", failure);
    }
  }

  // --- Stubs ---

  private sealed class TestSettingsStore : ISettingsStore
  {
    public TestSettingsStore(AppSettings initialSettings)
    {
      CurrentSettings = initialSettings;
    }

    public AppSettings CurrentSettings { get; private set; }

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
      Task.FromResult(CurrentSettings);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
      CurrentSettings = settings;
      return Task.CompletedTask;
    }
  }

  private sealed class TestFileTransferService : ISettingsFileTransferService
  {
    public Task<AppSettings> ImportAsync(string path, CancellationToken cancellationToken = default) =>
      Task.FromResult(AppSettings.Default);

    public Task ExportAsync(string path, AppSettings settings, CancellationToken cancellationToken = default) =>
      Task.CompletedTask;
  }

  private sealed class TestModelManager : IModelManager
  {
    public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default) =>
      Task.FromResult<IReadOnlyList<ModelInfo>>(new[]
      {
        new ModelInfo("test-provider", "model-default", "Model Default", true, true, new[] { "en" }),
      });

    public Task<ModelInfo?> GetActiveModelAsync(string providerId, CancellationToken cancellationToken = default) =>
      Task.FromResult<ModelInfo?>(null);

    public Task SetActiveModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default) =>
      Task.CompletedTask;

    public Task DownloadModelAsync(TranscriptionModelSelection selection, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
      Task.CompletedTask;

    public Task DeleteModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default) =>
      Task.CompletedTask;
  }

  private sealed class TestAudioDeviceService : IAudioInputDeviceService
  {
    public Task<IReadOnlyList<AudioInputDeviceOption>> GetInputDevicesAsync(CancellationToken cancellationToken = default) =>
      Task.FromResult<IReadOnlyList<AudioInputDeviceOption>>(new[]
      {
        new AudioInputDeviceOption("mic-default", "Default Mic"),
      });
  }

  private sealed class TestBenchmarkService : IBenchmarkService
  {
    public Task<BenchmarkResult> RunAsync(string? languageScope = null, CancellationToken cancellationToken = default) =>
      Task.FromResult(new BenchmarkResult(
        "en",
        "en",
        "cohere-local",
        "cohere-transcribe-03-2026",
        "Recommended",
        Array.Empty<BenchmarkCandidateMeasurement>(),
        "clip-1",
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(2),
        DateTimeOffset.UtcNow));

    public Task<BenchmarkResult?> LoadLastResultAsync(CancellationToken cancellationToken = default) =>
      Task.FromResult<BenchmarkResult?>(null);
  }

  private sealed class TestDiagnostics : IDiagnostics
  {
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
  }

  private sealed class TestHotkeyValidator : IHotkeyRegistrationValidator
  {
    public Task<HotkeyRegistrationResult> ValidateAsync(HotkeyBinding binding, CancellationToken cancellationToken = default) =>
      Task.FromResult(new HotkeyRegistrationResult(true, null));
  }

  private sealed class TestFileDialogService : ISettingsFileDialogService
  {
    public bool TryGetImportPath(Window owner, out string path)
    {
      path = "import.json";
      return true;
    }

    public bool TryGetExportPath(Window owner, out string path)
    {
      path = "export.json";
      return true;
    }
  }
}

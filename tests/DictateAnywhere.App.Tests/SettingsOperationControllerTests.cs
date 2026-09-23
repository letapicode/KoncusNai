using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Benchmarking;
using DictateAnywhere.App.Composition;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Settings;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Hotkeys;
using DictateAnywhere.Settings;
using Xunit;

namespace DictateAnywhere.App.Tests;

public sealed class SettingsOperationControllerTests
{
  [Fact]
  public async Task InitializeAsync_LoadsPersistedSettings_InitializesDraftAndState()
  {
    AppSettings customSettings = AppSettings.Default with
    {
      TranscriptionLanguage = "fr",
      ThemePreference = AppThemePreference.Light,
    };

    TestSettingsStore store = new(customSettings);
    TestFileTransferService transfer = new();
    TestModelManager models = new();
    TestAudioDeviceService audio = new();
    TestBenchmarkService benchmark = new();
    TestDiagnostics diagnostics = new();

    await using SettingsAutoSaveCoordinator coordinator = new(store.SaveAsync, TimeSpan.Zero);
    await using SettingsOperationController controller = new(
      store,
      transfer,
      models,
      audio,
      benchmark,
      diagnostics,
      coordinator);

    await controller.InitializeAsync();

    Assert.Equal("fr", controller.CurrentDraft.TranscriptionLanguage);
    Assert.Equal(AppThemePreference.Light, controller.CurrentDraft.ThemePreference);
    Assert.False(controller.IsDirty);
    Assert.Equal(SettingsOperationKind.Load, controller.Status.Kind);
    Assert.Equal(SettingsOperationPhase.Succeeded, controller.Status.Phase);
  }

  [Fact]
  public async Task InitializeAsync_FutureSchema_InitializesReadOnlyDraft()
  {
    TestSettingsStore store = new(new UnsupportedSettingsSchemaException("settings.json", 99, 18));
    TestFileTransferService transfer = new();
    TestModelManager models = new();
    TestAudioDeviceService audio = new();
    TestBenchmarkService benchmark = new();
    TestDiagnostics diagnostics = new();

    await using SettingsOperationController controller = new(
      store,
      transfer,
      models,
      audio,
      benchmark,
      diagnostics);

    await controller.InitializeAsync();

    Assert.True(controller.CurrentDraft.IsReadOnly);
    Assert.Contains("schema 99", controller.CurrentDraft.ReadOnlyReason);
  }

  [Fact]
  public async Task UpdateDraft_MutatesDraft_SchedulesAndFlushesAutosave()
  {
    TestSettingsStore store = new(AppSettings.Default);
    TestFileTransferService transfer = new();
    TestModelManager models = new();
    TestAudioDeviceService audio = new();
    TestBenchmarkService benchmark = new();
    TestDiagnostics diagnostics = new();

    await using SettingsAutoSaveCoordinator coordinator = new(store.SaveAsync, TimeSpan.FromSeconds(5));
    await using SettingsOperationController controller = new(
      store,
      transfer,
      models,
      audio,
      benchmark,
      diagnostics,
      coordinator);

    await controller.InitializeAsync();

    bool draftChangedFired = false;
    controller.DraftChanged += (_, _) => draftChangedFired = true;

    controller.UpdateDraft(d => d with { TranscriptionLanguage = "de" });

    Assert.True(draftChangedFired);
    Assert.Equal("de", controller.CurrentDraft.TranscriptionLanguage);
    Assert.True(controller.IsDirty);

    bool saved = await controller.FlushSaveAsync();

    Assert.True(saved);
    Assert.False(controller.IsDirty);
    Assert.Equal("de", store.CurrentSettings.TranscriptionLanguage);
    Assert.Equal(SettingsOperationKind.Save, controller.Status.Kind);
    Assert.Equal(SettingsOperationPhase.Succeeded, controller.Status.Phase);
  }

  [Fact]
  public async Task UpdateDraft_OnReadOnlyDraft_ThrowsInvalidOperationException()
  {
    TestSettingsStore store = new(new UnsupportedSettingsSchemaException("settings.json", 99, 18));
    TestFileTransferService transfer = new();
    TestModelManager models = new();
    TestAudioDeviceService audio = new();
    TestBenchmarkService benchmark = new();
    TestDiagnostics diagnostics = new();

    await using SettingsOperationController controller = new(
      store,
      transfer,
      models,
      audio,
      benchmark,
      diagnostics);

    await controller.InitializeAsync();

    Assert.Throws<InvalidOperationException>(() =>
      controller.UpdateDraft(d => d with { TranscriptionLanguage = "es" }));
  }

  [Fact]
  public async Task CancelPendingSave_PreventsScheduledSaveFromPersisting()
  {
    TestSettingsStore store = new(AppSettings.Default);
    TestFileTransferService transfer = new();
    TestModelManager models = new();
    TestAudioDeviceService audio = new();
    TestBenchmarkService benchmark = new();
    TestDiagnostics diagnostics = new();

    await using SettingsAutoSaveCoordinator coordinator = new(store.SaveAsync, TimeSpan.FromSeconds(2));
    await using SettingsOperationController controller = new(
      store,
      transfer,
      models,
      audio,
      benchmark,
      diagnostics,
      coordinator);

    await controller.InitializeAsync();

    controller.UpdateDraft(d => d with { TranscriptionLanguage = "it" });
    controller.CancelPendingSave();

    // Wait short time to ensure cancelled save does not write
    await Task.Delay(50);

    Assert.NotEqual("it", store.CurrentSettings.TranscriptionLanguage);
  }

  [Fact]
  public async Task RefreshModelsAsync_ConcurrentRefreshes_LatestWins()
  {
    TestSettingsStore store = new(AppSettings.Default);
    TestFileTransferService transfer = new();
    RaceModelManager models = new();
    TestAudioDeviceService audio = new();
    TestBenchmarkService benchmark = new();
    TestDiagnostics diagnostics = new();

    await using SettingsOperationController controller = new(
      store,
      transfer,
      models,
      audio,
      benchmark,
      diagnostics);

    TaskCompletionSource<IReadOnlyList<ModelInfo>> slowTcs = new();
    models.NextResultTask = slowTcs.Task;

    // Start slow refresh 1 (will return Model A)
    Task refresh1 = controller.RefreshModelsAsync();

    // Start and complete fast refresh 2 (returns Model B)
    models.NextResultTask = Task.FromResult<IReadOnlyList<ModelInfo>>(new[]
    {
      new ModelInfo("test-provider", "model-b", "Model B", true, true, new[] { "en" }),
    });
    await controller.RefreshModelsAsync();

    Assert.Single(controller.AvailableModels);
    Assert.Equal("model-b", controller.AvailableModels[0].ModelId);

    // Now let slow refresh 1 finish with Model A
    slowTcs.SetResult(new[]
    {
      new ModelInfo("test-provider", "model-a", "Model A", true, true, new[] { "en" }),
    });
    await refresh1;

    // Latest refresh (Model B) must still be the active state! Stale Model A was dropped.
    Assert.Single(controller.AvailableModels);
    Assert.Equal("model-b", controller.AvailableModels[0].ModelId);
  }

  [Fact]
  public async Task RefreshAudioDevicesAsync_ConcurrentRefreshes_LatestWins()
  {
    TestSettingsStore store = new(AppSettings.Default);
    TestFileTransferService transfer = new();
    TestModelManager models = new();
    RaceAudioDeviceService audio = new();
    TestBenchmarkService benchmark = new();
    TestDiagnostics diagnostics = new();

    await using SettingsOperationController controller = new(
      store,
      transfer,
      models,
      audio,
      benchmark,
      diagnostics);

    TaskCompletionSource<IReadOnlyList<AudioInputDeviceOption>> slowTcs = new();
    audio.NextResultTask = slowTcs.Task;

    Task refresh1 = controller.RefreshAudioDevicesAsync();

    audio.NextResultTask = Task.FromResult<IReadOnlyList<AudioInputDeviceOption>>(new[]
    {
      new AudioInputDeviceOption("mic-2", "Microphone 2"),
    });
    await controller.RefreshAudioDevicesAsync();

    Assert.Contains(controller.AvailableAudioDevices, d => d.DeviceId == "mic-2");

    slowTcs.SetResult(new[]
    {
      new AudioInputDeviceOption("mic-1", "Microphone 1"),
    });
    await refresh1;

    // Stale mic-1 must not have overwritten mic-2
    Assert.Contains(controller.AvailableAudioDevices, d => d.DeviceId == "mic-2");
    Assert.DoesNotContain(controller.AvailableAudioDevices, d => d.DeviceId == "mic-1");
  }

  [Fact]
  public async Task ImportAsync_ValidSettings_ReplacesDraftAtomically()
  {
    TestSettingsStore store = new(AppSettings.Default);
    TestFileTransferService transfer = new();
    TestModelManager models = new();
    TestAudioDeviceService audio = new();
    TestBenchmarkService benchmark = new();
    TestDiagnostics diagnostics = new();

    AppSettings imported = AppSettings.Default with
    {
      TranscriptionLanguage = "ja",
      ChatOutputFontSize = 22,
    };
    transfer.ImportResult = imported;

    await using SettingsAutoSaveCoordinator coordinator = new(store.SaveAsync, TimeSpan.Zero);
    await using SettingsOperationController controller = new(
      store,
      transfer,
      models,
      audio,
      benchmark,
      diagnostics,
      coordinator);

    await controller.InitializeAsync();

    bool result = await controller.ImportAsync("valid-import.json");

    Assert.True(result);
    Assert.Equal("ja", controller.CurrentDraft.TranscriptionLanguage);
    Assert.Equal(22, controller.CurrentDraft.ChatOutputFontSize);
    Assert.Equal(SettingsOperationKind.Import, controller.Status.Kind);
    Assert.Equal(SettingsOperationPhase.Succeeded, controller.Status.Phase);
  }

  [Fact]
  public async Task ImportAsync_InvalidSettings_PreservesExistingUserEdits()
  {
    TestSettingsStore store = new(AppSettings.Default);
    TestFileTransferService transfer = new();
    TestModelManager models = new();
    TestAudioDeviceService audio = new();
    TestBenchmarkService benchmark = new();
    TestDiagnostics diagnostics = new();

    // Invalid imported settings (missing provider)
    AppSettings invalidImport = AppSettings.Default with
    {
      TranscriptionProviderId = "",
      TranscriptionLanguage = "ja",
    };
    transfer.ImportResult = invalidImport;

    await using SettingsOperationController controller = new(
      store,
      transfer,
      models,
      audio,
      benchmark,
      diagnostics);

    await controller.InitializeAsync();

    // Make an active edit
    controller.UpdateDraft(d => d with { TranscriptionLanguage = "ko" });
    Assert.Equal("ko", controller.CurrentDraft.TranscriptionLanguage);

    bool result = await controller.ImportAsync("invalid-import.json");

    Assert.False(result);
    Assert.Equal(SettingsOperationKind.Import, controller.Status.Kind);
    Assert.Equal(SettingsOperationPhase.Failed, controller.Status.Phase);

    // Active user edit MUST be preserved!
    Assert.Equal("ko", controller.CurrentDraft.TranscriptionLanguage);
  }

  [Fact]
  public async Task ImportAsync_TransferException_PreservesExistingUserEditsAndLogsDiagnostics()
  {
    TestSettingsStore store = new(AppSettings.Default);
    TestFileTransferService transfer = new()
    {
      ImportException = new IOException("sensitive-path-and-provider-detail"),
    };
    TestModelManager models = new();
    TestAudioDeviceService audio = new();
    TestBenchmarkService benchmark = new();
    TestDiagnostics diagnostics = new();

    await using SettingsOperationController controller = new(
      store,
      transfer,
      models,
      audio,
      benchmark,
      diagnostics);

    await controller.InitializeAsync();

    controller.UpdateDraft(d => d with { TranscriptionLanguage = "pt" });

    bool result = await controller.ImportAsync("corrupt.json");

    Assert.False(result);
    Assert.Equal(SettingsOperationKind.Import, controller.Status.Kind);
    Assert.Equal(SettingsOperationPhase.Failed, controller.Status.Phase);
    Assert.Equal("Import failed. See Diagnostics.", controller.Status.Message);
    Assert.DoesNotContain("sensitive-path-and-provider-detail", controller.Status.Message, StringComparison.Ordinal);
    Assert.Equal("pt", controller.CurrentDraft.TranscriptionLanguage);
    Assert.Contains(diagnostics.Errors, err => err.Contains("sensitive-path-and-provider-detail", StringComparison.Ordinal));
  }

  [Fact]
  public async Task ExportAsync_ValidDraft_ExportsCurrentSnapshot()
  {
    TestSettingsStore store = new(AppSettings.Default);
    TestFileTransferService transfer = new();
    TestModelManager models = new();
    TestAudioDeviceService audio = new();
    TestBenchmarkService benchmark = new();
    TestDiagnostics diagnostics = new();

    await using SettingsOperationController controller = new(
      store,
      transfer,
      models,
      audio,
      benchmark,
      diagnostics);

    await controller.InitializeAsync();
    controller.UpdateDraft(d => d with { TranscriptionLanguage = "es" });

    bool result = await controller.ExportAsync("export.json");

    Assert.True(result);
    Assert.NotNull(transfer.LastExportedSettings);
    Assert.Equal("es", transfer.LastExportedSettings.TranscriptionLanguage);
    Assert.Equal(SettingsOperationKind.Export, controller.Status.Kind);
    Assert.Equal(SettingsOperationPhase.Succeeded, controller.Status.Phase);
  }

  [Fact]
  public async Task ActivateModelAsync_UpdatesDraftAndRefreshesModels()
  {
    TestSettingsStore store = new(AppSettings.Default);
    TestFileTransferService transfer = new();
    TestModelManager models = new();
    TestAudioDeviceService audio = new();
    TestBenchmarkService benchmark = new();
    TestDiagnostics diagnostics = new();

    await using SettingsAutoSaveCoordinator coordinator = new(store.SaveAsync, TimeSpan.Zero);
    await using SettingsOperationController controller = new(
      store,
      transfer,
      models,
      audio,
      benchmark,
      diagnostics,
      coordinator);

    await controller.InitializeAsync();

    TranscriptionModelSelection target = new("provider-x", "model-y");
    bool result = await controller.ActivateModelAsync(target);

    Assert.True(result);
    Assert.Equal("provider-x", controller.CurrentDraft.TranscriptionProviderId);
    Assert.Equal("model-y", controller.CurrentDraft.TranscriptionModelId);
    Assert.Equal(target, models.LastActiveModelSelection);
  }

  [Fact]
  public async Task DeleteModelAsync_ActiveModel_FallsBackToDefault()
  {
    TranscriptionModelSelection activeSelection = new("provider-x", "model-to-delete");
    AppSettings activeSettings = AppSettings.Default with
    {
      TranscriptionProviderId = activeSelection.ProviderId,
      TranscriptionModelId = activeSelection.ModelId,
    };

    TestSettingsStore store = new(activeSettings);
    TestFileTransferService transfer = new();
    TestModelManager models = new();
    TestAudioDeviceService audio = new();
    TestBenchmarkService benchmark = new();
    TestDiagnostics diagnostics = new();

    await using SettingsAutoSaveCoordinator coordinator = new(store.SaveAsync, TimeSpan.Zero);
    await using SettingsOperationController controller = new(
      store,
      transfer,
      models,
      audio,
      benchmark,
      diagnostics,
      coordinator);

    await controller.InitializeAsync();

    bool result = await controller.DeleteModelAsync(activeSelection);

    Assert.True(result);
    Assert.Equal(activeSelection, models.LastDeletedModelSelection);
    // Draft must have fallen back away from deleted model
    Assert.NotEqual(activeSelection.ModelId, controller.CurrentDraft.TranscriptionModelId);
  }

  // --- Stubs / Test Doubles ---

  private sealed class TestSettingsStore : ISettingsStore
  {
    private readonly Exception? loadException;

    public TestSettingsStore(AppSettings initialSettings)
    {
      CurrentSettings = initialSettings;
    }

    public TestSettingsStore(Exception loadException)
    {
      this.loadException = loadException;
      CurrentSettings = AppSettings.Default;
    }

    public AppSettings CurrentSettings { get; private set; }

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
      if (loadException is not null)
      {
        throw loadException;
      }

      return Task.FromResult(CurrentSettings);
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
      CurrentSettings = settings;
      return Task.CompletedTask;
    }
  }

  private sealed class TestFileTransferService : ISettingsFileTransferService
  {
    public AppSettings? ImportResult { get; set; }
    public Exception? ImportException { get; set; }
    public AppSettings? LastExportedSettings { get; private set; }

    public Task<AppSettings> ImportAsync(string path, CancellationToken cancellationToken = default)
    {
      if (ImportException is not null)
      {
        throw ImportException;
      }

      return Task.FromResult(ImportResult ?? AppSettings.Default);
    }

    public Task ExportAsync(string path, AppSettings settings, CancellationToken cancellationToken = default)
    {
      LastExportedSettings = settings;
      return Task.CompletedTask;
    }
  }

  private sealed class TestModelManager : IModelManager
  {
    public TranscriptionModelSelection? LastActiveModelSelection { get; private set; }
    public TranscriptionModelSelection? LastDeletedModelSelection { get; private set; }

    public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default) =>
      Task.FromResult<IReadOnlyList<ModelInfo>>(new[]
      {
        new ModelInfo("test-provider", "model-default", "Model Default", true, true, new[] { "en" }),
      });

    public Task<ModelInfo?> GetActiveModelAsync(string providerId, CancellationToken cancellationToken = default) =>
      Task.FromResult<ModelInfo?>(null);

    public Task SetActiveModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default)
    {
      LastActiveModelSelection = selection;
      return Task.CompletedTask;
    }

    public Task DownloadModelAsync(TranscriptionModelSelection selection, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
      Task.CompletedTask;

    public Task DeleteModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default)
    {
      LastDeletedModelSelection = selection;
      return Task.CompletedTask;
    }
  }

  private sealed class RaceModelManager : IModelManager
  {
    public Task<IReadOnlyList<ModelInfo>>? NextResultTask { get; set; }

    public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
      return NextResultTask ?? Task.FromResult<IReadOnlyList<ModelInfo>>(Array.Empty<ModelInfo>());
    }

    public Task<ModelInfo?> GetActiveModelAsync(string providerId, CancellationToken cancellationToken = default) => Task.FromResult<ModelInfo?>(null);
    public Task SetActiveModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DownloadModelAsync(TranscriptionModelSelection selection, IProgress<double>? progress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default) => Task.CompletedTask;
  }

  private sealed class TestAudioDeviceService : IAudioInputDeviceService
  {
    public Task<IReadOnlyList<AudioInputDeviceOption>> GetInputDevicesAsync(CancellationToken cancellationToken = default) =>
      Task.FromResult<IReadOnlyList<AudioInputDeviceOption>>(new[]
      {
        new AudioInputDeviceOption("mic-default", "Default Mic"),
      });
  }

  private sealed class RaceAudioDeviceService : IAudioInputDeviceService
  {
    public Task<IReadOnlyList<AudioInputDeviceOption>>? NextResultTask { get; set; }

    public Task<IReadOnlyList<AudioInputDeviceOption>> GetInputDevicesAsync(CancellationToken cancellationToken = default)
    {
      return NextResultTask ?? Task.FromResult<IReadOnlyList<AudioInputDeviceOption>>(Array.Empty<AudioInputDeviceOption>());
    }
  }

  private sealed class TestBenchmarkService : IBenchmarkService
  {
    public Task<BenchmarkResult> RunAsync(string? languageScope = null, CancellationToken cancellationToken = default) =>
      Task.FromResult(new BenchmarkResult(
        "en",
        "en",
        "cohere-local",
        "cohere-transcribe-03-2026",
        "Recommended model",
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
    public List<string> Infos { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Errors { get; } = new();

    public void Info(string message) => Infos.Add(message);
    public void Warning(string message) => Warnings.Add(message);
    public void Error(string message, Exception? exception = null) =>
      Errors.Add($"{message}: {exception?.Message}");
  }
}

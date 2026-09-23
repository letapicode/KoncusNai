using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
  "Reliability",
  "CA2000:Dispose objects before losing scope",
  Justification = "The harness takes ownership of every disposable controller and session it constructs.")]
public sealed class WorkbenchSettingsApplicationControllerTests
{
  [Xunit.Fact]
  public async Task ApplyQueuedAsync_AppliesNormalizedSettingsAndConfiguresFallbackHotkey()
  {
    await using Harness harness = CreateHarness(
      new HotkeyRegistrationResult(false, "already registered"),
      new HotkeyRegistrationResult(true, null));
    AppSettings requested = AppSettings.Default with { ChatOutputFontSize = 1 };

    AppSettings normalized = harness.Controller.Queue(requested, registerHotkey: true);
    WorkbenchSettingsApplyResult result = await harness.Controller.ApplyQueuedAsync();

    Xunit.Assert.Equal(WorkbenchSettingsApplyStatus.Applied, result.Status);
    Xunit.Assert.False(result.ShouldRefreshModels);
    Xunit.Assert.NotNull(result.HotkeyStatus);
    Xunit.Assert.Equal(normalized.ChatOutputFontSize, harness.Controller.CurrentSettings.ChatOutputFontSize);
    Xunit.Assert.NotEqual(normalized.Hotkey, harness.Controller.CurrentSettings.Hotkey);
  }

  [Xunit.Fact]
  public async Task TryApplyPendingAsync_DefersDuringRecordingThenAppliesTheLatestReplacement()
  {
    await using Harness harness = CreateHarness(new HotkeyRegistrationResult(true, null));
    _ = harness.Controller.Queue(AppSettings.Default, registerHotkey: true);
    _ = await harness.Controller.ApplyQueuedAsync();
    Xunit.Assert.True(await harness.Dictation.StartRecordingAsync());

    _ = harness.Controller.Queue(AppSettings.Default with { ChatOutputFontSize = 17 }, registerHotkey: true);
    _ = harness.Controller.Queue(AppSettings.Default with { ChatOutputFontSize = 23 }, registerHotkey: false);
    WorkbenchSettingsApplyResult deferred = await harness.Controller.TryApplyPendingAsync();

    Xunit.Assert.Equal(WorkbenchSettingsApplyStatus.Deferred, deferred.Status);
    Xunit.Assert.True(harness.Controller.HasPendingSettings);

    _ = await harness.Dictation.StopAndTranscribeAsync();
    WorkbenchSettingsApplyResult applied = await harness.Controller.TryApplyPendingAsync();

    Xunit.Assert.True(applied.WasApplied);
    Xunit.Assert.Equal(23, harness.Controller.CurrentSettings.ChatOutputFontSize);
    Xunit.Assert.Equal("Hotkey: handled by Koncus Nai global dictation.", applied.HotkeyStatus);
  }

  [Xunit.Fact]
  public async Task TryApplyPendingAsync_DefersDuringChatAndResumesAfterTheChatLeaseCompletes()
  {
    await using Harness harness = CreateHarness(new HotkeyRegistrationResult(true, null));
    using WorkbenchChatOperation operation = Xunit.Assert.IsType<WorkbenchChatOperation>(
      harness.Chat.TryBeginOperation(WorkbenchChatOperationKind.Completion));
    _ = harness.Controller.Queue(AppSettings.Default with { ChatOutputFontSize = 21 }, registerHotkey: false);

    WorkbenchSettingsApplyResult deferred = await harness.Controller.TryApplyPendingAsync();
    Xunit.Assert.Equal(WorkbenchSettingsApplyStatus.Deferred, deferred.Status);

    operation.Dispose();
    WorkbenchSettingsApplyResult applied = await harness.Controller.TryApplyPendingAsync();
    Xunit.Assert.True(applied.WasApplied);
    Xunit.Assert.Equal(21, harness.Controller.CurrentSettings.ChatOutputFontSize);
  }

  [Xunit.Fact]
  public async Task TryApplyPendingAsync_ReturnsCancellationWithoutStartingTheSettingsLease()
  {
    await using Harness harness = CreateHarness(new HotkeyRegistrationResult(true, null));
    _ = harness.Controller.Queue(AppSettings.Default, registerHotkey: true);
    using CancellationTokenSource cancellation = new();
    cancellation.Cancel();

    WorkbenchSettingsApplyResult result = await harness.Controller.TryApplyPendingAsync(cancellation.Token);

    Xunit.Assert.Equal(WorkbenchSettingsApplyStatus.Cancelled, result.Status);
    Xunit.Assert.True(harness.Controller.HasPendingSettings);
    Xunit.Assert.False(harness.Operations.IsBusy);
  }

  [Xunit.Fact]
  public async Task ApplyQueuedAsync_ReportsHotkeyFailureWithoutExposingTheDiagnosticDetail()
  {
    await using Harness harness = CreateHarness(
      new HotkeyRegistrationResult(false, "already registered"),
      new HotkeyRegistrationResult(false, "already registered"));
    _ = harness.Controller.Queue(AppSettings.Default, registerHotkey: true);

    WorkbenchSettingsApplyResult result = await harness.Controller.ApplyQueuedAsync();

    Xunit.Assert.True(result.WasApplied);
    Xunit.Assert.NotNull(result.HotkeyStatus);
    Xunit.Assert.Contains("Buttons are still available", result.HotkeyStatus, StringComparison.Ordinal);
    Xunit.Assert.False(result.ShouldRefreshModels);

    _ = harness.Controller.Queue(AppSettings.Default with { ChatOutputFontSize = 20 }, registerHotkey: false);
    WorkbenchSettingsApplyResult recovered = await harness.Controller.ApplyQueuedAsync();
    Xunit.Assert.True(recovered.WasApplied);
    Xunit.Assert.Equal(20, harness.Controller.CurrentSettings.ChatOutputFontSize);
  }

  private static Harness CreateHarness(params HotkeyRegistrationResult[] registrations)
  {
    Queue<HotkeyRegistrationResult> remainingRegistrations = new(registrations);
    RecordingDiagnostics diagnostics = new();
    WorkbenchOperationSession operations = new();
    WorkbenchDictationController dictation = new(
      diagnostics,
      _ => new FakeAudioCaptureService(),
      (_, _) => new FakeTranscriptionService(),
      () => new FakeHotkeyService(remainingRegistrations));
    WorkbenchChatController chat = new(_ => new FakeChatCompletionService());
    WorkbenchReadAloudController readAloud = new(new WorkbenchSpeechSession(() => new FakeSpeechService()), diagnostics);
    WorkbenchQuickSettingsController quickSettings = new(new FakeModelManager(), diagnostics);
    WorkbenchSettingsApplicationController controller = new(
      diagnostics,
      quickSettings,
      operations,
      dictation,
      chat,
      readAloud);
    return new Harness(controller, operations, dictation, chat, readAloud, quickSettings);
  }

  private sealed class Harness(
    WorkbenchSettingsApplicationController controller,
    WorkbenchOperationSession operations,
    WorkbenchDictationController dictation,
    WorkbenchChatController chat,
    WorkbenchReadAloudController readAloud,
    WorkbenchQuickSettingsController quickSettings) : IAsyncDisposable
  {
    public WorkbenchSettingsApplicationController Controller { get; } = controller;
    public WorkbenchOperationSession Operations { get; } = operations;
    public WorkbenchDictationController Dictation { get; } = dictation;
    public WorkbenchChatController Chat { get; } = chat;

    public async ValueTask DisposeAsync()
    {
      await Controller.DisposeAsync();
      await QuickSettings.DisposeAsync();
      await Operations.DisposeAsync();
      await Dictation.DisposeAsync();
      await Chat.DisposeAsync();
      await ReadAloud.DisposeAsync();
    }

    private WorkbenchReadAloudController ReadAloud { get; } = readAloud;
    private WorkbenchQuickSettingsController QuickSettings { get; } = quickSettings;
  }

  private sealed class FakeAudioCaptureService : IAudioCaptureService
  {
    public bool IsCapturing { get; private set; }
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
      IsCapturing = true;
      return Task.CompletedTask;
    }

    public Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken = default)
    {
      IsCapturing = false;
      return Task.FromResult(new AudioCaptureResult([], 16_000, TimeSpan.Zero));
    }
  }

  private sealed class FakeTranscriptionService : ITranscriptionService
  {
    public Task<TranscriptionResult> TranscribeAsync(AudioCaptureResult audio, string modelId, CancellationToken cancellationToken = default) =>
      Task.FromResult(new TranscriptionResult(string.Empty, modelId, TimeSpan.Zero));
  }

  private sealed class FakeHotkeyService(Queue<HotkeyRegistrationResult> registrations) : IHotkeyService
  {
    public event EventHandler<HotkeyEventArgs>? HotkeyPressed { add { } remove { } }
    public event EventHandler<HotkeyEventArgs>? HotkeyReleased { add { } remove { } }
    public Task<HotkeyRegistrationResult> RegisterAsync(HotkeyBinding binding, CancellationToken cancellationToken = default) =>
      Task.FromResult(registrations.Count == 0 ? new HotkeyRegistrationResult(true, null) : registrations.Dequeue());
    public Task UnregisterAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public ValueTask DisposeAsync()
    {
      return ValueTask.CompletedTask;
    }
  }

  private sealed class FakeChatCompletionService : IChatCompletionService
  {
    public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
      Task.FromResult(new ChatCompletionResult(string.Empty, "test", "test", TimeSpan.Zero));
  }

  private sealed class FakeSpeechService : ITextToSpeechService
  {
    public Task<TextToSpeechResult> SynthesizeAsync(TextToSpeechRequest request, CancellationToken cancellationToken = default) =>
      Task.FromResult(new TextToSpeechResult("unused.wav", TimeSpan.Zero, 0, "test", "test"));
  }

  private sealed class FakeModelManager : IModelManager
  {
    public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModelInfo>>([]);
    public Task<ModelInfo?> GetActiveModelAsync(string providerId, CancellationToken cancellationToken = default) => Task.FromResult<ModelInfo?>(null);
    public Task SetActiveModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DownloadModelAsync(TranscriptionModelSelection selection, IProgress<double>? progress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default) => Task.CompletedTask;
  }

  private sealed class RecordingDiagnostics : IDiagnostics
  {
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
  }
}

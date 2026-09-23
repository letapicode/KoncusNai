using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Domain;
using DictateAnywhere.Core.Services;

namespace DictateAnywhere.Core.Tests;

public sealed class DictationPipelineCoordinatorTests
{
  [Xunit.Fact]
  public async Task FailedFinalFlush_CancelsAndDrainsThePreviousSessionBeforeReturningIdle()
  {
    await using FakeHotkeyService hotkey = new();
    FakeChunkedAudioCaptureService audio = new() { FlushFailure = new InvalidOperationException("device failed") };
    FakeTranscriptionService transcription = new(new TranscriptionResult("old", "model", TimeSpan.Zero))
    { BlockUntilRelease = true, IgnoreCancellation = true };
    FakeOverlayService overlay = new();
    FakeTextInsertionService insertion = new(InsertionResult.Verified(InsertionMethod.ClipboardPaste));
    await using DictationPipelineCoordinator coordinator = new(hotkey, audio, transcription, insertion,
      new RuleBasedTextTransformationService(), overlay,
      new FakeSettingsStore(AppSettings.Default with { RecordingMode = RecordingMode.HoldToTalk }),
      new FakeDiagnostics(), new FakeHistoryRecorder());
    try
    {
      await coordinator.StartAsync();
      hotkey.RaisePressed();
      await audio.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
      audio.EmitChunk(new(0, new([1, 0], 16_000, TimeSpan.FromMilliseconds(1)), false, DateTimeOffset.UtcNow));
      await transcription.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
      hotkey.RaiseReleased();
      await transcription.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
      Xunit.Assert.NotEqual(DictationSessionState.Idle, coordinator.CurrentState);
      Xunit.Assert.Equal(0, insertion.CallCount);
    }
    finally { transcription.Release(); }
    await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
    Xunit.Assert.Equal(DictationSessionState.Idle, coordinator.CurrentState);
    Xunit.Assert.Equal(0, insertion.CallCount);
  }

  [Xunit.Fact]
  public async Task SlowTranscriptionBacklog_ReportsFailureInsteadOfInsertingIncompleteText()
  {
    await using FakeHotkeyService hotkey = new();
    FakeChunkedAudioCaptureService audio = new();
    FakeTranscriptionService transcription = new(new TranscriptionResult("partial", "model", TimeSpan.Zero)) { BlockUntilRelease = true };
    FakeOverlayService overlay = new();
    FakeTextInsertionService insertion = new(InsertionResult.Verified(InsertionMethod.ClipboardPaste));
    await using DictationPipelineCoordinator coordinator = new(hotkey, audio, transcription, insertion,
      new RuleBasedTextTransformationService(), overlay,
      new FakeSettingsStore(AppSettings.Default with { RecordingMode = RecordingMode.HoldToTalk }),
      new FakeDiagnostics(), new FakeHistoryRecorder());
    try
    {
      await coordinator.StartAsync();
      hotkey.RaisePressed();
      await audio.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
      for (int i = 0; i < 10; i++)
        audio.EmitChunk(new(i, new(new byte[1024 * 1024], 16_000, TimeSpan.FromSeconds(32)), false, DateTimeOffset.UtcNow));
      transcription.Release();
      hotkey.RaiseReleased();
      await overlay.ErrorShown.Task.WaitAsync(TimeSpan.FromSeconds(5));
      Xunit.Assert.Equal(0, insertion.CallCount);
      Xunit.Assert.InRange(transcription.CallCount, 0, 4);
    }
    finally { transcription.Release(); }
  }

  [Xunit.Fact]
  public async Task HoldToTalk_PipelineHappyPath_UpdatesOverlayAndInsertsText()
  {
    AppSettings settings = AppSettings.Default with
    {
      RecordingMode = RecordingMode.HoldToTalk,
      PreferredInsertionMethod = InsertionMethod.ClipboardPaste,
      RestoreClipboard = true,
    };

    await using FakeHotkeyService hotkey = new();
    FakeAudioCaptureService audio = new();
    FakeTranscriptionService transcription = new(new TranscriptionResult("hello world", "cohere-transcribe-03-2026", TimeSpan.FromMilliseconds(120)));
    FakeTextInsertionService insertion = new(new InsertionResult(true, InsertionMethod.ClipboardPaste, null));
    FakeOverlayService overlay = new();
    FakeSettingsStore settingsStore = new(settings);
    FakeDiagnostics diagnostics = new();

    await using DictationPipelineCoordinator coordinator = new(
      hotkey,
      audio,
      transcription,
      insertion,
      new RuleBasedTextTransformationService(),
      overlay,
      settingsStore,
      diagnostics,
      new FakeHistoryRecorder());

    await coordinator.StartAsync();
    hotkey.RaisePressed();
    hotkey.RaiseReleased();

    await insertion.InsertCompletion.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await Task.Delay(50);

    Xunit.Assert.Equal(1, audio.StartCallCount);
    Xunit.Assert.Equal(1, audio.StopCallCount);
    Xunit.Assert.Equal(1, transcription.CallCount);
    Xunit.Assert.Equal(1, insertion.CallCount);
    Xunit.Assert.Equal(InsertionMethod.ClipboardPaste, insertion.LastPreferredMethod);
    Xunit.Assert.True(insertion.LastRestoreClipboard);

    DictationSessionState[] shownStates = overlay.ShownStates.Select(entry => entry.State).ToArray();
    Xunit.Assert.Contains(DictationSessionState.Recording, shownStates);
    Xunit.Assert.Contains(DictationSessionState.Transcribing, shownStates);
    Xunit.Assert.DoesNotContain(DictationSessionState.Inserting, shownStates);
    Xunit.Assert.Contains(DictationSessionState.Completed, shownStates);
    Xunit.Assert.Contains(
      overlay.ShownStates,
      entry => entry.State == DictationSessionState.Recording
               && entry.Display == OverlayDisplayOptions.AnchoredRecording);
    Xunit.Assert.Contains(
      overlay.ShownStates,
      entry => entry.State == DictationSessionState.Transcribing
               && entry.Display == OverlayDisplayOptions.AnchoredTranscribing);
    Xunit.Assert.Contains(
      overlay.ShownStates,
      entry => entry.State == DictationSessionState.Completed
               && entry.Display == OverlayDisplayOptions.AnchoredCompletion);
    Xunit.Assert.Equal(0, overlay.HideCallCount);
    Xunit.Assert.Equal(DictationSessionState.Idle, coordinator.CurrentState);
    Xunit.Assert.Contains(diagnostics.InfoMessages, msg => msg.Contains("Insertion verified", StringComparison.OrdinalIgnoreCase));
    Xunit.Assert.Contains(diagnostics.InfoMessages, msg => msg.Contains(nameof(InsertionMethod.ClipboardPaste), StringComparison.Ordinal));
    (string _, IReadOnlyDictionary<string, object?> timing) = Xunit.Assert.Single(
      diagnostics.StructuredInfo,
      entry => entry.Message == "Dictation stop-to-visible timing completed.");
    Xunit.Assert.Equal("cohere-local", timing["providerId"]);
    Xunit.Assert.Equal(nameof(InsertionOutcome.VerifiedInserted), timing["insertionOutcome"]);
    Xunit.Assert.True((double)timing["transcribingOverlayMs"]! >= 0);
    Xunit.Assert.True((double)timing["stopToVisibleMs"]! >= 0);
    (string _, IReadOnlyDictionary<string, object?> recording) = Xunit.Assert.Single(
      diagnostics.StructuredInfo,
      entry => entry.Message == "Recording started.");
    Xunit.Assert.True((double)recording["recordingOverlayMs"]! >= 0);
    Xunit.Assert.True((double)recording["captureStartMs"]! >= 0);
  }

  [Xunit.Fact]
  public async Task PipelineFailure_ShowsErrorAndRecoversToIdle()
  {
    AppSettings settings = AppSettings.Default with
    {
      RecordingMode = RecordingMode.HoldToTalk,
    };

    await using FakeHotkeyService hotkey = new();
    FakeAudioCaptureService audio = new();
    FakeTranscriptionService transcription = new(new InvalidOperationException("transcription failed"));
    FakeTextInsertionService insertion = new(new InsertionResult(true, InsertionMethod.ClipboardPaste, null));
    FakeOverlayService overlay = new();
    FakeSettingsStore settingsStore = new(settings);
    FakeDiagnostics diagnostics = new();

    await using DictationPipelineCoordinator coordinator = new(
      hotkey,
      audio,
      transcription,
      insertion,
      new RuleBasedTextTransformationService(),
      overlay,
      settingsStore,
      diagnostics,
      new FakeHistoryRecorder());

    await coordinator.StartAsync();
    hotkey.RaisePressed();
    hotkey.RaiseReleased();

    await overlay.ErrorShown.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await Task.Delay(50);

    Xunit.Assert.Contains(overlay.ShownStates, entry => entry.State == DictationSessionState.Error);
    Xunit.Assert.Equal(0, overlay.HideCallCount);
    Xunit.Assert.Equal(DictationSessionState.Idle, coordinator.CurrentState);
    Xunit.Assert.NotEmpty(diagnostics.ErrorMessages);
    Xunit.Assert.Equal(1, insertion.CaptureCallCount);
    Xunit.Assert.Equal(1, insertion.ClearCallCount);
  }

  [Xunit.Fact]
  public async Task TargetChangedAfterTranscription_CompletesAndRecordsRecoveryHistory()
  {
    AppSettings settings = AppSettings.Default with
    {
      RecordingMode = RecordingMode.HoldToTalk,
    };

    await using FakeHotkeyService hotkey = new();
    FakeAudioCaptureService audio = new();
    FakeTranscriptionService transcription = new(
      new TranscriptionResult("recover this text", "cohere-transcribe-03-2026", TimeSpan.FromMilliseconds(80)));
    FakeTextInsertionService insertion = new(InsertionResult.Blocked(
      InsertionMethod.ClipboardPaste,
      "The original target could not be restored. A recovery copy is on the clipboard.",
      InsertionBlockReason.TargetChanged) with { RecoveryCopyAvailable = true });
    FakeOverlayService overlay = new();
    FakeSettingsStore settingsStore = new(settings);
    FakeDiagnostics diagnostics = new();
    FakeHistoryRecorder history = new();

    await using DictationPipelineCoordinator coordinator = new(
      hotkey,
      audio,
      transcription,
      insertion,
      new RuleBasedTextTransformationService(),
      overlay,
      settingsStore,
      diagnostics,
      history);

    await coordinator.StartAsync();
    hotkey.RaisePressed();
    hotkey.RaiseReleased();

    DictationHistoryRecord record = await history.Recorded.Task.WaitAsync(TimeSpan.FromSeconds(2));

    Xunit.Assert.Equal("recover this text", record.FinalText);
    Xunit.Assert.Equal("global-hotkey-recovery", record.Source);
    Xunit.Assert.Contains(overlay.ShownStates, entry => entry.State == DictationSessionState.Completed);
    Xunit.Assert.Contains(
      overlay.ShownStates,
      entry => entry.State == DictationSessionState.Completed
               && entry.Display == OverlayDisplayOptions.AnchoredStatus);
    Xunit.Assert.Contains(
      overlay.ShownStates,
      entry => entry.State == DictationSessionState.Completed
               && entry.Message == DictationStatusMessages.InsertFailedCopiedAndSaved);
    Xunit.Assert.DoesNotContain(overlay.ShownStates, entry => entry.State == DictationSessionState.Error);
  }

  [Xunit.Fact]
  public async Task SecureFieldBlock_RemainsAnErrorAndDoesNotWriteHistory()
  {
    AppSettings settings = AppSettings.Default with
    {
      RecordingMode = RecordingMode.HoldToTalk,
    };

    await using FakeHotkeyService hotkey = new();
    FakeTextInsertionService insertion = new(InsertionResult.Blocked(
      InsertionMethod.ClipboardPaste,
      "Insertion blocked in a secure field.",
      InsertionBlockReason.SecureField));
    FakeOverlayService overlay = new();
    FakeHistoryRecorder history = new();

    await using DictationPipelineCoordinator coordinator = new(
      hotkey,
      new FakeAudioCaptureService(),
      new FakeTranscriptionService(new TranscriptionResult("private text", "cohere-transcribe-03-2026", TimeSpan.Zero)),
      insertion,
      new RuleBasedTextTransformationService(),
      overlay,
      new FakeSettingsStore(settings),
      new FakeDiagnostics(),
      history);

    await coordinator.StartAsync();
    hotkey.RaisePressed();
    hotkey.RaiseReleased();

    await overlay.ErrorShown.Task.WaitAsync(TimeSpan.FromSeconds(2));

    Xunit.Assert.False(history.Recorded.Task.IsCompleted);
    Xunit.Assert.Contains(overlay.ShownStates, entry => entry.State == DictationSessionState.Error);
  }

  [Xunit.Fact]
  public async Task Pipeline_CapturesInsertionTargetBeforeLongTranscription_AndClearsAfterInsertion()
  {
    AppSettings settings = AppSettings.Default with
    {
      RecordingMode = RecordingMode.HoldToTalk,
    };

    await using FakeHotkeyService hotkey = new();
    FakeAudioCaptureService audio = new();
    FakeTranscriptionService transcription = new(new TranscriptionResult("slow provider text", "cohere-transcribe-03-2026", TimeSpan.FromSeconds(65)))
    {
      BlockUntilRelease = true,
    };
    FakeTextInsertionService insertion = new(new InsertionResult(true, InsertionMethod.ClipboardPaste, null));
    FakeOverlayService overlay = new();
    FakeSettingsStore settingsStore = new(settings);
    FakeDiagnostics diagnostics = new();

    await using DictationPipelineCoordinator coordinator = new(
      hotkey,
      audio,
      transcription,
      insertion,
      new RuleBasedTextTransformationService(),
      overlay,
      settingsStore,
      diagnostics,
      new FakeHistoryRecorder());

    await coordinator.StartAsync();
    hotkey.RaisePressed();
    await Task.Delay(50);

    Xunit.Assert.Equal(1, insertion.CaptureCallCount);
    Xunit.Assert.Equal(0, insertion.ClearCallCount);

    hotkey.RaiseReleased();
    await transcription.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

    Xunit.Assert.Equal(1, insertion.CaptureCallCount);
    Xunit.Assert.Equal(0, insertion.ClearCallCount);

    transcription.Release();
    await insertion.InsertCompletion.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await Task.Delay(50);

    Xunit.Assert.Equal(1, insertion.CallCount);
    Xunit.Assert.Equal(1, insertion.CaptureCallCount);
    Xunit.Assert.Equal(1, insertion.ClearCallCount);
  }

  [Xunit.Fact]
  public async Task Pipeline_WhenInsertionIsOnlyDispatched_CompletesWithoutError()
  {
    AppSettings settings = AppSettings.Default with
    {
      RecordingMode = RecordingMode.HoldToTalk,
    };

    await using FakeHotkeyService hotkey = new();
    FakeAudioCaptureService audio = new();
    FakeTranscriptionService transcription = new(new TranscriptionResult("hello world", "cohere-transcribe-03-2026", TimeSpan.FromMilliseconds(120)));
    FakeTextInsertionService insertion = new(InsertionResult.Dispatched(
      InsertionMethod.ClipboardPaste,
      "Clipboard paste was dispatched, but the target surface could not be verified."));
    FakeOverlayService overlay = new();
    FakeSettingsStore settingsStore = new(settings);
    FakeDiagnostics diagnostics = new();

    await using DictationPipelineCoordinator coordinator = new(
      hotkey,
      audio,
      transcription,
      insertion,
      new RuleBasedTextTransformationService(),
      overlay,
      settingsStore,
      diagnostics,
      new FakeHistoryRecorder());

    await coordinator.StartAsync();
    hotkey.RaisePressed();
    hotkey.RaiseReleased();

    await insertion.InsertCompletion.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await Task.Delay(50);

    Xunit.Assert.DoesNotContain(overlay.ShownStates, entry => entry.State == DictationSessionState.Error);
    Xunit.Assert.Contains(overlay.ShownStates, entry => entry.State == DictationSessionState.Completed);
    Xunit.Assert.Contains(
      overlay.ShownStates,
      entry => entry.State == DictationSessionState.Completed
               && entry.Message is null
               && entry.Display == OverlayDisplayOptions.AnchoredCompletion);
    Xunit.Assert.Equal(0, overlay.HideCallCount);
    Xunit.Assert.Equal(DictationSessionState.Idle, coordinator.CurrentState);
    Xunit.Assert.Empty(diagnostics.ErrorMessages);
    Xunit.Assert.Contains(
      diagnostics.WarningMessages,
      message => message.Contains("could not be verified", StringComparison.OrdinalIgnoreCase));
  }

  [Xunit.Fact]
  public async Task Pipeline_UsesConfiguredTextTransformationOptions_BeforeInsertion()
  {
    AppSettings settings = AppSettings.Default with
    {
      RecordingMode = RecordingMode.HoldToTalk,
      EnableDictationCommands = true,
    };

    await using FakeHotkeyService hotkey = new();
    FakeAudioCaptureService audio = new();
    FakeTranscriptionService transcription = new(new TranscriptionResult("um hello comma world", "cohere-transcribe-03-2026", TimeSpan.FromMilliseconds(120)));
    FakeTextInsertionService insertion = new(new InsertionResult(true, InsertionMethod.ClipboardPaste, null));
    FakeTextTransformationService textTransformation = new("Hello, world.");
    FakeOverlayService overlay = new();
    FakeSettingsStore settingsStore = new(settings);
    FakeDiagnostics diagnostics = new();

    await using DictationPipelineCoordinator coordinator = new(
      hotkey,
      audio,
      transcription,
      insertion,
      textTransformation,
      overlay,
      settingsStore,
      diagnostics,
      new FakeHistoryRecorder());

    await coordinator.StartAsync();
    hotkey.RaisePressed();
    hotkey.RaiseReleased();

    await insertion.InsertCompletion.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await Task.Delay(50);

    Xunit.Assert.Equal(1, textTransformation.CallCount);
    Xunit.Assert.Equal("um hello comma world", textTransformation.LastInputText);
    Xunit.Assert.NotNull(textTransformation.LastOptions);
    Xunit.Assert.True(textTransformation.LastOptions!.EnableDictationCommands);
    Xunit.Assert.Equal("Hello, world.", insertion.LastInsertedText);
  }

  [Xunit.Fact]
  public async Task Pipeline_WithChunkedCapture_TranscribesChunksBeforeStopAndCombinesInOrder()
  {
    AppSettings settings = AppSettings.Default with
    {
      RecordingMode = RecordingMode.HoldToTalk,
    };

    await using FakeHotkeyService hotkey = new();
    FakeChunkedAudioCaptureService audio = new()
    {
      FinalChunk = new AudioCaptureChunk(
        1,
        new AudioCaptureResult([5, 0, 6, 0], 16_000, TimeSpan.FromMilliseconds(120)),
        IsFinal: true,
        CapturedAtUtc: DateTimeOffset.UtcNow),
    };
    FakeTranscriptionService transcription = new(
    new[]
    {
      new TranscriptionResult("first chunk", "cohere-transcribe-03-2026", TimeSpan.FromMilliseconds(45)),
      new TranscriptionResult("final chunk", "cohere-transcribe-03-2026", TimeSpan.FromMilliseconds(35)),
    });
    FakeTextInsertionService insertion = new(new InsertionResult(true, InsertionMethod.ClipboardPaste, null));
    FakeOverlayService overlay = new();
    FakeSettingsStore settingsStore = new(settings);
    FakeDiagnostics diagnostics = new();

    await using DictationPipelineCoordinator coordinator = new(
      hotkey,
      audio,
      transcription,
      insertion,
      new RuleBasedTextTransformationService(),
      overlay,
      settingsStore,
      diagnostics,
      new FakeHistoryRecorder());

    await coordinator.StartAsync();
    hotkey.RaisePressed();
    await audio.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

    audio.EmitChunk(
      new AudioCaptureChunk(
        0,
        new AudioCaptureResult([1, 0, 2, 0], 16_000, TimeSpan.FromMilliseconds(120)),
        IsFinal: false,
        CapturedAtUtc: DateTimeOffset.UtcNow));
    await transcription.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

    Xunit.Assert.Equal(1, transcription.CallCount);
    Xunit.Assert.Equal(0, insertion.CallCount);

    hotkey.RaiseReleased();
    await insertion.InsertCompletion.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await Task.Delay(50);

    Xunit.Assert.Equal(1, audio.StopAndFlushCallCount);
    Xunit.Assert.Equal(0, audio.StopCallCount);
    Xunit.Assert.Equal(2, transcription.CallCount);
    Xunit.Assert.Equal("first chunk final chunk", insertion.LastInsertedText);
    Xunit.Assert.Contains(
      diagnostics.InfoMessages,
      message => message.Contains("Chunked transcription completed", StringComparison.OrdinalIgnoreCase)
                 && message.Contains("2 chunk", StringComparison.OrdinalIgnoreCase));
  }

  [Xunit.Fact]
  public async Task StartAsync_WhenHotkeyRegistrationFails_ShowsOverlayErrorAndThrows()
  {
    AppSettings settings = AppSettings.Default;
    await using FakeHotkeyService hotkey = new()
    {
      RegistrationResult = new HotkeyRegistrationResult(false, "hotkey already registered"),
    };
    FakeAudioCaptureService audio = new();
    FakeTranscriptionService transcription = new(new TranscriptionResult("text", "cohere-transcribe-03-2026", TimeSpan.FromMilliseconds(10)));
    FakeTextInsertionService insertion = new(new InsertionResult(true, InsertionMethod.ClipboardPaste, null));
    FakeOverlayService overlay = new();
    FakeSettingsStore settingsStore = new(settings);
    FakeDiagnostics diagnostics = new();

    await using DictationPipelineCoordinator coordinator = new(
      hotkey,
      audio,
      transcription,
      insertion,
      new RuleBasedTextTransformationService(),
      overlay,
      settingsStore,
      diagnostics,
      new FakeHistoryRecorder());

    InvalidOperationException exception =
      await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StartAsync());

    Xunit.Assert.Contains("hotkey", exception.Message, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Contains(
      overlay.ShownStates,
      entry => entry.State == DictationSessionState.Error
               && entry.Message == DictationStatusMessages.HotkeyUnavailable);
    Xunit.Assert.Contains(diagnostics.ErrorMessages, entry => entry.Message == exception.Message);
  }

  [Xunit.Fact]
  public async Task ToggleMode_RapidSignalsWhileBusy_DropsReentrantSignals()
  {
    AppSettings settings = AppSettings.Default with
    {
      RecordingMode = RecordingMode.ToggleToTalk,
    };

    await using FakeHotkeyService hotkey = new();
    FakeAudioCaptureService audio = new();
    FakeTranscriptionService transcription = new(new TranscriptionResult("toggle", "cohere-transcribe-03-2026", TimeSpan.FromMilliseconds(50)))
    {
      BlockUntilRelease = true,
    };
    FakeTextInsertionService insertion = new(new InsertionResult(true, InsertionMethod.ClipboardPaste, null));
    FakeOverlayService overlay = new();
    FakeSettingsStore settingsStore = new(settings);
    FakeDiagnostics diagnostics = new();

    await using DictationPipelineCoordinator coordinator = new(
      hotkey,
      audio,
      transcription,
      insertion,
      new RuleBasedTextTransformationService(),
      overlay,
      settingsStore,
      diagnostics,
      new FakeHistoryRecorder());

    await coordinator.StartAsync();

    hotkey.RaisePressed(); // start recording
    hotkey.RaisePressed(); // stop + transcribe + insert

    await transcription.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

    for (int i = 0; i < 8; i++)
    {
      hotkey.RaisePressed();
      hotkey.RaiseReleased();
    }

    transcription.Release();
    await insertion.InsertCompletion.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await Task.Delay(50);

    Xunit.Assert.Equal(1, audio.StartCallCount);
    Xunit.Assert.Equal(1, audio.StopCallCount);
    Xunit.Assert.Equal(1, transcription.CallCount);
    Xunit.Assert.Equal(1, insertion.CallCount);
    Xunit.Assert.Contains(
      diagnostics.InfoMessages,
      message => message.Contains("Dropped reentrant", StringComparison.OrdinalIgnoreCase));
  }

  [Xunit.Fact]
  public async Task ToggleMode_FirstPressStarts_SecondPressStopsAndReturnsToIdle()
  {
    AppSettings settings = AppSettings.Default with
    {
      RecordingMode = RecordingMode.ToggleToTalk,
    };

    await using FakeHotkeyService hotkey = new();
    FakeAudioCaptureService audio = new();
    FakeTranscriptionService transcription = new(new TranscriptionResult("toggle ready", "cohere-transcribe-03-2026", TimeSpan.FromMilliseconds(40)));
    FakeTextInsertionService insertion = new(new InsertionResult(true, InsertionMethod.ClipboardPaste, null));
    FakeOverlayService overlay = new();
    FakeSettingsStore settingsStore = new(settings);
    FakeDiagnostics diagnostics = new();

    await using DictationPipelineCoordinator coordinator = new(
      hotkey,
      audio,
      transcription,
      insertion,
      new RuleBasedTextTransformationService(),
      overlay,
      settingsStore,
      diagnostics,
      new FakeHistoryRecorder());

    await coordinator.StartAsync();
    hotkey.RaisePressed();
    hotkey.RaisePressed();

    await insertion.InsertCompletion.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await Task.Delay(50);

    Xunit.Assert.Equal(1, audio.StartCallCount);
    Xunit.Assert.Equal(1, audio.StopCallCount);
    Xunit.Assert.Equal(1, transcription.CallCount);
    Xunit.Assert.Equal(1, insertion.CallCount);
    Xunit.Assert.Equal(DictationSessionState.Idle, coordinator.CurrentState);
  }

  [Xunit.Fact]
  public async Task Pipeline_ClipboardPreferred_WhenInsertionServiceUsesTypingFallback_CompletesSuccessfully()
  {
    AppSettings settings = AppSettings.Default with
    {
      RecordingMode = RecordingMode.ToggleToTalk,
      PreferredInsertionMethod = InsertionMethod.ClipboardPaste,
      RestoreClipboard = true,
    };

    await using FakeHotkeyService hotkey = new();
    FakeAudioCaptureService audio = new();
    FakeTranscriptionService transcription = new(new TranscriptionResult("fallback route", "cohere-transcribe-03-2026", TimeSpan.FromMilliseconds(40)));
    FakeTextInsertionService insertion = new(new InsertionResult(true, InsertionMethod.SendInputUnicodeTyping, null));
    FakeOverlayService overlay = new();
    FakeSettingsStore settingsStore = new(settings);
    FakeDiagnostics diagnostics = new();

    await using DictationPipelineCoordinator coordinator = new(
      hotkey,
      audio,
      transcription,
      insertion,
      new RuleBasedTextTransformationService(),
      overlay,
      settingsStore,
      diagnostics,
      new FakeHistoryRecorder());

    await coordinator.StartAsync();
    hotkey.RaisePressed();
    hotkey.RaisePressed();

    await insertion.InsertCompletion.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await Task.Delay(50);

    Xunit.Assert.Equal(InsertionMethod.ClipboardPaste, insertion.LastPreferredMethod);
    Xunit.Assert.True(insertion.LastRestoreClipboard);
    Xunit.Assert.Equal(1, insertion.CallCount);
    Xunit.Assert.Equal(DictationSessionState.Idle, coordinator.CurrentState);
    Xunit.Assert.Contains(
      diagnostics.InfoMessages,
      message => message.Contains(nameof(InsertionMethod.SendInputUnicodeTyping), StringComparison.Ordinal));
  }

  [Xunit.Fact]
  public async Task HoldToTalk_LongRunCycles_RemainsStable()
  {
    const int cycles = 40;

    AppSettings settings = AppSettings.Default with
    {
      RecordingMode = RecordingMode.HoldToTalk,
      PreferredInsertionMethod = InsertionMethod.ClipboardPaste,
      RestoreClipboard = true,
    };

    await using FakeHotkeyService hotkey = new();
    FakeAudioCaptureService audio = new();
    FakeTranscriptionService transcription = new(new TranscriptionResult("loop", "cohere-transcribe-03-2026", TimeSpan.FromMilliseconds(25)));
    FakeTextInsertionService insertion = new(new InsertionResult(true, InsertionMethod.ClipboardPaste, null));
    FakeOverlayService overlay = new();
    FakeSettingsStore settingsStore = new(settings);
    FakeDiagnostics diagnostics = new();

    await using DictationPipelineCoordinator coordinator = new(
      hotkey,
      audio,
      transcription,
      insertion,
      new RuleBasedTextTransformationService(),
      overlay,
      settingsStore,
      diagnostics,
      new FakeHistoryRecorder());

    await coordinator.StartAsync();

    for (int i = 0; i < cycles; i++)
    {
      insertion.ResetInsertCompletion();
      hotkey.RaisePressed();
      hotkey.RaiseReleased();
      await insertion.InsertCompletion.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    await Task.Delay(50);

    Xunit.Assert.Equal(cycles, audio.StartCallCount);
    Xunit.Assert.Equal(cycles, audio.StopCallCount);
    Xunit.Assert.Equal(cycles, transcription.CallCount);
    Xunit.Assert.Equal(cycles, insertion.CallCount);
    Xunit.Assert.Equal(DictationSessionState.Idle, coordinator.CurrentState);
    Xunit.Assert.Empty(diagnostics.ErrorMessages);
  }

  [Xunit.Fact]
  public async Task Pipeline_UsesCurrentInsertionAndSpokenCommandSettings()
  {
    AppSettings settings = AppSettings.Default with
    {
      RecordingMode = RecordingMode.HoldToTalk,
      PreferredInsertionMethod = InsertionMethod.SendInputUnicodeTyping,
      RestoreClipboard = false,
      EnableDictationCommands = true,
    };

    await using FakeHotkeyService hotkey = new();
    FakeAudioCaptureService audio = new();
    FakeTranscriptionService transcription = new(new TranscriptionResult("greeting line", "cohere-transcribe-03-2026", TimeSpan.FromMilliseconds(80)));
    FakeTextInsertionService insertion = new(new InsertionResult(true, InsertionMethod.ClipboardPaste, null));
    FakeTextTransformationService textTransformation = new("Hi team,");
    FakeOverlayService overlay = new();
    FakeSettingsStore settingsStore = new(settings);
    FakeDiagnostics diagnostics = new();

    await using DictationPipelineCoordinator coordinator = new(
      hotkey,
      audio,
      transcription,
      insertion,
      textTransformation,
      overlay,
      settingsStore,
      diagnostics,
      new FakeHistoryRecorder());

    await coordinator.StartAsync();
    hotkey.RaisePressed();
    hotkey.RaiseReleased();

    await insertion.InsertCompletion.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await Task.Delay(50);

    Xunit.Assert.NotNull(textTransformation.LastOptions);
    Xunit.Assert.True(textTransformation.LastOptions!.EnableDictationCommands);
    Xunit.Assert.Equal(InsertionMethod.SendInputUnicodeTyping, insertion.LastPreferredMethod);
    Xunit.Assert.False(insertion.LastRestoreClipboard);
  }

  [Xunit.Fact]
  public async Task Pipeline_WhenTransformationProducesEmptyText_SkipsInsertionAndLogsWarning()
  {
    AppSettings settings = AppSettings.Default with
    {
      RecordingMode = RecordingMode.HoldToTalk,
    };

    await using FakeHotkeyService hotkey = new();
    FakeAudioCaptureService audio = new();
    FakeTranscriptionService transcription = new(new TranscriptionResult("...", "cohere-transcribe-03-2026", TimeSpan.FromMilliseconds(15)));
    FakeTextInsertionService insertion = new(new InsertionResult(true, InsertionMethod.ClipboardPaste, null));
    FakeTextTransformationService textTransformation = new(string.Empty);
    FakeOverlayService overlay = new();
    FakeSettingsStore settingsStore = new(settings);
    FakeDiagnostics diagnostics = new();

    await using DictationPipelineCoordinator coordinator = new(
      hotkey,
      audio,
      transcription,
      insertion,
      textTransformation,
      overlay,
      settingsStore,
      diagnostics,
      new FakeHistoryRecorder());

    await coordinator.StartAsync();
    hotkey.RaisePressed();
    hotkey.RaiseReleased();

    await Task.Delay(100);

    Xunit.Assert.Equal(0, insertion.CallCount);
    Xunit.Assert.Equal(DictationSessionState.Idle, coordinator.CurrentState);
    Xunit.Assert.Contains(
      diagnostics.WarningMessages,
      message => message.Contains("no insertable text", StringComparison.OrdinalIgnoreCase));
  }

  [Xunit.Fact]
  public async Task Pipeline_WhenTranscriptionIsEmpty_ShowsNoAudibleSpeechNotice()
  {
    AppSettings settings = AppSettings.Default with
    {
      RecordingMode = RecordingMode.HoldToTalk,
    };

    await using FakeHotkeyService hotkey = new();
    FakeTextInsertionService insertion = new(new InsertionResult(true, InsertionMethod.ClipboardPaste, null));
    FakeOverlayService overlay = new();

    await using DictationPipelineCoordinator coordinator = new(
      hotkey,
      new FakeAudioCaptureService(),
      new FakeTranscriptionService(new TranscriptionResult(string.Empty, "cohere-transcribe-03-2026", TimeSpan.Zero)),
      insertion,
      new RuleBasedTextTransformationService(),
      overlay,
      new FakeSettingsStore(settings),
      new FakeDiagnostics(),
      new FakeHistoryRecorder());

    await coordinator.StartAsync();
    hotkey.RaisePressed();
    hotkey.RaiseReleased();

    await overlay.CompletionShown.Task.WaitAsync(TimeSpan.FromSeconds(2));

    Xunit.Assert.Equal(0, insertion.CallCount);
    Xunit.Assert.Equal(DictationSessionState.Idle, coordinator.CurrentState);
    Xunit.Assert.Contains(
      overlay.ShownStates,
      entry => entry.State == DictationSessionState.Completed
               && entry.Message == DictationStatusMessages.NoAudibleSpeechDetected
               && entry.Display == OverlayDisplayOptions.AnchoredNotice);
    Xunit.Assert.Equal(0, overlay.HideCallCount);
  }

  private sealed class FakeHotkeyService : IHotkeyService
  {
    public event EventHandler<HotkeyEventArgs>? HotkeyPressed;
    public event EventHandler<HotkeyEventArgs>? HotkeyReleased;

    public HotkeyRegistrationResult RegistrationResult { get; set; } = new(true, null);
    public int RegisterCallCount { get; private set; }
    public int UnregisterCallCount { get; private set; }

    public Task<HotkeyRegistrationResult> RegisterAsync(HotkeyBinding binding, CancellationToken cancellationToken = default)
    {
      RegisterCallCount++;
      return Task.FromResult(RegistrationResult);
    }

    public Task UnregisterAsync(CancellationToken cancellationToken = default)
    {
      UnregisterCallCount++;
      return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
      return ValueTask.CompletedTask;
    }

    public void RaisePressed()
    {
      HotkeyPressed?.Invoke(this, new HotkeyEventArgs(DateTimeOffset.UtcNow));
    }

    public void RaiseReleased()
    {
      HotkeyReleased?.Invoke(this, new HotkeyEventArgs(DateTimeOffset.UtcNow));
    }
  }

  private sealed class FakeAudioCaptureService : IAudioCaptureService
  {
    public int StartCallCount { get; private set; }
    public int StopCallCount { get; private set; }
    public bool IsCapturing { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
      StartCallCount++;
      IsCapturing = true;
      return Task.CompletedTask;
    }

    public Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken = default)
    {
      StopCallCount++;
      IsCapturing = false;
      return Task.FromResult(new AudioCaptureResult([1, 0, 2, 0], 16_000, TimeSpan.FromMilliseconds(350)));
    }
  }

  private sealed class FakeHistoryRecorder : IDictationHistoryRecorder
  {
    public TaskCompletionSource<DictationHistoryRecord> Recorded { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task RecordAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default)
    {
      Recorded.TrySetResult(record);
      return Task.CompletedTask;
    }
  }

  private sealed class FakeChunkedAudioCaptureService : IChunkedAudioCaptureService
  {
    public event EventHandler<AudioCaptureChunkAvailableEventArgs>? ChunkAvailable;

    public int StartCallCount { get; private set; }
    public int StopCallCount { get; private set; }
    public int StopAndFlushCallCount { get; private set; }
    public bool IsCapturing { get; private set; }
    public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public AudioCaptureChunk? FinalChunk { get; set; }
    public Exception? FlushFailure { get; set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
      StartCallCount++;
      IsCapturing = true;
      Started.TrySetResult(true);
      return Task.CompletedTask;
    }

    public Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken = default)
    {
      StopCallCount++;
      IsCapturing = false;
      return Task.FromResult(new AudioCaptureResult([1, 0, 2, 0], 16_000, TimeSpan.FromMilliseconds(240)));
    }

    public Task<AudioCaptureChunk?> StopAndFlushChunkAsync(CancellationToken cancellationToken = default)
    {
      StopAndFlushCallCount++;
      IsCapturing = false;
      if (FlushFailure is not null) throw FlushFailure;
      return Task.FromResult(FinalChunk);
    }

    public void EmitChunk(AudioCaptureChunk chunk)
    {
      ChunkAvailable?.Invoke(this, new AudioCaptureChunkAvailableEventArgs(chunk));
    }
  }

  private sealed class FakeTranscriptionService : ITranscriptionService
  {
    private readonly Queue<TranscriptionResult> successResults;
    private readonly Exception? failure;
    private readonly TaskCompletionSource<bool> releaseGate =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeTranscriptionService(TranscriptionResult result)
      : this(new[] { result })
    {
    }

    public FakeTranscriptionService(IReadOnlyList<TranscriptionResult> results)
    {
      successResults = new Queue<TranscriptionResult>(results);
      Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public FakeTranscriptionService(Exception failure)
      : this(new TranscriptionResult(string.Empty, "cohere-transcribe-03-2026", TimeSpan.Zero))
    {
      this.failure = failure;
      releaseGate.TrySetResult(true);
    }

    public int CallCount { get; private set; }
    public bool BlockUntilRelease { get; set; }
    public bool IgnoreCancellation { get; set; }
    public TaskCompletionSource<bool> Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Started { get; }

    public async Task<TranscriptionResult> TranscribeAsync(
      AudioCaptureResult audio,
      string modelId,
      CancellationToken cancellationToken = default)
    {
      CallCount++;
      Started.TrySetResult(true);

      if (BlockUntilRelease)
      {
        using CancellationTokenRegistration registration = cancellationToken.Register(() => Canceled.TrySetResult(true));
        await releaseGate.Task.WaitAsync(IgnoreCancellation ? CancellationToken.None : cancellationToken).ConfigureAwait(false);
      }

      if (failure is not null)
      {
        throw failure;
      }

      return successResults.Count > 1
        ? successResults.Dequeue()
        : successResults.Peek();
    }

    public void Release()
    {
      releaseGate.TrySetResult(true);
    }
  }

  private sealed class FakeTextInsertionService : ITextInsertionService, ITextInsertionTargetSession
  {
    private readonly InsertionResult result;
    private TaskCompletionSource<bool> insertCompletion;

    public FakeTextInsertionService(InsertionResult result)
    {
      this.result = result;
      insertCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public int CallCount { get; private set; }
    public int CaptureCallCount { get; private set; }
    public int ClearCallCount { get; private set; }
    public TaskCompletionSource<bool> InsertCompletion => insertCompletion;
    public string? LastInsertedText { get; private set; }
    public InsertionMethod LastPreferredMethod { get; private set; }
    public bool LastRestoreClipboard { get; private set; }

    public Task<InsertionResult> InsertAsync(
      string text,
      InsertionMethod preferredMethod,
      bool restoreClipboard,
      CancellationToken cancellationToken = default)
    {
      CallCount++;
      LastInsertedText = text;
      LastPreferredMethod = preferredMethod;
      LastRestoreClipboard = restoreClipboard;
      insertCompletion.TrySetResult(true);
      return Task.FromResult(result);
    }

    public void ResetInsertCompletion()
    {
      if (insertCompletion.Task.IsCompleted)
      {
        insertCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
      }
    }

    public void CaptureCurrentTarget()
    {
      CaptureCallCount++;
    }

    public void ClearCapturedTarget()
    {
      ClearCallCount++;
    }
  }

  private sealed class FakeTextTransformationService : ITextTransformationService
  {
    private readonly string output;

    public FakeTextTransformationService(string output)
    {
      this.output = output;
    }

    public int CallCount { get; private set; }

    public string? LastInputText { get; private set; }

    public TextTransformationOptions? LastOptions { get; private set; }

    public Task<TextTransformationResult> TransformAsync(
      TextTransformationRequest request,
      CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      CallCount++;
      LastInputText = request.Text;
      LastOptions = request.Options;
      return Task.FromResult(new TextTransformationResult(output));
    }
  }

  private sealed class FakeOverlayService : IOverlayService
  {
    public FakeOverlayService()
    {
      ErrorShown = new(TaskCreationOptions.RunContinuationsAsynchronously);
      CompletionShown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public List<(DictationSessionState State, string? Message, TimeSpan? Elapsed, OverlayDisplayOptions? Display)> ShownStates { get; } = new();
    public int HideCallCount { get; private set; }
    public TaskCompletionSource<bool> ErrorShown { get; }
    public TaskCompletionSource<bool> CompletionShown { get; }

    public Task ShowStateAsync(
      DictationSessionState state,
      string? message = null,
      TimeSpan? elapsed = null,
      OverlayDisplayOptions? display = null,
      CancellationToken cancellationToken = default)
    {
      ShownStates.Add((state, message, elapsed, display));
      if (state == DictationSessionState.Error)
      {
        ErrorShown.TrySetResult(true);
      }
      else if (state == DictationSessionState.Completed)
      {
        CompletionShown.TrySetResult(true);
      }

      return Task.CompletedTask;
    }

    public Task HideAsync(CancellationToken cancellationToken = default)
    {
      HideCallCount++;
      return Task.CompletedTask;
    }
  }

  private sealed class FakeSettingsStore : ISettingsStore
  {
    private readonly AppSettings settings;

    public FakeSettingsStore(AppSettings settings)
    {
      this.settings = settings;
    }

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
      return Task.FromResult(settings);
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
      return Task.CompletedTask;
    }
  }

  private sealed class FakeDiagnostics : IStructuredDiagnostics
  {
    public List<string> InfoMessages { get; } = new();
    public List<(string Message, IReadOnlyDictionary<string, object?> Properties)> StructuredInfo { get; } = new();
    public List<string> WarningMessages { get; } = new();
    public List<(string Message, Exception? Exception)> ErrorMessages { get; } = new();

    public void Info(string message)
    {
      InfoMessages.Add(message);
    }

    public void Info(string message, IReadOnlyDictionary<string, object?> properties)
    {
      InfoMessages.Add(message);
      StructuredInfo.Add((message, properties));
    }

    public void Warning(string message)
    {
      WarningMessages.Add(message);
    }

    public void Warning(string message, IReadOnlyDictionary<string, object?> properties)
    {
      WarningMessages.Add(message);
    }

    public void Error(string message, Exception? exception = null)
    {
      ErrorMessages.Add((message, exception));
    }

    public void Error(
      string message,
      Exception? exception,
      IReadOnlyDictionary<string, object?> properties)
    {
      ErrorMessages.Add((message, exception));
    }
  }
}

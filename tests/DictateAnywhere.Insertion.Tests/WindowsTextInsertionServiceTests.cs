using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Insertion;

namespace DictateAnywhere.Insertion.Tests;

public sealed class WindowsTextInsertionServiceTests
{
  [Xunit.Fact]
  public async Task RichClipboardIsPreservedEvenWhenTypingFallbackFails()
  {
    FakeClipboardController clipboard = new() { CaptureSnapshotException = new ClipboardContentNotSupportedException("rich") };
    FakeInputDispatcher input = new() { TypingResult = new InputDispatchResult(false, "failed") };
    WindowsTextInsertionService service = CreateService(clipboard, input);
    InsertionResult result = await service.InsertAsync("dictation", InsertionMethod.ClipboardPaste, true);
    Xunit.Assert.False(result.RecoveryCopyAvailable);
    Xunit.Assert.Null(clipboard.LastSetText);
    Xunit.Assert.Equal(0, input.PasteCallCount);
  }

  [Xunit.Fact]
  public async Task PartialPasteFailureNeverReplaysTheWholeText()
  {
    FakeInputDispatcher input = new() { PasteResult = new InputDispatchResult(false, "partial") { MayHaveDispatched = true } };
    WindowsTextInsertionService service = CreateService(new FakeClipboardController(), input);
    _ = await service.InsertAsync("dictation", InsertionMethod.ClipboardPaste, true);
    Xunit.Assert.Equal(1, input.PasteCallCount);
    Xunit.Assert.Equal(0, input.TypingCallCount);
  }

  [Xunit.Fact]
  public async Task FocusChangeDuringLongTypingStopsSubsequentBatches()
  {
    FakeInputDispatcher input = new();
    FakeWindowFocusProvider focus = new();
    WindowsTextInsertionService service = CreateService(new FakeClipboardController(), input, focus);
    input.OnTyping = _ => focus.FocusContext = focus.FocusContext with { FocusedAutomationRuntimeId = "other-editor" };
    InsertionResult result = await service.InsertAsync(new string('x', 1000), InsertionMethod.SendInputUnicodeTyping, true);
    Xunit.Assert.False(result.Success);
    Xunit.Assert.Equal(1, input.TypingCallCount);
  }

  [Xunit.Fact]
  public void AuthorizedTargetCaptureRejectsAnotherEditorInTheSameWindow()
  {
    FakeWindowFocusProvider focus = new();
    WindowsTextInsertionService service = CreateService(new FakeClipboardController(), new FakeInputDispatcher(), focus);
    InsertionTargetIdentity authorized = InsertionTargetIdentity.FromContext(focus.GetWindowFocusContext());
    focus.FocusContext = focus.FocusContext with { FocusedAutomationRuntimeId = "other-editor" };
    Xunit.Assert.False(service.TryCaptureTarget(authorized));
    Xunit.Assert.False((authorized with { ProcessStartTicks = null }).Matches(focus.GetWindowFocusContext()));
  }

  [Xunit.Fact]
  public async Task NewClipboardWriteDuringVerification_IsNeitherRestoredNorReplacedByRecovery()
  {
    FakeClipboardController clipboard = new();
    FakeInputDispatcher input = new();
    FakeWindowFocusProvider focus = new() { TrackObservedTextChanges = false };
    WindowsTextInsertionService service = CreateService(clipboard, input, focus,
      options: TextInsertionOptions.Default with { ClipboardConsumptionWindow = TimeSpan.FromMilliseconds(1) });
    input.OnPaste = () => clipboard.SetUnicodeText("new user copy");
    InsertionResult result = await service.InsertAsync("dictation", InsertionMethod.ClipboardPaste, true);
    Xunit.Assert.Equal("new user copy", clipboard.LastSetText);
    Xunit.Assert.False(result.RecoveryCopyAvailable);
    Xunit.Assert.Equal(0, clipboard.RestoreSnapshotCallCount);
  }

  [Xunit.Fact]
  public async Task CapturedFieldChangedWithinWindow_DoesNotDispatch()
  {
    FakeClipboardController clipboard = new();
    FakeInputDispatcher input = new();
    FakeWindowFocusProvider focus = new();
    focus.FocusContext = focus.FocusContext with { FocusedAutomationRuntimeId = "field-a", FocusResolutionSource = "RawFocusedElement" };
    WindowsTextInsertionService service = CreateService(clipboard, input, focus);
    service.CaptureCurrentTarget();
    focus.FocusContext = focus.FocusContext with { FocusedAutomationRuntimeId = "field-b", FocusedControlPasswordProtected = true };
    InsertionResult result = await service.InsertAsync("private", InsertionMethod.ClipboardPaste, true);
    Xunit.Assert.False(result.Success);
    Xunit.Assert.Equal(0, input.PasteCallCount + input.TypingCallCount);
  }

  [Xunit.Fact]
  public async Task TargetChangedDuringClipboardAcquisition_DoesNotDispatch()
  {
    FakeClipboardController clipboard = new();
    FakeInputDispatcher input = new();
    FakeWindowFocusProvider focus = new();
    WindowsTextInsertionService service = CreateService(clipboard, input, focus);
    clipboard.OnSet = () => focus.FocusContext = focus.FocusContext with { FocusedAutomationRuntimeId = "different" };
    InsertionResult result = await service.InsertAsync("private", InsertionMethod.ClipboardPaste, true);
    Xunit.Assert.Equal(InsertionBlockReason.TargetChanged, result.BlockReason);
    Xunit.Assert.Equal(0, input.PasteCallCount + input.TypingCallCount);
  }

  [Xunit.Fact]
  public async Task PasteWithUnchangedValue_DoesNotReplayText()
  {
    FakeClipboardController clipboard = new();
    FakeInputDispatcher input = new();
    FakeWindowFocusProvider focus = new() { TrackObservedTextChanges = false };
    focus.FocusContext = focus.FocusContext with { ObservedText = "hello" };
    WindowsTextInsertionService service = CreateService(clipboard, input, focus,
      options: TextInsertionOptions.Default with { ClipboardConsumptionWindow = TimeSpan.FromMilliseconds(1), VerificationPollingInterval = TimeSpan.FromMilliseconds(1) });
    InsertionResult result = await service.InsertAsync("hello", InsertionMethod.ClipboardPaste, true);
    Xunit.Assert.Equal(InsertionOutcome.Dispatched, result.Outcome);
    Xunit.Assert.Equal(1, input.PasteCallCount);
    Xunit.Assert.Equal(0, input.TypingCallCount);
  }

  [Xunit.Fact]
  public async Task BlacklistedElevatedApplication_NeverInvokesHelper()
  {
    FakeElevatedInsertionBridge bridge = new() { Result = InsertionResult.Verified(InsertionMethod.ClipboardPaste) };
    FakeClipboardController clipboard = new();
    FakeInputDispatcher input = new();
    WindowsTextInsertionService service = CreateService(clipboard, input,
      boundaryDetector: new FakePrivilegeBoundaryDetector { Result = new PrivilegeBoundaryCheckResult(false, "UIPI") },
      options: TextInsertionOptions.Default with { EnableElevatedInsertion = true, BlockedProcessNames = ["notepad"] },
      elevatedInsertionBridge: bridge);
    InsertionResult result = await service.InsertAsync("private", InsertionMethod.ClipboardPaste, true);
    Xunit.Assert.Equal(InsertionBlockReason.BlockedApplication, result.BlockReason);
    Xunit.Assert.Equal(0, bridge.CallCount);
    Xunit.Assert.Null(clipboard.LastSetText);
  }

  [Xunit.Theory]
  [Xunit.InlineData(true)]
  [Xunit.InlineData(false)]
  public async Task UndoAfterInterveningEditOrEditorChange_IsRejected(bool edit)
  {
    FakeInputDispatcher input = new();
    FakeWindowFocusProvider focus = new();
    WindowsTextInsertionService service = CreateService(new FakeClipboardController(), input, focus);
    Xunit.Assert.True((await service.InsertAsync("hello", InsertionMethod.ClipboardPaste, true)).Success);
    focus.FocusContext = edit
      ? focus.FocusContext with { ObservedText = "hello later edit" }
      : focus.FocusContext with { FocusedAutomationRuntimeId = "other-editor" };
    Xunit.Assert.False((await service.UndoLastInsertionAsync()).Success);
    Xunit.Assert.Equal(0, input.UndoCallCount);
  }

  [Xunit.Fact]
  public async Task InsertAsync_ClipboardSuccess_PreservesAndRestoresClipboard()
  {
    FakeClipboardController clipboard = new()
    {
      SnapshotToReturn = ClipboardSnapshot.FromUnicodeText("existing-value"),
    };
    FakeInputDispatcher input = new()
    {
      PasteResult = InputDispatchResult.Ok,
      TypingResult = InputDispatchResult.Ok,
    };
    FakeDiagnostics diagnostics = new();

    WindowsTextInsertionService service = CreateService(clipboard, input, diagnostics: diagnostics);

    InsertionResult result = await service.InsertAsync(
      "new text",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.True(result.Success);
    Xunit.Assert.Equal(InsertionOutcome.VerifiedInserted, result.Outcome);
    Xunit.Assert.Equal(InsertionMethod.ClipboardPaste, result.MethodUsed);
    Xunit.Assert.Null(result.ErrorMessage);
    Xunit.Assert.Equal(1, clipboard.CaptureSnapshotCallCount);
    Xunit.Assert.Equal(1, clipboard.RestoreSnapshotCallCount);
    Xunit.Assert.Equal("new text", clipboard.LastSetText);
    Xunit.Assert.Equal(1, input.PasteCallCount);
    Xunit.Assert.Equal(0, input.TypingCallCount);
    Xunit.Assert.Contains(
      diagnostics.InfoMessages,
      message => message.Contains("process='notepad'", StringComparison.OrdinalIgnoreCase));
    Xunit.Assert.Contains(
      diagnostics.InfoMessages,
      message => message.Contains("textLength=8", StringComparison.Ordinal));
  }

  [Xunit.Fact]
  public async Task InsertAsync_ClipboardFails_FallsBackToUnicodeTyping()
  {
    FakeClipboardController clipboard = new()
    {
      SnapshotToReturn = ClipboardSnapshot.Empty,
      SetTextException = new ClipboardOperationException("clipboard busy"),
    };
    FakeInputDispatcher input = new()
    {
      PasteResult = InputDispatchResult.Ok,
      TypingResult = InputDispatchResult.Ok,
    };

    WindowsTextInsertionService service = CreateService(clipboard, input);

    InsertionResult result = await service.InsertAsync(
      "fallback text",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.True(result.Success);
    Xunit.Assert.Equal(InsertionOutcome.VerifiedInserted, result.Outcome);
    Xunit.Assert.Equal(InsertionMethod.SendInputUnicodeTyping, result.MethodUsed);
    Xunit.Assert.Equal(1, input.TypingCallCount);
  }

  [Xunit.Fact]
  public async Task InsertAsync_RestoreClipboardFalse_DoesNotCaptureOrRestore()
  {
    FakeClipboardController clipboard = new();
    FakeInputDispatcher input = new()
    {
      PasteResult = InputDispatchResult.Ok,
      TypingResult = InputDispatchResult.Ok,
    };
    WindowsTextInsertionService service = CreateService(clipboard, input);

    InsertionResult result = await service.InsertAsync(
      "paste text",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: false);

    Xunit.Assert.True(result.Success);
    Xunit.Assert.Equal(InsertionOutcome.VerifiedInserted, result.Outcome);
    Xunit.Assert.Equal(InsertionMethod.ClipboardPaste, result.MethodUsed);
    Xunit.Assert.Equal(0, clipboard.CaptureSnapshotCallCount);
    Xunit.Assert.Equal(0, clipboard.RestoreSnapshotCallCount);
  }

  [Xunit.Fact]
  public async Task InsertAsync_WhenCapturedTargetDrifts_RestoresOriginalWindowBeforePasting()
  {
    FakeClipboardController clipboard = new();
    FakeInputDispatcher input = new()
    {
      PasteResult = InputDispatchResult.Ok,
      TypingResult = InputDispatchResult.Ok,
    };
    FakeWindowFocusProvider focusProvider = new()
    {
      ForegroundWindowHandle = 10,
      FocusContext = new WindowFocusContext
      {
        ForegroundWindowHandle = 10,
        ForegroundProcessId = 100,
        ForegroundProcessName = "notepad",
        ForegroundWindowClassName = "Notepad",
        FocusedControlClassName = "Edit",
        Editability = WindowEditability.Editable,
        VerificationMode = "ValuePattern",
        ObservedText = string.Empty,
      },
    };
    WindowsTextInsertionService service = CreateService(
      clipboard,
      input,
      windowFocusProvider: focusProvider);
    service.CaptureCurrentTarget();

    focusProvider.FocusContext = new WindowFocusContext
    {
      ForegroundWindowHandle = 20,
      ForegroundProcessId = 200,
      ForegroundProcessName = "explorer",
      ForegroundWindowClassName = "TopLevelWindowForOverflowXamlIsland",
      FocusedControlClassName = "SystemTray.NormalButton",
      Editability = WindowEditability.Unknown,
    };
    focusProvider.ForegroundWindowHandle = 20;

    InsertionResult result = await service.InsertAsync(
      "captured text",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.True(result.Success);
    Xunit.Assert.Equal(1, focusProvider.ForegroundRestoreCallCount);
    Xunit.Assert.Equal(10, focusProvider.LastForegroundRestoreTarget?.ForegroundWindowHandle);
    Xunit.Assert.Equal(1, input.PasteCallCount);
  }

  [Xunit.Fact]
  public async Task InsertAsync_WhenCapturedTargetIsSlowToRestore_RetriesBeforePasting()
  {
    FakeClipboardController clipboard = new();
    FakeInputDispatcher input = new()
    {
      PasteResult = InputDispatchResult.Ok,
      TypingResult = InputDispatchResult.Ok,
    };
    FakeWindowFocusProvider focusProvider = new()
    {
      ForegroundWindowHandle = 10,
      FocusContext = new WindowFocusContext
      {
        ForegroundWindowHandle = 10,
        ForegroundProcessId = 100,
        ForegroundProcessName = "notepad",
        ForegroundWindowClassName = "Notepad",
        FocusedControlClassName = "Edit",
        Editability = WindowEditability.Editable,
        VerificationMode = "ValuePattern",
      },
    };
    WindowsTextInsertionService service = CreateService(clipboard, input, windowFocusProvider: focusProvider);
    service.CaptureCurrentTarget();

    focusProvider.FocusContext = new WindowFocusContext
    {
      ForegroundWindowHandle = 20,
      ForegroundProcessId = 200,
      ForegroundProcessName = "chrome",
      ForegroundWindowClassName = "Chrome_WidgetWin_1",
      FocusedControlClassName = "Edit",
      Editability = WindowEditability.Editable,
    };
    focusProvider.ForegroundWindowHandle = 20;
    focusProvider.ForegroundRestoreFailuresRemaining = 1;

    InsertionResult result = await service.InsertAsync(
      "captured text",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.True(result.Success);
    Xunit.Assert.Equal(2, focusProvider.ForegroundRestoreCallCount);
    Xunit.Assert.Equal(1, input.PasteCallCount);
  }

  [Xunit.Fact]
  public async Task InsertAsync_WhenCapturedTargetCannotBeRestored_PreservesRecoveryCopy()
  {
    FakeClipboardController clipboard = new();
    FakeInputDispatcher input = new();
    FakeWindowFocusProvider focusProvider = new()
    {
      ForegroundWindowHandle = 10,
      FocusContext = new WindowFocusContext
      {
        ForegroundWindowHandle = 10,
        ForegroundProcessId = 100,
        ForegroundProcessName = "notepad",
        Editability = WindowEditability.Editable,
      },
    };
    WindowsTextInsertionService service = CreateService(clipboard, input, windowFocusProvider: focusProvider);
    service.CaptureCurrentTarget();
    focusProvider.FocusContext = new WindowFocusContext
    {
      ForegroundWindowHandle = 20,
      ForegroundProcessId = 200,
      ForegroundProcessName = "explorer",
      Editability = WindowEditability.Unknown,
    };
    focusProvider.ForegroundWindowHandle = 20;
    focusProvider.ForegroundRestoreSucceeds = false;

    InsertionResult result = await service.InsertAsync(
      "recover this text",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.Equal(InsertionOutcome.Blocked, result.Outcome);
    Xunit.Assert.Equal(InsertionBlockReason.TargetChanged, result.BlockReason);
    Xunit.Assert.True(result.CanRecoverTranscript);
    Xunit.Assert.Equal("recover this text", clipboard.LastSetText);
    Xunit.Assert.Equal(0, input.PasteCallCount);
    Xunit.Assert.Equal(0, input.TypingCallCount);
  }

  [Xunit.Fact]
  public async Task InsertAsync_WhenSecureCapturedTargetCannotBeRestored_DoesNotCopyTranscript()
  {
    FakeClipboardController clipboard = new();
    FakeWindowFocusProvider focusProvider = new()
    {
      ForegroundWindowHandle = 10,
      FocusContext = new WindowFocusContext
      {
        ForegroundWindowHandle = 10,
        ForegroundProcessId = 100,
        ForegroundProcessName = "notepad",
        FocusedControlPasswordProtected = true,
        Editability = WindowEditability.Editable,
      },
    };
    WindowsTextInsertionService service = CreateService(
      clipboard,
      new FakeInputDispatcher(),
      windowFocusProvider: focusProvider);
    service.CaptureCurrentTarget();
    focusProvider.FocusContext = new WindowFocusContext
    {
      ForegroundWindowHandle = 20,
      ForegroundProcessId = 200,
      ForegroundProcessName = "explorer",
    };
    focusProvider.ForegroundWindowHandle = 20;
    focusProvider.ForegroundRestoreSucceeds = false;

    InsertionResult result = await service.InsertAsync(
      "do not copy this",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.Equal(InsertionOutcome.Blocked, result.Outcome);
    Xunit.Assert.Equal(InsertionBlockReason.SecureField, result.BlockReason);
    Xunit.Assert.False(result.CanRecoverTranscript);
    Xunit.Assert.Null(clipboard.LastSetText);
  }

  [Xunit.Fact]
  public async Task InsertAsync_BlockedByPrivilegeBoundary_ReturnsBlockedClassification()
  {
    FakeClipboardController clipboard = new();
    FakeInputDispatcher input = new()
    {
      PasteResult = InputDispatchResult.Ok,
      TypingResult = InputDispatchResult.Ok,
    };
    FakePrivilegeBoundaryDetector boundaryDetector = new()
    {
      Result = new PrivilegeBoundaryCheckResult(
        false,
        "Insertion into elevated applications is blocked by Windows privilege boundaries (UIPI)."),
    };

    WindowsTextInsertionService service = CreateService(clipboard, input, boundaryDetector: boundaryDetector);

    InsertionResult result = await service.InsertAsync(
      "blocked text",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.False(result.Success);
    Xunit.Assert.Equal(InsertionOutcome.Blocked, result.Outcome);
    Xunit.Assert.Equal(InsertionMethod.ClipboardPaste, result.MethodUsed);
    Xunit.Assert.NotNull(result.ErrorMessage);
    Xunit.Assert.Contains("UIPI", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Equal(0, clipboard.CaptureSnapshotCallCount);
    Xunit.Assert.Equal(0, input.PasteCallCount);
    Xunit.Assert.Equal(0, input.TypingCallCount);
  }

  [Xunit.Fact]
  public async Task InsertAsync_BlockedByUserBlacklist_ReturnsBlockedClassification()
  {
    FakeClipboardController clipboard = new();
    FakeInputDispatcher input = new()
    {
      PasteResult = InputDispatchResult.Ok,
      TypingResult = InputDispatchResult.Ok,
    };
    FakeWindowFocusProvider focusProvider = new()
    {
      FocusContext = new WindowFocusContext
      {
        ForegroundWindowHandle = 42,
        ForegroundProcessId = 101,
        ForegroundProcessName = "Code",
        ForegroundWindowClassName = "Chrome_WidgetWin_1",
        FocusedControlClassName = "Edit",
        FocusedControlPasswordProtected = false,
        ForegroundWindowTitle = "VS Code",
      },
    };

    WindowsTextInsertionService service = CreateService(
      clipboard,
      input,
      windowFocusProvider: focusProvider,
      options: new TextInsertionOptions(
        EnableSecureFieldDetection: true,
        BlockedProcessNames: new[] { "code.exe" },
        EnableElevatedInsertion: false));

    InsertionResult result = await service.InsertAsync(
      "blocked text",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.False(result.Success);
    Xunit.Assert.Equal(InsertionOutcome.Blocked, result.Outcome);
    Xunit.Assert.Contains("blacklist", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Equal(0, input.PasteCallCount);
    Xunit.Assert.Equal(0, input.TypingCallCount);
  }

  [Xunit.Fact]
  public async Task InsertAsync_BlockedBySecureFieldHeuristic_ReturnsBlockedClassification()
  {
    FakeClipboardController clipboard = new();
    FakeInputDispatcher input = new()
    {
      PasteResult = InputDispatchResult.Ok,
      TypingResult = InputDispatchResult.Ok,
    };
    FakeWindowFocusProvider focusProvider = new()
    {
      FocusContext = new WindowFocusContext
      {
        ForegroundWindowHandle = 55,
        ForegroundProcessId = 202,
        ForegroundProcessName = "notepad",
        ForegroundWindowClassName = "Notepad",
        FocusedControlClassName = "Edit",
        FocusedControlPasswordProtected = true,
        ForegroundWindowTitle = "Secure Note",
      },
    };

    WindowsTextInsertionService service = CreateService(
      clipboard,
      input,
      windowFocusProvider: focusProvider,
      options: new TextInsertionOptions(
        EnableSecureFieldDetection: true,
        BlockedProcessNames: Array.Empty<string>(),
        EnableElevatedInsertion: false));

    InsertionResult result = await service.InsertAsync(
      "blocked text",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.False(result.Success);
    Xunit.Assert.Equal(InsertionOutcome.Blocked, result.Outcome);
    Xunit.Assert.Contains("secure field", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Equal(0, input.PasteCallCount);
    Xunit.Assert.Equal(0, input.TypingCallCount);
  }

  [Xunit.Fact]
  public async Task InsertAsync_BothMethodsFail_ReturnsUnknownFailureClassification()
  {
    FakeClipboardController clipboard = new()
    {
      SnapshotToReturn = ClipboardSnapshot.Empty,
      SetTextException = new ClipboardOperationException("clipboard unavailable"),
    };
    FakeInputDispatcher input = new()
    {
      PasteResult = InputDispatchResult.Ok,
      TypingResult = new InputDispatchResult(false, "sendinput failure"),
    };

    WindowsTextInsertionService service = CreateService(clipboard, input);

    InsertionResult result = await service.InsertAsync(
      "fail text",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.False(result.Success);
    Xunit.Assert.Equal(InsertionOutcome.UnknownOutcome, result.Outcome);
    Xunit.Assert.Equal(InsertionMethod.SendInputUnicodeTyping, result.MethodUsed);
    Xunit.Assert.NotNull(result.ErrorMessage);
    Xunit.Assert.Contains("Clipboard paste returned UnknownOutcome", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Contains("Fallback typing returned UnknownOutcome", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Contains("recovery copy could not", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task InsertAsync_ClipboardComplexContentFallbacksToTyping()
  {
    FakeClipboardController clipboard = new()
    {
      CaptureSnapshotException = new ClipboardContentNotSupportedException("complex clipboard content"),
    };
    FakeInputDispatcher input = new()
    {
      PasteResult = InputDispatchResult.Ok,
      TypingResult = InputDispatchResult.Ok,
    };

    WindowsTextInsertionService service = CreateService(clipboard, input);

    InsertionResult result = await service.InsertAsync(
      "typing path",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.True(result.Success);
    Xunit.Assert.Equal(InsertionOutcome.VerifiedInserted, result.Outcome);
    Xunit.Assert.Equal(InsertionMethod.SendInputUnicodeTyping, result.MethodUsed);
    Xunit.Assert.Equal(1, input.TypingCallCount);
  }

  [Xunit.Fact]
  public async Task InsertAsync_TypingPreferred_UsesTypingWithoutClipboardCalls()
  {
    FakeClipboardController clipboard = new();
    FakeInputDispatcher input = new()
    {
      PasteResult = InputDispatchResult.Ok,
      TypingResult = InputDispatchResult.Ok,
    };

    WindowsTextInsertionService service = CreateService(clipboard, input);

    InsertionResult result = await service.InsertAsync(
      "typing preferred",
      InsertionMethod.SendInputUnicodeTyping,
      restoreClipboard: true);

    Xunit.Assert.True(result.Success);
    Xunit.Assert.Equal(InsertionOutcome.VerifiedInserted, result.Outcome);
    Xunit.Assert.Equal(InsertionMethod.SendInputUnicodeTyping, result.MethodUsed);
    Xunit.Assert.Equal(0, clipboard.CaptureSnapshotCallCount);
    Xunit.Assert.Equal(0, clipboard.RestoreSnapshotCallCount);
    Xunit.Assert.Equal(0, input.PasteCallCount);
    Xunit.Assert.Equal(1, input.TypingCallCount);
  }

  [Xunit.Fact]
  public async Task InsertAsync_RespectsCancellation()
  {
    FakeClipboardController clipboard = new();
    FakeInputDispatcher input = new();
    WindowsTextInsertionService service = CreateService(clipboard, input);

    using CancellationTokenSource cts = new();
    cts.Cancel();

    await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => service.InsertAsync("cancel", InsertionMethod.ClipboardPaste, restoreClipboard: true, cts.Token));
  }

  [Xunit.Fact]
  public async Task InsertAsync_WhenVerificationUnavailable_ReturnsDispatchedOutcome()
  {
    FakeClipboardController clipboard = new()
    {
      SnapshotToReturn = ClipboardSnapshot.Empty,
    };
    FakeInputDispatcher input = new()
    {
      PasteResult = InputDispatchResult.Ok,
      TypingResult = InputDispatchResult.Ok,
    };
    FakeWindowFocusProvider focusProvider = new()
    {
      FocusContext = new WindowFocusContext
      {
        ForegroundWindowHandle = 1,
        ForegroundProcessId = 100,
        ForegroundProcessName = "chrome",
        ForegroundWindowClassName = "Chrome_WidgetWin_1",
        FocusedControlClassName = "Chrome_WidgetWin_1",
        FocusedControlPasswordProtected = false,
        ForegroundWindowTitle = "Chrome",
        Editability = WindowEditability.Editable,
        EditabilityReason = "browser-editable-surface",
        BrowserSurfaceKind = BrowserSurfaceKind.EditablePage,
      },
    };

    WindowsTextInsertionService service = CreateService(clipboard, input, windowFocusProvider: focusProvider);

    InsertionResult result = await service.InsertAsync(
      "dispatch only",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.False(result.Success);
    Xunit.Assert.Equal(InsertionOutcome.Dispatched, result.Outcome);
    Xunit.Assert.Equal(InsertionMethod.ClipboardPaste, result.MethodUsed);
    Xunit.Assert.Contains("could not be verified", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Equal("dispatch only", clipboard.LastSetText);
    Xunit.Assert.Contains("recovery copy", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task InsertAsync_WhenIndirectSurfaceCannotBeVerified_ReturnsDispatchedWithoutTypingFallback()
  {
    FakeClipboardController clipboard = new()
    {
      SnapshotToReturn = ClipboardSnapshot.Empty,
    };
    FakeInputDispatcher input = new()
    {
      PasteResult = InputDispatchResult.Ok,
      TypingResult = InputDispatchResult.Ok,
    };
    FakeWindowFocusProvider focusProvider = new()
    {
      TrackObservedTextChanges = false,
      FocusContext = new WindowFocusContext
      {
        ForegroundWindowHandle = 1,
        ForegroundProcessId = 100,
        ForegroundProcessName = "Code",
        ForegroundWindowClassName = "Chrome_WidgetWin_1",
        FocusedControlClassName = "xterm-helper-textarea",
        FocusedControlPasswordProtected = false,
        ForegroundWindowTitle = "Terminal",
        Editability = WindowEditability.Editable,
        EditabilityReason = "uia-valuepattern",
        VerificationMode = "ValuePattern",
        FocusedAutomationId = "xterm-helper-textarea",
        FocusedAutomationName = "Terminal input",
        FocusedAutomationClassName = "xterm-helper-textarea",
        FocusedAutomationControlType = "ControlType.Edit",
        ObservedText = string.Empty,
      },
    };

    WindowsTextInsertionService service = CreateService(
      clipboard,
      input,
      windowFocusProvider: focusProvider,
      options: new TextInsertionOptions(
        EnableSecureFieldDetection: true,
        BlockedProcessNames: Array.Empty<string>(),
        EnableElevatedInsertion: false,
        ClipboardConsumptionWindow: TimeSpan.FromMilliseconds(20),
        BrowserClipboardConsumptionWindow: TimeSpan.FromMilliseconds(20),
        VerificationPollingInterval: TimeSpan.FromMilliseconds(20)));

    InsertionResult result = await service.InsertAsync(
      "dispatch only",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.False(result.Success);
    Xunit.Assert.Equal(InsertionOutcome.Dispatched, result.Outcome);
    Xunit.Assert.Equal(InsertionMethod.ClipboardPaste, result.MethodUsed);
    Xunit.Assert.Equal(1, input.PasteCallCount);
    Xunit.Assert.Equal(0, input.TypingCallCount);
    Xunit.Assert.Contains("indirect text surface", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task InsertAsync_WhenMenuFocusDrifts_RecoversEditableFocusBeforeDispatch()
  {
    FakeClipboardController clipboard = new()
    {
      SnapshotToReturn = ClipboardSnapshot.Empty,
    };
    FakeInputDispatcher input = new()
    {
      PasteResult = InputDispatchResult.Ok,
      TypingResult = InputDispatchResult.Ok,
    };
    FakeDiagnostics diagnostics = new();
    FakeWindowFocusProvider focusProvider = new()
    {
      FocusContext = new WindowFocusContext
      {
        ForegroundWindowHandle = 1,
        ForegroundProcessId = 100,
        ForegroundProcessName = "slack",
        ForegroundWindowClassName = "Chrome_WidgetWin_1",
        FocusedControlClassName = "MenuItemView",
        FocusedControlPasswordProtected = false,
        ForegroundWindowTitle = "Slack",
        Editability = WindowEditability.Unknown,
        RawFocusedAutomationClassName = "MenuItemView",
        RawFocusedAutomationControlType = "ControlType.MenuItem",
        RawFocusedAutomationName = "File",
        FocusedAutomationClassName = "MenuItemView",
        FocusedAutomationControlType = "ControlType.MenuItem",
        FocusedAutomationName = "File",
      },
      RecoveredFocusContext = new WindowFocusContext
      {
        ForegroundWindowHandle = 1,
        ForegroundProcessId = 100,
        ForegroundProcessName = "slack",
        ForegroundWindowClassName = "Chrome_WidgetWin_1",
        FocusedControlClassName = "ql-editor ql-blank",
        FocusedControlPasswordProtected = false,
        ForegroundWindowTitle = "Slack",
        Editability = WindowEditability.Editable,
        EditabilityReason = "uia-valuepattern",
        VerificationMode = "ValuePattern",
        ObservedText = string.Empty,
        RawFocusedAutomationClassName = "ql-editor ql-blank",
        RawFocusedAutomationControlType = "ControlType.Edit",
        RawFocusedAutomationName = "Message to general",
        FocusedAutomationClassName = "ql-editor ql-blank",
        FocusedAutomationControlType = "ControlType.Edit",
        FocusedAutomationName = "Message to general",
      },
    };

    WindowsTextInsertionService service = CreateService(
      clipboard,
      input,
      windowFocusProvider: focusProvider,
      diagnostics: diagnostics);

    InsertionResult result = await service.InsertAsync(
      "hello team",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.True(result.Success);
    Xunit.Assert.True(focusProvider.FocusRecoveryAttempted);
    Xunit.Assert.Equal(1, input.PasteCallCount);
    Xunit.Assert.Contains(
      diagnostics.WarningMessages,
      message => message.Contains("attempting editable focus recovery", StringComparison.OrdinalIgnoreCase));
    Xunit.Assert.Contains(
      diagnostics.InfoMessages,
      message => message.Contains("Editable focus recovery result", StringComparison.Ordinal));
  }

  [Xunit.Fact]
  public async Task InsertAsync_WhenEditableTargetWasPromotedFromRawFocus_RestoresFocusBeforeDispatch()
  {
    FakeClipboardController clipboard = new()
    {
      SnapshotToReturn = ClipboardSnapshot.Empty,
    };
    FakeInputDispatcher input = new()
    {
      PasteResult = InputDispatchResult.Ok,
      TypingResult = InputDispatchResult.Ok,
    };
    FakeDiagnostics diagnostics = new();
    FakeWindowFocusProvider focusProvider = new()
    {
      FocusContext = new WindowFocusContext
      {
        ForegroundWindowHandle = 1,
        ForegroundProcessId = 100,
        ForegroundProcessName = "chrome",
        ForegroundWindowClassName = "Chrome_WidgetWin_1",
        FocusedControlClassName = "ProseMirror",
        FocusedControlPasswordProtected = false,
        ForegroundWindowTitle = "ChatGPT - Google Chrome",
        Editability = WindowEditability.Editable,
        EditabilityReason = "uia-valuepattern",
        VerificationMode = "ValuePattern",
        BrowserSurfaceKind = BrowserSurfaceKind.EditablePage,
        FocusedAutomationId = "prompt-textarea",
        FocusedAutomationName = "Chat with ChatGPT",
        FocusedAutomationClassName = "ProseMirror",
        FocusedAutomationControlType = "ControlType.Edit",
        RawFocusedAutomationId = "view_1007",
        RawFocusedAutomationName = "Chrome",
        RawFocusedAutomationClassName = "BrowserAppMenuButton",
        RawFocusedAutomationControlType = "ControlType.Button",
        FocusResolutionSource = "ForegroundWindowSubtree",
        RawFocusedElementBounds = new ScreenBounds(3782, 69, 59, 51),
        FocusedElementBounds = new ScreenBounds(1541, 982, 1167, 76),
        ForegroundWindowBounds = new ScreenBounds(0, 0, 1600, 900),
        ObservedText = string.Empty,
      },
      RecoveredFocusContext = new WindowFocusContext
      {
        ForegroundWindowHandle = 1,
        ForegroundProcessId = 100,
        ForegroundProcessName = "chrome",
        ForegroundWindowClassName = "Chrome_WidgetWin_1",
        FocusedControlClassName = "ProseMirror ProseMirror-focused",
        FocusedControlPasswordProtected = false,
        ForegroundWindowTitle = "ChatGPT - Google Chrome",
        Editability = WindowEditability.Editable,
        EditabilityReason = "uia-valuepattern",
        VerificationMode = "ValuePattern",
        BrowserSurfaceKind = BrowserSurfaceKind.EditablePage,
        FocusedAutomationId = "prompt-textarea",
        FocusedAutomationName = "Chat with ChatGPT",
        FocusedAutomationClassName = "ProseMirror ProseMirror-focused",
        FocusedAutomationControlType = "ControlType.Edit",
        RawFocusedAutomationId = "prompt-textarea",
        RawFocusedAutomationName = "Chat with ChatGPT",
        RawFocusedAutomationClassName = "ProseMirror ProseMirror-focused",
        RawFocusedAutomationControlType = "ControlType.Edit",
        FocusResolutionSource = "RawFocusedElement",
        RawFocusedElementBounds = new ScreenBounds(1541, 982, 1167, 76),
        FocusedElementBounds = new ScreenBounds(1541, 982, 1167, 76),
        ForegroundWindowBounds = new ScreenBounds(0, 0, 1600, 900),
        ObservedText = string.Empty,
      },
    };

    WindowsTextInsertionService service = CreateService(
      clipboard,
      input,
      windowFocusProvider: focusProvider,
      diagnostics: diagnostics);

    InsertionResult result = await service.InsertAsync(
      "hello chatgpt",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.True(result.Success);
    Xunit.Assert.True(focusProvider.FocusRecoveryAttempted);
    Xunit.Assert.Equal(1, input.PasteCallCount);
    Xunit.Assert.Contains(
      diagnostics.WarningMessages,
      message => message.Contains("promoted away from the raw focused surface", StringComparison.OrdinalIgnoreCase));
    Xunit.Assert.Contains(
      diagnostics.InfoMessages,
      message => message.Contains("Editable focus recovery result", StringComparison.Ordinal));
  }

  [Xunit.Fact]
  public async Task InsertAsync_LogsRawAndResolvedBrowserTargets()
  {
    FakeClipboardController clipboard = new()
    {
      SnapshotToReturn = ClipboardSnapshot.Empty,
    };
    FakeInputDispatcher input = new()
    {
      PasteResult = InputDispatchResult.Ok,
      TypingResult = InputDispatchResult.Ok,
    };
    FakeDiagnostics diagnostics = new();
    FakeWindowFocusProvider focusProvider = new()
    {
      FocusContext = new WindowFocusContext
      {
        ForegroundWindowHandle = 1,
        ForegroundProcessId = 100,
        ForegroundProcessName = "chrome",
        ForegroundWindowClassName = "Chrome_WidgetWin_1",
        FocusedControlClassName = "Chrome_RenderWidgetHostHWND",
        FocusedControlPasswordProtected = false,
        ForegroundWindowTitle = "ChatGPT - Google Chrome",
        Editability = WindowEditability.Editable,
        EditabilityReason = "browser-editable-surface",
        VerificationMode = "TextPattern",
        BrowserSurfaceKind = BrowserSurfaceKind.EditablePage,
        FocusedAutomationId = "prompt-textarea",
        FocusedAutomationName = "Message ChatGPT",
        FocusedAutomationClassName = "Chrome_RenderWidgetHostHWND",
        FocusedAutomationControlType = "ControlType.Document",
        RawFocusedAutomationId = "browser-menu",
        RawFocusedAutomationName = "App menu",
        RawFocusedAutomationClassName = "BrowserAppMenuButton",
        RawFocusedAutomationControlType = "ControlType.Button",
        FocusResolutionSource = "ForegroundWindowSubtree",
        RawFocusedElementBounds = new ScreenBounds(16, 16, 20, 20),
        FocusedElementBounds = new ScreenBounds(120, 640, 700, 100),
        ForegroundWindowBounds = new ScreenBounds(0, 0, 1600, 900),
        ObservedText = string.Empty,
      },
    };

    WindowsTextInsertionService service = CreateService(
      clipboard,
      input,
      windowFocusProvider: focusProvider,
      diagnostics: diagnostics);

    InsertionResult result = await service.InsertAsync(
      "hello",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.True(result.Success);
    Xunit.Assert.Contains(
      diagnostics.InfoMessages,
      message => message.Contains("focusResolution='ForegroundWindowSubtree'", StringComparison.Ordinal)
                 && message.Contains("rawAutomationClass='BrowserAppMenuButton'", StringComparison.Ordinal)
                 && message.Contains("resolvedAutomationClass='Chrome_RenderWidgetHostHWND'", StringComparison.Ordinal));
  }

  [Xunit.Fact]
  public async Task InsertAsync_BlockedByPrivilegeBoundary_WithElevatedInsertionEnabled_RoutesToHelper()
  {
    FakeClipboardController clipboard = new();
    FakeInputDispatcher input = new()
    {
      PasteResult = InputDispatchResult.Ok,
      TypingResult = InputDispatchResult.Ok,
    };
    FakePrivilegeBoundaryDetector boundaryDetector = new()
    {
      Result = new PrivilegeBoundaryCheckResult(
        false,
        "Insertion into elevated applications is blocked by Windows privilege boundaries (UIPI)."),
    };
    FakeElevatedInsertionBridge elevatedBridge = new()
    {
      Result = new InsertionResult(true, InsertionMethod.SendInputUnicodeTyping, null),
    };

    WindowsTextInsertionService service = CreateService(
      clipboard,
      input,
      boundaryDetector: boundaryDetector,
      options: new TextInsertionOptions(
        EnableSecureFieldDetection: true,
        BlockedProcessNames: Array.Empty<string>(),
        EnableElevatedInsertion: true),
      elevatedInsertionBridge: elevatedBridge);

    InsertionResult result = await service.InsertAsync(
      "elevated path",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.True(result.Success);
    Xunit.Assert.Equal(InsertionOutcome.VerifiedInserted, result.Outcome);
    Xunit.Assert.Equal(1, elevatedBridge.CallCount);
    Xunit.Assert.Equal(0, input.PasteCallCount);
    Xunit.Assert.Equal(0, input.TypingCallCount);
  }

  [Xunit.Fact]
  public async Task InsertAsync_BlockedByPrivilegeBoundary_WithElevatedInsertionEnabled_AllowsDispatchedHelperResult()
  {
    FakeClipboardController clipboard = new();
    FakeInputDispatcher input = new();
    FakePrivilegeBoundaryDetector boundaryDetector = new()
    {
      Result = new PrivilegeBoundaryCheckResult(
        false,
        "Insertion into elevated applications is blocked by Windows privilege boundaries (UIPI)."),
    };
    FakeElevatedInsertionBridge elevatedBridge = new()
    {
      Result = InsertionResult.Dispatched(
        InsertionMethod.ClipboardPaste,
        "Clipboard paste was dispatched to an indirect text surface, but UI Automation could not verify the visible text."),
    };

    WindowsTextInsertionService service = CreateService(
      clipboard,
      input,
      boundaryDetector: boundaryDetector,
      options: new TextInsertionOptions(
        EnableSecureFieldDetection: true,
        BlockedProcessNames: Array.Empty<string>(),
        EnableElevatedInsertion: true),
      elevatedInsertionBridge: elevatedBridge);

    InsertionResult result = await service.InsertAsync(
      "elevated path",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.False(result.Success);
    Xunit.Assert.Equal(InsertionOutcome.Dispatched, result.Outcome);
    Xunit.Assert.Equal(1, elevatedBridge.CallCount);
    Xunit.Assert.Equal(0, input.PasteCallCount);
    Xunit.Assert.Equal(0, input.TypingCallCount);
  }

  [Xunit.Fact]
  public async Task InsertAsync_BlockedByPrivilegeBoundary_WithElevatedInsertionEnabled_ReturnsCombinedErrorWhenHelperFails()
  {
    FakeClipboardController clipboard = new();
    FakeInputDispatcher input = new();
    FakePrivilegeBoundaryDetector boundaryDetector = new()
    {
      Result = new PrivilegeBoundaryCheckResult(
        false,
        "Insertion into elevated applications is blocked by Windows privilege boundaries (UIPI)."),
    };
    FakeElevatedInsertionBridge elevatedBridge = new()
    {
      Result = new InsertionResult(false, InsertionMethod.ClipboardPaste, "helper unavailable"),
    };

    WindowsTextInsertionService service = CreateService(
      clipboard,
      input,
      boundaryDetector: boundaryDetector,
      options: new TextInsertionOptions(
        EnableSecureFieldDetection: true,
        BlockedProcessNames: Array.Empty<string>(),
        EnableElevatedInsertion: true),
      elevatedInsertionBridge: elevatedBridge);

    InsertionResult result = await service.InsertAsync(
      "elevated failure",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.False(result.Success);
    Xunit.Assert.Equal(InsertionOutcome.UnknownOutcome, result.Outcome);
    Xunit.Assert.Contains("UIPI", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Contains("helper unavailable", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Equal(1, elevatedBridge.CallCount);
  }

  [Xunit.Fact]
  public async Task UndoLastInsertionAsync_WithoutOwnedEditTransaction_NeverSendsGenericUndo()
  {
    FakeClipboardController clipboard = new()
    {
      SnapshotToReturn = ClipboardSnapshot.Empty,
    };
    FakeInputDispatcher input = new()
    {
      PasteResult = InputDispatchResult.Ok,
      TypingResult = InputDispatchResult.Ok,
      UndoResult = InputDispatchResult.Ok,
    };
    FakeWindowFocusProvider focusProvider = new()
    {
      FocusContext = new WindowFocusContext
      {
        ForegroundWindowHandle = 77,
        ForegroundProcessId = 321,
        ForegroundProcessName = "notepad",
        ForegroundWindowClassName = "Notepad",
        FocusedControlClassName = "Edit",
        FocusedControlPasswordProtected = false,
        ForegroundWindowTitle = "Untitled - Notepad",
        Editability = WindowEditability.Editable,
        EditabilityReason = "uia-valuepattern",
        VerificationMode = "ValuePattern",
        ObservedText = string.Empty,
      },
    };

    WindowsTextInsertionService service = CreateService(
      clipboard,
      input,
      windowFocusProvider: focusProvider);

    InsertionResult insertionResult = await service.InsertAsync(
      "insert",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.True(insertionResult.Success);

    UndoInsertionResult undoResult = await service.UndoLastInsertionAsync();
    Xunit.Assert.False(undoResult.Success);
    Xunit.Assert.Equal(0, input.UndoCallCount);
  }

  [Xunit.Fact]
  public async Task UndoLastInsertionAsync_WhenForegroundAppChanged_ReturnsFailure()
  {
    FakeClipboardController clipboard = new()
    {
      SnapshotToReturn = ClipboardSnapshot.Empty,
    };
    FakeInputDispatcher input = new()
    {
      PasteResult = InputDispatchResult.Ok,
      TypingResult = InputDispatchResult.Ok,
      UndoResult = InputDispatchResult.Ok,
    };
    FakeWindowFocusProvider focusProvider = new()
    {
      FocusContext = new WindowFocusContext
      {
        ForegroundWindowHandle = 88,
        ForegroundProcessId = 444,
        ForegroundProcessName = "notepad",
        ForegroundWindowClassName = "Notepad",
        FocusedControlClassName = "Edit",
        FocusedControlPasswordProtected = false,
        ForegroundWindowTitle = "Untitled - Notepad",
        Editability = WindowEditability.Editable,
        EditabilityReason = "uia-valuepattern",
        VerificationMode = "ValuePattern",
        ObservedText = string.Empty,
      },
    };

    WindowsTextInsertionService service = CreateService(
      clipboard,
      input,
      windowFocusProvider: focusProvider);

    InsertionResult insertionResult = await service.InsertAsync(
      "insert",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.True(insertionResult.Success);

    focusProvider.FocusContext = focusProvider.FocusContext with
    {
      ForegroundProcessId = 999,
      ForegroundProcessName = "chrome",
    };

    UndoInsertionResult undoResult = await service.UndoLastInsertionAsync();
    Xunit.Assert.False(undoResult.Success);
    Xunit.Assert.Contains("ownership", undoResult.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Equal(0, input.UndoCallCount);
  }

  private static WindowsTextInsertionService CreateService(
    FakeClipboardController clipboard,
    FakeInputDispatcher input,
    FakeWindowFocusProvider? windowFocusProvider = null,
    FakePrivilegeBoundaryDetector? boundaryDetector = null,
    TextInsertionOptions? options = null,
    FakeElevatedInsertionBridge? elevatedInsertionBridge = null,
    FakeDiagnostics? diagnostics = null)
  {
    FakeWindowFocusProvider resolvedFocusProvider = windowFocusProvider ?? new FakeWindowFocusProvider();
    input.OnPaste = () => resolvedFocusProvider.ApplyObservedText(clipboard.LastSetText ?? string.Empty);
    input.OnTyping = text => resolvedFocusProvider.ApplyObservedText(text);

    return new WindowsTextInsertionService(
      clipboard,
      input,
      resolvedFocusProvider,
      boundaryDetector ?? new FakePrivilegeBoundaryDetector(),
      options,
      elevatedInsertionBridge ?? new FakeElevatedInsertionBridge(),
      diagnostics);
  }

  private sealed class FakeClipboardController : IClipboardController
  {
    public uint Sequence { get; set; }
    public uint GetSequenceNumber() => Sequence;
    public ClipboardSnapshot SnapshotToReturn { get; set; } = ClipboardSnapshot.Empty;
    public Exception? CaptureSnapshotException { get; set; }
    public Exception? SetTextException { get; set; }
    public Exception? RestoreSnapshotException { get; set; }
    public int CaptureSnapshotCallCount { get; private set; }
    public int RestoreSnapshotCallCount { get; private set; }
    public string? LastSetText { get; private set; }
    public Action? OnSet { get; set; }

    public ClipboardSnapshot CaptureSnapshot()
    {
      CaptureSnapshotCallCount++;
      if (CaptureSnapshotException is not null)
      {
        throw CaptureSnapshotException;
      }

      return SnapshotToReturn with { SequenceNumber = Sequence };
    }

    public uint SetUnicodeText(string text, uint? expectedSequence = null)
    {
      if (expectedSequence.HasValue && expectedSequence != Sequence) throw new ClipboardOperationException("Clipboard changed.");
      LastSetText = text;
      OnSet?.Invoke();
      if (SetTextException is not null)
      {
        throw SetTextException;
      }
      return ++Sequence;
    }

    public uint? RestoreSnapshot(ClipboardSnapshot snapshot, uint expectedSequence)
    {
      if (Sequence != expectedSequence) return null;
      RestoreSnapshotCallCount++;
      if (RestoreSnapshotException is not null)
      {
        throw RestoreSnapshotException;
      }
      return ++Sequence;
    }
  }

  private sealed class FakeInputDispatcher : IInputDispatcher
  {
    public InputDispatchResult PasteResult { get; set; } = InputDispatchResult.Ok;
    public InputDispatchResult TypingResult { get; set; } = InputDispatchResult.Ok;
    public InputDispatchResult UndoResult { get; set; } = InputDispatchResult.Ok;
    public InputDispatchResult CopyResult { get; set; } = InputDispatchResult.Ok;
    public Action? OnPaste { get; set; }
    public Action<string>? OnTyping { get; set; }
    public int PasteCallCount { get; private set; }
    public int TypingCallCount { get; private set; }
    public int UndoCallCount { get; private set; }
    public int CopyCallCount { get; private set; }

    public InputDispatchResult SendCopyShortcut()
    {
      CopyCallCount++;
      return CopyResult;
    }

    public InputDispatchResult SendPasteShortcut()
    {
      PasteCallCount++;
      if (PasteResult.Success)
      {
        OnPaste?.Invoke();
      }

      return PasteResult;
    }

    public InputDispatchResult SendUnicodeText(string text)
    {
      TypingCallCount++;
      if (TypingResult.Success)
      {
        OnTyping?.Invoke(text);
      }

      return TypingResult;
    }

    public InputDispatchResult SendUndoShortcut()
    {
      UndoCallCount++;
      return UndoResult;
    }
  }

  private sealed class FakeWindowFocusProvider : IWindowFocusProvider, IEditableFocusRestorer, IWindowFocusRestorer
  {
    public nint ForegroundWindowHandle { get; set; } = 1;
    public WindowFocusContext? RecoveredFocusContext { get; set; }
    public bool FocusRecoveryAttempted { get; private set; }
    public int ForegroundRestoreCallCount { get; private set; }
    public bool ForegroundRestoreSucceeds { get; set; } = true;
    public int ForegroundRestoreFailuresRemaining { get; set; }
    public WindowFocusContext? LastForegroundRestoreTarget { get; private set; }
    public bool TrackObservedTextChanges { get; set; } = true;
    public WindowFocusContext FocusContext { get; set; } = new()
    {
      ForegroundWindowHandle = 1,
      ForegroundProcessId = 100,
      ForegroundProcessName = "notepad",
      ForegroundWindowClassName = "Notepad",
      FocusedControlClassName = "Edit",
      FocusedControlPasswordProtected = false,
      ForegroundWindowTitle = "Untitled - Notepad",
      Editability = WindowEditability.Editable,
      EditabilityReason = "uia-valuepattern",
      VerificationMode = "ValuePattern",
      ObservedText = string.Empty,
    };

    public nint GetForegroundWindowHandle()
    {
      return ForegroundWindowHandle;
    }

    public WindowFocusContext GetWindowFocusContext()
    {
      return FocusContext with
      {
        ForegroundWindowHandle = ForegroundWindowHandle,
        ForegroundProcessStartTicks = FocusContext.ForegroundProcessStartTicks ?? 1,
        FocusedAutomationRuntimeId = FocusContext.FocusedAutomationRuntimeId ?? "fake-editor",
      };
    }

    public bool TryRestoreEditableFocus(WindowFocusContext currentContext)
    {
      FocusRecoveryAttempted = true;
      if (RecoveredFocusContext is null)
      {
        return false;
      }

      FocusContext = RecoveredFocusContext with
      {
        ForegroundWindowHandle = ForegroundWindowHandle,
      };
      return true;
    }

    public bool TryRestoreForegroundWindow(WindowFocusContext targetContext)
    {
      ForegroundRestoreCallCount++;
      LastForegroundRestoreTarget = targetContext;
      if (!ForegroundRestoreSucceeds || ForegroundRestoreFailuresRemaining > 0)
      {
        if (ForegroundRestoreFailuresRemaining > 0)
        {
          ForegroundRestoreFailuresRemaining--;
        }

        return false;
      }

      FocusContext = targetContext;
      ForegroundWindowHandle = targetContext.ForegroundWindowHandle;
      return true;
    }

    public void ApplyObservedText(string text)
    {
      if (!TrackObservedTextChanges || !FocusContext.CanVerifyText)
      {
        return;
      }

      FocusContext = FocusContext with
      {
        ObservedText = (FocusContext.ObservedText ?? string.Empty) + text,
      };
    }
  }

  private sealed class FakePrivilegeBoundaryDetector : IPrivilegeBoundaryDetector
  {
    public PrivilegeBoundaryCheckResult Result { get; set; } = PrivilegeBoundaryCheckResult.AllowedResult;

    public PrivilegeBoundaryCheckResult Evaluate(nint targetWindowHandle)
    {
      return Result;
    }
  }

  private sealed class FakeElevatedInsertionBridge : IElevatedInsertionBridge
  {
    public Task<InsertionResult> InsertAsync(string text, InsertionMethod method, bool restore,
      InsertionTargetIdentity target, CancellationToken cancellationToken = default) =>
      InsertAsync(text, method, restore, cancellationToken);
    public InsertionResult Result { get; set; } = new(false, InsertionMethod.ClipboardPaste, "not configured");

    public int CallCount { get; private set; }

    public Task<InsertionResult> InsertAsync(
      string text,
      InsertionMethod preferredMethod,
      bool restoreClipboard,
      CancellationToken cancellationToken = default)
    {
      CallCount++;
      return Task.FromResult(Result);
    }
  }

  private sealed class FakeDiagnostics : IDiagnostics
  {
    public string[] EmptyErrorMessages => Array.Empty<string>();
    public System.Collections.Generic.List<string> InfoMessages { get; } = new();
    public System.Collections.Generic.List<string> WarningMessages { get; } = new();

    public void Info(string message)
    {
      InfoMessages.Add(message);
    }

    public void Warning(string message)
    {
      WarningMessages.Add(message);
    }

    public void Error(string message, Exception? exception = null)
    {
    }
  }
}

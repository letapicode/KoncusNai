using System;
using System.Collections.Generic;
using DictateAnywhere.Core.Contracts;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Insertion;

public sealed class WindowsTextInsertionService : ITextInsertionService, IUndoInsertionService, ITextInsertionTargetSession
{
  private static readonly TimeSpan FocusRecoverySettleDelay = TimeSpan.FromMilliseconds(75);
  private static readonly TimeSpan TargetRestoreRetryDelay = TimeSpan.FromMilliseconds(250);
  private const int TargetRestoreAttempts = 3;
  private static readonly string[] SecureClassNameKeywords = ["password", "credential", "secure"];
  private static readonly string[] IndirectTextSurfaceClassTokens = ["prosemirror", "ql-editor", "xterm-helper-textarea"];
  private static readonly string[] IndirectTextSurfaceNameTokens = ["message chatgpt", "chat with chatgpt", "message to", "terminal "];
  private static readonly HashSet<string> KnownSecureProcesses = new(StringComparer.OrdinalIgnoreCase)
  {
    "CredentialUIBroker",
    "LogonUI",
    "consent",
  };

  private readonly IClipboardController clipboardController;
  private readonly IInputDispatcher inputDispatcher;
  private readonly IWindowFocusProvider windowFocusProvider;
  private readonly IEditableFocusRestorer? editableFocusRestorer;
  private readonly IWindowFocusRestorer? windowFocusRestorer;
  private readonly IPrivilegeBoundaryDetector privilegeBoundaryDetector;
  private readonly IElevatedInsertionBridge elevatedInsertionBridge;
  private readonly IDiagnostics? diagnostics;
  private readonly TextInsertionOptions options;
  private readonly HashSet<string> blockedProcessNames;
  private readonly object capturedTargetSync = new();

  private WindowFocusContext? capturedTarget;

  public WindowsTextInsertionService()
    : this(TextInsertionOptions.Default)
  {
  }

  public WindowsTextInsertionService(TextInsertionOptions options)
    : this(options, diagnostics: null)
  {
  }

  public WindowsTextInsertionService(TextInsertionOptions options, IDiagnostics? diagnostics)
    : this(
      new WindowsClipboardController(),
      new WindowsInputDispatcher(),
      new WindowsWindowFocusProvider(),
      new WindowsPrivilegeBoundaryDetector(),
      options,
      new UiAccessHelperProcessBridge(UiAccessHelperProcessBridgeOptions.Default, diagnostics),
      diagnostics)
  {
  }

  public WindowsTextInsertionService(
    IClipboardController clipboardController,
    IInputDispatcher inputDispatcher,
    IWindowFocusProvider windowFocusProvider,
    IPrivilegeBoundaryDetector privilegeBoundaryDetector,
    TextInsertionOptions? options = null,
    IElevatedInsertionBridge? elevatedInsertionBridge = null,
    IDiagnostics? diagnostics = null)
  {
    this.clipboardController = clipboardController ?? throw new ArgumentNullException(nameof(clipboardController));
    this.inputDispatcher = inputDispatcher ?? throw new ArgumentNullException(nameof(inputDispatcher));
    this.windowFocusProvider = windowFocusProvider ?? throw new ArgumentNullException(nameof(windowFocusProvider));
    editableFocusRestorer = windowFocusProvider as IEditableFocusRestorer;
    windowFocusRestorer = windowFocusProvider as IWindowFocusRestorer;
    this.privilegeBoundaryDetector = privilegeBoundaryDetector ?? throw new ArgumentNullException(nameof(privilegeBoundaryDetector));
    this.options = options ?? TextInsertionOptions.Default;
    this.elevatedInsertionBridge = elevatedInsertionBridge
      ?? new UiAccessHelperProcessBridge(UiAccessHelperProcessBridgeOptions.Default, diagnostics);
    this.diagnostics = diagnostics;

    blockedProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    IReadOnlyList<string> blockedList = this.options.BlockedProcessNames ?? Array.Empty<string>();
    foreach (string processName in blockedList)
    {
      string? normalized = NormalizeProcessName(processName);
      if (!string.IsNullOrWhiteSpace(normalized))
      {
        blockedProcessNames.Add(normalized);
      }
    }
  }

  public async Task<InsertionResult> InsertAsync(
    string text,
    InsertionMethod preferredMethod,
    bool restoreClipboard,
    CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    ArgumentNullException.ThrowIfNull(text);

    if (text.Length == 0)
    {
      diagnostics?.Warning("Insertion skipped because no insertable text remained after transcription/transformation.");
      return InsertionResult.Verified(preferredMethod);
    }

    ClipboardOwnership ownership = new(clipboardController.GetSequenceNumber());
    WindowFocusContext focusContext = GetInitialFocusContext(out bool usingCapturedTarget);
    InsertionTargetIdentity originalTarget = InsertionTargetIdentity.FromContext(focusContext);
    if (usingCapturedTarget
        && !await TryRestoreCapturedTargetAsync(focusContext, cancellationToken).ConfigureAwait(false))
    {
      InsertionResult restoreFailure = ClassifyCapturedTargetRestoreFailure(focusContext, preferredMethod);
      return restoreFailure.CanRecoverTranscript
        ? PreserveRecoveryCopyIfNeeded(text, restoreFailure, ownership)
        : restoreFailure;
    }

    if (usingCapturedTarget) focusContext = windowFocusProvider.GetWindowFocusContext();
    focusContext = await TryRecoverEditableFocusAsync(focusContext, cancellationToken).ConfigureAwait(false);
    if (usingCapturedTarget && !originalTarget.Matches(focusContext))
    {
      return TargetChanged(preferredMethod);
    }
    diagnostics?.Info(
      $"Insertion request: textLength={text.Length}, preferredMethod={preferredMethod}, restoreClipboard={restoreClipboard}, target={WindowFocusContextLogFormatter.Describe(focusContext)}.");

    InsertionResult? preflightResult = await PrepareTargetAsync(
      text,
      focusContext,
      preferredMethod,
      restoreClipboard,
      cancellationToken).ConfigureAwait(false);
    if (preflightResult is not null)
    {
      return PreserveRecoveryCopyIfNeeded(text, FinalizeInsertionAttempt(focusContext, preflightResult, elevatedRoute: true), ownership);
    }

    InsertionResult result = preferredMethod switch
    {
      InsertionMethod.ClipboardPaste => await InsertWithClipboardFallbackAsync(
        text,
        focusContext,
        restoreClipboard,
        ownership,
        cancellationToken).ConfigureAwait(false),
      InsertionMethod.SendInputUnicodeTyping => await InsertUsingUnicodeTypingAsync(
        text,
        focusContext,
        cancellationToken).ConfigureAwait(false),
      _ => InsertionResult.Unknown(preferredMethod, "Unsupported insertion method."),
    };

    return PreserveRecoveryCopyIfNeeded(text, FinalizeInsertionAttempt(focusContext, result, elevatedRoute: false), ownership);
  }

  private InsertionResult PreserveRecoveryCopyIfNeeded(string text, InsertionResult result, ClipboardOwnership ownership)
  {
    if (!ownership.AllowRecoveryCopy) return result;
    // Browser editors such as Google Translate can accept Ctrl+V yet expose no
    // observable editable value to UI Automation. Preserve the dictation when
    // a dispatch cannot be verified, and also when both insertion routes fail.
    if (!result.CanRecoverTranscript && result.Outcome != InsertionOutcome.Dispatched)
    {
      return result;
    }

    try
    {
      ownership.Sequence = clipboardController.SetUnicodeText(text, ownership.Sequence);
      diagnostics?.Info("Insertion was not fully verified; a recovery copy was kept on the clipboard.");
      return result with
      {
        RecoveryCopyAvailable = true,
        ErrorMessage = AppendMessage(result.ErrorMessage, "A recovery copy of this dictation is on the clipboard."),
      };
    }
    catch (ClipboardOperationException ex)
    {
      diagnostics?.Warning($"Could not preserve the failed insertion on the clipboard: {ex.Message}");
      return result with
      {
        ErrorMessage = AppendMessage(result.ErrorMessage, $"A recovery copy could not be placed on the clipboard: {ex.Message}"),
      };
    }
  }

  public void CaptureCurrentTarget()
  {
    WindowFocusContext focusContext = windowFocusProvider.GetWindowFocusContext();
    lock (capturedTargetSync)
    {
      capturedTarget = focusContext;
    }

    diagnostics?.Info($"Captured insertion target: target={WindowFocusContextLogFormatter.Describe(focusContext)}.");
  }

  public void ClearCapturedTarget()
  {
    lock (capturedTargetSync)
    {
      capturedTarget = null;
    }
  }

  public bool TryCaptureTarget(InsertionTargetIdentity expectedTarget)
  {
    ArgumentNullException.ThrowIfNull(expectedTarget);
    WindowFocusContext current = windowFocusProvider.GetWindowFocusContext();
    lock (capturedTargetSync)
    {
      capturedTarget = expectedTarget.Matches(current) ? current : null;
      return capturedTarget is not null;
    }
  }

  public Task<UndoInsertionResult> UndoLastInsertionAsync(CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    // SendInput and UI Automation expose no edit transaction or undo-stack identity.
    // Even identical text can follow an intervening edit, navigation, or manual undo.
    return Task.FromResult(new UndoInsertionResult(false,
      "Automatic undo is unavailable because the target app cannot prove ownership of the last edit. Use Undo in the target app after checking its current text."));
  }
  private async Task<InsertionResult?> PrepareTargetAsync(
    string text,
    WindowFocusContext focusContext,
    InsertionMethod preferredMethod,
    bool restoreClipboard,
    CancellationToken cancellationToken)
  {
    if (focusContext.ForegroundWindowHandle == 0)
    {
      return InsertionResult.Blocked(
        preferredMethod,
        "No active target window is available for insertion.",
        InsertionBlockReason.NoActiveTarget);
    }

    if (IsTargetProcessBlacklisted(focusContext, out string? blacklistedProcessName))
    {
      return InsertionResult.Blocked(
        preferredMethod,
        $"Insertion is blocked for '{blacklistedProcessName}' because it is in your insertion blacklist.",
        InsertionBlockReason.BlockedApplication);
    }

    if (options.EnableSecureFieldDetection && IsLikelySecureField(focusContext, out string secureFieldReason))
    {
      return InsertionResult.Blocked(
        preferredMethod,
        $"Insertion blocked in a likely secure field ({secureFieldReason}).",
        InsertionBlockReason.SecureField);
    }

    if (focusContext.IsNonEditable)
    {
      string nonEditableReason = string.IsNullOrWhiteSpace(focusContext.EditabilityReason)
        ? "target surface was classified as non-editable"
        : focusContext.EditabilityReason;
      return InsertionResult.Blocked(
        preferredMethod,
        $"Insertion blocked because the focused target is not editable ({nonEditableReason}).",
        InsertionBlockReason.NonEditableTarget);
    }

    PrivilegeBoundaryCheckResult boundaryCheck = privilegeBoundaryDetector.Evaluate(focusContext.ForegroundWindowHandle);
    if (!boundaryCheck.Allowed)
    {
      if (options.EnableElevatedInsertion)
      {
        InsertionResult helperResult = await elevatedInsertionBridge
          .InsertAsync(text, preferredMethod, restoreClipboard, InsertionTargetIdentity.FromContext(focusContext), cancellationToken)
          .ConfigureAwait(false);

        if (helperResult.Outcome is InsertionOutcome.VerifiedInserted or InsertionOutcome.Dispatched)
        {
          return helperResult;
        }

        string boundaryMessage = boundaryCheck.ErrorMessage ?? "Insertion blocked by Windows privilege boundaries.";
        string helperMessage = helperResult.ErrorMessage ?? "UIAccess helper attempt failed.";
        return helperResult with
        {
          ErrorMessage = $"{boundaryMessage} Elevated insertion attempt returned {helperResult.Outcome}: {helperMessage}",
        };
      }

      return InsertionResult.Blocked(
        preferredMethod,
        boundaryCheck.ErrorMessage ?? "Insertion blocked by Windows privilege boundaries.",
        InsertionBlockReason.PrivilegeBoundary);
    }

    return null;
  }

  private async Task<WindowFocusContext> TryRecoverEditableFocusAsync(
    WindowFocusContext focusContext,
    CancellationToken cancellationToken)
  {
    if (editableFocusRestorer is null
        || focusContext.ForegroundWindowHandle == 0
        || !ShouldAttemptEditableFocusRecovery(focusContext))
    {
      return focusContext;
    }

    string recoveryReason = focusContext.IsEditable
      ? "Insertion target was promoted away from the raw focused surface; attempting explicit editable focus recovery."
      : "Insertion focus drift detected before dispatch; attempting editable focus recovery.";
    diagnostics?.Warning(
      $"{recoveryReason} target={WindowFocusContextLogFormatter.Describe(focusContext)}");

    if (!editableFocusRestorer.TryRestoreEditableFocus(focusContext))
    {
      diagnostics?.Warning("Editable focus recovery did not find a restorable target before insertion dispatch.");
      return focusContext;
    }

    await Task.Delay(FocusRecoverySettleDelay, cancellationToken).ConfigureAwait(false);
    WindowFocusContext recoveredContext = windowFocusProvider.GetWindowFocusContext();
    diagnostics?.Info(
      $"Editable focus recovery result: target={WindowFocusContextLogFormatter.Describe(recoveredContext)}.");
    return recoveredContext;
  }

  private InsertionResult ClassifyCapturedTargetRestoreFailure(
    WindowFocusContext capturedFocusContext,
    InsertionMethod preferredMethod)
  {
    if (IsTargetProcessBlacklisted(capturedFocusContext, out string? blockedProcessName))
    {
      return InsertionResult.Blocked(
        preferredMethod,
        $"The original target could not be restored and '{blockedProcessName}' is in the insertion blacklist.",
        InsertionBlockReason.BlockedApplication);
    }

    if (options.EnableSecureFieldDetection
        && IsLikelySecureField(capturedFocusContext, out string secureFieldReason))
    {
      return InsertionResult.Blocked(
        preferredMethod,
        $"The original target could not be restored and was classified as a likely secure field ({secureFieldReason}).",
        InsertionBlockReason.SecureField);
    }

    if (capturedFocusContext.IsNonEditable)
    {
      return InsertionResult.Blocked(
        preferredMethod,
        "The original target could not be restored and was classified as non-editable.",
        InsertionBlockReason.NonEditableTarget);
    }

    return InsertionResult.Blocked(
      preferredMethod,
      "Insertion target changed while transcription was running and the original target could not be restored.",
      InsertionBlockReason.TargetChanged);
  }

  private WindowFocusContext GetInitialFocusContext(out bool usingCapturedTarget)
  {
    lock (capturedTargetSync)
    {
      if (capturedTarget is not null && capturedTarget.ForegroundWindowHandle != 0)
      {
        usingCapturedTarget = true;
        diagnostics?.Info($"Using captured insertion target: target={WindowFocusContextLogFormatter.Describe(capturedTarget)}.");
        return capturedTarget;
      }
    }

    usingCapturedTarget = false;
    return windowFocusProvider.GetWindowFocusContext();
  }

  private async Task<bool> TryRestoreCapturedTargetAsync(
    WindowFocusContext capturedFocusContext,
    CancellationToken cancellationToken)
  {
    WindowFocusContext currentContext = windowFocusProvider.GetWindowFocusContext();
    if (MatchesForegroundTarget(capturedFocusContext, currentContext))
    {
      return InsertionTargetIdentity.FromContext(capturedFocusContext).Matches(currentContext);
    }

    if (windowFocusRestorer is null)
    {
      diagnostics?.Warning("Captured insertion target could not be restored because the focus provider does not support target restoration.");
      return false;
    }

    diagnostics?.Warning(
      $"Insertion target drift detected after transcription; restoring captured target. captured={WindowFocusContextLogFormatter.Describe(capturedFocusContext)} current={WindowFocusContextLogFormatter.Describe(currentContext)}.");

    for (int attempt = 1; attempt <= TargetRestoreAttempts; attempt++)
    {
      if (attempt > 1)
      {
        await Task.Delay(TargetRestoreRetryDelay, cancellationToken).ConfigureAwait(false);
      }

      if (!windowFocusRestorer.TryRestoreForegroundWindow(capturedFocusContext))
      {
        diagnostics?.Warning(
          $"Captured insertion target restore request was rejected by Windows (attempt {attempt}/{TargetRestoreAttempts}).");
        continue;
      }

      await Task.Delay(FocusRecoverySettleDelay, cancellationToken).ConfigureAwait(false);
      WindowFocusContext restoredContext = windowFocusProvider.GetWindowFocusContext();
      if (!InsertionTargetIdentity.FromContext(capturedFocusContext).Matches(restoredContext))
      {
        diagnostics?.Warning(
          $"Captured insertion target restore did not land on the original target (attempt {attempt}/{TargetRestoreAttempts}). restored={WindowFocusContextLogFormatter.Describe(restoredContext)}.");
        continue;
      }

      lock (capturedTargetSync)
      {
        capturedTarget = restoredContext;
      }

      diagnostics?.Info(
        $"Captured insertion target restored on attempt {attempt}: target={WindowFocusContextLogFormatter.Describe(restoredContext)}.");
      return true;
    }

    return false;
  }

  private static bool MatchesForegroundTarget(
    WindowFocusContext expectedContext,
    WindowFocusContext actualContext)
  {
    if (expectedContext.ForegroundWindowHandle != 0
        && actualContext.ForegroundWindowHandle != 0)
    {
      return expectedContext.ForegroundWindowHandle == actualContext.ForegroundWindowHandle
        && expectedContext.ForegroundProcessId == actualContext.ForegroundProcessId
        && expectedContext.ForegroundProcessStartTicks == actualContext.ForegroundProcessStartTicks;
    }

    return expectedContext.ForegroundProcessId != 0
           && actualContext.ForegroundProcessId != 0
           && expectedContext.ForegroundProcessId == actualContext.ForegroundProcessId
           && string.Equals(
             expectedContext.ForegroundProcessName,
             actualContext.ForegroundProcessName,
             StringComparison.OrdinalIgnoreCase);
  }

  private static bool ShouldAttemptEditableFocusRecovery(WindowFocusContext focusContext)
  {
    if (!focusContext.IsEditable)
    {
      return true;
    }

    if (string.Equals(focusContext.FocusResolutionSource, "RawFocusedElement", StringComparison.Ordinal))
    {
      return false;
    }

    return ResolvedTargetDiffersFromRawFocus(focusContext);
  }

  private static bool ResolvedTargetDiffersFromRawFocus(WindowFocusContext focusContext)
  {
    return !string.Equals(focusContext.RawFocusedAutomationId, focusContext.FocusedAutomationId, StringComparison.Ordinal)
           || !string.Equals(focusContext.RawFocusedAutomationName, focusContext.FocusedAutomationName, StringComparison.Ordinal)
           || !string.Equals(focusContext.RawFocusedAutomationClassName, focusContext.FocusedAutomationClassName, StringComparison.Ordinal)
           || !string.Equals(focusContext.RawFocusedAutomationControlType, focusContext.FocusedAutomationControlType, StringComparison.Ordinal)
           || !Nullable.Equals(focusContext.RawFocusedElementBounds, focusContext.FocusedElementBounds);
  }

  private InsertionResult FinalizeInsertionAttempt(
    WindowFocusContext focusContext,
    InsertionResult result,
    bool elevatedRoute)
  {
    if (result.Success)
    {
      diagnostics?.Info(
        $"{(elevatedRoute ? "Elevated insertion" : "Insertion")} verified using {result.MethodUsed} for {WindowFocusContextLogFormatter.Describe(focusContext)}.");
      return result;
    }

    string routeLabel = elevatedRoute ? "Elevated insertion" : "Insertion";
    string details = result.ErrorMessage ?? "No error message provided.";
    if (result.Outcome == InsertionOutcome.Dispatched)
    {
      diagnostics?.Warning(
        $"{routeLabel} only reached dispatch using {result.MethodUsed}; visible text was not verified. target={WindowFocusContextLogFormatter.Describe(focusContext)} details={details}");
    }
    else
    {
      diagnostics?.Warning(
        $"{routeLabel} outcome={result.Outcome} method={result.MethodUsed} target={WindowFocusContextLogFormatter.Describe(focusContext)} details={details}");
    }

    return result;
  }

  private async Task<InsertionResult> InsertWithClipboardFallbackAsync(
    string text,
    WindowFocusContext focusContext,
    bool restoreClipboard,
    ClipboardOwnership ownership,
    CancellationToken cancellationToken)
  {
    InsertionResult clipboardResult = await AttemptClipboardPasteAsync(
      text,
      focusContext,
      restoreClipboard,
      ownership,
      cancellationToken).ConfigureAwait(false);
    if (!clipboardResult.SafeToRetry)
    {
      return clipboardResult;
    }

    cancellationToken.ThrowIfCancellationRequested();
    diagnostics?.Warning(
      $"Clipboard paste reached {clipboardResult.Outcome}; attempting fallback via {InsertionMethod.SendInputUnicodeTyping}.");

    InsertionResult typingFallbackResult = await InsertUsingUnicodeTypingAsync(
      text,
      focusContext,
      cancellationToken).ConfigureAwait(false);
    if (typingFallbackResult.Outcome != InsertionOutcome.UnknownOutcome)
    {
      return string.IsNullOrWhiteSpace(clipboardResult.ErrorMessage)
        ? typingFallbackResult
        : typingFallbackResult with
        {
          ErrorMessage = AppendMessage(
            clipboardResult.ErrorMessage,
            typingFallbackResult.ErrorMessage),
        };
    }

    string combinedError = CombineAttemptMessages(
      $"Clipboard paste returned {clipboardResult.Outcome}: {clipboardResult.ErrorMessage}",
      $"Fallback typing returned {typingFallbackResult.Outcome}: {typingFallbackResult.ErrorMessage}");
    return InsertionResult.Unknown(InsertionMethod.SendInputUnicodeTyping, combinedError);
  }

  private async Task<InsertionResult> AttemptClipboardPasteAsync(
    string text,
    WindowFocusContext focusContext,
    bool restoreClipboard,
    ClipboardOwnership ownership,
    CancellationToken cancellationToken)
  {
    ClipboardSnapshot? snapshot = null;
    bool clipboardMutated = false;
    string? restoreError = null;
    InsertionResult result = InsertionResult.Unknown(
      InsertionMethod.ClipboardPaste,
      "Clipboard paste did not complete.");

    try
    {
      if (restoreClipboard)
      {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
          snapshot = clipboardController.CaptureSnapshot();
        }
        catch (ClipboardContentNotSupportedException ex)
        {
          ownership.AllowRecoveryCopy = false;
          return InsertionResult.Unknown(InsertionMethod.ClipboardPaste, ex.Message) with { SafeToRetry = true };
        }
        catch (ClipboardOperationException ex)
        {
          return InsertionResult.Unknown(InsertionMethod.ClipboardPaste, ex.Message) with { SafeToRetry = true };
        }
      }

      cancellationToken.ThrowIfCancellationRequested();
      try
      {
        ownership.Sequence = clipboardController.SetUnicodeText(text, snapshot?.SequenceNumber ?? ownership.Sequence);
        clipboardMutated = true;
      }
      catch (ClipboardOperationException ex)
      {
        result = InsertionResult.Unknown(InsertionMethod.ClipboardPaste, ex.Message) with { SafeToRetry = !ex.MayHaveMutated };
        return result;
      }

      if (!IsSafeDispatchTarget(focusContext))
      {
        return TargetChanged(InsertionMethod.ClipboardPaste);
      }
      InputDispatchResult pasteDispatch = inputDispatcher.SendPasteShortcut();
      if (!pasteDispatch.Success)
      {
        result = ClassifyDispatchFailure(
          InsertionMethod.ClipboardPaste,
          pasteDispatch.ErrorMessage ?? "Failed to send paste shortcut.") with { SafeToRetry = !pasteDispatch.MayHaveDispatched };
        return result;
      }

      result = await VerifyOutcomeAsync(
        text,
        focusContext,
        InsertionMethod.ClipboardPaste,
        waitBeforeFinalize: restoreClipboard ? GetClipboardConsumptionWindow(focusContext) : TimeSpan.Zero,
        cancellationToken: cancellationToken).ConfigureAwait(false);
      return result;
    }
    finally
    {
      if (clipboardMutated && snapshot is not null)
      {
        TryRestoreSnapshot(snapshot, ownership, out restoreError);
      }

      if (restoreError is not null)
      {
        diagnostics?.Warning(restoreError);
      }
    }
  }

  private async Task<InsertionResult> InsertUsingUnicodeTypingAsync(
    string text,
    WindowFocusContext focusContext,
    CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (!IsSafeDispatchTarget(focusContext))
    {
      return TargetChanged(InsertionMethod.SendInputUnicodeTyping);
    }
    InputDispatchResult typingDispatch = DispatchUnicodeToTarget(text, focusContext, cancellationToken);
    if (!typingDispatch.Success)
    {
      return ClassifyDispatchFailure(
        InsertionMethod.SendInputUnicodeTyping,
        typingDispatch.ErrorMessage ?? "Failed to send Unicode typing input.");
    }

    return await VerifyOutcomeAsync(
      text,
      focusContext,
      InsertionMethod.SendInputUnicodeTyping,
      // Desktop controls can apply SendInput on their next dispatcher turn. Waiting
      // briefly for verification is safer than sending the text again and duplicating it.
      waitBeforeFinalize: GetTypingVerificationWindow(),
      cancellationToken: cancellationToken).ConfigureAwait(false);
  }

  private InputDispatchResult DispatchUnicodeToTarget(string text, WindowFocusContext target, CancellationToken cancellationToken)
  {
    for (int offset = 0; offset < text.Length;)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (!IsSafeDispatchTarget(target))
        return new InputDispatchResult(false, "The target changed during typing.") { MayHaveDispatched = offset > 0 };
      int count = Math.Min(256, text.Length - offset);
      if (offset + count < text.Length && char.IsHighSurrogate(text[offset + count - 1])
          && char.IsLowSurrogate(text[offset + count])) count--;
      InputDispatchResult result = inputDispatcher.SendUnicodeText(text.Substring(offset, count));
      if (!result.Success) return result with { MayHaveDispatched = offset > 0 || result.MayHaveDispatched };
      offset += count;
    }
    return InputDispatchResult.Ok;
  }

  private async Task<InsertionResult> VerifyOutcomeAsync(
    string insertedText,
    WindowFocusContext baselineContext,
    InsertionMethod methodUsed,
    TimeSpan waitBeforeFinalize,
    CancellationToken cancellationToken)
  {
    if (!baselineContext.CanVerifyText)
    {
      if (waitBeforeFinalize > TimeSpan.Zero)
      {
        await Task.Delay(waitBeforeFinalize, cancellationToken).ConfigureAwait(false);
      }

      return InsertionResult.Dispatched(
        methodUsed,
        $"{FormatMethod(methodUsed)} was dispatched, but the target surface could not be verified.");
    }

    TimeSpan pollInterval = GetVerificationPollingInterval();
    TimeSpan verificationWindow = GetVerificationWindow(baselineContext, waitBeforeFinalize);
    DateTimeOffset deadline = DateTimeOffset.UtcNow + verificationWindow;

    while (true)
    {
      cancellationToken.ThrowIfCancellationRequested();

      WindowFocusContext currentContext = windowFocusProvider.GetWindowFocusContext();
      if (MatchesVerificationTarget(baselineContext, currentContext)
          && HasObservedInsertedText(baselineContext, currentContext, insertedText))
      {
        return InsertionResult.Verified(methodUsed);
      }

      if (DateTimeOffset.UtcNow >= deadline)
      {
        if (MatchesVerificationTarget(baselineContext, currentContext)
            && IsLikelyIndirectTextSurface(baselineContext, currentContext))
        {
          return InsertionResult.Dispatched(
            methodUsed,
            $"{FormatMethod(methodUsed)} was dispatched to an indirect text surface, but UI Automation could not verify the visible text.");
        }

        return InsertionResult.Dispatched(
          methodUsed,
          $"{FormatMethod(methodUsed)} was dispatched, but verification did not observe text appearing.");
      }

      await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
    }
  }

  private static bool MatchesVerificationTarget(WindowFocusContext baselineContext, WindowFocusContext currentContext)
  {
    if (!InsertionTargetIdentity.FromContext(baselineContext).Matches(currentContext))
    {
      return false;
    }
    if (baselineContext.ForegroundWindowHandle != 0
        && currentContext.ForegroundWindowHandle != 0
        && baselineContext.ForegroundWindowHandle != currentContext.ForegroundWindowHandle)
    {
      return false;
    }

    if (baselineContext.ForegroundProcessId != 0
        && currentContext.ForegroundProcessId != 0
        && baselineContext.ForegroundProcessId != currentContext.ForegroundProcessId)
    {
      return false;
    }

    return true;
  }

  private static bool HasObservedInsertedText(
    WindowFocusContext baselineContext,
    WindowFocusContext currentContext,
    string insertedText)
  {
    string expectedText = NormalizeObservedText(insertedText);
    if (expectedText.Length == 0 || !currentContext.CanVerifyText)
    {
      return false;
    }

    string before = NormalizeObservedText(baselineContext.ObservedText);
    string after = NormalizeObservedText(currentContext.ObservedText);
    if (string.Equals(before, after, StringComparison.Ordinal))
    {
      return false;
    }

    int prefix = 0;
    while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix]) prefix++;
    // Repeated text can shift the minimal diff into an existing suffix. Accept
    // only a contiguous edit consistent with inserting exactly this text.
    for (int start = Math.Max(0, prefix - expectedText.Length); start <= prefix; start++)
    {
      if (start + expectedText.Length <= after.Length
          && after.AsSpan(start, expectedText.Length).SequenceEqual(expectedText)
          && before.AsSpan(0, start).SequenceEqual(after.AsSpan(0, start))
          && after.Length - start - expectedText.Length <= before.Length - start
          && before.EndsWith(after[(start + expectedText.Length)..], StringComparison.Ordinal)) return true;
    }
    return false;
  }

  private static string NormalizeObservedText(string? value)
  {
    return string.IsNullOrEmpty(value)
      ? string.Empty
      : value.Replace("\r\n", "\n", StringComparison.Ordinal);
  }

  private static InsertionResult ClassifyDispatchFailure(InsertionMethod methodUsed, string errorMessage)
  {
    return errorMessage.Contains("privilege boundaries", StringComparison.OrdinalIgnoreCase)
           || errorMessage.Contains("UIPI", StringComparison.OrdinalIgnoreCase)
      ? InsertionResult.Blocked(methodUsed, errorMessage, InsertionBlockReason.PrivilegeBoundary)
      : InsertionResult.Unknown(methodUsed, errorMessage);
  }

  private TimeSpan GetClipboardConsumptionWindow(WindowFocusContext focusContext)
  {
    TimeSpan defaultWindow = options.ClipboardConsumptionWindow > TimeSpan.Zero
      ? options.ClipboardConsumptionWindow
      : TextInsertionOptions.Default.ClipboardConsumptionWindow;
    TimeSpan browserWindow = options.BrowserClipboardConsumptionWindow > TimeSpan.Zero
      ? options.BrowserClipboardConsumptionWindow
      : TextInsertionOptions.Default.BrowserClipboardConsumptionWindow;

    return focusContext.BrowserSurfaceKind == BrowserSurfaceKind.None
      ? defaultWindow
      : browserWindow;
  }

  private TimeSpan GetVerificationWindow(WindowFocusContext focusContext, TimeSpan requestedWindow)
  {
    TimeSpan baselineWindow = requestedWindow > TimeSpan.Zero
      ? requestedWindow
      : GetVerificationPollingInterval();
    if (!IsLikelyIndirectTextSurface(focusContext, focusContext))
    {
      return baselineWindow;
    }

    TimeSpan indirectWindow = focusContext.BrowserSurfaceKind == BrowserSurfaceKind.EditablePage
      ? TimeSpan.FromMilliseconds(700)
      : TimeSpan.FromMilliseconds(300);
    return baselineWindow < indirectWindow
      ? indirectWindow
      : baselineWindow;
  }

  private TimeSpan GetVerificationPollingInterval()
  {
    return options.VerificationPollingInterval > TimeSpan.Zero
      ? options.VerificationPollingInterval
      : TextInsertionOptions.Default.VerificationPollingInterval;
  }

  private TimeSpan GetTypingVerificationWindow()
  {
    return options.TypingVerificationWindow > TimeSpan.Zero
      ? options.TypingVerificationWindow
      : TextInsertionOptions.Default.TypingVerificationWindow;
  }

  private static bool IsLikelyIndirectTextSurface(WindowFocusContext baselineContext, WindowFocusContext currentContext)
  {
    return baselineContext.IsEditable
           && currentContext.IsEditable
           && (ContainsAny(
                 baselineContext.FocusedControlClassName,
                 IndirectTextSurfaceClassTokens)
               || ContainsAny(
                 baselineContext.FocusedAutomationClassName,
                 IndirectTextSurfaceClassTokens)
               || ContainsAny(
                 baselineContext.FocusedAutomationName,
                 IndirectTextSurfaceNameTokens)
               || ContainsAny(
                 currentContext.FocusedControlClassName,
                 IndirectTextSurfaceClassTokens)
               || ContainsAny(
                 currentContext.FocusedAutomationClassName,
                 IndirectTextSurfaceClassTokens)
               || ContainsAny(
                 currentContext.FocusedAutomationName,
                 IndirectTextSurfaceNameTokens));
  }

  private bool IsTargetProcessBlacklisted(WindowFocusContext context, out string? processName)
  {
    processName = null;
    if (blockedProcessNames.Count == 0)
    {
      return false;
    }

    string? normalized = NormalizeProcessName(context.ForegroundProcessName);
    if (string.IsNullOrWhiteSpace(normalized))
    {
      return false;
    }

    if (!blockedProcessNames.Contains(normalized))
    {
      return false;
    }

    processName = normalized;
    return true;
  }

  private static bool IsLikelySecureField(WindowFocusContext context, out string reason)
  {
    if (context.FocusedControlPasswordProtected)
    {
      reason = "password-protected input control";
      return true;
    }

    if (!string.IsNullOrWhiteSpace(context.FocusedControlClassName)
        && ContainsAny(context.FocusedControlClassName, SecureClassNameKeywords))
    {
      reason = $"focused control class '{context.FocusedControlClassName}'";
      return true;
    }

    if (!string.IsNullOrWhiteSpace(context.ForegroundWindowClassName)
        && ContainsAny(context.ForegroundWindowClassName, SecureClassNameKeywords))
    {
      reason = $"window class '{context.ForegroundWindowClassName}'";
      return true;
    }

    string? normalizedProcessName = NormalizeProcessName(context.ForegroundProcessName);
    if (!string.IsNullOrWhiteSpace(normalizedProcessName)
        && KnownSecureProcesses.Contains(normalizedProcessName))
    {
      reason = $"process '{normalizedProcessName}'";
      return true;
    }

    reason = string.Empty;
    return false;
  }

  private static bool ContainsAny(string value, IReadOnlyList<string> tokens)
  {
    foreach (string token in tokens)
    {
      if (value.Contains(token, StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }

    return false;
  }

  private static bool ContainsAny(string? value, params string[] tokens)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return false;
    }

    foreach (string token in tokens)
    {
      if (value.Contains(token, StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }

    return false;
  }

  private static string? NormalizeProcessName(string? processName)
  {
    if (string.IsNullOrWhiteSpace(processName))
    {
      return null;
    }

    string normalized = processName.Trim();
    if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
    {
      normalized = normalized[..^4];
    }

    return normalized;
  }

  private static string FormatMethod(InsertionMethod methodUsed)
  {
    return methodUsed == InsertionMethod.ClipboardPaste
      ? "Clipboard paste"
      : "Unicode typing";
  }

  private static string? AppendMessage(string? primary, string? secondary)
  {
    if (string.IsNullOrWhiteSpace(primary))
    {
      return secondary;
    }

    if (string.IsNullOrWhiteSpace(secondary))
    {
      return primary;
    }

    return $"{primary} {secondary}";
  }

  private static string CombineAttemptMessages(string? first, string? second)
  {
    if (string.IsNullOrWhiteSpace(first))
    {
      return second ?? "Insertion outcome is unknown.";
    }

    if (string.IsNullOrWhiteSpace(second))
    {
      return first;
    }

    return $"{first} {second}";
  }

  private bool TryRestoreSnapshot(ClipboardSnapshot? snapshot, ClipboardOwnership ownership, out string? error)
  {
    error = null;

    if (snapshot is null)
    {
      return true;
    }

    try
    {
      uint? restored = clipboardController.RestoreSnapshot(snapshot, ownership.Sequence);
      if (restored.HasValue) ownership.Sequence = restored.Value;
      return true;
    }
    catch (ClipboardOperationException ex)
    {
      error = $"Clipboard restore failed after insertion dispatch: {ex.Message}";
      return false;
    }
  }

  private sealed class ClipboardOwnership(uint sequence)
  {
    public uint Sequence { get; set; } = sequence;
    public bool AllowRecoveryCopy { get; set; } = true;
  }

  private bool IsSafeDispatchTarget(WindowFocusContext expected)
  {
    WindowFocusContext current = windowFocusProvider.GetWindowFocusContext();
    return InsertionTargetIdentity.FromContext(expected).Matches(current)
      && !current.IsNonEditable && !current.FocusedControlReadOnly
      && !IsTargetProcessBlacklisted(current, out _)
      && (!options.EnableSecureFieldDetection || !IsLikelySecureField(current, out _));
  }

  private static InsertionResult TargetChanged(InsertionMethod method) => InsertionResult.Blocked(
    method, "The original editable target is no longer focused.", InsertionBlockReason.TargetChanged);
}

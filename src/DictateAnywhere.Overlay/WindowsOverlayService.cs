using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Domain;

namespace DictateAnywhere.Overlay;

public sealed class WindowsOverlayService : IOverlayService, IAsyncDisposable
{
  private readonly object sync = new();
  private readonly Lazy<IOverlayPresenter> presenter;
  private readonly OverlayServiceOptions options;
  private readonly IOverlayAnchorProvider? anchorProvider;
  private readonly IDiagnostics? diagnostics;

  private DateTimeOffset? recordingStartUtc;
  private OverlayAnchorSnapshot? lastResolvedAnchor;
  private CancellationTokenSource? autoHideCts;
  private Task autoHideTask = Task.CompletedTask;
  private bool disposed;

  public WindowsOverlayService(
    IOverlayPresenter presenter,
    OverlayServiceOptions options,
    IOverlayAnchorProvider? anchorProvider = null,
    IDiagnostics? diagnostics = null)
    : this(() => presenter, options, anchorProvider, diagnostics)
  {
  }

  public WindowsOverlayService(
    Func<IOverlayPresenter> presenterFactory,
    OverlayServiceOptions options,
    IOverlayAnchorProvider? anchorProvider = null,
    IDiagnostics? diagnostics = null)
  {
    ArgumentNullException.ThrowIfNull(presenterFactory);
    this.options = options ?? throw new ArgumentNullException(nameof(options));
    this.anchorProvider = anchorProvider;
    this.diagnostics = diagnostics;
    presenter = new Lazy<IOverlayPresenter>(
      () => presenterFactory() ?? throw new InvalidOperationException("Overlay presenter factory returned null."),
      LazyThreadSafetyMode.ExecutionAndPublication);

    if (this.options.InsertedStateDuration <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(options), "Inserted state duration must be greater than zero.");
    }

    if (this.options.ErrorStateDuration <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(options), "Error state duration must be greater than zero.");
    }
  }

  public async Task ShowStateAsync(
    DictationSessionState state,
    string? message = null,
    TimeSpan? elapsed = null,
    OverlayDisplayOptions? display = null,
    CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    cancellationToken.ThrowIfCancellationRequested();

    if (!options.Enabled)
    {
      LogOverlaySuppressed(state, display ?? OverlayDisplayOptions.Default, "overlay-disabled");
      return;
    }

    CancelAutoHide();

    if (state == DictationSessionState.Idle)
    {
      ResetRecordingTimer();
      ResetLastResolvedAnchor();
      if (presenter.IsValueCreated)
      {
        await presenter.Value.HideAsync(cancellationToken).ConfigureAwait(false);
      }

      return;
    }

    OverlayVisualState visualState = MapState(state);
    string resolvedMessage = ResolveMessage(visualState, message);
    TimeSpan? resolvedElapsed = ResolveElapsed(state, elapsed);
    OverlayDisplayOptions resolvedDisplay = display ?? OverlayDisplayOptions.Default;
    OverlayPresentation? presentation = BuildPresentation(
      state,
      visualState,
      resolvedMessage,
      resolvedElapsed,
      resolvedDisplay);

    if (presentation is null)
    {
      if (presenter.IsValueCreated)
      {
        await presenter.Value.HideAsync(cancellationToken).ConfigureAwait(false);
      }

      return;
    }

    await presenter.Value.ShowAsync(presentation, cancellationToken).ConfigureAwait(false);

    if (visualState == OverlayVisualState.Inserted && options.AutoHideInsertedState)
    {
      BeginAutoHide(ResolveAutoHideDuration(resolvedDisplay, options.InsertedStateDuration));
    }
    else if (visualState == OverlayVisualState.Error && options.AutoHideErrorState)
    {
      BeginAutoHide(ResolveAutoHideDuration(resolvedDisplay, options.ErrorStateDuration));
    }
  }

  private static TimeSpan ResolveAutoHideDuration(OverlayDisplayOptions display, TimeSpan fallback)
  {
    TimeSpan duration = display.AutoHideDuration ?? fallback;
    return duration > TimeSpan.Zero
      ? duration
      : throw new ArgumentOutOfRangeException(nameof(display), "Overlay auto-hide duration must be greater than zero.");
  }

  public Task HideAsync(CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    cancellationToken.ThrowIfCancellationRequested();

    if (!options.Enabled)
    {
      return Task.CompletedTask;
    }

    if (!presenter.IsValueCreated)
    {
      return Task.CompletedTask;
    }

    CancelAutoHide();
    ResetRecordingTimer();
    ResetLastResolvedAnchor();
    return presenter.Value.HideAsync(cancellationToken);
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    CancelAutoHide();
    Task pendingAutoHide;
    lock (sync)
    {
      pendingAutoHide = autoHideTask;
    }

    await pendingAutoHide.ConfigureAwait(false);

    if (!presenter.IsValueCreated)
    {
      return;
    }

    if (presenter.Value is IAsyncDisposable asyncDisposable)
    {
      await asyncDisposable.DisposeAsync().ConfigureAwait(false);
      return;
    }

    if (presenter.Value is IDisposable disposable)
    {
      disposable.Dispose();
    }
  }

  private static OverlayVisualState MapState(DictationSessionState state)
  {
    return state switch
    {
      DictationSessionState.Recording => OverlayVisualState.Recording,
      DictationSessionState.Transcribing => OverlayVisualState.Transcribing,
      DictationSessionState.Inserting => OverlayVisualState.Inserting,
      DictationSessionState.Completed => OverlayVisualState.Inserted,
      DictationSessionState.Error => OverlayVisualState.Error,
      _ => OverlayVisualState.Transcribing,
    };
  }

  private static string ResolveMessage(OverlayVisualState visualState, string? message)
  {
    if (!string.IsNullOrWhiteSpace(message))
    {
      return message;
    }

    return visualState switch
    {
      OverlayVisualState.Recording => "Recording...",
      OverlayVisualState.Transcribing => "Transcribing speech locally...",
      OverlayVisualState.Inserting => "Inserting text at cursor...",
      OverlayVisualState.Inserted => "Inserted",
      OverlayVisualState.Error => "Dictation failed.",
      _ => "Working...",
    };
  }

  private OverlayPresentation? BuildPresentation(
    DictationSessionState state,
    OverlayVisualState visualState,
    string resolvedMessage,
    TimeSpan? resolvedElapsed,
    OverlayDisplayOptions display)
  {
    if (!display.RequestsAnchor)
    {
      ResetLastResolvedAnchor();
      LogPlacement(
        state,
        display,
        OverlayPlacementMode.CornerPanel,
        null,
        reusedCachedAnchor: false,
        skipped: false,
        decisionReason: "status-panel-requested");
      return CreateCornerPanelPresentation(visualState, resolvedMessage, resolvedElapsed);
    }

    if (!options.CaretIndicatorEnabled)
    {
      ResetLastResolvedAnchor();
      if (options.FallbackToCornerOverlay)
      {
        LogPlacement(
          state,
          display,
          OverlayPlacementMode.CornerPanel,
          null,
          reusedCachedAnchor: false,
          skipped: false,
          decisionReason: "caret-indicator-disabled");
        return CreateCornerPanelPresentation(visualState, resolvedMessage, resolvedElapsed);
      }

      LogPlacement(
        state,
        display,
        OverlayPlacementMode.FocusAnchor,
        null,
        reusedCachedAnchor: false,
        skipped: true,
        decisionReason: "caret-indicator-disabled-no-fallback");
      return null;
    }

    OverlayAnchorSnapshot? anchor = TryResolveAnchor(display, visualState, out bool reusedCachedAnchor);
    if (options.CaretIndicatorEnabled && anchor is not null && anchor.HasBounds)
    {
      CacheAnchor(anchor);
      LogPlacement(
        state,
        display,
        OverlayPlacementMode.FocusAnchor,
        anchor,
        reusedCachedAnchor,
        skipped: false,
        decisionReason: reusedCachedAnchor ? "cached-anchor" : "resolved-anchor");
      return new OverlayPresentation(
        VisualState: visualState,
        Message: resolvedMessage,
        ShowTimer: visualState == OverlayVisualState.Recording,
        Elapsed: resolvedElapsed,
        PlacementMode: OverlayPlacementMode.FocusAnchor,
        IndicatorKind: display.IndicatorKind,
        AnimationState: display.AnimationState,
        Anchor: anchor);
    }

    ResetLastResolvedAnchor();

    if (options.FallbackToCornerOverlay)
    {
      LogPlacement(
        state,
        display,
        OverlayPlacementMode.CornerPanel,
        anchor,
        reusedCachedAnchor,
        skipped: false,
        decisionReason: "anchor-unavailable-corner-fallback");
      return CreateCornerPanelPresentation(visualState, resolvedMessage, resolvedElapsed);
    }

    LogPlacement(
      state,
      display,
      OverlayPlacementMode.FocusAnchor,
      anchor,
      reusedCachedAnchor,
      skipped: true,
      decisionReason: "anchor-unavailable-no-fallback");
    return null;
  }

  private static OverlayPresentation CreateCornerPanelPresentation(
    OverlayVisualState visualState,
    string resolvedMessage,
    TimeSpan? resolvedElapsed)
  {
    return new OverlayPresentation(
      VisualState: visualState,
      Message: resolvedMessage,
      ShowTimer: visualState == OverlayVisualState.Recording,
      Elapsed: resolvedElapsed,
      PlacementMode: OverlayPlacementMode.CornerPanel,
      IndicatorKind: OverlayIndicatorKind.StatusPanel,
      AnimationState: OverlayAnimationState.None,
      Anchor: null);
  }

  private TimeSpan? ResolveElapsed(DictationSessionState state, TimeSpan? requestedElapsed)
  {
    if (state != DictationSessionState.Recording)
    {
      ResetRecordingTimer();
      return null;
    }

    if (requestedElapsed.HasValue)
    {
      lock (sync)
      {
        recordingStartUtc = DateTimeOffset.UtcNow - requestedElapsed.Value;
      }

      return requestedElapsed;
    }

    lock (sync)
    {
      DateTimeOffset now = DateTimeOffset.UtcNow;
      recordingStartUtc ??= now;
      return now - recordingStartUtc.Value;
    }
  }

  private OverlayAnchorSnapshot? TryResolveAnchor(
    OverlayDisplayOptions display,
    OverlayVisualState visualState,
    out bool reusedCachedAnchor)
  {
    reusedCachedAnchor = false;
    if (!display.RequestsAnchor || anchorProvider is null)
    {
      return null;
    }

    if (visualState != OverlayVisualState.Recording)
    {
      OverlayAnchorSnapshot? cached = GetLastResolvedAnchor();
      if (cached is not null && cached.HasBounds)
      {
        reusedCachedAnchor = true;
        return cached;
      }
    }

    OverlayAnchorSnapshot resolved = anchorProvider.GetCurrentAnchor();
    return resolved.HasBounds ? resolved : null;
  }

  private void BeginAutoHide(TimeSpan duration)
  {
    CancellationTokenSource? previousSource;
    Task previousTask;
    CancellationTokenSource cts = new();

    lock (sync)
    {
      previousSource = autoHideCts;
      previousTask = autoHideTask.IsCompleted ? Task.CompletedTask : autoHideTask;
      autoHideCts = cts;
      autoHideTask = Task.WhenAll(
        previousTask,
        ObserveAutoHideAsync(duration, cts));
    }

    TryCancel(previousSource);
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Auto-hide is a background presentation boundary; failures are observed and reported without escaping the owned task.")]
  private async Task ObserveAutoHideAsync(TimeSpan duration, CancellationTokenSource source)
  {
    try
    {
      await Task.Delay(duration, source.Token).ConfigureAwait(false);
      if (presenter.IsValueCreated)
      {
        await presenter.Value.HideAsync(source.Token).ConfigureAwait(false);
      }
    }
    catch (OperationCanceledException) when (source.IsCancellationRequested)
    {
    }
    catch (Exception ex)
    {
      diagnostics?.Warning($"Overlay auto-hide failed: {ex.Message}");
    }
    finally
    {
      source.Dispose();
    }
  }

  private void CancelAutoHide()
  {
    CancellationTokenSource? toCancel = null;
    lock (sync)
    {
      if (autoHideCts is not null)
      {
        toCancel = autoHideCts;
        autoHideCts = null;
      }
    }

    TryCancel(toCancel);
  }

  private static void TryCancel(CancellationTokenSource? source)
  {
    if (source is null)
    {
      return;
    }

    try
    {
      source.Cancel();
    }
    catch (ObjectDisposedException)
    {
      // The auto-hide operation completed while cancellation was requested.
    }
  }

  private void ResetRecordingTimer()
  {
    lock (sync)
    {
      recordingStartUtc = null;
    }
  }

  private void CacheAnchor(OverlayAnchorSnapshot anchor)
  {
    lock (sync)
    {
      lastResolvedAnchor = anchor;
    }
  }

  private OverlayAnchorSnapshot? GetLastResolvedAnchor()
  {
    lock (sync)
    {
      return lastResolvedAnchor;
    }
  }

  private void ResetLastResolvedAnchor()
  {
    lock (sync)
    {
      lastResolvedAnchor = null;
    }
  }

  private void LogPlacement(
    DictationSessionState state,
    OverlayDisplayOptions display,
    OverlayPlacementMode resolvedPlacement,
    OverlayAnchorSnapshot? anchor,
    bool reusedCachedAnchor,
    bool skipped,
    string decisionReason)
  {
    if (diagnostics is null)
    {
      return;
    }

    string anchorMode = anchor?.Source == OverlayAnchorSource.Caret
      ? "caret"
      : anchor?.Source is OverlayAnchorSource.FocusedElement or OverlayAnchorSource.ForegroundWindow
        ? "control"
        : "none";
    string anchorBounds = anchor?.Bounds?.ToString() ?? "<none>";
    diagnostics.Info(
      $"Overlay placement: state={state}, requested={display.PlacementMode}, resolved={resolvedPlacement}, indicator={display.IndicatorKind}, animation={display.AnimationState}, skipped={skipped}, decisionReason={decisionReason}, anchorSource={anchor?.Source ?? OverlayAnchorSource.None}, anchorFallback={anchor?.UsedFallbackSource ?? false}, anchorMode={anchorMode}, reusedAnchor={reusedCachedAnchor}, bounds={anchorBounds}.");
  }

  private void LogOverlaySuppressed(
    DictationSessionState state,
    OverlayDisplayOptions display,
    string reason)
  {
    diagnostics?.Info(
      $"Overlay suppressed: state={state}, requested={display.PlacementMode}, indicator={display.IndicatorKind}, animation={display.AnimationState}, reason={reason}, overlayEnabled={options.Enabled}, caretIndicatorEnabled={options.CaretIndicatorEnabled}, fallbackToCornerOverlay={options.FallbackToCornerOverlay}.");
  }
}

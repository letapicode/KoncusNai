using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Domain;
using DictateAnywhere.Overlay;

namespace DictateAnywhere.Overlay.Tests;

public sealed class WindowsOverlayServiceTests
{
  [Xunit.Fact]
  public async Task ShowStateAsync_Recording_RendersTimerState()
  {
    FakeOverlayPresenter presenter = new();
    await using WindowsOverlayService service = new(
      presenter,
      OverlayServiceOptions.Default with
      {
        AutoHideInsertedState = false,
      });

    await service.ShowStateAsync(DictationSessionState.Recording);

    OverlayPresentation presentation = Xunit.Assert.Single(presenter.ShownPresentations);
    Xunit.Assert.Equal(OverlayVisualState.Recording, presentation.VisualState);
    Xunit.Assert.True(presentation.ShowTimer);
    Xunit.Assert.True(presentation.Elapsed.HasValue);
    Xunit.Assert.Contains("Recording", presentation.Message, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Equal(OverlayPlacementMode.CornerPanel, presentation.PlacementMode);
    Xunit.Assert.Equal(OverlayIndicatorKind.StatusPanel, presentation.IndicatorKind);
  }

  [Xunit.Fact]
  public async Task ShowStateAsync_Completed_MapsToInserted()
  {
    FakeOverlayPresenter presenter = new();
    await using WindowsOverlayService service = new(
      presenter,
      OverlayServiceOptions.Default with
      {
        AutoHideInsertedState = false,
      });

    await service.ShowStateAsync(DictationSessionState.Completed);

    OverlayPresentation presentation = Xunit.Assert.Single(presenter.ShownPresentations);
    Xunit.Assert.Equal(OverlayVisualState.Inserted, presentation.VisualState);
    Xunit.Assert.False(presentation.ShowTimer);
    Xunit.Assert.Equal("Inserted", presentation.Message);
    Xunit.Assert.Equal(OverlayPlacementMode.CornerPanel, presentation.PlacementMode);
  }

  [Xunit.Fact]
  public async Task ShowStateAsync_Idle_HidesOverlay()
  {
    FakeOverlayPresenter presenter = new();
    await using WindowsOverlayService service = new(presenter, OverlayServiceOptions.Default);

    await service.ShowStateAsync(DictationSessionState.Recording);
    await service.ShowStateAsync(DictationSessionState.Idle);

    Xunit.Assert.Single(presenter.ShownPresentations);
    Xunit.Assert.Equal(1, presenter.HideCallCount);
  }

  [Xunit.Fact]
  public async Task ShowStateAsync_WhenDisabled_DoesNotRender()
  {
    FakeOverlayPresenter presenter = new();
    await using WindowsOverlayService service = new(
      presenter,
      OverlayServiceOptions.Default with
      {
        Enabled = false,
      });

    await service.ShowStateAsync(DictationSessionState.Recording, elapsed: TimeSpan.FromSeconds(2));

    Xunit.Assert.Empty(presenter.ShownPresentations);
    Xunit.Assert.Equal(0, presenter.HideCallCount);
  }

  [Xunit.Fact]
  public async Task ShowStateAsync_WhenDisabled_LogsSuppressionReason()
  {
    FakeOverlayPresenter presenter = new();
    FakeDiagnostics diagnostics = new();
    await using WindowsOverlayService service = new(
      presenter,
      OverlayServiceOptions.Default with
      {
        Enabled = false,
      },
      diagnostics: diagnostics);

    await service.ShowStateAsync(
      DictationSessionState.Recording,
      display: OverlayDisplayOptions.AnchoredRecording);

    Xunit.Assert.Contains(
      diagnostics.InfoMessages,
      message => message.Contains("Overlay suppressed:", StringComparison.Ordinal)
                 && message.Contains("reason=overlay-disabled", StringComparison.Ordinal));
  }

  [Xunit.Fact]
  public async Task DisabledOverlay_DoesNotCreatePresenter()
  {
    int factoryCallCount = 0;
    await using WindowsOverlayService service = new(
      () =>
      {
        factoryCallCount++;
        return new FakeOverlayPresenter();
      },
      OverlayServiceOptions.Default with
      {
        Enabled = false,
      });

    await service.ShowStateAsync(DictationSessionState.Recording);
    await service.HideAsync();

    Xunit.Assert.Equal(0, factoryCallCount);
  }

  [Xunit.Fact]
  public async Task ShowStateAsync_Inserted_AutoHides()
  {
    FakeOverlayPresenter presenter = new();
    await using WindowsOverlayService service = new(
      presenter,
      OverlayServiceOptions.Default with
      {
        InsertedStateDuration = TimeSpan.FromMilliseconds(10),
      });

    await service.ShowStateAsync(DictationSessionState.Completed);
    await presenter.FirstHide.WaitAsync(TimeSpan.FromSeconds(5));

    Xunit.Assert.Single(presenter.ShownPresentations);
    Xunit.Assert.True(presenter.HideCallCount >= 1);
  }

  [Xunit.Fact]
  public async Task ShowStateAsync_Inserted_UsesDisplayAutoHideDurationOverride()
  {
    FakeOverlayPresenter presenter = new();
    await using WindowsOverlayService service = new(
      presenter,
      OverlayServiceOptions.Default with
      {
        InsertedStateDuration = TimeSpan.FromSeconds(10),
      });
    OverlayDisplayOptions display = OverlayDisplayOptions.AnchoredNotice with
    {
      AutoHideDuration = TimeSpan.FromMilliseconds(10),
    };

    await service.ShowStateAsync(
      DictationSessionState.Completed,
      DictationStatusMessages.NoAudibleSpeechDetected,
      display: display);
    await presenter.FirstHide.WaitAsync(TimeSpan.FromSeconds(5));

    OverlayPresentation presentation = Xunit.Assert.Single(presenter.ShownPresentations);
    Xunit.Assert.Equal(DictationStatusMessages.NoAudibleSpeechDetected, presentation.Message);
    Xunit.Assert.True(presenter.HideCallCount >= 1);
  }

  [Xunit.Fact]
  public async Task ShowStateAsync_Error_AutoHides()
  {
    FakeOverlayPresenter presenter = new();
    await using WindowsOverlayService service = new(
      presenter,
      OverlayServiceOptions.Default with
      {
        ErrorStateDuration = TimeSpan.FromMilliseconds(10),
      });

    await service.ShowStateAsync(DictationSessionState.Error, "Insertion failed.");
    await presenter.FirstHide.WaitAsync(TimeSpan.FromSeconds(5));

    Xunit.Assert.Single(presenter.ShownPresentations);
    Xunit.Assert.True(presenter.HideCallCount >= 1);
  }

  [Xunit.Fact]
  public async Task HideAsync_ResetsRecordingTimer()
  {
    FakeOverlayPresenter presenter = new();
    await using WindowsOverlayService service = new(
      presenter,
      OverlayServiceOptions.Default with
      {
        AutoHideInsertedState = false,
      });

    await service.ShowStateAsync(DictationSessionState.Recording, elapsed: TimeSpan.FromSeconds(7));
    await service.HideAsync();
    await service.ShowStateAsync(DictationSessionState.Recording);

    Xunit.Assert.Equal(2, presenter.ShownPresentations.Count);
    OverlayPresentation latest = presenter.ShownPresentations[1];
    Xunit.Assert.True(latest.Elapsed.HasValue);
    Xunit.Assert.True(latest.Elapsed.Value < TimeSpan.FromSeconds(2));
  }

  [Xunit.Fact]
  public async Task ShowStateAsync_AnchoredRecording_UsesAnchorProvider()
  {
    FakeOverlayPresenter presenter = new();
    FakeOverlayAnchorProvider anchorProvider = new(
      new OverlayAnchorSnapshot(
        new ScreenBounds(120, 240, 1, 18),
        OverlayAnchorSource.Caret,
        UsedFallbackSource: false));
    await using WindowsOverlayService service = new(
      presenter,
      OverlayServiceOptions.Default,
      anchorProvider);

    await service.ShowStateAsync(
      DictationSessionState.Recording,
      display: OverlayDisplayOptions.AnchoredRecording);

    OverlayPresentation presentation = Xunit.Assert.Single(presenter.ShownPresentations);
    Xunit.Assert.Equal(OverlayPlacementMode.FocusAnchor, presentation.PlacementMode);
    Xunit.Assert.Equal(OverlayIndicatorKind.RecordingIndicator, presentation.IndicatorKind);
    Xunit.Assert.NotNull(presentation.Anchor);
    Xunit.Assert.Equal(OverlayAnchorSource.Caret, presentation.Anchor!.Source);
  }

  [Xunit.Fact]
  public async Task ShowStateAsync_AnchoredTranscribing_ReusesPreviousAnchor()
  {
    FakeOverlayPresenter presenter = new();
    FakeOverlayAnchorProvider anchorProvider = new(
      new OverlayAnchorSnapshot(
        new ScreenBounds(140, 260, 1, 18),
        OverlayAnchorSource.Caret,
        UsedFallbackSource: false),
      OverlayAnchorSnapshot.Unavailable);
    await using WindowsOverlayService service = new(
      presenter,
      OverlayServiceOptions.Default,
      anchorProvider);

    await service.ShowStateAsync(
      DictationSessionState.Recording,
      display: OverlayDisplayOptions.AnchoredRecording);
    await service.ShowStateAsync(
      DictationSessionState.Transcribing,
      display: OverlayDisplayOptions.AnchoredTranscribing);

    Xunit.Assert.Equal(2, presenter.ShownPresentations.Count);
    OverlayPresentation transcribing = presenter.ShownPresentations[1];
    Xunit.Assert.Equal(OverlayPlacementMode.FocusAnchor, transcribing.PlacementMode);
    Xunit.Assert.Equal(OverlayIndicatorKind.TranscribingIndicator, transcribing.IndicatorKind);
    Xunit.Assert.Equal(OverlayAnimationState.LetterSpin, transcribing.AnimationState);
    Xunit.Assert.NotNull(transcribing.Anchor);
    Xunit.Assert.Equal(new ScreenBounds(140, 260, 1, 18), transcribing.Anchor!.Bounds);
  }

  [Xunit.Fact]
  public async Task ShowStateAsync_AnchoredCompletion_ReusesAnchorAndRendersCheckOnly()
  {
    FakeOverlayPresenter presenter = new();
    FakeOverlayAnchorProvider anchorProvider = new(
      new OverlayAnchorSnapshot(
        new ScreenBounds(140, 260, 1, 18),
        OverlayAnchorSource.Caret,
        UsedFallbackSource: false),
      OverlayAnchorSnapshot.Unavailable);
    await using WindowsOverlayService service = new(
      presenter,
      OverlayServiceOptions.Default with { AutoHideInsertedState = false },
      anchorProvider);

    await service.ShowStateAsync(
      DictationSessionState.Recording,
      display: OverlayDisplayOptions.AnchoredRecording);
    await service.ShowStateAsync(
      DictationSessionState.Completed,
      display: OverlayDisplayOptions.AnchoredCompletion);

    OverlayPresentation completed = presenter.ShownPresentations[1];
    Xunit.Assert.Equal(OverlayPlacementMode.FocusAnchor, completed.PlacementMode);
    Xunit.Assert.Equal(OverlayIndicatorKind.CompletionIndicator, completed.IndicatorKind);
    Xunit.Assert.Equal(OverlayAnimationState.None, completed.AnimationState);
    Xunit.Assert.Equal(new ScreenBounds(140, 260, 1, 18), completed.Anchor?.Bounds);
  }

  [Xunit.Fact]
  public async Task ShowStateAsync_AnchoredStatus_ReusesRecordingAnchorAtBottomCenter()
  {
    FakeOverlayPresenter presenter = new();
    FakeOverlayAnchorProvider anchorProvider = new(
      new OverlayAnchorSnapshot(
        new ScreenBounds(140, 260, 1, 18),
        OverlayAnchorSource.Caret,
        UsedFallbackSource: false),
      OverlayAnchorSnapshot.Unavailable);
    await using WindowsOverlayService service = new(
      presenter,
      OverlayServiceOptions.Default with { AutoHideInsertedState = false },
      anchorProvider);

    await service.ShowStateAsync(
      DictationSessionState.Recording,
      display: OverlayDisplayOptions.AnchoredRecording);
    await service.ShowStateAsync(
      DictationSessionState.Completed,
      "Saved to history.",
      display: OverlayDisplayOptions.AnchoredStatus);

    Xunit.Assert.Equal(2, presenter.ShownPresentations.Count);
    OverlayPresentation completed = presenter.ShownPresentations[1];
    Xunit.Assert.Equal(OverlayPlacementMode.FocusAnchor, completed.PlacementMode);
    Xunit.Assert.Equal(OverlayIndicatorKind.StatusIndicator, completed.IndicatorKind);
    Xunit.Assert.Equal(new ScreenBounds(140, 260, 1, 18), completed.Anchor?.Bounds);
  }

  [Xunit.Fact]
  public async Task ShowStateAsync_AnchoredRecording_FallsBackToCornerPanel_WhenAnchorUnavailable()
  {
    FakeOverlayPresenter presenter = new();
    FakeDiagnostics diagnostics = new();
    await using WindowsOverlayService service = new(
      presenter,
      OverlayServiceOptions.Default,
      new FakeOverlayAnchorProvider(OverlayAnchorSnapshot.Unavailable),
      diagnostics);

    await service.ShowStateAsync(
      DictationSessionState.Recording,
      display: OverlayDisplayOptions.AnchoredRecording);

    OverlayPresentation presentation = Xunit.Assert.Single(presenter.ShownPresentations);
    Xunit.Assert.Equal(OverlayPlacementMode.CornerPanel, presentation.PlacementMode);
    Xunit.Assert.Equal(OverlayIndicatorKind.StatusPanel, presentation.IndicatorKind);
    Xunit.Assert.Null(presentation.Anchor);
    Xunit.Assert.Contains(
      diagnostics.InfoMessages,
      message => message.Contains("decisionReason=anchor-unavailable-corner-fallback", StringComparison.Ordinal));
  }

  [Xunit.Fact]
  public async Task ShowStateAsync_AnchoredRecording_WithFallbackDisabled_HidesOverlayWhenNoAnchor()
  {
    FakeOverlayPresenter presenter = new();
    await using WindowsOverlayService service = new(
      presenter,
      OverlayServiceOptions.Default with
      {
        FallbackToCornerOverlay = false,
      },
      new FakeOverlayAnchorProvider(OverlayAnchorSnapshot.Unavailable));

    await service.ShowStateAsync(
      DictationSessionState.Recording,
      display: OverlayDisplayOptions.AnchoredRecording);

    Xunit.Assert.Empty(presenter.ShownPresentations);
    Xunit.Assert.Equal(0, presenter.HideCallCount);
  }

  [Xunit.Fact]
  public async Task ShowStateAsync_AnchoredRecording_WhenCaretIndicatorDisabled_LogsCornerFallbackReason()
  {
    FakeOverlayPresenter presenter = new();
    FakeDiagnostics diagnostics = new();
    await using WindowsOverlayService service = new(
      presenter,
      OverlayServiceOptions.Default with
      {
        CaretIndicatorEnabled = false,
      },
      new FakeOverlayAnchorProvider(
        new OverlayAnchorSnapshot(
          new ScreenBounds(120, 240, 1, 18),
          OverlayAnchorSource.Caret,
          UsedFallbackSource: false)),
      diagnostics);

    await service.ShowStateAsync(
      DictationSessionState.Recording,
      display: OverlayDisplayOptions.AnchoredRecording);

    OverlayPresentation presentation = Xunit.Assert.Single(presenter.ShownPresentations);
    Xunit.Assert.Equal(OverlayPlacementMode.CornerPanel, presentation.PlacementMode);
    Xunit.Assert.Contains(
      diagnostics.InfoMessages,
      message => message.Contains("decisionReason=caret-indicator-disabled", StringComparison.Ordinal));
  }

  private sealed class FakeOverlayPresenter : IOverlayPresenter
  {
    private readonly TaskCompletionSource<bool> firstHide = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<OverlayPresentation> ShownPresentations { get; } = new();
    public int HideCallCount { get; private set; }
    public Task FirstHide => firstHide.Task;

    public Task ShowAsync(OverlayPresentation presentation, CancellationToken cancellationToken = default)
    {
      ShownPresentations.Add(presentation);
      return Task.CompletedTask;
    }

    public Task HideAsync(CancellationToken cancellationToken = default)
    {
      HideCallCount++;
      firstHide.TrySetResult(true);
      return Task.CompletedTask;
    }
  }

  private sealed class FakeOverlayAnchorProvider : IOverlayAnchorProvider
  {
    private readonly Queue<OverlayAnchorSnapshot> anchors;

    public FakeOverlayAnchorProvider(params OverlayAnchorSnapshot[] anchors)
    {
      this.anchors = new Queue<OverlayAnchorSnapshot>(anchors);
    }

    public OverlayAnchorSnapshot GetCurrentAnchor()
    {
      return anchors.Count > 0
        ? anchors.Dequeue()
        : OverlayAnchorSnapshot.Unavailable;
    }
  }

  private sealed class FakeDiagnostics : IDiagnostics
  {
    public List<string> InfoMessages { get; } = new();

    public void Info(string message)
    {
      InfoMessages.Add(message);
    }

    public void Warning(string message)
    {
    }

    public void Error(string message, Exception? exception = null)
    {
    }
  }
}

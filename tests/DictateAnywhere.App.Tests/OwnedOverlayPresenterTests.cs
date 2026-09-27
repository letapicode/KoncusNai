using DictateAnywhere.Overlay;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;
using Xunit;
using System.Diagnostics;
using Xunit.Abstractions;

namespace DictateAnywhere.App.Tests;

public sealed class OwnedOverlayPresenterTests(ITestOutputHelper output)
{
  [Fact]
  public async Task PresentationCoordinationMeasurementWithIdenticalWarmPresenter()
  {
    const int iterations = 20000;
    FakePresenter direct = new();
    await using OwnedOverlayPresenter owned = new(new FakePresenter());
    OverlayPresentation recording = Presentation(OverlayVisualState.Recording);
    await owned.ShowAsync(recording);
    for (int warmup = 0; warmup < 1000; warmup++)
    { await direct.ShowAsync(recording); await owned.ShowAsync(recording); }
    for (int sample = 0; sample < 3; sample++)
    {
      Stopwatch timer = Stopwatch.StartNew();
      for (int i = 0; i < iterations; i++) await direct.ShowAsync(recording);
      double baseline = timer.Elapsed.TotalMilliseconds;
      timer.Restart();
      for (int i = 0; i < iterations; i++) await owned.ShowAsync(recording);
      output.WriteLine($"Sample {sample + 1}: direct={baseline / iterations * 1000:F3} us/call, owned={timer.Elapsed.TotalMilliseconds / iterations * 1000:F3} us/call ({iterations} warm no-op presentations). Not a device/inference latency measurement.");
    }
  }
  [Fact]
  public async Task NewRecordingRetiresOldTranscriptionAndItsDelayedHide()
  {
    FakePresenter old = new();
    FakePresenter current = new();
    await using OwnedOverlayPresenter first = new(old);
    await using OwnedOverlayPresenter second = new(current);
    await first.ShowAsync(Presentation(OverlayVisualState.Recording));
    await first.ShowAsync(Presentation(OverlayVisualState.Transcribing));
    await second.ShowAsync(Presentation(OverlayVisualState.Recording));
    Assert.False(old.Visible);
    Assert.True(current.Visible);
    await first.ShowAsync(Presentation(OverlayVisualState.Inserted));
    await first.HideAsync();
    Assert.Equal(2, old.Shows);
    Assert.True(current.Visible);
    await second.HideAsync();
    await first.ShowAsync(Presentation(OverlayVisualState.Error));
    Assert.False(old.Visible);
  }

  private static OverlayPresentation Presentation(OverlayVisualState state) => new(state, "", false, null,
    OverlayPlacementMode.CornerPanel, OverlayIndicatorKind.StatusPanel, OverlayAnimationState.None, null);

  private sealed class FakePresenter : IOverlayPresenter
  {
    public bool Visible { get; private set; }
    public int Shows { get; private set; }
    public Task ShowAsync(OverlayPresentation presentation, CancellationToken cancellationToken = default)
    { Visible = true; Shows++; return Task.CompletedTask; }
    public Task HideAsync(CancellationToken cancellationToken = default)
    { Visible = false; return Task.CompletedTask; }
  }
}

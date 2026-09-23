using DictateAnywhere.Core.Contracts;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace DictateAnywhere.Overlay.Tests;

public sealed class WindowsOverlayPresenterTests
{
  [Xunit.Fact]
  public void RecordingIndicator_IsAnIntentionalBoundedNonActivatingPill()
  {
    using OverlayIndicatorWindow window = new();
    OverlayPresentation recording = new(
      OverlayVisualState.Recording,
      "Recording...",
      ShowTimer: true,
      Elapsed: TimeSpan.Zero,
      OverlayPlacementMode.FocusAnchor,
      OverlayIndicatorKind.RecordingIndicator,
      OverlayAnimationState.Pulse,
      new OverlayAnchorSnapshot(new ScreenBounds(100, 100, 20, 20), OverlayAnchorSource.Caret, false));

    MethodInfo sizeMethod = typeof(OverlayIndicatorWindow).GetMethod(
      "GetIndicatorSize",
      BindingFlags.Instance | BindingFlags.NonPublic)
      ?? throw new InvalidOperationException("Recording indicator size policy was not found.");
    MethodInfo backgroundMethod = typeof(OverlayIndicatorWindow).GetMethod(
      "GetBackgroundColor",
      BindingFlags.Static | BindingFlags.NonPublic)
      ?? throw new InvalidOperationException("Recording indicator background policy was not found.");
    MethodInfo borderMethod = typeof(OverlayIndicatorWindow).GetMethod(
      "GetBorderColor",
      BindingFlags.Static | BindingFlags.NonPublic)
      ?? throw new InvalidOperationException("Recording indicator border policy was not found.");

    Xunit.Assert.Equal(new Size(84, 28), Xunit.Assert.IsType<Size>(sizeMethod.Invoke(window, [recording])));
    Xunit.Assert.NotEqual(
      Xunit.Assert.IsType<Color>(backgroundMethod.Invoke(null, [recording])),
      Xunit.Assert.IsType<Color>(borderMethod.Invoke(null, [recording])));
    Xunit.Assert.Equal(FormBorderStyle.None, window.FormBorderStyle);
    Xunit.Assert.False(window.ShowInTaskbar);
    Xunit.Assert.True(window.TopMost);
    Xunit.Assert.Empty(window.Controls.Cast<Control>());
    typeof(OverlayIndicatorWindow).GetField("presentation", BindingFlags.Instance | BindingFlags.NonPublic)!
      .SetValue(window, recording);
    using Bitmap bitmap = new(window.Width, window.Height);
    using Graphics graphics = Graphics.FromImage(bitmap);
    using PaintEventArgs args = new(graphics, new Rectangle(Point.Empty, bitmap.Size));
    typeof(OverlayIndicatorWindow).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!
      .Invoke(window, [args]);
    Color[] pixels = Enumerable.Range(0, bitmap.Width)
      .SelectMany(x => Enumerable.Range(0, bitmap.Height).Select(y => bitmap.GetPixel(x, y)))
      .ToArray();
    Xunit.Assert.Contains(pixels, color => color.R > 180 && color.R > color.G * 1.8 && color.R > color.B * 1.5);
    Xunit.Assert.Contains(pixels, color => color.R > 200 && color.G > 200 && color.B > 200);
  }

  [Xunit.Fact]
  public async Task ShowAsync_AnimatedTranscriptionAndCompletion_KeepPresenterAvailableForNextRecording()
  {
    await using WindowsOverlayPresenter presenter = new();
    OverlayAnchorSnapshot anchor = new(
      new ScreenBounds(100, 100, 400, 200),
      OverlayAnchorSource.FocusedElement,
      UsedFallbackSource: true);
    OverlayPresentation transcribing = new(
      OverlayVisualState.Transcribing,
      "Transcribing",
      ShowTimer: false,
      Elapsed: null,
      OverlayPlacementMode.FocusAnchor,
      OverlayIndicatorKind.TranscribingIndicator,
      OverlayAnimationState.LetterSpin,
      anchor);
    OverlayPresentation completed = new(
      OverlayVisualState.Inserted,
      string.Empty,
      ShowTimer: false,
      Elapsed: null,
      OverlayPlacementMode.FocusAnchor,
      OverlayIndicatorKind.CompletionIndicator,
      OverlayAnimationState.None,
      anchor);
    OverlayPresentation recording = new(
      OverlayVisualState.Recording,
      "Recording...",
      ShowTimer: true,
      Elapsed: TimeSpan.Zero,
      OverlayPlacementMode.FocusAnchor,
      OverlayIndicatorKind.RecordingIndicator,
      OverlayAnimationState.Pulse,
      anchor);

    await presenter.ShowAsync(transcribing).WaitAsync(TimeSpan.FromSeconds(2));
    await presenter.ShowAsync(completed).WaitAsync(TimeSpan.FromSeconds(2));
    await presenter.ShowAsync(recording).WaitAsync(TimeSpan.FromSeconds(2));
    await presenter.HideAsync().WaitAsync(TimeSpan.FromSeconds(2));
  }
}

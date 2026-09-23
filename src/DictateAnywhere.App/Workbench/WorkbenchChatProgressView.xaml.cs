using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DictateAnywhere.App.Workbench;

/// <summary>Owns the deterministic, low-motion chat progress lettering.</summary>
public partial class WorkbenchChatProgressView : UserControl
{
  private readonly List<RotateTransform> letterTransforms = [];
  private RotateTransform? activeTransform;
  private readonly DispatcherTimer timer;
  private Stopwatch? stopwatch;
  private FontFamily? progressFontFamily;
  private string? stage;
  private int animatedLetterIndex = -1;
  private bool presentationDisposed;

  public WorkbenchChatProgressView()
  {
    InitializeComponent();
    timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
    {
      Interval = TimeSpan.FromMilliseconds(100),
    };
    timer.Tick += OnTimerTick;
  }

  public void Start(FontFamily fontFamily)
  {
    ArgumentNullException.ThrowIfNull(fontFamily);
    ObjectDisposedException.ThrowIf(presentationDisposed, this);
    _ = Stop();
    progressFontFamily = fontFamily;
    stopwatch = Stopwatch.StartNew();
    timer.Start();
  }

  public TimeSpan Stop()
  {
    TimeSpan elapsed = stopwatch?.Elapsed ?? TimeSpan.Zero;
    stopwatch?.Stop();
    stopwatch = null;
    progressFontFamily = null;
    timer.Stop();
    Hide();
    return elapsed;
  }

  public void Update(TimeSpan elapsed, FontFamily fontFamily)
  {
    ArgumentNullException.ThrowIfNull(fontFamily);
    if (presentationDisposed || !ChatRequestProgress.ShouldShow(elapsed))
    {
      return;
    }

    Visibility = Visibility.Visible;
    SetStage(ChatRequestProgress.GetStage(elapsed), fontFamily);
    RotateNextLetter(elapsed);
  }

  public void Hide()
  {
    Visibility = Visibility.Collapsed;
    ClearAnimation();
  }

  internal int LetterCount => Letters.Children.Count;

  internal bool IsRunning => timer.IsEnabled;

  internal void DisposePresentation()
  {
    if (presentationDisposed)
    {
      return;
    }

    presentationDisposed = true;
    _ = Stop();
    timer.Tick -= OnTimerTick;
  }

  private void OnTimerTick(object? sender, EventArgs eventArgs)
  {
    if (stopwatch is not null && progressFontFamily is not null)
    {
      Update(stopwatch.Elapsed, progressFontFamily);
    }
  }

  private void SetStage(string nextStage, FontFamily fontFamily)
  {
    if (string.Equals(stage, nextStage, StringComparison.Ordinal))
    {
      return;
    }

    ClearAnimation();
    stage = nextStage;
    foreach (char character in nextStage)
    {
      RotateTransform? rotation = char.IsWhiteSpace(character) ? null : new RotateTransform();
      if (rotation is not null)
      {
        letterTransforms.Add(rotation);
      }

      Letters.Children.Add(new TextBlock
      {
        Text = character.ToString(),
        Style = TryFindResource("SubtleLabelStyle") as Style,
        FontFamily = fontFamily,
        Margin = new Thickness(0, 0, 1, 0),
        RenderTransformOrigin = new Point(0.5, 0.5),
        RenderTransform = rotation,
      });
    }
  }

  private void RotateNextLetter(TimeSpan elapsed)
  {
    if (!SystemParameters.ClientAreaAnimation)
    {
      return;
    }

    int letterIndex = ChatRequestProgress.GetLetterIndex(elapsed, letterTransforms.Count);
    if (letterIndex < 0 || letterIndex == animatedLetterIndex)
    {
      return;
    }

    activeTransform?.BeginAnimation(RotateTransform.AngleProperty, null);
    RotateTransform rotation = letterTransforms[letterIndex];
    rotation.BeginAnimation(
      RotateTransform.AngleProperty,
      new DoubleAnimation
      {
        From = 0d,
        To = 360d,
        Duration = ChatRequestProgress.LetterRotationDuration,
        FillBehavior = FillBehavior.Stop,
      });
    activeTransform = rotation;
    animatedLetterIndex = letterIndex;
  }

  private void ClearAnimation()
  {
    foreach (RotateTransform rotation in letterTransforms)
    {
      rotation.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    letterTransforms.Clear();
    Letters.Children.Clear();
    activeTransform = null;
    stage = null;
    animatedLetterIndex = -1;
  }
}

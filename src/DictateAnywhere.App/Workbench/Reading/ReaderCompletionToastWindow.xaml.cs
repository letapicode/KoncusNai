using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>A non-activating desktop confirmation shown when long reader work completes.</summary>
public partial class ReaderCompletionToastWindow : Window
{
  private readonly DispatcherTimer closeTimer = new() { Interval = TimeSpan.FromSeconds(8) };

  public ReaderCompletionToastWindow(string detail, string? title = null)
  {
    InitializeComponent();
    if (!string.IsNullOrWhiteSpace(title))
    {
      TitleTextBlock.Text = title;
    }

    DetailTextBlock.Text = detail;
    closeTimer.Tick += OnCloseTimerTick;
  }

  private void OnLoaded(object sender, RoutedEventArgs e)
  {
    Rect workArea = SystemParameters.WorkArea;
    Left = workArea.Right - Width - 22;
    Top = workArea.Bottom - Height - 22;
    if (!SystemParameters.ClientAreaAnimation)
    {
      ApplyStaticToastState();
      closeTimer.Start();
      return;
    }

    try
    {
      CubicEase entranceEase = new() { EasingMode = EasingMode.EaseOut };
      BeginAnimation(OpacityProperty, new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(1_000)));
      ToastSlideTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(72d, 0d, TimeSpan.FromMilliseconds(1_000))
      {
        EasingFunction = entranceEase,
      });
      ToastRevealTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(1_100)) { EasingFunction = entranceEase });
    }
    catch (InvalidOperationException)
    {
      // A completion notification must never be allowed to terminate a finished
      // audiobook job. Fall back to a static toast if desktop composition rejects animation.
      ApplyStaticToastState();
    }

    closeTimer.Start();
  }

  private void ApplyStaticToastState()
  {
    BeginAnimation(OpacityProperty, null);
    ToastSlideTransform.BeginAnimation(TranslateTransform.XProperty, null);
    ToastSlideTransform.BeginAnimation(TranslateTransform.YProperty, null);
    ToastScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
    ToastScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    ToastRevealTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
    ToastSlideTransform.X = 0;
    ToastSlideTransform.Y = 0;
    ToastScaleTransform.ScaleX = 1;
    ToastScaleTransform.ScaleY = 1;
    ToastRevealTransform.ScaleX = 1;
    Opacity = 1;
  }

  private void OnCloseTimerTick(object? sender, EventArgs e)
  {
    closeTimer.Stop();
    if (!SystemParameters.ClientAreaAnimation)
    {
      Close();
      return;
    }

    DoubleAnimation fade = new(1d, 0d, TimeSpan.FromMilliseconds(1_000))
    {
      EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
    };
    fade.Completed += (_, _) => Close();
    try
    {
      BeginAnimation(OpacityProperty, fade);
      ToastSlideTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0d, 72d, TimeSpan.FromMilliseconds(1_000))
      {
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
      });
    }
    catch (InvalidOperationException)
    {
      Close();
    }
  }

  protected override void OnClosed(EventArgs e)
  {
    closeTimer.Stop();
    closeTimer.Tick -= OnCloseTimerTick;
    base.OnClosed(e);
  }
}

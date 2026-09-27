using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench;

/// <summary>Owns operation, model-readiness, and session-status presentation.</summary>
public partial class WorkbenchOperationalStatusView : UserControl
{
  private readonly DispatcherTimer transientOutcomeTimer;
  private bool presentationDisposed;
  private long expiryTick;

  public WorkbenchOperationalStatusView()
  {
    InitializeComponent();
    transientOutcomeTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
    {
      Interval = DictationStatusMessages.TransientOutcomeDuration,
    };
    transientOutcomeTimer.Tick += OnTransientOutcomeTimerTick;
  }

  public event EventHandler? TransientOutcomeExpired;

  internal string SessionStatusText => SessionStatus.Text;

  internal bool IsTransientOutcomeVisible { get; private set; }

  internal void SetVisible(bool isVisible) =>
    Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

  internal void Render(WorkbenchOperationalStatusPresentation state)
  {
    ArgumentNullException.ThrowIfNull(state);
    SetVisible(state.IsVisible);
  }

  internal void SetModelReadiness(double progress, string detail)
  {
    ModelProgress.IsIndeterminate = false;
    ModelProgress.Value = progress;
    ModelReadiness.Text = string.Empty;
  }

  internal void SetModelReadinessText(string detail) =>
    ModelReadiness.Text = detail ?? string.Empty;

  internal void SetProgress(double? progress)
  {
    ModelProgress.IsIndeterminate = progress is null;
    if (progress is not null)
    {
      ModelProgress.Value = progress.Value;
    }
  }

  internal void StopProgress() => ModelProgress.IsIndeterminate = false;

  internal void SetCompactDictationFeedback(bool compact)
  {
    ModelProgress.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    ModelReadiness.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
  }

  internal void SetSessionStatus(string status, Brush foreground)
  {
    ArgumentNullException.ThrowIfNull(foreground);
    SessionStatus.Text = status ?? string.Empty;
    SessionStatus.Visibility = string.IsNullOrWhiteSpace(status) || string.Equals(status, "Idle", StringComparison.OrdinalIgnoreCase)
      ? Visibility.Collapsed : Visibility.Visible;
    SessionStatus.Foreground = foreground;
  }

  internal void SetModelDetail(string detail) => ModelDetail.Text = detail ?? string.Empty;

  internal void ShowTransientOutcome(bool autoExpire = true)
  {
    if (presentationDisposed)
    {
      return;
    }

    IsTransientOutcomeVisible = true;
    transientOutcomeTimer.Stop();
    if (autoExpire)
    {
      expiryTick = Environment.TickCount64 + (long)transientOutcomeTimer.Interval.TotalMilliseconds;
      transientOutcomeTimer.Start();
    }
  }

  internal void ClearTransientOutcome()
  {
    transientOutcomeTimer.Stop();
    IsTransientOutcomeVisible = false;
    SessionStatus.Text = string.Empty;
  }

  internal void DisposePresentation()
  {
    if (presentationDisposed)
    {
      return;
    }

    presentationDisposed = true;
    ClearTransientOutcome();
    transientOutcomeTimer.Tick -= OnTransientOutcomeTimerTick;
  }

  internal bool IsTransientOutcomeTimerRunning => transientOutcomeTimer.IsEnabled;

  internal void ExpireTransientOutcome()
  {
    if (presentationDisposed)
    {
      return;
    }

    ClearTransientOutcome();
    TransientOutcomeExpired?.Invoke(this, EventArgs.Empty);
  }

  private void OnTransientOutcomeTimerTick(object? sender, EventArgs args)
  {
    // A previously queued tick must not expire a replacement or persistent notice.
    if (transientOutcomeTimer.IsEnabled && Environment.TickCount64 >= expiryTick) ExpireTransientOutcome();
  }

}

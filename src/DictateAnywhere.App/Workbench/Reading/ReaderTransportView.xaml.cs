using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DictateAnywhere.App.Workbench.Reading;

internal enum ReaderTransportIntent
{
  Previous,
  PlayPause,
  Next,
  Edit,
  ToggleDistractionFree,
}

/// <summary>Owns Reading Studio transport rendering, seek suppression, and typed transport intent.</summary>
public partial class ReaderTransportView : UserControl
{
  private bool applyingTimeline;
  private TimeSpan duration;

  public ReaderTransportView()
  {
    InitializeComponent();
    PreviousButton.Click += (_, _) => IntentRequested?.Invoke(ReaderTransportIntent.Previous);
    PlayPauseButton.Click += (_, _) => IntentRequested?.Invoke(ReaderTransportIntent.PlayPause);
    NextButton.Click += (_, _) => IntentRequested?.Invoke(ReaderTransportIntent.Next);
    EditDocumentButton.Click += (_, _) => IntentRequested?.Invoke(ReaderTransportIntent.Edit);
    DistractionFreeButton.Click += (_, _) => IntentRequested?.Invoke(ReaderTransportIntent.ToggleDistractionFree);
    SeekSlider.ValueChanged += OnSeekValueChanged;
    SeekSlider.PreviewMouseLeftButtonDown += OnSeekSliderPreviewMouseLeftButtonDown;
  }

  internal event Action<ReaderTransportIntent>? IntentRequested;
  internal event Action<TimeSpan>? SeekRequested;

  internal void Render(ReaderTransportPresentation state)
  {
    ArgumentNullException.ThrowIfNull(state);
    Visibility = state.IsVisible ? Visibility.Visible : Visibility.Collapsed;
    PreviousButton.IsEnabled = state.CanPrevious;
    NextButton.IsEnabled = state.CanNext;
    PlayPauseButton.IsEnabled = state.CanPlay;
    PlayPauseButton.Content = state.IsPlaying ? "Pause" : "Play";
    AutomationProperties.SetName(PlayPauseButton, state.IsPlaying ? "Pause" : "Play");
    EditDocumentButton.Visibility = state.IsDraft ? Visibility.Collapsed : Visibility.Visible;
    EditDocumentButton.IsEnabled = state.CanEdit;
    PlaybackStatusTextBlock.Text = state.Status;
    DocumentMetaTextBlock.Text = state.DocumentMetadata;
    DocumentProgressBar.Value = state.DocumentProgress;
    DocumentProgressTextBlock.Text = $"{state.DocumentProgress:P0}";
    SectionStatusTextBlock.Text = state.SectionCount > 0
      ? $"Section {state.SectionNumber:N0} of {state.SectionCount:N0}"
      : string.Empty;
    duration = state.Duration;
    applyingTimeline = true;
    try
    {
      SeekSlider.Maximum = Math.Max(1d, state.Duration.TotalSeconds);
      SeekSlider.Value = Math.Clamp(state.Position.TotalSeconds, 0d, SeekSlider.Maximum);
    }
    finally
    {
      applyingTimeline = false;
    }
    ElapsedTimeTextBlock.Text = FormatPlaybackTime(state.Position);
    DurationTextBlock.Text = FormatPlaybackTime(state.Duration);
  }

  private void OnSeekValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
  {
    if (!applyingTimeline && duration > TimeSpan.Zero)
    {
      SeekRequested?.Invoke(TimeSpan.FromSeconds(Math.Clamp(SeekSlider.Value, 0d, duration.TotalSeconds)));
    }
  }

  private void OnSeekSliderPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
  {
    if (duration <= TimeSpan.Zero || FindVisualAncestor<Thumb>(e.OriginalSource as DependencyObject) is not null)
    {
      return;
    }
    double ratio = Math.Clamp(e.GetPosition(SeekSlider).X / Math.Max(1d, SeekSlider.ActualWidth), 0d, 1d);
    SeekRequested?.Invoke(TimeSpan.FromTicks((long)Math.Round(duration.Ticks * ratio)));
    e.Handled = true;
  }

  private static T? FindVisualAncestor<T>(DependencyObject? source) where T : DependencyObject
  {
    for (DependencyObject? current = source; current is not null; current = VisualTreeHelper.GetParent(current))
    {
      if (current is T match) return match;
    }
    return null;
  }

  internal static string FormatPlaybackTime(TimeSpan value) => value.TotalHours >= 1
    ? $"{(int)value.TotalHours}:{value.Minutes:D2}:{value.Seconds:D2}"
    : $"{(int)value.TotalMinutes}:{value.Seconds:D2}";
}

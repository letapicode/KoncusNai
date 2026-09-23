using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace DictateAnywhere.App.Workbench.Reading;

internal enum ReaderSidebarIntent
{
  OpenDocument,
  ShowHelp,
  ToggleVoicePreview,
  Prepare,
  ExportAudio,
  ExportVideo,
  Publish,
}

internal enum ReaderSidebarSelectionChange
{
  Range,
  Narration,
  Appearance,
  PlaybackSpeed,
  VideoFormat,
  PreparationMode,
}

internal sealed record ReaderSidebarSelection(
  ReaderSectionOption StartSection,
  ReaderSectionOption EndSection,
  ReaderLanguageOption Language,
  ReaderVoiceOption Voice,
  ReaderPreparationOption Preparation,
  ReaderFollowAlongOption FollowAlong,
  ReaderHighlightVisualOption HighlightStyle,
  ReaderHighlightColorOption HighlightColor,
  ReaderFontOption Font,
  ReaderThemeOption Theme,
  double FontSize,
  double Speed,
  ReaderVideoFormatOption VideoFormat)
{
  public ReaderNarrationProfile NarrationProfile => ReaderNarrationProfile.Create(Language, Voice);
  public ReadingHighlightMode HighlightMode => FollowAlong.HighlightMode;
  public ReaderViewMode ViewMode => FollowAlong.ViewMode;
  public ReaderTypographyMetrics Typography => ReaderTypographyCatalog.GetMetrics(Language.Capability.Text.LineMetrics);
  public ReaderVideoCaptionStyle VideoCaptionStyle => HighlightStyle.Style switch
  {
    ReaderHighlightVisualStyle.BoldFocus => ReaderVideoCaptionStyle.KineticBold,
    ReaderHighlightVisualStyle.AccentFill => ReaderVideoCaptionStyle.FocusPill,
    _ => ReaderVideoCaptionStyle.ReaderPage,
  };
}

/// <summary>Owns Reading Studio control selection, suppression, preview media, and intent forwarding.</summary>
public partial class ReaderSidebarView : UserControl, IDisposable
{
  private readonly MediaPlayer voicePreviewPlayer = new();
  private bool synchronizingSelections;
  private bool disposed;

  public ReaderSidebarView()
  {
    InitializeComponent();
    OpenDocumentButton.Click += (_, _) => IntentRequested?.Invoke(ReaderSidebarIntent.OpenDocument);
    ReadingHelpButton.Click += (_, _) => IntentRequested?.Invoke(ReaderSidebarIntent.ShowHelp);
    VoicePreviewButton.Click += (_, _) => IntentRequested?.Invoke(ReaderSidebarIntent.ToggleVoicePreview);
    PrepareRangeButton.Click += (_, _) => IntentRequested?.Invoke(ReaderSidebarIntent.Prepare);
    ExportAudioButton.Click += (_, _) => IntentRequested?.Invoke(ReaderSidebarIntent.ExportAudio);
    ExportVideoButton.Click += (_, _) => IntentRequested?.Invoke(ReaderSidebarIntent.ExportVideo);
    PublishYouTubeButton.Click += (_, _) => IntentRequested?.Invoke(ReaderSidebarIntent.Publish);
    StartSectionComboBox.SelectionChanged += OnRangeChanged;
    EndSectionComboBox.SelectionChanged += OnRangeChanged;
    LanguageComboBox.SelectionChanged += OnLanguageChanged;
    VoiceComboBox.SelectionChanged += (_, _) => RaiseSelectionChanged(ReaderSidebarSelectionChange.Narration);
    PreparationModeToggleButton.Click += (_, _) =>
    {
      UpdatePreparationModeAccessibility();
      RaiseSelectionChanged(ReaderSidebarSelectionChange.PreparationMode);
    };
    FollowAlongComboBox.SelectionChanged += (_, _) => RaiseSelectionChanged(ReaderSidebarSelectionChange.Appearance);
    HighlightStyleComboBox.SelectionChanged += (_, _) => RaiseSelectionChanged(ReaderSidebarSelectionChange.Appearance);
    HighlightColorListBox.SelectionChanged += (_, _) => RaiseSelectionChanged(ReaderSidebarSelectionChange.Appearance);
    FontComboBox.SelectionChanged += (_, _) => RaiseSelectionChanged(ReaderSidebarSelectionChange.Appearance);
    ThemeListBox.SelectionChanged += (_, _) => RaiseSelectionChanged(ReaderSidebarSelectionChange.Appearance);
    FontSizeSlider.ValueChanged += (_, _) =>
    {
      FontSizeTextBlock.Text = $"{FontSizeSlider.Value:0} pt";
      RaiseSelectionChanged(ReaderSidebarSelectionChange.Appearance);
    };
    SpeedSlider.ValueChanged += (_, _) =>
    {
      SpeedTextBlock.Text = $"{SpeedSlider.Value:0.00}×";
      RaiseSelectionChanged(ReaderSidebarSelectionChange.PlaybackSpeed);
    };
    VideoFormatComboBox.SelectionChanged += (_, _) =>
    {
      UpdateVideoHint();
      RaiseSelectionChanged(ReaderSidebarSelectionChange.VideoFormat);
    };
    voicePreviewPlayer.MediaEnded += OnPreviewEnded;
    voicePreviewPlayer.MediaFailed += OnPreviewFailed;
    InitializeChoices();
  }

  internal event Action<ReaderSidebarIntent>? IntentRequested;
  internal event Action<ReaderSidebarSelectionChange, ReaderSidebarSelection>? SelectionChanged;
  internal event EventHandler? VoicePreviewEnded;
  internal event EventHandler<Exception>? VoicePreviewFailed;

  internal ReaderSidebarSelection Selection => new(
    StartSectionComboBox.SelectedItem as ReaderSectionOption ?? new ReaderSectionOption(0, "Section 1"),
    EndSectionComboBox.SelectedItem as ReaderSectionOption ?? StartSectionComboBox.SelectedItem as ReaderSectionOption ?? new ReaderSectionOption(0, "Section 1"),
    LanguageComboBox.SelectedItem as ReaderLanguageOption ?? ReaderLanguageRegistry.DefaultLanguage,
    VoiceComboBox.SelectedItem as ReaderVoiceOption ?? (LanguageComboBox.SelectedItem as ReaderLanguageOption ?? ReaderLanguageRegistry.DefaultLanguage).DefaultVoice,
    PreparationModeToggleButton.IsChecked == true ? ReaderPreparationOption.Defaults[1] : ReaderPreparationOption.Defaults[0],
    FollowAlongComboBox.SelectedItem as ReaderFollowAlongOption ?? ReaderFollowAlongOption.Defaults[1],
    HighlightStyleComboBox.SelectedItem as ReaderHighlightVisualOption ?? ReaderHighlightVisualOption.Defaults[0],
    HighlightColorListBox.SelectedItem as ReaderHighlightColorOption ?? ReaderHighlightColorOption.Defaults[0],
    FontComboBox.SelectedItem as ReaderFontOption ?? ReaderTypographyCatalog.GetDefault(ReaderLanguageRegistry.DefaultLanguage.Capability.Text.Font),
    ThemeListBox.SelectedItem as ReaderThemeOption ?? DefaultTheme,
    FontSizeSlider.Value > 0 ? FontSizeSlider.Value : 23d,
    SpeedSlider.Value > 0 ? SpeedSlider.Value : 1d,
    VideoFormatComboBox.SelectedItem as ReaderVideoFormatOption ?? ReaderVideoFormatOption.Defaults[0]);

  internal static ReaderThemeOption DefaultTheme => ReaderThemeOption.Defaults.Single(option => option.DisplayName == "Midnight");

  internal void SetSections(IReadOnlyList<ReaderSectionOption> options, int startIndex, int endIndex)
  {
    ArgumentNullException.ThrowIfNull(options);
    if (options.Count == 0)
    {
      throw new ArgumentException("At least one section is required.", nameof(options));
    }

    SectionRangeExpander.Visibility = options.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    ApplySelection(() =>
    {
      StartSectionComboBox.ItemsSource = options;
      EndSectionComboBox.ItemsSource = options;
      StartSectionComboBox.SelectedIndex = Math.Clamp(startIndex, 0, options.Count - 1);
      EndSectionComboBox.SelectedIndex = Math.Clamp(endIndex, StartSectionComboBox.SelectedIndex, options.Count - 1);
    });
  }

  internal void Render(ReaderSidebarPresentation state)
  {
    ArgumentNullException.ThrowIfNull(state);
    StartSectionComboBox.IsEnabled = state.CanSelectRange;
    EndSectionComboBox.IsEnabled = state.CanSelectRange;
    PrepareRangeButton.IsEnabled = state.CanPrepare;
    PrepareRangeButton.Content = state.IsPreparing ? "Creating narration…" : "Create narration";
    OpenDocumentButton.IsEnabled = state.CanInteract;
    ReadingHelpButton.IsEnabled = state.CanInteract;
    LanguageComboBox.IsEnabled = state.CanInteract;
    VoiceComboBox.IsEnabled = state.CanInteract;
    VoicePreviewButton.IsEnabled = state.CanInteract;
    PreparationModeToggleButton.IsEnabled = state.CanInteract;
    FollowAlongComboBox.IsEnabled = state.CanEditAppearance;
    HighlightStyleComboBox.IsEnabled = state.CanEditAppearance;
    HighlightColorListBox.IsEnabled = state.CanEditAppearance;
    FontComboBox.IsEnabled = state.CanEditAppearance;
    ThemeListBox.IsEnabled = state.CanEditAppearance;
    FontSizeSlider.IsEnabled = state.CanEditAppearance;
    SpeedSlider.IsEnabled = state.CanEditAppearance;
    VideoFormatComboBox.IsEnabled = state.CanInteract;
    ExportControls.Visibility = state.CanUsePreparedRange ? Visibility.Visible : Visibility.Collapsed;
    ExportReadinessText.Visibility = state.CanUsePreparedRange ? Visibility.Collapsed : Visibility.Visible;
    ExportAudioButton.IsEnabled = state.CanUsePreparedRange;
    ExportVideoButton.IsEnabled = state.CanUsePreparedRange;
    PublishYouTubeButton.IsEnabled = state.CanUsePreparedRange;
  }

  internal void RenderVoicePreview(string message, bool isBusy, bool canStop)
  {
    VoicePreviewStatusTextBlock.Text = message;
    VoicePreviewProgressBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
    VoicePreviewStatusPanel.Visibility = string.IsNullOrWhiteSpace(message) && !isBusy
      ? Visibility.Collapsed
      : Visibility.Visible;
    string label = canStop ? "Stop voice preview" : "Preview voice";
    AutomationProperties.SetName(VoicePreviewButton, label);
    VoicePreviewGlyph.Data = Geometry.Parse(canStop
      ? "M5,5 H10 V19 H5 Z M14,5 H19 V19 H14 Z"
      : "M5,3 L18,12 L5,21 Z");
  }

  internal void PlayVoicePreview(string path)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    voicePreviewPlayer.Open(new Uri(path, UriKind.Absolute));
    voicePreviewPlayer.Play();
  }

  internal void StopVoicePreview() => voicePreviewPlayer.Stop();

  internal void ApplyThemeSelection(ReaderThemeOption theme)
  {
    ArgumentNullException.ThrowIfNull(theme);
    ApplySelection(() => ThemeListBox.SelectedItem = ReaderThemeOption.Defaults.Single(option => option.DisplayName == theme.DisplayName));
  }

  public void Dispose()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    voicePreviewPlayer.MediaEnded -= OnPreviewEnded;
    voicePreviewPlayer.MediaFailed -= OnPreviewFailed;
    voicePreviewPlayer.Close();
  }

  private void InitializeChoices()
  {
    ApplySelection(() =>
    {
      LanguageComboBox.ItemsSource = ReaderLanguageRegistry.Languages;
      LanguageComboBox.SelectedItem = ReaderLanguageRegistry.DefaultLanguage;
      SynchronizeLanguageChoices(ReaderLanguageRegistry.DefaultLanguage);
      ThemeListBox.ItemsSource = ReaderThemeOption.Defaults;
      ThemeListBox.SelectedItem = DefaultTheme;
      PreparationModeToggleButton.IsChecked = false;
      VideoFormatComboBox.ItemsSource = ReaderVideoFormatOption.Defaults;
      VideoFormatComboBox.SelectedIndex = 0;
      FollowAlongComboBox.ItemsSource = ReaderFollowAlongOption.Defaults;
      FollowAlongComboBox.SelectedItem = ReaderFollowAlongOption.Defaults.First(option => option.DisplayName == "Word on Page");
      HighlightStyleComboBox.ItemsSource = ReaderHighlightVisualOption.Defaults;
      HighlightStyleComboBox.SelectedIndex = 0;
      HighlightColorListBox.ItemsSource = ReaderHighlightColorOption.Defaults;
      HighlightColorListBox.SelectedIndex = 0;
    });
    UpdatePreparationModeAccessibility();
    UpdateVideoHint();
    FontSizeTextBlock.Text = $"{FontSizeSlider.Value:0} pt";
    SpeedTextBlock.Text = $"{SpeedSlider.Value:0.00}×";
  }

  private void OnRangeChanged(object sender, SelectionChangedEventArgs e)
  {
    if (synchronizingSelections
        || StartSectionComboBox.SelectedItem is not ReaderSectionOption start
        || EndSectionComboBox.SelectedItem is not ReaderSectionOption end)
    {
      return;
    }

    if (start.Index > end.Index)
    {
      ApplySelection(() => EndSectionComboBox.SelectedIndex = start.Index);
    }
    RaiseSelectionChanged(ReaderSidebarSelectionChange.Range);
  }

  private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
  {
    if (synchronizingSelections || LanguageComboBox.SelectedItem is not ReaderLanguageOption language)
    {
      return;
    }

    ApplySelection(() => SynchronizeLanguageChoices(language));
    RaiseSelectionChanged(ReaderSidebarSelectionChange.Narration);
  }

  private void SynchronizeLanguageChoices(ReaderLanguageOption language)
  {
    ReaderFontOption? currentFont = FontComboBox.SelectedItem as ReaderFontOption;
    IReadOnlyList<ReaderFontOption> fonts = ReaderTypographyCatalog.GetOptions(language.Capability.Text.Font);
    VoiceComboBox.ItemsSource = language.Voices;
    VoiceComboBox.SelectedItem = language.DefaultVoice;
    FontComboBox.ItemsSource = fonts;
    FontComboBox.SelectedItem = currentFont is not null && currentFont.Supports(language.Capability.Text.Font)
      ? fonts.Single(option => option.Id == currentFont.Id)
      : ReaderTypographyCatalog.GetDefault(language.Capability.Text.Font);
  }

  private void UpdatePreparationModeAccessibility()
  {
    bool fullRange = PreparationModeToggleButton.IsChecked == true;
    string mode = fullRange ? "Full Range" : "Start Sooner";
    string explanation = fullRange
      ? "Full range prepares every selected section before playback. Click to start sooner instead."
      : "Start sooner prepares one section now and the next while you listen. Click to prepare the full range instead.";
    AutomationProperties.SetName(PreparationModeToggleButton, $"Processing mode: {mode}. {explanation}");
  }

  private void UpdateVideoHint()
  {
    bool shortVideo = (VideoFormatComboBox.SelectedItem as ReaderVideoFormatOption)?.Format == ReaderVideoFormat.YouTubeShort;
    VideoExportHintTextBlock.Text = shortVideo
      ? "Vertical 1080 × 1920. It uses your reader style with synchronized highlights inside mobile safe areas."
      : "High-quality 2560 × 1440 export using your selected reader style.";
  }

  private void RaiseSelectionChanged(ReaderSidebarSelectionChange change)
  {
    if (!synchronizingSelections && !disposed)
    {
      SelectionChanged?.Invoke(change, Selection);
    }
  }

  private void ApplySelection(Action action)
  {
    synchronizingSelections = true;
    try
    {
      action();
    }
    finally
    {
      synchronizingSelections = false;
    }
  }

  private void OnPreviewEnded(object? sender, EventArgs e) => VoicePreviewEnded?.Invoke(this, EventArgs.Empty);

  private void OnPreviewFailed(object? sender, ExceptionEventArgs e) =>
    VoicePreviewFailed?.Invoke(this, e.ErrorException ?? new InvalidOperationException("The voice preview could not be played."));
}

internal sealed record ReaderSectionOption(int Index, string DisplayName)
{
  public override string ToString() => DisplayName;
}

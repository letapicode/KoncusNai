using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DictateAnywhere.App.Presentation;

namespace DictateAnywhere.App.Workbench;

/// <summary>Owns Workbench composer presentation and forwards composer intent.</summary>
public partial class WorkbenchComposerView : UserControl
{
  private bool isApplyingPrompt;
  private bool expansionRefreshQueued;
  private bool expansionOverlayVisible;
  private bool promptKeyboardFocusVisible;
  private bool presentationDisposed;
  private long promptRevision;
  private DispatcherOperation? expansionRefreshOperation;

  public WorkbenchComposerView()
  {
    InitializeComponent();
    NewChatButton.Click += (_, args) => NewChatClicked?.Invoke(this, args);
    AddFile.Click += (_, args) => AddFileClicked?.Invoke(this, args);
    ReadDocument.Click += (_, args) => ReadDocumentClicked?.Invoke(this, args);
    ExportChatButton.Click += (_, args) => ExportChatClicked?.Invoke(this, args);
    StopSpeaking.Click += (_, args) => StopSpeakingClicked?.Invoke(this, args);
    Prompt.KeyDown += (_, args) => PromptKeyDown?.Invoke(this, args);
    Prompt.PreviewKeyDown += OnPromptPreviewKeyDown;
    Prompt.PreviewMouseDown += OnPromptPreviewMouseDown;
    Prompt.GotKeyboardFocus += OnPromptGotKeyboardFocus;
    Prompt.LostKeyboardFocus += OnPromptLostKeyboardFocus;
    Prompt.TextChanged += OnPromptTextChanged;
    ExpandPrompt.Click += (_, args) => ExpandPromptClicked?.Invoke(this, args);
    Record.Click += (_, args) => RecordClicked?.Invoke(this, args);
    StopDictation.Click += (_, args) => StopDictationClicked?.Invoke(this, args);
    Send.Click += (_, args) => SendClicked?.Invoke(this, args);
    StopChat.Click += (_, args) => StopChatClicked?.Invoke(this, args);
  }

  public event RoutedEventHandler? NewChatClicked;
  public event RoutedEventHandler? AddFileClicked;
  public event RoutedEventHandler? ReadDocumentClicked;
  public event RoutedEventHandler? ExportChatClicked;
  public event RoutedEventHandler? StopSpeakingClicked;
  public event KeyEventHandler? PromptKeyDown;
  public event TextChangedEventHandler? PromptTextChanged;
  public event RoutedEventHandler? ExpandPromptClicked;
  public event RoutedEventHandler? RecordClicked;
  public event RoutedEventHandler? StopDictationClicked;
  public event RoutedEventHandler? SendClicked;
  public event RoutedEventHandler? StopChatClicked;
  public event RoutedEventHandler? RemovePendingFileClicked;

  internal string PromptText
  {
    get => Prompt.Text;
    set
    {
      isApplyingPrompt = true;
      try
      {
        Prompt.Text = value ?? string.Empty;
      }
      finally
      {
        isApplyingPrompt = false;
      }
    }
  }

  internal bool HasPrompt => !string.IsNullOrWhiteSpace(Prompt.Text);

  internal long PromptRevision => promptRevision;

  internal bool ArePendingFilesVisible => PendingFiles.Visibility == Visibility.Visible;

  internal int PendingFileCount => PendingFiles.Items.Count;

  internal bool IsSpeechPreparationVisible => SpeechPreparationStatus.Visibility == Visibility.Visible;

  internal string SpeechPreparationStatusText => SpeechPreparationText.Text;

  internal double PromptFontSize => Prompt.FontSize;

  internal double PlaceholderFontSize => PromptPlaceholder.FontSize;

  internal bool IsCompactComposerVisible => CompactComposer.Visibility == Visibility.Visible;

  internal bool IsPromptKeyboardFocusVisible => promptKeyboardFocusVisible;

  internal string HotkeyStatusTextValue => HotkeyStatusText.Text;

  internal void SetHotkeyStatus(string status) => HotkeyStatusText.Text = status ?? string.Empty;

  internal void ClearPrompt() => Prompt.Clear();

  internal void FocusPromptAtEnd()
  {
    Prompt.CaretIndex = Prompt.Text.Length;
    Prompt.ScrollToEnd();
    Prompt.Focus();
  }

  internal void SetPendingFiles(IReadOnlyList<ChatFileAttachment> files)
  {
    ArgumentNullException.ThrowIfNull(files);
    PendingFiles.ItemsSource = files.ToArray();
    PendingFiles.Visibility = files.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
  }

  internal void SetSpeechPreparation(bool isVisible, string status)
  {
    SpeechPreparationStatus.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    if (isVisible)
    {
      SpeechPreparationText.Text = status ?? string.Empty;
    }
  }

  internal void ApplyTypography(double fontSize, FontFamily fontFamily)
  {
    ArgumentNullException.ThrowIfNull(fontFamily);
    Prompt.FontSize = fontSize;
    Prompt.FontFamily = fontFamily;
    PromptPlaceholder.FontSize = fontSize;
    PromptPlaceholder.FontFamily = fontFamily;
  }

  internal void SetCompactComposerVisible(bool isVisible) =>
    SetCompactComposerVisibility(isVisible);

  private void SetCompactComposerVisibility(bool isVisible)
  {
    CompactComposer.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    if (!isVisible)
    {
      SetPromptKeyboardFocusVisible(false);
    }
  }

  internal void RefreshExpansionButton(bool expansionOverlayVisible)
  {
    bool shouldOffer = !expansionOverlayVisible
      && PromptExpansionPolicy.ShouldOfferExpansion(Prompt.Text, Prompt.LineCount);
    ExpandPrompt.Visibility = shouldOffer ? Visibility.Visible : Visibility.Collapsed;
  }

  internal void QueueExpansionButtonRefresh(bool overlayVisible)
  {
    expansionOverlayVisible = overlayVisible;
    if (presentationDisposed || expansionRefreshQueued)
    {
      return;
    }

    expansionRefreshQueued = true;
    expansionRefreshOperation = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
    {
      expansionRefreshOperation = null;
      expansionRefreshQueued = false;
      if (!presentationDisposed)
      {
        RefreshExpansionButton(expansionOverlayVisible);
      }
    }));
  }

  internal void StartProgress(FontFamily fontFamily) => ProgressView.Start(fontFamily);

  internal void SetPaperView(bool enabled)
  {
    DictateAnywhere.App.Presentation.PaperChatResources.Apply(this, enabled);
    Prompt.Resources.Remove("Brush.Control.InputDisabled");
    bool sharePaper = enabled && !WindowThemeBehavior.GetIsHighContrastActive(this);
    if (sharePaper) CompactComposer.Background = Brushes.Transparent;
    else CompactComposer.SetResourceReference(Border.BackgroundProperty,
      enabled ? "Brush.Paper.Composer" : "Brush.Surface.Composer");
    CompactComposer.SetResourceReference(Border.BorderBrushProperty,
      enabled ? "Brush.Paper.Border" : "Brush.Border.Subtle");
    Prompt.SetResourceReference(Control.ForegroundProperty,
      enabled ? "Brush.Paper.Ink" : "Brush.Text.Primary");
    if (sharePaper)
    {
      Prompt.Background = Brushes.Transparent;
      Prompt.Resources["Brush.Control.InputDisabled"] = Brushes.Transparent;
    }
    else Prompt.SetResourceReference(Control.BackgroundProperty,
      enabled ? "Brush.Paper.Composer" : "Brush.Control.Input");
    PromptPlaceholder.SetResourceReference(TextBlock.ForegroundProperty,
      enabled ? "Brush.Paper.Comment" : "Brush.Text.Secondary");
  }

  internal TimeSpan StopProgress() => ProgressView.Stop();

  internal void DisposePresentation()
  {
    if (presentationDisposed)
    {
      return;
    }

    presentationDisposed = true;
    expansionRefreshOperation?.Abort();
    expansionRefreshOperation = null;
    expansionRefreshQueued = false;
    SetPromptKeyboardFocusVisible(false);
    ProgressView.DisposePresentation();
  }

  internal void SetChatActionState(
    bool chatIsBusy,
    bool canCancelChat,
    bool isImportingFiles,
    bool canEditPrompt,
    bool canInteract,
    bool canSend,
    bool canUseQuickLocalReply,
    bool isPreparingSpeech,
    bool canStopSpeech)
  {
    bool hasPrompt = HasPrompt;
    Prompt.IsEnabled = canEditPrompt;
    PromptPlaceholder.Visibility = hasPrompt ? Visibility.Collapsed : Visibility.Visible;
    Send.IsEnabled = canInteract && canSend && hasPrompt;
    Send.Visibility = !chatIsBusy && hasPrompt ? Visibility.Visible : Visibility.Collapsed;
    Send.ToolTip = canUseQuickLocalReply
      ? "Send quick local greeting"
      : "Submit (checks model readiness)";
    StopChat.IsEnabled = canCancelChat;
    StopChat.Visibility = canCancelChat ? Visibility.Visible : Visibility.Collapsed;
    ReadDocument.IsEnabled = !isPreparingSpeech;
    StopSpeaking.IsEnabled = canStopSpeech;
    StopSpeaking.Visibility = canStopSpeech ? Visibility.Visible : Visibility.Collapsed;
  }

  internal void Render(WorkbenchComposerPresentation state, WorkbenchViewModel dictationViewModel)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(dictationViewModel);
    SetCompactComposerVisible(!state.IsExpanded);
    SetDictationState(dictationViewModel, state.IsRecording);
    SetConversationActionState(state.CanStartNewChat, state.CanExportChat);
    SetAddFileEnabled(state.CanAddFile);
    SetChatActionState(
      state.IsChatBusy,
      state.CanCancelChat,
      state.IsImportingFiles,
      state.CanEditPrompt,
      state.CanInteractWithChat,
      state.CanSend,
      state.CanUseQuickLocalReply,
      state.IsPreparingSpeech,
      state.CanStopSpeech);
    QueueExpansionButtonRefresh(state.IsExpanded);
  }

  internal void SetDictationState(
    WorkbenchViewModel viewModel,
    bool isRecording)
  {
    ArgumentNullException.ThrowIfNull(viewModel);
    HotkeyStatusText.Text = viewModel.HotkeyStatusText;
    Record.IsEnabled = viewModel.RecordEnabled || viewModel.StopEnabled;
    RecordIcon.Text = isRecording ? "\uE71A" : "\uE720";
    Record.ToolTip = isRecording ? "Stop dictation" : "Dictate";
    StopDictation.IsEnabled = viewModel.StopEnabled;
  }

  internal void SetAddFileEnabled(bool isEnabled) => AddFile.IsEnabled = isEnabled;

  internal void SetConversationActionState(bool canStartNewChat, bool canExportChat)
  {
    NewChatButton.IsEnabled = canStartNewChat;
    ExportChatButton.IsEnabled = canExportChat;
    ExportChatButton.Visibility = canExportChat ? Visibility.Visible : Visibility.Collapsed;
  }

  private void OnRemovePendingFileClicked(object sender, RoutedEventArgs e) =>
    RemovePendingFileClicked?.Invoke(sender, e);

  private void OnPromptTextChanged(object sender, TextChangedEventArgs args)
  {
    promptRevision++;
    PromptPlaceholder.Visibility = HasPrompt ? Visibility.Collapsed : Visibility.Visible;
    if (!isApplyingPrompt)
    {
      PromptTextChanged?.Invoke(this, args);
    }
  }

  private void OnPromptGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs args) =>
    UpdatePromptFocusPresentation(InputManager.Current.MostRecentInputDevice);

  private void OnPromptLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs args) =>
    SetPromptKeyboardFocusVisible(false);

  private void OnPromptPreviewMouseDown(object sender, MouseButtonEventArgs args) =>
    SetPromptKeyboardFocusVisible(false);

  private void OnPromptPreviewKeyDown(object sender, KeyEventArgs args)
  {
    if (ShouldRevealPromptFocusForKey(args.Key, args.SystemKey, Keyboard.Modifiers))
    {
      SetPromptKeyboardFocusVisible(true);
    }
  }

  internal void UpdatePromptFocusPresentation(InputDevice? mostRecentInputDevice) =>
    SetPromptKeyboardFocusVisible(mostRecentInputDevice is KeyboardDevice);

  internal static bool ShouldRevealPromptFocusForKey(Key key, Key systemKey, ModifierKeys modifiers) =>
    key is Key.LeftAlt or Key.RightAlt
    || systemKey is Key.LeftAlt or Key.RightAlt
    || (modifiers & ModifierKeys.Alt) != 0;

  private void SetPromptKeyboardFocusVisible(bool isVisible)
  {
    bool shouldShow = isVisible
      && Prompt.IsKeyboardFocused
      && Prompt.IsEnabled
      && Prompt.Visibility == Visibility.Visible
      && CompactComposer.Visibility == Visibility.Visible;
    promptKeyboardFocusVisible = shouldShow;
    CompactComposer.SetResourceReference(
      Border.BorderBrushProperty,
      shouldShow ? "Brush.Control.Primary" : "Brush.Border.Subtle");
  }

  internal Button ExpandPromptElement => ExpandPrompt;
  internal TextBox PromptElement => Prompt;
  internal bool IsExpansionRefreshQueued => expansionRefreshQueued;
  internal bool IsProgressRunning => ProgressView.IsRunning;
}

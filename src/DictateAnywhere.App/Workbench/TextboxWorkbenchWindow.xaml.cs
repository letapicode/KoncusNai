using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DictateAnywhere.App.Composition;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Lifecycle;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Productivity;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.App.Settings;
using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;

namespace DictateAnywhere.App.Workbench;

public partial class TextboxWorkbenchWindow : Window, IAsyncDisposable
{
  private readonly IDiagnostics diagnostics;
  private readonly WorkbenchQuickSettingsController quickSettingsController;
  private readonly WorkbenchSettingsApplicationController settingsApplicationController;
  private readonly IChatFileDialogService chatFileDialogService;
  private readonly IChatExportFileDialogService chatExportFileDialogService;
  private readonly WorkbenchReadAloudController readAloudController;
  private readonly WorkbenchOperationSession operationSession;
  private readonly WorkbenchHistoryController historyController;
  private readonly WorkbenchHistoryInteractionController historyInteractionController;
  private readonly WorkbenchDictationController dictationController;
  private readonly WorkbenchDictationCommandController dictationCommandController;
  private readonly LocalChatProviderRegistry chatProviderRegistry;
  private readonly WorkbenchFileImportCommandController fileImportCommandController;
  private readonly WorkbenchChatController chatController;
  private readonly WorkbenchChatSendController chatSendController;
  private readonly WorkbenchChatModelSetupCommandController chatModelSetupCommandController;
  private readonly Func<ReaderWindow> readerWindowFactory;
  private const string ChatContextDisclosure =
    "Private local chat. Koncus Nai sends the most recent 12 messages (about 8,000 characters) and bounded excerpts from files you add.";

  private readonly object deferredReadAloudRenderSync = new();
  private static readonly DependencyPropertyDescriptor HighContrastDescriptor =
    DependencyPropertyDescriptor.FromProperty(WindowThemeBehavior.IsHighContrastActiveProperty, typeof(Window));
  private AppSettings CurrentSettings => settingsApplicationController.CurrentSettings;
  private IDisposable? themeChangeSubscription;
  private DispatcherOperation? deferredReadAloudRender;
  private IInputElement? quickSettingsFocusReturnTarget;
  private IInputElement? inlineSettingsFocusReturnTarget;
  private bool disposed;

  public event Action? OpenSettingsRequested;
  public event EventHandler? SettingsSaved;
  public event Action<AppThemePreference>? ThemePreferenceRequested;
  public event Action<TranscriptionModelSelection>? TranscriptionModelSelectionRequested;
  public event Action<int>? ChatOutputFontSizeRequested;
  public event Action<bool>? ChatPaperViewRequested;
  public event Action<int>? WorkbenchZoomRequested;

  private Border SidebarBorder => SidebarView.Surface;
  private Button SettingsButton => SidebarView.SettingsAction;

  internal Task RefreshPersistedDictationHistoryAsync(CancellationToken cancellationToken = default)
  {
    return RefreshHistorySidebarsAsync(cancellationToken);
  }

  internal TextboxWorkbenchWindow(
    IDiagnostics diagnostics,
    WorkbenchDependencies dependencies)
  {
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    ArgumentNullException.ThrowIfNull(dependencies);
    quickSettingsController = dependencies.QuickSettingsController;
    settingsApplicationController = dependencies.SettingsApplicationController;
    this.chatFileDialogService = dependencies.ChatFileDialogService;
    chatExportFileDialogService = dependencies.ChatExportFileDialogService;
    chatProviderRegistry = dependencies.ChatProviderRegistry;
    readAloudController = dependencies.ReadAloudController;
    operationSession = dependencies.OperationSession;
    dictationController = dependencies.DictationController;
    dictationCommandController = dependencies.DictationCommandController;
    fileImportCommandController = dependencies.FileImportCommandController;
    chatController = dependencies.ChatController;
    chatSendController = dependencies.ChatSendController;
    chatModelSetupCommandController = dependencies.ChatModelSetupCommandController;
    readerWindowFactory = dependencies.ReaderWindowFactory;
    historyController = dependencies.HistoryController;
    historyInteractionController = dependencies.HistoryInteractionController;
    dictationController.ToggleRequested += OnDictationToggleRequested;
    AppThemeManager.ApplyThemeResources(AppThemeManager.CurrentPreference);
    InitializeComponent();
    PreviewKeyDown += OnWorkbenchZoomKeyDown;
    QuickSettingsView.ZoomRequested += ChangeWorkbenchZoom;
    QuickSettingsView.PaperViewToggleRequested += OnPaperViewToggleRequested;
    HighContrastDescriptor.AddValueChanged(this, OnHighContrastChanged);
    readAloudController.StateChanged += OnReadAloudStateChanged;
    QuickSettingsView.ChatTextSizePreviewRequested += OnChatTextSizePreviewRequested;
    QuickSettingsView.ChatTextSizeCommitRequested += OnChatTextSizeCommitRequested;
    OperationalStatusView.TransientOutcomeExpired += OnTransientOutcomeExpired;
    StateChanged += OnWindowStateChanged;
    SizeChanged += OnWindowSizeChanged;
    themeChangeSubscription = AppThemeManager.SubscribeToSystemThemeChanges(Dispatcher);
    PopulateChatModelOptions();
    NewChat();
    RefreshThemeMenuState();
    RenderPresentation("Idle");
  }

  public async Task ApplySettingsAsync(
    AppSettings settings,
    bool registerWorkbenchHotkey = true,
    CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    ArgumentNullException.ThrowIfNull(settings);

    AppSettings normalized = settingsApplicationController.Queue(settings, registerWorkbenchHotkey);
    AppThemeManager.ApplyThemeResources(normalized.ThemePreference);
    RenderPresentation();
    await RenderSettingsApplyResultAsync(
      await settingsApplicationController.ApplyQueuedAsync(cancellationToken).ConfigureAwait(true),
      cancellationToken).ConfigureAwait(true);
  }

  private async Task RenderSettingsApplyResultAsync(
    WorkbenchSettingsApplyResult result,
    CancellationToken cancellationToken = default)
  {
    if (result.Status == WorkbenchSettingsApplyStatus.Cancelled || disposed)
    {
      return;
    }

    if (result.WasApplied)
    {
      ApplyChatTypography();
      RefreshChatTextSizeMenuState();
      RefreshThemeMenuState();
      if (!string.IsNullOrWhiteSpace(result.HotkeyStatus))
      {
        SetHotkeyStatus(result.HotkeyStatus);
      }

      if (result.ShouldRefreshModels)
      {
        await RefreshTranscriptionModelOptionsAsync(cancellationToken).ConfigureAwait(true);
      }

      if (result.ShouldRefreshHistory)
      {
        await RefreshHistorySidebarsAsync(cancellationToken).ConfigureAwait(true);
      }

      if (result.ShouldRefreshChatReadiness)
      {
        await RefreshChatModelReadinessAsync(cancellationToken).ConfigureAwait(true);
      }

      if (result.ShouldRenderTranscript)
      {
        RenderChatTranscript();
      }
    }

    if (!string.IsNullOrWhiteSpace(result.StatusMessage))
    {
      UpdateVisualState(result.StatusMessage);
    }
  }

  public void ShowInlineSettings(SettingsPanel settingsPanel)
  {
    ArgumentNullException.ThrowIfNull(settingsPanel);

    inlineSettingsFocusReturnTarget ??= SettingsButton;
    CloseSettingsMenu(restoreFocus: false);
    SetPromptExpanded(false);
    SetWorkbenchInteractionEnabled(WorkbenchInteractionSurface, isEnabled: false);
    InlineSettingsView.ShowPanel(settingsPanel);
  }

  public void ShowInlineSettingsLoading()
  {
    inlineSettingsFocusReturnTarget ??= SettingsButton;
    HideInlineSettings(restoreFocus: false);
    CloseSettingsMenu(restoreFocus: false);
    SetPromptExpanded(false);
    SetWorkbenchInteractionEnabled(WorkbenchInteractionSurface, isEnabled: false);
    InlineSettingsView.ShowLoading();
  }

  public void HideInlineSettings() => HideInlineSettings(restoreFocus: true);

  private void HideInlineSettings(bool restoreFocus)
  {
    bool wasOpen = InlineSettingsView.IsOpen;
    InlineSettingsView.Hide();
    UpdateWorkbenchInteractionState();
    IInputElement? target = inlineSettingsFocusReturnTarget;
    inlineSettingsFocusReturnTarget = null;
    if (wasOpen && restoreFocus)
    {
      RestoreWorkbenchFocus(target);
    }
  }

  private void OnInlineSettingsBackRequested(object? sender, EventArgs e) => HideInlineSettings();

  private void OnInlineSettingsSaved(object? sender, EventArgs e)
  {
    SettingsSaved?.Invoke(this, EventArgs.Empty);
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    StateChanged -= OnWindowStateChanged;
    SizeChanged -= OnWindowSizeChanged;
    HideInlineSettings(restoreFocus: false);
    CloseSettingsMenu(restoreFocus: false);
    themeChangeSubscription?.Dispose();
    themeChangeSubscription = null;
    readAloudController.StateChanged -= OnReadAloudStateChanged;
    DispatcherOperation? readAloudRender;
    lock (deferredReadAloudRenderSync)
    {
      readAloudRender = deferredReadAloudRender;
      deferredReadAloudRender = null;
    }
    readAloudRender?.Abort();
    QuickSettingsView.CommitPendingTextSize();
    QuickSettingsView.ChatTextSizePreviewRequested -= OnChatTextSizePreviewRequested;
    QuickSettingsView.ChatTextSizeCommitRequested -= OnChatTextSizeCommitRequested;
    QuickSettingsView.PaperViewToggleRequested -= OnPaperViewToggleRequested;
    HighContrastDescriptor.RemoveValueChanged(this, OnHighContrastChanged);
    QuickSettingsView.DisposePresentation();
    ComposerView.DisposePresentation();
    OperationalStatusView.TransientOutcomeExpired -= OnTransientOutcomeExpired;
    OperationalStatusView.DisposePresentation();
    CancelActiveChatRequest();
    ValueTask settingsApplicationDisposal = settingsApplicationController.DisposeAsync();
    ValueTask quickSettingsDisposal = quickSettingsController.DisposeAsync();
    ValueTask historyControllerDisposal = historyController.DisposeAsync();
    ValueTask operationDisposal = operationSession.DisposeAsync();
    await settingsApplicationDisposal.ConfigureAwait(true);
    await quickSettingsDisposal.ConfigureAwait(true);
    await historyControllerDisposal.ConfigureAwait(true);
    await operationDisposal.ConfigureAwait(true);
    dictationController.ToggleRequested -= OnDictationToggleRequested;
    await dictationController.DisposeAsync().ConfigureAwait(true);
    await chatController.DisposeAsync().ConfigureAwait(true);
    await fileImportCommandController.DisposeAsync().ConfigureAwait(true);
    await readAloudController.DisposeAsync().ConfigureAwait(true);
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "The hotkey event boundary must observe unexpected dispatcher and workflow failures.")]
  private async void OnDictationToggleRequested(object? sender, EventArgs e)
  {
    if (disposed || Dispatcher.HasShutdownStarted)
    {
      return;
    }

    try
    {
      if (Dispatcher.CheckAccess())
      {
        await ToggleFromHotkeyAsync().ConfigureAwait(true);
      }
      else
      {
        await Dispatcher.InvokeAsync(ToggleFromHotkeyAsync).Task.Unwrap().ConfigureAwait(false);
      }
    }
    catch (Exception ex) when (disposed && ex is (OperationCanceledException or ObjectDisposedException))
    {
      // Window shutdown owns cancellation of the active hotkey command.
    }
    catch (Exception ex)
    {
      diagnostics.Error("Unexpected Workbench hotkey command failure.", ex);
    }
  }

  private Task ToggleFromHotkeyAsync()
  {
    diagnostics.Info("Workbench hotkey toggle invoked.");
    return dictationController.State == WorkbenchSessionState.Recording
      ? StopAndTranscribeAsync("hotkey")
      : StartRecordingAsync("hotkey");
  }

  private async void OnRecordClicked(object sender, RoutedEventArgs e)
  {
    if (dictationController.State == WorkbenchSessionState.Recording)
    {
      await StopAndTranscribeAsync("microphone").ConfigureAwait(true);
      return;
    }

    await StartRecordingAsync("microphone").ConfigureAwait(true);
  }

  private async void OnStopClicked(object sender, RoutedEventArgs e)
  {
    await StopAndTranscribeAsync("button").ConfigureAwait(true);
  }

  private void OnNewSessionClicked(object sender, RoutedEventArgs e)
  {
    if (!CanReplaceCurrentConversation())
    {
      return;
    }

    NewSession();
    NewChat();
    UpdateVisualState("New chat ready.");
  }

  private void OnHistorySelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (!IsConversationNavigationAvailable() || SidebarView.SelectedDictationGroup is not { } selected)
    {
      return;
    }

    LoadHistoryGroup(selected);
  }

  private void OnHistoryPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
  {
    if (IsConversationNavigationAvailable()
        && SidebarView.GetDictationGroupAt(e.OriginalSource as DependencyObject) is { } selected
        && SidebarView.IsSelected(selected))
    {
      LoadHistoryGroup(selected);
    }
  }

  private void LoadHistoryGroup(HistoryItemViewModel selected)
  {
    ApplyHistoryInteractionResult(historyInteractionController.LoadDictationGroup(selected));
    SetSelectionMetadata($"Dictations - {selected.LatestRecord.CreatedUtc.LocalDateTime:D}");
  }

  private async void OnHistorySearchTextChanged(object sender, TextChangedEventArgs e)
  {
    await RefreshHistorySidebarsAsync().ConfigureAwait(true);
  }

  private void OnTranscriptTextChanged(object sender, TextChangedEventArgs e)
  {
    historyInteractionController.NotifyComposerTextChanged(ComposerView.PromptText);
    RenderPresentation();
  }

  private async void OnSaveHistoryEditsClicked(object sender, RoutedEventArgs e)
  {
    WorkbenchHistoryInteractionResult result = await historyInteractionController
      .SaveDictationEditAsync(CurrentSettings, ComposerView.PromptText)
      .ConfigureAwait(true);
    await ApplyHistoryInteractionResultAsync(result).ConfigureAwait(true);
  }

  private async void OnDeleteHistorySessionClicked(object sender, RoutedEventArgs e)
  {
    IReadOnlyList<HistoryItemViewModel> selectedItems = SidebarView.GetSelectedDictationGroups();
    if (selectedItems.Count == 0)
    {
      UpdateVisualState("Select a history item first.");
      return;
    }

    int selectedCount = selectedItems.Count;
    bool confirmed = ThemedDialog.Confirm(
      this,
      "Delete",
      selectedCount == 1
        ? "Delete this dictation session from local history?"
        : $"Delete these {selectedCount} dictation sessions from local history?",
      "Delete",
      destructive: true);
    if (!confirmed)
    {
      ApplyHistoryInteractionResult(historyInteractionController.CancelDictationDelete());
      return;
    }

    WorkbenchHistoryInteractionResult result = await historyInteractionController
      .DeleteDictationGroupsAsync(CurrentSettings, selectedItems)
      .ConfigureAwait(true);
    await ApplyHistoryInteractionResultAsync(result).ConfigureAwait(true);
  }

  private async void OnRenameHistoryClicked(object sender, RoutedEventArgs e)
  {
    if (!historyInteractionController.State.HasSelectedDictation || SidebarView.SelectedDictationCount != 1)
    {
      UpdateVisualState("Select a history item first.");
      return;
    }

    string? requestedTitle = ShowRenameDialog(
      "Rename",
      "Rename",
      historyInteractionController.State.SelectedDictationRecord!.Title,
      "Rename");
    if (requestedTitle is null)
    {
      ApplyHistoryInteractionResult(historyInteractionController.CancelDictationRename());
      return;
    }

    WorkbenchHistoryInteractionResult result = await historyInteractionController
      .RenameDictationAsync(CurrentSettings, requestedTitle)
      .ConfigureAwait(true);
    await ApplyHistoryInteractionResultAsync(result).ConfigureAwait(true);
  }

  private void OnDeleteHistoryContextMenuClicked(object sender, RoutedEventArgs e)
  {
    OnDeleteHistorySessionClicked(sender, e);
  }

  private void OnChatHistorySelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (!IsConversationNavigationAvailable() || SidebarView.SelectedChat is not { } selected)
    {
      return;
    }

    ApplyHistoryInteractionResult(historyInteractionController.LoadChat(selected), chatStatus: true);
  }

  private void OnNewChatClicked(object sender, RoutedEventArgs e)
  {
    if (!CanReplaceCurrentConversation())
    {
      return;
    }

    NewSession();
    NewChat();
    SetChatStatus("New chat ready.");
  }

  private async void OnRenameChatClicked(object sender, RoutedEventArgs e)
  {
    if (chatController.IsBusy)
    {
      return;
    }

    string? requestedTitle = ShowRenameDialog(
      "Rename",
      "Rename",
      historyInteractionController.State.ChatTitle,
      "Rename");
    if (requestedTitle is null)
    {
      ApplyHistoryInteractionResult(historyInteractionController.CancelChatRename(), chatStatus: true);
      return;
    }
    WorkbenchHistoryInteractionResult result = await historyInteractionController
      .RenameChatAsync(CurrentSettings, requestedTitle)
      .ConfigureAwait(true);
    await ApplyHistoryInteractionResultAsync(result, chatStatus: true).ConfigureAwait(true);
  }

  private async void OnDeleteChatClicked(object sender, RoutedEventArgs e)
  {
    if (chatController.IsBusy)
    {
      return;
    }

    IReadOnlyList<ChatHistoryItemViewModel> selectedItems = SidebarView.GetSelectedChats();
    if (selectedItems.Count == 0)
    {
      SetChatStatus("Select a chat first.");
      return;
    }

    int selectedCount = selectedItems.Count;
    bool confirmed = ThemedDialog.Confirm(
      this,
      "Delete",
      selectedCount == 1
        ? "Delete this saved chat from local history?"
        : $"Delete these {selectedCount} saved chats from local history?",
      "Delete",
      destructive: true);
    if (!confirmed)
    {
      ApplyHistoryInteractionResult(historyInteractionController.CancelChatDelete(), chatStatus: true);
      return;
    }

    WorkbenchHistoryInteractionResult result = await historyInteractionController
      .DeleteChatsAsync(CurrentSettings, selectedItems)
      .ConfigureAwait(true);
    await ApplyHistoryInteractionResultAsync(result, chatStatus: true).ConfigureAwait(true);
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "The model-selection UI event must observe unexpected readiness failures.")]
  private async void OnChatModelSelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (QuickSettingsView.SelectedChatModel is not { } selected)
    {
      return;
    }

    chatController.SelectModel(selected.Selection);
    RefreshChatModelDetail();
    SetChatStatus($"{selected.Label} selected.");
    try
    {
      await RefreshChatModelReadinessAsync().ConfigureAwait(true);
    }
    catch (OperationCanceledException) when (disposed)
    {
      // Window shutdown owns cancellation of the readiness check.
    }
    catch (Exception ex)
    {
      diagnostics.Error("Unexpected chat model readiness failure.", ex);
      SetChatStatus("Could not check the selected chat model. The error was recorded in Diagnostics.");
    }
  }

  private void OnChatTextSizePreviewRequested(int size)
  {
    settingsApplicationController.ApplyPresentationOnly(CurrentSettings with
    {
      ChatOutputFontSize = size,
    });
    ApplyChatTypography();
  }

  private void OnChatTextSizeCommitRequested(int size)
  {
    ChatOutputFontSizeRequested?.Invoke(size);
  }

  private void OnTranscriptionModelSelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (QuickSettingsView.SelectedTranscriptionModel is not { } selected)
    {
      return;
    }

    if (!selected.IsInstalled)
    {
      QuickSettingsView.SetTranscriptionStatus("Download this speech model in All settings before using it.");
      return;
    }

    TranscriptionModelSelection selection = selected.Selection.Normalize();
    settingsApplicationController.ApplyPresentationOnly(
      CurrentSettings.WithConfiguredTranscription(selection.ProviderId, selection.ModelId));
    QuickSettingsView.SetTranscriptionStatus($"Speech: {selected.Label}");
    TranscriptionModelSelectionRequested?.Invoke(selection);
  }

  private void OnChatHistoryPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
  {
    SidebarView.PrepareChatContextMenuSelection(e.OriginalSource as DependencyObject);
  }

  private void OnHistoryPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
  {
    SidebarView.PrepareDictationContextMenuSelection(e.OriginalSource as DependencyObject);
  }

  private void OnChatHistoryPreviewKeyDown(object sender, KeyEventArgs e)
  {
    if (e.Key != Key.Delete || SidebarView.SelectedChatCount == 0)
    {
      return;
    }

    e.Handled = true;
    OnDeleteChatClicked(sender, e);
  }

  private void OnHistoryPreviewKeyDown(object sender, KeyEventArgs e)
  {
    if (e.Key != Key.Delete || SidebarView.SelectedDictationCount == 0)
    {
      return;
    }

    e.Handled = true;
    OnDeleteHistorySessionClicked(sender, e);
  }

  private async void OnDownloadChatModelClicked(object sender, RoutedEventArgs e)
  {
    try
    {
      Progress<WorkbenchChatModelSetupProgress> progress = new(update =>
      {
        SetChatStatus(update.Status);
        OperationalStatusView.SetProgress(update.Completion is null or <= 0
          ? null
          : update.Completion.Value * 100);
      });
      WorkbenchChatModelSetupCommandResult result = await chatModelSetupCommandController
        .SetupAsync(progress, ApplyChatSendProgress, () => RenderPresentation())
        .ConfigureAwait(true);
      OperationalStatusView.StopProgress();
      SetChatStatus(result.StatusMessage);
    }
    finally
    {
      await FinishChatOperationAsync().ConfigureAwait(true);
    }
  }

  private async void OnSendChatClicked(object sender, RoutedEventArgs e)
  {
    await SendChatAsync().ConfigureAwait(true);
  }

  private void OnStopChatClicked(object sender, RoutedEventArgs e)
  {
    CancelActiveChatRequest();
  }

  private async void OnChatPromptKeyDown(object sender, KeyEventArgs e)
  {
    if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
    {
      return;
    }

    e.Handled = true;
    await SendChatAsync().ConfigureAwait(true);
  }

  private void OnChatPromptTextChanged(object sender, TextChangedEventArgs e)
  {
    if (ExpandedPromptView.IsOpen)
    {
      SyncExpandedPromptFromComposer();
    }

    RefreshPromptExpansionButton();
    RenderPresentation();
  }

  private void OnFilesDragOver(object sender, DragEventArgs e)
  {
    e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
      ? DragDropEffects.Copy
      : DragDropEffects.None;
    e.Handled = true;
  }

  private async void OnFilesDropped(object sender, DragEventArgs e)
  {
    if (!e.Data.GetDataPresent(DataFormats.FileDrop)
        || e.Data.GetData(DataFormats.FileDrop) is not string[] files
        || files.Length == 0)
    {
      return;
    }

    await ImportFilesAsync(files, "file-drop").ConfigureAwait(true);
  }

  private async void OnAddFileClicked(object sender, RoutedEventArgs e)
  {
    if (!chatFileDialogService.TryGetFilePaths(this, out IReadOnlyList<string> files))
    {
      return;
    }

    await ImportFilesAsync(files, "file-picker").ConfigureAwait(true);
  }

  private void OnRemovePendingFileClicked(object sender, RoutedEventArgs e)
  {
    if (sender is not Button { Tag: string attachmentId })
    {
      return;
    }

    _ = chatController.RemovePendingFile(attachmentId);
    RefreshPendingFileChips();
    SetChatStatus(chatController.PendingFiles.Count == 0
      ? "File removed."
      : $"{chatController.PendingFiles.Count} file{(chatController.PendingFiles.Count == 1 ? string.Empty : "s")} ready.");
  }

  private void OnReadDocumentClicked(object sender, RoutedEventArgs e)
  {
    try
    {
      ReaderWindow reader = readerWindowFactory();
      reader.Show();
      SetChatStatus("Reading Studio is open.");
    }
    catch (InvalidOperationException ex)
    {
      diagnostics.Warning($"Reading Studio could not open: {ex.Message}");
      SetChatStatus("Could not open Reading Studio. See Diagnostics.");
    }
  }

  private void OnStopSpeakingClicked(object sender, RoutedEventArgs e)
  {
    readAloudController.Stop();
  }

  private void SpeakChatResponse(string text) => readAloudController.StartRead(text, "this response");

  private void OnReadAloudStateChanged(object? sender, WorkbenchReadAloudState state)
  {
    if (disposed)
    {
      return;
    }

    if (!Dispatcher.CheckAccess())
    {
      DispatcherOperation? previousRender;
      lock (deferredReadAloudRenderSync)
      {
        if (disposed)
        {
          return;
        }

        previousRender = deferredReadAloudRender;
        DispatcherOperation? scheduledRender = null;
        scheduledRender = Dispatcher.BeginInvoke(new Action(() =>
        {
          lock (deferredReadAloudRenderSync)
          {
            if (ReferenceEquals(deferredReadAloudRender, scheduledRender))
            {
              deferredReadAloudRender = null;
            }
          }
          OnReadAloudStateChanged(sender, state);
        }));
        deferredReadAloudRender = scheduledRender;
      }
      previousRender?.Abort();
      return;
    }

    ComposerView.SetSpeechPreparation(state.IsPreparationVisible, state.PreparationMessage);
    SetChatStatus(state.StatusMessage);
  }

  private async Task ImportFilesAsync(IEnumerable<string> files, string source)
  {
    try
    {
      WorkbenchFileImportResult result = await fileImportCommandController
        .ImportAsync(
          files,
          source,
          ComposerView.PromptText,
          historyInteractionController.DictationSessionId,
          CurrentSettings,
          ApplyFileImportProgress)
        .ConfigureAwait(true);
      if (result.PendingFilesChanged)
      {
        RefreshPendingFileChips();
      }

      if (result.ShouldRefreshHistory)
      {
        await RefreshHistorySidebarsAsync().ConfigureAwait(true);
      }

      SetChatStatus(result.StatusMessage);
    }
    catch (Exception ex) when (disposed && ex is (OperationCanceledException or ObjectDisposedException))
    {
      // Window shutdown owns cancellation and waits for the active import to unwind.
    }
    finally
    {
      OperationalStatusView.StopProgress();
      await FinishWorkbenchOperationAsync().ConfigureAwait(true);
    }
  }

  private void ApplyFileImportProgress(WorkbenchFileImportProgress progress)
  {
    switch (progress.Kind)
    {
      case WorkbenchFileImportProgressKind.AudioBatchStarted:
        OperationalStatusView.SetProgress(null);
        OperationalStatusView.SetModelReadinessText("Transcribing imported audio...");
        SetChatStatus(progress.StatusMessage);
        break;
      case WorkbenchFileImportProgressKind.AudioItemCompleted:
        if (progress.ComposerText is not null)
        {
          ComposerView.PromptText = progress.ComposerText;
          SyncExpandedPromptFromComposer();
        }

        if (progress.HistoryWrite is not null)
        {
          ApplyDictationHistoryWrite(progress.HistoryWrite);
        }

        UpdateVisualState(progress.StatusMessage);
        break;
      case WorkbenchFileImportProgressKind.DocumentItemStarted:
        SetChatStatus(progress.StatusMessage);
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(progress), progress.Kind, "Unsupported file-import progress kind.");
    }
  }

  private void RefreshPendingFileChips()
  {
    ComposerView.SetPendingFiles(chatController.PendingFiles);
  }

  private async Task StartRecordingAsync(string source)
  {
    WorkbenchDictationCommandResult result = await dictationCommandController
      .StartAsync(source, progress => ApplyDictationCommandProgress(progress, source))
      .ConfigureAwait(true);
    try
    {
      if (result.Status == WorkbenchDictationCommandStatus.RecordingStarted)
      {
        ClearTransientSessionOutcome();
      }

      if (!disposed && !string.IsNullOrWhiteSpace(result.StatusMessage))
      {
        UpdateVisualState(result.StatusMessage);
      }
    }
    finally
    {
      if (result.OperationAccepted)
      {
        await FinishWorkbenchOperationAsync().ConfigureAwait(true);
      }
    }
  }

  private async Task StopAndTranscribeAsync(string source)
  {
    string sourceSessionId = historyInteractionController.DictationSessionId;
    long sourceComposerRevision = ComposerView.PromptRevision;
    WorkbenchDictationCommandResult result = await dictationCommandController
      .StopAndTranscribeAsync(
        source,
        ComposerView.PromptText,
        sourceSessionId,
        CurrentSettings,
        sourceComposerRevision,
        progress => ApplyDictationCommandProgress(progress, source))
      .ConfigureAwait(true);
    try
    {
      if (result.Status == WorkbenchDictationCommandStatus.NoAudibleSpeech)
      {
        ShowTransientSessionOutcome(result.StatusMessage);
        return;
      }

      if (result.Status == WorkbenchDictationCommandStatus.TranscriptionCompleted)
      {
        bool isSourceSessionCurrent = string.Equals(
          result.SourceSessionId,
          historyInteractionController.DictationSessionId,
          StringComparison.Ordinal);
        if (isSourceSessionCurrent)
        {
          ComposerView.PromptText = result.ReconcileComposerText(
              ComposerView.PromptText,
              historyInteractionController.DictationSessionId,
              ComposerView.PromptRevision)
            ?? ComposerView.PromptText;
          ComposerView.FocusPromptAtEnd();
        }

        if (isSourceSessionCurrent && result.HistoryWrite is not null)
        {
          ApplyDictationHistoryWrite(result.HistoryWrite);
        }

        if (result.ShouldRefreshHistory)
        {
          await RefreshHistorySidebarsAsync().ConfigureAwait(true);
        }
      }

      if (!disposed && !string.IsNullOrWhiteSpace(result.StatusMessage))
      {
        string status = result.Status == WorkbenchDictationCommandStatus.TranscriptionCompleted
                        && !string.Equals(
                          result.SourceSessionId,
                          historyInteractionController.DictationSessionId,
                          StringComparison.Ordinal)
          ? $"{result.StatusMessage} Saved to history; the newer session was left unchanged."
          : result.StatusMessage;
        UpdateVisualState(status);
      }
    }
    finally
    {
      if (result.OperationAccepted)
      {
        await FinishWorkbenchOperationAsync().ConfigureAwait(true);
      }
    }
  }

  private void ApplyDictationCommandProgress(WorkbenchDictationCommandProgress progress, string source)
  {
    if (progress == WorkbenchDictationCommandProgress.Transcribing)
    {
      UpdateVisualState($"Transcribing ({source})...");
      return;
    }

    RenderPresentation();
  }

  private async Task FinishWorkbenchOperationAsync()
  {
    if (disposed)
    {
      return;
    }

    RenderPresentation();
    await ApplyPendingSettingsAsync().ConfigureAwait(true);
  }

  private async Task ApplyPendingSettingsAsync()
  {
    if (disposed || !settingsApplicationController.HasPendingSettings)
    {
      return;
    }

    WorkbenchSettingsApplyResult result = await settingsApplicationController
      .TryApplyPendingAsync()
      .ConfigureAwait(true);
    await RenderSettingsApplyResultAsync(result).ConfigureAwait(true);
    if (result.WasApplied && settingsApplicationController.HasPendingSettings)
    {
      await ApplyPendingSettingsAsync().ConfigureAwait(true);
    }
  }

  private void NewSession()
  {
    historyInteractionController.BeginNewDictationSession();
    historyInteractionController.ResetDictationSelection();
    SidebarView.ClearDictationSelection();
    ComposerView.ClearPrompt();
    SetSelectionMetadata(null);
  }

  private void NewChat()
  {
    ApplyHistoryInteractionResult(historyInteractionController.BeginNewChat(), chatStatus: false);
    SetSelectionMetadata(null);
  }

  private bool CanReplaceCurrentConversation()
  {
    if (!IsConversationNavigationAvailable())
    {
      SetChatStatus("Finish the current recording or chat action first.");
      return false;
    }

    return true;
  }

  private bool IsConversationNavigationAvailable() =>
    dictationController.State == WorkbenchSessionState.Idle
    && !operationSession.IsBusy
    && !operationSession.IsImportingFiles
    && !chatController.IsBusy;

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "An async UI command must surface unexpected local-runtime failures instead of terminating the dispatcher.")]
  private async Task SendChatAsync()
  {
    bool operationAccepted = false;
    try
    {
      WorkbenchChatSendResult result = await chatSendController.SendAsync(
          ComposerView.PromptText,
          historyInteractionController.State.ChatTitle,
          GetSelectedChatModelLabel(),
          CurrentSettings,
          ApplyChatSendProgress)
        .ConfigureAwait(true);
      operationAccepted = result.OperationAccepted;
      bool isCurrentConversation = chatController.IsCurrent(result.Conversation);
      if (isCurrentConversation && !string.IsNullOrWhiteSpace(result.Title))
      {
        historyInteractionController.UpdateChatTitle(result.Title);
        HeaderView.Title = historyInteractionController.State.ChatTitle;
      }

      if (result.ShouldRefreshHistory)
      {
        await RefreshHistorySidebarsAsync().ConfigureAwait(true);
      }
      if (isCurrentConversation && !string.IsNullOrWhiteSpace(result.StatusMessage))
      {
        SetChatStatus(result.StatusMessage);
      }
    }
    catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
    {
      diagnostics.Error("Workbench chat presentation refresh failed.", ex);
      SetChatStatus("Chat completed, but the view could not refresh. See Diagnostics.");
    }
    finally
    {
      _ = ComposerView.StopProgress();
      if (operationAccepted)
      {
        await FinishChatOperationAsync().ConfigureAwait(true);
      }
      else
      {
        RenderPresentation();
      }
    }
  }

  private void ApplyChatSendProgress(WorkbenchChatSendProgress progress)
  {
    if (progress.Conversation is { } identity && !chatController.IsCurrent(identity))
    {
      return;
    }

    switch (progress.Kind)
    {
      case WorkbenchChatSendProgressKind.OperationStarted:
        break;
      case WorkbenchChatSendProgressKind.ReadinessChanged when progress.Readiness is not null:
        ApplyChatModelReadinessState(progress.Readiness);
        break;
      case WorkbenchChatSendProgressKind.LocalReplyAdded:
        ComposerView.ClearPrompt();
        RenderChatTranscript();
        break;
      case WorkbenchChatSendProgressKind.CompletionStarted:
        RefreshPendingFileChips();
        ComposerView.ClearPrompt();
        RenderChatTranscript();
        ComposerView.StartProgress(ChatTypefaceCatalog.CreateFontFamily(CurrentSettings.ChatTypefaceId));
        break;
      case WorkbenchChatSendProgressKind.CompletionAdded:
      case WorkbenchChatSendProgressKind.FailureAdded:
        _ = ComposerView.StopProgress();
        RenderChatTranscript();
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(progress), progress.Kind, "Unsupported chat-send progress state.");
    }

    RenderPresentation();
  }

  private async Task FinishChatOperationAsync()
  {
    if (disposed)
    {
      return;
    }

    RenderPresentation();
    await ApplyPendingSettingsAsync().ConfigureAwait(true);
  }

  private void RenderChatTranscript()
  {
    ChatTranscriptView.Render(
      chatController.Messages,
      ResolveChatMessageBrush,
      CopyReplyToClipboard,
      CopyCodeToClipboard,
      SpeakChatResponse,
      CurrentSettings.ChatPaperViewEnabled);
    RefreshChatModelDetail();
  }

  private void CopyReplyToClipboard(string reply) => CopyChatTextToClipboard(reply, "reply");

  private void CopyCodeToClipboard(string code) => CopyChatTextToClipboard(code, "code");

  private void CopyChatTextToClipboard(string text, string label)
  {
    try
    {
      Clipboard.SetText(text);
      SetChatStatus($"Copied {label}.");
    }
    catch (COMException ex)
    {
      diagnostics.Warning($"Could not copy chat {label}: {ex.Message}");
      SetChatStatus($"Could not copy {label}; another app is using the clipboard.");
    }
  }

  private void OnExportChatClicked(object sender, RoutedEventArgs e)
  {
    string exportTitle = HeaderView.Title;
    ChatMessage[] exportMessages = chatController.Messages.ToArray();
    if (exportMessages.Length == 0 || !chatExportFileDialogService.TryGetExportPath(this, exportTitle, out string path))
    {
      return;
    }

    try
    {
      string contents = string.Equals(Path.GetExtension(path), ".rtf", StringComparison.OrdinalIgnoreCase)
        ? ChatTranscriptExporter.ToRtf(exportTitle, exportMessages)
        : ChatTranscriptExporter.ToMarkdown(exportTitle, exportMessages);
      ChatTranscriptFileWriter.WriteAtomically(path, contents);
      SetChatStatus($"Exported chat to {Path.GetFileName(path)}.");
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
    {
      diagnostics.Warning($"Chat export failed: {ex.Message}");
      SetChatStatus("Could not export chat. See Diagnostics.");
    }
  }

  private Brush ResolveChatMessageBrush(bool isAssistant)
  {
    if (CurrentSettings.ChatPaperViewEnabled)
      return TryFindResource(isAssistant ? "Brush.Paper.Ink" : "Brush.Paper.UserText") as Brush ?? Brushes.Black;
    string resourceKey = isAssistant ? "Brush.Text.Primary" : "Brush.Chat.UserText";
    return TryFindResource(resourceKey) as Brush
      ?? (isAssistant ? Brushes.Black : Brushes.DimGray);
  }

  private string GetSelectedChatModelLabel()
  {
    return QuickSettingsView.SelectedChatModel is { } selected
      ? selected.ShortLabel
      : "Local model";
  }

  private void ApplyDictationHistoryWrite(WorkbenchDictationHistoryWriteResult result)
  {
    historyInteractionController.RecordDictationHistoryWrite(result);

    if (result.Status != HistoryCommandStatus.Succeeded && !string.IsNullOrWhiteSpace(result.StatusMessage))
    {
      SidebarView.SetDictationStatus(result.StatusMessage);
    }
  }

  private void PopulateChatModelOptions()
  {
    IReadOnlyList<ChatModelOptionViewModel> models = chatProviderRegistry
      .GetDefinitions()
      .SelectMany(provider => provider.Models.Select(model => new ChatModelOptionViewModel(
        new ChatModelSelection(provider.ProviderId, model.ModelId).Normalize(),
        GetChatModelShortLabel(model.ModelId),
        model.DisplayName,
        model.Description)))
      .ToList();
    QuickSettingsView.SetChatModels(models);
    SelectChatModel(ChatModelSelection.Default);
  }

  private async Task RefreshTranscriptionModelOptionsAsync(
    CancellationToken cancellationToken = default,
    bool latestRequestOnly = false)
  {
    WorkbenchTranscriptionModelState? state = latestRequestOnly
      ? await quickSettingsController.QueryLatestModelsAsync(CurrentSettings, cancellationToken).ConfigureAwait(true)
      : await quickSettingsController.QueryModelsAsync(CurrentSettings, cancellationToken).ConfigureAwait(true);
    if (state is not null)
    {
      QuickSettingsView.SetTranscriptionModels(state);
    }
  }

  private static string GetChatModelShortLabel(string modelId)
  {
    return modelId switch
    {
      "gemma4:e4b" => "Gemma 4",
      "gemma-4-E2B-it-mtp" => "Fast",
      "gemma-4-E2B-it" => "2B",
      "gemma-4-E4B-it" => "4B",
      "gemma-3-4b-it-Q4_K_M.gguf" => "Gemma 3",
      "Qwen3-1.7B-Q4_K_M.gguf" => "Qwen Fast",
      "Qwen3-4B-Q4_K_M.gguf" => "Qwen Code",
      _ => "Local model",
    };
  }

  private void SelectChatModel(ChatModelSelection selection)
  {
    ChatModelSelection normalizedSelection = selection.Normalize();
    ChatModelOptionViewModel? item = QuickSettingsView.SelectChatModel(normalizedSelection);
    if (item is not null)
    {
      chatController.SelectModel(item.Selection);
    }
  }

  private async Task RefreshChatModelReadinessAsync(CancellationToken cancellationToken = default)
  {
    WorkbenchChatModelReadinessState? state = await chatSendController
      .RefreshReadinessAsync(ApplyChatSendProgress, cancellationToken)
      .ConfigureAwait(true);
    if (state is null)
    {
      return;
    }

    if (!chatController.IsBusy)
    {
      SetChatStatus(state.Status);
    }
  }

  private void ApplyChatModelReadinessState(WorkbenchChatModelReadinessState state)
  {
    OperationalStatusView.SetModelReadiness(state.Progress, state.Detail);
    RefreshChatModelDetail();
  }

  private void RefreshChatModelDetail()
  {
    if (QuickSettingsView.SelectedChatModel is not { } selected)
    {
      OperationalStatusView.SetModelDetail(ChatContextDisclosure);
      return;
    }

    string installState = chatController.IsModelInstalled
      ? chatController.IsRuntimeReady ? "Ready." : "Downloaded; runtime needs update."
      : "Not downloaded.";
    string conversationState = chatController.Messages.Count == 0
      ? "No prior messages in this chat yet."
      : $"{chatController.Messages.Count} message(s) in this chat; recent ones are included with the next prompt.";
    OperationalStatusView.SetModelDetail(
      $"{selected.Description} {installState} {conversationState} {ChatContextDisclosure}");
  }

  private void OnWindowStateChanged(object? sender, EventArgs e)
  {
    UpdateSettingsMenuPlacement();
  }

  private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
  {
    UpdateSettingsMenuPlacement();
  }

  private async void OnSettingsButtonClicked(object sender, RoutedEventArgs e)
  {
    if (QuickSettingsView.IsOpen)
    {
      CloseSettingsMenu();
      return;
    }

    RefreshThemeMenuState();
    quickSettingsFocusReturnTarget = Keyboard.FocusedElement ?? SettingsButton;
    SetWorkbenchInteractionEnabled(WorkbenchInteractionSurface, isEnabled: false);
    QuickSettingsView.Show();
    UpdateSettingsMenuPlacement();

    if (!QuickSettingsView.HasTranscriptionModels && !operationSession.IsBusy)
    {
      await RefreshSettingsMenuModelOptionsAsync().ConfigureAwait(true);
    }
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "This async UI event boundary must observe unexpected refresh failures instead of terminating the dispatcher.")]
  private async Task RefreshSettingsMenuModelOptionsAsync()
  {
    try
    {
      await Dispatcher.Yield(DispatcherPriority.Background);
      await RefreshTranscriptionModelOptionsAsync(latestRequestOnly: true).ConfigureAwait(true);
    }
    catch (ObjectDisposedException) when (disposed)
    {
      // Window shutdown owns the refresh-session disposal.
    }
    catch (Exception ex)
    {
      diagnostics.Error("Workbench settings-menu model refresh failed.", ex);
      if (!disposed)
      {
        QuickSettingsView.SetTranscriptionStatus("Speech models unavailable. Open Settings to retry.");
      }
    }
  }

  private void OnOpenFullSettingsClicked(object sender, RoutedEventArgs e)
  {
    inlineSettingsFocusReturnTarget = quickSettingsFocusReturnTarget ?? SettingsButton;
    CloseSettingsMenu(restoreFocus: false);
    OpenSettingsRequested?.Invoke();
  }

  private void OnAboutClicked(object sender, RoutedEventArgs e)
  {
    CloseSettingsMenu();
    ShowAboutDialog();
  }

  private void OnSettingsMenuOverlayMouseDown(object sender, MouseButtonEventArgs e)
  {
    CloseSettingsMenu();
  }

  private void OnSettingsMenuDismissRequested(object? sender, EventArgs e) => CloseSettingsMenu();

  private void OnSettingsMenuSurfaceMouseDown(object sender, MouseButtonEventArgs e)
  {
    e.Handled = true;
  }

  private void OnToggleThemeClicked(object sender, RoutedEventArgs e)
  {
    AppThemePreference current = CurrentSettings.ThemePreference == AppThemePreference.Light
      ? AppThemePreference.Light
      : AppThemePreference.Dark;
    ApplyThemePreferenceFromMenu(current == AppThemePreference.Dark
      ? AppThemePreference.Light
      : AppThemePreference.Dark);
  }

  private void OnPaperViewToggleRequested()
  {
    bool enabled = !CurrentSettings.ChatPaperViewEnabled;
    settingsApplicationController.ApplyPresentationOnly(CurrentSettings with { ChatPaperViewEnabled = enabled });
    ApplyPaperView();
    CloseSettingsMenu();
    ChatPaperViewRequested?.Invoke(enabled);
  }

  private void OnHighContrastChanged(object? sender, EventArgs e) => ApplyPaperView();

  private void ApplyPaperView()
  {
    bool enabled = CurrentSettings.ChatPaperViewEnabled;
    PaperSurface.Visibility = enabled && !WindowThemeBehavior.GetIsHighContrastActive(this)
      ? Visibility.Visible : Visibility.Collapsed;
    ChatTranscriptView.SetPaperView(enabled);
    ComposerView.SetPaperView(enabled);
    ComposerView.Margin = new Thickness(24, 0, 24, 18);
    ExpandedPromptView.SetPaperView(enabled);
    QuickSettingsView.SetPaperViewState(enabled);
    RenderChatTranscript();
  }

  private void ApplyThemePreferenceFromMenu(AppThemePreference preference)
  {
    settingsApplicationController.ApplyPresentationOnly(CurrentSettings with
    {
      ThemePreference = preference,
    });
    AppThemeManager.ApplyThemeResources(preference);
    ApplyPaperView();
    RefreshThemeMenuState();
    CloseSettingsMenu();
    ThemePreferenceRequested?.Invoke(preference);
  }

  private void CloseSettingsMenu(bool restoreFocus = true)
  {
    bool wasOpen = QuickSettingsView.IsOpen;
    quickSettingsController.CancelLatestQuery();
    QuickSettingsView.Hide();
    UpdateWorkbenchInteractionState();
    IInputElement? target = quickSettingsFocusReturnTarget;
    quickSettingsFocusReturnTarget = null;
    if (wasOpen && restoreFocus)
    {
      RestoreWorkbenchFocus(target);
    }
  }

  private void UpdateWorkbenchInteractionState() =>
    SetWorkbenchInteractionEnabled(
      WorkbenchInteractionSurface,
      isEnabled: !QuickSettingsView.IsOpen && !InlineSettingsView.IsOpen);

  internal static void SetWorkbenchInteractionEnabled(UIElement interactionSurface, bool isEnabled)
  {
    ArgumentNullException.ThrowIfNull(interactionSurface);
    interactionSurface.IsEnabled = isEnabled;
  }

  private void RestoreWorkbenchFocus(IInputElement? preferredTarget)
  {
    if (TryFocus(preferredTarget) || TryFocus(SettingsButton))
    {
      return;
    }

    _ = TryFocus(ComposerView.PromptElement);
  }

  internal static bool TryFocus(IInputElement? target)
  {
    if (target is not FrameworkElement element
        || !element.IsLoaded
        || !element.IsVisible
        || !element.IsEnabled
        || !element.Focusable
        || PresentationSource.FromVisual(element) is null)
    {
      return false;
    }

    return element.Focus();
  }

  private void UpdateSettingsMenuPlacement()
  {
    QuickSettingsView.UpdatePlacement(
      WorkbenchRoot,
      SidebarBorder,
      SettingsButton,
      SidebarColumn.ActualWidth);
  }

  private void RefreshThemeMenuState()
  {
    AppThemePreference preference = CurrentSettings.ThemePreference == AppThemePreference.System
      ? AppThemePreference.Dark
      : CurrentSettings.ThemePreference;
    bool isDark = preference == AppThemePreference.Dark;
    QuickSettingsView.SetThemeState(isDark);
  }

  private void ApplyWorkbenchZoom()
  {
    int percent = Math.Clamp(CurrentSettings.WorkbenchZoomPercent, 80, 150);
    double scale = percent / 100d;
    if (WorkbenchRoot.LayoutTransform is not ScaleTransform transform || transform.ScaleX != scale)
      WorkbenchRoot.LayoutTransform = new ScaleTransform(scale, scale);
    QuickSettingsView.SetZoom(percent);
    if (QuickSettingsView.IsOpen) UpdateSettingsMenuPlacement();
  }

  private void ChangeWorkbenchZoom(int delta)
  {
    int percent = delta == 0 ? 100 : Math.Clamp(CurrentSettings.WorkbenchZoomPercent + delta, 80, 150);
    if (percent == CurrentSettings.WorkbenchZoomPercent) return;
    settingsApplicationController.ApplyPresentationOnly(CurrentSettings with { WorkbenchZoomPercent = percent });
    ApplyWorkbenchZoom();
    WorkbenchZoomRequested?.Invoke(percent);
  }

  private void OnWorkbenchZoomKeyDown(object sender, KeyEventArgs e)
  {
    // Inline Settings contains hotkey capture; let that surface own all keystrokes.
    if (InlineSettingsView.IsOpen || e.IsRepeat || e.Key == Key.ImeProcessed) return;
    int? delta = WorkbenchZoomShortcut.Delta(e.Key, Keyboard.Modifiers);
    if (delta is null) return;
    e.Handled = true;
    ChangeWorkbenchZoom(delta.Value);
  }

  private void OnToggleSidebarClicked(object sender, RoutedEventArgs e)
  {
    CloseSettingsMenu();
    bool isVisible = SidebarView.ToggleVisibility();
    if (isVisible)
    {
      SidebarColumn.SetResourceReference(ColumnDefinition.WidthProperty, "Layout.Workbench.SidebarWidth");
      HeaderSidebarColumn.SetResourceReference(ColumnDefinition.WidthProperty, "Layout.Workbench.SidebarWidth");
    }
    else
    {
      SidebarColumn.Width = new GridLength(0);
      HeaderSidebarColumn.Width = new GridLength(190);
    }
    SetSidebarToggleAccessibility(SidebarToggleButton, isVisible);
    RenderPresentation();
  }

  internal static void SetSidebarToggleAccessibility(Button toggleButton, bool isSidebarVisible)
  {
    ArgumentNullException.ThrowIfNull(toggleButton);
    string sidebarActionName = isSidebarVisible ? "Hide sidebar" : "Show sidebar";
    toggleButton.ToolTip = sidebarActionName;
    AutomationProperties.SetName(toggleButton, sidebarActionName);
  }

  private void OnExpandPromptClicked(object sender, RoutedEventArgs e)
  {
    SyncExpandedPromptFromComposer();
    SetPromptExpanded(true);
    ExpandedPromptView.ShowAndFocus();
  }

  private void OnCollapsePromptClicked(object sender, RoutedEventArgs e)
  {
    SyncComposerPromptFromExpanded();
    SetPromptExpanded(false);
    RefreshPromptExpansionButton();
    ComposerView.FocusPromptAtEnd();
  }

  private async void OnExpandedSendChatClicked(object sender, RoutedEventArgs e)
  {
    SyncComposerPromptFromExpanded();
    SetPromptExpanded(false);
    RefreshPromptExpansionButton();
    await SendChatAsync().ConfigureAwait(true);
  }

  private async void OnExpandedPromptKeyDown(object sender, KeyEventArgs e)
  {
    if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
    {
      return;
    }

    e.Handled = true;
    SyncComposerPromptFromExpanded();
    SetPromptExpanded(false);
    RefreshPromptExpansionButton();
    await SendChatAsync().ConfigureAwait(true);
  }

  private void OnExpandedPromptTextChanged(object sender, TextChangedEventArgs e)
  {
    SyncComposerPromptFromExpanded();
  }

  private void SyncExpandedPromptFromComposer() =>
    ExpandedPromptView.SetText(ComposerView.PromptText);

  private void SyncComposerPromptFromExpanded()
  {
    if (!string.Equals(ComposerView.PromptText, ExpandedPromptView.Text, StringComparison.Ordinal))
    {
      ComposerView.PromptText = ExpandedPromptView.Text;
      ComposerView.FocusPromptAtEnd();
    }

    RenderPresentation();
  }

  private void RefreshPromptExpansionButton()
  {
    ComposerView.RefreshExpansionButton(ExpandedPromptView.IsOpen);
  }

  private void SetSelectionMetadata(string? text)
  {
    // Conversation timestamps add noise without helping navigation. They remain in the saved record and export.
    HeaderView.HideSelectionMetadata();
  }

  private string? ShowRenameDialog(
    string title,
    string labelText,
    string currentValue,
    string confirmText)
  {
    Window dialog = new()
    {
      Title = title,
      Owner = this,
      Width = 420,
      SizeToContent = SizeToContent.Height,
      MinWidth = 360,
      ResizeMode = ResizeMode.NoResize,
      WindowStyle = WindowStyle.None,
      AllowsTransparency = true,
      WindowStartupLocation = WindowStartupLocation.CenterOwner,
      Background = Brushes.Transparent,
      Foreground = ResolveBrush("Brush.Text.Primary"),
    };

    Grid root = new()
    {
      Margin = new Thickness(18),
    };
    root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

    TextBlock label = new()
    {
      Text = labelText,
      Margin = new Thickness(0, 0, 0, 6),
    };
    Grid.SetRow(label, 0);
    root.Children.Add(label);

    TextBox titleTextBox = new()
    {
      Text = currentValue,
      MinWidth = 340,
    };
    Grid.SetRow(titleTextBox, 1);
    root.Children.Add(titleTextBox);

    StackPanel buttons = new()
    {
      Orientation = Orientation.Horizontal,
      HorizontalAlignment = HorizontalAlignment.Right,
      Margin = new Thickness(0, 14, 0, 0),
    };
    Button cancelButton = new()
    {
      Content = "Cancel",
      Style = TryFindResource("AppActionButtonStyle") as Style,
      IsCancel = true,
      Margin = new Thickness(0, 0, 8, 0),
    };
    Button renameButton = new()
    {
      Content = confirmText,
      Style = TryFindResource("AppPrimaryActionButtonStyle") as Style,
      IsDefault = true,
    };
    renameButton.Click += (_, _) =>
    {
      dialog.DialogResult = true;
      dialog.Close();
    };
    buttons.Children.Add(cancelButton);
    buttons.Children.Add(renameButton);
    Grid.SetRow(buttons, 2);
    root.Children.Add(buttons);

    dialog.Content = new Border
    {
      Background = ResolveBrush("Brush.Surface.Subtle"),
      BorderBrush = ResolveBrush("Brush.Border.Subtle"),
      BorderThickness = new Thickness(1),
      CornerRadius = new CornerRadius(14),
      Child = root,
    };
    WindowThemeBehavior.SetIsEnabled(dialog, true);
    dialog.Loaded += (_, _) =>
    {
      titleTextBox.Focus();
      titleTextBox.SelectAll();
    };

    return dialog.ShowDialog() == true
      ? titleTextBox.Text
      : null;
  }

  private void ShowAboutDialog()
  {
    AboutWindow dialog = new()
    {
      Owner = this,
    };
    _ = dialog.ShowDialog();
  }

  private Brush ResolveBrush(string resourceKey)
  {
    return TryFindResource(resourceKey) as Brush
      ?? Brushes.Transparent;
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "This shared async UI boundary must observe unexpected history-query failures instead of terminating the dispatcher.")]
  private async Task RefreshHistorySidebarsAsync(CancellationToken cancellationToken = default)
  {
    WorkbenchHistoryViewState? state;
    try
    {
      state = await historyController
        .RefreshAsync(CurrentSettings, SidebarView.SearchText, cancellationToken)
        .ConfigureAwait(true);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (ObjectDisposedException) when (disposed)
    {
      return;
    }
    if (state is null)
    {
      return;
    }

    WorkbenchHistoryInteractionResult reconciliation = historyInteractionController.ReconcileRefresh(state);
    SidebarView.RenderHistory(
      state,
      reconciliation.State.SelectedDictationEntryId,
      reconciliation.State.SelectedChatConversationId);
    RenderPresentation();
  }

  private async Task ApplyHistoryInteractionResultAsync(
    WorkbenchHistoryInteractionResult result,
    bool chatStatus = false)
  {
    ApplyHistoryInteractionResult(result, chatStatus);
    if (result.ShouldRefresh)
    {
      await RefreshHistorySidebarsAsync().ConfigureAwait(true);
    }
  }

  private void ApplyHistoryInteractionResult(
    WorkbenchHistoryInteractionResult result,
    bool chatStatus = false)
  {
    HeaderView.Title = result.State.ChatTitle;
    switch (result.ComposerDirective)
    {
      case WorkbenchHistoryComposerDirective.Replace:
        ComposerView.PromptText = result.ComposerText ?? string.Empty;
        break;
      case WorkbenchHistoryComposerDirective.ClearComposerContent:
        ComposerView.ClearPrompt();
        break;
      case WorkbenchHistoryComposerDirective.ClearHistorySourcedContent:
        ComposerView.ClearPrompt();
        break;
      case WorkbenchHistoryComposerDirective.None:
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(result), result.ComposerDirective, "Unsupported composer directive.");
    }

    if (result.ShouldFocusComposer)
    {
      ComposerView.FocusPromptAtEnd();
    }

    if (result.ShouldRefreshPendingFiles)
    {
      RefreshPendingFileChips();
    }

    if (result.ShouldSelectChatModel)
    {
      SelectChatModel(chatController.Selection);
    }

    if (result.ShouldRenderChat)
    {
      if (chatController.Messages.Count == 0)
      {
        ChatTranscriptView.SetWelcomeMessage(WelcomeMessageSelector.Select());
        ChatTranscriptView.Clear();
      }

      RenderChatTranscript();
    }

    if (!string.IsNullOrWhiteSpace(result.StatusMessage))
    {
      if (chatStatus)
      {
        SetChatStatus(result.StatusMessage);
      }
      else
      {
        UpdateVisualState(result.StatusMessage);
      }
    }

    RenderPresentation();
  }

  private void UpdateVisualState(string status)
  {
    RenderPresentation(status);
  }

  private void ShowTransientSessionOutcome(string status)
  {
    OperationalStatusView.ShowTransientOutcome();
    UpdateVisualState(status);
  }

  private void OnTransientOutcomeExpired(object? sender, EventArgs e)
  {
    RenderPresentation();
  }

  private void ClearTransientSessionOutcome()
  {
    OperationalStatusView.ClearTransientOutcome();
    RenderPresentation();
  }

  private void SetChatStatus(string status)
  {
    HeaderView.SetChatStatus(status, HeaderView.IsChatStatusVisible);
    RenderPresentation();
  }

  private void ApplyChatTypography()
  {
    ApplyWorkbenchZoom();
    double fontSize = ChatTextSizePolicy.Normalize(CurrentSettings.ChatOutputFontSize);
    FontFamily fontFamily = ChatTypefaceCatalog.CreateFontFamily(CurrentSettings.ChatTypefaceId);

    ChatTranscriptView.ApplyTypography(fontSize, fontFamily);
    ComposerView.ApplyTypography(fontSize, fontFamily);
    ExpandedPromptView.ApplyTypography(fontSize, fontFamily);
    ApplyPaperView();
    QueuePromptExpansionButtonRefresh();
  }

  private void RefreshChatTextSizeMenuState()
  {
    QuickSettingsView.SetTextSize(ChatTextSizePolicy.Normalize(CurrentSettings.ChatOutputFontSize));
  }

  private void SetPromptExpanded(bool isExpanded)
  {
    ExpandedPromptView.SetOpen(isExpanded);
    RenderPresentation();
  }

  private void QueuePromptExpansionButtonRefresh()
  {
    if (!disposed)
    {
      ComposerView.QueueExpansionButtonRefresh(ExpandedPromptView.IsOpen);
    }
  }

  private void CancelActiveChatRequest()
  {
    WorkbenchChatOperationKind? cancelledOperation = chatController.CancelActiveOperation();
    if (cancelledOperation is null)
    {
      return;
    }

    SetChatStatus(cancelledOperation == WorkbenchChatOperationKind.ModelSetup
      ? "Stopping model setup…"
      : "Stopping response…");
  }

  private void RenderPresentation(string? sessionStatusText = null)
  {
    WorkbenchPresentationState state = WorkbenchPresentationReducer.Reduce(new WorkbenchPresentationSnapshot(
      dictationController.State,
      operationSession.IsBusy,
      operationSession.IsImportingFiles,
      chatController.IsBusy,
      chatController.CanCancel,
      chatController.IsModelInstalled,
      chatController.IsRuntimeReady,
      chatController.Messages.Count > 0,
      ComposerView.HasPrompt,
      LocalGreetingResponder.IsStandaloneGreeting(ComposerView.PromptText),
      historyInteractionController.State.HasSelectedDictation,
      readAloudController.IsPreparing,
      readAloudController.CanStop,
      SidebarView.IsSidebarVisible,
      ExpandedPromptView.IsOpen,
      OperationalStatusView.IsTransientOutcomeVisible,
      string.Equals(chatController.Selection.ProviderId, ChatProviderIds.OllamaLocal, StringComparison.OrdinalIgnoreCase),
      sessionStatusText ?? OperationalStatusView.SessionStatusText,
      ComposerView.HotkeyStatusTextValue));
    OperationalStatusView.SetSessionStatus(
      state.Dictation.SessionStatusText,
      ThemeResourceResolver.ResolveStatusBrush(this, state.Dictation.SessionStatusKind));
    ConversationLayout.VerticalAlignment = chatController.Messages.Count == 0
      ? VerticalAlignment.Center : VerticalAlignment.Stretch;
    HeaderView.Render(state.Header);
    HeaderView.SetChatStatus(HeaderView.ChatStatusValue, state.OperationalStatus.IsVisible);
    ComposerView.Render(state.Composer, state.Dictation);
    QuickSettingsView.Render(state.QuickSettings);
    SidebarView.Render(state.Sidebar);
    OperationalStatusView.Render(state.OperationalStatus);
  }

  private void SetHotkeyStatus(string status)
  {
    ComposerView.SetHotkeyStatus(status);
    RenderPresentation();
  }

}

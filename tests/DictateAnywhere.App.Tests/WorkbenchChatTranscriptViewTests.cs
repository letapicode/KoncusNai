using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

[Xunit.Trait("Category", "WindowsWpf")]
public sealed class WorkbenchChatTranscriptViewTests
{
  [Xunit.Fact]
  public void QuickSettingsView_OwnsThemeStatusAndVisibilityPresentation()
  {
    RunOnSta(() =>
    {
      WorkbenchQuickSettingsView view = new();
      TextBlock themeGlyph = Xunit.Assert.IsType<TextBlock>(view.FindName("ThemeGlyph"));
      TextBlock themeMode = Xunit.Assert.IsType<TextBlock>(view.FindName("ThemeMode"));
      TextBlock transcriptionStatus = Xunit.Assert.IsType<TextBlock>(view.FindName("TranscriptionStatus"));
      TextBlock textSizeValue = Xunit.Assert.IsType<TextBlock>(view.FindName("TextSizeValue"));
      Slider textSizeSlider = Xunit.Assert.IsType<Slider>(view.FindName("TextSizeSlider"));
      Button modelAction = Xunit.Assert.IsType<Button>(view.FindName("DownloadChatModel"));

      Xunit.Assert.Equal(Visibility.Collapsed, view.Visibility);
      Xunit.Assert.False(view.IsOpen);

      view.SetThemeState(isDark: true);
      view.SetTranscriptionStatus("Speech model ready");
      view.SetTextSize(22);
      view.SetChatModelState(
        canInteract: true,
        isInstalled: false,
        isRuntimeReady: false,
        usesExternalRuntime: false);
      view.Show();

      Xunit.Assert.True(view.IsOpen);
      Xunit.Assert.Equal(Visibility.Visible, view.Visibility);
      Xunit.Assert.Equal("\uE708", themeGlyph.Text);
      Xunit.Assert.Equal("Dark Mode", themeMode.Text);
      Xunit.Assert.Equal("Switch to light mode", themeMode.ToolTip);
      Xunit.Assert.Equal("Speech model ready", transcriptionStatus.Text);
      Xunit.Assert.Equal(22, textSizeSlider.Value);
      Xunit.Assert.Equal("22 px", textSizeValue.Text);
      Xunit.Assert.True(modelAction.IsEnabled);
      Xunit.Assert.Equal(Visibility.Visible, modelAction.Visibility);

      view.Hide();
      Xunit.Assert.False(view.IsOpen);
      Xunit.Assert.Equal(Visibility.Collapsed, view.Visibility);
    });
  }

  [Xunit.Fact]
  public void QuickSettingsView_ShowMovesFocusIntoTheContainedSurface()
  {
    RunOnSta(() =>
    {
      Window? window = null;
      try
      {
        Button underlying = new() { Content = "Underlying Workbench action" };
        WorkbenchQuickSettingsView view = new();
        Grid host = new();
        host.Children.Add(underlying);
        host.Children.Add(view);
        window = new Window
        {
          Width = 900,
          Height = 700,
          Left = -10_000,
          Top = -10_000,
          ShowActivated = true,
          ShowInTaskbar = false,
          Content = host,
        };
        window.Show();
        window.Activate();
        window.UpdateLayout();
        Xunit.Assert.True(underlying.Focus());

        view.Show();
        window.UpdateLayout();
        view.Dispatcher.Invoke(DispatcherPriority.Input, new Action(() => { }));

        Button advanced = Xunit.Assert.IsType<Button>(view.FindName("AdvancedButton"));
        Xunit.Assert.True(advanced.IsKeyboardFocused);
        for (int i = 0; i < 10; i++)
        {
          Xunit.Assert.True(((UIElement)Keyboard.FocusedElement).MoveFocus(
            new TraversalRequest(FocusNavigationDirection.Next)));
          Xunit.Assert.True(view.IsKeyboardFocusWithin, "Tab escaped Quick Settings into covered Workbench content.");
        }
      }
      finally
      {
        window?.Close();
      }
    });
  }

  [Xunit.Fact]
  public void QuickSettingsView_HideCancelsDeferredFocusEntry()
  {
    RunOnSta(() =>
    {
      Window? window = null;
      try
      {
        Button underlying = new() { Content = "Underlying Workbench action" };
        WorkbenchQuickSettingsView view = new();
        Grid host = new();
        host.Children.Add(underlying);
        host.Children.Add(view);
        window = new Window
        {
          Width = 900,
          Height = 700,
          Left = -10_000,
          Top = -10_000,
          ShowActivated = true,
          ShowInTaskbar = false,
          Content = host,
        };
        window.Show();
        window.Activate();
        window.UpdateLayout();
        Xunit.Assert.True(underlying.Focus());

        view.Show();
        view.Hide();
        view.Dispatcher.Invoke(DispatcherPriority.Input, new Action(() => { }));

        Xunit.Assert.True(underlying.IsKeyboardFocused);
        Xunit.Assert.False(view.IsKeyboardFocusWithin);
      }
      finally
      {
        window?.Close();
      }
    });
  }

  [Xunit.Fact]
  public void SidebarView_OwnsHistoryRenderingStatusAndSelectionActionState()
  {
    RunOnSta(() =>
    {
      WorkbenchSidebarView view = new();
      WorkbenchHistoryViewState state = new(
        string.Empty,
        [],
        [],
        "No matching dictation.",
        "No matching chat.",
        IsAvailable: true);

      view.RenderHistory(state, selectedDictationEntryId: null, selectedChatConversationId: null);
      view.SetChatSelectionActionState(canInteract: true);
      view.SetDictationActionState(canInteract: true);

      ListBox dictations = Xunit.Assert.IsType<ListBox>(view.FindName("HistoryListBox"));
      ListBox chats = Xunit.Assert.IsType<ListBox>(view.FindName("ChatHistoryListBox"));
      TextBlock dictationStatus = Xunit.Assert.IsType<TextBlock>(view.FindName("HistorySidebarStatusTextBlock"));
      TextBlock chatStatus = Xunit.Assert.IsType<TextBlock>(view.FindName("ChatHistoryStatusTextBlock"));
      TextBox search = Xunit.Assert.IsType<TextBox>(view.FindName("HistorySearchTextBox"));

      Xunit.Assert.Empty(dictations.Items);
      Xunit.Assert.Empty(chats.Items);
      Xunit.Assert.Equal("Your chats and dictations will appear here.", dictationStatus.Text);
      Xunit.Assert.Equal(Visibility.Visible, dictationStatus.Visibility);
      Xunit.Assert.Empty(chatStatus.Text);
      Xunit.Assert.Null(view.FindName("NewChatButton"));
      Xunit.Assert.Null(view.FindName("ExportChatButton"));

      view.Render(new WorkbenchSidebarPresentation(IsVisible: true, CanInteract: false));
      Xunit.Assert.False(search.IsEnabled);
      Xunit.Assert.False(dictations.IsEnabled);
      Xunit.Assert.False(chats.IsEnabled);
      view.Render(new WorkbenchSidebarPresentation(IsVisible: true, CanInteract: true));
      Xunit.Assert.True(search.IsEnabled);
      Xunit.Assert.True(dictations.IsEnabled);
      Xunit.Assert.True(chats.IsEnabled);
    });
  }

  [Xunit.Fact]
  public void ComposerConversationActions_HaveConciseHoverHelpAndAccessibleNames()
  {
    RunOnSta(() =>
    {
      WorkbenchComposerView view = new();
      Button newChat = Xunit.Assert.IsType<Button>(view.FindName("NewChatButton"));
      Button addFile = Xunit.Assert.IsType<Button>(view.FindName("AddFile"));
      Button readDocument = Xunit.Assert.IsType<Button>(view.FindName("ReadDocument"));
      Button export = Xunit.Assert.IsType<Button>(view.FindName("ExportChatButton"));

      Xunit.Assert.Equal("New chat", newChat.ToolTip);
      Xunit.Assert.Equal("Add file", addFile.ToolTip);
      Xunit.Assert.Equal("Open Reading Studio", readDocument.ToolTip);
      Xunit.Assert.Equal("Export this chat", export.ToolTip);
      Xunit.Assert.Equal("New chat", System.Windows.Automation.AutomationProperties.GetName(newChat));
      Xunit.Assert.Equal("Add file", System.Windows.Automation.AutomationProperties.GetName(addFile));
      Xunit.Assert.Equal("Open Reading Studio", System.Windows.Automation.AutomationProperties.GetName(readDocument));
      Xunit.Assert.Equal("Export this chat", System.Windows.Automation.AutomationProperties.GetName(export));
      Xunit.Assert.True(ToolTipService.GetShowOnDisabled(newChat));
      Xunit.Assert.True(ToolTipService.GetShowOnDisabled(addFile));
      Xunit.Assert.True(ToolTipService.GetShowOnDisabled(readDocument));
      Xunit.Assert.True(ToolTipService.GetShowOnDisabled(export));
    });
  }

  [Xunit.Fact]
  public void ComposerConversationActions_RetainLayoutWhenKeyboardFocusMoves()
  {
    RunOnSta(() =>
    {
      Window? window = null;
      try
      {
        WorkbenchComposerView view = new();
        view.SetConversationActionState(canStartNewChat: true, canExportChat: true);
        window = new Window
        {
          Width = 720,
          Height = 220,
          Left = -10_000,
          Top = -10_000,
          ShowActivated = true,
          ShowInTaskbar = false,
          Content = view,
        };
        window.Show();
        window.Activate();
        window.UpdateLayout();

        Button newChat = Xunit.Assert.IsType<Button>(view.FindName("NewChatButton"));
        Button export = Xunit.Assert.IsType<Button>(view.FindName("ExportChatButton"));
        Size newChatSize = newChat.RenderSize;
        Size exportSize = export.RenderSize;

        Xunit.Assert.True(newChat.Focus());
        Xunit.Assert.True(newChat.IsKeyboardFocused);
        window.UpdateLayout();
        Xunit.Assert.Equal(newChatSize, newChat.RenderSize);

        Xunit.Assert.True(export.Focus());
        Xunit.Assert.True(export.IsKeyboardFocused);
        window.UpdateLayout();
        Xunit.Assert.Equal(exportSize, export.RenderSize);
      }
      finally
      {
        window?.Close();
      }
    });
  }

  [Xunit.Fact]
  public void ComposerConversationActions_UseRequestedOrderIconsAndIndependentState()
  {
    RunOnSta(() =>
    {
      WorkbenchComposerView view = new();
      Button newChat = Xunit.Assert.IsType<Button>(view.FindName("NewChatButton"));
      Button addFile = Xunit.Assert.IsType<Button>(view.FindName("AddFile"));
      Button readDocument = Xunit.Assert.IsType<Button>(view.FindName("ReadDocument"));
      Button export = Xunit.Assert.IsType<Button>(view.FindName("ExportChatButton"));

      WrapPanel actions = Xunit.Assert.IsType<WrapPanel>(newChat.Parent);
      Xunit.Assert.Equal(new UIElement[] { newChat, addFile, readDocument, export }, actions.Children.Cast<UIElement>().Take(4));
      StackPanel newChatContent = Xunit.Assert.IsType<StackPanel>(newChat.Content);
      Xunit.Assert.Equal("New chat", Xunit.Assert.IsType<TextBlock>(newChatContent.Children[1]).Text);
      StackPanel importContent = Xunit.Assert.IsType<StackPanel>(addFile.Content);
      Xunit.Assert.Equal("Import", Xunit.Assert.IsType<TextBlock>(importContent.Children[1]).Text);
      Xunit.Assert.Equal("\uE74E", Xunit.Assert.IsType<TextBlock>(export.Content).Text);

      view.SetConversationActionState(canStartNewChat: true, canExportChat: false);
      Xunit.Assert.True(newChat.IsEnabled);
      Xunit.Assert.False(export.IsEnabled);

      view.SetConversationActionState(canStartNewChat: false, canExportChat: true);
      Xunit.Assert.False(newChat.IsEnabled);
      Xunit.Assert.True(export.IsEnabled);
    });
  }

  [Xunit.Fact]
  public void ComposerConversationActions_ForwardIntentExactlyOnce()
  {
    RunOnSta(() =>
    {
      WorkbenchComposerView view = new();
      Button newChat = Xunit.Assert.IsType<Button>(view.FindName("NewChatButton"));
      Button export = Xunit.Assert.IsType<Button>(view.FindName("ExportChatButton"));
      int newChatCount = 0;
      int exportCount = 0;
      view.NewChatClicked += (_, _) => newChatCount++;
      view.ExportChatClicked += (_, _) => exportCount++;
      view.SetConversationActionState(canStartNewChat: true, canExportChat: true);

      newChat.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
      export.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

      Xunit.Assert.Equal(1, newChatCount);
      Xunit.Assert.Equal(1, exportCount);
    });
  }

  [Xunit.Fact]
  public void SidebarSearch_CaretUsesFreedDecorationSpaceAndSupportsRtl()
  {
    RunOnSta(() =>
    {
      WorkbenchSidebarView view = new();
      TextBox search = Xunit.Assert.IsType<TextBox>(view.FindName("HistorySearchTextBox"));
      Window host = new() { Width = 320, Height = 500, Content = view, ShowInTaskbar = false };
      host.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DictateAnywhere.App;component/Theming/DesignTokens.xaml", UriKind.Relative) });
      host.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DictateAnywhere.App;component/Theming/ControlStyles.xaml", UriKind.Relative) });
      try
      {
        host.Show();
        search.Text = "Query";
        search.ApplyTemplate();
        host.UpdateLayout();
        TextBlock icon = Xunit.Assert.IsType<TextBlock>(search.Template.FindName("SearchIcon", search));
        FrameworkElement decoration = Xunit.Assert.IsAssignableFrom<FrameworkElement>(search.Template.FindName("SearchDecoration", search));
        Rect caret = search.GetRectFromCharacterIndex(0, trailingEdge: false);

        Xunit.Assert.False(icon.IsHitTestVisible);
        Xunit.Assert.Equal(Visibility.Collapsed, decoration.Visibility);
        Xunit.Assert.InRange(caret.X, 10d, 16d);

        search.FlowDirection = FlowDirection.RightToLeft;
        search.Text = "بحث";
        search.CaretIndex = 0;
        host.UpdateLayout();
        Rect rightToLeftCaret = search.GetRectFromCharacterIndex(0, trailingEdge: false);
        Xunit.Assert.True(rightToLeftCaret.X > search.ActualWidth / 2d);
        Xunit.Assert.Equal(Visibility.Collapsed, decoration.Visibility);
      }
      finally
      {
        host.Close();
      }
    });
  }

  [Xunit.Fact]
  public void Composer_ConditionalActions_AppearOnlyInTheirValidPresentationStates()
  {
    RunOnSta(() =>
    {
      WorkbenchComposerView view = new();
      Button expand = Xunit.Assert.IsType<Button>(view.FindName("ExpandPrompt"));
      Button send = Xunit.Assert.IsType<Button>(view.FindName("Send"));
      Button stop = Xunit.Assert.IsType<Button>(view.FindName("StopChat"));

      foreach (string text in new[] { string.Empty, "   ", "Short prompt" })
      {
        view.PromptText = text;
        view.RefreshExpansionButton(expansionOverlayVisible: false);
        Xunit.Assert.Equal(Visibility.Collapsed, expand.Visibility);
      }

      view.PromptText = "Line one\nLine two";
      view.RefreshExpansionButton(expansionOverlayVisible: false);
      Xunit.Assert.Equal(Visibility.Visible, expand.Visibility);

      view.PromptText = new string('a', 121);
      view.RefreshExpansionButton(expansionOverlayVisible: false);
      Xunit.Assert.Equal(Visibility.Visible, expand.Visibility);
      view.RefreshExpansionButton(expansionOverlayVisible: true);
      Xunit.Assert.Equal(Visibility.Collapsed, expand.Visibility);

      view.PromptText = "Ready prompt";
      view.SetChatActionState(
        chatIsBusy: false,
        canCancelChat: false,
        isImportingFiles: false,
        canEditPrompt: true,
        canInteract: true,
        canSend: false,
        canUseQuickLocalReply: false,
        isPreparingSpeech: false,
        canStopSpeech: false);
      Xunit.Assert.Equal(Visibility.Visible, send.Visibility);
      Xunit.Assert.False(send.IsEnabled);
      Xunit.Assert.Equal(Visibility.Collapsed, stop.Visibility);

      view.SetChatActionState(
        chatIsBusy: false,
        canCancelChat: false,
        isImportingFiles: false,
        canEditPrompt: true,
        canInteract: true,
        canSend: true,
        canUseQuickLocalReply: false,
        isPreparingSpeech: false,
        canStopSpeech: false);
      Xunit.Assert.True(send.IsEnabled);

      view.SetChatActionState(
        chatIsBusy: true,
        canCancelChat: true,
        isImportingFiles: false,
        canEditPrompt: true,
        canInteract: false,
        canSend: false,
        canUseQuickLocalReply: false,
        isPreparingSpeech: false,
        canStopSpeech: false);
      Xunit.Assert.Equal(Visibility.Collapsed, send.Visibility);
      Xunit.Assert.False(send.IsEnabled);
      Xunit.Assert.Equal(Visibility.Visible, stop.Visibility);
      Xunit.Assert.True(stop.IsEnabled);

      view.DisposePresentation();
    });
  }

  [Xunit.Fact]
  public void QuickSettingsView_OwnsTextSizeCommitDebounceAndDisposal()
  {
    RunOnSta(() =>
    {
      WorkbenchQuickSettingsView view = new();
      Slider slider = Xunit.Assert.IsType<Slider>(view.FindName("TextSizeSlider"));
      int? previewed = null;
      int? committed = null;
      view.ChatTextSizePreviewRequested += size => previewed = size;
      view.ChatTextSizeCommitRequested += size => committed = size;

      view.SetTextSize(15);
      slider.Value = 19;

      Xunit.Assert.True(view.IsTextSizeCommitPending);
      Xunit.Assert.Equal(19, previewed);
      Xunit.Assert.Null(committed);
      view.CommitPendingTextSize();
      Xunit.Assert.Equal(19, committed);

      slider.Value = 21;
      view.DisposePresentation();
      Xunit.Assert.False(view.IsTextSizeCommitPending);
      view.CommitPendingTextSize();
      Xunit.Assert.Equal(19, committed);
    });
  }

  [Xunit.Fact]
  public void OperationalStatusView_OwnsTransientOutcomeTimerDisposal()
  {
    RunOnSta(() =>
    {
      WorkbenchOperationalStatusView view = new();
      view.ShowTransientOutcome();

      Xunit.Assert.True(view.IsTransientOutcomeVisible);
      Xunit.Assert.True(view.IsTransientOutcomeTimerRunning);

      view.DisposePresentation();

      Xunit.Assert.False(view.IsTransientOutcomeVisible);
      Xunit.Assert.False(view.IsTransientOutcomeTimerRunning);

      view.ShowTransientOutcome();

      Xunit.Assert.False(view.IsTransientOutcomeVisible);
      Xunit.Assert.False(view.IsTransientOutcomeTimerRunning);
    });
  }

  [Xunit.Fact]
  public void OperationalStatusView_ExpiresTransientOutcomeExactlyOnce()
  {
    RunOnSta(() =>
    {
      WorkbenchOperationalStatusView view = new();
      int expirationCount = 0;
      view.TransientOutcomeExpired += (_, _) => expirationCount++;
      view.ShowTransientOutcome();

      view.ExpireTransientOutcome();

      Xunit.Assert.False(view.IsTransientOutcomeVisible);
      Xunit.Assert.False(view.IsTransientOutcomeTimerRunning);
      Xunit.Assert.Equal(1, expirationCount);
      view.DisposePresentation();
      view.ExpireTransientOutcome();
      Xunit.Assert.Equal(1, expirationCount);
    });
  }

  [Xunit.Fact]
  public void InlineSettingsView_OwnsLoadingAndVisibilityState()
  {
    RunOnSta(() =>
    {
      WorkbenchInlineSettingsView view = new();

      view.ShowLoading();

      Grid loading = Xunit.Assert.IsType<Grid>(view.FindName("LoadingState"));
      Xunit.Assert.Equal(Visibility.Visible, view.Visibility);
      Xunit.Assert.Equal(Visibility.Visible, loading.Visibility);

      view.Hide();

      Xunit.Assert.Equal(Visibility.Collapsed, view.Visibility);
      Xunit.Assert.Equal(Visibility.Collapsed, loading.Visibility);
    });
  }

  [Xunit.Fact]
  public void InlineSettingsView_LoadingStateReceivesFocusAndReleasesItWhenHidden()
  {
    RunOnSta(() =>
    {
      Window? window = null;
      try
      {
        WorkbenchInlineSettingsView view = new();
        window = new Window
        {
          Width = 900,
          Height = 700,
          Left = -10_000,
          Top = -10_000,
          ShowActivated = true,
          ShowInTaskbar = false,
          Content = view,
        };
        window.Show();
        window.Activate();
        view.ShowLoading();
        window.UpdateLayout();
        view.Dispatcher.Invoke(DispatcherPriority.Input, new Action(() => { }));

        Grid loading = Xunit.Assert.IsType<Grid>(view.FindName("LoadingState"));
        Xunit.Assert.True(loading.IsKeyboardFocused);

        view.Hide();
        Xunit.Assert.False(view.IsKeyboardFocusWithin);
      }
      finally
      {
        window?.Close();
      }
    });
  }

  [Xunit.Fact]
  public void WorkbenchFocusRestorationRejectsDetachedHiddenAndDisabledTargets()
  {
    RunOnSta(() =>
    {
      Button detached = new() { Content = "Detached" };
      Xunit.Assert.False(TextboxWorkbenchWindow.TryFocus(detached));

      Window? window = null;
      try
      {
        Button target = new() { Content = "Return target" };
        window = new Window
        {
          Width = 300,
          Height = 200,
          Left = -10_000,
          Top = -10_000,
          ShowActivated = true,
          ShowInTaskbar = false,
          Content = target,
        };
        window.Show();
        window.Activate();
        window.UpdateLayout();

        target.IsEnabled = false;
        Xunit.Assert.False(TextboxWorkbenchWindow.TryFocus(target));
        target.IsEnabled = true;
        target.Visibility = Visibility.Collapsed;
        Xunit.Assert.False(TextboxWorkbenchWindow.TryFocus(target));
        target.Visibility = Visibility.Visible;
        window.UpdateLayout();
        Xunit.Assert.True(TextboxWorkbenchWindow.TryFocus(target));
        Xunit.Assert.True(target.IsKeyboardFocused);
      }
      finally
      {
        window?.Close();
      }
    });
  }

  [Xunit.Fact]
  public void WorkbenchOverlayInteractionBoundaryPreventsCoveredControlsFromReceivingFocus()
  {
    RunOnSta(() =>
    {
      Window? window = null;
      try
      {
        Button covered = new() { Content = "Covered Workbench action" };
        Grid interactionSurface = new();
        interactionSurface.Children.Add(covered);
        Button overlayAction = new() { Content = "Active overlay action" };
        Grid host = new();
        host.Children.Add(interactionSurface);
        host.Children.Add(overlayAction);
        window = new Window
        {
          Width = 400,
          Height = 300,
          Left = -10_000,
          Top = -10_000,
          ShowActivated = true,
          ShowInTaskbar = false,
          Content = host,
        };
        window.Show();
        window.Activate();
        window.UpdateLayout();

        TextboxWorkbenchWindow.SetWorkbenchInteractionEnabled(interactionSurface, isEnabled: false);
        Xunit.Assert.True(overlayAction.Focus());
        Xunit.Assert.False(covered.Focus());
        Xunit.Assert.True(overlayAction.IsKeyboardFocused);

        TextboxWorkbenchWindow.SetWorkbenchInteractionEnabled(interactionSurface, isEnabled: true);
        Xunit.Assert.True(covered.Focus());
        Xunit.Assert.True(covered.IsKeyboardFocused);
      }
      finally
      {
        window?.Close();
      }
    });
  }

  [Xunit.Fact]
  public void HeaderAndOperationalStatusViews_OwnPresentationState()
  {
    RunOnSta(() =>
    {
      WorkbenchHeaderView header = new();
      WorkbenchOperationalStatusView status = new();

      header.Title = "Project notes";
      header.SetChatStatus("Ready", isVisible: true);
      header.SetDictationActionState(
        newSessionEnabled: true,
        saveEnabled: false,
        deleteEnabled: true);
      status.SetVisible(true);
      status.SetProgress(42);
      status.SetModelReadinessText("Model ready");
      status.SetSessionStatus("Idle", Brushes.Green);
      status.SetModelDetail("Local model");

      Border chatStatus = Xunit.Assert.IsType<Border>(header.FindName("ChatStatus"));
      Button newSession = Xunit.Assert.IsType<Button>(header.FindName("NewSession"));
      Button save = Xunit.Assert.IsType<Button>(header.FindName("SaveHistoryEdits"));
      Button delete = Xunit.Assert.IsType<Button>(header.FindName("DeleteHistorySession"));
      ProgressBar progress = Xunit.Assert.IsType<ProgressBar>(status.FindName("ModelProgress"));
      TextBlock readiness = Xunit.Assert.IsType<TextBlock>(status.FindName("ModelReadiness"));
      TextBlock detail = Xunit.Assert.IsType<TextBlock>(status.FindName("ModelDetail"));

      Xunit.Assert.Equal("Project notes", header.Title);
      Xunit.Assert.Equal("Ready", header.ChatStatusValue);
      Xunit.Assert.Equal(Visibility.Visible, chatStatus.Visibility);
      Xunit.Assert.True(newSession.IsEnabled);
      Xunit.Assert.False(save.IsEnabled);
      Xunit.Assert.True(delete.IsEnabled);
      Xunit.Assert.Equal(Visibility.Visible, status.Visibility);
      Xunit.Assert.False(progress.IsIndeterminate);
      Xunit.Assert.Equal(42, progress.Value);
      Xunit.Assert.Equal("Model ready", readiness.Text);
      Xunit.Assert.Equal("Idle", status.SessionStatusText);
      Xunit.Assert.Equal("Local model", detail.Text);
    });
  }

  [Xunit.Fact]
  public void ExpandedPromptView_OwnsTextVisibilityAndTypography()
  {
    RunOnSta(() =>
    {
      WorkbenchExpandedPromptView view = new();
      int textChangedCount = 0;
      view.PromptTextChanged += (_, _) => textChangedCount++;

      view.SetText("Draft prompt");
      view.ApplyTypography(19, new FontFamily("Segoe UI"));
      view.SetOpen(true);

      Xunit.Assert.Equal("Draft prompt", view.Text);
      Xunit.Assert.Equal(0, textChangedCount);
      Xunit.Assert.Equal(19, view.PromptElement.FontSize);
      Xunit.Assert.True(view.IsOpen);

      view.PromptElement.AppendText(" updated");
      view.Hide();

      Xunit.Assert.Equal(1, textChangedCount);
      Xunit.Assert.False(view.IsOpen);
    });
  }

  [Xunit.Fact]
  public void CompactAndExpandedPrompts_SynchronizeWithoutRecursiveTextEvents()
  {
    RunOnSta(() =>
    {
      WorkbenchComposerView compact = new();
      WorkbenchExpandedPromptView expanded = new();
      int compactEvents = 0;
      int expandedEvents = 0;
      compact.PromptTextChanged += (_, _) =>
      {
        compactEvents++;
        expanded.SetText(compact.PromptText);
      };
      expanded.PromptTextChanged += (_, _) =>
      {
        expandedEvents++;
        compact.PromptText = expanded.Text;
      };

      compact.PromptElement.AppendText("First");

      Xunit.Assert.Equal("First", expanded.Text);
      Xunit.Assert.Equal(1, compactEvents);
      Xunit.Assert.Equal(0, expandedEvents);

      expanded.PromptElement.AppendText(" second");

      Xunit.Assert.Equal("First second", compact.PromptText);
      Xunit.Assert.Equal(1, compactEvents);
      Xunit.Assert.Equal(1, expandedEvents);
    });
  }

  [Xunit.Fact]
  public void ComposerView_PresentationMethods_OwnComposerVisualState()
  {
    RunOnSta(() =>
    {
      WorkbenchComposerView view = new();
      ChatFileAttachment attachment = new("id", "C:\\notes.txt", "notes.txt", "context", false);

      view.SetPendingFiles([attachment]);
      view.SetSpeechPreparation(true, "Preparing speech");
      view.ApplyTypography(18, new FontFamily("Segoe UI"));
      view.SetCompactComposerVisible(false);

      Xunit.Assert.True(view.ArePendingFilesVisible);
      Xunit.Assert.Equal(1, view.PendingFileCount);
      Xunit.Assert.True(view.IsSpeechPreparationVisible);
      Xunit.Assert.Equal("Preparing speech", view.SpeechPreparationStatusText);
      Xunit.Assert.Equal(18, view.PromptFontSize);
      Xunit.Assert.Equal(18, view.PlaceholderFontSize);
      Xunit.Assert.False(view.IsCompactComposerVisible);

      view.SetPendingFiles([]);
      view.SetSpeechPreparation(false, "ignored");

      Xunit.Assert.False(view.ArePendingFilesVisible);
      Xunit.Assert.False(view.IsSpeechPreparationVisible);
    });
  }

  [Xunit.Fact]
  public void ComposerPrompt_DelegatesKeyboardFocusCueToLayoutNeutralComposerBoundary()
  {
    RunOnSta(() =>
    {
      WorkbenchComposerView view = new();
      SolidColorBrush restingBrush = new(Colors.Gray);
      SolidColorBrush focusBrush = new(Colors.DodgerBlue);
      view.Resources["Brush.Border.Subtle"] = restingBrush;
      view.Resources["Brush.Control.Primary"] = focusBrush;
      Window? window = null;
      try
      {
        window = new Window
        {
          Width = 760,
          Height = 300,
          Left = -10_000,
          Top = -10_000,
          ShowInTaskbar = false,
          Content = view,
        };
        window.Show();
        window.Activate();
        window.UpdateLayout();

        TextBox prompt = view.PromptElement;
        Border composer = Xunit.Assert.IsType<Border>(view.FindName("CompactComposer"));
        Size originalComposerSize = composer.RenderSize;
        Thickness originalComposerThickness = composer.BorderThickness;
        Size originalPromptSize = prompt.RenderSize;
        Thickness originalPromptPadding = prompt.Padding;

        Xunit.Assert.Null(prompt.FocusVisualStyle);
        Xunit.Assert.True(prompt.Focus());
        view.UpdatePromptFocusPresentation(Keyboard.PrimaryDevice);
        window.UpdateLayout();

        Xunit.Assert.True(view.IsPromptKeyboardFocusVisible);
        Xunit.Assert.Same(focusBrush, composer.BorderBrush);
        Xunit.Assert.Equal(originalComposerSize, composer.RenderSize);
        Xunit.Assert.Equal(originalComposerThickness, composer.BorderThickness);
        Xunit.Assert.Equal(originalPromptSize, prompt.RenderSize);
        Xunit.Assert.Equal(originalPromptPadding, prompt.Padding);

        view.SetDictationState(
          new WorkbenchViewModel("Recording", UiStatusKind.Pending, "Recording", false, true),
          isRecording: true);
        Xunit.Assert.True(view.IsPromptKeyboardFocusVisible);
        Xunit.Assert.Same(focusBrush, composer.BorderBrush);

        view.UpdatePromptFocusPresentation(InputManager.Current.PrimaryMouseDevice);
        Xunit.Assert.False(view.IsPromptKeyboardFocusVisible);
        Xunit.Assert.Same(restingBrush, composer.BorderBrush);

        Xunit.Assert.True(WorkbenchComposerView.ShouldRevealPromptFocusForKey(
          Key.System,
          Key.Space,
          ModifierKeys.Alt));
        Xunit.Assert.True(WorkbenchComposerView.ShouldRevealPromptFocusForKey(
          Key.LeftAlt,
          Key.None,
          ModifierKeys.None));
        Xunit.Assert.False(WorkbenchComposerView.ShouldRevealPromptFocusForKey(
          Key.A,
          Key.None,
          ModifierKeys.None));

        view.SetCompactComposerVisible(false);
        Xunit.Assert.False(view.IsPromptKeyboardFocusVisible);
      }
      finally
      {
        window?.Close();
      }
    });
  }

  [Xunit.Fact]
  public void ComposerView_UsesAvailableWidthUpToItsPreferredMaximumWithoutClippingActions()
  {
    RunOnSta(() =>
    {
      foreach (double availableWidth in new[] { 680d, 719d, 720d, 900d })
      {
        WorkbenchComposerView view = new();
        view.Measure(new Size(availableWidth, 500));
        view.Arrange(new Rect(0, 0, availableWidth, view.DesiredSize.Height));
        view.UpdateLayout();

        Border compactComposer = Xunit.Assert.IsType<Border>(view.FindName("CompactComposer"));
        Button newChat = Xunit.Assert.IsType<Button>(view.FindName("NewChatButton"));
        Button addFile = Xunit.Assert.IsType<Button>(view.FindName("AddFile"));
        Button readDocument = Xunit.Assert.IsType<Button>(view.FindName("ReadDocument"));
        Button export = Xunit.Assert.IsType<Button>(view.FindName("ExportChatButton"));
        Button record = Xunit.Assert.IsType<Button>(view.FindName("Record"));
        Button send = Xunit.Assert.IsType<Button>(view.FindName("Send"));
        double expectedWidth = Math.Min(availableWidth, 720d);
        Xunit.Assert.InRange(Math.Abs(compactComposer.ActualWidth - expectedWidth), 0d, 0.01d);
        Button[] orderedActions = [newChat, addFile, readDocument, export];
        double previousRight = 0d;
        foreach (Button action in orderedActions)
        {
          Point origin = action.TranslatePoint(new Point(0, 0), compactComposer);
          Xunit.Assert.True(origin.X >= previousRight);
          Xunit.Assert.True(origin.X + action.ActualWidth <= compactComposer.ActualWidth);
          previousRight = origin.X + action.ActualWidth;
        }

        Xunit.Assert.True(record.TranslatePoint(new Point(record.ActualWidth, 0), compactComposer).X <= compactComposer.ActualWidth);
        Xunit.Assert.True(send.TranslatePoint(new Point(send.ActualWidth, 0), compactComposer).X <= compactComposer.ActualWidth);
      }
    });
  }

  [Xunit.Fact]
  public void ChatProgressView_UpdateAndHide_OwnsVisualLifecycle()
  {
    RunOnSta(() =>
    {
      WorkbenchChatProgressView view = new();

      view.Update(TimeSpan.FromSeconds(2), new FontFamily("Segoe UI"));

      Xunit.Assert.Equal(Visibility.Visible, view.Visibility);
      Xunit.Assert.True(view.LetterCount > 0);

      view.Hide();

      Xunit.Assert.Equal(Visibility.Collapsed, view.Visibility);
      Xunit.Assert.Equal(0, view.LetterCount);
    });
  }

  [Xunit.Fact]
  public void ChatProgressView_StartAndStop_OwnTimerLifecycle()
  {
    RunOnSta(() =>
    {
      WorkbenchChatProgressView view = new();

      view.Start(new FontFamily("Segoe UI"));

      Xunit.Assert.True(view.IsRunning);
      TimeSpan elapsed = view.Stop();
      Xunit.Assert.False(view.IsRunning);
      Xunit.Assert.True(elapsed >= TimeSpan.Zero);
      Xunit.Assert.Equal(Visibility.Collapsed, view.Visibility);
    });
  }

  [Xunit.Fact]
  public void ComposerView_DisposeStopsProgressAndCancelsDeferredExpansionRendering()
  {
    RunOnSta(() =>
    {
      WorkbenchComposerView view = new();
      view.QueueExpansionButtonRefresh(overlayVisible: false);
      view.StartProgress(new FontFamily("Segoe UI"));

      Xunit.Assert.True(view.IsExpansionRefreshQueued);
      Xunit.Assert.True(view.IsProgressRunning);

      view.DisposePresentation();

      Xunit.Assert.False(view.IsExpansionRefreshQueued);
      Xunit.Assert.False(view.IsProgressRunning);
      Xunit.Assert.Throws<ObjectDisposedException>(() => view.StartProgress(new FontFamily("Segoe UI")));
    });
  }

  [Xunit.Fact]
  public void Render_SystemOnlyConversation_ShowsEmptyState()
  {
    RunOnSta(() =>
    {
      WorkbenchChatTranscriptView view = new();

      view.Render(
        [new ChatMessage(ChatMessageRoles.System, "private context", DateTimeOffset.UtcNow)],
        _ => Brushes.Black,
        _ => { },
        _ => { },
        _ => { });

      Xunit.Assert.True(view.IsEmptyStateVisible);
      Xunit.Assert.Equal(Visibility.Collapsed, view.TranscriptElement.Visibility);
      Xunit.Assert.Empty(view.TranscriptElement.Document.Blocks);
    });
  }

  [Xunit.Fact]
  public void Render_VisibleMessages_ReplacesDocumentAndHidesEmptyState()
  {
    RunOnSta(() =>
    {
      WorkbenchChatTranscriptView view = new();
      ChatMessage[] messages =
      [
        new ChatMessage(ChatMessageRoles.User, "Hello", DateTimeOffset.UtcNow),
        new ChatMessage(ChatMessageRoles.Assistant, "Hi there", DateTimeOffset.UtcNow),
      ];

      view.Render(messages, _ => Brushes.Black, _ => { }, _ => { }, _ => { });
      int firstRenderBlockCount = view.TranscriptElement.Document.Blocks.Count;
      view.Render(messages, _ => Brushes.Black, _ => { }, _ => { }, _ => { });

      Xunit.Assert.Equal(Visibility.Visible, view.TranscriptElement.Visibility);
      Xunit.Assert.True(firstRenderBlockCount > 0);
      Xunit.Assert.Equal(firstRenderBlockCount, view.TranscriptElement.Document.Blocks.Count);
    });
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "The STA test-thread boundary captures and rethrows any test failure on the xUnit thread.")]
  private static void RunOnSta(Action action)
  {
    Exception? failure = null;
    Thread thread = new(() =>
    {
      try
      {
        action();
      }
      catch (Exception ex)
      {
        failure = ex;
      }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    bool completed = thread.Join(TimeSpan.FromSeconds(15));
    Xunit.Assert.True(completed, "STA test thread timed out.");
    if (failure is not null)
    {
      throw failure;
    }
  }
}

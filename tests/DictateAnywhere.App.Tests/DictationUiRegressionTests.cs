using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using DictateAnywhere.App.Settings;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;
using Xunit;

namespace DictateAnywhere.App.Tests;

[Collection(WpfApplicationCollection.Name)]
public sealed class DictationUiRegressionTests
{
  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void ComposerDraftTrackingCoversProgrammaticInsertionUndoAndClearing() => WpfTestSta.Run(() =>
  {
    WorkbenchComposerView composer = new();
    WpfTestSta.Cleanup(composer.DisposePresentation);
    List<string> edits = [];
    composer.DraftChanged += edits.Add;
    composer.PromptText = "loaded";
    TextBox editor = composer.PromptElement;
    editor.Select(0, 6);
    editor.SelectedText = "inserted";
    editor.Undo();
    composer.ClearPrompt();
    Assert.Contains("loaded", edits);
    Assert.Contains("inserted", edits);
    Assert.Equal(string.Empty, edits[^1]);
  });

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void MicrophoneChromeIsTransparentWithStableHitTargetAndFocus() => WpfTestSta.Run(() =>
  {
    WorkbenchComposerView composer = new();
    WpfTestSta.Cleanup(composer.DisposePresentation);
    Button mic = Assert.IsType<Button>(composer.FindName("Record"));
    mic.ApplyTemplate();
    Border chrome = Assert.IsType<Border>(mic.Template.FindName("Chrome", mic));
    Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(chrome.Background).Color);
    Assert.Null(mic.FocusVisualStyle);
    composer.SetDictationState(WorkbenchViewModelFactory.Create(WorkbenchSessionState.Recording, "", ""), true);
    Assert.NotSame(Brushes.Transparent, mic.Background);
    composer.SetDictationState(WorkbenchViewModelFactory.Create(WorkbenchSessionState.Transcribing, "", ""), false);
    Assert.Same(Brushes.Transparent, mic.Background);
    Assert.Equal(36, mic.Width);
    Assert.Equal(36, mic.Height);
    Assert.Contains(mic.Template.Triggers.OfType<Trigger>(), t => t.Property == UIElement.IsKeyboardFocusedProperty);
  });

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void DisabledEditorRejectsCapturedDictationWithoutMutatingDraft() => WpfTestSta.Run(() =>
  {
    TextBox editor = new() { Text = "draft" };
    WorkbenchDictationTarget target = new(editor, () => true);
    editor.IsEnabled = false;
    Assert.False(target.TryInsert("late transcript"));
    Assert.Equal("draft", editor.Text);
  });

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void ImportFeedbackSharesAvailableWidthWithoutFixedPanelOverflow() => WpfTestSta.Run(() =>
  {
    WorkbenchOperationalStatusView view = new();
    WpfTestSta.Cleanup(view.DisposePresentation);
    view.SetVisible(true);
    view.SetModelReadinessText("Transcribing imported audio with a long descriptive filename");
    view.SetSessionStatus("Import is in progress", Brushes.Black);
    view.SetCompactDictationFeedback(false);
    view.Measure(new Size(260, double.PositiveInfinity));
    view.Arrange(new Rect(0, 0, 260, view.DesiredSize.Height));
    Assert.Equal(260, Assert.IsType<Grid>(view.FindName("StatusRow")).ActualWidth);
    Assert.True(view.DesiredSize.Width <= 260);
  });

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void ContextTargetSelectionDoesNotActivateHistoryIncludingRowActionClick() => WpfTestSta.Run(() =>
  {
    WorkbenchSidebarView sidebar = new();
    Window host = new() { Content = sidebar, Width = 320, Height = 600,
      Left = -10000, Top = -10000, ShowInTaskbar = false };
    WpfTestSta.Cleanup(host.Close);
    ChatHistoryRecord record = new("chat", "Chat", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "provider", "model", []);
    sidebar.RenderHistory(new WorkbenchHistoryViewState("", [], [ChatHistoryItemViewModel.FromRecord(record)], "", "", true), null, null);
    int activations = 0;
    sidebar.ChatHistorySelectionChanged += (_, _) => activations++;
    host.Show(); host.UpdateLayout();
    ListBox list = Assert.IsType<ListBox>(sidebar.FindName("ChatHistoryListBox"));
    WpfTestSta.Cleanup(() => { if (list.ContextMenu is { } menu) menu.IsOpen = false; });
    ListBoxItem item = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(0));
    sidebar.ChatHistoryPreviewMouseRightButtonDown += (_, e) => sidebar.PrepareChatContextMenuSelection(e.OriginalSource as DependencyObject);
    item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
      { RoutedEvent = Mouse.PreviewMouseDownEvent });
    Assert.True(item.IsSelected);
    Assert.Equal(0, activations);
    list.UnselectAll(); activations = 0;
    Button action = Descendants(item).OfType<Button>().Single();
    action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Assert.True(item.IsSelected);
    Assert.Equal(0, activations);
    ChatHistoryRecord other = record with { ConversationId = "other", Title = "Other" };
    sidebar.RenderHistory(new WorkbenchHistoryViewState("", [],
      [ChatHistoryItemViewModel.FromRecord(record), ChatHistoryItemViewModel.FromRecord(other)], "", "", true), null, "other");
    Assert.Equal("chat", sidebar.SelectedChat?.Record.ConversationId);
    Assert.Equal(0, activations);
    list.ContextMenu!.IsOpen = false;
    list.UnselectAll(); activations = 0;
    list.SelectedIndex = 0;
    Assert.Equal(1, activations);
  });

  private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
  {
    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
    {
      DependencyObject child = VisualTreeHelper.GetChild(parent, i);
      yield return child;
      foreach (DependencyObject nested in Descendants(child)) yield return nested;
    }
  }
  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void SettingsNavigation_RespectsFocusedControlsAndModifiers() => WpfTestSta.Run(() =>
  {
    foreach (Key key in new[] { Key.Home, Key.End })
    {
      Assert.True(SettingsPageNavigation.ShouldScroll(key, ModifierKeys.None, new Button(), false));
      Assert.True(SettingsPageNavigation.ShouldScroll(key, ModifierKeys.Control, new Button(), false));
      Assert.False(SettingsPageNavigation.ShouldScroll(key, ModifierKeys.None, new TextBox(), false));
      Assert.False(SettingsPageNavigation.ShouldScroll(key, ModifierKeys.Control, new ComboBox(), false));
      Assert.False(SettingsPageNavigation.ShouldScroll(key, ModifierKeys.None, new ListBox(), false));
      Assert.False(SettingsPageNavigation.ShouldScroll(key, ModifierKeys.None, new PasswordBox(), false));
      Assert.False(SettingsPageNavigation.ShouldScroll(key, ModifierKeys.Shift, new Button(), false));
      Assert.False(SettingsPageNavigation.ShouldScroll(key, ModifierKeys.None, new Button(), true));
    }
    Assert.False(SettingsPageNavigation.ShouldScroll(Key.ImeProcessed, ModifierKeys.None, new Button(), false));
  });

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void ModelNotice_ReservesSpaceAndNeverChangesTheDraft() => WpfTestSta.Run(() =>
  {
    WorkbenchChatController chat = new(_ => throw new InvalidOperationException());
    WpfTestSta.Cleanup(() => chat.DisposeAsync().AsTask().GetAwaiter().GetResult());
    WorkbenchModelNoticeView notice = new();
    WorkbenchComposerView composer = new() { PromptText = "Unicode draft — नमस्ते\nsecond line" };
    WpfTestSta.Cleanup(composer.DisposePresentation);
    StackPanel host = new() { Width = 420 };
    host.Children.Add(notice);
    host.Children.Add(composer);
    chat.SetReadiness(false, false, false);
    chat.SetModelStatus("Ollama is not running. Start Ollama to check the installed model.");
    notice.Render(chat);
    host.Measure(new Size(420, 600)); host.Arrange(new Rect(0, 0, 420, 600));
    double before = composer.TranslatePoint(new Point(), host).Y;
    chat.SetReadiness(true, true);
    notice.Render(chat);
    host.Measure(new Size(420, 600)); host.Arrange(new Rect(0, 0, 420, 600));
    Assert.Equal(Visibility.Hidden, notice.Visibility);
    Assert.Equal(before, composer.TranslatePoint(new Point(), host).Y);
    Assert.Equal("Unicode draft — नमस्ते\nsecond line", composer.PromptText);
    notice.Render(chat, "Dictation saved to history; the draft changed.");
    Assert.Equal(Visibility.Visible, notice.Visibility);
    notice.Render(chat);
    Assert.Equal(Visibility.Hidden, notice.Visibility);
  });

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void LongNoticeAtNarrowWidthStaysBoundedAndActionRemainsReachable() => WpfTestSta.Run(() =>
  {
    WorkbenchChatController chat = new(_ => throw new InvalidOperationException());
    WpfTestSta.Cleanup(() => chat.DisposeAsync().AsTask().GetAwaiter().GetResult());
    chat.SetReadiness(false, false, false);
    chat.SetModelStatus(string.Concat(Enumerable.Repeat("Model status unavailable — कृपया पुनः प्रयास करें. ", 30)));
    WorkbenchModelNoticeView notice = new();
    notice.Render(chat);
    notice.Measure(new Size(280, double.PositiveInfinity));
    notice.Arrange(new Rect(0, 0, 280, notice.DesiredSize.Height));
    notice.UpdateLayout();
    Assert.InRange(notice.DesiredSize.Height, 44, 80);
    Grid grid = Assert.IsType<Grid>(notice.Content);
    Button action = grid.Children.OfType<Button>().Single();
    Assert.True(action.IsEnabled);
    Rect bounds = action.TransformToAncestor(notice).TransformBounds(new Rect(action.RenderSize));
    Assert.True(bounds.Right <= notice.ActualWidth);
  });

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void ExpiredStatusCannotReappearAndSidebarRowsAreCompact() => WpfTestSta.Run(() =>
  {
    WorkbenchOperationalStatusView status = new();
    WpfTestSta.Cleanup(status.DisposePresentation);
    status.SetSessionStatus("Old outcome", Brushes.Black);
    status.ShowTransientOutcome();
    status.ExpireTransientOutcome();
    Assert.Empty(status.SessionStatusText);
    Assert.False(status.IsTransientOutcomeVisible);
    WorkbenchSidebarView sidebar = new();
    ListBox list = Assert.IsType<ListBox>(sidebar.FindName("ChatHistoryListBox"));
    WpfTestSta.Cleanup(() => { if (list.ContextMenu is { } menu) menu.IsOpen = false; });
    ListBoxItem item = new() { Style = list.ItemContainerStyle };
    Assert.Equal(new Thickness(10, 3, 10, 3), item.Padding);
    Assert.Equal(34, item.MinHeight);
  });

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void ReplyActions_RenderWithThemeAndPaperResources() => WpfTestSta.Run(() =>
  {
    StackPanel samples = new();
    foreach ((string label, Brush background, bool paper) in new[] {
      ("Dark", (Brush)Brushes.DarkSlateGray, false), ("Light", (Brush)Brushes.WhiteSmoke, false),
      ("Paper", (Brush)Brushes.Beige, true) })
    {
      StackPanel row = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(12) };
      row.Children.Add(new TextBlock { Text = label, Width = 70, VerticalAlignment = VerticalAlignment.Center });
      foreach ((string tooltip, string glyph) in new[] { ("Copy code", "\uE8C8"), ("Read aloud", "\uE767"), ("Copy reply", "\uE8C8") })
        row.Children.Add(ChatMarkdownRenderer.CreateIconButton(tooltip, glyph, () => { }, isPaper: paper));
      Border surface = new() { Background = background, Child = row };
      surface.Resources["Brush.Text.Primary"] = paper || label == "Light" ? Brushes.Black : Brushes.White;
      surface.Resources["Brush.Text.Secondary"] = paper || label == "Light" ? Brushes.DimGray : Brushes.LightGray;
      surface.Resources["Brush.Paper.Comment"] = Brushes.DimGray;
      surface.Resources["Brush.Paper.Ink"] = Brushes.Black;
      samples.Children.Add(surface);
    }
    samples.Measure(new Size(280, 180));
    samples.Arrange(new Rect(0, 0, 280, 180));
    samples.UpdateLayout();
    RenderTargetBitmap bitmap = new(280, 180, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(samples);
    string? gallery = Environment.GetEnvironmentVariable("NOTYPE_UI_GALLERY");
    if (string.IsNullOrEmpty(gallery)) return;
    Directory.CreateDirectory(gallery);
    PngBitmapEncoder encoder = new();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using FileStream file = File.Create(Path.Combine(gallery, "dictation-reply-actions.png"));
    encoder.Save(file);
  });
  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void HistoryRefresh_RestoresSelectionWithoutActivatingEitherHistory() => WpfTestSta.Run(() =>
  {
    WorkbenchSidebarView sidebar = new();
    DictationHistoryRecord record = new(DateTimeOffset.UtcNow, "default", "provider", "model",
      "hello", "hello", TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, EntryId: "entry", SessionId: "session");
    ChatHistoryRecord chat = new("chat", "Chat", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
      "provider", "model", []);
    WorkbenchHistoryViewState state = new("", [HistoryItemViewModel.FromRecords([record])],
      [ChatHistoryItemViewModel.FromRecord(chat)], "", "", true);
    int dictationActivations = 0;
    int chatActivations = 0;
    sidebar.HistorySelectionChanged += (_, _) => dictationActivations++;
    sidebar.ChatHistorySelectionChanged += (_, _) => chatActivations++;
    sidebar.RenderHistory(state, record.EntryId, chat.ConversationId);
    // New query objects, arriving when the Workbench is idle, must not open history.
    sidebar.RenderHistory(state with { DictationItems = [HistoryItemViewModel.FromRecords([record])] },
      record.EntryId, chat.ConversationId);
    Assert.Equal(0, dictationActivations);
    Assert.Equal(0, chatActivations);
    ListBox list = Assert.IsType<ListBox>(sidebar.FindName("HistoryListBox"));
    list.UnselectAll();
    list.SelectedIndex = 0;
    Assert.Equal(2, dictationActivations);
    Assert.NotNull(sidebar.SelectedDictationGroup);
  });

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void ComposerInsertion_ReplacesSelectionOnceAndSupportsUndo() => WpfTestSta.Run(() =>
  {
    TextBox editor = new() { Text = "Hello old world", IsUndoEnabled = true };
    Window host = new() { Content = editor, Width = 300, Height = 100, Left = -10000, Top = -10000, ShowInTaskbar = false };
    WpfTestSta.Cleanup(host.Close);
    host.Show();
    host.UpdateLayout();
    editor.Select(6, 3);
    WorkbenchDictationTarget target = new(editor, () => true);
    Assert.True(target.TryInsert("new"));
    Assert.Equal("Hello new world", editor.Text);
    Assert.Equal(9, editor.CaretIndex);
    Assert.False(target.TryInsert("duplicate"));
    Assert.True(editor.Undo());
    Assert.Equal("Hello old world", editor.Text);
  });

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void ComposerInsertion_RejectsEditsAndChangedIdentity() => WpfTestSta.Run(() =>
  {
    TextBox editor = new() { Text = "draft" };
    WorkbenchDictationTarget target = new(editor, () => true);
    editor.Clear();
    Assert.False(target.TryInsert("hello"));
    Assert.Equal("", editor.Text);
    target = new(editor, () => false);
    Assert.False(target.TryInsert("another chat"));
  });

  [Theory]
  [Trait("Category", "WindowsWpf")]
  [InlineData(false, false)]
  [InlineData(true, false)]
  [InlineData(false, true)]
  public void ReplyActions_HaveTransparentChromeAndStableTooltip(bool paper, bool code) => WpfTestSta.Run(() =>
  {
    int copies = 0;
    Button button = ChatMarkdownRenderer.CreateIconButton("Copy code", "\uE8C8", () => copies++,
      paper, code);
    button.ApplyTemplate();
    Border chrome = Assert.IsType<Border>(button.Template.FindName("Chrome", button));
    Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(chrome.Background).Color);
    Assert.DoesNotContain(button.Template.Triggers.OfType<Trigger>(), trigger => trigger.Property == UIElement.IsMouseOverProperty);
    Assert.Null(chrome.Effect);
    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Assert.Equal(1, copies);
    Assert.Equal("Copy code", button.ToolTip);
    button.IsEnabled = false;
    Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(chrome.Background).Color);
  });

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void GlobalInsertion_UsesOwnedEditorAndNeverFallsBackAfterDraftChanges() => WpfTestSta.Run(() =>
  {
    TextBox editor = new() { Text = "Hello " };
    editor.Select(editor.Text.Length, 0);
    FakeExternalInsertion external = new();
    WorkbenchTextInsertionService service = new(external, () => new WorkbenchDictationTarget(editor, () => true));
    service.CaptureCurrentTarget();
    Task<InsertionResult> insertion = service.InsertAsync("world", InsertionMethod.ClipboardPaste, true);
    editor.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
    Assert.True(insertion.GetAwaiter().GetResult().Success);
    Assert.Equal("Hello world", editor.Text);
    Assert.Equal(0, external.Insertions);
    service.CaptureCurrentTarget();
    editor.Text = "newer draft";
    insertion = service.InsertAsync("stale", InsertionMethod.ClipboardPaste, true);
    editor.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
    Assert.Equal(InsertionBlockReason.TargetChanged, insertion.GetAwaiter().GetResult().BlockReason);
    Assert.Equal(0, external.Insertions);
    Assert.Equal("newer draft", editor.Text);
    service = new WorkbenchTextInsertionService(external, () => null);
    service.CaptureCurrentTarget();
    Assert.True(service.InsertAsync("external", InsertionMethod.ClipboardPaste, true).GetAwaiter().GetResult().Success);
    Assert.Equal(1, external.Insertions);
  });

  [Fact]
  public void RecoveryDismissalRetainsUnsavedRecordsAndStaleActionsCannotAffectNewerNotice()
  {
    WorkbenchRecoveryStore store = new();
    WorkbenchRecovery unsaved = store.Add("first", "not saved", false);
    Assert.True(store.Dismiss(unsaved.Id));
    Assert.Null(store.Current);
    Assert.Single(store.Pending);
    WorkbenchRecovery newer = store.Add("second", "saved", true);
    Assert.False(store.Dismiss(unsaved.Id, copied: true));
    Assert.Equal(newer, store.Current);
    Assert.True(store.Dismiss(newer.Id));
    Assert.Single(store.Pending);
    Assert.True(store.Select(unsaved.Id));
    Assert.Equal("first", store.Current!.Text);
    store.Hide();
    store.Add("third", "not saved", false);
    Assert.Equal(2, store.Pending.Count);
    Assert.True(store.Select(unsaved.Id));
    Assert.True(store.Dismiss(unsaved.Id, copied: true));
    Assert.False(store.Select(unsaved.Id));
    Assert.Equal("third", Assert.Single(store.Pending).Text);
  }

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void TargetReportsActualDraftChangeSeparatelyFromFocusOrAvailability() => WpfTestSta.Run(() =>
  {
    TextBox editor = new() { Text = "original" };
    WorkbenchInsertionOutcome permission = WorkbenchInsertionOutcome.Inserted;
    WorkbenchDictationTarget target = new(editor, () => permission);
    editor.Text = "modified";
    Assert.Equal(WorkbenchInsertionOutcome.DraftChanged, target.Insert("late"));
    permission = WorkbenchInsertionOutcome.FocusLost;
    Assert.Equal(WorkbenchInsertionOutcome.FocusLost, target.Insert("late"));
    Assert.Equal("modified", editor.Text);
  });

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void CodeWrapTooltipIsShortAndCopyRetainsOriginalWhitespaceAndUnicode() => WpfTestSta.Run(() =>
  {
    string source = "\tvar नमस्ते = \"" + new string('x', 220) + "\";  \n\t// second line  ";
    System.Windows.Documents.FlowDocument document = new();
    string? copied = null;
    ChatMarkdownRenderer.Append(document, "```java\n" + source + "\n```", Brushes.Black, onCopyCode: text => copied = text);
    System.Windows.Documents.Section section = Assert.IsType<System.Windows.Documents.Section>(document.Blocks.FirstBlock);
    System.Windows.Documents.BlockUIContainer container = Assert.IsType<System.Windows.Documents.BlockUIContainer>(section.Blocks.FirstBlock);
    Border border = Assert.IsType<Border>(container.Child);
    Grid grid = Assert.IsType<Grid>(border.Child);
    CheckBox wrap = grid.Children.OfType<CheckBox>().Single();
    Assert.Equal("Wrap long lines.", wrap.ToolTip);
    Button copy = grid.Children.OfType<Button>().Single();
    copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    string original = copied!;
    Assert.Contains("\tvar नमस्ते", original);
    Assert.Contains(";  ", original);
    foreach (bool enabled in new[] { true, false, true })
    {
      wrap.IsChecked = enabled;
      copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
      Assert.Equal(original, copied);
    }
  });

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void QueuedExpirationTickCannotHidePersistentReplacement() => WpfTestSta.Run(() =>
  {
    WorkbenchOperationalStatusView view = new();
    WpfTestSta.Cleanup(view.DisposePresentation);
    view.ShowTransientOutcome();
    view.ShowTransientOutcome(autoExpire: false);
    typeof(WorkbenchOperationalStatusView).GetMethod("OnTransientOutcomeTimerTick",
      System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(view, [view, EventArgs.Empty]);
    Assert.True(view.IsTransientOutcomeVisible);
  });

  [Fact]
  [Trait("Category", "WindowsWpf")]
  public void PressedRecoveryActionKeepsIdentityAcrossNoticeReplacement() => WpfTestSta.Run(() =>
  {
    WorkbenchModelNoticeView notice = new();
    Grid grid = Assert.IsType<Grid>(notice.Content);
    Button copy = grid.Children.OfType<Button>().Single();
    Guid first = Guid.NewGuid(), second = Guid.NewGuid();
    WorkbenchRecovery[] pending = [new(first, "first", "message", false), new(second, "second", "message", false)];
    notice.SetRecoveries(first, pending);
    copy.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
      { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
    notice.SetRecoveries(second, pending);
    Guid? clicked = null;
    notice.ActionRequested += (_, _) => clicked = notice.RecoveryIdentity;
    copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Assert.Equal(first, clicked);
    Assert.Equal(second, notice.RecoveryIdentity);
    Button dismiss = grid.Children.OfType<WrapPanel>().Single().Children.OfType<Button>()
      .Single(button => Equals(button.Content, "Dismiss"));
    notice.SetRecoveries(first, pending);
    dismiss.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
      { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
    notice.SetRecoveries(second, pending);
    notice.RecoveryDismissRequested += id => clicked = id;
    dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Assert.Equal(first, clicked);
  });

  private sealed class FakeExternalInsertion : ITextInsertionService
  {
    public int Insertions { get; private set; }
    public Task<InsertionResult> InsertAsync(string text, InsertionMethod preferredMethod, bool restoreClipboard,
      CancellationToken cancellationToken = default)
    {
      Insertions++;
      return Task.FromResult(InsertionResult.Verified(preferredMethod));
    }
  }

}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DictateAnywhere.App.Workbench;

/// <summary>Renders Workbench navigation and forwards user intent without owning workflow state.</summary>
public partial class WorkbenchSidebarView : UserControl
{
  public WorkbenchSidebarView()
  {
    InitializeComponent();
  }

  public event TextChangedEventHandler HistorySearchTextChanged
  {
    add => HistorySearchTextBox.TextChanged += value;
    remove => HistorySearchTextBox.TextChanged -= value;
  }

  public event SelectionChangedEventHandler ChatHistorySelectionChanged
  {
    add => ChatHistoryListBox.SelectionChanged += value;
    remove => ChatHistoryListBox.SelectionChanged -= value;
  }

  public event MouseButtonEventHandler ChatHistoryPreviewMouseRightButtonDown
  {
    add => ChatHistoryListBox.PreviewMouseRightButtonDown += value;
    remove => ChatHistoryListBox.PreviewMouseRightButtonDown -= value;
  }

  public event KeyEventHandler ChatHistoryPreviewKeyDown
  {
    add => ChatHistoryListBox.PreviewKeyDown += value;
    remove => ChatHistoryListBox.PreviewKeyDown -= value;
  }

  public event RoutedEventHandler RenameChatClicked
  {
    add => RenameChatMenuItem.Click += value;
    remove => RenameChatMenuItem.Click -= value;
  }

  public event RoutedEventHandler DeleteChatClicked
  {
    add => DeleteSelectedChatsMenuItem.Click += value;
    remove => DeleteSelectedChatsMenuItem.Click -= value;
  }

  public event SelectionChangedEventHandler HistorySelectionChanged
  {
    add => HistoryListBox.SelectionChanged += value;
    remove => HistoryListBox.SelectionChanged -= value;
  }

  public event MouseButtonEventHandler HistoryPreviewMouseRightButtonDown
  {
    add => HistoryListBox.PreviewMouseRightButtonDown += value;
    remove => HistoryListBox.PreviewMouseRightButtonDown -= value;
  }

  public event MouseButtonEventHandler HistoryPreviewMouseLeftButtonDown
  {
    add => HistoryListBox.PreviewMouseLeftButtonDown += value;
    remove => HistoryListBox.PreviewMouseLeftButtonDown -= value;
  }

  public event KeyEventHandler HistoryPreviewKeyDown
  {
    add => HistoryListBox.PreviewKeyDown += value;
    remove => HistoryListBox.PreviewKeyDown -= value;
  }

  public event RoutedEventHandler RenameHistoryClicked
  {
    add => RenameHistoryMenuItem.Click += value;
    remove => RenameHistoryMenuItem.Click -= value;
  }

  public event RoutedEventHandler DeleteHistoryClicked
  {
    add => DeleteSelectedHistoryMenuItem.Click += value;
    remove => DeleteSelectedHistoryMenuItem.Click -= value;
  }

  public event RoutedEventHandler SettingsClicked
  {
    add => SettingsButton.Click += value;
    remove => SettingsButton.Click -= value;
  }

  internal string SearchText => HistorySearchTextBox.Text;

  internal ChatHistoryItemViewModel? SelectedChat =>
    ChatHistoryListBox.SelectedItem as ChatHistoryItemViewModel;

  internal HistoryItemViewModel? SelectedDictationGroup =>
    HistoryListBox.SelectedItem as HistoryItemViewModel;

  internal int SelectedChatCount => ChatHistoryListBox.SelectedItems.Count;

  internal int SelectedDictationCount => HistoryListBox.SelectedItems.Count;

  internal IReadOnlyList<ChatHistoryItemViewModel> GetSelectedChats() =>
    ChatHistoryListBox.SelectedItems.OfType<ChatHistoryItemViewModel>().ToArray();

  internal IReadOnlyList<HistoryItemViewModel> GetSelectedDictationGroups() =>
    HistoryListBox.SelectedItems.OfType<HistoryItemViewModel>().ToArray();

  internal bool IsSelected(HistoryItemViewModel item) =>
    ReferenceEquals(HistoryListBox.SelectedItem, item);

  internal HistoryItemViewModel? GetDictationGroupAt(DependencyObject? source) =>
    FindAncestor<ListBoxItem>(source)?.DataContext as HistoryItemViewModel;

  internal void ClearDictationSelection() => HistoryListBox.UnselectAll();

  internal void RenderHistory(
    WorkbenchHistoryViewState state,
    string? selectedDictationEntryId,
    string? selectedChatConversationId)
  {
    ArgumentNullException.ThrowIfNull(state);
    HistoryListBox.ItemsSource = state.DictationItems;
    if (!string.IsNullOrWhiteSpace(selectedDictationEntryId))
    {
      HistoryListBox.SelectedItem = state.DictationItems.FirstOrDefault(item => item.Records.Any(record =>
        string.Equals(record.EntryId, selectedDictationEntryId, StringComparison.OrdinalIgnoreCase)));
    }

    ChatHistoryListBox.ItemsSource = state.ChatItems;
    if (!string.IsNullOrWhiteSpace(selectedChatConversationId))
    {
      ChatHistoryListBox.SelectedItem = state.ChatItems.FirstOrDefault(item =>
        string.Equals(
          item.Record.ConversationId,
          selectedChatConversationId,
          StringComparison.OrdinalIgnoreCase));
    }

    HistoryListBox.Visibility = state.DictationItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    ChatHistoryListBox.Visibility = state.ChatItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    HistoryListBoxHeading.Visibility = HistoryListBox.Visibility;
    ChatHistoryListBoxHeading.Visibility = ChatHistoryListBox.Visibility;
    SetDictationStatus(state.DictationStatus);
    SetChatStatus(state.ChatStatus);
    if (state.IsAvailable && state.DictationItems.Count == 0 && state.ChatItems.Count == 0)
    {
      SetChatStatus(string.Empty);
      SetDictationStatus(string.IsNullOrWhiteSpace(state.SearchText)
        ? "Your chats and dictations will appear here."
        : "No history matches your search. Clear search to see all entries.");
    }
  }

  internal void SetChatStatus(string status) => SetStatus(ChatHistoryStatusTextBlock, status);

  internal void SetDictationStatus(string status) => SetStatus(HistorySidebarStatusTextBlock, status);

  internal void SetChatSelectionActionState(bool canInteract)
  {
    RenameChatMenuItem.IsEnabled = canInteract && SelectedChatCount == 1;
    DeleteSelectedChatsMenuItem.IsEnabled = canInteract && SelectedChatCount > 0;
  }

  internal void SetDictationActionState(bool canInteract)
  {
    RenameHistoryMenuItem.IsEnabled = canInteract && SelectedDictationCount == 1;
    DeleteSelectedHistoryMenuItem.IsEnabled = canInteract && SelectedDictationCount > 0;
  }

  internal void SetSidebarVisible(bool isVisible) =>
    SidebarSurface.Visibility = (IsSidebarVisible = isVisible) ? Visibility.Visible : Visibility.Collapsed;

  internal bool IsSidebarVisible { get; private set; } = true;

  internal bool ToggleVisibility()
  {
    SetSidebarVisible(!IsSidebarVisible);
    return IsSidebarVisible;
  }

  internal void Render(WorkbenchSidebarPresentation state)
  {
    ArgumentNullException.ThrowIfNull(state);
    SetSidebarVisible(state.IsVisible);
    HistorySearchTextBox.IsEnabled = state.CanInteract;
    ChatHistoryListBox.IsEnabled = state.CanInteract;
    HistoryListBox.IsEnabled = state.CanInteract;
    SetChatSelectionActionState(state.CanInteract);
    SetDictationActionState(state.CanInteract);
  }

  internal bool PrepareChatContextMenuSelection(DependencyObject? source) =>
    PrepareContextMenuSelection(ChatHistoryListBox, source);

  internal bool PrepareDictationContextMenuSelection(DependencyObject? source) =>
    PrepareContextMenuSelection(HistoryListBox, source);

  internal Border Surface => SidebarSurface;
  internal Button SettingsAction => SettingsButton;

  private void OnRowActionsClicked(object sender, RoutedEventArgs e)
  {
    if (sender is not Button button || FindAncestor<ListBox>(button) is not { } list || list.ContextMenu is not { } menu)
    {
      return;
    }
    if (!PrepareContextMenuSelection(list, button)) { return; }
    SetChatSelectionActionState(ChatHistoryListBox.IsEnabled);
    SetDictationActionState(HistoryListBox.IsEnabled);
    menu.PlacementTarget = button;
    menu.IsOpen = true;
    e.Handled = true;
  }

  private static void SetStatus(TextBlock textBlock, string? status)
  {
    textBlock.Text = status ?? string.Empty;
    textBlock.Visibility = string.IsNullOrWhiteSpace(status)
      ? Visibility.Collapsed
      : Visibility.Visible;
  }

  private static bool PrepareContextMenuSelection(ListBox listBox, DependencyObject? source)
  {
    if (FindAncestor<ListBoxItem>(source) is not { } item)
    {
      return false;
    }

    if (!item.IsSelected)
    {
      listBox.SelectedItems.Clear();
      item.IsSelected = true;
    }

    item.Focus();
    return true;
  }

  private static T? FindAncestor<T>(DependencyObject? source)
    where T : DependencyObject
  {
    DependencyObject? current = source;
    while (current is not null)
    {
      if (current is T match)
      {
        return match;
      }

      current = VisualTreeHelper.GetParent(current);
    }

    return null;
  }
}

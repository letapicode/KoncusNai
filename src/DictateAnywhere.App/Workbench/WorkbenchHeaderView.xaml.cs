using System.Windows;
using System.Windows.Controls;

namespace DictateAnywhere.App.Workbench;

/// <summary>Owns Workbench header presentation and forwards header intent.</summary>
public partial class WorkbenchHeaderView : UserControl
{
  public WorkbenchHeaderView()
  {
    InitializeComponent();
    NewSession.Click += (_, args) => NewSessionClicked?.Invoke(this, args);
    SaveHistoryEdits.Click += (_, args) => SaveHistoryEditsClicked?.Invoke(this, args);
    DeleteHistorySession.Click += (_, args) => DeleteHistorySessionClicked?.Invoke(this, args);
  }

  public event RoutedEventHandler? NewSessionClicked;
  public event RoutedEventHandler? SaveHistoryEditsClicked;
  public event RoutedEventHandler? DeleteHistorySessionClicked;

  internal string Title
  {
    get => ChatTitle.Text;
    set => ChatTitle.Text = value ?? string.Empty;
  }

  internal string ChatStatusValue => ChatStatusText.Text;
  internal bool IsChatStatusVisible => ChatStatus.Visibility == Visibility.Visible;

  internal void SetChatStatus(string status, bool isVisible)
  {
    ChatStatusText.Text = status ?? string.Empty;
    ChatStatus.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
  }

  internal void HideSelectionMetadata()
  {
    SelectionMetadata.Text = string.Empty;
    SelectionMetadata.Visibility = Visibility.Collapsed;
  }

  internal void SetDictationActionState(
    bool newSessionEnabled,
    bool saveEnabled,
    bool deleteEnabled)
  {
    NewSession.IsEnabled = newSessionEnabled;
    SaveHistoryEdits.IsEnabled = saveEnabled;
    DeleteHistorySession.IsEnabled = deleteEnabled;
  }

  internal void Render(WorkbenchHeaderPresentation state)
  {
    ArgumentNullException.ThrowIfNull(state);
    SetDictationActionState(
      state.CanStartNewSession,
      state.CanSaveSelectedDictation,
      state.CanDeleteSelectedDictation);
  }

}

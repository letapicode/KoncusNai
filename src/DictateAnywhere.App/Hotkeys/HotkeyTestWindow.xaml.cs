using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace DictateAnywhere.App.Hotkeys;

public partial class HotkeyTestWindow : Window
{
  private const int MaxEntries = 200;
  private readonly ObservableCollection<string> entries = [];

  public HotkeyTestWindow()
  {
    InitializeComponent();
    EventsListBox.ItemsSource = entries;
  }

  private void OnPreviewKeyDown(object sender, KeyEventArgs e)
  {
    Log("Down", e);
  }

  private void OnPreviewKeyUp(object sender, KeyEventArgs e)
  {
    Log("Up", e);
  }

  private void OnClearClicked(object sender, RoutedEventArgs e)
  {
    entries.Clear();
  }

  private void OnCloseClicked(object sender, RoutedEventArgs e)
  {
    Close();
  }

  private void Log(string phase, KeyEventArgs e)
  {
    Key key = e.Key == Key.System ? e.SystemKey : e.Key;
    int virtualKey = KeyInterop.VirtualKeyFromKey(key);
    string virtualKeyHex = virtualKey.ToString("X2", CultureInfo.InvariantCulture);
    string timestamp = DateTimeOffset.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
    string line = $"{timestamp} | {phase} | Key={key} | VK=0x{virtualKeyHex} | Modifiers={Keyboard.Modifiers}";
    entries.Add(line);

    if (entries.Count > MaxEntries)
    {
      entries.RemoveAt(0);
    }

    EventsListBox.ScrollIntoView(entries[^1]);
  }
}

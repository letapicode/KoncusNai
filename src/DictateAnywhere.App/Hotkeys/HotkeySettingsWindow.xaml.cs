using System;
using System.IO;
using System.Windows;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Hotkeys;

namespace DictateAnywhere.App.Hotkeys;

public partial class HotkeySettingsWindow : Window
{
  private readonly ISettingsStore settingsStore;
  private readonly IHotkeyRegistrationValidator validator;
  private AppSettings currentSettings = AppSettings.Default;

  public HotkeySettingsWindow(ISettingsStore settingsStore, IHotkeyRegistrationValidator validator)
  {
    this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    this.validator = validator ?? throw new ArgumentNullException(nameof(validator));

    InitializeComponent();
    HotkeyCaptureControl.RegistrationValidator = this.validator;
    HotkeyCaptureControl.HotkeyChanged += OnHotkeyChanged;
  }

  private async void OnLoaded(object sender, RoutedEventArgs e)
  {
    try
    {
      currentSettings = await settingsStore.LoadAsync().ConfigureAwait(true);
      HotkeyCaptureControl.SetBinding(currentSettings.Hotkey);
      PersistStatusTextBlock.Text = "Loaded saved hotkey.";
      PersistStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 93, 33));
    }
    catch (IOException ex)
    {
      PersistStatusTextBlock.Text = $"Failed to load settings: {ex.Message}";
      PersistStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(138, 47, 0));
    }
    catch (UnauthorizedAccessException ex)
    {
      PersistStatusTextBlock.Text = $"Failed to load settings: {ex.Message}";
      PersistStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(138, 47, 0));
    }
    catch (InvalidOperationException ex)
    {
      PersistStatusTextBlock.Text = $"Failed to load settings: {ex.Message}";
      PersistStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(138, 47, 0));
    }
  }

  private async void OnHotkeyChanged(object? sender, HotkeyBinding binding)
  {
    try
    {
      currentSettings = currentSettings with
      {
        Hotkey = binding,
      };

      await settingsStore.SaveAsync(currentSettings).ConfigureAwait(true);
      PersistStatusTextBlock.Text = $"Saved: {HotkeyFormatter.ToDisplayString(binding)}";
      PersistStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 93, 33));
    }
    catch (IOException ex)
    {
      PersistStatusTextBlock.Text = $"Failed to save settings: {ex.Message}";
      PersistStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(138, 47, 0));
    }
    catch (UnauthorizedAccessException ex)
    {
      PersistStatusTextBlock.Text = $"Failed to save settings: {ex.Message}";
      PersistStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(138, 47, 0));
    }
    catch (InvalidOperationException ex)
    {
      PersistStatusTextBlock.Text = $"Failed to save settings: {ex.Message}";
      PersistStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(138, 47, 0));
    }
  }

  private void OnHotkeyTestClicked(object sender, RoutedEventArgs e)
  {
    HotkeyTestWindow testWindow = new()
    {
      Owner = this,
    };
    _ = testWindow.ShowDialog();
  }

  private void OnCloseClicked(object sender, RoutedEventArgs e)
  {
    Close();
  }
}

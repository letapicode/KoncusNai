using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Hotkeys;

namespace DictateAnywhere.App.Hotkeys;

public partial class HotkeyCaptureControl : UserControl
{
  private bool isCapturing;
  private int validationVersion;

  public HotkeyCaptureControl()
  {
    InitializeComponent();
    SetBinding(AppSettings.Default.Hotkey);
    SetStatus(string.Empty);
  }

  public HotkeyBinding CurrentBinding { get; private set; } = AppSettings.Default.Hotkey;

  public IHotkeyRegistrationValidator? RegistrationValidator { get; set; }

  public event EventHandler<HotkeyBinding>? HotkeyChanged;

  public void SetBinding(HotkeyBinding binding)
  {
    CurrentBinding = binding;
    HotkeyTextBox.Text = HotkeyFormatter.ToDisplayString(binding);
  }

  private void OnCaptureClicked(object sender, System.Windows.RoutedEventArgs e)
  {
    isCapturing = true;
    CaptureButton.Content = "Press keys...";
    SetStatus("Press the shortcut you want to use.");
    Keyboard.Focus(this);
  }

  private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
  {
    if (!isCapturing)
    {
      return;
    }

    e.Handled = true;

    Key key = e.Key == Key.System ? e.SystemKey : e.Key;
    if (!HotkeyCaptureUtility.TryCreateBinding(key, Keyboard.Modifiers, out HotkeyBinding binding, out string? errorMessage))
    {
      SetStatus(errorMessage ?? "Invalid hotkey.");
      return;
    }

    await ValidateAndApplyAsync(binding).ConfigureAwait(true);
  }

  private async Task ValidateAndApplyAsync(HotkeyBinding binding)
  {
    int version = Interlocked.Increment(ref validationVersion);

    HotkeyRegistrationResult validationResult = new(true, null);
    if (RegistrationValidator is not null)
    {
      validationResult = await RegistrationValidator.ValidateAsync(binding).ConfigureAwait(true);
    }

    if (version != validationVersion)
    {
      return;
    }

    if (!validationResult.Success)
    {
      SetStatus(validationResult.ErrorMessage ?? "Windows rejected this hotkey.");
      return;
    }

    SetBinding(binding);
    SetStatus(string.Empty);
    isCapturing = false;
    CaptureButton.Content = "Change";
    HotkeyChanged?.Invoke(this, binding);
  }

  private void SetStatus(string message)
  {
    StatusTextBlock.Text = message;
    StatusTextBlock.Visibility = string.IsNullOrWhiteSpace(message)
      ? System.Windows.Visibility.Collapsed
      : System.Windows.Visibility.Visible;
  }
}

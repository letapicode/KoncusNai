using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.App.Benchmarking;
using DictateAnywhere.App.Composition;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Hotkeys;

namespace DictateAnywhere.App.Settings;

public partial class SettingsWindow : Window
{
  private readonly SettingsPanel settingsPanel;
  private int zoomPercent = 100;

  internal SettingsWindow(
    ISettingsStore settingsStore,
    IHotkeyRegistrationValidator validator,
    IModelManager modelManager,
    IBenchmarkService benchmarkService,
    IAudioInputDeviceService audioInputDeviceService,
    ISettingsFileTransferService settingsFileTransferService,
    ISettingsFileDialogService settingsFileDialogService,
    LocalTranscriptionProviderRegistry transcriptionProviderRegistry,
    IDiagnostics diagnostics)
  {
    AppThemeManager.ApplyThemeResources(AppThemeManager.CurrentPreference);
    InitializeComponent();

    settingsPanel = new SettingsPanel(
      settingsStore,
      validator,
      modelManager,
      benchmarkService,
      audioInputDeviceService,
      settingsFileTransferService,
      settingsFileDialogService,
      transcriptionProviderRegistry,
      diagnostics);
    settingsPanel.SettingsSaved += OnSettingsPanelSaved;
    settingsPanel.BackRequested += OnSettingsPanelBackRequested;
    SettingsPanelHost.Content = settingsPanel;
    PreviewKeyDown += OnZoomKeyDown;
  }

  public event EventHandler? SettingsSaved;

  private void OnZoomKeyDown(object sender, KeyEventArgs e)
  {
    if (settingsPanel.IsCapturingHotkey || e.IsRepeat || e.Key == Key.ImeProcessed) return;
    int? delta = WorkbenchZoomShortcut.Delta(e.Key, Keyboard.Modifiers);
    if (delta is null) return;
    e.Handled = true;
    zoomPercent = delta == 0 ? 100 : Math.Clamp(zoomPercent + delta.Value, 80, 150);
    settingsPanel.LayoutTransform = new ScaleTransform(zoomPercent / 100d, zoomPercent / 100d);
  }

  private void OnSettingsPanelSaved(object? sender, EventArgs e)
  {
    SettingsSaved?.Invoke(this, EventArgs.Empty);
  }

  private void OnSettingsPanelBackRequested(object? sender, EventArgs e)
  {
    Close();
  }

}

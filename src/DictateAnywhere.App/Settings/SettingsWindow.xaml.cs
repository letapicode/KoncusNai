using System;
using System.Windows;
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
  }

  public event EventHandler? SettingsSaved;

  private void OnSettingsPanelSaved(object? sender, EventArgs e)
  {
    SettingsSaved?.Invoke(this, EventArgs.Empty);
  }

  private void OnSettingsPanelBackRequested(object? sender, EventArgs e)
  {
    Close();
  }

}

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DictateAnywhere.App.Composition;
using DictateAnywhere.App.Experience;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Hotkeys;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;
using DictateAnywhere.Hotkeys;

namespace DictateAnywhere.App.Settings;

/// <summary>
/// Cohesive settings view host bound to SettingsOperationController and SettingsDraft.
/// Presents dictation shortcuts, audio input devices, speech models, formatting, and typography.
/// </summary>
[SuppressMessage(
  "Design",
  "CA1031:Do not catch general exception types",
  Justification = "UI event boundaries report failures to IDiagnostics and presentation status.")]
public partial class SettingsPanel : UserControl, IAsyncDisposable
{
  private readonly SettingsOperationController controller;
  private readonly IHotkeyRegistrationValidator validator;
  private readonly ISettingsFileDialogService settingsFileDialogService;
  private readonly IDiagnostics diagnostics;
  private readonly SettingsSpeechSectionPresenter speechPresenter;

  private bool isUpdatingUi;
  private bool disposed;

  internal SettingsPanel(
    SettingsOperationController controller,
    IHotkeyRegistrationValidator validator,
    ISettingsFileDialogService settingsFileDialogService,
    LocalTranscriptionProviderRegistry transcriptionProviderRegistry,
    IDiagnostics diagnostics)
  {
    this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
    this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
    this.settingsFileDialogService = settingsFileDialogService ?? throw new ArgumentNullException(nameof(settingsFileDialogService));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

    AppThemeManager.ApplyThemeResources(AppThemeManager.CurrentPreference);
    InitializeComponent();

    speechPresenter = new SettingsSpeechSectionPresenter(
      this.controller,
      transcriptionProviderRegistry ?? throw new ArgumentNullException(nameof(transcriptionProviderRegistry)),
      TranscriptionProviderComboBox,
      ModelComboBox,
      TranscriptionLanguageComboBox,
      EnableAutomaticPunctuationCheckBox,
      AutomaticPunctuationLabel,
      AutomaticPunctuationPanel,
      ModelStatusTextBlock,
      ModelActionStatusTextBlock,
      ModelBenchmarkSummaryTextBlock,
      ModelProgressBar,
      ActivateModelButton,
      DownloadModelButton,
      RunBenchmarkButton,
      DeleteModelButton);

    HotkeyCaptureControl.RegistrationValidator = this.validator;
    HotkeyCaptureControl.HotkeyChanged += OnHotkeyChanged;
    UndoHotkeyCaptureControl.RegistrationValidator = this.validator;
    UndoHotkeyCaptureControl.HotkeyChanged += OnUndoHotkeyChanged;
    RetryLastDictationHotkeyCaptureControl.RegistrationValidator = this.validator;
    RetryLastDictationHotkeyCaptureControl.HotkeyChanged += OnRetryLastDictationHotkeyChanged;

    this.controller.DraftChanged += OnDraftChanged;
    this.controller.StatusChanged += OnStatusChanged;
    this.controller.SettingsSaved += OnSettingsSaved;
  }

  [SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "SettingsPanel owns and disposes the newly created SettingsOperationController in DisposeAsync.")]
  internal SettingsPanel(
    ISettingsStore settingsStore,
    IHotkeyRegistrationValidator validator,
    IModelManager modelManager,
    IBenchmarkService benchmarkService,
    IAudioInputDeviceService audioInputDeviceService,
    ISettingsFileTransferService settingsFileTransferService,
    ISettingsFileDialogService settingsFileDialogService,
    LocalTranscriptionProviderRegistry transcriptionProviderRegistry,
    IDiagnostics diagnostics)
    : this(
        new SettingsOperationController(
          settingsStore,
          settingsFileTransferService,
          modelManager,
          audioInputDeviceService,
          benchmarkService,
          diagnostics),
        validator,
        settingsFileDialogService,
        transcriptionProviderRegistry,
        diagnostics)
  {
  }

  public event EventHandler? SettingsSaved;
  public event EventHandler? BackRequested;
  public event EventHandler? ManageHistoryRequested;

  internal SettingsOperationController Controller => controller;

  internal bool FocusInitialControl() => BackButton.Focus();

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;

    controller.DraftChanged -= OnDraftChanged;
    controller.StatusChanged -= OnStatusChanged;
    controller.SettingsSaved -= OnSettingsSaved;

    HotkeyCaptureControl.HotkeyChanged -= OnHotkeyChanged;
    UndoHotkeyCaptureControl.HotkeyChanged -= OnUndoHotkeyChanged;
    RetryLastDictationHotkeyCaptureControl.HotkeyChanged -= OnRetryLastDictationHotkeyChanged;

    await controller.DisposeAsync().ConfigureAwait(false);
  }

  ValueTask IAsyncDisposable.DisposeAsync() => DisposeAsync();

  private Window? HostWindow => Window.GetWindow(this);

  private async void OnLoaded(object sender, RoutedEventArgs e)
  {
    ChatTypefaceComboBox.ItemsSource = ChatTypefaceCatalog.Options;
    speechPresenter.PopulateProviders();

    await controller.InitializeAsync().ConfigureAwait(true);
    await controller.RefreshModelsAsync(controller.CurrentDraft.ToSettings().GetConfiguredTranscriptionSelection()).ConfigureAwait(true);
    await controller.RefreshAudioDevicesAsync().ConfigureAwait(true);
  }

  private void OnDraftChanged(object? sender, SettingsDraft draft)
  {
    if (!Dispatcher.CheckAccess())
    {
      _ = Dispatcher.BeginInvoke(() => OnDraftChanged(sender, draft));
      return;
    }

    ApplyDraftToUi(draft);
  }

  private void ApplyDraftToUi(SettingsDraft draft)
  {
    SettingsSaveHint.Text = draft.IsReadOnly ? draft.ReadOnlyReason : "Changes save automatically.";
    isUpdatingUi = true;
    try
    {
      HotkeyCaptureControl.SetBinding(draft.Hotkey);
      UndoHotkeyCaptureControl.SetBinding(draft.UndoHotkey);
      RetryLastDictationHotkeyCaptureControl.SetBinding(draft.RetryLastDictationHotkey ?? AppSettings.Default.RetryLastDictationHotkey!);

      AppThemeManager.ApplyThemeResources(draft.ThemePreference);
      ApplyNativeHostTheme();

      EnableSecureFieldDetectionCheckBox.IsChecked = draft.EnableSecureFieldDetection;
      EnableElevatedInsertionCheckBox.IsChecked = draft.EnableElevatedInsertion;
      EnableDictationCommandsCheckBox.IsChecked = draft.EnableDictationCommands;
      ChatTypefaceComboBox.SelectedItem = ChatTypefaceCatalog.Resolve(draft.ChatTypefaceId);
      AssistantFeaturesEnabledCheckBox.IsChecked = draft.AssistantFeaturesEnabled;

      speechPresenter.ApplyDraft(draft, isUpdating: false);

      AudioDeviceComboBox.ItemsSource = controller.AvailableAudioDevices;
      AudioDeviceComboBox.SelectedItem = SettingsTranscriptionPresentationHelper.ResolveSelectedAudioDevice(
        controller.AvailableAudioDevices,
        draft.PreferredAudioInputDeviceId);
    }
    finally
    {
      isUpdatingUi = false;
    }
  }

  private void OnAssistantFeaturesEnabledChanged(object sender, RoutedEventArgs e)
  {
    if (!isUpdatingUi)
    {
      controller.UpdateDraft(draft => draft with
      {
        AssistantFeaturesEnabled = AssistantFeaturesEnabledCheckBox.IsChecked == true,
      });
    }
  }

  private void OnStatusChanged(object? sender, SettingsOperationStatus status)
  {
    if (!Dispatcher.CheckAccess())
    {
      _ = Dispatcher.BeginInvoke(() => OnStatusChanged(sender, status));
      return;
    }

    if (status.Phase == SettingsOperationPhase.Failed)
    {
      PersistStatusTextBlock.Text = status.Message;
      PersistStatusTextBlock.Visibility = Visibility.Visible;
      PersistStatusTextBlock.Foreground = ThemeResourceResolver.ResolveStatusBrush(this, UiStatusKind.Error);
    }
    else if (status.Kind == SettingsOperationKind.Save)
    {
      PersistStatusTextBlock.Text = status.IsBusy ? "Saving changes…" : "Changes saved.";
      PersistStatusTextBlock.Visibility = Visibility.Visible;
      PersistStatusTextBlock.Foreground = ThemeResourceResolver.ResolveStatusBrush(this, UiStatusKind.Neutral);
    }
    else
    {
      PersistStatusTextBlock.Text = string.Empty;
      PersistStatusTextBlock.Visibility = Visibility.Collapsed;
    }

    speechPresenter.ApplyStatus(status, controller.LastBenchmarkResult, this);

    if (status.Kind == SettingsOperationKind.RefreshAudioDevices && status.Phase == SettingsOperationPhase.Succeeded)
    {
      isUpdatingUi = true;
      try
      {
        AudioDeviceComboBox.ItemsSource = controller.AvailableAudioDevices;
        AudioDeviceComboBox.SelectedItem = SettingsTranscriptionPresentationHelper.ResolveSelectedAudioDevice(
          controller.AvailableAudioDevices,
          controller.CurrentDraft.PreferredAudioInputDeviceId);
      }
      finally
      {
        isUpdatingUi = false;
      }
    }
  }

  private void OnSettingsSaved(object? sender, AppSettings saved)
  {
    if (!Dispatcher.CheckAccess())
    {
      _ = Dispatcher.BeginInvoke(() => OnSettingsSaved(sender, saved));
      return;
    }

    SettingsSaved?.Invoke(this, EventArgs.Empty);
  }

  private void OnHotkeyChanged(object? sender, HotkeyBinding binding)
  {
    if (isUpdatingUi || controller.CurrentDraft.Hotkey == binding) return;
    controller.UpdateDraft(d => d with { Hotkey = binding });
  }

  private void OnUndoHotkeyChanged(object? sender, HotkeyBinding binding)
  {
    if (isUpdatingUi || controller.CurrentDraft.UndoHotkey == binding) return;
    controller.UpdateDraft(d => d with { UndoHotkey = binding });
  }

  private void OnRetryLastDictationHotkeyChanged(object? sender, HotkeyBinding binding)
  {
    if (isUpdatingUi || controller.CurrentDraft.RetryLastDictationHotkey == binding) return;
    controller.UpdateDraft(d => d with { RetryLastDictationHotkey = binding });
  }

  private void OnAudioDeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (isUpdatingUi || AudioDeviceComboBox.SelectedItem is not AudioDeviceOptionViewModel selected) return;
    if (!string.Equals(controller.CurrentDraft.PreferredAudioInputDeviceId, selected.DeviceId, StringComparison.Ordinal))
    {
      controller.UpdateDraft(d => d with { PreferredAudioInputDeviceId = selected.DeviceId });
    }
  }

  private async void OnAudioDeviceDropDownOpened(object sender, EventArgs e)
  {
    if (controller.IsBusy) return;
    await controller.RefreshAudioDevicesAsync().ConfigureAwait(true);
  }

  private void OnTranscriptionProviderSelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (isUpdatingUi) return;
    speechPresenter.HandleProviderSelectionChanged(WithUpdatingGuard);
  }

  private void OnTranscriptionModelSelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (isUpdatingUi) return;
    speechPresenter.HandleModelSelectionChanged(WithUpdatingGuard);
  }

  private void OnTranscriptionLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (isUpdatingUi) return;
    speechPresenter.HandleLanguageSelectionChanged();
  }

  private void OnEnableAutomaticPunctuationCheckedChanged(object sender, RoutedEventArgs e)
  {
    if (isUpdatingUi) return;
    bool isChecked = EnableAutomaticPunctuationCheckBox.IsChecked == true;
    if (controller.CurrentDraft.EnableAutomaticPunctuation != isChecked)
    {
      controller.UpdateDraft(d => d with { EnableAutomaticPunctuation = isChecked });
    }
  }

  private void OnEnableSecureFieldDetectionCheckedChanged(object sender, RoutedEventArgs e)
  {
    if (isUpdatingUi) return;
    bool isChecked = EnableSecureFieldDetectionCheckBox.IsChecked == true;
    if (controller.CurrentDraft.EnableSecureFieldDetection != isChecked)
    {
      controller.UpdateDraft(d => d with { EnableSecureFieldDetection = isChecked });
    }
  }

  private void OnEnableElevatedInsertionCheckedChanged(object sender, RoutedEventArgs e)
  {
    if (isUpdatingUi) return;
    bool isChecked = EnableElevatedInsertionCheckBox.IsChecked == true;
    if (controller.CurrentDraft.EnableElevatedInsertion != isChecked)
    {
      controller.UpdateDraft(d => d with { EnableElevatedInsertion = isChecked });
    }
  }

  private void OnEnableDictationCommandsCheckedChanged(object sender, RoutedEventArgs e)
  {
    if (isUpdatingUi) return;
    bool isChecked = EnableDictationCommandsCheckBox.IsChecked == true;
    if (controller.CurrentDraft.EnableDictationCommands != isChecked)
    {
      controller.UpdateDraft(d => d with { EnableDictationCommands = isChecked });
    }
  }

  private void OnChatTypefaceSelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (isUpdatingUi || ChatTypefaceComboBox.SelectedItem is not ChatTypefaceOption selected) return;
    if (!string.Equals(controller.CurrentDraft.ChatTypefaceId, selected.Id, StringComparison.OrdinalIgnoreCase))
    {
      controller.UpdateDraft(d => d with { ChatTypefaceId = selected.Id });
    }
  }

  private async void OnRefreshModelsClicked(object sender, RoutedEventArgs e) =>
    await speechPresenter.RefreshModelsAsync().ConfigureAwait(true);

  private async void OnDownloadModelClicked(object sender, RoutedEventArgs e) =>
    await speechPresenter.DownloadModelAsync().ConfigureAwait(true);

  private async void OnActivateModelClicked(object sender, RoutedEventArgs e) =>
    await speechPresenter.ActivateModelAsync().ConfigureAwait(true);

  private async void OnDeleteModelClicked(object sender, RoutedEventArgs e) =>
    await speechPresenter.DeleteModelAsync().ConfigureAwait(true);

  private async void OnRunBenchmarkClicked(object sender, RoutedEventArgs e) =>
    await speechPresenter.RunBenchmarkAsync().ConfigureAwait(true);

  private void OnManageHistoryClicked(object sender, RoutedEventArgs e) =>
    ManageHistoryRequested?.Invoke(this, EventArgs.Empty);

  private async void OnBackClicked(object sender, RoutedEventArgs e)
  {
    if (await controller.FlushSaveAsync().ConfigureAwait(true))
    {
      BackRequested?.Invoke(this, EventArgs.Empty);
    }
  }

  internal async Task<bool> ImportSettingsAsync()
  {
    Window? owner = HostWindow;
    if (owner is null) return false;
    if (!settingsFileDialogService.TryGetImportPath(owner, out string importPath)) return false;
    return await controller.ImportAsync(importPath).ConfigureAwait(true);
  }

  internal async Task<bool> ExportSettingsAsync()
  {
    Window? owner = HostWindow;
    if (owner is null) return false;
    if (!settingsFileDialogService.TryGetExportPath(owner, out string exportPath)) return false;
    return await controller.ExportAsync(exportPath).ConfigureAwait(true);
  }

  private void WithUpdatingGuard(Action action)
  {
    isUpdatingUi = true;
    try
    {
      action();
    }
    finally
    {
      isUpdatingUi = false;
    }
  }

  private void ApplyNativeHostTheme()
  {
    Window? window = HostWindow;
    if (window is not null)
    {
      AppThemeManager.ApplyNativeWindowTheme(window);
    }
  }
}

using System;
using System.Linq;
using DictateAnywhere.App.Lifecycle;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
  private CancellationTokenSource? dictationPreparationCancellation;
  private Task? dictationPreparationTask;
  private readonly DispatcherTimer runtimeStatusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
  internal bool IsCapturingHotkey => HotkeyCaptureControl.IsCapturing
    || UndoHotkeyCaptureControl.IsCapturing || RetryLastDictationHotkeyCaptureControl.IsCapturing;
  internal void SynchronizePresentationZoom(int percent)
  {
    if (!controller.CurrentDraft.IsReadOnly)
      controller.UpdateDraft(draft => draft with { WorkbenchZoomPercent = percent }, scheduleAutoSave: false);
  }
  private readonly SettingsOperationController controller;
  private readonly IHotkeyRegistrationValidator validator;
  private readonly ISettingsFileDialogService settingsFileDialogService;
  private readonly IDiagnostics diagnostics;
  private readonly SettingsSpeechSectionPresenter speechPresenter;

  private bool isUpdatingUi;
  private bool disposed;
  private Task? disposalTask;
  private readonly System.Collections.Generic.HashSet<Task> activeViewOperations = [];
  private readonly CancellationTokenSource viewCancellation = new();

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
    runtimeStatusTimer.Tick += OnRuntimeStatusTick;
    Unloaded += OnRuntimePanelUnloaded;
    PreviewKeyDown += OnPageNavigationKeyDown;

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

  private void OnPageNavigationKeyDown(object sender, KeyEventArgs e)
  {
    if (e.Handled || !SettingsPageNavigation.ShouldScroll(e.Key, Keyboard.Modifiers,
      e.OriginalSource as DependencyObject, IsCapturingHotkey)) return;
    if (e.Key == Key.Home) SettingsScrollViewer.ScrollToTop();
    else SettingsScrollViewer.ScrollToBottom();
    e.Handled = true;
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

  public ValueTask DisposeAsync()
  {
    if (disposalTask is not null) return new ValueTask(disposalTask);
    disposed = true;
    disposalTask = DisposeCoreAsync();
    return new ValueTask(disposalTask);
  }

  private async Task DisposeCoreAsync()
  {
    await System.Windows.Threading.Dispatcher.Yield();
    runtimeStatusTimer.Stop();
    runtimeStatusTimer.Tick -= OnRuntimeStatusTick;
    Unloaded -= OnRuntimePanelUnloaded;
    controller.DraftChanged -= OnDraftChanged;
    controller.StatusChanged -= OnStatusChanged;
    controller.SettingsSaved -= OnSettingsSaved;

    HotkeyCaptureControl.HotkeyChanged -= OnHotkeyChanged;
    UndoHotkeyCaptureControl.HotkeyChanged -= OnUndoHotkeyChanged;
    RetryLastDictationHotkeyCaptureControl.HotkeyChanged -= OnRetryLastDictationHotkeyChanged;

    await LifecycleCleanup.RunAsync(
      LifecycleCleanup.Sync("Cancel settings view", viewCancellation.Cancel),
      LifecycleCleanup.Sync("Cancel runtime preparation", () => dictationPreparationCancellation?.Cancel()),
      new CleanupStep("Runtime preparation", () => dictationPreparationTask ?? Task.CompletedTask),
      new CleanupStep("Accepted settings view operations", () => Task.WhenAll(activeViewOperations.ToArray())),
      new CleanupStep("Settings operations", () => controller.DisposeAsync().AsTask()),
      LifecycleCleanup.Sync("Settings view lifetime", viewCancellation.Dispose)).ConfigureAwait(true);
  }

  private async Task RunViewOperationAsync(Func<Task> action)
  {
    if (disposed) return;
    Task operation = action();
    activeViewOperations.Add(operation);
    try { await LifecycleCleanup.ObserveAsync(operation, diagnostics.Error, "Settings view operation").ConfigureAwait(true); }
    finally { activeViewOperations.Remove(operation); }
  }

  ValueTask IAsyncDisposable.DisposeAsync() => DisposeAsync();

  private Window? HostWindow => Window.GetWindow(this);

  private async void OnLoaded(object sender, RoutedEventArgs e) =>
    await RunViewOperationAsync(OnLoadedAsync).ConfigureAwait(true);

  private async Task OnLoadedAsync()
  {
    runtimeStatusTimer.Start();
    ChatTypefaceComboBox.ItemsSource = ChatTypefaceCatalog.Options;
    speechPresenter.PopulateProviders();

    await controller.InitializeAsync(viewCancellation.Token).ConfigureAwait(true);
    if (disposed) return;
    await controller.RefreshModelsAsync(controller.CurrentDraft.ToSettings().GetConfiguredTranscriptionSelection(), viewCancellation.Token).ConfigureAwait(true);
    if (disposed) return;
    await controller.RefreshAudioDevicesAsync(viewCancellation.Token).ConfigureAwait(true);
  }

  private void OnDraftChanged(object? sender, SettingsDraft draft)
  {
    if (disposed) return;
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
      RefreshRuntimeChoices(draft);

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
    if (!isUpdatingUi && !disposed)
    {
      controller.UpdateDraft(draft => draft with
      {
        AssistantFeaturesEnabled = AssistantFeaturesEnabledCheckBox.IsChecked == true,
      });
    }
  }

  private void OnStatusChanged(object? sender, SettingsOperationStatus status)
  {
    if (disposed) return;
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
    if (disposed) return;
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

  private async void OnAudioDeviceDropDownOpened(object sender, EventArgs e) =>
    await RunViewOperationAsync(OnAudioDeviceDropDownOpenedAsync).ConfigureAwait(true);

  private async Task OnAudioDeviceDropDownOpenedAsync()
  {
    if (controller.IsBusy) return;
    await controller.RefreshAudioDevicesAsync(viewCancellation.Token).ConfigureAwait(true);
  }

  private void OnTranscriptionProviderSelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (disposed || isUpdatingUi) return;
    speechPresenter.HandleProviderSelectionChanged(WithUpdatingGuard);
  }

  private void OnTranscriptionModelSelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (disposed || isUpdatingUi) return;
    speechPresenter.HandleModelSelectionChanged(WithUpdatingGuard);
  }

  private void OnTranscriptionLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (disposed || isUpdatingUi) return;
    speechPresenter.HandleLanguageSelectionChanged();
  }

  private void OnEnableAutomaticPunctuationCheckedChanged(object sender, RoutedEventArgs e)
  {
    if (disposed || isUpdatingUi) return;
    bool isChecked = EnableAutomaticPunctuationCheckBox.IsChecked == true;
    if (controller.CurrentDraft.EnableAutomaticPunctuation != isChecked)
    {
      controller.UpdateDraft(d => d with { EnableAutomaticPunctuation = isChecked });
    }
  }

  private void OnEnableSecureFieldDetectionCheckedChanged(object sender, RoutedEventArgs e)
  {
    if (disposed || isUpdatingUi) return;
    bool isChecked = EnableSecureFieldDetectionCheckBox.IsChecked == true;
    if (controller.CurrentDraft.EnableSecureFieldDetection != isChecked)
    {
      controller.UpdateDraft(d => d with { EnableSecureFieldDetection = isChecked });
    }
  }

  private void OnEnableElevatedInsertionCheckedChanged(object sender, RoutedEventArgs e)
  {
    if (disposed || isUpdatingUi) return;
    bool isChecked = EnableElevatedInsertionCheckBox.IsChecked == true;
    if (controller.CurrentDraft.EnableElevatedInsertion != isChecked)
    {
      controller.UpdateDraft(d => d with { EnableElevatedInsertion = isChecked });
    }
  }

  private void OnEnableDictationCommandsCheckedChanged(object sender, RoutedEventArgs e)
  {
    if (disposed || isUpdatingUi) return;
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
    await RunViewOperationAsync(() => speechPresenter.RefreshModelsAsync()).ConfigureAwait(true);

  private async void OnDownloadModelClicked(object sender, RoutedEventArgs e) =>
    await RunViewOperationAsync(OnDownloadModelClickedAsync).ConfigureAwait(true);

  private async Task OnDownloadModelClickedAsync()
  {
    if (disposed || dictationPreparationCancellation is not null) return;
    using CancellationTokenSource cancellation = new();
    dictationPreparationCancellation = cancellation;
    CancelDictationPreparationButton.Visibility = Visibility.Visible;
    try
    {
      Task<bool> preparation = speechPresenter.DownloadModelAsync(cancellation.Token);
      dictationPreparationTask = preparation;
      if (await preparation.ConfigureAwait(true) && !disposed) SettingsSaved?.Invoke(this, EventArgs.Empty);
    }
    finally { dictationPreparationCancellation = null; CancelDictationPreparationButton.Visibility = Visibility.Collapsed; }
  }

  private void RefreshRuntimeChoices(SettingsDraft draft)
  {
    System.Collections.Generic.IReadOnlyList<DictationRuntimeChoice> choices = CohereRuntimePresentation.Choices(draft.ToSettings().GetConfiguredTranscriptionSelection());
    DictationRuntimeComboBox.ItemsSource = choices;
    DictationRuntimeComboBox.SelectedItem = System.Linq.Enumerable.FirstOrDefault(choices,
      choice => choice.Preference == draft.DictationRuntimePreference && choice.Device == draft.DictationRuntimeDevice) ?? choices[0];
    bool supported = draft.TranscriptionProviderId == TranscriptionProviderIds.CohereLocal;
    DictationRuntimeComboBox.IsEnabled = supported && !controller.IsBusy && !draft.IsReadOnly;
    PrepareDictationRuntimeButton.IsEnabled = DictationRuntimeComboBox.IsEnabled;
    DictationRuntimeStatusTextBlock.Text = CohereRuntimePresentation.Status(draft.DictationRuntimePreference);
  }

  private void OnRuntimeStatusTick(object? sender, EventArgs e)
  {
    DictationRuntimeStatusTextBlock.Text = CohereRuntimePresentation.Status(controller.CurrentDraft.DictationRuntimePreference);
    bool enabled = !controller.IsBusy && !controller.CurrentDraft.IsReadOnly;
    DictationRuntimeComboBox.IsEnabled = enabled && controller.CurrentDraft.TranscriptionProviderId == TranscriptionProviderIds.CohereLocal;
    PrepareDictationRuntimeButton.IsEnabled = DictationRuntimeComboBox.IsEnabled;
  }

  private void OnRuntimePanelUnloaded(object sender, RoutedEventArgs e) => runtimeStatusTimer.Stop();

  private void OnDictationRuntimeChanged(object sender, SelectionChangedEventArgs e)
  {
    if (isUpdatingUi || DictationRuntimeComboBox.SelectedItem is not DictationRuntimeChoice choice) return;
    controller.UpdateDraft(draft => draft with { DictationRuntimePreference = choice.Preference, DictationRuntimeDevice = choice.Device });
  }

  private async void OnPrepareDictationRuntimeClicked(object sender, RoutedEventArgs e) =>
    await RunViewOperationAsync(OnPrepareDictationRuntimeClickedAsync).ConfigureAwait(true);

  private async Task OnPrepareDictationRuntimeClickedAsync()
  {
    if (disposed || dictationPreparationCancellation is not null || controller.IsBusy) return;
    using CancellationTokenSource cancellation = new();
    dictationPreparationCancellation = cancellation;
    CancelDictationPreparationButton.Visibility = Visibility.Visible;
    try
    {
      Task<bool> preparation = controller.PrepareRuntimeAsync(controller.CurrentDraft.ToSettings().GetConfiguredTranscriptionSelection(), cancellation.Token);
      dictationPreparationTask = preparation;
      if (await preparation.ConfigureAwait(true) && !disposed)
      {
        isUpdatingUi = true;
        try { RefreshRuntimeChoices(controller.CurrentDraft); }
        finally { isUpdatingUi = false; }
        SettingsSaved?.Invoke(this, EventArgs.Empty);
      }
    }
    finally { dictationPreparationCancellation = null; CancelDictationPreparationButton.Visibility = Visibility.Collapsed; }
  }

  private void OnCancelDictationPreparationClicked(object sender, RoutedEventArgs e) => dictationPreparationCancellation?.Cancel();

  private async void OnActivateModelClicked(object sender, RoutedEventArgs e) =>
    await RunViewOperationAsync(() => speechPresenter.ActivateModelAsync()).ConfigureAwait(true);

  private async void OnDeleteModelClicked(object sender, RoutedEventArgs e) =>
    await RunViewOperationAsync(() => speechPresenter.DeleteModelAsync()).ConfigureAwait(true);

  private async void OnRunBenchmarkClicked(object sender, RoutedEventArgs e) =>
    await RunViewOperationAsync(() => speechPresenter.RunBenchmarkAsync()).ConfigureAwait(true);

  private void OnManageHistoryClicked(object sender, RoutedEventArgs e) =>
    ManageHistoryRequested?.Invoke(this, EventArgs.Empty);

  private async void OnBackClicked(object sender, RoutedEventArgs e) =>
    await RunViewOperationAsync(OnBackClickedAsync).ConfigureAwait(true);

  private async Task OnBackClickedAsync()
  {
    if (await controller.FlushSaveAsync().ConfigureAwait(true) && !disposed)
    {
      BackRequested?.Invoke(this, EventArgs.Empty);
    }
  }

  internal async Task<bool> ImportSettingsAsync()
  {
    if (disposed) return false;
    Window? owner = HostWindow;
    if (owner is null) return false;
    if (!settingsFileDialogService.TryGetImportPath(owner, out string importPath)) return false;
    Task<bool> work = controller.ImportAsync(importPath, viewCancellation.Token);
    activeViewOperations.Add(work);
    try { return await work.ConfigureAwait(true); }
    finally { activeViewOperations.Remove(work); }
  }

  internal async Task<bool> ExportSettingsAsync()
  {
    if (disposed) return false;
    Window? owner = HostWindow;
    if (owner is null) return false;
    if (!settingsFileDialogService.TryGetExportPath(owner, out string exportPath)) return false;
    Task<bool> work = controller.ExportAsync(exportPath, viewCancellation.Token);
    activeViewOperations.Add(work);
    try { return await work.ConfigureAwait(true); }
    finally { activeViewOperations.Remove(work); }
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

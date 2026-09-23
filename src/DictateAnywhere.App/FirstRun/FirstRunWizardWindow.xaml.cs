using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using DictateAnywhere.App.Benchmarking;
using DictateAnywhere.App.Hotkeys;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Hotkeys;

namespace DictateAnywhere.App.FirstRun;

public partial class FirstRunWizardWindow : Window
{
  private readonly ISettingsStore settingsStore;
  private readonly IHotkeyRegistrationValidator validator;
  private readonly IModelManager modelManager;
  private readonly IBenchmarkService benchmarkService;
  private readonly IDiagnostics diagnostics;

  private AppSettings currentSettings = AppSettings.Default;
  private bool isBusy;
  private readonly CancellationTokenSource lifetime = new();
  private readonly CancellationToken lifetimeToken;
  private bool closed;

  public FirstRunWizardWindow(
    ISettingsStore settingsStore,
    IHotkeyRegistrationValidator validator,
    IModelManager modelManager,
    IBenchmarkService benchmarkService,
    IDiagnostics diagnostics)
  {
    lifetimeToken = lifetime.Token;
    this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
    this.modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
    this.benchmarkService = benchmarkService ?? throw new ArgumentNullException(nameof(benchmarkService));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

    InitializeComponent();
    HotkeyCaptureControl.RegistrationValidator = this.validator;
    HotkeyCaptureControl.HotkeyChanged += OnHotkeyChanged;
  }

  protected override void OnClosed(EventArgs e)
  {
    closed = true;
    lifetime.Cancel();
    lifetime.Dispose();
    HotkeyCaptureControl.HotkeyChanged -= OnHotkeyChanged;
    base.OnClosed(e);
  }

  public AppSettings? CompletedSettings { get; private set; }

  private async void OnLoaded(object sender, RoutedEventArgs e)
  {
    try
    {
      currentSettings = await settingsStore.LoadAsync(lifetimeToken).ConfigureAwait(true);
      lifetimeToken.ThrowIfCancellationRequested();
      FullAssistantRadioButton.IsChecked = currentSettings.AssistantFeaturesEnabled;
      DictationOnlyRadioButton.IsChecked = !currentSettings.AssistantFeaturesEnabled;
      HotkeyCaptureControl.SetBinding(currentSettings.Hotkey);
      await RefreshModelsAsync(currentSettings.GetConfiguredTranscriptionSelection()).ConfigureAwait(true);
      await RunBenchmarkAsync(isAutomatic: true).ConfigureAwait(true);
    }
    catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested) { }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException || IsModelManagementException(ex))
    {
      ReportFailure("First-run initialization", ex);
      SetModelActionStatus("Could not initialize setup. See Diagnostics.", isError: true);
    }
  }

  private void OnHotkeyChanged(object? sender, HotkeyBinding binding)
  {
    currentSettings = currentSettings with
    {
      Hotkey = binding,
    };
  }

  private async void OnRunBenchmarkClicked(object sender, RoutedEventArgs e)
  {
    await RunBenchmarkAsync(isAutomatic: false).ConfigureAwait(true);
  }

  private async void OnRefreshModelsClicked(object sender, RoutedEventArgs e)
  {
    if (isBusy || closed) return;
    try
    {
      await RefreshModelsAsync(GetSelectedModelSelection()).ConfigureAwait(true);
    }
    catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested) { }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException || IsModelManagementException(ex))
    {
      ReportFailure("First-run model refresh", ex);
      SetModelActionStatus("Could not refresh models. See Diagnostics.", isError: true);
    }
  }

  private async void OnFinishClicked(object sender, RoutedEventArgs e)
  {
    if (isBusy || closed)
    {
      return;
    }

    TranscriptionModelSelection? selectedModel = GetSelectedModelSelection();
    if (selectedModel is null)
    {
      SetModelActionStatus("Select a model before finishing setup.", isError: true);
      return;
    }

    if (!CrisperWhisperLicenseConfirmation.EnsureAccepted(this, selectedModel, currentSettings))
    {
      SetModelActionStatus("CrisperWhisper remains disabled because its research license was not accepted.", isError: true);
      return;
    }

    if (CrisperWhisperLicensePolicy.IsCrisperWhisper(selectedModel.ProviderId)
        && !CrisperWhisperLicensePolicy.HasCurrentAcceptance(currentSettings))
    {
      currentSettings = CrisperWhisperLicensePolicy.AcceptCurrentVersion(currentSettings);
    }

    isBusy = true;
    SetBusyState(true);
    DownloadProgressBar.Value = 0;

    try
    {
      IReadOnlyList<ModelInfo> models = await modelManager.GetModelsAsync(lifetimeToken).ConfigureAwait(true);
      lifetimeToken.ThrowIfCancellationRequested();
      ModelInfo? selectedModelInfo = null;
      foreach (ModelInfo model in models)
      {
        if (string.Equals(model.ProviderId, selectedModel.ProviderId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(model.ModelId, selectedModel.ModelId, StringComparison.OrdinalIgnoreCase))
        {
          selectedModelInfo = model;
          break;
        }
      }

      if (selectedModelInfo is null || !selectedModelInfo.IsInstalled)
      {
        Progress<double> progress = new(value => { if (!closed) DownloadProgressBar.Value = value * 100d; });
        SetModelActionStatus($"Downloading {FormatModelIdentity(selectedModel)} ...", isError: false);
        await modelManager.DownloadModelAsync(selectedModel, progress, lifetimeToken).ConfigureAwait(true);
      }

      lifetimeToken.ThrowIfCancellationRequested();
      await modelManager.SetActiveModelAsync(selectedModel, lifetimeToken).ConfigureAwait(true);
      lifetimeToken.ThrowIfCancellationRequested();
      currentSettings = (currentSettings
        .WithConfiguredTranscription(selectedModel.ProviderId, selectedModel.ModelId)) with
      {
        HasCompletedFirstRun = true,
        AssistantFeaturesEnabled = FullAssistantRadioButton.IsChecked == true,
      };

      await settingsStore.SaveAsync(currentSettings, lifetimeToken).ConfigureAwait(true);
      lifetimeToken.ThrowIfCancellationRequested();
      CompletedSettings = currentSettings;
      DialogResult = true;
      Close();
    }
    catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested) { }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException || IsModelManagementException(ex))
    {
      ReportFailure("First-run completion", ex);
      SetModelActionStatus("Could not complete setup. See Diagnostics.", isError: true);
    }
    finally
    {
      isBusy = false;
      SetBusyState(false);
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

  private void OnCancelClicked(object sender, RoutedEventArgs e)
  {
    DialogResult = false;
    Close();
  }

  private async Task RunBenchmarkAsync(bool isAutomatic)
  {
    if (isBusy)
    {
      return;
    }

    isBusy = true;
    SetBusyState(true);

    try
    {
      BenchmarkStatusTextBlock.Text = "Benchmarking...";
      string benchmarkLanguage = TranscriptionLanguageSettings.NormalizeGlobal(currentSettings.TranscriptionLanguage);
      BenchmarkResult result = await benchmarkService.RunAsync(benchmarkLanguage, lifetimeToken).ConfigureAwait(true);
      lifetimeToken.ThrowIfCancellationRequested();

      BenchmarkStatusTextBlock.Text = $"Completed at {result.ExecutedAtUtc.LocalDateTime:HH:mm:ss}.";
      BenchmarkRecommendationTextBlock.Text =
        BenchmarkResultSummaryFormatter.Format("Recommended", result);

      await RefreshModelsAsync(new TranscriptionModelSelection(result.RecommendedProviderId, result.RecommendedModelId)).ConfigureAwait(true);

      if (!isAutomatic)
      {
        SetModelActionStatus("Benchmark updated recommendation.", isError: false);
      }
    }
    catch (OperationCanceledException)
    {
      SetModelActionStatus("Benchmark canceled.", isError: true);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException || IsModelManagementException(ex))
    {
      ReportFailure("First-run benchmark", ex);
      SetModelActionStatus("Benchmark failed. See Diagnostics.", isError: true);
    }
    finally
    {
      isBusy = false;
      SetBusyState(false);
    }
  }

  private async Task RefreshModelsAsync(TranscriptionModelSelection? preferredSelection)
  {
    IReadOnlyList<ModelInfo> models = await modelManager.GetModelsAsync(lifetimeToken).ConfigureAwait(true);
    lifetimeToken.ThrowIfCancellationRequested();
    List<ModelOptionViewModel> items = new(models.Count);
    foreach (ModelInfo model in models)
    {
      items.Add(ModelOptionViewModel.FromModelInfo(model));
    }

    ModelComboBox.ItemsSource = items;

    ModelOptionViewModel? target = null;
    TranscriptionModelSelection? normalizedSelection = preferredSelection?.Normalize();
    foreach (ModelOptionViewModel item in items)
    {
      if (normalizedSelection is not null
          && string.Equals(item.ProviderId, normalizedSelection.ProviderId, StringComparison.OrdinalIgnoreCase)
          && string.Equals(item.ModelId, normalizedSelection.ModelId, StringComparison.OrdinalIgnoreCase))
      {
        target = item;
        break;
      }

      if (target is null && item.IsActive)
      {
        target = item;
      }
    }

    ModelComboBox.SelectedItem = target ?? items.FirstOrDefault();
  }

  private TranscriptionModelSelection? GetSelectedModelSelection()
  {
    return (ModelComboBox.SelectedItem as ModelOptionViewModel)?.ToSelection();
  }

  private void SetBusyState(bool busy)
  {
    if (closed) return;
    RunBenchmarkButton.IsEnabled = !busy;
    FinishButton.IsEnabled = !busy;
    ModelComboBox.IsEnabled = !busy;
  }

  private void SetModelActionStatus(string message, bool isError)
  {
    if (closed) return;
    ModelActionStatusTextBlock.Text = message;
    UiStatusKind statusKind = isError ? UiStatusKind.Error : UiStatusKind.Success;
    ModelActionStatusTextBlock.Foreground = ThemeResourceResolver.ResolveStatusBrush(this, statusKind);
  }

  private void ReportFailure(string operation, Exception exception) =>
    diagnostics.Warning($"{operation} failed: {exception.Message}");

  private static string FormatModelIdentity(TranscriptionModelSelection selection)
  {
    return $"{selection.ProviderId}/{selection.ModelId}";
  }

  private static bool IsModelManagementException(Exception exception)
  {
    return string.Equals(exception.GetType().Name, "ModelManagementException", StringComparison.Ordinal);
  }

}

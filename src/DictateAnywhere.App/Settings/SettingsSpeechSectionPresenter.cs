using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DictateAnywhere.App.Benchmarking;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;

namespace DictateAnywhere.App.Settings;

/// <summary>
/// Presenter governing speech transcription provider, model, language, and benchmark UI controls.
/// </summary>
internal sealed class SettingsSpeechSectionPresenter
{
  private readonly SettingsOperationController controller;
  private readonly LocalTranscriptionProviderRegistry transcriptionProviderRegistry;
  private readonly ComboBox providerComboBox;
  private readonly ComboBox modelComboBox;
  private readonly ComboBox languageComboBox;
  private readonly CheckBox automaticPunctuationCheckBox;
  private readonly TextBlock automaticPunctuationLabel;
  private readonly FrameworkElement automaticPunctuationPanel;
  private readonly TextBlock modelStatusTextBlock;
  private readonly TextBlock modelActionStatusTextBlock;
  private readonly TextBlock modelBenchmarkSummaryTextBlock;
  private readonly ProgressBar modelProgressBar;
  private readonly Button activateModelButton;
  private readonly Button downloadModelButton;
  private readonly Button runBenchmarkButton;
  private readonly Button deleteModelButton;

  public SettingsSpeechSectionPresenter(
    SettingsOperationController controller,
    LocalTranscriptionProviderRegistry transcriptionProviderRegistry,
    ComboBox providerComboBox,
    ComboBox modelComboBox,
    ComboBox languageComboBox,
    CheckBox automaticPunctuationCheckBox,
    TextBlock automaticPunctuationLabel,
    FrameworkElement automaticPunctuationPanel,
    TextBlock modelStatusTextBlock,
    TextBlock modelActionStatusTextBlock,
    TextBlock modelBenchmarkSummaryTextBlock,
    ProgressBar modelProgressBar,
    Button activateModelButton,
    Button downloadModelButton,
    Button runBenchmarkButton,
    Button deleteModelButton)
  {
    this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
    this.transcriptionProviderRegistry = transcriptionProviderRegistry ?? throw new ArgumentNullException(nameof(transcriptionProviderRegistry));
    this.providerComboBox = providerComboBox ?? throw new ArgumentNullException(nameof(providerComboBox));
    this.modelComboBox = modelComboBox ?? throw new ArgumentNullException(nameof(modelComboBox));
    this.languageComboBox = languageComboBox ?? throw new ArgumentNullException(nameof(languageComboBox));
    this.automaticPunctuationCheckBox = automaticPunctuationCheckBox ?? throw new ArgumentNullException(nameof(automaticPunctuationCheckBox));
    this.automaticPunctuationLabel = automaticPunctuationLabel ?? throw new ArgumentNullException(nameof(automaticPunctuationLabel));
    this.automaticPunctuationPanel = automaticPunctuationPanel ?? throw new ArgumentNullException(nameof(automaticPunctuationPanel));
    this.modelStatusTextBlock = modelStatusTextBlock ?? throw new ArgumentNullException(nameof(modelStatusTextBlock));
    this.modelActionStatusTextBlock = modelActionStatusTextBlock ?? throw new ArgumentNullException(nameof(modelActionStatusTextBlock));
    this.modelBenchmarkSummaryTextBlock = modelBenchmarkSummaryTextBlock ?? throw new ArgumentNullException(nameof(modelBenchmarkSummaryTextBlock));
    this.modelProgressBar = modelProgressBar ?? throw new ArgumentNullException(nameof(modelProgressBar));
    this.activateModelButton = activateModelButton ?? throw new ArgumentNullException(nameof(activateModelButton));
    this.downloadModelButton = downloadModelButton ?? throw new ArgumentNullException(nameof(downloadModelButton));
    this.runBenchmarkButton = runBenchmarkButton ?? throw new ArgumentNullException(nameof(runBenchmarkButton));
    this.deleteModelButton = deleteModelButton ?? throw new ArgumentNullException(nameof(deleteModelButton));
  }

  public void PopulateProviders()
  {
    providerComboBox.ItemsSource = transcriptionProviderRegistry
      .GetDefinitions()
      .Select(definition => new TranscriptionProviderOptionViewModel(
        definition.ProviderId,
        definition.DisplayName,
        definition.Description,
        definition.Capabilities.SupportsBenchmarking,
        definition.Capabilities.SupportsPunctuationControl,
        definition.UsageNotice))
      .ToList();
  }

  public void ApplyDraft(SettingsDraft draft, bool isUpdating)
  {
    if (isUpdating)
    {
      return;
    }

    SelectTranscriptionProvider(draft.TranscriptionProviderId);

    IReadOnlyList<ModelOptionViewModel> providerModels =
      SettingsTranscriptionPresentationHelper.GetModelsForProvider(controller.AvailableModels, draft.TranscriptionProviderId);
    modelComboBox.ItemsSource = providerModels;

    ModelOptionViewModel? selectedModel =
      SettingsTranscriptionPresentationHelper.ResolveSelectedModel(providerModels, draft.ToSettings().GetConfiguredTranscriptionSelection());
    modelComboBox.SelectedItem = selectedModel;

    (IReadOnlyList<LanguageOptionViewModel> languages, LanguageOptionViewModel? selectedLanguage) =
      SettingsTranscriptionPresentationHelper.ResolveLanguages(selectedModel, draft.TranscriptionLanguage);
    languageComboBox.ItemsSource = languages;
    languageComboBox.SelectedItem = selectedLanguage;

    RefreshModelCapabilitySummary();
    RefreshAutomaticPunctuationControl(draft);
    SetModelControlsEnabled(!controller.IsBusy);
  }

  public void ApplyStatus(SettingsOperationStatus status, BenchmarkResult? lastBenchmarkResult, FrameworkElement themeContext)
  {
    bool showProgress = status.IsBusy && status.Kind is SettingsOperationKind.DownloadModel or SettingsOperationKind.ActivateModel or SettingsOperationKind.RefreshModels;
    modelProgressBar.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
    modelProgressBar.IsIndeterminate = showProgress && status.Kind != SettingsOperationKind.DownloadModel;
    switch (status.Kind)
    {
      case SettingsOperationKind.DownloadModel:
      case SettingsOperationKind.ActivateModel:
      case SettingsOperationKind.DeleteModel:
      case SettingsOperationKind.RefreshModels:
        modelActionStatusTextBlock.Text = status.Message;
        UiStatusKind kind = status.Phase switch
        {
          SettingsOperationPhase.Running => UiStatusKind.Pending,
          SettingsOperationPhase.Succeeded => UiStatusKind.Success,
          SettingsOperationPhase.Failed => UiStatusKind.Error,
          _ => UiStatusKind.Neutral,
        };
        modelActionStatusTextBlock.Foreground = ThemeResourceResolver.ResolveStatusBrush(themeContext, kind);
        break;

      case SettingsOperationKind.RunBenchmark:
        if (status.Phase == SettingsOperationPhase.Running)
        {
          modelActionStatusTextBlock.Text = "Benchmarking...";
          modelActionStatusTextBlock.Foreground = ThemeResourceResolver.ResolveStatusBrush(themeContext, UiStatusKind.Pending);
        }
        else if (status.Phase == SettingsOperationPhase.Succeeded && lastBenchmarkResult is not null)
        {
          modelBenchmarkSummaryTextBlock.Text = BenchmarkResultSummaryFormatter.Format("Current benchmark", lastBenchmarkResult);
          modelActionStatusTextBlock.Text = status.Message;
          modelActionStatusTextBlock.Foreground = ThemeResourceResolver.ResolveStatusBrush(themeContext, UiStatusKind.Success);
        }
        else if (status.Phase == SettingsOperationPhase.Failed)
        {
          modelActionStatusTextBlock.Text = status.Message;
          modelActionStatusTextBlock.Foreground = ThemeResourceResolver.ResolveStatusBrush(themeContext, UiStatusKind.Error);
        }
        break;
    }

    if (controller.AvailableModels.Count > 0)
    {
      modelStatusTextBlock.Text = $"{controller.AvailableModels.Count(m => m.IsInstalled)} downloaded / {controller.AvailableModels.Count} speech models";
    }

    if (status.Kind == SettingsOperationKind.RefreshModels && status.Phase == SettingsOperationPhase.Succeeded)
    {
      ApplyDraft(controller.CurrentDraft, false);
    }

    SetModelControlsEnabled(!status.IsBusy);
  }

  public void HandleProviderSelectionChanged(Action<Action> withUpdatingGuard)
  {
    if (providerComboBox.SelectedItem is not TranscriptionProviderOptionViewModel selectedProvider)
    {
      return;
    }

    IReadOnlyList<ModelOptionViewModel> providerModels =
      SettingsTranscriptionPresentationHelper.GetModelsForProvider(controller.AvailableModels, selectedProvider.ProviderId);
    ModelOptionViewModel? selectedModel = SettingsTranscriptionPresentationHelper.ResolveSelectedModel(
      providerModels,
      new TranscriptionModelSelection(selectedProvider.ProviderId, controller.CurrentDraft.TranscriptionModelId));

    withUpdatingGuard(() =>
    {
      modelComboBox.ItemsSource = providerModels;
      modelComboBox.SelectedItem = selectedModel;
      (IReadOnlyList<LanguageOptionViewModel> languages, LanguageOptionViewModel? selectedLanguage) =
        SettingsTranscriptionPresentationHelper.ResolveLanguages(selectedModel, controller.CurrentDraft.TranscriptionLanguage);
      languageComboBox.ItemsSource = languages;
      languageComboBox.SelectedItem = selectedLanguage;
    });

    controller.UpdateDraft(d =>
    {
      SettingsDraft updated = d with { TranscriptionProviderId = selectedProvider.ProviderId };
      if (selectedModel is not null)
      {
        updated = updated with { TranscriptionModelId = selectedModel.ModelId };
      }
      return updated;
    });

    RefreshModelCapabilitySummary();
    RefreshAutomaticPunctuationControl(controller.CurrentDraft);
  }

  public void HandleModelSelectionChanged(Action<Action> withUpdatingGuard)
  {
    if (modelComboBox.SelectedItem is not ModelOptionViewModel selectedModel)
    {
      return;
    }

    withUpdatingGuard(() =>
    {
      (IReadOnlyList<LanguageOptionViewModel> languages, LanguageOptionViewModel? selectedLanguage) =
        SettingsTranscriptionPresentationHelper.ResolveLanguages(selectedModel, controller.CurrentDraft.TranscriptionLanguage);
      languageComboBox.ItemsSource = languages;
      languageComboBox.SelectedItem = selectedLanguage;
    });

    controller.UpdateDraft(d => d with
    {
      TranscriptionProviderId = selectedModel.ProviderId,
      TranscriptionModelId = selectedModel.ModelId,
    });

    RefreshModelCapabilitySummary();
  }

  public void HandleLanguageSelectionChanged()
  {
    if (languageComboBox.SelectedItem is not LanguageOptionViewModel selectedLanguage)
    {
      return;
    }

    if (!string.Equals(controller.CurrentDraft.TranscriptionLanguage, selectedLanguage.LanguageCode, StringComparison.OrdinalIgnoreCase))
    {
      controller.UpdateDraft(d => d with { TranscriptionLanguage = selectedLanguage.LanguageCode });
    }
  }

  public async Task RefreshModelsAsync()
  {
    await controller.RefreshModelsAsync(GetSelectedModelSelection()).ConfigureAwait(true);
  }

  public async Task DownloadModelAsync()
  {
    if (controller.IsBusy)
    {
      return;
    }

    TranscriptionModelSelection? selection = GetSelectedModelSelection();
    if (selection is null)
    {
      return;
    }

    if (!EnsureResearchLicenseAccepted(selection))
    {
      return;
    }

    modelProgressBar.Value = 0;
    Progress<double> progress = new(value => modelProgressBar.Value = value * 100d);
    await controller.DownloadModelAsync(selection, progress).ConfigureAwait(true);
  }

  public async Task ActivateModelAsync()
  {
    if (controller.IsBusy)
    {
      return;
    }

    TranscriptionModelSelection? selection = GetSelectedModelSelection();
    if (selection is null)
    {
      return;
    }

    if (!EnsureResearchLicenseAccepted(selection))
    {
      return;
    }

    await controller.ActivateModelAsync(selection).ConfigureAwait(true);
  }

  public async Task DeleteModelAsync()
  {
    if (controller.IsBusy)
    {
      return;
    }

    TranscriptionModelSelection? selection = GetSelectedModelSelection();
    if (selection is null)
    {
      return;
    }

    await controller.DeleteModelAsync(selection).ConfigureAwait(true);
  }

  public async Task RunBenchmarkAsync()
  {
    if (controller.IsBusy)
    {
      return;
    }

    if (providerComboBox.SelectedItem is not TranscriptionProviderOptionViewModel selectedProvider
        || !selectedProvider.SupportsBenchmarking)
    {
      return;
    }

    string benchmarkLanguage = TranscriptionLanguageSettings.NormalizeGlobal(controller.CurrentDraft.TranscriptionLanguage);
    await controller.RunBenchmarkAsync(benchmarkLanguage).ConfigureAwait(true);
  }

  public TranscriptionModelSelection? GetSelectedModelSelection()
  {
    return (modelComboBox.SelectedItem as ModelOptionViewModel)?.ToSelection();
  }

  public void SetModelControlsEnabled(bool enabled)
  {
    ModelOptionViewModel? selectedModel = modelComboBox.SelectedItem as ModelOptionViewModel;
    bool isActiveModel = selectedModel?.IsActive == true;
    activateModelButton.Content = isActiveModel ? "Active" : "Use model";
    bool installed = selectedModel?.IsInstalled == true;
    downloadModelButton.Visibility = selectedModel is not null && !installed ? Visibility.Visible : Visibility.Collapsed;
    activateModelButton.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
    downloadModelButton.Margin = new Thickness(0);
    downloadModelButton.IsEnabled = enabled && selectedModel is not null && !selectedModel.IsInstalled;
    activateModelButton.IsEnabled = enabled && selectedModel is not null && !isActiveModel;
    deleteModelButton.IsEnabled = enabled;
    providerComboBox.IsEnabled = enabled;
    modelComboBox.IsEnabled = enabled;
    languageComboBox.IsEnabled = enabled;
    automaticPunctuationCheckBox.IsEnabled =
      enabled
      && providerComboBox.SelectedItem is TranscriptionProviderOptionViewModel
      {
        SupportsPunctuationControl: true,
      };
    bool supportsBenchmarking = providerComboBox.SelectedItem is TranscriptionProviderOptionViewModel provider
      && provider.SupportsBenchmarking;
    runBenchmarkButton.IsEnabled = enabled && supportsBenchmarking;
  }

  private void SelectTranscriptionProvider(string providerId)
  {
    IEnumerable<TranscriptionProviderOptionViewModel> items = providerComboBox.ItemsSource as IEnumerable<TranscriptionProviderOptionViewModel>
      ?? Array.Empty<TranscriptionProviderOptionViewModel>();
    providerComboBox.SelectedItem = items.FirstOrDefault(item =>
      string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
      ?? items.FirstOrDefault();
  }

  private void RefreshModelCapabilitySummary()
  {
    TranscriptionProviderOptionViewModel? provider = providerComboBox.SelectedItem as TranscriptionProviderOptionViewModel;
    ModelOptionViewModel? model = modelComboBox.SelectedItem as ModelOptionViewModel;
    LanguageOptionViewModel? selectedLanguage = languageComboBox.SelectedItem as LanguageOptionViewModel;
    ModelCapabilitySummary summary = SettingsTranscriptionPresentationHelper.FormatCapabilitySummary(provider, model, selectedLanguage);
    modelActionStatusTextBlock.Text = string.Empty;
    modelStatusTextBlock.Text = summary.PrimaryText;
    modelBenchmarkSummaryTextBlock.Text = summary.SecondaryText;
    SetModelControlsEnabled(!controller.IsBusy);
  }

  private void RefreshAutomaticPunctuationControl(SettingsDraft draft)
  {
    bool isSupported = providerComboBox.SelectedItem is TranscriptionProviderOptionViewModel
    {
      SupportsPunctuationControl: true,
    };
    Visibility visibility = isSupported ? Visibility.Visible : Visibility.Collapsed;
    automaticPunctuationLabel.Visibility = visibility;
    automaticPunctuationPanel.Visibility = visibility;
    automaticPunctuationCheckBox.IsChecked = draft.EnableAutomaticPunctuation;
  }

  private bool EnsureResearchLicenseAccepted(TranscriptionModelSelection selection)
  {
    AppSettings current = controller.CurrentDraft.ToSettings();
    if (!CrisperWhisperLicenseConfirmation.EnsureAccepted(
          Window.GetWindow(downloadModelButton),
          selection,
          current))
    {
      modelActionStatusTextBlock.Text = "CrisperWhisper remains disabled because its research license was not accepted.";
      return false;
    }

    if (CrisperWhisperLicensePolicy.IsCrisperWhisper(selection.ProviderId)
        && !CrisperWhisperLicensePolicy.HasCurrentAcceptance(current))
    {
      controller.UpdateDraft(d => d with
      {
        CrisperWhisperLicenseAcceptanceVersion = CrisperWhisperLicensePolicy.AcceptanceVersion,
      });
    }

    return true;
  }
}

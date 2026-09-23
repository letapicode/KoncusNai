using System;
using System.Globalization;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Presentation;

public sealed record ModelCapabilitySummary(string PrimaryText, string SecondaryText);

public static class ModelCapabilitySummaryFormatter
{
  public static ModelCapabilitySummary Format(
    TranscriptionProviderOptionViewModel provider,
    ModelOptionViewModel? model,
    LanguageOptionViewModel? selectedLanguage)
  {
    ArgumentNullException.ThrowIfNull(provider);

    if (model is null)
    {
      return new ModelCapabilitySummary(
        "Choose a speech model.",
        "Model actions become available after the model list loads.");
    }

    string installationState = model.IsActive
      ? "Active"
      : model.IsInstalled
        ? "Downloaded"
        : "Not downloaded";
    string languageState = FormatLanguageState(model, selectedLanguage);

    string usageNotice = string.IsNullOrWhiteSpace(provider.UsageNotice)
      ? string.Empty
      : $" {provider.UsageNotice}";

    return new ModelCapabilitySummary(
      $"{model.DisplayName}: {installationState}.",
      $"{languageState}{usageNotice}");
  }

  private static string FormatLanguageState(ModelOptionViewModel model, LanguageOptionViewModel? selectedLanguage)
  {
    int languageCount = model.SupportedLanguages.Count;
    if (languageCount == 0)
    {
      return "Language metadata is unavailable.";
    }

    string selectedLanguageText = selectedLanguage is null
      ? "No language selected"
      : $"Language: {selectedLanguage.Label}";
    string countText = languageCount == 1
      ? "1 language option"
      : string.Format(CultureInfo.InvariantCulture, "{0} supported languages", languageCount);
    return $"{selectedLanguageText}; {countText}.";
  }
}

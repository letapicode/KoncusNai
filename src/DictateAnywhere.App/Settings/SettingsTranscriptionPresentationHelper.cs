using System;
using System.Collections.Generic;
using System.Linq;
using DictateAnywhere.App.Composition;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Settings;

internal static class SettingsTranscriptionPresentationHelper
{
  public static IReadOnlyList<ModelOptionViewModel> GetModelsForProvider(
    IReadOnlyList<ModelOptionViewModel> allModels,
    string providerId)
  {
    return allModels
      .Where(model => string.Equals(model.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
      .ToList();
  }

  public static ModelOptionViewModel? ResolveSelectedModel(
    IReadOnlyList<ModelOptionViewModel> items,
    TranscriptionModelSelection? preferredSelection)
  {
    TranscriptionModelSelection? normalizedSelection = preferredSelection?.Normalize();
    foreach (ModelOptionViewModel item in items)
    {
      if (normalizedSelection is not null
          && string.Equals(item.ProviderId, normalizedSelection.ProviderId, StringComparison.OrdinalIgnoreCase)
          && string.Equals(item.ModelId, normalizedSelection.ModelId, StringComparison.OrdinalIgnoreCase))
      {
        return item;
      }
    }

    foreach (ModelOptionViewModel item in items)
    {
      if (item.IsActive)
      {
        return item;
      }
    }

    return items.FirstOrDefault();
  }

  public static (IReadOnlyList<LanguageOptionViewModel> Languages, LanguageOptionViewModel? SelectedLanguage) ResolveLanguages(
    ModelOptionViewModel? selectedModel,
    string configuredLanguage)
  {
    IReadOnlyList<LanguageOptionViewModel> languages = (selectedModel?.SupportedLanguages ?? Array.Empty<string>())
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .Select(LanguageOptionViewModel.FromCode)
      .ToList();

    if (languages.Count == 0)
    {
      return (languages, null);
    }

    LanguageOptionViewModel selected = languages
      .FirstOrDefault(item => string.Equals(item.LanguageCode, configuredLanguage, StringComparison.OrdinalIgnoreCase))
      ?? languages[0];

    return (languages, selected);
  }

  public static AudioDeviceOptionViewModel? ResolveSelectedAudioDevice(
    IReadOnlyList<AudioDeviceOptionViewModel> items,
    string? preferredDeviceId)
  {
    foreach (AudioDeviceOptionViewModel item in items)
    {
      if (string.Equals(item.DeviceId, preferredDeviceId, StringComparison.Ordinal))
      {
        return item;
      }
    }

    return items.FirstOrDefault(item => item.DeviceId is null) ?? items.FirstOrDefault();
  }

  public static ModelCapabilitySummary FormatCapabilitySummary(
    TranscriptionProviderOptionViewModel? provider,
    ModelOptionViewModel? model,
    LanguageOptionViewModel? language)
  {
    if (provider is null)
    {
      return new ModelCapabilitySummary(string.Empty, string.Empty);
    }

    return ModelCapabilitySummaryFormatter.Format(provider, model, language);
  }
}

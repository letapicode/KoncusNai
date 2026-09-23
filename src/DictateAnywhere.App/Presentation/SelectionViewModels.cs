using System;
using System.Collections.Generic;
using System.Globalization;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Presentation;

public sealed record TranscriptionProviderOptionViewModel(
  string ProviderId,
  string DisplayName,
  string Description,
  bool SupportsBenchmarking,
  bool SupportsPunctuationControl = false,
  string? UsageNotice = null)
{
  public string Label => DisplayName;

  public override string ToString()
  {
    return Label;
  }
}

public sealed record ModelOptionViewModel(
  string ProviderId,
  string ModelId,
  string DisplayName,
  bool IsInstalled,
  bool IsActive,
  IReadOnlyList<string> SupportedLanguages)
{
  public static ModelOptionViewModel FromModelInfo(ModelInfo model)
  {
    ArgumentNullException.ThrowIfNull(model);
    return new ModelOptionViewModel(
      model.ProviderId,
      model.ModelId,
      model.DisplayName,
      model.IsInstalled,
      model.IsActive,
      model.SupportedLanguages);
  }

  public TranscriptionModelSelection ToSelection()
  {
    return new TranscriptionModelSelection(ProviderId, ModelId);
  }

  public string Label
  {
    get
    {
      return DisplayName;
    }
  }

  public override string ToString()
  {
    return Label;
  }
}

public sealed record LanguageOptionViewModel(string LanguageCode, string Label)
{
  public override string ToString()
  {
    return Label;
  }

  public static LanguageOptionViewModel FromCode(string languageCode)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(languageCode);

    return new LanguageOptionViewModel(languageCode, GetDisplayName(languageCode));
  }

  private static string GetDisplayName(string languageCode)
  {
    string normalized = languageCode.Trim().ToLowerInvariant();
    return normalized switch
    {
      "en" => "English (en)",
      "fr" => "French (fr)",
      "de" => "German (de)",
      "it" => "Italian (it)",
      "es" => "Spanish (es)",
      "pt" => "Portuguese (pt)",
      "el" => "Greek (el)",
      "nl" => "Dutch (nl)",
      "pl" => "Polish (pl)",
      "zh" => "Chinese (zh)",
      "ja" => "Japanese (ja)",
      "ko" => "Korean (ko)",
      "vi" => "Vietnamese (vi)",
      "ar" => "Arabic (ar)",
      "en-us" => "English (United States)",
      "en-gb" => "English (United Kingdom)",
      _ => $"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.Replace('-', ' '))} ({normalized})",
    };
  }
}

public sealed record AudioDeviceOptionViewModel(string? DeviceId, string Label)
{
  public override string ToString()
  {
    return Label;
  }
}

using System;

namespace DictateAnywhere.Core.Contracts;

public static class TranscriptionLanguageSettings
{
  public const string DefaultLanguage = "en";

  public static readonly string[] WhisperLanguageCodes =
  [
    "en", "zh", "de", "es", "ru", "ko", "fr", "ja", "pt", "tr", "pl", "ca",
    "nl", "ar", "sv", "it", "id", "hi", "fi", "vi", "he", "uk", "el", "ms",
    "cs", "ro", "da", "hu", "ta", "no", "th", "ur", "hr", "bg", "lt", "la",
    "mi", "ml", "cy", "sk", "te", "fa", "lv", "bn", "sr", "az", "sl", "kn",
    "et", "mk", "br", "eu", "is", "hy", "ne", "mn", "bs", "kk", "sq", "sw",
    "gl", "mr", "pa", "si", "km", "sn", "yo", "so", "af", "oc", "ka", "be",
    "tg", "sd", "gu", "am", "yi", "lo", "uz", "fo", "ht", "ps", "tk", "nn",
    "mt", "sa", "lb", "my", "bo", "tl", "mg", "as", "tt", "haw", "ln", "ha",
    "ba", "jw", "su", "yue",
  ];

  public static string NormalizeGlobal(string? value)
  {
    string? normalized = NormalizeTag(value);
    return string.IsNullOrWhiteSpace(normalized)
      ? DefaultLanguage
      : normalized;
  }

  public static string? NormalizeOverride(string? value)
  {
    return NormalizeTag(value);
  }

  private static string? NormalizeTag(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return null;
    }

    return value
      .Trim()
      .Replace('_', '-')
      .ToLowerInvariant();
  }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace DictateAnywhere.Inference;

public enum IndicParlerSupportTier
{
  Official,
  Experimental,
}

public sealed record IndicParlerLanguage(
  string Code,
  string EnglishName,
  string NativeName,
  IndicParlerSupportTier SupportTier,
  string? RecommendedSpeaker,
  IReadOnlyList<string> AvailableSpeakers,
  string DefaultDescription,
  IReadOnlyList<string> Aliases);

/// <summary>Upstream-documented language and speaker capabilities for ai4bharat/indic-parler-tts.</summary>
public static class IndicParlerLanguageRegistry
{
  private const string NeutralDescription =
    "A clear narrator speaks at a normal pace with balanced pitch and natural expression. " +
    "The recording is very clear, close-sounding, and contains no background noise.";

  public static IReadOnlyList<IndicParlerLanguage> Languages { get; } =
  [
    Create("as", "Assamese", "অসমীয়া", "Amit", ["Amit", "Sita", "Poonam", "Rakesh"]),
    Create("bn", "Bengali", "বাংলা", "Arjun", ["Arjun", "Aditi", "Tapan", "Rashmi", "Arnav", "Riya"]),
    Create("brx", "Bodo", "बर'", "Bikram", ["Bikram", "Maya", "Kalpana"]),
    Create("doi", "Dogri", "डोगरी", "Karan", ["Karan"]),
    Create("en", "English", "English", "Thoma", ["Thoma", "Mary", "Swapna", "Dinesh", "Meera", "Jatin", "Aakash", "Sneha", "Kabir", "Tisha", "Chingkhei", "Thoiba", "Priya", "Tarun", "Gauri", "Nisha", "Raghav", "Kavya", "Ravi", "Vikas", "Riya"]),
    Create("gu", "Gujarati", "ગુજરાતી", "Yash", ["Yash", "Neha"]),
    Create("hi", "Hindi", "हिन्दी", "Rohit", ["Rohit", "Divya", "Aman", "Rani"]),
    Create("kn", "Kannada", "ಕನ್ನಡ", "Suresh", ["Suresh", "Anu", "Chetan", "Vidya"]),
    Create("kok", "Konkani", "कोंकणी", null, []),
    Create("mai", "Maithili", "मैथिली", null, []),
    Create("ml", "Malayalam", "മലയാളം", "Anjali", ["Anjali", "Anju", "Harish"]),
    Create("mni", "Manipuri", "মৈতৈলোন্", "Laishram", ["Laishram", "Ranjit"]),
    Create("mr", "Marathi", "मराठी", "Sanjay", ["Sanjay", "Sunita", "Nikhil", "Radha", "Varun", "Isha"]),
    Create("ne", "Nepali", "नेपाली", "Amrita", ["Amrita"], aliases: ["npi"]),
    Create("or", "Odia", "ଓଡ଼ିଆ", "Manas", ["Manas", "Debjani"]),
    Create("sa", "Sanskrit", "संस्कृतम्", "Aryan", ["Aryan"], aliases: ["san"]),
    Create("sat", "Santali", "ᱥᱟᱱᱛᱟᱲᱤ", null, []),
    Create("sd", "Sindhi", "سنڌي", null, []),
    Create("ta", "Tamil", "தமிழ்", "Jaya", ["Kavitha", "Jaya"]),
    Create("te", "Telugu", "తెలుగు", "Prakash", ["Prakash", "Lalitha", "Kiran"]),
    Create("ur", "Urdu", "اردو", null, []),
    Create("hne", "Chhattisgarhi", "छत्तीसगढ़ी", "Bhanu", ["Bhanu", "Champa"], IndicParlerSupportTier.Experimental),
    Create("ks", "Kashmiri", "کٲشُر", null, [], IndicParlerSupportTier.Experimental),
    Create("pa", "Punjabi", "ਪੰਜਾਬੀ", "Divjot", ["Divjot", "Gurpreet"], IndicParlerSupportTier.Experimental, aliases: ["pan"]),
  ];

  public static IReadOnlyList<IndicParlerLanguage> OfficialLanguages { get; } =
    Languages.Where(language => language.SupportTier == IndicParlerSupportTier.Official).ToArray();

  public static bool TryResolve(string? code, out IndicParlerLanguage? language)
  {
    string normalized = NormalizeCode(code);
    language = Languages.FirstOrDefault(candidate =>
      string.Equals(candidate.Code, normalized, StringComparison.OrdinalIgnoreCase)
      || candidate.Aliases.Any(alias => string.Equals(alias, normalized, StringComparison.OrdinalIgnoreCase)));
    return language is not null;
  }

  public static IndicParlerLanguage GetRequired(string? code)
  {
    if (TryResolve(code, out IndicParlerLanguage? language) && language is not null)
    {
      return language;
    }

    throw new ArgumentException($"Indic Parler-TTS does not support language code '{code}'.", nameof(code));
  }

  public static string? ResolveSpeaker(IndicParlerLanguage language, string? requestedSpeaker)
  {
    ArgumentNullException.ThrowIfNull(language);
    if (string.IsNullOrWhiteSpace(requestedSpeaker))
    {
      return language.RecommendedSpeaker;
    }

    string? match = language.AvailableSpeakers.FirstOrDefault(speaker =>
      string.Equals(speaker, requestedSpeaker.Trim(), StringComparison.OrdinalIgnoreCase));
    return match ?? throw new ArgumentException(
      $"'{requestedSpeaker}' is not a documented {language.EnglishName} speaker.",
      nameof(requestedSpeaker));
  }

  public static string CreateDefaultDescription(string? speaker)
  {
    return string.IsNullOrWhiteSpace(speaker)
      ? NeutralDescription
      : $"{speaker.Trim()} speaks clearly at a normal pace with balanced pitch and natural expression. " +
        "The recording is very clear, close-sounding, and contains no background noise.";
  }

  private static IndicParlerLanguage Create(
    string code,
    string englishName,
    string nativeName,
    string? recommendedSpeaker,
    IReadOnlyList<string> speakers,
    IndicParlerSupportTier tier = IndicParlerSupportTier.Official,
    IReadOnlyList<string>? aliases = null)
  {
    string description = CreateDefaultDescription(recommendedSpeaker);
    return new IndicParlerLanguage(
      code,
      englishName,
      nativeName,
      tier,
      recommendedSpeaker,
      speakers,
      description,
      aliases ?? Array.Empty<string>());
  }

  private static string NormalizeCode(string? code) => (code ?? string.Empty).Trim().ToLowerInvariant().Split('-', 2)[0];
}

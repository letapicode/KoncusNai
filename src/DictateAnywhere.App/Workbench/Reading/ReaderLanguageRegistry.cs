using System;
using System.Collections.Generic;
using System.Linq;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>
/// One provider-aware registry for every language and voice combination exposed by Reading Studio.
/// Provider-specific registries remain responsible only for upstream engine metadata.
/// </summary>
internal sealed record ReaderLanguageOption(
  string Code,
  string DisplayName,
  string ProviderId,
  ReaderVoiceOption DefaultVoice,
  IReadOnlyList<ReaderVoiceOption> Voices,
  ReaderLanguageCapability Capability,
  bool IsExperimental = false)
{
  public string LanguageLabel { get; init; } = DisplayName;

  public string CapabilitySummary
  {
    get
    {
      List<string> details = [];
      if (IsExperimental)
      {
        details.Add("Experimental language support");
      }

      if (Capability.Text.Direction == ReaderTextDirection.RightToLeft)
      {
        details.Add("Right-to-left reading");
      }

      details.Add(Capability.WordTiming switch
      {
        ReaderWordTimingStrategy.NativeWithDeterministicFallback => "Native word timing with deterministic fallback",
        ReaderWordTimingStrategy.LocalForcedAlignment => "Local forced alignment",
        _ => throw new ArgumentOutOfRangeException(
          nameof(Capability.WordTiming),
          Capability.WordTiming,
          "Unknown reader word-timing strategy."),
      });
      details.Add(Capability.Ocr == ReaderOcrSupport.Unavailable
        ? "Scanned-page OCR unavailable"
        : "Scanned-page OCR supported");
      return string.Join(" · ", details);
    }
  }

  public override string ToString() => DisplayName;
}

internal sealed record ReaderVoiceOption(
  string Id,
  string DisplayName,
  string Note,
  string? Speaker = null,
  string? Description = null)
{
  public override string ToString() => DisplayName;
}

internal static class ReaderLanguageRegistry
{
  public static IReadOnlyList<ReaderLanguageOption> Languages { get; } = BuildLanguages();

  public static ReaderLanguageOption DefaultLanguage { get; } = Languages.Single(language =>
    language.Code == "en-us"
    && language.ProviderId == TextToSpeechProviderIds.KokoroLocal);

  private static IReadOnlyList<ReaderLanguageOption> BuildLanguages()
  {
    List<ReaderLanguageOption> languages = IndicParlerLanguageRegistry.Languages
      .Select(CreateIndicParler)
      .ToList();

    languages.AddRange(
    [
      CreateKokoro("en-us", "English · United States", "af_bella",
        ("af_heart", "Heart · female", "Recommended · grade A"),
        ("af_bella", "Bella · female", "Grade A−"),
        ("af_nicole", "Nicole · female", "Grade B−"),
        ("af_aoede", "Aoede · female", "Grade C+"),
        ("af_kore", "Kore · female", "Grade C+"),
        ("af_sarah", "Sarah · female", "Grade C+"),
        ("af_nova", "Nova · female", "Grade C"),
        ("af_alloy", "Alloy · female", "Grade C"),
        ("af_sky", "Sky · female", "Grade C−"),
        ("af_jessica", "Jessica · female", "Grade D"),
        ("af_river", "River · female", "Grade D"),
        ("am_fenrir", "Fenrir · male", "Grade C+"),
        ("am_michael", "Michael · male", "Grade C+"),
        ("am_puck", "Puck · male", "Grade C+"),
        ("am_onyx", "Onyx · male", "Grade C"),
        ("am_echo", "Echo · male", "Grade D"),
        ("am_eric", "Eric · male", "Grade D"),
        ("am_liam", "Liam · male", "Grade D"),
        ("am_santa", "Santa · male", "Grade D−"),
        ("am_adam", "Adam · male", "Grade F+")),
      CreateKokoro("en-gb", "English · United Kingdom", "bf_emma",
        ("bf_emma", "Emma · female", "Recommended · grade B−"),
        ("bf_isabella", "Isabella · female", "Grade C"),
        ("bf_alice", "Alice · female", "Grade D"),
        ("bf_lily", "Lily · female", "Grade D"),
        ("bm_george", "George · male", "Grade C"),
        ("bm_fable", "Fable · male", "Grade C"),
        ("bm_lewis", "Lewis · male", "Grade D+"),
        ("bm_daniel", "Daniel · male", "Grade D")),
      CreateKokoro("ja", "Japanese", "jf_alpha",
        ("jf_alpha", "Alpha · female", "Recommended · grade C+"),
        ("jf_gongitsune", "Gongitsune · female", "Grade C"),
        ("jf_nezumi", "Nezumi · female", "Grade C−"),
        ("jf_tebukuro", "Tebukuro · female", "Grade C"),
        ("jm_kumo", "Kumo · male", "Grade C−")),
      CreateKokoro("zh", "Mandarin Chinese", "zf_xiaobei",
        ("zf_xiaobei", "Xiaobei · female", "Recommended"),
        ("zf_xiaoni", "Xiaoni · female", "Alternative"),
        ("zf_xiaoxiao", "Xiaoxiao · female", "Alternative"),
        ("zf_xiaoyi", "Xiaoyi · female", "Alternative"),
        ("zm_yunjian", "Yunjian · male", "Alternative"),
        ("zm_yunxi", "Yunxi · male", "Alternative"),
        ("zm_yunxia", "Yunxia · male", "Alternative"),
        ("zm_yunyang", "Yunyang · male", "Alternative")),
      CreateKokoro("es", "Spanish", "ef_dora",
        ("ef_dora", "Dora · female", "Recommended"),
        ("em_alex", "Alex · male", "Alternative"),
        ("em_santa", "Santa · male", "Alternative")),
      CreateKokoro("fr", "French", "ff_siwis", ("ff_siwis", "Siwis · female", "Recommended · grade B−")),
      CreateKokoro("hi", "Hindi · हिन्दी", "hf_alpha",
        ("hf_alpha", "Alpha · female", "Recommended · grade C"),
        ("hf_beta", "Beta · female", "Grade C"),
        ("hm_omega", "Omega · male", "Grade C"),
        ("hm_psi", "Psi · male", "Grade C")),
      CreateKokoro("it", "Italian", "if_sara",
        ("if_sara", "Sara · female", "Recommended"),
        ("im_nicola", "Nicola · male", "Alternative")),
      CreateKokoro("pt-br", "Portuguese · Brazil", "pf_dora",
        ("pf_dora", "Dora · female", "Recommended"),
        ("pm_alex", "Alex · male", "Alternative"),
        ("pm_santa", "Santa · male", "Alternative")),
    ]);
    return languages;
  }

  private static ReaderLanguageOption CreateIndicParler(IndicParlerLanguage language)
  {
    bool experimental = language.SupportTier == IndicParlerSupportTier.Experimental;
    string displayName = string.Equals(language.EnglishName, language.NativeName, StringComparison.Ordinal)
      ? language.EnglishName
      : $"{language.EnglishName} · {language.NativeName}";
    if (experimental)
    {
      displayName += " · Experimental";
    }
    string languageLabel = displayName;
    displayName += " · Indic Parler";

    List<ReaderVoiceOption> voices = language.AvailableSpeakers.Count == 0
      ? [new ReaderVoiceOption("default", "Automatic · clear, neutral", "Generated for the selected language.", null, language.DefaultDescription)]
      : language.AvailableSpeakers.Select(speaker => new ReaderVoiceOption(
          speaker.ToLowerInvariant(),
          $"{speaker} · {GetIndicVoiceCharacter(speaker)}",
          string.Equals(speaker, language.RecommendedSpeaker, StringComparison.OrdinalIgnoreCase) ? "Recommended voice" : "Alternative voice",
          speaker,
          IndicParlerLanguageRegistry.CreateDefaultDescription(speaker)))
        .ToList();
    ReaderVoiceOption defaultVoice = language.RecommendedSpeaker is null
      ? voices[0]
      : voices.First(voice => string.Equals(voice.Speaker, language.RecommendedSpeaker, StringComparison.OrdinalIgnoreCase));
    return new ReaderLanguageOption(
      language.Code,
      displayName,
      TextToSpeechProviderIds.IndicParlerLocal,
      defaultVoice,
      voices,
      CreateCapability(language.Code, TextToSpeechProviderIds.IndicParlerLocal),
      experimental) { LanguageLabel = languageLabel };
  }

  private static ReaderLanguageOption CreateKokoro(
    string code,
    string language,
    string defaultVoiceId,
    params (string Id, string DisplayName, string Note)[] definitions)
  {
    List<ReaderVoiceOption> voices = definitions
      .Select(definition => new ReaderVoiceOption(
        definition.Id,
        $"{definition.DisplayName} · {GetKokoroVoiceCharacter(definition.Id)}",
        $"{GetKokoroVoiceCharacter(definition.Id)}. {definition.Note}",
        definition.Id))
      .ToList();
    ReaderVoiceOption defaultVoice = voices.Find(voice => voice.Id == defaultVoiceId)
      ?? throw new InvalidOperationException($"The default voice '{defaultVoiceId}' is not in the {language} catalog.");
    return new ReaderLanguageOption(
      code,
      $"{language} · Kokoro",
      TextToSpeechProviderIds.KokoroLocal,
      defaultVoice,
      voices,
      CreateCapability(code, TextToSpeechProviderIds.KokoroLocal)) { LanguageLabel = language };
  }

  private static ReaderLanguageCapability CreateCapability(string languageCode, string providerId)
  {
    ReaderWordTimingStrategy timing = providerId switch
    {
      TextToSpeechProviderIds.KokoroLocal => ReaderWordTimingStrategy.NativeWithDeterministicFallback,
      TextToSpeechProviderIds.IndicParlerLocal => ReaderWordTimingStrategy.LocalForcedAlignment,
      _ => throw new ArgumentException($"Unsupported reader provider '{providerId}'.", nameof(providerId)),
    };

    return new ReaderLanguageCapability(
      ReaderScriptProfile.Create(ResolveScript(languageCode)),
      timing,
      ResolveOcrSupport(languageCode));
  }

  private static ReaderScriptFamily ResolveScript(string languageCode)
  {
    return languageCode switch
    {
      "en" or "en-us" or "en-gb" or "es" or "fr" or "it" or "pt-br" => ReaderScriptFamily.Latin,
      "zh" => ReaderScriptFamily.Han,
      "ja" => ReaderScriptFamily.Japanese,
      "brx" or "doi" or "hi" or "kok" or "mai" or "mr" or "ne" or "sa" or "hne" => ReaderScriptFamily.Devanagari,
      "as" or "bn" or "mni" => ReaderScriptFamily.BengaliAssamese,
      "gu" => ReaderScriptFamily.Gujarati,
      "pa" => ReaderScriptFamily.Gurmukhi,
      "kn" => ReaderScriptFamily.Kannada,
      "ml" => ReaderScriptFamily.Malayalam,
      "or" => ReaderScriptFamily.Odia,
      "ta" => ReaderScriptFamily.Tamil,
      "te" => ReaderScriptFamily.Telugu,
      "sat" => ReaderScriptFamily.OlChiki,
      "ks" or "sd" or "ur" => ReaderScriptFamily.ArabicDerived,
      _ => throw new ArgumentException($"Reader language '{languageCode}' does not have a script profile.", nameof(languageCode)),
    };
  }

  private static ReaderOcrSupport ResolveOcrSupport(string languageCode)
  {
    return languageCode switch
    {
      "en" or "en-us" or "en-gb" => ReaderOcrSupport.English,
      "es" or "fr" or "it" or "pt-br" => ReaderOcrSupport.Latin,
      "hi" or "ne" => ReaderOcrSupport.Devanagari,
      "zh" => ReaderOcrSupport.Chinese,
      "ja" => ReaderOcrSupport.Japanese,
      _ => ReaderOcrSupport.Unavailable,
    };
  }

  private static string GetKokoroVoiceCharacter(string voiceId)
  {
    return voiceId switch
    {
      "af_bella" => "warm, expressive",
      "af_heart" => "bright, natural",
      "af_nicole" => "calm, intimate",
      "af_aoede" => "smooth, lyrical",
      "af_kore" => "clear, composed",
      "af_sarah" => "friendly, balanced",
      "af_nova" => "modern, energetic",
      "af_alloy" => "steady, neutral",
      "af_sky" => "light, conversational",
      "af_jessica" => "soft, approachable",
      "af_river" => "grounded, relaxed",
      "am_fenrir" => "rich, animated",
      "am_michael" => "warm, dependable",
      "am_puck" => "playful, lively",
      "am_onyx" => "deep, measured",
      "am_echo" => "crisp, direct",
      "am_eric" => "casual, natural",
      "am_liam" => "gentle, clear",
      "am_santa" => "deep, theatrical",
      "am_adam" => "firm, neutral",
      "bf_emma" => "polished, warm",
      "bf_isabella" => "soft, elegant",
      "bf_alice" => "bright, articulate",
      "bf_lily" => "gentle, youthful",
      "bm_george" => "classic, composed",
      "bm_fable" => "expressive, literary",
      "bm_lewis" => "calm, conversational",
      "bm_daniel" => "steady, formal",
      "jf_alpha" or "hf_alpha" => "clear, confident",
      "jf_gongitsune" => "warm, storybook",
      "jf_nezumi" => "light, animated",
      "jf_tebukuro" => "gentle, narrative",
      "jm_kumo" => "calm, grounded",
      "zf_xiaobei" => "bright, natural",
      "zf_xiaoni" => "soft, friendly",
      "zf_xiaoxiao" => "clear, lively",
      "zf_xiaoyi" => "gentle, measured",
      "zm_yunjian" => "confident, clear",
      "zm_yunxi" => "smooth, conversational",
      "zm_yunxia" => "warm, grounded",
      "zm_yunyang" => "deep, composed",
      "ef_dora" or "pf_dora" => "warm, vibrant",
      "em_alex" or "pm_alex" => "clear, conversational",
      "em_santa" or "pm_santa" => "deep, theatrical",
      "ff_siwis" => "soft, elegant",
      "hf_beta" => "gentle, balanced",
      "hm_omega" => "deep, steady",
      "hm_psi" => "clear, measured",
      "if_sara" => "warm, lyrical",
      "im_nicola" => "smooth, composed",
      _ => "clear, natural",
    };
  }

  private static string GetIndicVoiceCharacter(string speaker)
  {
    return speaker.ToLowerInvariant() switch
    {
      "amrita" => "warm, expressive",
      "aryan" => "clear, resonant",
      "thoma" => "calm, articulate",
      _ => "natural, expressive",
    };
  }
}

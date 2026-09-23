using System;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>Captures every selection that can change synthesized narration or word timing.</summary>
internal sealed record ReaderNarrationProfile
{
  public ReaderNarrationProfile(
    string languageCode,
    string providerId,
    string? voiceId,
    string? description,
    ReaderWordTimingStrategy wordTimingStrategy)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(languageCode);
    ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
    if (!Enum.IsDefined(wordTimingStrategy))
    {
      throw new ArgumentOutOfRangeException(
        nameof(wordTimingStrategy),
        wordTimingStrategy,
        "Unknown reader word-timing strategy.");
    }

    LanguageCode = languageCode.Trim().ToLowerInvariant();
    ProviderId = providerId.Trim().ToLowerInvariant();
    VoiceId = string.IsNullOrWhiteSpace(voiceId) ? null : voiceId.Trim();
    Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    WordTimingStrategy = wordTimingStrategy;
  }

  public string LanguageCode { get; }

  public string ProviderId { get; }

  public string? VoiceId { get; }

  public string? Description { get; }

  public ReaderWordTimingStrategy WordTimingStrategy { get; }

  public static ReaderNarrationProfile Create(
    ReaderLanguageOption language,
    ReaderVoiceOption voice)
  {
    ArgumentNullException.ThrowIfNull(language);
    ArgumentNullException.ThrowIfNull(voice);

    return new ReaderNarrationProfile(
      language.Code,
      language.ProviderId,
      voice.Speaker,
      voice.Description,
      language.Capability.WordTiming);
  }

  public TextToSpeechRequest CreateSpeechRequest(string narrationText)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(narrationText);
    return new TextToSpeechRequest(
      narrationText,
      LanguageCode,
      VoiceId,
      Description,
      ProviderId: ProviderId);
  }
}

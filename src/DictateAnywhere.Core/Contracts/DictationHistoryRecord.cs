using System;

namespace DictateAnywhere.Core.Contracts;

public sealed record DictationHistoryRecord(
  DateTimeOffset CreatedUtc,
  string ProfileId,
  string TranscriptionProviderId,
  string TranscriptionModelId,
  string RawTranscript,
  string FinalText,
  TimeSpan TranscriptionDuration,
  TimeSpan TextTransformationDuration,
  TimeSpan TotalPipelineDuration,
  string EntryId = "",
  string SessionId = "",
  string Title = "",
  string Source = "dictation")
{
  public const string DefaultProfileId = "default";

  public DictationHistoryRecord Normalize()
  {
    string normalizedEntryId = string.IsNullOrWhiteSpace(EntryId)
      ? Guid.NewGuid().ToString("N")
      : EntryId.Trim();
    string normalizedSessionId = string.IsNullOrWhiteSpace(SessionId)
      ? normalizedEntryId
      : SessionId.Trim();
    string normalizedTitle = string.IsNullOrWhiteSpace(Title)
      ? CreateTitle(FinalText, RawTranscript, CreatedUtc)
      : Title.Trim();
    string normalizedSource = string.IsNullOrWhiteSpace(Source)
      ? "dictation"
      : Source.Trim();

    return this with
    {
      EntryId = normalizedEntryId,
      SessionId = normalizedSessionId,
      Title = normalizedTitle,
      Source = normalizedSource,
    };
  }

  private static string CreateTitle(string finalText, string rawTranscript, DateTimeOffset createdUtc)
  {
    string source = string.IsNullOrWhiteSpace(finalText) ? rawTranscript : finalText;
    string normalized = string.Join(" ", source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    if (string.IsNullOrWhiteSpace(normalized))
    {
      return $"Dictation {createdUtc:yyyy-MM-dd HH:mm}";
    }

    return normalized.Length <= 48
      ? normalized
      : normalized[..48] + "...";
  }
}

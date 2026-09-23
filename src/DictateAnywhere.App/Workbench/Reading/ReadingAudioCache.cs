using System.Collections.Generic;
using System.IO;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench.Reading;

internal readonly record struct ReaderSpeechCacheKey(
  int SectionIndex,
  string NarrationText,
  string LanguageCode,
  string ProviderId,
  string? VoiceId,
  string? Description);

/// <summary>Keeps generated section WAVs addressable for the lifetime of a Reading Studio session.</summary>
internal sealed class ReadingAudioCache
{
  private readonly Dictionary<ReaderSpeechCacheKey, TextToSpeechResult> speechBySection = [];

  public bool TryGet(ReaderSpeechCacheKey key, out TextToSpeechResult? speech)
  {
    if (speechBySection.TryGetValue(key, out TextToSpeechResult? cached) && IsValidAudioFile(cached.AudioPath))
    {
      speech = cached;
      return true;
    }

    speechBySection.Remove(key);
    speech = null;
    return false;
  }

  public bool Evict(ReaderSpeechCacheKey key) => speechBySection.Remove(key);

  public int Count => speechBySection.Count;

  public TextToSpeechResult Store(ReaderSpeechCacheKey key, TextToSpeechResult speech)
  {
    speechBySection[key] = speech;
    return speech;
  }

  public void Clear() => speechBySection.Clear();

  internal static bool IsValidAudioFile(string? path)
  {
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
    {
      return false;
    }

    try
    {
      FileInfo fileInfo = new(path);
      return fileInfo.Length > 0;
    }
    catch (IOException)
    {
      return false;
    }
    catch (UnauthorizedAccessException)
    {
      return false;
    }
  }
}

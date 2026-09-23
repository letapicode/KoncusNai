using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class ReadingAudioCacheTests
{
  [Xunit.Fact]
  public void TryGet_ReturnsGeneratedSectionAudioWithoutResynthesis()
  {
    string path = Path.Combine(Path.GetTempPath(), $"notype-reader-{Guid.NewGuid():N}.wav");
    try
    {
      File.WriteAllBytes(path, new byte[64]);
      TextToSpeechResult result = new(path, TimeSpan.FromSeconds(3), 1, "kokoro-local", "Kokoro-82M");
      ReadingAudioCache cache = new();
      ReaderSpeechCacheKey key = new(4, "A short reading.", "en-us", "kokoro-local", "af_bella", null);
      cache.Store(key, result);

      bool found = cache.TryGet(key, out TextToSpeechResult? cached);

      Xunit.Assert.True(found);
      Xunit.Assert.Same(result, cached);
      Xunit.Assert.Equal(1, cache.Count);
    }
    finally
    {
      if (File.Exists(path))
      {
        File.Delete(path);
      }
    }
  }

  [Xunit.Fact]
  public void TryGet_WhenFileIsZeroBytes_EvictsEntryAndReturnsFalse()
  {
    string path = Path.Combine(Path.GetTempPath(), $"notype-reader-corrupt-{Guid.NewGuid():N}.wav");
    try
    {
      File.WriteAllBytes(path, []); // 0 bytes = corrupt
      TextToSpeechResult result = new(path, TimeSpan.FromSeconds(3), 1, "kokoro-local", "Kokoro-82M");
      ReadingAudioCache cache = new();
      ReaderSpeechCacheKey key = new(1, "Test text", "en-us", "kokoro-local", "af_bella", null);
      cache.Store(key, result);
      Xunit.Assert.Equal(1, cache.Count);

      bool found = cache.TryGet(key, out TextToSpeechResult? cached);

      Xunit.Assert.False(found);
      Xunit.Assert.Null(cached);
      Xunit.Assert.Equal(0, cache.Count);
    }
    finally
    {
      if (File.Exists(path))
      {
        File.Delete(path);
      }
    }
  }

  [Xunit.Fact]
  public void TryGet_WhenFileIsDeleted_EvictsEntryAndReturnsFalse()
  {
    string path = Path.Combine(Path.GetTempPath(), $"notype-reader-deleted-{Guid.NewGuid():N}.wav");
    TextToSpeechResult result = new(path, TimeSpan.FromSeconds(3), 1, "kokoro-local", "Kokoro-82M");
    ReadingAudioCache cache = new();
    ReaderSpeechCacheKey key = new(1, "Test text", "en-us", "kokoro-local", "af_bella", null);
    cache.Store(key, result);

    bool found = cache.TryGet(key, out TextToSpeechResult? cached);

    Xunit.Assert.False(found);
    Xunit.Assert.Null(cached);
    Xunit.Assert.Equal(0, cache.Count);
  }

  [Xunit.Theory]
  [Xunit.InlineData(0, "A", "en-us", "kokoro", "voice1", null)]
  [Xunit.InlineData(1, "A", "en-us", "kokoro", "voice1", null)]
  [Xunit.InlineData(0, "B", "en-us", "kokoro", "voice1", null)]
  [Xunit.InlineData(0, "A", "hi", "kokoro", "voice1", null)]
  [Xunit.InlineData(0, "A", "en-us", "indicparler", "voice1", null)]
  [Xunit.InlineData(0, "A", "en-us", "kokoro", "voice2", null)]
  [Xunit.InlineData(0, "A", "en-us", "kokoro", "voice1", "desc")]
  public void CacheKey_DistinguishesAllOutputAffectingInputs(
    int sectionIndex,
    string text,
    string lang,
    string provider,
    string? voice,
    string? desc)
  {
    ReaderSpeechCacheKey canonical = new(0, "A", "en-us", "kokoro", "voice1", null);
    ReaderSpeechCacheKey tested = new(sectionIndex, text, lang, provider, voice, desc);

    if (sectionIndex == 0 && text == "A" && lang == "en-us" && provider == "kokoro" && voice == "voice1" && desc == null)
    {
      Xunit.Assert.Equal(canonical, tested);
    }
    else
    {
      Xunit.Assert.NotEqual(canonical, tested);
    }
  }
}

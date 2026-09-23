using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.Core.Contracts;
using System.Security.Cryptography;
using System.Text.Json;

namespace DictateAnywhere.App.Tests;

public sealed class ReaderVoicePreviewCacheTests
{
  [Xunit.Fact]
  public async Task GetOrCreateAsync_GeneratesOnceThenReusesThePerVoiceCache()
  {
    string directory = Path.Combine(Path.GetTempPath(), $"notype-preview-test-{Guid.NewGuid():N}");
    string sourcePath = Path.Combine(Path.GetTempPath(), $"notype-preview-source-{Guid.NewGuid():N}.wav");
    try
    {
      await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);
      RecordingSpeechService speech = new(sourcePath);
      ReaderVoicePreviewCache cache = new(
        directory,
        new ReaderVoicePreviewAssetStore(Path.Combine(directory, "empty-assets")));
      ReaderLanguageOption language = ReaderLanguageRegistry.Languages.First(option => option.Code == "hi");

      string first = await cache.GetOrCreateAsync(speech, language, language.DefaultVoice, CancellationToken.None);
      string second = await cache.GetOrCreateAsync(speech, language, language.DefaultVoice, CancellationToken.None);

      Xunit.Assert.Equal(first, second);
      Xunit.Assert.True(File.Exists(first));
      Xunit.Assert.Equal(1, speech.CallCount);
      Xunit.Assert.Contains("जिज्ञासु", speech.LastRequest?.Text, StringComparison.Ordinal);
      Xunit.Assert.Equal(language.ProviderId, speech.LastRequest?.ProviderId);
    }
    finally
    {
      if (Directory.Exists(directory))
      {
        Directory.Delete(directory, recursive: true);
      }
      if (File.Exists(sourcePath))
      {
        File.Delete(sourcePath);
      }
    }
  }

  [Xunit.Fact]
  public void Catalog_ProvidesConciseNativePriorityLanguageSamples()
  {
    string english = ReaderVoicePreviewCatalog.GetSample("en");
    string hindi = ReaderVoicePreviewCatalog.GetSample("hi");
    string nepali = ReaderVoicePreviewCatalog.GetSample("ne");

    Xunit.Assert.EndsWith(".", english, StringComparison.Ordinal);
    Xunit.Assert.True(english.Length < 80);
    Xunit.Assert.Contains("जिज्ञासु", hindi, StringComparison.Ordinal);
    Xunit.Assert.Contains('।', hindi);
    Xunit.Assert.Contains("जिज्ञासु", nepali, StringComparison.Ordinal);
    Xunit.Assert.Contains('।', nepali);
  }

  [Xunit.Fact]
  public void Catalog_ProvidesLocalizedSampleForEveryNonEnglishLanguage()
  {
    string english = ReaderVoicePreviewCatalog.GetSample("en");
    IEnumerable<ReaderLanguageOption> nonEnglish = ReaderLanguageRegistry.Languages
      .Where(language => !string.Equals(language.Code, "en", StringComparison.OrdinalIgnoreCase)
        && !language.Code.StartsWith("en-", StringComparison.OrdinalIgnoreCase));

    foreach (ReaderLanguageOption language in nonEnglish)
    {
      Xunit.Assert.NotEqual(english, ReaderVoicePreviewCatalog.GetSample(language.Code));
    }
  }

  [Xunit.Fact]
  public async Task GetOrCreateAsync_PrefersVerifiedBundledPreviewWithoutStartingSpeechEngine()
  {
    string assetDirectory = Path.Combine(Path.GetTempPath(), $"notype-preview-assets-{Guid.NewGuid():N}");
    string cacheDirectory = Path.Combine(Path.GetTempPath(), $"notype-preview-cache-{Guid.NewGuid():N}");
    string sourcePath = Path.Combine(Path.GetTempPath(), $"notype-preview-unused-{Guid.NewGuid():N}.wav");
    try
    {
      Directory.CreateDirectory(assetDirectory);
      ReaderLanguageOption language = ReaderLanguageRegistry.DefaultLanguage;
      ReaderVoiceOption voice = language.DefaultVoice;
      string fileName = ReaderVoicePreviewAssetStore.CreateFileName(language, voice);
      string bundledPath = Path.Combine(assetDirectory, fileName);
      byte[] audio = [82, 73, 70, 70, 1, 2, 3, 4];
      await File.WriteAllBytesAsync(bundledPath, audio);
      ReaderVoicePreviewManifest manifest = new(
        ReaderVoicePreviewManifest.CurrentSchemaVersion,
        DateTimeOffset.UtcNow,
        [new ReaderVoicePreviewAsset(
          language.ProviderId,
          language.Code,
          voice.Id,
          fileName,
          1,
          Convert.ToHexString(SHA256.HashData(audio)))]);
      await File.WriteAllTextAsync(
        Path.Combine(assetDirectory, "manifest.json"),
        JsonSerializer.Serialize(manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

      RecordingSpeechService speech = new(sourcePath);
      ReaderVoicePreviewCache cache = new(cacheDirectory, new ReaderVoicePreviewAssetStore(assetDirectory));
      string resolved = await cache.GetOrCreateAsync(speech, language, voice, CancellationToken.None);

      Xunit.Assert.Equal(bundledPath, resolved);
      Xunit.Assert.Equal(0, speech.CallCount);
      Xunit.Assert.True(cache.HasBundledPreview(language, voice));
    }
    finally
    {
      if (Directory.Exists(assetDirectory))
      {
        Directory.Delete(assetDirectory, recursive: true);
      }
      if (Directory.Exists(cacheDirectory))
      {
        Directory.Delete(cacheDirectory, recursive: true);
      }
    }
  }

  [Xunit.Fact]
  public void Catalog_ContainsEveryKokoroAndIndicParlerPreviewEntry()
  {
    Xunit.Assert.Equal(129, ReaderLanguageRegistry.Languages.Sum(language => language.Voices.Count));
  }

  [Xunit.Fact]
  public async Task GetOrCreateAsync_WhenCachedFileIsCorruptZeroBytes_DeletesItAndRegenerates()
  {
    string directory = Path.Combine(Path.GetTempPath(), $"notype-preview-corrupt-{Guid.NewGuid():N}");
    string sourcePath = Path.Combine(Path.GetTempPath(), $"notype-preview-source-{Guid.NewGuid():N}.wav");
    try
    {
      Directory.CreateDirectory(directory);
      await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

      ReaderLanguageOption language = ReaderLanguageRegistry.Languages.First(option => option.Code == "hi");
      ReaderVoiceOption voice = language.DefaultVoice;
      string sample = ReaderVoicePreviewCatalog.GetSample(language.Code);
      string cacheFileName = ReaderVoicePreviewCache.CreateCacheFileName(language, voice, sample);
      string corruptedTarget = Path.Combine(directory, cacheFileName);

      // Pre-seed a corrupt 0-byte file in the cache directory
      await File.WriteAllBytesAsync(corruptedTarget, []);
      Xunit.Assert.True(File.Exists(corruptedTarget));
      Xunit.Assert.Equal(0, new FileInfo(corruptedTarget).Length);

      RecordingSpeechService speech = new(sourcePath);
      ReaderVoicePreviewCache cache = new(
        directory,
        new ReaderVoicePreviewAssetStore(Path.Combine(directory, "empty-assets")));

      string resolved = await cache.GetOrCreateAsync(speech, language, voice, CancellationToken.None);

      Xunit.Assert.Equal(corruptedTarget, resolved);
      Xunit.Assert.Equal(1, speech.CallCount); // Successfully recovered by re-synthesizing!
      Xunit.Assert.True(new FileInfo(resolved).Length > 0); // No longer corrupt!
    }
    finally
    {
      if (Directory.Exists(directory))
      {
        Directory.Delete(directory, recursive: true);
      }
      if (File.Exists(sourcePath))
      {
        File.Delete(sourcePath);
      }
    }
  }

  [Xunit.Fact]
  public void CreateCacheFileName_DifferentSampleTextProducesDifferentCacheKey()
  {
    ReaderLanguageOption language = ReaderLanguageRegistry.Languages.First(option => option.Code == "en");
    ReaderVoiceOption voice = language.DefaultVoice;

    string key1 = ReaderVoicePreviewCache.CreateCacheFileName(language, voice, "Sample text version 1.");
    string key2 = ReaderVoicePreviewCache.CreateCacheFileName(language, voice, "Sample text version 2.");
    string key1Duplicate = ReaderVoicePreviewCache.CreateCacheFileName(language, voice, "Sample text version 1.");

    Xunit.Assert.NotEqual(key1, key2);
    Xunit.Assert.Equal(key1, key1Duplicate);
  }

  [Xunit.Fact]
  public async Task PruneGeneratedCache_BoundsOwnedFilesAndLeavesUnrelatedAudioAlone()
  {
    string directory = Path.Combine(Path.GetTempPath(), $"notype-preview-prune-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      DateTimeOffset now = new(2026, 9, 22, 2, 0, 0, TimeSpan.Zero);
      string current = Path.Combine(directory, "provider-en-current-v1-aaaaaaaa.wav");
      await File.WriteAllBytesAsync(current, [1]);
      File.SetLastWriteTimeUtc(current, now.UtcDateTime);
      for (int index = 0; index < ReaderVoicePreviewCache.MaximumCachedPreviewFiles + 5; index++)
      {
        string path = Path.Combine(directory, $"provider-en-voice{index:D2}-v1-{index:D8}.wav");
        await File.WriteAllBytesAsync(path, [1]);
        File.SetLastWriteTimeUtc(path, now.UtcDateTime.AddMinutes(-index - 1));
      }

      string unrelated = Path.Combine(directory, "user-recording.wav");
      await File.WriteAllBytesAsync(unrelated, [1]);
      string abandonedTemporary = Path.Combine(directory, "preview.tmp");
      await File.WriteAllBytesAsync(abandonedTemporary, [1]);
      File.SetLastWriteTimeUtc(abandonedTemporary, now.UtcDateTime.AddDays(-2));

      ReaderVoicePreviewCache.PruneGeneratedCache(directory, current, now);

      Xunit.Assert.True(File.Exists(current));
      Xunit.Assert.True(File.Exists(unrelated));
      Xunit.Assert.False(File.Exists(abandonedTemporary));
      Xunit.Assert.True(Directory.EnumerateFiles(directory, "*-v1-*.wav").Count()
        <= ReaderVoicePreviewCache.MaximumCachedPreviewFiles);
    }
    finally
    {
      Directory.Delete(directory, recursive: true);
    }
  }

  private sealed class RecordingSpeechService(string sourcePath) : ITextToSpeechService
  {
    public int CallCount { get; private set; }

    public TextToSpeechRequest? LastRequest { get; private set; }

    public Task<TextToSpeechResult> SynthesizeAsync(TextToSpeechRequest request, CancellationToken cancellationToken = default)
    {
      CallCount++;
      LastRequest = request;
      return Task.FromResult(new TextToSpeechResult(sourcePath, TimeSpan.FromSeconds(1), 1, "test", "test"));
    }
  }
}

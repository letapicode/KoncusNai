using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench.Reading;

internal static class ReaderVoicePreviewCatalog
{
  private const string EnglishSample = "A curious brown fox crosses the quiet valley.";

  internal static string GetSample(string languageCode) => languageCode switch
  {
    "as" => "এটা কৌতূহলী শিয়ালে শান্ত উপত্যকাটো পাৰ হয়।",
    "bn" => "একটি কৌতূহলী শিয়াল শান্ত উপত্যকা পার হয়।",
    "brx" => "मोनसे सानस्रि खेकसिया सिरि दैमा बारग'।",
    "doi" => "इक उत्सुक लोमड़ी शान्त घाटी गी पार करदी ऐ।",
    "hi" => "एक जिज्ञासु लोमड़ी शांत घाटी से गुज़रती है।",
    "gu" => "એક જિજ્ઞાસુ શિયાળ શાંત ખીણ પાર કરે છે.",
    "kn" => "ಕುತೂಹಲದ ನರಿಯೊಂದು ಶಾಂತ ಕಣಿವೆಯನ್ನು ದಾಟುತ್ತದೆ.",
    "kok" => "एक जिज्ञासू कोल्हो शांत दरी पार करता.",
    "mai" => "एकटा जिज्ञासु लोमड़ी शान्त घाटी पार करैत अछि।",
    "ml" => "ആകാംക്ഷയുള്ള ഒരു കുറുക്കൻ ശാന്തമായ താഴ്വര കടക്കുന്നു.",
    "mni" => "কৌতূহল লৈবা লোই অমনা লোইশং অদু ফাওই।",
    "mr" => "एक जिज्ञासू कोल्हा शांत दरी ओलांडतो.",
    "ne" => "एउटा जिज्ञासु स्याल शान्त उपत्यका पार गर्छ।",
    "or" => "ଗୋଟିଏ କୌତୁହଳୀ ଶିଆଳ ଶାନ୍ତ ଉପତ୍ୟକା ପାର ହେଉଛି।",
    "sa" => "जिज्ञासुः शृगालः शान्ताम् उपत्यकां तरति।",
    "sat" => "ᱢᱤᱫ ᱠᱩᱛᱩᱦᱚᱞᱤᱭᱟᱹ ᱛᱩᱭᱩ ᱥᱩᱞᱩᱠ ᱜᱟᱰᱟ ᱯᱟᱨᱚᱢᱚᱜᱼᱟ।",
    "sd" => "هڪ تجسس وارو لومڙ خاموش وادي پار ڪري ٿو.",
    "ta" => "ஆர்வமுள்ள ஒரு நரி அமைதியான பள்ளத்தாக்கைக் கடக்கிறது.",
    "te" => "ఆసక్తిగల నక్క నిశ్శబ్ద లోయను దాటుతోంది.",
    "ur" => "ایک متجسس لومڑی پُرسکون وادی عبور کرتی ہے۔",
    "hne" => "एक जिज्ञासु कोलिहा शांत घाटी ला पार करत हे।",
    "ks" => "اکھ دلچسپ ژور چھُ خاموش وادی پار کران۔",
    "pa" => "ਇੱਕ ਉਤਸੁਕ ਲੂੰਬੜੀ ਸ਼ਾਂਤ ਘਾਟੀ ਪਾਰ ਕਰਦੀ ਹੈ।",
    "ja" => "好奇心いっぱいの狐が、静かな谷を渡ります。",
    "zh" => "一只好奇的狐狸穿过安静的山谷。",
    "es" => "Un zorro curioso cruza el valle tranquilo.",
    "fr" => "Un renard curieux traverse la vallée paisible.",
    "it" => "Una volpe curiosa attraversa la valle tranquilla.",
    "pt-br" => "Uma raposa curiosa atravessa o vale tranquilo.",
    _ => EnglishSample,
  };
}

internal sealed class ReaderVoicePreviewCache
{
  private const int CacheSchemaVersion = 1;
  internal const int MaximumCachedPreviewFiles = 64;
  internal const long MaximumCachedPreviewBytes = 256L * 1024 * 1024;
  internal static readonly TimeSpan MaximumCachedPreviewAge = TimeSpan.FromDays(30);
  private readonly string cacheDirectory;
  private readonly ReaderVoicePreviewAssetStore assetStore;

  internal ReaderVoicePreviewCache(
    string? cacheDirectory = null,
    ReaderVoicePreviewAssetStore? assetStore = null)
  {
    this.cacheDirectory = cacheDirectory ?? Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DictateAnywhere",
      "voice-previews");
    this.assetStore = assetStore ?? new ReaderVoicePreviewAssetStore();
  }

  internal bool HasBundledPreview(ReaderLanguageOption language, ReaderVoiceOption voice) =>
    assetStore.TryResolve(language, voice, out _);

  internal async Task<string> GetOrCreateAsync(
    ITextToSpeechService speechService,
    ReaderLanguageOption language,
    ReaderVoiceOption voice,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(speechService);
    if (assetStore.TryResolve(language, voice, out string bundledPath))
    {
      return bundledPath;
    }

    string sample = ReaderVoicePreviewCatalog.GetSample(language.Code);
    string fileName = CreateCacheFileName(language, voice, sample);
    string path = Path.Combine(cacheDirectory, fileName);
    if (IsValidPreviewAudio(path))
    {
      return path;
    }

    // Corruption recovery: delete truncated or invalid file on disk before regenerating
    if (File.Exists(path))
    {
      TryDeleteFile(path);
    }

    TextToSpeechResult result = await speechService.SynthesizeAsync(
      new TextToSpeechRequest(
        sample,
        language.Code,
        voice.Speaker,
        voice.Description,
        ProviderId: language.ProviderId),
      cancellationToken).ConfigureAwait(false);
    cancellationToken.ThrowIfCancellationRequested();
    if (!File.Exists(result.AudioPath) || new FileInfo(result.AudioPath).Length == 0)
    {
      throw new InvalidOperationException("The narrator preview did not produce a playable audio file.");
    }

    Directory.CreateDirectory(cacheDirectory);
    string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
    try
    {
      File.Copy(result.AudioPath, temporaryPath, overwrite: true);
      File.Move(temporaryPath, path, overwrite: true);
      PruneGeneratedCache(cacheDirectory, path, DateTimeOffset.UtcNow);
      return path;
    }
    finally
    {
      if (File.Exists(temporaryPath))
      {
        TryDeleteFile(temporaryPath);
      }
    }
  }

  internal static string CreateCacheFileName(ReaderLanguageOption language, ReaderVoiceOption voice, string sampleText)
  {
    string sampleHash = ComputeShortHash(sampleText);
    return $"{ReaderVoicePreviewAssetStore.Sanitize(language.ProviderId)}-{ReaderVoicePreviewAssetStore.Sanitize(language.Code)}-{ReaderVoicePreviewAssetStore.Sanitize(voice.Id)}-v{CacheSchemaVersion}-{sampleHash}.wav";
  }

  internal static bool IsValidPreviewAudio(string? path)
  {
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
    {
      return false;
    }

    try
    {
      FileInfo info = new(path);
      return info.Length > 0;
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

  internal static void PruneGeneratedCache(string directory, string? retainedPath, DateTimeOffset now)
  {
    if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
    {
      return;
    }

    try
    {
      string resolvedDirectory = Path.GetFullPath(directory);
      string? resolvedRetainedPath = string.IsNullOrWhiteSpace(retainedPath) ? null : Path.GetFullPath(retainedPath);
      FileInfo[] candidates = new DirectoryInfo(resolvedDirectory)
        .EnumerateFiles("*.wav", SearchOption.TopDirectoryOnly)
        .Where(file => file.Name.Contains($"-v{CacheSchemaVersion}-", StringComparison.Ordinal))
        .OrderByDescending(file => file.LastWriteTimeUtc)
        .ToArray();

      long retainedBytes = 0;
      int retainedFiles = 0;
      foreach (FileInfo candidate in candidates)
      {
        bool isCurrent = resolvedRetainedPath is not null
          && string.Equals(candidate.FullName, resolvedRetainedPath, StringComparison.OrdinalIgnoreCase);
        bool expired = now.UtcDateTime - candidate.LastWriteTimeUtc > MaximumCachedPreviewAge;
        bool exceedsBudget = retainedFiles >= MaximumCachedPreviewFiles
          || retainedBytes > MaximumCachedPreviewBytes - Math.Min(candidate.Length, MaximumCachedPreviewBytes);
        if (!isCurrent && (expired || exceedsBudget))
        {
          TryDeleteFile(candidate.FullName);
          continue;
        }

        retainedFiles++;
        retainedBytes += candidate.Length;
      }

      foreach (FileInfo temporary in new DirectoryInfo(resolvedDirectory).EnumerateFiles("*.tmp", SearchOption.TopDirectoryOnly))
      {
        if (now.UtcDateTime - temporary.LastWriteTimeUtc > TimeSpan.FromDays(1))
        {
          TryDeleteFile(temporary.FullName);
        }
      }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      // Cache maintenance must never block preview playback.
    }
  }

  private static string ComputeShortHash(string text)
  {
    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
    return Convert.ToHexString(hash)[..8].ToLowerInvariant();
  }

  private static void TryDeleteFile(string path)
  {
    try
    {
      File.Delete(path);
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
  }
}

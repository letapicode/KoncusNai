using System.Security.Cryptography;
using System.Text.Json;
using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;
using NAudio.Wave;

namespace DictateAnywhere.VoicePreviewGenerator;

internal static class Program
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
  };

  public static async Task<int> Main(string[] args)
  {
    GeneratorOptions options;
    try
    {
      options = GeneratorOptions.Parse(args);
    }
    catch (ArgumentException exception)
    {
      Console.Error.WriteLine(exception.Message);
      WriteUsage();
      return 2;
    }

    using CancellationTokenSource cancellation = new();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
      eventArgs.Cancel = true;
      cancellation.Cancel();
    };

    try
    {
      return await GenerateAsync(options, cancellation.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      Console.Error.WriteLine("Preview generation cancelled. Completed files were kept for resuming.");
      return 130;
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
    {
      Console.Error.WriteLine($"Preview generation failed: {exception.Message}");
      return 1;
    }
  }

  private static async Task<int> GenerateAsync(GeneratorOptions options, CancellationToken cancellationToken)
  {
    Directory.CreateDirectory(options.OutputDirectory);
    Dictionary<string, ReaderVoicePreviewAsset> completed = LoadExistingManifest(options.OutputDirectory);
    IReadOnlyList<(ReaderLanguageOption Language, ReaderVoiceOption Voice)> inventory = ReaderLanguageRegistry.Languages
      .Where(language => options.ProviderId is null
        || string.Equals(language.ProviderId, options.ProviderId, StringComparison.OrdinalIgnoreCase))
      .SelectMany(language => language.Voices.Select(voice => (language, voice)))
      .ToArray();

    if (inventory.Count == 0)
    {
      throw new InvalidOperationException($"No preview voices matched provider '{options.ProviderId}'.");
    }

    await using ReaderTextToSpeechService speechService = new();
    int current = 0;
    foreach ((ReaderLanguageOption language, ReaderVoiceOption voice) in inventory)
    {
      cancellationToken.ThrowIfCancellationRequested();
      current++;
      string fileName = ReaderVoicePreviewAssetStore.CreateFileName(language, voice);
      string finalPath = Path.Combine(options.OutputDirectory, fileName);
      Console.WriteLine($"[{current}/{inventory.Count}] {language.DisplayName} — {voice.DisplayName}");

      if (options.Overwrite || !File.Exists(finalPath) || new FileInfo(finalPath).Length == 0)
      {
        TextToSpeechResult result = await speechService.SynthesizeAsync(
          new TextToSpeechRequest(
            ReaderVoicePreviewCatalog.GetSample(language.Code),
            language.Code,
            voice.Speaker,
            voice.Description,
            ProviderId: language.ProviderId),
          cancellationToken).ConfigureAwait(false);
        CopyAtomically(result.AudioPath, finalPath);
      }

      ReaderVoicePreviewAsset asset = new(
        language.ProviderId,
        language.Code,
        voice.Id,
        fileName,
        ReadDuration(finalPath).TotalSeconds,
        Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(finalPath, cancellationToken).ConfigureAwait(false))));
      completed[ReaderVoicePreviewAssetStore.CreateKey(language.ProviderId, language.Code, voice.Id)] = asset;
      WriteManifest(options.OutputDirectory, completed.Values);
    }

    long totalBytes = completed.Values
      .Select(asset => Path.Combine(options.OutputDirectory, asset.FileName))
      .Where(File.Exists)
      .Sum(path => new FileInfo(path).Length);
    Console.WriteLine($"Completed {inventory.Count} previews. Pack size: {totalBytes / (1024d * 1024d):F1} MB.");
    return 0;
  }

  private static Dictionary<string, ReaderVoicePreviewAsset> LoadExistingManifest(string outputDirectory)
  {
    string path = Path.Combine(outputDirectory, "manifest.json");
    if (!File.Exists(path))
    {
      return new Dictionary<string, ReaderVoicePreviewAsset>(StringComparer.Ordinal);
    }

    try
    {
      ReaderVoicePreviewManifest? manifest = JsonSerializer.Deserialize<ReaderVoicePreviewManifest>(
        File.ReadAllText(path),
        JsonOptions);
      return manifest?.Previews.ToDictionary(
          asset => ReaderVoicePreviewAssetStore.CreateKey(asset.ProviderId, asset.LanguageCode, asset.VoiceId),
          StringComparer.Ordinal)
        ?? new Dictionary<string, ReaderVoicePreviewAsset>(StringComparer.Ordinal);
    }
    catch (JsonException)
    {
      return new Dictionary<string, ReaderVoicePreviewAsset>(StringComparer.Ordinal);
    }
  }

  private static void WriteManifest(string outputDirectory, IEnumerable<ReaderVoicePreviewAsset> assets)
  {
    ReaderVoicePreviewManifest manifest = new(
      ReaderVoicePreviewManifest.CurrentSchemaVersion,
      DateTimeOffset.UtcNow,
      assets
        .OrderBy(asset => asset.ProviderId, StringComparer.Ordinal)
        .ThenBy(asset => asset.LanguageCode, StringComparer.Ordinal)
        .ThenBy(asset => asset.VoiceId, StringComparer.Ordinal)
        .ToArray());
    string finalPath = Path.Combine(outputDirectory, "manifest.json");
    string temporaryPath = finalPath + ".tmp";
    File.WriteAllText(temporaryPath, JsonSerializer.Serialize(manifest, JsonOptions));
    File.Move(temporaryPath, finalPath, overwrite: true);
  }

  private static void CopyAtomically(string sourcePath, string destinationPath)
  {
    string temporaryPath = destinationPath + ".tmp";
    try
    {
      File.Copy(sourcePath, temporaryPath, overwrite: true);
      File.Move(temporaryPath, destinationPath, overwrite: true);
    }
    finally
    {
      if (File.Exists(temporaryPath))
      {
        File.Delete(temporaryPath);
      }
    }
  }

  private static TimeSpan ReadDuration(string path)
  {
    using WaveFileReader reader = new(path);
    return reader.TotalTime;
  }

  private static void WriteUsage()
  {
    Console.Error.WriteLine("Usage: DictateAnywhere.VoicePreviewGenerator [--output DIRECTORY] [--provider ID] [--overwrite]");
  }

  private sealed record GeneratorOptions(string OutputDirectory, string? ProviderId, bool Overwrite)
  {
    internal static GeneratorOptions Parse(IReadOnlyList<string> args)
    {
      string? output = null;
      string? provider = null;
      bool overwrite = false;
      for (int index = 0; index < args.Count; index++)
      {
        switch (args[index])
        {
          case "--output" when index + 1 < args.Count:
            output = args[++index];
            break;
          case "--provider" when index + 1 < args.Count:
            provider = args[++index];
            break;
          case "--overwrite":
            overwrite = true;
            break;
          default:
            throw new ArgumentException($"Unknown or incomplete option '{args[index]}'.");
        }
      }

      string outputDirectory = output is null
        ? Path.Combine(FindRepositoryRoot(), "src", "DictateAnywhere.App", "Assets", "VoicePreviews")
        : Path.GetFullPath(output);
      return new GeneratorOptions(outputDirectory, string.IsNullOrWhiteSpace(provider) ? null : provider.Trim(), overwrite);
    }

    private static string FindRepositoryRoot()
    {
      DirectoryInfo? directory = new(Environment.CurrentDirectory);
      while (directory is not null)
      {
        if (File.Exists(Path.Combine(directory.FullName, "DictateAnywhere.sln")))
        {
          return directory.FullName;
        }

        directory = directory.Parent;
      }

      throw new ArgumentException("Run the generator inside the Notype repository or pass --output.");
    }
  }
}

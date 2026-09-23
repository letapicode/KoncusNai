using System.Text;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.TtsCli;

internal static class Program
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
  };

  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The CLI boundary converts failures to deterministic exit codes without exposing stack traces.")]
  public static async Task<int> Main(string[] args)
  {
    try
    {
      if (args.Length == 0 || IsHelp(args[0]))
      {
        WriteUsage();
        return args.Length == 0 ? 2 : 0;
      }

      return args[0].ToLowerInvariant() switch
      {
        "languages" => ListLanguages(args[1..]),
        "synthesize" => await SynthesizeAsync(args[1..]).ConfigureAwait(false),
        "batch" => await GenerateBatchAsync(args[1..]).ConfigureAwait(false),
        _ => throw new CliUsageException($"Unknown command '{args[0]}'."),
      };
    }
    catch (CliUsageException exception)
    {
      Console.Error.WriteLine($"Error: {exception.Message}");
      Console.Error.WriteLine("Run with --help to see command usage.");
      return 2;
    }
    catch (OperationCanceledException)
    {
      Console.Error.WriteLine("Synthesis was cancelled.");
      return 130;
    }
    catch (Exception exception)
    {
      Console.Error.WriteLine($"Synthesis failed: {exception.Message}");
      return 1;
    }
  }

  private static int ListLanguages(string[] args)
  {
    bool json = args.Length == 1 && string.Equals(args[0], "--json", StringComparison.OrdinalIgnoreCase);
    if (args.Length > (json ? 1 : 0))
    {
      throw new CliUsageException("languages accepts only the optional --json flag.");
    }

    if (json)
    {
      Console.WriteLine(JsonSerializer.Serialize(IndicParlerLanguageRegistry.Languages, JsonOptions));
      return 0;
    }

    foreach (IndicParlerLanguage language in IndicParlerLanguageRegistry.Languages)
    {
      string speakers = language.AvailableSpeakers.Count == 0
        ? "automatic voice"
        : string.Join(", ", language.AvailableSpeakers);
      Console.WriteLine($"{language.Code,-5} {language.EnglishName,-16} {language.SupportTier,-12} {speakers}");
    }

    return 0;
  }

  private static async Task<int> SynthesizeAsync(string[] args)
  {
    CliArguments options = CliArguments.Parse(args);
    string language = options.Required("language");
    string text = ReadText(options);
    string? output = options.Optional("output");
    int seed = options.OptionalInteger("seed", 42);

    await using IndicParlerTextToSpeechService service = new();
    Progress<IndicParlerSynthesisProgress> progress = new(item =>
      Console.Error.WriteLine($"[chunk {item.CurrentChunk}/{item.TotalChunks}] {item.Elapsed:g}"));
    TextToSpeechResult result = await service.SynthesizeWithProgressAsync(
      new TextToSpeechRequest(
        text,
        language,
        options.Optional("speaker"),
        options.Optional("description"),
        seed),
      progress).ConfigureAwait(false);

    string finalPath = output is null ? result.AudioPath : CopyToRequestedOutput(result.AudioPath, output);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
      output_path = finalPath,
      result.Duration,
      result.SegmentCount,
      result.ProviderId,
      result.ModelId,
      language,
      speaker = options.Optional("speaker"),
      description = options.Optional("description"),
      seed,
      result.RuntimeMetadata,
    }, JsonOptions));
    return 0;
  }

  private static async Task<int> GenerateBatchAsync(string[] args)
  {
    CliArguments options = CliArguments.Parse(args);
    string input = options.Required("input");
    string outputDirectory = options.Required("output-dir");
    IReadOnlyList<IndicParlerBatchItem> items = IndicParlerBatchGenerator.LoadManifest(input);
    Progress<IndicParlerBatchProgress> progress = new(item =>
      Console.Error.WriteLine($"[{item.CurrentItem}/{item.TotalItems}] {item.Elapsed:g}  {item.OutputPath}"));

    await using IndicParlerTextToSpeechService service = new();
    IReadOnlyList<IndicParlerBatchResult> results = await IndicParlerBatchGenerator.GenerateAsync(
      service,
      items,
      outputDirectory,
      progress).ConfigureAwait(false);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
      completed = results.Count,
      output_directory = Path.GetFullPath(outputDirectory),
      results = results.Select(result => new
      {
        result.OutputPath,
        result.MetadataPath,
        result.Synthesis.RuntimeMetadata,
      }),
    }, JsonOptions));
    return 0;
  }

  private static string ReadText(CliArguments options)
  {
    string? inline = options.Optional("text");
    string? file = options.Optional("text-file");
    if ((inline is null) == (file is null))
    {
      throw new CliUsageException("Specify exactly one of --text or --text-file.");
    }

    if (inline is not null)
    {
      return inline;
    }

    if (!File.Exists(file))
    {
      throw new CliUsageException($"Text file not found: {file}");
    }

    return File.ReadAllText(file, Encoding.UTF8);
  }

  private static string CopyToRequestedOutput(string source, string requestedPath)
  {
    string destination = Path.GetFullPath(requestedPath);
    if (!Path.GetExtension(destination).Equals(".wav", StringComparison.OrdinalIgnoreCase))
    {
      throw new CliUsageException("--output must use the .wav extension.");
    }

    string? parent = Path.GetDirectoryName(destination);
    if (!string.IsNullOrWhiteSpace(parent))
    {
      Directory.CreateDirectory(parent);
    }

    File.Copy(source, destination, overwrite: true);
    return destination;
  }

  private static bool IsHelp(string value) => value is "--help" or "-h" or "help";

  private static void WriteUsage()
  {
    Console.WriteLine("""
      Offline Indic Parler-TTS command line interface

      Commands:
        languages [--json]
        synthesize --language CODE (--text TEXT | --text-file PATH)
                   [--speaker NAME] [--description TEXT] [--seed NUMBER] [--output FILE.wav]
        batch --input MANIFEST.json|csv --output-dir DIRECTORY

      Runtime configuration:
        TTS_DEVICE=auto|cpu|cuda|mps
        TTS_DTYPE=auto|float32|bfloat16|float16
        TTS_CPU_THREADS=NUMBER
        TTS_CACHE=true|false
      """);
  }

  private sealed class CliArguments
  {
    private readonly IReadOnlyDictionary<string, string> values;

    private CliArguments(IReadOnlyDictionary<string, string> values)
    {
      this.values = values;
    }

    public static CliArguments Parse(string[] args)
    {
      Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
      for (int index = 0; index < args.Length; index += 2)
      {
        string key = args[index];
        if (!key.StartsWith("--", StringComparison.Ordinal) || key.Length == 2)
        {
          throw new CliUsageException($"Expected an option name, but found '{key}'.");
        }

        if (index + 1 >= args.Length)
        {
          throw new CliUsageException($"Option '{key}' requires a value.");
        }

        string name = key[2..];
        if (!values.TryAdd(name, args[index + 1]))
        {
          throw new CliUsageException($"Option '{key}' was supplied more than once.");
        }
      }

      return new CliArguments(values);
    }

    public string Required(string name) => Optional(name)
      ?? throw new CliUsageException($"Missing required option '--{name}'.");

    public string? Optional(string name) => values.TryGetValue(name, out string? value) ? value : null;

    public int OptionalInteger(string name, int defaultValue)
    {
      string? raw = Optional(name);
      return raw is null
        ? defaultValue
        : int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int value)
          ? value
          : throw new CliUsageException($"Option '--{name}' must be an integer.");
    }
  }

  private sealed class CliUsageException : Exception
  {
    public CliUsageException(string message)
      : base(message)
    {
    }
  }
}

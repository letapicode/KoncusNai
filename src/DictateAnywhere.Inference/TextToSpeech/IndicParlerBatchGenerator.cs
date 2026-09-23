using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Inference;

public sealed record IndicParlerBatchItem(
  string Text,
  string Language,
  [property: JsonPropertyName("output_filename")]
  string OutputFileName,
  string? Speaker = null,
  string? Description = null,
  int Seed = 42);

public sealed record IndicParlerBatchProgress(
  int CurrentItem,
  int TotalItems,
  TimeSpan Elapsed,
  string OutputPath);

public sealed record IndicParlerBatchResult(
  IndicParlerBatchItem Item,
  TextToSpeechResult Synthesis,
  string OutputPath,
  string MetadataPath);

/// <summary>Validates JSON/CSV batch manifests and runs them through one sequential TTS service.</summary>
public static class IndicParlerBatchGenerator
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    WriteIndented = true,
  };

  public static IReadOnlyList<IndicParlerBatchItem> LoadManifest(string manifestPath)
  {
    if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
    {
      throw new FileNotFoundException("The batch manifest was not found.", manifestPath);
    }

    string extension = Path.GetExtension(manifestPath);
    IReadOnlyList<IndicParlerBatchItem> items = extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
      ? LoadJson(manifestPath)
      : extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
        ? LoadCsv(manifestPath)
        : throw new ArgumentException("Batch manifests must be JSON or CSV.", nameof(manifestPath));
    if (items.Count == 0)
    {
      throw new InvalidOperationException("The batch manifest does not contain any synthesis jobs.");
    }

    return items.Select(ValidateItem).ToArray();
  }

  public static async Task<IReadOnlyList<IndicParlerBatchResult>> GenerateAsync(
    ITextToSpeechService service,
    IReadOnlyList<IndicParlerBatchItem> items,
    string outputDirectory,
    IProgress<IndicParlerBatchProgress>? progress = null,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(service);
    ArgumentNullException.ThrowIfNull(items);
    if (items.Count == 0)
    {
      return Array.Empty<IndicParlerBatchResult>();
    }

    string outputRoot = Path.GetFullPath(outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory)));
    Directory.CreateDirectory(outputRoot);
    List<IndicParlerBatchResult> results = [];
    System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
    for (int index = 0; index < items.Count; index++)
    {
      cancellationToken.ThrowIfCancellationRequested();
      IndicParlerBatchItem item = ValidateItem(items[index]);
      string outputPath = ResolveContainedOutputPath(outputRoot, item.OutputFileName);
      TextToSpeechResult synthesis = await service.SynthesizeAsync(
        new TextToSpeechRequest(item.Text, item.Language, item.Speaker, item.Description, item.Seed),
        cancellationToken).ConfigureAwait(false);
      File.Copy(synthesis.AudioPath, outputPath, overwrite: true);
      string metadataPath = outputPath + ".json";
      var auditMetadata = new
      {
        item.Text,
        item.Language,
        item.Speaker,
        item.Description,
        item.Seed,
        output_path = outputPath,
        synthesis.ProviderId,
        synthesis.ModelId,
        synthesis.Duration,
        synthesis.SegmentCount,
        synthesis.RuntimeMetadata,
      };
      await File.WriteAllTextAsync(
        metadataPath,
        JsonSerializer.Serialize(auditMetadata, JsonOptions),
        Encoding.UTF8,
        cancellationToken).ConfigureAwait(false);
      IndicParlerBatchResult result = new(item, synthesis, outputPath, metadataPath);
      results.Add(result);
      progress?.Report(new IndicParlerBatchProgress(index + 1, items.Count, stopwatch.Elapsed, outputPath));
    }

    return results;
  }

  public static string ResolveContainedOutputPath(string outputDirectory, string outputFileName)
  {
    string fileName = outputFileName?.Trim() ?? string.Empty;
    if (fileName.Length == 0
        || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal)
        || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
    {
      throw new ArgumentException("Each output filename must be a safe filename without directories.", nameof(outputFileName));
    }

    if (!Path.GetExtension(fileName).Equals(".wav", StringComparison.OrdinalIgnoreCase))
    {
      throw new ArgumentException("Batch output filenames must use the .wav extension.", nameof(outputFileName));
    }

    string root = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    string result = Path.GetFullPath(Path.Combine(root, fileName));
    return result.StartsWith(root, StringComparison.OrdinalIgnoreCase)
      ? result
      : throw new ArgumentException("The output filename escapes the configured batch directory.", nameof(outputFileName));
  }

  private static IndicParlerBatchItem ValidateItem(IndicParlerBatchItem item)
  {
    ArgumentNullException.ThrowIfNull(item);
    if (string.IsNullOrWhiteSpace(item.Text))
    {
      throw new ArgumentException("Every batch item requires non-empty text.", nameof(item));
    }

    IndicParlerLanguage language = IndicParlerLanguageRegistry.GetRequired(item.Language);
    string? speaker = IndicParlerLanguageRegistry.ResolveSpeaker(language, item.Speaker);
    _ = ResolveContainedOutputPath(Path.GetTempPath(), item.OutputFileName);
    return item with
    {
      Language = language.Code,
      Speaker = speaker,
      Description = string.IsNullOrWhiteSpace(item.Description) ? null : item.Description.Trim(),
      OutputFileName = item.OutputFileName.Trim(),
    };
  }

  private static IReadOnlyList<IndicParlerBatchItem> LoadJson(string path)
  {
    IReadOnlyList<IndicParlerBatchItem>? items = JsonSerializer.Deserialize<List<IndicParlerBatchItem>>(
      File.ReadAllText(path, Encoding.UTF8),
      JsonOptions);
    return items ?? throw new InvalidOperationException("The JSON batch manifest is invalid.");
  }

  private static IReadOnlyList<IndicParlerBatchItem> LoadCsv(string path)
  {
    List<string[]> rows = File.ReadLines(path, Encoding.UTF8)
      .Where(line => !string.IsNullOrWhiteSpace(line))
      .Select(ParseCsvLine)
      .ToList();
    if (rows.Count < 2)
    {
      return Array.Empty<IndicParlerBatchItem>();
    }

    Dictionary<string, int> columns = rows[0]
      .Select((name, index) => (Name: name.Trim().ToLowerInvariant(), Index: index))
      .ToDictionary(column => column.Name, column => column.Index, StringComparer.OrdinalIgnoreCase);
    int Required(string name) => columns.TryGetValue(name, out int index)
      ? index
      : throw new InvalidOperationException($"The CSV manifest is missing the '{name}' column.");
    int text = Required("text");
    int language = Required("language");
    int output = columns.TryGetValue("output_filename", out int outputIndex)
      ? outputIndex
      : Required("output");

    return rows.Skip(1).Select(row => new IndicParlerBatchItem(
      Get(row, text),
      Get(row, language),
      Get(row, output),
      Optional(row, columns, "speaker"),
      Optional(row, columns, "description"),
      int.TryParse(Optional(row, columns, "seed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed) ? seed : 42))
      .ToArray();
  }

  private static string[] ParseCsvLine(string line)
  {
    List<string> fields = [];
    StringBuilder field = new();
    bool quoted = false;
    for (int index = 0; index < line.Length; index++)
    {
      char character = line[index];
      if (character == '"')
      {
        if (quoted && index + 1 < line.Length && line[index + 1] == '"')
        {
          field.Append('"');
          index++;
        }
        else
        {
          quoted = !quoted;
        }
      }
      else if (character == ',' && !quoted)
      {
        fields.Add(field.ToString());
        field.Clear();
      }
      else
      {
        field.Append(character);
      }
    }

    if (quoted)
    {
      throw new InvalidOperationException("The CSV manifest contains an unterminated quoted field.");
    }

    fields.Add(field.ToString());
    return fields.ToArray();
  }

  private static string Get(string[] row, int index) => index < row.Length ? row[index] : string.Empty;

  private static string? Optional(string[] row, IReadOnlyDictionary<string, int> columns, string name)
  {
    return columns.TryGetValue(name, out int index) && index < row.Length && !string.IsNullOrWhiteSpace(row[index])
      ? row[index]
      : null;
  }
}

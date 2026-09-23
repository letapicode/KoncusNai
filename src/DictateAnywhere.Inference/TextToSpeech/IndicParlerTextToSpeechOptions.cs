using System;
using System.Globalization;
using System.IO;

namespace DictateAnywhere.Inference;

public sealed record IndicParlerTextToSpeechOptions(
  string PythonExecutablePath,
  string ScriptFileName,
  string ModelId,
  string ModelCacheRootPath,
  string OutputRootPath,
  TimeSpan WorkerStartupTimeout,
  TimeSpan RequestTimeout,
  int MaximumSegmentCharacters,
  int SilenceBetweenSegmentsMilliseconds,
  string Device,
  string DataType,
  int? CpuThreadCount,
  bool EnableCache)
{
  public static IndicParlerTextToSpeechOptions Default => FromEnvironment();

  public static IndicParlerTextToSpeechOptions FromEnvironment()
  {
    string root = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DictateAnywhere");
    return new IndicParlerTextToSpeechOptions(
      PythonExecutablePath: "python",
      ScriptFileName: "indic_parler_tts_worker.py",
      ModelId: "ai4bharat/indic-parler-tts",
      ModelCacheRootPath: ReadPath("DICTATEANYWHERE_INDIC_PARLER_MODEL_CACHE", Path.Combine(root, "models", "indic-parler-cache")),
      OutputRootPath: ReadPath("DICTATEANYWHERE_INDIC_PARLER_OUTPUT_ROOT", Path.Combine(root, "speech-cache", "indic-parler")),
      WorkerStartupTimeout: TimeSpan.FromMinutes(20),
      RequestTimeout: TimeSpan.FromMinutes(30),
      MaximumSegmentCharacters: 450,
      SilenceBetweenSegmentsMilliseconds: 200,
      Device: ReadChoice("TTS_DEVICE", "auto", ["auto", "cpu", "cuda", "mps"]),
      DataType: ReadChoice("TTS_DTYPE", "auto", ["auto", "float32", "bfloat16", "float16"]),
      CpuThreadCount: ReadPositiveInteger("TTS_CPU_THREADS"),
      EnableCache: ReadBoolean("TTS_CACHE", defaultValue: true));
  }

  private static string ReadPath(string name, string defaultValue)
  {
    string? raw = Environment.GetEnvironmentVariable(name);
    return string.IsNullOrWhiteSpace(raw) ? defaultValue : Path.GetFullPath(raw.Trim());
  }

  private static string ReadChoice(string name, string defaultValue, string[] allowed)
  {
    string value = Environment.GetEnvironmentVariable(name)?.Trim().ToLowerInvariant() ?? defaultValue;
    return Array.Exists(allowed, candidate => string.Equals(candidate, value, StringComparison.Ordinal))
      ? value
      : throw new InvalidOperationException($"{name} must be one of: {string.Join(", ", allowed)}.");
  }

  private static int? ReadPositiveInteger(string name)
  {
    string? raw = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(raw))
    {
      return null;
    }

    return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value > 0
      ? value
      : throw new InvalidOperationException($"{name} must be a positive integer.");
  }

  private static bool ReadBoolean(string name, bool defaultValue)
  {
    string? raw = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(raw))
    {
      return defaultValue;
    }

    return bool.TryParse(raw, out bool value)
      ? value
      : throw new InvalidOperationException($"{name} must be 'true' or 'false'.");
  }
}

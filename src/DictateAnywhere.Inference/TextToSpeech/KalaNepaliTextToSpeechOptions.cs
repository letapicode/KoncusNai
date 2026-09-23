using System;
using System.IO;

namespace DictateAnywhere.Inference;

/// <summary>Configuration for the CPU-native Kala Nepali reader.</summary>
public sealed record KalaNepaliTextToSpeechOptions(
  string PythonExecutablePath,
  string ScriptFileName,
  string ModelCacheRootPath,
  string OutputRootPath,
  TimeSpan WorkerStartupTimeout,
  TimeSpan RequestTimeout,
  int MaximumSegmentCharacters,
  string DefaultVoiceId)
{
  public static KalaNepaliTextToSpeechOptions Default { get; } = new(
    PythonExecutablePath: "python",
    ScriptFileName: "kala_nepali_tts_worker.py",
    ModelCacheRootPath: Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DictateAnywhere",
      "models",
      "kala-nepali-cache"),
    OutputRootPath: Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DictateAnywhere",
      "speech-cache"),
    WorkerStartupTimeout: TimeSpan.FromMinutes(3),
    RequestTimeout: TimeSpan.FromMinutes(2),
    MaximumSegmentCharacters: 700,
    DefaultVoiceId: "kala");
}

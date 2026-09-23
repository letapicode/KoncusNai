using System;
using System.IO;

namespace DictateAnywhere.Inference;

/// <summary>Configuration for the local Kokoro-82M reader.</summary>
public sealed record KokoroTextToSpeechOptions(
  string PythonExecutablePath,
  string ScriptFileName,
  string ModelId,
  string ModelCacheRootPath,
  string OutputRootPath,
  TimeSpan WorkerStartupTimeout,
  TimeSpan RequestTimeout,
  int MaximumSegmentCharacters,
  string DefaultVoiceId)
{
  public static KokoroTextToSpeechOptions Default { get; } = new(
    PythonExecutablePath: "python",
    ScriptFileName: "kokoro_tts_worker.py",
    ModelId: "hexgrad/Kokoro-82M",
    ModelCacheRootPath: Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DictateAnywhere",
      "models",
      "kokoro-cache"),
    OutputRootPath: Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DictateAnywhere",
      "speech-cache"),
    WorkerStartupTimeout: TimeSpan.FromMinutes(3),
    RequestTimeout: TimeSpan.FromMinutes(2),
    MaximumSegmentCharacters: 700,
    DefaultVoiceId: "af_heart");
}

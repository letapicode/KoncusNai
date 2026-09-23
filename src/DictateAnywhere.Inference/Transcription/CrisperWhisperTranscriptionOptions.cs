using System;
using System.Collections.Generic;
using System.IO;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Inference;

public sealed record CrisperWhisperTranscriptionOptions(
  string PythonExecutablePath,
  string ModelRootPath,
  IReadOnlyList<string> AdditionalModelRootPaths,
  string ProviderId,
  string ScriptFileName,
  TimeSpan WorkerStartupTimeout,
  TimeSpan RequestTimeout,
  string Language,
  string Mode)
{
  public static CrisperWhisperTranscriptionOptions Default { get; } = new(
    PythonExecutablePath: "python",
    ModelRootPath: Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DictateAnywhere",
      "models"),
    AdditionalModelRootPaths: Array.Empty<string>(),
    ProviderId: TranscriptionProviderIds.CrisperWhisperLocal,
    ScriptFileName: "crisperwhisper_transcribe_worker.py",
    WorkerStartupTimeout: TimeSpan.FromMinutes(10),
    RequestTimeout: TimeSpan.FromMinutes(15),
    Language: "en",
    Mode: "intended");
}

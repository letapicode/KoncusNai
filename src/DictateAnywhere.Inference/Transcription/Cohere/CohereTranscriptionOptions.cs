using System;
using System.Collections.Generic;
using System.IO;

namespace DictateAnywhere.Inference;

public sealed record CohereTranscriptionOptions(
  string PythonExecutablePath,
  string ModelRootPath,
  IReadOnlyList<string> AdditionalModelRootPaths,
  string ProviderId,
  string ScriptFileName,
  bool EnableWorkerHealthCheck,
  TimeSpan WorkerStartupTimeout,
  TimeSpan RequestTimeout,
  string Language,
  bool EnableAutomaticPunctuation)
{
  public static CohereTranscriptionOptions Default { get; } = new(
    PythonExecutablePath: "python",
    ModelRootPath: Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DictateAnywhere",
      "models"),
    AdditionalModelRootPaths: Array.Empty<string>(),
    ProviderId: "cohere-local",
    ScriptFileName: "cohere_transcribe_worker.py",
    EnableWorkerHealthCheck: true,
    WorkerStartupTimeout: TimeSpan.FromMinutes(10),
    RequestTimeout: TimeSpan.FromMinutes(5),
    Language: "en",
    EnableAutomaticPunctuation: true);
}

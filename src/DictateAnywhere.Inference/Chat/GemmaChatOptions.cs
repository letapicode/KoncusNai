using System;
using System.IO;

namespace DictateAnywhere.Inference;

public sealed record GemmaChatOptions(
  string PythonExecutablePath,
  string ScriptFileName,
  string ModelRootPath,
  string ModelCacheRootPath,
  TimeSpan WorkerStartupTimeout,
  TimeSpan RequestTimeout,
  int MaxNewTokens)
{
  public static GemmaChatOptions Default { get; } = new(
    PythonExecutablePath: "python",
    ScriptFileName: "gemma_chat_worker.py",
    ModelRootPath: Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DictateAnywhere",
      "models"),
    ModelCacheRootPath: Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DictateAnywhere",
      "models",
      "gemma-cache"),
    WorkerStartupTimeout: TimeSpan.FromMinutes(10),
    RequestTimeout: TimeSpan.FromMinutes(15),
    MaxNewTokens: 2048);
}

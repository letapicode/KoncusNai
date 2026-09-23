using System;

namespace DictateAnywhere.Inference;

internal sealed class PersistentPythonWorkerClientFactory : IPersistentWorkerClientFactory
{
  public IPersistentWorkerClient Create(
    string pythonExecutablePath,
    string scriptPath,
    string arguments,
    TimeSpan startupTimeout)
  {
    return new PersistentPythonWorkerClient(
      pythonExecutablePath,
      scriptPath,
      arguments,
      startupTimeout);
  }
}

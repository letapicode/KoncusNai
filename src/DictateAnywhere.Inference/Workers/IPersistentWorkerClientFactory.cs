using System;

namespace DictateAnywhere.Inference;

internal interface IPersistentWorkerClientFactory
{
  IPersistentWorkerClient Create(
    string pythonExecutablePath,
    string scriptPath,
    string arguments,
    TimeSpan startupTimeout);
}

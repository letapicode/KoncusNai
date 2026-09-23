using System;
using System.IO;
using System.Reflection;

namespace DictateAnywhere.Inference.Tests;

public sealed class LocalModelScriptPathResolverTests
{
  [Xunit.Fact]
  public void Resolve_FindsBundledLocalModelAsset()
  {
    Type resolverType = typeof(CohereTranscriptionOptions).Assembly.GetType("DictateAnywhere.Inference.LocalModelScriptPathResolver")
      ?? throw new InvalidOperationException("LocalModelScriptPathResolver type was not found.");
    MethodInfo resolveMethod = resolverType.GetMethod(
      "Resolve",
      BindingFlags.Public | BindingFlags.Static)
      ?? throw new InvalidOperationException("Resolve method was not found.");

    string resolvedPath = (string)(resolveMethod.Invoke(obj: null, parameters: ["cohere_transcribe_worker.py"])
      ?? throw new InvalidOperationException("Resolve returned null."));

    Xunit.Assert.True(File.Exists(resolvedPath));
    Xunit.Assert.EndsWith("local-models\\cohere_transcribe_worker.py", resolvedPath, StringComparison.OrdinalIgnoreCase);
  }
}

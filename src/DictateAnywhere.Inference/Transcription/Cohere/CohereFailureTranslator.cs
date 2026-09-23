using System;

namespace DictateAnywhere.Inference;

internal static class CohereFailureTranslator
{
  public static InferenceException TranslateWorkerException(Exception exception, string stage)
  {
    if (exception is InferenceException inferenceException)
    {
      return inferenceException;
    }

    if (exception is TimeoutException timeoutException)
    {
      return new InferenceException(
        $"Cohere transcription timed out. {timeoutException.Message}",
        InferenceFailureReason.ProcessTimedOut,
        "The Cohere local worker timed out before returning text.",
        timeoutException);
    }

    string message = (exception.Message ?? string.Empty).Trim();
    if (LooksLikeMissingPythonDependency(message, out string? dependencyName))
    {
      string runtimeMessage = dependencyName is null
        ? "Cohere local runtime is missing required Python dependencies. Run scripts\\setup-local-model-runtime.ps1 and retry."
        : $"Cohere local runtime is missing required Python dependency '{dependencyName}'. Run scripts\\setup-local-model-runtime.ps1 and retry.";
      return new InferenceException(
        runtimeMessage,
        InferenceFailureReason.RuntimeUnavailable,
        "The Cohere local model runtime is missing required Python dependencies.",
        exception);
    }

    if (LooksLikeUnsupportedTransformersVersion(message))
    {
      return new InferenceException(
        "Cohere local runtime requires a transformers version with Cohere ASR support. Run scripts\\setup-local-model-runtime.ps1 and retry.",
        InferenceFailureReason.RuntimeUnavailable,
        "The Cohere local model runtime does not expose CohereAsrForConditionalGeneration.",
        exception);
    }

    if (message.Contains("Unable to start python worker", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Python worker startup timed out", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Python worker exited during startup", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Python worker could not be started", StringComparison.OrdinalIgnoreCase))
    {
      return new InferenceException(
        $"Cohere local runtime failed during startup. {message}",
        InferenceFailureReason.RuntimeUnavailable,
        "The Cohere local model runtime could not start cleanly.",
        exception);
    }

    return new InferenceException(
      $"Cohere transcription failed during {stage}. {message}",
      InferenceFailureReason.ProcessExitedWithError,
      "The Cohere local worker exited with an error before returning text.",
      exception);
  }

  public static string? GetExceptionDetail(Exception exception)
  {
    Exception detailSource = exception.InnerException ?? exception;
    string message = (detailSource.Message ?? string.Empty).Trim();
    if (message.Length == 0)
    {
      return null;
    }

    const int maxLength = 4000;
    return message.Length <= maxLength
      ? message
      : string.Concat(message.AsSpan(0, maxLength), "...");
  }

  private static bool LooksLikeUnsupportedTransformersVersion(string message)
  {
    return message.Contains("CohereAsrForConditionalGeneration", StringComparison.OrdinalIgnoreCase)
      && (message.Contains("cannot import name", StringComparison.OrdinalIgnoreCase)
          || message.Contains("has no attribute", StringComparison.OrdinalIgnoreCase)
          || message.Contains("is not defined", StringComparison.OrdinalIgnoreCase));
  }

  private static bool LooksLikeMissingPythonDependency(string message, out string? dependencyName)
  {
    string[] knownDependencies =
    [
      "librosa",
      "soundfile",
      "numpy",
      "torch",
      "transformers",
      "sentencepiece",
      "protobuf",
    ];

    foreach (string dependency in knownDependencies)
    {
      if (message.Contains($"No module named '{dependency}'", StringComparison.OrdinalIgnoreCase)
          || message.Contains($"packages that were not found in your environment: {dependency}", StringComparison.OrdinalIgnoreCase)
          || message.Contains($"requires the following packages that were not found in your environment: {dependency}", StringComparison.OrdinalIgnoreCase))
      {
        dependencyName = dependency;
        return true;
      }
    }

    dependencyName = null;
    return false;
  }
}

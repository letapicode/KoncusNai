using System;
using DictateAnywhere.Inference;

namespace DictateAnywhere.Inference.Tests;

public sealed class CohereFailureTranslatorTests
{
  [Xunit.Fact]
  public void TranslateWorkerException_MapsMissingDependency_ToRuntimeUnavailable()
  {
    InvalidOperationException workerError = new(
      "This modeling file requires the following packages that were not found in your environment: librosa.");

    InferenceException translated = CohereFailureTranslator.TranslateWorkerException(workerError, "workerReady");

    Xunit.Assert.Equal(InferenceFailureReason.RuntimeUnavailable, translated.Reason);
    Xunit.Assert.Contains("librosa", translated.Message, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Equal(
      "The Cohere local model runtime is missing required Python dependencies.",
      translated.DiagnosticSummary);
  }

  [Xunit.Fact]
  public void TranslateWorkerException_MapsTimeout_ToProcessTimedOut()
  {
    TimeoutException workerError = new("Python worker request timed out after 45.0s.");

    InferenceException translated = CohereFailureTranslator.TranslateWorkerException(workerError, "requestDispatched");

    Xunit.Assert.Equal(InferenceFailureReason.ProcessTimedOut, translated.Reason);
    Xunit.Assert.Equal("The Cohere local worker timed out before returning text.", translated.DiagnosticSummary);
  }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DictateAnywhere.Core.Contracts;
using Xunit;

namespace DictateAnywhere.Core.Tests;

public sealed class CorePublicSurfaceTests
{
  private static readonly HashSet<string> ApprovedPublicTypeNames = new(StringComparer.Ordinal)
  {
    "DictateAnywhere.Core.Contracts.AppLegalAcceptancePolicy",
    "DictateAnywhere.Core.Contracts.AppSettings",
    "DictateAnywhere.Core.Contracts.AppSettingsTranscriptionSelection",
    "DictateAnywhere.Core.Contracts.AppThemePreference",
    "DictateAnywhere.Core.Contracts.AudioCaptureChunk",
    "DictateAnywhere.Core.Contracts.AudioCaptureChunkAvailableEventArgs",
    "DictateAnywhere.Core.Contracts.AudioCaptureResult",
    "DictateAnywhere.Core.Contracts.BenchmarkCandidateMeasurement",
    "DictateAnywhere.Core.Contracts.BenchmarkResult",
    "DictateAnywhere.Core.Contracts.ChatCompletionRequest",
    "DictateAnywhere.Core.Contracts.ChatCompletionResult",
    "DictateAnywhere.Core.Contracts.ChatMessage",
    "DictateAnywhere.Core.Contracts.ChatMessageRoles",
    "DictateAnywhere.Core.Contracts.ChatModelSelection",
    "DictateAnywhere.Core.Contracts.ChatProviderIds",
    "DictateAnywhere.Core.Contracts.ChatTypefaceIds",
    "DictateAnywhere.Core.Contracts.ChatTypefaceSettings",
    "DictateAnywhere.Core.Contracts.CrisperWhisperLicensePolicy",
    "DictateAnywhere.Core.Contracts.DictationHistoryRecord",
    "DictateAnywhere.Core.Contracts.DictationStatusMessages",
    "DictateAnywhere.Core.Contracts.DocumentOcrLine",
    "DictateAnywhere.Core.Contracts.DocumentOcrRequest",
    "DictateAnywhere.Core.Contracts.DocumentOcrResult",
    "DictateAnywhere.Core.Contracts.HotkeyBinding",
    "DictateAnywhere.Core.Contracts.HotkeyEventArgs",
    "DictateAnywhere.Core.Contracts.HotkeyModifiers",
    "DictateAnywhere.Core.Contracts.HotkeyRegistrationResult",
    "DictateAnywhere.Core.Contracts.IAudioCaptureService",
    "DictateAnywhere.Core.Contracts.IBenchmarkService",
    "DictateAnywhere.Core.Contracts.IChatCompletionService",
    "DictateAnywhere.Core.Contracts.IChunkedAudioCaptureService",
    "DictateAnywhere.Core.Contracts.IDiagnostics",
    "DictateAnywhere.Core.Contracts.IDictationHistoryRecorder",
    "DictateAnywhere.Core.Contracts.IDocumentOcrService",
    "DictateAnywhere.Core.Contracts.IHotkeyService",
    "DictateAnywhere.Core.Contracts.IModelManager",
    "DictateAnywhere.Core.Contracts.IOverlayAnchorProvider",
    "DictateAnywhere.Core.Contracts.IOverlayService",
    "DictateAnywhere.Core.Contracts.IProviderModelManager",
    "DictateAnywhere.Core.Contracts.ISettingsStore",
    "DictateAnywhere.Core.Contracts.ISpeechAlignmentService",
    "DictateAnywhere.Core.Contracts.IStructuredDiagnostics",
    "DictateAnywhere.Core.Contracts.ITextInsertionService",
    "DictateAnywhere.Core.Contracts.ITextInsertionTargetSession",
    "DictateAnywhere.Core.Contracts.ITextToSpeechService",
    "DictateAnywhere.Core.Contracts.ITextTransformationService",
    "DictateAnywhere.Core.Contracts.ITranscriptionService",
    "DictateAnywhere.Core.Contracts.IUndoInsertionService",
    "DictateAnywhere.Core.Contracts.InsertionBlockReason",
    "DictateAnywhere.Core.Contracts.InsertionMethod",
    "DictateAnywhere.Core.Contracts.InsertionOutcome",
    "DictateAnywhere.Core.Contracts.InsertionResult",
    "DictateAnywhere.Core.Contracts.ModelInfo",
    "DictateAnywhere.Core.Contracts.OverlayAnchorSnapshot",
    "DictateAnywhere.Core.Contracts.OverlayAnchorSource",
    "DictateAnywhere.Core.Contracts.OverlayAnimationState",
    "DictateAnywhere.Core.Contracts.OverlayDisplayOptions",
    "DictateAnywhere.Core.Contracts.OverlayIndicatorKind",
    "DictateAnywhere.Core.Contracts.OverlayPlacementMode",
    "DictateAnywhere.Core.Contracts.RecordingMode",
    "DictateAnywhere.Core.Contracts.ScreenBounds",
    "DictateAnywhere.Core.Contracts.SpeechAlignmentRequest",
    "DictateAnywhere.Core.Contracts.SpeechAlignmentResult",
    "DictateAnywhere.Core.Contracts.SpeechWordTiming",
    "DictateAnywhere.Core.Contracts.TextToSpeechProviderIds",
    "DictateAnywhere.Core.Contracts.TextToSpeechRequest",
    "DictateAnywhere.Core.Contracts.TextToSpeechResult",
    "DictateAnywhere.Core.Contracts.TextToSpeechRuntimeMetadata",
    "DictateAnywhere.Core.Contracts.TextTransformationOptions",
    "DictateAnywhere.Core.Contracts.TextTransformationRequest",
    "DictateAnywhere.Core.Contracts.TextTransformationResult",
    "DictateAnywhere.Core.Contracts.TranscriptionLanguageSettings",
    "DictateAnywhere.Core.Contracts.TranscriptionModelSelection",
    "DictateAnywhere.Core.Contracts.TranscriptionProviderIds",
    "DictateAnywhere.Core.Contracts.TranscriptionResult",
    "DictateAnywhere.Core.Contracts.UndoInsertionResult",
    "DictateAnywhere.Core.Domain.DictationSessionState",
    "DictateAnywhere.Core.Services.CurrentSettingsPolicy",
    "DictateAnywhere.Core.Services.DictationPipelineCoordinator",
    "DictateAnywhere.Core.Services.FaultTolerantOverlayService",
    "DictateAnywhere.Core.Services.RuleBasedTextTransformationService",
    "DictateAnywhere.Core.Services.SpeechTextChunker",
  };

  [Fact]
  public void CorePublicSurface_MatchesApprovedCanonicalInventory()
  {
    Assembly coreAssembly = typeof(AppSettings).Assembly;
    string[] actualExported = coreAssembly.GetExportedTypes()
      .Select(t => t.FullName!)
      .Where(name => !name.StartsWith("System.", StringComparison.Ordinal))
      .OrderBy(name => name, StringComparer.Ordinal)
      .ToArray();

    List<string> unexpected = actualExported
      .Where(name => !ApprovedPublicTypeNames.Contains(name))
      .ToList();

    List<string> missing = ApprovedPublicTypeNames
      .Where(name => !actualExported.Contains(name))
      .ToList();

    Assert.True(
      unexpected.Count == 0 && missing.Count == 0,
      $"Core public surface deviation.{Environment.NewLine}" +
      $"Unexpected ({unexpected.Count}): {string.Join(", ", unexpected)}{Environment.NewLine}" +
      $"Missing ({missing.Count}): {string.Join(", ", missing)}");

    Assert.Equal(82, actualExported.Length);
  }

  [Theory]
  [InlineData("DictateAnywhere.Core.Domain.DictationSessionStateMachine")]
  [InlineData("DictateAnywhere.Core.Services.ChunkedTranscriptionSession")]
  [InlineData("DictateAnywhere.Core.Services.TranscriptChunkCombiner")]
  [InlineData("DictateAnywhere.Core.Services.TranscriptionChunkResult")]
  public void InternalImplementationTypes_AreNotExported(string internalTypeName)
  {
    Assembly coreAssembly = typeof(AppSettings).Assembly;
    Type? type = coreAssembly.GetType(internalTypeName, throwOnError: false);

    Assert.NotNull(type);
    Assert.False(type!.IsVisible, $"Type '{internalTypeName}' must not be publicly visible.");
  }

  [Theory]
  [InlineData("DictateAnywhere.Core.Contracts.LocalTranscriptionProviderDefinition")]
  [InlineData("DictateAnywhere.Core.Contracts.ModelProviderOperationalMetadata")]
  [InlineData("DictateAnywhere.Core.Contracts.ChatHistoryRecord")]
  [InlineData("DictateAnywhere.Core.Services.LocalGreetingResponder")]
  public void RelocatedTypes_NoLongerExistInCore(string retiredCoreTypeName)
  {
    Assembly coreAssembly = typeof(AppSettings).Assembly;
    Type? type = coreAssembly.GetType(retiredCoreTypeName, throwOnError: false);

    Assert.Null(type);
  }
}

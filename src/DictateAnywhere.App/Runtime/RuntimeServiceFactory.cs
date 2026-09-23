using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using DictateAnywhere.Audio;
using DictateAnywhere.Audio.WASAPI;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;
using DictateAnywhere.Hotkeys;
using DictateAnywhere.Inference;
using DictateAnywhere.Insertion;

namespace DictateAnywhere.App.Runtime;

internal static class RuntimeServiceFactory
{
  [SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "WasapiAudioCaptureService takes ownership of the input source and disposes it.")]
  public static IAudioCaptureService CreateAudioCaptureService(AppSettings settings)
  {
    AudioCaptureOptions options = AudioCaptureOptions.Default with
    {
      PreferredInputDeviceId = settings.PreferredAudioInputDeviceId,
    };

    return new WasapiAudioCaptureService(new WasapiAudioInputSource(), options);
  }

  public static ITranscriptionService CreateTranscriptionService(AppSettings settings)
  {
    return CreateTranscriptionService(settings, registry: null, diagnostics: null);
  }

  internal static ITranscriptionService CreateTranscriptionService(
    AppSettings settings,
    LocalTranscriptionProviderRegistry? registry,
    IDiagnostics? diagnostics = null)
  {
    ArgumentNullException.ThrowIfNull(settings);

    TranscriptionModelSelection selection = RuntimeServiceSelection.ResolveTranscription(settings);
    LocalTranscriptionProviderRegistry providerRegistry = registry ?? LocalTranscriptionProviderRegistry.CreateDefault();
    if (!providerRegistry.TryResolve(selection.ProviderId, out LocalTranscriptionProviderRegistration? registration)
        || registration is null)
    {
      throw new InvalidOperationException($"No local transcription provider is registered for '{selection.ProviderId}'.");
    }

    return registration.Factory(settings, diagnostics);
  }

  public static ITextTransformationService CreateTextTransformationService(AppSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);
    return new RuleBasedTextTransformationService();
  }

  public static ITextToSpeechService CreateTextToSpeechService()
  {
    return new ReaderTextToSpeechService();
  }

  [SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "ReaderSpeechAlignmentService takes ownership of the gated CrisperWhisper service and disposes the complete alignment graph.")]
  public static ISpeechAlignmentService CreateSpeechAlignmentService(ISettingsStore settingsStore)
  {
    ArgumentNullException.ThrowIfNull(settingsStore);
    return new ReaderSpeechAlignmentService(
      new CrisperWhisperLicensedAlignmentService(
        settingsStore,
        new CrisperWhisperForcedAlignmentService()));
  }

  public static IDocumentOcrService CreateDocumentOcrService()
  {
    return new RapidOcrDocumentService();
  }

  internal static IChatCompletionService CreateChatCompletionService(
    ChatModelSelection selection,
    LocalChatProviderRegistry? registry = null)
  {
    ChatModelSelection normalizedSelection = (selection ?? ChatModelSelection.Default).Normalize();
    LocalChatProviderRegistry providerRegistry = registry ?? LocalChatProviderRegistry.CreateDefault();
    if (!providerRegistry.TryResolve(normalizedSelection.ProviderId, out LocalChatProviderRegistration? registration)
        || registration is null)
    {
      throw new InvalidOperationException($"No local chat provider is registered for '{normalizedSelection.ProviderId}'.");
    }

    return registration.Factory(normalizedSelection);
  }

  public static IHotkeyService CreateHotkeyService(int? hotkeyId = null)
  {
    return hotkeyId.HasValue
      ? new WindowsHotkeyService(hotkeyId.Value)
      : new WindowsHotkeyService();
  }

  internal static WindowsTextInsertionService CreateTextInsertionService(
    AppSettings settings,
    IDiagnostics diagnostics)
  {
    ArgumentNullException.ThrowIfNull(settings);
    ArgumentNullException.ThrowIfNull(diagnostics);

    IWindowFocusProvider windowFocusProvider = new WindowsWindowFocusProvider();
    TextInsertionOptions insertionOptions = new(
      EnableSecureFieldDetection: settings.EnableSecureFieldDetection,
      BlockedProcessNames: settings.InsertionBlockedProcessNames ?? Array.Empty<string>(),
      EnableElevatedInsertion: settings.EnableElevatedInsertion);

    return new WindowsTextInsertionService(
      new WindowsClipboardController(),
      new WindowsInputDispatcher(),
      windowFocusProvider,
      new WindowsPrivilegeBoundaryDetector(),
      insertionOptions,
      new UiAccessHelperProcessBridge(UiAccessHelperProcessBridgeOptions.Default, diagnostics),
      diagnostics);
  }

  internal static TranscriptionModelRegistry CreateTranscriptionModelRegistry(
    AppSettings settings,
    IDiagnostics? diagnostics)
  {
    string compatibleLanguage = TranscriptionLanguageCompatibilityPolicy.ResolveCompatibleLanguage(settings);
    CohereTranscriptionOptions cohereOptions = CohereTranscriptionOptions.Default with
    {
      Language = compatibleLanguage,
      EnableAutomaticPunctuation = settings.EnableAutomaticPunctuation,
    };
    CrisperWhisperTranscriptionOptions crisperWhisperOptions = CrisperWhisperTranscriptionOptions.Default with
    {
      Language = compatibleLanguage,
    };

    return TranscriptionModelRegistry.CreateDefault(cohereOptions, crisperWhisperOptions, diagnostics);
  }

  internal static ITranscriptionService CreateCohereTranscriptionService(AppSettings settings)
  {
    return CreateCohereTranscriptionService(settings, diagnostics: null);
  }

  internal static ITranscriptionService CreateCohereTranscriptionService(
    AppSettings settings,
    IDiagnostics? diagnostics)
  {
    CohereTranscriptionOptions options = CohereTranscriptionOptions.Default with
    {
      Language = TranscriptionLanguageCompatibilityPolicy.ResolveCompatibleLanguage(settings),
      EnableAutomaticPunctuation = settings.EnableAutomaticPunctuation,
    };

    CohereTranscriptionService service = new(options, diagnostics);
    service.WarmUpInBackground(settings.GetConfiguredTranscriptionModelId());
    return service;
  }

  [SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "The returned transcription service owns and disposes its persistent local worker.")]
  internal static ITranscriptionService CreateCrisperWhisperTranscriptionService(AppSettings settings)
  {
    return CreateCrisperWhisperTranscriptionService(settings, diagnostics: null);
  }

  internal static ITranscriptionService CreateCrisperWhisperTranscriptionService(
    AppSettings settings,
    IDiagnostics? diagnostics)
  {
    CrisperWhisperTranscriptionOptions options = CrisperWhisperTranscriptionOptions.Default with
    {
      Language = TranscriptionLanguageCompatibilityPolicy.ResolveCompatibleLanguage(settings),
    };
    CrisperWhisperTranscriptionService service = new(options);
    string modelId = settings.GetConfiguredTranscriptionModelId();
    service.WarmUpInBackground(modelId, diagnostics);
    return service;
  }

}

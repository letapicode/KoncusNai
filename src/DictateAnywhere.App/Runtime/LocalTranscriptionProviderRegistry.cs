using System;
using System.Collections.Generic;
using System.Linq;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Models;

namespace DictateAnywhere.App.Runtime;

internal sealed class LocalTranscriptionProviderRegistry
{
  private readonly IReadOnlyDictionary<string, LocalTranscriptionProviderRegistration> registrationsByProviderId;

  public LocalTranscriptionProviderRegistry(IEnumerable<LocalTranscriptionProviderRegistration> registrations)
  {
    ArgumentNullException.ThrowIfNull(registrations);

    Dictionary<string, LocalTranscriptionProviderRegistration> indexed = new(StringComparer.OrdinalIgnoreCase);
    foreach (LocalTranscriptionProviderRegistration registration in registrations)
    {
      ArgumentNullException.ThrowIfNull(registration);
      if (!indexed.TryAdd(registration.Definition.ProviderId, registration))
      {
        throw new InvalidOperationException(
          $"Duplicate local transcription provider registration for '{registration.Definition.ProviderId}'.");
      }
    }

    if (indexed.Count == 0)
    {
      throw new InvalidOperationException("At least one local transcription provider registration is required.");
    }

    registrationsByProviderId = indexed;
  }

  public bool TryResolve(string providerId, out LocalTranscriptionProviderRegistration? registration)
  {
    string normalizedProviderId = string.IsNullOrWhiteSpace(providerId)
      ? string.Empty
      : providerId.Trim();
    return registrationsByProviderId.TryGetValue(normalizedProviderId, out registration);
  }

  public IReadOnlyList<LocalTranscriptionProviderDefinition> GetDefinitions()
  {
    return registrationsByProviderId.Values
      .Select(registration => registration.Definition)
      .OrderBy(definition => definition.DisplayName, StringComparer.OrdinalIgnoreCase)
      .ToArray();
  }

  public IModelManager CreateModelManager() => new CompositeModelManager(
    registrationsByProviderId.Values
      .OrderBy(registration => registration.Definition.ProviderId, StringComparer.OrdinalIgnoreCase)
      .Select(registration => registration.ModelManagerFactory())
      .ToArray());

  public static LocalTranscriptionProviderRegistry CreateDefault()
  {
    LocalTranscriptionProviderDefinition cohere = CreateCohereDefinition();
    LocalTranscriptionProviderDefinition crisperWhisper = CreateCrisperWhisperDefinition();
    return new LocalTranscriptionProviderRegistry(
    [
      new LocalTranscriptionProviderRegistration(
        cohere,
        RuntimeServiceFactory.CreateCohereTranscriptionService,
        () => CreateModelManager(cohere)),
      new LocalTranscriptionProviderRegistration(
        crisperWhisper,
        RuntimeServiceFactory.CreateCrisperWhisperTranscriptionService,
        () => CreateModelManager(crisperWhisper)),
    ]);
  }

  private static LocalTranscriptionProviderDefinition CreateCohereDefinition()
  {
    return new LocalTranscriptionProviderDefinition(
      TranscriptionProviderIds.CohereLocal,
      "Cohere",
      new LocalTranscriptionProviderCapabilities(
        SupportedLanguages:
        [
          "en",
          "fr",
          "de",
          "it",
          "es",
          "pt",
          "el",
          "nl",
          "pl",
          "zh",
          "ja",
          "ko",
          "vi",
          "ar",
        ],
        StreamingMode: TranscriptionStreamingMode.BatchOnly,
        HardwareRequirement: LocalExecutionHardwareRequirement.GpuPreferred,
        SupportsBenchmarking: false,
        SupportsPunctuationControl: true),
      [
        new LocalTranscriptionModelDefinition(
          "cohere-transcribe-03-2026",
          "Cohere",
          "CohereLabs/cohere-transcribe-03-2026",
          "32d9e4ba6271d78168c095c2f90bc173eaad97d2",
          ["en", "fr", "de", "it", "es", "pt", "el", "nl", "pl", "zh", "ja", "ko", "vi", "ar"],
          [".eval_results/**", "assets/**", "demo/**", ".gitattributes"],
          RequiresAuthentication: true),
      ],
      [
        new LocalModelResourceRequirement(
          LocalModelResourceKind.ModelWeights,
          "CohereLabs/cohere-transcribe-03-2026",
          IsRequired: true,
          IsDownloadManaged: true,
          Description: "Open-weight Cohere Transcribe checkpoint assets managed from a local-only source."),
        new LocalModelResourceRequirement(
          LocalModelResourceKind.Tokenizer,
          "processor/tokenizer config",
          IsRequired: true,
          IsDownloadManaged: true,
          Description: "Associated processor, tokenizer, and generation config assets."),
        new LocalModelResourceRequirement(
          LocalModelResourceKind.RuntimeLibrary,
          "transformers/vLLM/approved offline adapter",
          IsRequired: true,
          IsDownloadManaged: false,
          Description: "Validated local runtime stack for offline inference."),
      ],
      ModelProviderOperationalMetadata.LocalOffline,
      Description: "GPU preferred; 14 supported languages.",
      UsageNotice: "The first download is gated. Accept the Cohere model terms and sign in with your own Hugging Face account using 'hf auth login'. The token stays in the user's Hugging Face credential store.");
  }

  private static LocalTranscriptionProviderDefinition CreateCrisperWhisperDefinition()
  {
    return new LocalTranscriptionProviderDefinition(
      TranscriptionProviderIds.CrisperWhisperLocal,
      "CrisperWhisper",
      new LocalTranscriptionProviderCapabilities(
        SupportedLanguages: TranscriptionLanguageSettings.WhisperLanguageCodes,
        StreamingMode: TranscriptionStreamingMode.BatchOnly,
        HardwareRequirement: LocalExecutionHardwareRequirement.GpuPreferred,
        SupportsBenchmarking: false),
      [
        new LocalTranscriptionModelDefinition(
          "crisperwhisper-2-turbo",
          "CrisperWhisper Turbo (fast transcription)",
          "nyralabs/CrisperWhisper2.0_turbo",
          "de0369c8a68025b7f6e86387b6eb5a3b369787c8",
          TranscriptionLanguageSettings.WhisperLanguageCodes,
          [".gitattributes"]),
        new LocalTranscriptionModelDefinition(
          "crisperwhisper-2-large",
          "CrisperWhisper Large (highest quality)",
          "nyralabs/CrisperWhisper2.0_large",
          "f4334f6e8193f2691212d49b20fa12d370e13896",
          TranscriptionLanguageSettings.WhisperLanguageCodes,
          [".gitattributes"]),
      ],
      [
        new LocalModelResourceRequirement(
          LocalModelResourceKind.ModelWeights,
          "nyralabs/CrisperWhisper2.0_<size>",
          IsRequired: true,
          IsDownloadManaged: true,
          Description: "CrisperWhisper 2.0 checkpoint assets managed locally from the selected model repository."),
        new LocalModelResourceRequirement(
          LocalModelResourceKind.RuntimeLibrary,
          "crisperwhisper[transformers]",
          IsRequired: true,
          IsDownloadManaged: false,
          Description: "Validated local Python runtime. Intended mode removes filler words and formats speech into readable text."),
      ],
      ModelProviderOperationalMetadata.LocalOffline,
      Description: $"GPU preferred; {TranscriptionLanguageSettings.WhisperLanguageCodes.Count()} supported languages.",
      UsageNotice: "Research-only optional model. Its weights and outputs, including transcripts and timestamps, are not licensed for ordinary operational or commercial use. Review and accept the upstream license before downloading or activating it.");
  }

  private static IProviderModelManager CreateModelManager(LocalTranscriptionProviderDefinition definition) =>
    new HuggingFaceSnapshotModelManager(
      definition.ProviderId,
      definition.Models.Select(model => new RepositoryModelManifestEntry(
        model.ModelId,
        model.DisplayName,
        model.RepositoryId,
        model.SupportedLanguages,
        Revision: model.Revision,
        IgnorePatterns: model.IgnorePatterns,
        RequiresAuthentication: model.RequiresAuthentication)).ToArray(),
      ModelManagerOptions.Default);
}

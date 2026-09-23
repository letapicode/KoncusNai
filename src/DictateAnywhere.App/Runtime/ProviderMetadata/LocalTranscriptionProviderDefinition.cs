using System.Collections.Generic;

namespace DictateAnywhere.App.Runtime;

internal sealed record LocalTranscriptionProviderDefinition(
  string ProviderId,
  string DisplayName,
  LocalTranscriptionProviderCapabilities Capabilities,
  IReadOnlyList<LocalTranscriptionModelDefinition> Models,
  IReadOnlyList<LocalModelResourceRequirement> ResourceRequirements,
  ModelProviderOperationalMetadata OperationalMetadata,
  string Description = "",
  string? UsageNotice = null);

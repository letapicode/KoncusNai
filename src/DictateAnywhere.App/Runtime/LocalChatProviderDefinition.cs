using System.Collections.Generic;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Runtime;

internal sealed record LocalChatProviderDefinition(
  string ProviderId,
  string DisplayName,
  string Description,
  IReadOnlyList<LocalChatModelDefinition> Models,
  ModelProviderOperationalMetadata OperationalMetadata);

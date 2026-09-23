using System;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Runtime;

internal sealed record LocalChatProviderRegistration(
  LocalChatProviderDefinition Definition,
  Func<ChatModelSelection, IChatCompletionService> Factory)
{
  public string ProviderId => Definition.ProviderId;
}

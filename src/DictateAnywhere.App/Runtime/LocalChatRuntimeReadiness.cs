using System.Collections.Generic;

namespace DictateAnywhere.App.Runtime;

internal sealed record LocalChatRuntimeReadiness(
  bool IsReady,
  string StatusMessage,
  IReadOnlyList<string> MissingModules,
  IReadOnlyList<string> BrokenModules)
{
  public static LocalChatRuntimeReadiness Ready { get; } = new(
    true,
    "Local model runtime is ready.",
    [],
    []);
}

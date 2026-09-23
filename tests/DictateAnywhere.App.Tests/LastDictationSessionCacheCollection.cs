using Xunit;

namespace DictateAnywhere.App.Tests;

/// <summary>Serializes tests that share the process-wide last-dictation cache.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LastDictationSessionCacheCollection
{
  public const string Name = "Last dictation session cache";
}

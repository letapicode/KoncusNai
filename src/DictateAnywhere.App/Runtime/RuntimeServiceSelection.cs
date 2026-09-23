using System;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Runtime;

internal static class RuntimeServiceSelection
{
  public static TranscriptionModelSelection ResolveTranscription(AppSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);
    return settings.GetConfiguredTranscriptionSelection();
  }
}

using DictateAnywhere.Core.Services;
using System;
using System.Collections.Generic;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Productivity;

internal static class RetryActionDiagnostics
{
  internal static void Write(IStructuredDiagnostics diagnostics, ProductivityActionResult result)
  {
    ArgumentNullException.ThrowIfNull(diagnostics);
    ArgumentNullException.ThrowIfNull(result);

    // Outcome codes are fixed by our retry flow. Do not record the result message or dictation text.
    DiagnosticBoundary.Report(() => diagnostics.Info("Retry last dictation completed.", new Dictionary<string, object?>
    {
      ["outcome"] = result.OutcomeCode.ToString(),
      ["succeeded"] = result.Success,
    }));
  }
}

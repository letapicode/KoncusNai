using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;
using DictateAnywhere.Insertion;

namespace DictateAnywhere.App.Productivity;

internal static class ProductivityTextActions
{
  public static async Task<ProductivityActionResult> RetryLastDictationAsync(
    AppSettings settings,
    IDiagnostics diagnostics,
    Func<CancellationToken, Task<DictationHistoryRecord?>> readLatestAsync,
    Func<AppSettings, IDiagnostics, ITextInsertionService> createInsertionService,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(settings);
    ArgumentNullException.ThrowIfNull(diagnostics);
    ArgumentNullException.ThrowIfNull(readLatestAsync);
    ArgumentNullException.ThrowIfNull(createInsertionService);

    LastDictationRetryResolution resolution = await LastDictationRetryResolver
      .ResolveAsync(
        settings,
        readLatestAsync,
        cancellationToken)
      .ConfigureAwait(false);
    if (!resolution.Success || resolution.Record is null)
    {
      return ProductivityActionResult.Failed(resolution.Message, resolution.FailureOutcome);
    }

    string text = resolution.Record.FinalText;
    if (string.IsNullOrWhiteSpace(text))
    {
      return ProductivityActionResult.Failed("The last dictation has no insertable text.", RetryOutcomeCode.EmptyText);
    }

    ITextInsertionService insertionService = createInsertionService(settings, diagnostics);
    InsertionResult insertion = await insertionService
      .InsertAsync(
        text,
        settings.PreferredInsertionMethod,
        settings.RestoreClipboard,
        cancellationToken)
      .ConfigureAwait(false);

    return insertion.Success
      ? ProductivityActionResult.Succeeded("Last dictation retried.", RetryOutcomeCode.Inserted)
      : ProductivityActionResult.Failed(insertion.ErrorMessage ?? $"Insertion returned {insertion.Outcome}.",
        RetryOutcomeCode.InsertionFailed);
  }

}

internal enum RetryOutcomeCode
{
  None,
  Inserted,
  NoHistory,
  Expired,
  EmptyText,
  InsertionFailed,
}

internal sealed record ProductivityActionResult(bool Success, string Message, RetryOutcomeCode OutcomeCode)
{
  public static ProductivityActionResult Succeeded(string message, RetryOutcomeCode outcomeCode) =>
    new(true, message, outcomeCode);

  public static ProductivityActionResult Failed(string message, RetryOutcomeCode outcomeCode) =>
    new(false, message, outcomeCode);
}

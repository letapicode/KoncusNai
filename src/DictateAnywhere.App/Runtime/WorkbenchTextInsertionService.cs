using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Runtime;

/// <summary>Uses dispatcher-owned editing for our composer; external targets keep Windows insertion.</summary>
internal sealed class WorkbenchTextInsertionService(ITextInsertionService external,
  Func<WorkbenchDictationTarget?> capture) : ITextInsertionService, ITextInsertionTargetSession
{
  private WorkbenchDictationTarget? target;

  internal static WorkbenchDictationTarget? CaptureActiveComposer()
  {
    Application? app = Application.Current;
    if (app is null || app.Dispatcher.HasShutdownStarted) return null;
    return app.Dispatcher.Invoke(() => app.Windows.OfType<TextboxWorkbenchWindow>()
      .FirstOrDefault(window => window.HasForegroundOwnership)?.CaptureComposerDictationTarget(requireKeyboardFocus: true));
  }

  public void CaptureCurrentTarget()
  {
    target = capture();
    if (target is null) (external as ITextInsertionTargetSession)?.CaptureCurrentTarget();
    else (external as ITextInsertionTargetSession)?.ClearCapturedTarget();
  }

  public void ClearCapturedTarget()
  {
    target = null;
    (external as ITextInsertionTargetSession)?.ClearCapturedTarget();
  }

  public Task<InsertionResult> InsertAsync(string text, InsertionMethod preferredMethod,
    bool restoreClipboard, CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    WorkbenchDictationTarget? captured = target;
    if (captured is null) return external.InsertAsync(text, preferredMethod, restoreClipboard, cancellationToken);
    if (captured.Dispatcher.HasShutdownStarted) return Task.FromResult(Changed(preferredMethod));
    return captured.Dispatcher.InvokeAsync(() =>
    {
      cancellationToken.ThrowIfCancellationRequested();
      return captured.TryInsert(text) ? InsertionResult.Verified(preferredMethod) : Changed(preferredMethod);
    }).Task;
  }

  private static InsertionResult Changed(InsertionMethod method) => InsertionResult.Blocked(method,
    "The composer changed or lost focus. Dictation was not inserted.", InsertionBlockReason.TargetChanged);
}

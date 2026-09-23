using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Insertion;

namespace DictateAnywhere.App.Runtime;

public sealed class GlobalToggleHotkeyService : IHotkeyService
{
  private readonly IHotkeyService inner;
  private readonly IDiagnostics? diagnostics;
  private readonly IWindowFocusProvider? windowFocusProvider;
  private readonly GlobalHotkeyRegistrationCoordinator registrationCoordinator;
  private bool disposed;

  public GlobalToggleHotkeyService(
    IHotkeyService inner,
    IWindowFocusProvider? windowFocusProvider = null,
    IDiagnostics? diagnostics = null)
  {
    this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
    this.windowFocusProvider = windowFocusProvider;
    this.diagnostics = diagnostics;
    registrationCoordinator = new GlobalHotkeyRegistrationCoordinator(this.inner.RegisterAsync);
    this.inner.HotkeyPressed += ForwardHotkeyPressed;
    this.inner.HotkeyReleased += ForwardHotkeyReleased;
  }

  public event EventHandler<HotkeyEventArgs>? HotkeyPressed;

  public event EventHandler<HotkeyEventArgs>? HotkeyReleased;

  public GlobalHotkeyRegistrationOutcome? LastRegistrationOutcome { get; private set; }

  public async Task<HotkeyRegistrationResult> RegisterAsync(HotkeyBinding binding, CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);

    GlobalHotkeyRegistrationOutcome outcome = await registrationCoordinator
      .RegisterWithFallbackAsync(binding, cancellationToken)
      .ConfigureAwait(false);

    LastRegistrationOutcome = outcome;
    if (diagnostics is not null)
    {
      GlobalHotkeyTriggerDiagnostics.LogRegistrationOutcome(outcome, diagnostics);
    }

    return outcome.Success
      ? new HotkeyRegistrationResult(true, null)
      : new HotkeyRegistrationResult(false, outcome.StatusMessage);
  }

  public async Task UnregisterAsync(CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);

    LastRegistrationOutcome = null;
    await inner.UnregisterAsync(cancellationToken).ConfigureAwait(false);
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    inner.HotkeyPressed -= ForwardHotkeyPressed;
    inner.HotkeyReleased -= ForwardHotkeyReleased;
    await inner.DisposeAsync().ConfigureAwait(false);
  }

  private void ForwardHotkeyPressed(object? sender, HotkeyEventArgs e)
  {
    if (diagnostics is not null
        && windowFocusProvider is not null
        && LastRegistrationOutcome is { Success: true } outcome)
    {
      WindowFocusContext focusContext = windowFocusProvider.GetWindowFocusContext();
      GlobalHotkeyTriggerDiagnostic triggerDiagnostic = GlobalHotkeyTriggerDiagnostics
        .AnalyzeTrigger(outcome.ActiveBinding, focusContext);
      diagnostics.Info(triggerDiagnostic.FiredMessage);
      if (!string.IsNullOrWhiteSpace(triggerDiagnostic.SecondaryMessage))
      {
        diagnostics.Warning(triggerDiagnostic.SecondaryMessage);
      }
    }

    HotkeyPressed?.Invoke(this, e);
  }

  private void ForwardHotkeyReleased(object? sender, HotkeyEventArgs e)
  {
    HotkeyReleased?.Invoke(this, e);
  }
}

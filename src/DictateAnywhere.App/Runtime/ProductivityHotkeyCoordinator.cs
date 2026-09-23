using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Hotkeys;

namespace DictateAnywhere.App.Runtime;

internal sealed class ProductivityHotkeyCoordinator : IAsyncDisposable
{
  private readonly IDiagnostics diagnostics;
  private readonly Func<int, IHotkeyService> createHotkeyService;
  private readonly List<Registration> registrations = new();

  private bool disposed;

  internal ProductivityHotkeyCoordinator(
    IDiagnostics diagnostics,
    Func<int, IHotkeyService>? createHotkeyService = null)
  {
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    this.createHotkeyService = createHotkeyService ?? (hotkeyId => new WindowsHotkeyService(hotkeyId));
  }

  public async Task RestartAsync(
    AppSettings settings,
    Func<Task> retryLastDictationAction,
    CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    await StopAsync(cancellationToken).ConfigureAwait(false);

    List<(string Name, HotkeyBinding? Binding, Func<Task> Action)> requested =
    [
      ("retry last dictation", settings.RetryLastDictationHotkey, retryLastDictationAction),
    ];

    HashSet<HotkeyBinding> reserved = new()
    {
      settings.Hotkey,
      settings.UndoHotkey,
    };
    int hotkeyId = WindowsHotkeyService.DefaultHotkeyId + 10;
    foreach ((string name, HotkeyBinding? binding, Func<Task> action) in requested)
    {
      if (binding is null || binding.VirtualKey == 0)
      {
        continue;
      }

      if (!reserved.Add(binding))
      {
        diagnostics.Warning($"Productivity hotkey for {name} skipped because it duplicates another configured hotkey.");
        continue;
      }

      IHotkeyService service = createHotkeyService(hotkeyId++);
      Registration registration = new(name, binding, service, action, diagnostics);
      HotkeyRegistrationResult result = await service.RegisterAsync(binding, cancellationToken).ConfigureAwait(false);
      if (!result.Success)
      {
        diagnostics.Warning(
          result.ErrorMessage
          ?? $"Productivity hotkey for {name} could not register: {HotkeyFormatter.ToDisplayString(binding)}.");
        await service.DisposeAsync().ConfigureAwait(false);
        continue;
      }

      service.HotkeyPressed += registration.OnHotkeyPressed;
      registrations.Add(registration);
      diagnostics.Info($"Productivity hotkey registered for {name}: {HotkeyFormatter.ToDisplayString(binding)}.");
    }
  }

  public async Task StopAsync(CancellationToken cancellationToken = default)
  {
    foreach (Registration registration in registrations)
    {
      await registration.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    registrations.Clear();
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    await StopAsync(CancellationToken.None).ConfigureAwait(false);
  }

  private sealed class Registration
  {
    private readonly Func<Task> action;
    private readonly IDiagnostics diagnostics;
    private readonly object operationSync = new();
    private Task activeAction = Task.CompletedTask;
    private bool stopped;

    public Registration(
      string name,
      HotkeyBinding binding,
      IHotkeyService service,
      Func<Task> action,
      IDiagnostics diagnostics)
    {
      Name = name;
      Binding = binding;
      Service = service;
      this.action = action;
      this.diagnostics = diagnostics;
    }

    public string Name { get; }

    public HotkeyBinding Binding { get; }

    public IHotkeyService Service { get; }

    public void OnHotkeyPressed(object? sender, HotkeyEventArgs e)
    {
      lock (operationSync)
      {
        if (stopped || !activeAction.IsCompleted)
        {
          return;
        }

        activeAction = RunAsync();
      }
    }

    [SuppressMessage(
      "Design",
      "CA1031:Do not catch general exception types",
      Justification = "Registration shutdown must attempt every owned cleanup step and then rethrow the first failure.")]
    public async Task StopAsync(CancellationToken cancellationToken)
    {
      Task actionToAwait;
      lock (operationSync)
      {
        stopped = true;
        actionToAwait = activeAction;
      }

      Service.HotkeyPressed -= OnHotkeyPressed;
      Exception? firstFailure = null;
      try
      {
        await Service.UnregisterAsync(cancellationToken).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        firstFailure = ex;
      }

      await actionToAwait.WaitAsync(cancellationToken).ConfigureAwait(false);

      try
      {
        await Service.DisposeAsync().ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        firstFailure ??= ex;
      }

      if (firstFailure is not null)
      {
        ExceptionDispatchInfo.Capture(firstFailure).Throw();
      }
    }

    [SuppressMessage(
      "Design",
      "CA1031:Do not catch general exception types",
      Justification = "Hotkey event boundary must not crash the app.")]
    private async Task RunAsync()
    {
      try
      {
        await action().ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        diagnostics.Warning($"Productivity hotkey for {Name} failed: {ex.Message}");
      }
    }
  }
}

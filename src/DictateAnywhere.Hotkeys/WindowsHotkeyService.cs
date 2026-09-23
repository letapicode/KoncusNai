using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Platform.Windows.Interop;

namespace DictateAnywhere.Hotkeys;

public sealed class WindowsHotkeyService : IHotkeyService
{
  public const int DefaultHotkeyId = 0x4021;
  private static readonly TimeSpan DefaultReleasePollInterval = TimeSpan.FromMilliseconds(15);
  private static readonly TimeSpan DefaultThreadStopTimeout = TimeSpan.FromSeconds(5);

  private readonly object sync = new();
  private readonly IUser32HotkeyApi hotkeyApi;
  private readonly int hotkeyId;
  private readonly TimeSpan releasePollInterval;
  private readonly TimeSpan threadStopTimeout;

  private Thread? messageThread;
  private uint messageThreadId;
  private CancellationTokenSource? releaseMonitorCts;
  private Task releaseMonitorTask = Task.CompletedTask;
  private bool isDisposed;

  public WindowsHotkeyService()
    : this(new User32HotkeyApi(), DefaultHotkeyId, DefaultReleasePollInterval)
  {
  }

  public WindowsHotkeyService(int hotkeyId)
    : this(new User32HotkeyApi(), hotkeyId, DefaultReleasePollInterval)
  {
  }

  public WindowsHotkeyService(IUser32HotkeyApi hotkeyApi, int hotkeyId, TimeSpan releasePollInterval)
    : this(hotkeyApi, hotkeyId, releasePollInterval, DefaultThreadStopTimeout)
  {
  }

  public WindowsHotkeyService(
    IUser32HotkeyApi hotkeyApi,
    int hotkeyId,
    TimeSpan releasePollInterval,
    TimeSpan threadStopTimeout)
  {
    this.hotkeyApi = hotkeyApi ?? throw new ArgumentNullException(nameof(hotkeyApi));
    if (releasePollInterval <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(releasePollInterval));
    }

    if (threadStopTimeout <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(threadStopTimeout));
    }

    this.hotkeyId = hotkeyId;
    this.releasePollInterval = releasePollInterval;
    this.threadStopTimeout = threadStopTimeout;
  }

  public event EventHandler<HotkeyEventArgs>? HotkeyPressed;
  public event EventHandler<HotkeyEventArgs>? HotkeyReleased;

  public async Task<HotkeyRegistrationResult> RegisterAsync(HotkeyBinding binding, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    HotkeyRegistrationResult validation = ValidateBinding(binding);
    if (!validation.Success)
    {
      return validation;
    }

    await UnregisterAsync(cancellationToken).ConfigureAwait(false);

    TaskCompletionSource<HotkeyRegistrationResult> registrationCompletion =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    Thread thread = new(() => MessageLoop(binding, registrationCompletion))
    {
      IsBackground = true,
      Name = "DictateAnywhere.Hotkey.MessageLoop",
    };

    lock (sync)
    {
      messageThread = thread;
      messageThreadId = 0;
    }

    thread.Start();

    HotkeyRegistrationResult result;
    try
    {
      result = await registrationCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      await UnregisterAsync(CancellationToken.None).ConfigureAwait(false);
      throw;
    }

    if (!result.Success)
    {
      await UnregisterAsync(CancellationToken.None).ConfigureAwait(false);
    }

    return result;
  }

  public async Task UnregisterAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    Thread? thread;
    uint threadId;

    lock (sync)
    {
      thread = messageThread;
      threadId = messageThreadId;
    }

    Task initialReleaseMonitor = RequestReleaseMonitorCancellation();

    if (thread is null)
    {
      await initialReleaseMonitor.WaitAsync(cancellationToken).ConfigureAwait(false);
      return;
    }

    if (threadId != 0)
    {
      hotkeyApi.PostThreadMessage(threadId, Win32Constants.WmQuit, UIntPtr.Zero, IntPtr.Zero);
    }

    if (thread.IsAlive && !thread.Join(threadStopTimeout))
    {
      throw new InvalidOperationException("Failed to stop hotkey message loop thread.");
    }

    lock (sync)
    {
      if (ReferenceEquals(messageThread, thread))
      {
        messageThread = null;
        messageThreadId = 0;
      }
    }

    Task finalReleaseMonitor = RequestReleaseMonitorCancellation();
    await Task.WhenAll(initialReleaseMonitor, finalReleaseMonitor)
      .WaitAsync(cancellationToken)
      .ConfigureAwait(false);
  }

  public async ValueTask DisposeAsync()
  {
    if (isDisposed)
    {
      return;
    }

    try
    {
      await UnregisterAsync(CancellationToken.None).ConfigureAwait(false);
    }
    finally
    {
      isDisposed = true;
      await RequestReleaseMonitorCancellation().ConfigureAwait(false);
    }
  }

  private void MessageLoop(HotkeyBinding binding, TaskCompletionSource<HotkeyRegistrationResult> registrationCompletion)
  {
    lock (sync)
    {
      messageThreadId = hotkeyApi.GetCurrentThreadId();
    }

    uint nativeModifiers = ConvertModifiers(binding.Modifiers);
    bool registerSuccess = hotkeyApi.RegisterHotKey(hotkeyId, nativeModifiers, checked((uint)binding.VirtualKey));
    if (!registerSuccess)
    {
      int errorCode = hotkeyApi.GetLastError();
      registrationCompletion.TrySetResult(new HotkeyRegistrationResult(false, TranslateRegistrationFailure(errorCode, binding)));
      return;
    }

    registrationCompletion.TrySetResult(new HotkeyRegistrationResult(true, null));

    try
    {
      while (true)
      {
        int getMessageResult = hotkeyApi.GetMessage(out WindowsMessage message);
        if (getMessageResult <= 0)
        {
          return;
        }

        if (message.Message == Win32Constants.WmHotkey && ToInt32(message.WParam) == hotkeyId)
        {
          OnHotkeyPressed();
          StartReleaseMonitor(binding.VirtualKey);
        }
      }
    }
    finally
    {
      hotkeyApi.UnregisterHotKey(hotkeyId);
    }
  }

  private void OnHotkeyPressed()
  {
    HotkeyPressed?.Invoke(this, new HotkeyEventArgs(DateTimeOffset.UtcNow));
  }

  private void OnHotkeyReleased()
  {
    HotkeyReleased?.Invoke(this, new HotkeyEventArgs(DateTimeOffset.UtcNow));
  }

  private void StartReleaseMonitor(int virtualKey)
  {
    CancellationTokenSource? previousCts;
    Task previousTask;
    CancellationTokenSource cts = new();
    lock (sync)
    {
      previousCts = releaseMonitorCts;
      previousTask = releaseMonitorTask;
      releaseMonitorCts = cts;
      releaseMonitorTask = Task.Run(
        () => MonitorReleaseAsync(virtualKey, previousTask, cts),
        CancellationToken.None);
    }

    CancelIfActive(previousCts);
  }

  private async Task MonitorReleaseAsync(
    int virtualKey,
    Task previousTask,
    CancellationTokenSource cts)
  {
    try
    {
      await previousTask.ConfigureAwait(false);
      while (!cts.Token.IsCancellationRequested)
      {
        if (!IsVirtualKeyDown(virtualKey))
        {
          OnHotkeyReleased();
          return;
        }

        await Task.Delay(releasePollInterval, cts.Token).ConfigureAwait(false);
      }
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested)
    {
    }
    finally
    {
      lock (sync)
      {
        if (ReferenceEquals(releaseMonitorCts, cts))
        {
          releaseMonitorCts = null;
          releaseMonitorTask = Task.CompletedTask;
        }
      }

      cts.Dispose();
    }
  }

  private Task RequestReleaseMonitorCancellation()
  {
    CancellationTokenSource? existing;
    Task task;
    lock (sync)
    {
      existing = releaseMonitorCts;
      task = releaseMonitorTask;
      releaseMonitorCts = null;
      releaseMonitorTask = Task.CompletedTask;
    }

    CancelIfActive(existing);
    return task;
  }

  private static void CancelIfActive(CancellationTokenSource? source)
  {
    if (source is null)
    {
      return;
    }

    try
    {
      source.Cancel();
    }
    catch (ObjectDisposedException)
    {
      // The monitor completed and disposed its token between capture and cancellation.
    }
  }

  private bool IsVirtualKeyDown(int virtualKey)
  {
    short keyState = hotkeyApi.GetAsyncKeyState(virtualKey);
    return (keyState & 0x8000) != 0;
  }

  private static uint ConvertModifiers(HotkeyModifiers modifiers)
  {
    uint nativeModifiers = 0;

    if ((modifiers & HotkeyModifiers.Alt) != 0)
    {
      nativeModifiers |= Win32Constants.ModAlt;
    }

    if ((modifiers & HotkeyModifiers.Control) != 0)
    {
      nativeModifiers |= Win32Constants.ModControl;
    }

    if ((modifiers & HotkeyModifiers.Shift) != 0)
    {
      nativeModifiers |= Win32Constants.ModShift;
    }

    if ((modifiers & HotkeyModifiers.Windows) != 0)
    {
      nativeModifiers |= Win32Constants.ModWindows;
    }

    return nativeModifiers;
  }

  private static HotkeyRegistrationResult ValidateBinding(HotkeyBinding binding)
  {
    if (binding.Modifiers == HotkeyModifiers.None)
    {
      return new HotkeyRegistrationResult(false, "Hotkey must include at least one modifier key.");
    }

    if (binding.VirtualKey is <= 0 or > 0xFE)
    {
      return new HotkeyRegistrationResult(false, "Hotkey key must be a valid Windows virtual-key code.");
    }

    return new HotkeyRegistrationResult(true, null);
  }

  private static string TranslateRegistrationFailure(int errorCode, HotkeyBinding binding)
  {
    string hotkeyText = HotkeyFormatter.ToDisplayString(binding);
    if (errorCode == Win32Constants.ErrorHotkeyAlreadyRegistered)
    {
      return $"Hotkey '{hotkeyText}' is already registered by another application.";
    }

    return $"Windows could not register hotkey '{hotkeyText}' (Win32 error {errorCode}).";
  }

  private static int ToInt32(UIntPtr value)
  {
    return unchecked((int)value.ToUInt64());
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(isDisposed, this);
  }
}

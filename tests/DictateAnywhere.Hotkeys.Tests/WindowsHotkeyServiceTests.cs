using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Hotkeys;
using DictateAnywhere.Platform.Windows.Interop;

namespace DictateAnywhere.Hotkeys.Tests;

public sealed class WindowsHotkeyServiceTests
{
  [Xunit.Fact]
  public async Task RegisterAsync_ReturnsConflictError_WhenHotkeyAlreadyRegistered()
  {
    FakeUser32HotkeyApi api = new()
    {
      RegisterHotKeyResult = false,
      LastError = Win32Constants.ErrorHotkeyAlreadyRegistered,
    };

    await using WindowsHotkeyService service = new(api, WindowsHotkeyService.DefaultHotkeyId, TimeSpan.FromMilliseconds(5));

    HotkeyRegistrationResult result = await service.RegisterAsync(
      new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x20));

    Xunit.Assert.False(result.Success);
    Xunit.Assert.NotNull(result.ErrorMessage);
    Xunit.Assert.Contains("already registered", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Equal(1, api.RegisterHotKeyCallCount);
  }

  [Xunit.Fact]
  public async Task RegisterAsync_ReturnsValidationError_WhenModifiersMissing()
  {
    FakeUser32HotkeyApi api = new();
    await using WindowsHotkeyService service = new(api, WindowsHotkeyService.DefaultHotkeyId, TimeSpan.FromMilliseconds(5));

    HotkeyRegistrationResult result = await service.RegisterAsync(
      new HotkeyBinding(HotkeyModifiers.None, 0x41));

    Xunit.Assert.False(result.Success);
    Xunit.Assert.NotNull(result.ErrorMessage);
    Xunit.Assert.Contains("modifier", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Equal(0, api.RegisterHotKeyCallCount);
  }

  [Xunit.Fact]
  public async Task RegisterThenUnregister_InvokesNativeLifecycle()
  {
    FakeUser32HotkeyApi api = new();
    await using WindowsHotkeyService service = new(api, WindowsHotkeyService.DefaultHotkeyId, TimeSpan.FromMilliseconds(5));

    HotkeyRegistrationResult result = await service.RegisterAsync(
      new HotkeyBinding(HotkeyModifiers.Windows | HotkeyModifiers.Alt, 0x20));

    Xunit.Assert.True(result.Success);

    await service.UnregisterAsync();

    Xunit.Assert.Equal(1, api.RegisterHotKeyCallCount);
    Xunit.Assert.Equal(1, api.UnregisterHotKeyCallCount);
    Xunit.Assert.True(api.PostThreadMessageCallCount >= 1);
  }

  [Xunit.Fact]
  public async Task HotkeyMessage_RaisesPressedAndReleased()
  {
    FakeUser32HotkeyApi api = new();
    api.SetVirtualKeyDown(true);

    await using WindowsHotkeyService service = new(api, WindowsHotkeyService.DefaultHotkeyId, TimeSpan.FromMilliseconds(5));

    TaskCompletionSource<bool> pressed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource<bool> released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    service.HotkeyPressed += (_, _) => pressed.TrySetResult(true);
    service.HotkeyReleased += (_, _) => released.TrySetResult(true);

    HotkeyRegistrationResult result = await service.RegisterAsync(
      new HotkeyBinding(HotkeyModifiers.Control, 0x41));
    Xunit.Assert.True(result.Success);

    api.EnqueueHotkeyMessage(WindowsHotkeyService.DefaultHotkeyId);
    await pressed.Task.WaitAsync(TimeSpan.FromSeconds(2));

    api.SetVirtualKeyDown(false);
    await released.Task.WaitAsync(TimeSpan.FromSeconds(2));
  }

  [Xunit.Fact]
  public async Task UnregisterTimeout_PreservesThreadOwnershipSoCleanupCanRetry()
  {
    FakeUser32HotkeyApi api = new() { IgnoreQuitMessages = true };
    WindowsHotkeyService service = new(
      api,
      WindowsHotkeyService.DefaultHotkeyId,
      TimeSpan.FromMilliseconds(5),
      TimeSpan.FromMilliseconds(25));
    try
    {
      HotkeyRegistrationResult result = await service.RegisterAsync(
        new HotkeyBinding(HotkeyModifiers.Control, 0x41));
      Xunit.Assert.True(result.Success);

      await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => service.UnregisterAsync());

      api.IgnoreQuitMessages = false;
      await service.UnregisterAsync();

      Xunit.Assert.True(api.PostThreadMessageCallCount >= 2);
      Xunit.Assert.Equal(1, api.UnregisterHotKeyCallCount);
    }
    finally
    {
      api.IgnoreQuitMessages = false;
      await service.DisposeAsync();
    }
  }

  [Xunit.Fact]
  public async Task UnregisterAsync_WaitsForReleaseMonitorToExit()
  {
    FakeUser32HotkeyApi api = new();
    api.SetVirtualKeyDown(true);
    api.BlockKeyStateReads = true;
    await using WindowsHotkeyService service = new(
      api,
      WindowsHotkeyService.DefaultHotkeyId,
      TimeSpan.FromMilliseconds(5));

    TaskCompletionSource<bool> pressed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    service.HotkeyPressed += (_, _) => pressed.TrySetResult(true);

    HotkeyRegistrationResult result = await service.RegisterAsync(
      new HotkeyBinding(HotkeyModifiers.Control, 0x41));
    Xunit.Assert.True(result.Success);

    api.EnqueueHotkeyMessage(WindowsHotkeyService.DefaultHotkeyId);
    await pressed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Xunit.Assert.True(api.KeyStateReadStarted.Wait(TimeSpan.FromSeconds(2)));

    Task unregister = service.UnregisterAsync();
    Xunit.Assert.False(unregister.IsCompleted);

    api.AllowKeyStateReads.Set();
    await unregister.WaitAsync(TimeSpan.FromSeconds(2));
  }

  private sealed class FakeUser32HotkeyApi : IUser32HotkeyApi
  {
    private readonly ConcurrentQueue<WindowsMessage> queue = new();
    private short asyncKeyState;

    public bool RegisterHotKeyResult { get; set; } = true;
    public int LastError { get; set; }
    public int RegisterHotKeyCallCount { get; private set; }
    public int UnregisterHotKeyCallCount { get; private set; }
    public int PostThreadMessageCallCount { get; private set; }
    public bool IgnoreQuitMessages { get; set; }
    public bool BlockKeyStateReads { get; set; }
    public ManualResetEventSlim KeyStateReadStarted { get; } = new(false);
    public ManualResetEventSlim AllowKeyStateReads { get; } = new(false);

    public bool RegisterHotKey(int id, uint modifiers, uint virtualKey)
    {
      RegisterHotKeyCallCount++;
      return RegisterHotKeyResult;
    }

    public bool UnregisterHotKey(int id)
    {
      UnregisterHotKeyCallCount++;
      return true;
    }

    public int GetMessage(out WindowsMessage message)
    {
      while (true)
      {
        if (queue.TryDequeue(out message))
        {
          if (message.Message == Win32Constants.WmQuit)
          {
            if (!IgnoreQuitMessages)
            {
              return 0;
            }

            continue;
          }

          return 1;
        }

        Thread.Sleep(5);
      }
    }

    public bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam)
    {
      PostThreadMessageCallCount++;
      queue.Enqueue(new WindowsMessage
      {
        Message = message,
        WParam = wParam,
        LParam = lParam,
      });
      return true;
    }

    public short GetAsyncKeyState(int virtualKey)
    {
      KeyStateReadStarted.Set();
      if (BlockKeyStateReads)
      {
        AllowKeyStateReads.Wait();
      }

      return asyncKeyState;
    }

    public uint GetCurrentThreadId()
    {
      return 1234;
    }

    public int GetLastError()
    {
      return LastError;
    }

    public void SetVirtualKeyDown(bool isDown)
    {
      asyncKeyState = isDown ? unchecked((short)0x8000) : (short)0;
    }

    public void EnqueueHotkeyMessage(int hotkeyId)
    {
      queue.Enqueue(new WindowsMessage
      {
        Message = Win32Constants.WmHotkey,
        WParam = (UIntPtr)(uint)hotkeyId,
        LParam = IntPtr.Zero,
      });
    }
  }
}


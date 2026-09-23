using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Hotkeys;

namespace DictateAnywhere.Hotkeys.Tests;

public sealed class WindowsHotkeyRegistrationValidatorTests
{
  [Xunit.Fact]
  public async Task ValidateAsync_ReturnsSuccess_AndUnregisters()
  {
    await using FakeHotkeyService service = new(
      registerResult: new HotkeyRegistrationResult(true, null));
    WindowsHotkeyRegistrationValidator validator = new(() => service);

    HotkeyRegistrationResult result = await validator.ValidateAsync(
      new HotkeyBinding(HotkeyModifiers.Control, 0x41));

    Xunit.Assert.True(result.Success);
    Xunit.Assert.Equal(1, service.RegisterCallCount);
    Xunit.Assert.Equal(1, service.UnregisterCallCount);
    Xunit.Assert.True(service.DisposeCallCount >= 1);
  }

  [Xunit.Fact]
  public async Task ValidateAsync_ReturnsFailure_WithoutUnregister()
  {
    await using FakeHotkeyService service = new(
      registerResult: new HotkeyRegistrationResult(false, "conflict"));
    WindowsHotkeyRegistrationValidator validator = new(() => service);

    HotkeyRegistrationResult result = await validator.ValidateAsync(
      new HotkeyBinding(HotkeyModifiers.Control, 0x41));

    Xunit.Assert.False(result.Success);
    Xunit.Assert.Equal(1, service.RegisterCallCount);
    Xunit.Assert.Equal(0, service.UnregisterCallCount);
    Xunit.Assert.True(service.DisposeCallCount >= 1);
  }

  private sealed class FakeHotkeyService : IHotkeyService
  {
    private readonly HotkeyRegistrationResult registerResult;

    public FakeHotkeyService(HotkeyRegistrationResult registerResult)
    {
      this.registerResult = registerResult;
    }

    public int RegisterCallCount { get; private set; }
    public int UnregisterCallCount { get; private set; }
    public int DisposeCallCount { get; private set; }

    public event EventHandler<HotkeyEventArgs>? HotkeyPressed
    {
      add { }
      remove { }
    }

    public event EventHandler<HotkeyEventArgs>? HotkeyReleased
    {
      add { }
      remove { }
    }

    public Task<HotkeyRegistrationResult> RegisterAsync(HotkeyBinding binding, CancellationToken cancellationToken = default)
    {
      RegisterCallCount++;
      return Task.FromResult(registerResult);
    }

    public Task UnregisterAsync(CancellationToken cancellationToken = default)
    {
      UnregisterCallCount++;
      return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
      DisposeCallCount++;
      return ValueTask.CompletedTask;
    }
  }
}

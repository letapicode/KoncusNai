using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Experience;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Hotkeys;

namespace DictateAnywhere.App.Tests;

public sealed class GlobalHotkeyRegistrationCoordinatorTests
{
  [Xunit.Fact]
  public async Task RegisterWithFallbackAsync_PrimarySuccess_UsesRequestedBinding()
  {
    List<HotkeyBinding> attempted = new();
    GlobalHotkeyRegistrationCoordinator coordinator = new((binding, _) =>
    {
      attempted.Add(binding);
      return Task.FromResult(new HotkeyRegistrationResult(true, null));
    });

    HotkeyBinding requested = GlobalToggleSettingsPolicy.PreferredHotkey;
    GlobalHotkeyRegistrationOutcome outcome = await coordinator.RegisterWithFallbackAsync(requested);

    Xunit.Assert.True(outcome.Success);
    Xunit.Assert.False(outcome.UsedFallbackBinding);
    Xunit.Assert.Equal(requested, outcome.ActiveBinding);
    Xunit.Assert.Single(attempted);
  }

  [Xunit.Fact]
  public async Task RegisterWithFallbackAsync_AltSpaceConflict_FallsBackToWinAltSpace()
  {
    Queue<HotkeyRegistrationResult> results = new();
    results.Enqueue(new HotkeyRegistrationResult(false, "already registered by another application"));
    results.Enqueue(new HotkeyRegistrationResult(true, null));

    List<HotkeyBinding> attempted = new();
    GlobalHotkeyRegistrationCoordinator coordinator = new((binding, _) =>
    {
      attempted.Add(binding);
      return Task.FromResult(results.Dequeue());
    });

    GlobalHotkeyRegistrationOutcome outcome = await coordinator.RegisterWithFallbackAsync(GlobalToggleSettingsPolicy.PreferredHotkey);

    Xunit.Assert.True(outcome.Success);
    Xunit.Assert.True(outcome.UsedFallbackBinding);
    Xunit.Assert.Equal(GlobalToggleSettingsPolicy.FallbackHotkey, outcome.ActiveBinding);
    Xunit.Assert.Equal(2, attempted.Count);
    Xunit.Assert.Equal(GlobalToggleSettingsPolicy.PreferredHotkey, attempted[0]);
    Xunit.Assert.Equal(GlobalToggleSettingsPolicy.FallbackHotkey, attempted[1]);
    Xunit.Assert.Contains("Fallback active", outcome.StatusMessage);
    Xunit.Assert.DoesNotContain("reserved by system/app", outcome.DiagnosticsMessage);
  }

  [Xunit.Fact]
  public async Task RegisterWithFallbackAsync_CustomHotkeyFailure_DoesNotAutoFallback()
  {
    List<HotkeyBinding> attempted = new();
    GlobalHotkeyRegistrationCoordinator coordinator = new((binding, _) =>
    {
      attempted.Add(binding);
      return Task.FromResult(new HotkeyRegistrationResult(false, "already registered"));
    });

    HotkeyBinding customBinding = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x41);
    GlobalHotkeyRegistrationOutcome outcome = await coordinator.RegisterWithFallbackAsync(customBinding);

    Xunit.Assert.False(outcome.Success);
    Xunit.Assert.False(outcome.UsedFallbackBinding);
    Xunit.Assert.Equal(customBinding, outcome.ActiveBinding);
    Xunit.Assert.Single(attempted);
  }

  [Xunit.Fact]
  public async Task RegisterWithFallbackAsync_AltSpaceAndFallbackFail_ReturnsFailure()
  {
    Queue<HotkeyRegistrationResult> results = new();
    results.Enqueue(new HotkeyRegistrationResult(false, "already registered"));
    results.Enqueue(new HotkeyRegistrationResult(false, "windows rejected"));

    GlobalHotkeyRegistrationCoordinator coordinator = new((_, _) =>
      Task.FromResult(results.Dequeue()));

    GlobalHotkeyRegistrationOutcome outcome = await coordinator.RegisterWithFallbackAsync(GlobalToggleSettingsPolicy.PreferredHotkey, CancellationToken.None);

    Xunit.Assert.False(outcome.Success);
    Xunit.Assert.Contains("also failed", outcome.StatusMessage);
  }
}

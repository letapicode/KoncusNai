using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Experience;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Insertion;

namespace DictateAnywhere.App.Tests;

public sealed class GlobalToggleHotkeyServiceTests
{
  [Xunit.Fact]
  public async Task RegisterAsync_PrimarySuccess_UsesRequestedBinding()
  {
    await using FakeHotkeyService inner = new();
    await using GlobalToggleHotkeyService service = new(inner);

    HotkeyRegistrationResult result = await service.RegisterAsync(GlobalToggleSettingsPolicy.PreferredHotkey);

    Xunit.Assert.True(result.Success);
    Xunit.Assert.Single(inner.AttemptedBindings);
    Xunit.Assert.Equal(GlobalToggleSettingsPolicy.PreferredHotkey, inner.AttemptedBindings[0]);
    Xunit.Assert.NotNull(service.LastRegistrationOutcome);
    Xunit.Assert.False(service.LastRegistrationOutcome!.UsedFallbackBinding);
  }

  [Xunit.Fact]
  public async Task RegisterAsync_AltSpaceConflict_FallsBackToWinAltSpace()
  {
    await using FakeHotkeyService inner = new();
    inner.EnqueueResult(new HotkeyRegistrationResult(false, "already registered by another application"));
    inner.EnqueueResult(new HotkeyRegistrationResult(true, null));

    await using GlobalToggleHotkeyService service = new(inner);

    HotkeyRegistrationResult result = await service.RegisterAsync(GlobalToggleSettingsPolicy.PreferredHotkey);

    Xunit.Assert.True(result.Success);
    Xunit.Assert.Equal(2, inner.AttemptedBindings.Count);
    Xunit.Assert.Equal(GlobalToggleSettingsPolicy.PreferredHotkey, inner.AttemptedBindings[0]);
    Xunit.Assert.Equal(GlobalToggleSettingsPolicy.FallbackHotkey, inner.AttemptedBindings[1]);
    Xunit.Assert.NotNull(service.LastRegistrationOutcome);
    Xunit.Assert.True(service.LastRegistrationOutcome!.UsedFallbackBinding);
    Xunit.Assert.Equal(GlobalToggleSettingsPolicy.FallbackHotkey, service.LastRegistrationOutcome.ActiveBinding);
  }

  [Xunit.Fact]
  public async Task RegisterAsync_CustomHotkeyFailure_DoesNotFallback()
  {
    await using FakeHotkeyService inner = new();
    inner.EnqueueResult(new HotkeyRegistrationResult(false, "already registered"));

    HotkeyBinding customBinding = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x41);
    await using GlobalToggleHotkeyService service = new(inner);

    HotkeyRegistrationResult result = await service.RegisterAsync(customBinding);

    Xunit.Assert.False(result.Success);
    Xunit.Assert.Single(inner.AttemptedBindings);
    Xunit.Assert.Equal(customBinding, inner.AttemptedBindings[0]);
    Xunit.Assert.NotNull(service.LastRegistrationOutcome);
    Xunit.Assert.False(service.LastRegistrationOutcome!.UsedFallbackBinding);
  }

  [Xunit.Fact]
  public async Task UnregisterAsync_ClearsLastRegistrationOutcome()
  {
    await using FakeHotkeyService inner = new();
    await using GlobalToggleHotkeyService service = new(inner);

    await service.RegisterAsync(GlobalToggleSettingsPolicy.PreferredHotkey);
    await service.UnregisterAsync();

    Xunit.Assert.Null(service.LastRegistrationOutcome);
    Xunit.Assert.Equal(1, inner.UnregisterCallCount);
  }

  [Xunit.Fact]
  public async Task RegisterAsync_PrimarySuccess_LogsHotkeyRegistered()
  {
    await using FakeHotkeyService inner = new();
    FakeDiagnostics diagnostics = new();
    await using GlobalToggleHotkeyService service = new(
      inner,
      windowFocusProvider: new FakeWindowFocusProvider(),
      diagnostics: diagnostics);

    HotkeyRegistrationResult result = await service.RegisterAsync(GlobalToggleSettingsPolicy.PreferredHotkey);

    Xunit.Assert.True(result.Success);
    Xunit.Assert.Contains(
      diagnostics.InfoMessages,
      message => message.Contains("Global hotkey registered", StringComparison.Ordinal));
  }

  [Xunit.Fact]
  public async Task HotkeyPressed_OnEditableTarget_LogsHotkeyFiredWithoutLeakWarning()
  {
    await using FakeHotkeyService inner = new();
    FakeDiagnostics diagnostics = new();
    FakeWindowFocusProvider focusProvider = new()
    {
      FocusContext = new WindowFocusContext
      {
        ForegroundWindowHandle = 10,
        ForegroundProcessId = 20,
        ForegroundProcessName = "chrome",
        ForegroundWindowClassName = "Chrome_WidgetWin_1",
        FocusedControlClassName = "Chrome_RenderWidgetHostHWND",
        FocusedAutomationId = "prompt-textarea",
        FocusedAutomationName = "Message ChatGPT",
        FocusedAutomationClassName = "Chrome_RenderWidgetHostHWND",
        FocusedAutomationControlType = "ControlType.Document",
        BrowserSurfaceKind = BrowserSurfaceKind.EditablePage,
        Editability = WindowEditability.Editable,
        EditabilityReason = "browser-editable-surface",
        VerificationMode = "TextPattern",
      },
    };
    await using GlobalToggleHotkeyService service = new(
      inner,
      windowFocusProvider: focusProvider,
      diagnostics: diagnostics);

    await service.RegisterAsync(GlobalToggleSettingsPolicy.PreferredHotkey);
    inner.RaisePressed();

    Xunit.Assert.Contains(
      diagnostics.InfoMessages,
      message => message.Contains("Global hotkey fired", StringComparison.Ordinal)
                 && message.Contains("prompt-textarea", StringComparison.Ordinal));
    Xunit.Assert.DoesNotContain(
      diagnostics.WarningMessages,
      message => message.Contains("Trigger keys leaked to app before recording start", StringComparison.Ordinal)
                 || message.Contains("Focus changed before recording start", StringComparison.Ordinal));
  }

  [Xunit.Fact]
  public async Task HotkeyPressed_OnBrowserMenuTarget_LogsTriggerLeakWarning()
  {
    await using FakeHotkeyService inner = new();
    FakeDiagnostics diagnostics = new();
    FakeWindowFocusProvider focusProvider = new()
    {
      FocusContext = new WindowFocusContext
      {
        ForegroundWindowHandle = 10,
        ForegroundProcessId = 20,
        ForegroundProcessName = "chrome",
        ForegroundWindowClassName = "Chrome_WidgetWin_1",
        FocusedControlClassName = "BrowserAppMenuButton",
        FocusedAutomationId = "browser-menu",
        FocusedAutomationName = "App menu",
        FocusedAutomationClassName = "BrowserAppMenuButton",
        FocusedAutomationControlType = "ControlType.Button",
        RawFocusedAutomationId = "browser-menu",
        RawFocusedAutomationName = "App menu",
        RawFocusedAutomationClassName = "BrowserAppMenuButton",
        RawFocusedAutomationControlType = "ControlType.Button",
        BrowserSurfaceKind = BrowserSurfaceKind.NonEditableSurface,
        Editability = WindowEditability.NonEditable,
        EditabilityReason = "browser-non-editable-surface",
      },
    };
    await using GlobalToggleHotkeyService service = new(
      inner,
      windowFocusProvider: focusProvider,
      diagnostics: diagnostics);

    await service.RegisterAsync(GlobalToggleSettingsPolicy.PreferredHotkey);
    inner.RaisePressed();

    Xunit.Assert.Contains(
      diagnostics.WarningMessages,
      message => message.Contains("Trigger keys leaked to app before recording start", StringComparison.Ordinal)
                 && message.Contains("browser-chrome-menu-surface", StringComparison.Ordinal));
  }

  [Xunit.Fact]
  public async Task HotkeyPressed_EmitsLeakClassification_BeforeForwardingPressedEvent()
  {
    await using FakeHotkeyService inner = new();
    FakeDiagnostics diagnostics = new();
    FakeWindowFocusProvider focusProvider = new()
    {
      FocusContext = new WindowFocusContext
      {
        ForegroundWindowHandle = 10,
        ForegroundProcessId = 20,
        ForegroundProcessName = "chrome",
        ForegroundWindowClassName = "Chrome_WidgetWin_1",
        FocusedControlClassName = "BrowserAppMenuButton",
        FocusedAutomationId = "browser-menu",
        FocusedAutomationName = "App menu",
        FocusedAutomationClassName = "BrowserAppMenuButton",
        FocusedAutomationControlType = "ControlType.Button",
        BrowserSurfaceKind = BrowserSurfaceKind.NonEditableSurface,
        Editability = WindowEditability.NonEditable,
        EditabilityReason = "browser-non-editable-surface",
      },
    };
    await using GlobalToggleHotkeyService service = new(
      inner,
      windowFocusProvider: focusProvider,
      diagnostics: diagnostics);

    string? warningAtForwardTime = null;
    int forwardedPressCount = 0;
    service.HotkeyPressed += (_, _) =>
    {
      forwardedPressCount++;
      warningAtForwardTime = diagnostics.WarningMessages.Count > 0
        ? diagnostics.WarningMessages[^1]
        : null;
    };

    await service.RegisterAsync(GlobalToggleSettingsPolicy.PreferredHotkey);
    inner.RaisePressed();

    Xunit.Assert.Equal(1, forwardedPressCount);
    Xunit.Assert.NotNull(warningAtForwardTime);
    Xunit.Assert.Contains("Trigger keys leaked to app before recording start", warningAtForwardTime, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public async Task HotkeyPressed_WhenRawEditableFocusPromotesToUnknownBrowserSurface_LogsFocusDriftWarning()
  {
    await using FakeHotkeyService inner = new();
    FakeDiagnostics diagnostics = new();
    FakeWindowFocusProvider focusProvider = new()
    {
      FocusContext = new WindowFocusContext
      {
        ForegroundWindowHandle = 10,
        ForegroundProcessId = 20,
        ForegroundProcessName = "msedge",
        ForegroundWindowClassName = "Chrome_WidgetWin_1",
        FocusedControlClassName = "Chrome_RenderWidgetHostHWND",
        FocusedAutomationId = "browser-chrome",
        FocusedAutomationName = "Edge surface",
        FocusedAutomationClassName = "Chrome_RenderWidgetHostHWND",
        FocusedAutomationControlType = "ControlType.Pane",
        RawFocusedAutomationId = "searchbox",
        RawFocusedAutomationName = "Google Search",
        RawFocusedAutomationClassName = "Chrome_RenderWidgetHostHWND",
        RawFocusedAutomationControlType = "ControlType.Edit",
        BrowserSurfaceKind = BrowserSurfaceKind.UnknownBrowserSurface,
        Editability = WindowEditability.Unknown,
        EditabilityReason = "browser-focus-unclassified",
      },
    };
    await using GlobalToggleHotkeyService service = new(
      inner,
      windowFocusProvider: focusProvider,
      diagnostics: diagnostics);

    await service.RegisterAsync(GlobalToggleSettingsPolicy.PreferredHotkey);
    inner.RaisePressed();

    Xunit.Assert.Contains(
      diagnostics.WarningMessages,
      message => message.Contains("Focus changed before recording start", StringComparison.Ordinal)
                 && message.Contains("editable-raw-focus-promoted-to-noneditable-surface", StringComparison.Ordinal));
  }

  private sealed class FakeHotkeyService : IHotkeyService
  {
    private readonly Queue<HotkeyRegistrationResult> results = new();

    public event EventHandler<HotkeyEventArgs>? HotkeyPressed;

    public event EventHandler<HotkeyEventArgs>? HotkeyReleased;

    public List<HotkeyBinding> AttemptedBindings { get; } = new();

    public int UnregisterCallCount { get; private set; }

    public void EnqueueResult(HotkeyRegistrationResult result)
    {
      results.Enqueue(result);
    }

    public Task<HotkeyRegistrationResult> RegisterAsync(HotkeyBinding binding, CancellationToken cancellationToken = default)
    {
      AttemptedBindings.Add(binding);
      HotkeyRegistrationResult result = results.Count > 0
        ? results.Dequeue()
        : new HotkeyRegistrationResult(true, null);
      return Task.FromResult(result);
    }

    public Task UnregisterAsync(CancellationToken cancellationToken = default)
    {
      UnregisterCallCount++;
      return Task.CompletedTask;
    }

    public void RaisePressed()
    {
      HotkeyPressed?.Invoke(this, new HotkeyEventArgs(DateTimeOffset.UtcNow));
    }

    public void RaiseReleased()
    {
      HotkeyReleased?.Invoke(this, new HotkeyEventArgs(DateTimeOffset.UtcNow));
    }

    public ValueTask DisposeAsync()
    {
      return ValueTask.CompletedTask;
    }
  }

  private sealed class FakeWindowFocusProvider : IWindowFocusProvider
  {
    public WindowFocusContext FocusContext { get; set; } = WindowFocusContext.Empty;

    public nint GetForegroundWindowHandle()
    {
      return FocusContext.ForegroundWindowHandle;
    }

    public WindowFocusContext GetWindowFocusContext()
    {
      return FocusContext;
    }
  }

  private sealed class FakeDiagnostics : IDiagnostics
  {
    public List<string> InfoMessages { get; } = new();

    public List<string> WarningMessages { get; } = new();

    public List<string> ErrorMessages { get; } = new();

    public void Info(string message)
    {
      InfoMessages.Add(message);
    }

    public void Warning(string message)
    {
      WarningMessages.Add(message);
    }

    public void Error(string message, Exception? exception = null)
    {
      ErrorMessages.Add(message);
    }
  }
}

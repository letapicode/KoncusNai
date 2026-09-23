using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Audio;
using DictateAnywhere.Audio.WASAPI;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Hotkeys;
using DictateAnywhere.Inference;
using DictateAnywhere.Insertion;

internal static class Program
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false,
  };

  public static async Task<int> Main(string[] args)
  {
    if (args.Length == 0)
    {
      PrintUsage();
      return 1;
    }

    string command = args[0].Trim().ToLowerInvariant();
    string[] commandArgs = args.Length > 1 ? args[1..] : Array.Empty<string>();

    TimeSpan operationTimeout = ResolveOperationTimeout(command, commandArgs);

    SpikeExecutionResult result;
    try
    {
      Task<SpikeExecutionResult> execution = command switch
      {
        "hotkey" => RunHotkeySpikeAsync(commandArgs),
        "audio" => RunAudioSpikeAsync(commandArgs),
        "insertion" => RunInsertionSpikeAsync(commandArgs),
        _ => Task.FromResult(new SpikeExecutionResult(
          command,
          false,
          "Unknown command.",
          null,
          null,
          "Use one of: hotkey, audio, insertion.")),
      };

      result = await execution.WaitAsync(operationTimeout).ConfigureAwait(false);
    }
    catch (TimeoutException)
    {
      result = new SpikeExecutionResult(
        command,
        false,
        "Spike command timed out.",
        operationTimeout,
        null,
        $"Timed out after {operationTimeout.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)} seconds.");
    }
#pragma warning disable CA1031 // Spike runner must return structured failure payloads.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      result = new SpikeExecutionResult(
        command,
        false,
        "Unhandled spike failure.",
        null,
        null,
        ex.Message);
    }

    Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
    int exitCode = result.Success ? 0 : 1;
    Environment.Exit(exitCode);
    return exitCode;
  }

  private static async Task<SpikeExecutionResult> RunHotkeySpikeAsync(string[] args)
  {
    HotkeyModifiers modifiers = ParseModifiers(GetOption(args, "--modifiers") ?? "Windows,Alt");
    int virtualKey = GetIntOption(args, "--virtual-key", 0x20);
    int timeoutSeconds = GetIntOption(args, "--timeout-seconds", 20);
    bool selfTrigger = GetBoolOption(args, "--self-trigger", true);

    HotkeyBinding binding = new(modifiers, virtualKey);
    Stopwatch stopwatch = Stopwatch.StartNew();

    await using WindowsHotkeyService service = new();
    HotkeyRegistrationResult registration = await service.RegisterAsync(binding).ConfigureAwait(false);
    if (!registration.Success)
    {
      return new SpikeExecutionResult(
        "hotkey",
        false,
        "Hotkey registration failed.",
        null,
        null,
        registration.ErrorMessage);
    }

    TaskCompletionSource<DateTimeOffset> pressed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource<DateTimeOffset> released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    service.HotkeyPressed += (_, eventArgs) => pressed.TrySetResult(eventArgs.ObservedAtUtc);
    service.HotkeyReleased += (_, eventArgs) => released.TrySetResult(eventArgs.ObservedAtUtc);

    string triggerDetails = "manual trigger";
    if (selfTrigger)
    {
      await Task.Delay(TimeSpan.FromMilliseconds(150)).ConfigureAwait(false);
      InputDispatchResult dispatchResult = TriggerHotkey(binding);
      if (!dispatchResult.Success)
      {
        return new SpikeExecutionResult(
          "hotkey",
          false,
          "Failed to self-trigger hotkey after successful registration.",
          stopwatch.Elapsed,
          null,
          dispatchResult.ErrorMessage);
      }

      triggerDetails = "self-triggered via SendInput";
    }

    try
    {
      TimeSpan initialWait = TimeSpan.FromSeconds(Math.Max(3, Math.Min(timeoutSeconds, 8)));
      (bool success, DateTimeOffset pressedAt, DateTimeOffset releasedAt) = await TryAwaitHotkeyPairAsync(
        pressed.Task,
        released.Task,
        initialWait).ConfigureAwait(false);

      if (!success && selfTrigger)
      {
        if (!TryPostSyntheticHotkeyMessage(service, WindowsHotkeyService.DefaultHotkeyId, out string postError))
        {
          return new SpikeExecutionResult(
            "hotkey",
            false,
            "Timed out waiting for hotkey press/release.",
            stopwatch.Elapsed,
            null,
            $"SendInput did not produce WM_HOTKEY and fallback post failed: {postError}");
        }

        triggerDetails = "synthetic WM_HOTKEY post";
        TimeSpan remainingWait = TimeSpan.FromSeconds(Math.Max(2, timeoutSeconds));
        (success, pressedAt, releasedAt) = await TryAwaitHotkeyPairAsync(
          pressed.Task,
          released.Task,
          remainingWait).ConfigureAwait(false);
      }

      if (!success)
      {
        return new SpikeExecutionResult(
          "hotkey",
          false,
          "Timed out waiting for hotkey press/release.",
          stopwatch.Elapsed,
          null,
          selfTrigger
            ? "Self-trigger did not produce WM_HOTKEY in the expected window."
            : "Press the configured hotkey while the spike is running.");
      }

      stopwatch.Stop();
      TimeSpan holdDuration = releasedAt - pressedAt;
      return new SpikeExecutionResult(
        "hotkey",
        true,
        $"Observed WM_HOTKEY press/release for {HotkeyFormatter.ToDisplayString(binding)}.",
        stopwatch.Elapsed,
        null,
        $"{triggerDetails}; hold duration: {holdDuration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)} ms");
    }
    finally
    {
      await service.UnregisterAsync().ConfigureAwait(false);
    }
  }

  private static async Task<SpikeExecutionResult> RunAudioSpikeAsync(string[] args)
  {
    int durationSeconds = GetIntOption(args, "--duration-seconds", 5);
    string outputPath = GetOption(args, "--output")
      ?? Path.Combine("artifacts", "spikes", "audio-capture.wav");

    string resolvedOutputPath = Path.GetFullPath(outputPath);
    string outputDirectory = Path.GetDirectoryName(resolvedOutputPath)
      ?? throw new InvalidOperationException("Output path must include a directory.");

    Directory.CreateDirectory(outputDirectory);

    await using WasapiAudioInputSource inputSource = new();
    IReadOnlyList<AudioInputDevice> devices = inputSource.GetInputDevices();
    if (devices.Count == 0)
    {
      return new SpikeExecutionResult(
        "audio",
        false,
        "No active microphone capture devices were found.",
        null,
        null,
        "Enable a Windows capture device and retry.");
    }

    string? preferredDeviceId = devices.FirstOrDefault(d => d.IsDefault)?.DeviceId
      ?? inputSource.GetDefaultInputDeviceId()
      ?? devices[0].DeviceId;

    AudioCaptureOptions options = AudioCaptureOptions.Default with
    {
      PreferredInputDeviceId = preferredDeviceId,
      EnableSilenceTrimHook = false,
    };

    await using WasapiAudioCaptureService captureService = new(inputSource, options);

    Stopwatch stopwatch = Stopwatch.StartNew();
    AudioCaptureResult result;
    try
    {
      await captureService.StartAsync().ConfigureAwait(false);
      await Task.Delay(TimeSpan.FromSeconds(durationSeconds)).ConfigureAwait(false);
      result = await captureService.StopAsync()
        .WaitAsync(TimeSpan.FromSeconds(12))
        .ConfigureAwait(false);
      stopwatch.Stop();
    }
    catch (Exception ex) when (ex is AudioCaptureException or InvalidOperationException or TimeoutException)
    {
      stopwatch.Stop();
      string deviceSummary = string.Join(
        "; ",
        devices.Select(d => d.IsDefault
          ? $"{d.DisplayName} [{d.DeviceId}] (default)"
          : $"{d.DisplayName} [{d.DeviceId}]"));

      string failureDetails = ex is AudioCaptureException captureEx && captureEx.InnerException is not null
        ? $"{captureEx.Message} Inner: {captureEx.InnerException.Message}"
        : ex.Message;

      return new SpikeExecutionResult(
        "audio",
        false,
        "WASAPI capture failed.",
        stopwatch.Elapsed,
        null,
        $"preferred={preferredDeviceId}; devices={deviceSummary}; error={failureDetails}");
    }

    Pcm16WaveFileWriter.WriteMonoPcm16(resolvedOutputPath, result.Pcm16Mono, result.SampleRateHz);
    AudioCaptureMetrics metrics = captureService.LastMetrics;

    string details = string.Format(
      CultureInfo.InvariantCulture,
      "Captured {0} bytes ({1:F2}s). start={2:F1}ms stop={3:F1}ms",
      result.Pcm16Mono.Length,
      result.Duration.TotalSeconds,
      metrics.StartLatency.TotalMilliseconds,
      metrics.StopLatency.TotalMilliseconds);

    return new SpikeExecutionResult(
      "audio",
      true,
      "WASAPI capture completed.",
      stopwatch.Elapsed,
      resolvedOutputPath,
      details);
  }

  private static async Task<SpikeExecutionResult> RunInsertionSpikeAsync(string[] args)
  {
    string text = GetOption(args, "--text") ?? "Dictate Anywhere insertion spike";
    string methodText = GetOption(args, "--method") ?? "clipboard";
    string? requiredMethodText = GetOption(args, "--require-method");
    bool restoreClipboard = GetBoolOption(args, "--restore-clipboard", true);

    InsertionMethod method = methodText.Equals("typing", StringComparison.OrdinalIgnoreCase)
      ? InsertionMethod.SendInputUnicodeTyping
      : InsertionMethod.ClipboardPaste;
    InsertionMethod? requiredMethod = requiredMethodText is null
      ? null
      : requiredMethodText.Equals("typing", StringComparison.OrdinalIgnoreCase)
        ? InsertionMethod.SendInputUnicodeTyping
        : InsertionMethod.ClipboardPaste;

    WindowsTextInsertionService insertionService = new();

    Stopwatch stopwatch = Stopwatch.StartNew();
    InsertionResult result = await insertionService.InsertAsync(text, method, restoreClipboard).ConfigureAwait(false);
    stopwatch.Stop();

    if (result.Outcome != InsertionOutcome.VerifiedInserted)
    {
      return new SpikeExecutionResult(
        "insertion",
        false,
        $"Text insertion returned {result.Outcome}.",
        stopwatch.Elapsed,
        null,
        result.ErrorMessage ?? "Unknown insertion failure.");
    }

    if (requiredMethod.HasValue && result.MethodUsed != requiredMethod.Value)
    {
      return new SpikeExecutionResult(
        "insertion",
        false,
        "Insertion fell back to an unexpected method.",
        stopwatch.Elapsed,
        null,
        $"Expected method {requiredMethod.Value} but used {result.MethodUsed}. {result.ErrorMessage}");
    }

    return new SpikeExecutionResult(
      "insertion",
      true,
      $"Insertion verified using {result.MethodUsed}.",
      stopwatch.Elapsed,
      null,
      string.IsNullOrWhiteSpace(result.ErrorMessage)
        ? "Automation verified that text appeared in the foreground application."
        : result.ErrorMessage);
  }

  private static string? GetOption(string[] args, string optionName)
  {
    for (int i = 0; i < args.Length - 1; i++)
    {
      if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
      {
        return args[i + 1];
      }
    }

    return null;
  }

  private static int GetIntOption(string[] args, string optionName, int defaultValue)
  {
    string? value = GetOption(args, optionName);
    if (string.IsNullOrWhiteSpace(value))
    {
      return defaultValue;
    }

    return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
      ? parsed
      : defaultValue;
  }

  private static bool GetBoolOption(string[] args, string optionName, bool defaultValue)
  {
    string? value = GetOption(args, optionName);
    if (string.IsNullOrWhiteSpace(value))
    {
      return defaultValue;
    }

    return bool.TryParse(value, out bool parsed) ? parsed : defaultValue;
  }

  private static HotkeyModifiers ParseModifiers(string input)
  {
    HotkeyModifiers result = HotkeyModifiers.None;
    string[] parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    foreach (string part in parts)
    {
      if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
      {
        result |= HotkeyModifiers.Alt;
      }
      else if (part.Equals("Control", StringComparison.OrdinalIgnoreCase) || part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
      {
        result |= HotkeyModifiers.Control;
      }
      else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
      {
        result |= HotkeyModifiers.Shift;
      }
      else if (part.Equals("Windows", StringComparison.OrdinalIgnoreCase) || part.Equals("Win", StringComparison.OrdinalIgnoreCase))
      {
        result |= HotkeyModifiers.Windows;
      }
    }

    return result;
  }

  private static InputDispatchResult TriggerHotkey(HotkeyBinding binding)
  {
    INPUT[] inputs = BuildHotkeyInputs(binding);
    int inputSize = Marshal.SizeOf<INPUT>();
    uint sent = SendInputNative((uint)inputs.Length, inputs, inputSize);
    if (sent == inputs.Length)
    {
      return InputDispatchResult.Ok;
    }

    int error = Marshal.GetLastWin32Error();
    return new InputDispatchResult(
      false,
      $"SendInput failed while self-triggering hotkey. sent={sent}/{inputs.Length}, error={error}, inputSize={inputSize}.");
  }

  private static async Task<(bool success, DateTimeOffset pressedAt, DateTimeOffset releasedAt)> TryAwaitHotkeyPairAsync(
    Task<DateTimeOffset> pressedTask,
    Task<DateTimeOffset> releasedTask,
    TimeSpan timeout)
  {
    try
    {
      DateTimeOffset pressedAt = await pressedTask.WaitAsync(timeout).ConfigureAwait(false);
      DateTimeOffset releasedAt = await releasedTask.WaitAsync(timeout).ConfigureAwait(false);
      return (true, pressedAt, releasedAt);
    }
    catch (TimeoutException)
    {
      return (false, default, default);
    }
  }

  private static bool TryPostSyntheticHotkeyMessage(WindowsHotkeyService service, int hotkeyId, out string error)
  {
    const uint wmHotkey = 0x0312;

    try
    {
      FieldInfo? threadIdField = typeof(WindowsHotkeyService).GetField("messageThreadId", BindingFlags.NonPublic | BindingFlags.Instance);
      if (threadIdField?.GetValue(service) is not uint threadId || threadId == 0)
      {
        error = "Hotkey message thread ID is unavailable.";
        return false;
      }

      bool posted = PostThreadMessageNative(threadId, wmHotkey, new UIntPtr((uint)hotkeyId), IntPtr.Zero);
      if (!posted)
      {
        int win32Error = Marshal.GetLastWin32Error();
        error = $"PostThreadMessage failed with Win32 error {win32Error}.";
        return false;
      }

      error = string.Empty;
      return true;
    }
#pragma warning disable CA1031 // Spike fallback must return structured failure details.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      error = ex.Message;
      return false;
    }
  }

  private static INPUT[] BuildHotkeyInputs(HotkeyBinding binding)
  {
    const ushort virtualKeyLeftWindows = 0x5B;
    const ushort virtualKeyControl = 0x11;
    const ushort virtualKeyAlt = 0x12;
    const ushort virtualKeyShift = 0x10;
    const uint keyEventKeyUp = 0x0002;

    Span<ushort> modifiers = stackalloc ushort[4];
    int modifierCount = 0;
    if ((binding.Modifiers & HotkeyModifiers.Windows) != 0)
    {
      modifiers[modifierCount++] = virtualKeyLeftWindows;
    }

    if ((binding.Modifiers & HotkeyModifiers.Control) != 0)
    {
      modifiers[modifierCount++] = virtualKeyControl;
    }

    if ((binding.Modifiers & HotkeyModifiers.Alt) != 0)
    {
      modifiers[modifierCount++] = virtualKeyAlt;
    }

    if ((binding.Modifiers & HotkeyModifiers.Shift) != 0)
    {
      modifiers[modifierCount++] = virtualKeyShift;
    }

    INPUT[] inputs = new INPUT[(modifierCount * 2) + 2];
    int index = 0;

    for (int i = 0; i < modifierCount; i++)
    {
      inputs[index++] = CreateVirtualKeyInput(modifiers[i], flags: 0);
    }

    inputs[index++] = CreateVirtualKeyInput((ushort)binding.VirtualKey, flags: 0);
    inputs[index++] = CreateVirtualKeyInput((ushort)binding.VirtualKey, flags: keyEventKeyUp);

    for (int i = modifierCount - 1; i >= 0; i--)
    {
      inputs[index++] = CreateVirtualKeyInput(modifiers[i], flags: keyEventKeyUp);
    }

    return inputs;
  }

  private static INPUT CreateVirtualKeyInput(ushort virtualKey, uint flags)
  {
    return new INPUT
    {
      Type = 1,
      InputUnion = new INPUTUNION
      {
        KeyboardInput = new KEYBDINPUT
        {
          VirtualKey = virtualKey,
          ScanCode = 0,
          Flags = flags,
          Time = 0,
          ExtraInfo = IntPtr.Zero,
        },
      },
    };
  }

  private static void PrintUsage()
  {
    Console.Error.WriteLine("Usage: DictateAnywhere.Spikes <hotkey|audio|insertion> [options]");
    Console.Error.WriteLine("hotkey options: --modifiers Windows,Alt --virtual-key 32 --timeout-seconds 20 --self-trigger true");
    Console.Error.WriteLine("audio options: --duration-seconds 5 --output artifacts/spikes/audio-capture.wav");
    Console.Error.WriteLine("insertion options: --method clipboard|typing --text \"example\" --restore-clipboard true|false");
  }

  private static TimeSpan ResolveOperationTimeout(string command, string[] args)
  {
    int explicitTimeoutSeconds = GetIntOption(args, "--operation-timeout-seconds", -1);
    if (explicitTimeoutSeconds > 0)
    {
      return TimeSpan.FromSeconds(explicitTimeoutSeconds);
    }

    return command switch
    {
      "hotkey" => TimeSpan.FromSeconds(GetIntOption(args, "--timeout-seconds", 20) + 10),
      "audio" => TimeSpan.FromSeconds(GetIntOption(args, "--duration-seconds", 5) + 20),
      "insertion" => TimeSpan.FromSeconds(20),
      _ => TimeSpan.FromSeconds(30),
    };
  }

  [DllImport("user32.dll", EntryPoint = "SendInput", SetLastError = true)]
  private static extern uint SendInputNative(uint inputCount, INPUT[] inputs, int inputSize);

  [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
  private static extern bool PostThreadMessageNative(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);

  [StructLayout(LayoutKind.Sequential)]
  private struct INPUT
  {
    public uint Type;
    public INPUTUNION InputUnion;
  }

  [StructLayout(LayoutKind.Explicit)]
  private struct INPUTUNION
  {
    [FieldOffset(0)]
    public MOUSEINPUT MouseInput;

    [FieldOffset(0)]
    public KEYBDINPUT KeyboardInput;

    [FieldOffset(0)]
    public HARDWAREINPUT HardwareInput;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct MOUSEINPUT
  {
    public int Dx;
    public int Dy;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public IntPtr ExtraInfo;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct KEYBDINPUT
  {
    public ushort VirtualKey;
    public ushort ScanCode;
    public uint Flags;
    public uint Time;
    public IntPtr ExtraInfo;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct HARDWAREINPUT
  {
    public uint Message;
    public ushort ParamL;
    public ushort ParamH;
  }

  private sealed record SpikeExecutionResult(
    string SpikeName,
    bool Success,
    string Summary,
    TimeSpan? Duration,
    string? ArtifactPath,
    string? Details);
}

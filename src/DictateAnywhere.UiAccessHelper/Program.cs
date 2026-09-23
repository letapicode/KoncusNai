using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
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
    try
    {
      HelperInsertCommand command = await ParseArgumentsAsync(args).ConfigureAwait(false);
      InsertionResult insertionResult = await ExecuteInsertionAsync(command).ConfigureAwait(false);
      WriteResponse(new HelperInsertionResponse(
        (int)insertionResult.Outcome,
        insertionResult.Success,
        (int)insertionResult.MethodUsed,
        insertionResult.ErrorMessage,
        insertionResult.BlockReason,
        insertionResult.RecoveryCopyAvailable));
      return 0;
    }
    catch (HelperCommandException ex)
    {
      WriteResponse(new HelperInsertionResponse(
        (int)InsertionOutcome.UnknownOutcome,
        false,
        (int)InsertionMethod.ClipboardPaste,
        ex.Message));
      return 0;
    }
#pragma warning disable CA1031
    catch (Exception ex)
#pragma warning restore CA1031
    {
      Console.Error.WriteLine(ex);
      WriteResponse(new HelperInsertionResponse(
        (int)InsertionOutcome.UnknownOutcome,
        false,
        (int)InsertionMethod.ClipboardPaste,
        "UIAccess helper failed unexpectedly. Try again."));
      return 0;
    }
  }

  private static async Task<InsertionResult> ExecuteInsertionAsync(HelperInsertCommand command)
  {
    WindowsWindowFocusProvider focus = new();
    if (!command.Target.Matches(focus.GetWindowFocusContext()))
    {
      return InsertionResult.Blocked(command.Method, "The authorized target changed before helper insertion.", InsertionBlockReason.TargetChanged);
    }
    TextInsertionOptions options = new(
      EnableSecureFieldDetection: true,
      BlockedProcessNames: Array.Empty<string>(),
      EnableElevatedInsertion: false);

    WindowsTextInsertionService insertionService = new(
      new WindowsClipboardController(),
      new WindowsInputDispatcher(),
      focus,
      AllowAllPrivilegeBoundaryDetector.Instance,
      options,
      elevatedInsertionBridge: NullElevatedInsertionBridge.Instance);

    if (!insertionService.TryCaptureTarget(command.Target))
    {
      return InsertionResult.Blocked(command.Method, "The authorized target changed before helper insertion.", InsertionBlockReason.TargetChanged);
    }
    return await insertionService
      .InsertAsync(command.Text, command.Method, command.RestoreClipboard)
      .ConfigureAwait(false);
  }

  private static async Task<HelperInsertCommand> ParseArgumentsAsync(string[] args)
  {
    if (args.Length == 0)
    {
      throw new HelperCommandException(UsageMessage);
    }

    bool insertRequested = false;
    bool textStdinRequested = false;
    Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase);

    for (int index = 0; index < args.Length; index++)
    {
      string argument = args[index];
      if (string.Equals(argument, "--insert", StringComparison.OrdinalIgnoreCase))
      {
        insertRequested = true;
        continue;
      }

      if (string.Equals(argument, "--textStdin", StringComparison.OrdinalIgnoreCase))
      {
        textStdinRequested = true;
        continue;
      }

      if (!argument.StartsWith("--", StringComparison.Ordinal))
      {
        throw new HelperCommandException($"Unexpected argument '{argument}'. {UsageMessage}");
      }

      if (index >= args.Length - 1)
      {
        throw new HelperCommandException($"Missing value for argument '{argument}'. {UsageMessage}");
      }

      string value = args[index + 1];
      options[argument] = value;
      index++;
    }

    if (!insertRequested)
    {
      throw new HelperCommandException($"The '--insert' command is required. {UsageMessage}");
    }

    if (!textStdinRequested)
    {
      throw new HelperCommandException($"The '--textStdin' option is required. {UsageMessage}");
    }

    InsertionMethod method = ParseMethod(ReadRequiredOption(options, "--method"));
    bool restoreClipboard = ParseBooleanFlag(ReadRequiredOption(options, "--restoreClipboard"), "--restoreClipboard");
    string? textBase64 = await Console.In.ReadLineAsync().ConfigureAwait(false);
    if (string.IsNullOrWhiteSpace(textBase64))
    {
      throw new HelperCommandException("Missing Base64-encoded UTF-8 text on standard input.");
    }

    string text = ParseTextBase64(textBase64);

    InsertionTargetIdentity target;
    try
    {
      target = JsonSerializer.Deserialize<InsertionTargetIdentity>(
        Encoding.UTF8.GetString(Convert.FromBase64String(ReadRequiredOption(options, "--target"))))
        ?? throw new HelperCommandException("Missing insertion target identity.");
    }
    catch (Exception ex) when (ex is FormatException or JsonException)
    {
      throw new HelperCommandException("Invalid insertion target identity.");
    }
    return new HelperInsertCommand(method, restoreClipboard, text, target);
  }

  private static InsertionMethod ParseMethod(string value)
  {
    if (string.Equals(value, "clipboard", StringComparison.OrdinalIgnoreCase))
    {
      return InsertionMethod.ClipboardPaste;
    }

    if (string.Equals(value, "typing", StringComparison.OrdinalIgnoreCase))
    {
      return InsertionMethod.SendInputUnicodeTyping;
    }

    throw new HelperCommandException("--method must be 'clipboard' or 'typing'.");
  }

  private static bool ParseBooleanFlag(string value, string optionName)
  {
    if (value == "1")
    {
      return true;
    }

    if (value == "0")
    {
      return false;
    }

    if (bool.TryParse(value, out bool parsed))
    {
      return parsed;
    }

    throw new HelperCommandException($"{optionName} must be 0/1 or true/false.");
  }

  private static string ParseTextBase64(string value)
  {
    try
    {
      byte[] bytes = Convert.FromBase64String(value);
      return Encoding.UTF8.GetString(bytes);
    }
    catch (FormatException)
    {
      throw new HelperCommandException("Standard input must contain valid Base64-encoded UTF-8 text.");
    }
  }

  private static string ReadRequiredOption(IReadOnlyDictionary<string, string> options, string optionName)
  {
    if (!options.TryGetValue(optionName, out string? value) || string.IsNullOrWhiteSpace(value))
    {
      throw new HelperCommandException($"Missing required argument '{optionName}'. {UsageMessage}");
    }

    return value;
  }

  private static void WriteResponse(HelperInsertionResponse response)
  {
    string payload = JsonSerializer.Serialize(response, JsonOptions);
    Console.Out.WriteLine(payload);
  }

  private sealed record HelperInsertCommand(
    InsertionMethod Method,
    bool RestoreClipboard,
    string Text,
    InsertionTargetIdentity Target);

  private sealed record HelperInsertionResponse(
    [property: JsonPropertyName("outcome")] int Outcome,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("methodUsed")] int MethodUsed,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage,
    [property: JsonPropertyName("blockReason")] InsertionBlockReason BlockReason = InsertionBlockReason.Unspecified,
    [property: JsonPropertyName("recoveryCopyAvailable")] bool RecoveryCopyAvailable = false);

  private sealed class HelperCommandException : Exception
  {
    public HelperCommandException(string message)
      : base(message)
    {
    }
  }

  private sealed class AllowAllPrivilegeBoundaryDetector : IPrivilegeBoundaryDetector
  {
    public static AllowAllPrivilegeBoundaryDetector Instance { get; } = new();

    public PrivilegeBoundaryCheckResult Evaluate(nint targetWindowHandle)
    {
      return targetWindowHandle == 0
        ? new PrivilegeBoundaryCheckResult(false, "No active target window is available for insertion.")
        : PrivilegeBoundaryCheckResult.AllowedResult;
    }
  }

  private sealed class NullElevatedInsertionBridge : IElevatedInsertionBridge
  {
    public static NullElevatedInsertionBridge Instance { get; } = new();

    public Task<InsertionResult> InsertAsync(
      string text,
      InsertionMethod preferredMethod,
      bool restoreClipboard,
      System.Threading.CancellationToken cancellationToken = default)
    {
      return Task.FromResult(new InsertionResult(false, preferredMethod, "Elevated routing is disabled in UIAccess helper."));
    }
  }

  private const string UsageMessage =
    "Usage: DictateAnywhere.UiAccessHelper --insert --method <clipboard|typing> --restoreClipboard <0|1> --textStdin; send one Base64 UTF-8 line on standard input.";
}

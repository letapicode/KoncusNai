using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Insertion;

public sealed class UiAccessHelperProcessBridge : IElevatedInsertionBridge
{
  private readonly UiAccessHelperProcessBridgeOptions options;
  private readonly IDiagnostics? diagnostics;

  public UiAccessHelperProcessBridge()
    : this(UiAccessHelperProcessBridgeOptions.Default, diagnostics: null)
  {
  }

  public UiAccessHelperProcessBridge(UiAccessHelperProcessBridgeOptions options)
    : this(options, diagnostics: null)
  {
  }

  public UiAccessHelperProcessBridge(UiAccessHelperProcessBridgeOptions options, IDiagnostics? diagnostics)
  {
    this.options = options ?? throw new ArgumentNullException(nameof(options));
    this.diagnostics = diagnostics;
    if (string.IsNullOrWhiteSpace(this.options.HelperExecutablePath))
    {
      throw new ArgumentException("Helper executable path must not be empty.", nameof(options));
    }

    if (this.options.InvocationTimeout <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(options), "Invocation timeout must be positive.");
    }
  }

  public async Task<InsertionResult> InsertAsync(
    string text,
    InsertionMethod preferredMethod,
    bool restoreClipboard,
    CancellationToken cancellationToken = default)
    => await InsertAsync(text, preferredMethod, restoreClipboard,
      InsertionTargetIdentity.FromContext(new WindowsWindowFocusProvider().GetWindowFocusContext()), cancellationToken).ConfigureAwait(false);

  public async Task<InsertionResult> InsertAsync(
    string text,
    InsertionMethod preferredMethod,
    bool restoreClipboard,
    InsertionTargetIdentity target,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(text);
    cancellationToken.ThrowIfCancellationRequested();

    string helperPath = Path.GetFullPath(options.HelperExecutablePath);
    if (!File.Exists(helperPath))
    {
      return new InsertionResult(
        false,
        preferredMethod,
        $"UIAccess helper executable was not found at '{helperPath}'.");
    }

    if (options.RequireSecureInstallLocation && !IsSecureInstallLocation(AppContext.BaseDirectory, helperPath, out string? locationError))
    {
      return new InsertionResult(false, preferredMethod, locationError);
    }

    if (options.RequireSignedHostBinary && !TryVerifySignedBinary(Environment.ProcessPath, out string? hostSignatureError))
    {
      return new InsertionResult(false, preferredMethod, hostSignatureError);
    }

    if (options.RequireSignedHelperBinary && !TryVerifySignedBinary(helperPath, out string? helperSignatureError))
    {
      return new InsertionResult(false, preferredMethod, helperSignatureError);
    }

    string methodToken = preferredMethod == InsertionMethod.ClipboardPaste
      ? "clipboard"
      : "typing";
    string restoreToken = restoreClipboard ? "1" : "0";
    string textBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    ProcessStartInfo startInfo = new()
    {
      FileName = helperPath,
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardInput = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      WorkingDirectory = Path.GetDirectoryName(helperPath) ?? AppContext.BaseDirectory,
      StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
    };
    startInfo.ArgumentList.Add("--insert");
    startInfo.ArgumentList.Add("--method");
    startInfo.ArgumentList.Add(methodToken);
    startInfo.ArgumentList.Add("--restoreClipboard");
    startInfo.ArgumentList.Add(restoreToken);
    startInfo.ArgumentList.Add("--textStdin");
    startInfo.ArgumentList.Add("--target");
    startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(target))));

    using Process process = new()
    {
      StartInfo = startInfo,
    };

    if (!process.Start())
    {
      return new InsertionResult(false, preferredMethod, "Failed to start UIAccess helper process.");
    }

    using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutCts.CancelAfter(options.InvocationTimeout);
    Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
    Task<string> stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

    try
    {
      await process.StandardInput.WriteLineAsync(textBase64.AsMemory(), timeoutCts.Token).ConfigureAwait(false);
      process.StandardInput.Close();
      await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      await TryTerminateProcessAsync(process).ConfigureAwait(false);
      if (!cancellationToken.IsCancellationRequested)
      {
        return new InsertionResult(false, preferredMethod, "UIAccess helper timed out while processing insertion.");
      }

      throw;
    }

    string stdout = (await stdoutTask.ConfigureAwait(false)).Trim();
    string stderr = (await stderrTask.ConfigureAwait(false)).Trim();
    if (!string.IsNullOrWhiteSpace(stderr))
    {
      diagnostics?.Warning($"UIAccess helper technical output: {stderr}");
    }

    if (process.ExitCode != 0)
    {
      return new InsertionResult(false, preferredMethod, "UIAccess helper exited unexpectedly. Try again.");
    }

    if (string.IsNullOrWhiteSpace(stdout))
    {
      return new InsertionResult(false, preferredMethod, "UIAccess helper returned no result payload.");
    }

    try
    {
      HelperInsertionResponse? response = JsonSerializer.Deserialize<HelperInsertionResponse>(stdout);
      if (response is null)
      {
        return new InsertionResult(false, preferredMethod, "UIAccess helper response was empty.");
      }

      InsertionMethod methodUsed = response.MethodUsed switch
      {
        0 => InsertionMethod.ClipboardPaste,
        1 => InsertionMethod.SendInputUnicodeTyping,
        _ => preferredMethod,
      };

      InsertionOutcome outcome = Enum.IsDefined(typeof(InsertionOutcome), response.Outcome)
        ? (InsertionOutcome)response.Outcome
        : response.Success
          ? InsertionOutcome.VerifiedInserted
          : InsertionOutcome.UnknownOutcome;

      return new InsertionResult(outcome, methodUsed, response.ErrorMessage)
      {
        BlockReason = response.BlockReason,
        RecoveryCopyAvailable = response.RecoveryCopyAvailable,
      };
    }
    catch (JsonException)
    {
      return new InsertionResult(false, preferredMethod, "UIAccess helper returned malformed JSON.");
    }
  }

  private static bool IsSecureInstallLocation(string appBaseDirectory, string helperExecutablePath, out string error)
  {
    error = string.Empty;

    if (!TryIsUnderProgramFiles(appBaseDirectory))
    {
      error = "Elevated insertion requires installation under Program Files.";
      return false;
    }

    string helperDirectory = Path.GetDirectoryName(helperExecutablePath) ?? string.Empty;
    if (!TryIsUnderProgramFiles(helperDirectory))
    {
      error = "UIAccess helper must be installed under Program Files.";
      return false;
    }

    return true;
  }

  private static bool TryIsUnderProgramFiles(string path)
  {
    if (string.IsNullOrWhiteSpace(path))
    {
      return false;
    }

    string fullPath = Path.GetFullPath(path)
      .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
      + Path.DirectorySeparatorChar;

    string programFiles = NormalizeDirectory(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
    string programFilesX86 = NormalizeDirectory(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

    return (!string.IsNullOrWhiteSpace(programFiles)
            && fullPath.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase))
           || (!string.IsNullOrWhiteSpace(programFilesX86)
               && fullPath.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase));
  }

  private static string NormalizeDirectory(string? path)
  {
    if (string.IsNullOrWhiteSpace(path))
    {
      return string.Empty;
    }

    return Path.GetFullPath(path)
      .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
      + Path.DirectorySeparatorChar;
  }

  private static bool TryVerifySignedBinary(string? executablePath, out string error)
  {
    if (string.IsNullOrWhiteSpace(executablePath))
    {
      error = "Executable path required for signature verification was not available.";
      return false;
    }

    try
    {
      using X509Certificate signedBinary = X509Certificate.CreateFromSignedFile(executablePath);
      error = string.Empty;
      return true;
    }
    catch (CryptographicException)
    {
      error = $"Signed binary verification failed for '{executablePath}'.";
      return false;
    }
    catch (FileNotFoundException)
    {
      error = $"Signed binary verification failed because '{executablePath}' does not exist.";
      return false;
    }
  }

  private async Task TryTerminateProcessAsync(Process process)
  {
    try
    {
      if (!process.HasExited)
      {
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync()
          .WaitAsync(TimeSpan.FromSeconds(5))
          .ConfigureAwait(false);
      }
    }
    catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or TimeoutException)
    {
      diagnostics?.Warning($"UIAccess helper cleanup did not complete cleanly: {ex.Message}");
    }
  }

  private sealed record HelperInsertionResponse(
    [property: JsonPropertyName("outcome")] int Outcome,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("methodUsed")] int MethodUsed,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage,
    [property: JsonPropertyName("blockReason")] InsertionBlockReason BlockReason = InsertionBlockReason.Unspecified,
    [property: JsonPropertyName("recoveryCopyAvailable")] bool RecoveryCopyAvailable = false);
}

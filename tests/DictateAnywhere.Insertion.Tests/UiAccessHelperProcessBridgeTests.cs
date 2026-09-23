using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Insertion;

namespace DictateAnywhere.Insertion.Tests;

[Xunit.Trait("Category", "ProcessIntegration")]
public sealed class UiAccessHelperProcessBridgeTests
{
  [Xunit.Fact]
  public async Task InsertAsync_ReturnsFailure_WhenHelperExecutableMissing()
  {
    UiAccessHelperProcessBridge bridge = new(new UiAccessHelperProcessBridgeOptions(
      HelperExecutablePath: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing-helper.exe"),
      InvocationTimeout: TimeSpan.FromSeconds(3),
      RequireSecureInstallLocation: false,
      RequireSignedHostBinary: false,
      RequireSignedHelperBinary: false));

    InsertionResult result = await bridge.InsertAsync(
      "test",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.False(result.Success);
    Xunit.Assert.Contains("not found", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task InsertAsync_ReturnsFailure_WhenHelperBinaryIsUnsigned()
  {
    using TemporaryDirectoryScope scope = new();
    string helperPath = Path.Combine(scope.DirectoryPath, "DictateAnywhere.UiAccessHelper.exe");
    await File.WriteAllTextAsync(helperPath, "unsigned");

    UiAccessHelperProcessBridge bridge = new(new UiAccessHelperProcessBridgeOptions(
      HelperExecutablePath: helperPath,
      InvocationTimeout: TimeSpan.FromSeconds(3),
      RequireSecureInstallLocation: false,
      RequireSignedHostBinary: false,
      RequireSignedHelperBinary: true));

    InsertionResult result = await bridge.InsertAsync(
      "test",
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.False(result.Success);
    Xunit.Assert.Contains("signed binary verification failed", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task InsertAsync_RespectsCancellation()
  {
    UiAccessHelperProcessBridge bridge = new(new UiAccessHelperProcessBridgeOptions(
      HelperExecutablePath: Path.Combine(Path.GetTempPath(), "missing-helper.exe"),
      InvocationTimeout: TimeSpan.FromSeconds(3),
      RequireSecureInstallLocation: false,
      RequireSignedHostBinary: false,
      RequireSignedHelperBinary: false));

    using CancellationTokenSource cts = new();
    cts.Cancel();

    await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => bridge.InsertAsync("test", InsertionMethod.ClipboardPaste, restoreClipboard: true, cts.Token));
  }

  [Xunit.Fact]
  public async Task InsertAsync_ReturnsFailure_WhenHelperTimesOut()
  {
    using TemporaryDirectoryScope scope = new();
    string helperScript = Path.Combine(scope.DirectoryPath, "timeout-helper.cmd");
    await File.WriteAllTextAsync(helperScript, "@echo off\r\nping -n 10 127.0.0.1 > nul\r\n");

    UiAccessHelperProcessBridge bridge = new(new UiAccessHelperProcessBridgeOptions(
      HelperExecutablePath: helperScript,
      InvocationTimeout: TimeSpan.FromMilliseconds(400),
      RequireSecureInstallLocation: false,
      RequireSignedHostBinary: false,
      RequireSignedHelperBinary: false));

    InsertionResult result = await bridge.InsertAsync("test", InsertionMethod.ClipboardPaste, restoreClipboard: true);

    Xunit.Assert.False(result.Success);
    Xunit.Assert.Contains("timed out", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task InsertAsync_KillsProcessAndThrows_WhenCancelledMidExecution()
  {
    using TemporaryDirectoryScope scope = new();
    string helperScript = Path.Combine(scope.DirectoryPath, "cancel-helper.cmd");
    await File.WriteAllTextAsync(helperScript, "@echo off\r\nping -n 10 127.0.0.1 > nul\r\n");

    UiAccessHelperProcessBridge bridge = new(new UiAccessHelperProcessBridgeOptions(
      HelperExecutablePath: helperScript,
      InvocationTimeout: TimeSpan.FromSeconds(10),
      RequireSecureInstallLocation: false,
      RequireSignedHostBinary: false,
      RequireSignedHelperBinary: false));

    using CancellationTokenSource cts = new();
    cts.CancelAfter(TimeSpan.FromMilliseconds(200));

    await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => bridge.InsertAsync("test", InsertionMethod.ClipboardPaste, restoreClipboard: true, cts.Token));
  }

  [Xunit.Fact]
  public async Task InsertAsync_ReturnsFailure_WhenHelperExitsWithNonZeroCode()
  {
    using TemporaryDirectoryScope scope = new();
    string helperScript = Path.Combine(scope.DirectoryPath, "crash-helper.cmd");
    await File.WriteAllTextAsync(helperScript, "@echo off\r\necho UAC access denied 1>&2\r\nexit /b 42\r\n");

    TestDiagnostics diagnostics = new();
    UiAccessHelperProcessBridge bridge = new(new UiAccessHelperProcessBridgeOptions(
      HelperExecutablePath: helperScript,
      InvocationTimeout: TimeSpan.FromSeconds(5),
      RequireSecureInstallLocation: false,
      RequireSignedHostBinary: false,
      RequireSignedHelperBinary: false), diagnostics);

    InsertionResult result = await bridge.InsertAsync("test", InsertionMethod.ClipboardPaste, restoreClipboard: true);

    Xunit.Assert.False(result.Success);
    Xunit.Assert.Equal("UIAccess helper exited unexpectedly. Try again.", result.ErrorMessage);
    Xunit.Assert.DoesNotContain("UAC access denied", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Contains(diagnostics.Warnings, warning =>
      warning.Contains("UAC access denied", StringComparison.OrdinalIgnoreCase));
  }

  [Xunit.Fact]
  public async Task InsertAsync_ReturnsFailure_WhenHelperReturnsMalformedJson()
  {
    using TemporaryDirectoryScope scope = new();
    string helperScript = Path.Combine(scope.DirectoryPath, "malformed-helper.cmd");
    await File.WriteAllTextAsync(helperScript, "@echo off\r\necho not a json response\r\n");

    UiAccessHelperProcessBridge bridge = new(new UiAccessHelperProcessBridgeOptions(
      HelperExecutablePath: helperScript,
      InvocationTimeout: TimeSpan.FromSeconds(5),
      RequireSecureInstallLocation: false,
      RequireSignedHostBinary: false,
      RequireSignedHelperBinary: false));

    InsertionResult result = await bridge.InsertAsync("test", InsertionMethod.ClipboardPaste, restoreClipboard: true);

    Xunit.Assert.False(result.Success);
    Xunit.Assert.Contains("malformed JSON", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task InsertAsync_ReturnsSuccess_WhenHelperReturnsValidJson()
  {
    using TemporaryDirectoryScope scope = new();
    string helperScript = Path.Combine(scope.DirectoryPath, "valid-helper.cmd");
    await File.WriteAllTextAsync(helperScript, "@echo off\r\necho {\"outcome\":0,\"success\":true,\"methodUsed\":0}\r\n");

    UiAccessHelperProcessBridge bridge = new(new UiAccessHelperProcessBridgeOptions(
      HelperExecutablePath: helperScript,
      InvocationTimeout: TimeSpan.FromSeconds(5),
      RequireSecureInstallLocation: false,
      RequireSignedHostBinary: false,
      RequireSignedHelperBinary: false));

    InsertionResult result = await bridge.InsertAsync("test", InsertionMethod.ClipboardPaste, restoreClipboard: true);

    Xunit.Assert.True(result.Success);
    Xunit.Assert.Equal(InsertionOutcome.VerifiedInserted, result.Outcome);
    Xunit.Assert.Equal(InsertionMethod.ClipboardPaste, result.MethodUsed);
  }

  [Xunit.Fact]
  public async Task InsertAsync_SendsSensitiveTextThroughStandardInput_NotProcessArguments()
  {
    using TemporaryDirectoryScope scope = new();
    string helperScript = Path.Combine(scope.DirectoryPath, "capture-helper.cmd");
    string argumentsPath = Path.Combine(scope.DirectoryPath, "arguments.txt");
    string standardInputPath = Path.Combine(scope.DirectoryPath, "stdin.txt");
    await File.WriteAllTextAsync(
      helperScript,
      "@echo off\r\n"
      + "set /p payload=\r\n"
      + ">\"%~dp0arguments.txt\" echo %*\r\n"
      + ">\"%~dp0stdin.txt\" echo %payload%\r\n"
      + "echo {\"outcome\":0,\"success\":true,\"methodUsed\":0}\r\n");

    UiAccessHelperProcessBridge bridge = new(new UiAccessHelperProcessBridgeOptions(
      HelperExecutablePath: helperScript,
      InvocationTimeout: TimeSpan.FromSeconds(5),
      RequireSecureInstallLocation: false,
      RequireSignedHostBinary: false,
      RequireSignedHelperBinary: false));
    const string sensitiveText = "private dictation text";
    string encodedText = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(sensitiveText));

    InsertionResult result = await bridge.InsertAsync(
      sensitiveText,
      InsertionMethod.ClipboardPaste,
      restoreClipboard: true);

    Xunit.Assert.True(result.Success);
    string arguments = (await File.ReadAllTextAsync(argumentsPath)).Trim();
    string standardInput = (await File.ReadAllTextAsync(standardInputPath)).Trim();
    Xunit.Assert.StartsWith("--insert --method clipboard --restoreClipboard 1 --textStdin --target ", arguments, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain(sensitiveText, arguments, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain(encodedText, arguments, StringComparison.Ordinal);
    Xunit.Assert.Equal(encodedText, standardInput);
  }

  private sealed class TemporaryDirectoryScope : IDisposable
  {
    public TemporaryDirectoryScope()
    {
      DirectoryPath = Path.Combine(
        Path.GetTempPath(),
        "DictateAnywhere.Tests.UiAccessHelperBridge",
        Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public void Dispose()
    {
      try
      {
        if (Directory.Exists(DirectoryPath))
        {
          Directory.Delete(DirectoryPath, recursive: true);
        }
      }
      catch (IOException)
      {
      }
      catch (UnauthorizedAccessException)
      {
      }
    }
  }

  private sealed class TestDiagnostics : IDiagnostics
  {
    public List<string> Warnings { get; } = new();

    public void Info(string message)
    {
    }

    public void Warning(string message) => Warnings.Add(message);

    public void Error(string message, Exception? exception = null)
    {
    }
  }
}

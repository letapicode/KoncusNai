using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Inference;

internal interface IIndicParlerRuntimeProvisioner
{
  Task EnsureReadyAsync(CancellationToken cancellationToken);
}

/// <summary>Installs the isolated Parler Python runtime on first use, never during ordinary app startup.</summary>
internal sealed class IndicParlerRuntimeProvisioner : IIndicParlerRuntimeProvisioner
{
  private static readonly SemaphoreSlim ProvisioningGate = new(1, 1);

  private readonly string requirementsPath;
  private readonly string torchLockPath;
  private readonly string setupScriptPath;
  private readonly string workerPath;
  private readonly string compatManifestPath;
  private readonly string runtimePythonPath;
  private readonly string requirementsStampPath;
  private readonly TimeSpan timeout;

  public IndicParlerRuntimeProvisioner(TimeSpan? timeout = null)
    : this(
      LocalModelScriptPathResolver.Resolve("requirements-indic-parler-lock.txt"),
      LocalModelScriptPathResolver.Resolve("requirements-indic-parler-torch-cpu-lock.txt"),
      ResolveSetupScriptPath(),
      LocalModelScriptPathResolver.Resolve("indic_parler_tts_worker.py"),
      ResolveCompatManifestPath(),
      ResolveRuntimePythonPath(),
      ResolveRequirementsStampPath(),
      timeout ?? TimeSpan.FromMinutes(45))
  {
  }

  internal IndicParlerRuntimeProvisioner(
    string requirementsPath,
    string torchLockPath,
    string setupScriptPath,
    string runtimePythonPath,
    string requirementsStampPath,
    TimeSpan timeout)
    : this(requirementsPath, torchLockPath, setupScriptPath, string.Empty, string.Empty, runtimePythonPath, requirementsStampPath, timeout)
  {
  }

  internal IndicParlerRuntimeProvisioner(
    string requirementsPath,
    string torchLockPath,
    string setupScriptPath,
    string workerPath,
    string compatManifestPath,
    string runtimePythonPath,
    string requirementsStampPath,
    TimeSpan timeout)
  {
    this.requirementsPath = requirementsPath;
    this.torchLockPath = torchLockPath;
    this.setupScriptPath = setupScriptPath;
    this.workerPath = workerPath;
    this.compatManifestPath = compatManifestPath;
    this.runtimePythonPath = runtimePythonPath;
    this.requirementsStampPath = requirementsStampPath;
    this.timeout = timeout;
  }

  public async Task EnsureReadyAsync(CancellationToken cancellationToken)
  {
    if (IsRuntimeReady(runtimePythonPath, requirementsStampPath, requirementsPath, torchLockPath, workerPath, compatManifestPath)
        && await CanImportRuntimeAsync(cancellationToken).ConfigureAwait(false))
    {
      return;
    }

    await ProvisioningGate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (IsRuntimeReady(runtimePythonPath, requirementsStampPath, requirementsPath, torchLockPath, workerPath, compatManifestPath)
          && await CanImportRuntimeAsync(cancellationToken).ConfigureAwait(false))
      {
        return;
      }

      if (!File.Exists(setupScriptPath))
      {
        throw new InvalidOperationException("The bundled Indic Parler-TTS setup component is missing. Repair the Koncus Nai installation and try again.");
      }

      await RunSetupAsync(cancellationToken).ConfigureAwait(false);
      if (!IsRuntimeReady(runtimePythonPath, requirementsStampPath, requirementsPath, torchLockPath, workerPath, compatManifestPath)
          || !await CanImportRuntimeAsync(cancellationToken).ConfigureAwait(false))
      {
        throw new InvalidOperationException("Automatic Indic Parler-TTS setup completed without producing a usable runtime.");
      }
    }
    finally
    {
      ProvisioningGate.Release();
    }
  }

  internal static bool IsRuntimeReady(string pythonPath, string stampPath, string requirementsPath, string torchLockPath)
    => IsRuntimeReady(pythonPath, stampPath, requirementsPath, torchLockPath, string.Empty, string.Empty);

  internal static bool IsRuntimeReady(
    string pythonPath,
    string stampPath,
    string requirementsPath,
    string torchLockPath,
    string workerPath,
    string compatManifestPath)
  {
    try
    {
      if (!File.Exists(pythonPath) || !File.Exists(stampPath) || !File.Exists(requirementsPath) || !File.Exists(torchLockPath))
      {
        return false;
      }

      string[] inputs = string.IsNullOrWhiteSpace(workerPath) || string.IsNullOrWhiteSpace(compatManifestPath)
        ? [requirementsPath, torchLockPath]
        : [requirementsPath, torchLockPath, workerPath, compatManifestPath];
      if (Array.Exists(inputs, input => !File.Exists(input)))
      {
        return false;
      }

      string hashMaterial = string.Join("\n", Array.ConvertAll(
        inputs,
        input => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)))));
      string expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashMaterial)));
      string actual = File.ReadAllText(stampPath, Encoding.UTF8).Trim();
      return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
    {
      return false;
    }
  }

  private async Task RunSetupAsync(CancellationToken cancellationToken)
  {
    ProcessStartInfo startInfo = new()
    {
      FileName = "powershell.exe",
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      WorkingDirectory = Path.GetDirectoryName(setupScriptPath)!,
    };
    startInfo.ArgumentList.Add("-NoProfile");
    startInfo.ArgumentList.Add("-NonInteractive");
    startInfo.ArgumentList.Add("-ExecutionPolicy");
    startInfo.ArgumentList.Add("Bypass");
    startInfo.ArgumentList.Add("-File");
    startInfo.ArgumentList.Add(setupScriptPath);
    startInfo.ArgumentList.Add("-TorchBackend");
    startInfo.ArgumentList.Add("Auto");

    using Process process = new() { StartInfo = startInfo };
    try
    {
      if (!process.Start())
      {
        throw new InvalidOperationException("Koncus Nai could not start automatic Indic Parler-TTS setup.");
      }

      Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
      Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
      try
      {
        await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
      }
      catch (TimeoutException)
      {
        TryStop(process);
        throw new TimeoutException($"Automatic Indic Parler-TTS setup did not finish within {timeout.TotalMinutes:F0} minutes.");
      }
      catch (OperationCanceledException)
      {
        TryStop(process);
        throw;
      }

      string output = await stdout.ConfigureAwait(false);
      string errors = await stderr.ConfigureAwait(false);
      if (process.ExitCode != 0)
      {
        _ = output;
        _ = errors;
        throw new InvalidOperationException($"Automatic Indic Parler-TTS setup failed with exit code {process.ExitCode}. See the local diagnostics log for technical details.");
      }
    }
    catch (Win32Exception ex)
    {
      throw new InvalidOperationException("Koncus Nai could not start its bundled Indic Parler-TTS setup process.", ex);
    }
  }

  private async Task<bool> CanImportRuntimeAsync(CancellationToken cancellationToken)
  {
    if (!File.Exists(runtimePythonPath))
    {
      return false;
    }

    ProcessStartInfo startInfo = new()
    {
      FileName = runtimePythonPath,
      UseShellExecute = false,
      CreateNoWindow = true,
    };
    startInfo.ArgumentList.Add("-c");
    startInfo.ArgumentList.Add("import torch, parler_tts, transformers, accelerate, safetensors, soundfile");
    try
    {
      using Process process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Koncus Nai could not validate its Indic Parler-TTS runtime.");
      try
      {
        await process.WaitForExitAsync(cancellationToken)
          .WaitAsync(TimeSpan.FromMinutes(2), cancellationToken)
          .ConfigureAwait(false);
      }
      catch (TimeoutException)
      {
        TryStop(process);
        return false;
      }
      catch (OperationCanceledException)
      {
        TryStop(process);
        throw;
      }

      return process.ExitCode == 0;
    }
    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
    {
      return false;
    }
  }

  private static void TryStop(Process process)
  {
    try
    {
      if (!process.HasExited)
      {
        process.Kill(entireProcessTree: true);
      }
    }
    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
    {
    }
  }

  private static string ResolveSetupScriptPath()
  {
    string? overridePath = Environment.GetEnvironmentVariable("DICTATEANYWHERE_INDIC_PARLER_SETUP_SCRIPT");
    if (!string.IsNullOrWhiteSpace(overridePath))
    {
      return Path.GetFullPath(overridePath.Trim());
    }

    string deployed = Path.Combine(AppContext.BaseDirectory, "setup-indic-parler-runtime.ps1");
    if (File.Exists(deployed))
    {
      return deployed;
    }

    DirectoryInfo? current = new(AppContext.BaseDirectory);
    while (current is not null)
    {
      string candidate = Path.Combine(current.FullName, "scripts", "setup-indic-parler-runtime.ps1");
      if (File.Exists(candidate))
      {
        return candidate;
      }

      current = current.Parent;
    }

    return deployed;
  }

  private static string ResolveRuntimeRoot()
  {
    string? overridePath = Environment.GetEnvironmentVariable("DICTATEANYWHERE_INDIC_PARLER_RUNTIME_ROOT");
    return string.IsNullOrWhiteSpace(overridePath)
      ? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DictateAnywhere",
        "indic-parler-runtime")
      : Path.GetFullPath(overridePath.Trim());
  }

  private static string ResolveCompatManifestPath()
  {
    string setupRoot = Path.GetDirectoryName(ResolveSetupScriptPath())!;
    string published = Path.Combine(setupRoot, "compat", "MANIFEST.sha256");
    return File.Exists(published)
      ? published
      : Path.GetFullPath(Path.Combine(setupRoot, "..", "third_party", "compat", "MANIFEST.sha256"));
  }

  private static string ResolveRuntimePythonPath() => Path.Combine(
    ResolveRuntimeRoot(),
    ".venv",
    OperatingSystem.IsWindows() ? "Scripts" : "bin",
    OperatingSystem.IsWindows() ? "python.exe" : "python");

  private static string ResolveRequirementsStampPath() => Path.Combine(
    ResolveRuntimeRoot(),
    ".requirements.sha256");
}

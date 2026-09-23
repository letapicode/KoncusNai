using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.App.Runtime;

internal sealed class LocalPythonRuntimeDependencyProbe : IChatRuntimeReadinessProbe
{
  private const string DependencyProbeScript = """
import importlib
import json
import sys

modules = sys.argv[1:]
missing = []
broken = []
versions = {}

for module in modules:
    try:
        loaded = importlib.import_module(module)
        versions[module] = str(getattr(loaded, "__version__", "unknown"))
    except ModuleNotFoundError as exc:
        name = str(getattr(exc, "name", "") or "")
        if name == module or module.startswith(name + ".") or name.startswith(module + "."):
            missing.append(module)
        else:
            broken.append({"module": module, "error": str(exc)})
    except Exception as exc:
        broken.append({"module": module, "error": str(exc)})

print(json.dumps({"missing": missing, "broken": broken, "versions": versions}))
sys.exit(0 if not missing and not broken else 2)
""";

  private static readonly IReadOnlyList<string> GemmaChatModules =
  [
    "torch",
    "torchvision",
    "transformers",
    "PIL",
    "accelerate",
    "huggingface_hub",
  ];

  private readonly string configuredPythonExecutablePath;

  public LocalPythonRuntimeDependencyProbe(string configuredPythonExecutablePath = "python")
  {
    this.configuredPythonExecutablePath = string.IsNullOrWhiteSpace(configuredPythonExecutablePath)
      ? "python"
      : configuredPythonExecutablePath.Trim();
  }

  public async Task<LocalChatRuntimeReadiness> CheckGemmaChatAsync(
    CancellationToken cancellationToken = default)
  {
    string pythonPath = ResolvePythonExecutablePath(configuredPythonExecutablePath);
    ProcessResult result;
    try
    {
      result = await RunProcessAsync(
          pythonPath,
          ["-c", DependencyProbeScript, .. GemmaChatModules],
          workingDirectory: AppContext.BaseDirectory,
          cancellationToken)
        .ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or DirectoryNotFoundException)
    {
      return new LocalChatRuntimeReadiness(
        false,
        "Local model runtime is missing. Run scripts\\setup-local-model-runtime.ps1, then retry.",
        GemmaChatModules,
        []);
    }

    DependencyProbePayload? payload = TryReadProbePayload(result.StandardOutput);
    if (payload is null)
    {
      string message = NormalizeWhitespace(string.IsNullOrWhiteSpace(result.StandardError)
        ? result.StandardOutput
        : result.StandardError);
      return new LocalChatRuntimeReadiness(
        false,
        string.IsNullOrWhiteSpace(message)
          ? "Local model runtime dependency check failed."
          : $"Local model runtime dependency check failed: {message}",
        [],
        []);
    }

    string[] missing = payload.Missing
      .Where(module => !string.IsNullOrWhiteSpace(module))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToArray();
    string[] broken = payload.Broken
      .Where(module => !string.IsNullOrWhiteSpace(module.Module))
      .Select(module => module.Module)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToArray();

    if (missing.Length == 0 && broken.Length == 0 && result.ExitCode == 0)
    {
      return LocalChatRuntimeReadiness.Ready;
    }

    string status = missing.Length > 0
      ? $"Local model runtime is missing {FormatModuleList(missing)}. Click Update Runtime or run scripts\\setup-local-model-runtime.ps1."
      : $"Local model runtime has a broken {FormatModuleList(broken)} install. Click Update Runtime or run scripts\\setup-local-model-runtime.ps1.";

    return new LocalChatRuntimeReadiness(false, status, missing, broken);
  }

  public async Task<LocalChatRuntimeReadiness> RepairGemmaChatAsync(
    IProgress<double>? progress = null,
    CancellationToken cancellationToken = default)
  {
    string requirementsPath = ResolveRequirementsPath();
    string pythonPath = await ResolveRepairTargetPythonAsync(progress, cancellationToken).ConfigureAwait(false);
    progress?.Report(0.35);

    await RunRequiredProcessAsync(
        pythonPath,
        BuildLockedInstallArguments(requirementsPath),
        "Failed to install local model runtime requirements.",
        cancellationToken)
      .ConfigureAwait(false);
    progress?.Report(0.9);

    LocalChatRuntimeReadiness readiness = await CheckGemmaChatAsync(cancellationToken).ConfigureAwait(false);
    progress?.Report(readiness.IsReady ? 1.0 : 0.0);
    return readiness;
  }

  private static async Task<string> ResolveRepairTargetPythonAsync(
    IProgress<double>? progress,
    CancellationToken cancellationToken)
  {
    string? environmentOverride = Environment.GetEnvironmentVariable("DICTATEANYWHERE_LOCAL_MODEL_PYTHON");
    if (!string.IsNullOrWhiteSpace(environmentOverride))
    {
      progress?.Report(0.2);
      return environmentOverride.Trim();
    }

    return await EnsureLocalRuntimeAsync(progress, cancellationToken).ConfigureAwait(false);
  }

  private static async Task<string> EnsureLocalRuntimeAsync(
    IProgress<double>? progress,
    CancellationToken cancellationToken)
  {
    string pythonPath = GetLocalRuntimePythonPath();
    if (File.Exists(pythonPath))
    {
      progress?.Report(0.2);
      return pythonPath;
    }

    Directory.CreateDirectory(GetLocalRuntimeRootPath());
    Exception? lastFailure = null;
    foreach (PythonLauncher launcher in ResolvePythonLaunchers())
    {
      List<string> arguments = [.. launcher.Arguments, "-m", "venv", GetLocalRuntimeVirtualEnvironmentPath()];
      try
      {
        await RunRequiredProcessAsync(
            launcher.Command,
            arguments,
            "Failed to create the local model runtime.",
            cancellationToken)
          .ConfigureAwait(false);
        lastFailure = null;
        break;
      }
      catch (InvalidOperationException ex)
      {
        lastFailure = ex;
      }
    }

    if (lastFailure is not null)
    {
      throw lastFailure;
    }

    if (!File.Exists(pythonPath))
    {
      throw new InvalidOperationException("Local model runtime was created but python.exe was not found.");
    }

    progress?.Report(0.25);
    return pythonPath;
  }

  private static IReadOnlyList<PythonLauncher> ResolvePythonLaunchers()
  {
    string? environmentOverride = Environment.GetEnvironmentVariable("DICTATEANYWHERE_BASE_PYTHON");
    if (!string.IsNullOrWhiteSpace(environmentOverride))
    {
      return [new PythonLauncher(environmentOverride.Trim(), [])];
    }

    return
    [
      new PythonLauncher("py", ["-3.11"]),
      new PythonLauncher("python", []),
    ];
  }

  private static string ResolvePythonExecutablePath(string configuredPath)
  {
    string? environmentOverride = Environment.GetEnvironmentVariable("DICTATEANYWHERE_LOCAL_MODEL_PYTHON");
    if (!string.IsNullOrWhiteSpace(environmentOverride))
    {
      return environmentOverride.Trim();
    }

    string localRuntimePath = GetLocalRuntimePythonPath();
    return File.Exists(localRuntimePath)
      ? localRuntimePath
      : configuredPath;
  }

  internal static IReadOnlyList<string> BuildLockedInstallArguments(string requirementsPath) =>
  [
    "-m",
    "pip",
    "install",
    "--require-hashes",
    "--no-deps",
    "--no-build-isolation",
    "-r",
    requirementsPath,
  ];

  internal static string ResolveRequirementsPath()
  {
    string deployedPath = Path.Combine(AppContext.BaseDirectory, "local-models", "requirements-lock.txt");
    if (File.Exists(deployedPath))
    {
      return deployedPath;
    }

    string repoPath = Path.GetFullPath(
      Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "local-models", "requirements-lock.txt"));
    if (File.Exists(repoPath))
    {
      return repoPath;
    }

    throw new InvalidOperationException("Bundled local-model requirements file was not found.");
  }

  private static string GetLocalRuntimeRootPath()
  {
    return Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DictateAnywhere",
      "local-model-runtime");
  }

  private static string GetLocalRuntimeVirtualEnvironmentPath()
  {
    return Path.Combine(GetLocalRuntimeRootPath(), ".venv");
  }

  private static string GetLocalRuntimePythonPath()
  {
    return Path.Combine(GetLocalRuntimeVirtualEnvironmentPath(), "Scripts", "python.exe");
  }

  private static async Task RunRequiredProcessAsync(
    string fileName,
    IReadOnlyList<string> arguments,
    string failureMessage,
    CancellationToken cancellationToken)
  {
    ProcessResult result;
    try
    {
      result = await RunProcessAsync(
          fileName,
          arguments,
          workingDirectory: AppContext.BaseDirectory,
          cancellationToken)
        .ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or DirectoryNotFoundException)
    {
      throw new InvalidOperationException($"{failureMessage} {ex.Message}", ex);
    }

    if (result.ExitCode != 0)
    {
      string output = NormalizeWhitespace(string.IsNullOrWhiteSpace(result.StandardError)
        ? result.StandardOutput
        : result.StandardError);
      throw new InvalidOperationException(string.IsNullOrWhiteSpace(output)
        ? failureMessage
        : $"{failureMessage} {output}");
    }
  }

  private static async Task<ProcessResult> RunProcessAsync(
    string fileName,
    IReadOnlyList<string> arguments,
    string workingDirectory,
    CancellationToken cancellationToken)
  {
    ProcessStartInfo startInfo = new()
    {
      FileName = fileName,
      WorkingDirectory = workingDirectory,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true,
      StandardOutputEncoding = Encoding.UTF8,
      StandardErrorEncoding = Encoding.UTF8,
    };

    foreach (string argument in arguments)
    {
      startInfo.ArgumentList.Add(argument);
    }

    using Process process = new()
    {
      StartInfo = startInfo,
      EnableRaisingEvents = true,
    };

    if (!process.Start())
    {
      throw new InvalidOperationException(
        string.Create(
          CultureInfo.InvariantCulture,
          $"Failed to start process '{fileName}'."));
    }

    Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
    Task<string> stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

    try
    {
      await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      TryTerminateProcess(process);
      throw;
    }

    return new ProcessResult(
      process.ExitCode,
      await stdOutTask.ConfigureAwait(false),
      await stdErrTask.ConfigureAwait(false));
  }

  private static DependencyProbePayload? TryReadProbePayload(string standardOutput)
  {
    string? line = standardOutput
      .Split([ '\r', '\n' ], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
      .LastOrDefault();
    if (string.IsNullOrWhiteSpace(line))
    {
      return null;
    }

    try
    {
      return JsonSerializer.Deserialize<DependencyProbePayload>(
        line,
        new JsonSerializerOptions
        {
          PropertyNameCaseInsensitive = true,
        });
    }
    catch (JsonException)
    {
      return null;
    }
  }

  private static string FormatModuleList(IReadOnlyList<string> modules)
  {
    string[] names = modules
      .Select(ToDisplayName)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToArray();
    return names.Length switch
    {
      0 => "required Python packages",
      1 => names[0],
      2 => $"{names[0]} and {names[1]}",
      _ => $"{string.Join(", ", names.Take(names.Length - 1))}, and {names[^1]}",
    };
  }

  private static string ToDisplayName(string module)
  {
    return module.ToLowerInvariant() switch
    {
      "pil" => "Pillow",
      "torch" => "PyTorch",
      "torchvision" => "Torchvision",
      "transformers" => "Transformers",
      "accelerate" => "Accelerate",
      "huggingface_hub" => "Hugging Face Hub",
      _ => module,
    };
  }

  private static string NormalizeWhitespace(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return string.Empty;
    }

    string[] parts = value
      .Split([ '\r', '\n', '\t' ], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return string.Join(" ", parts);
  }

  private static void TryTerminateProcess(Process process)
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

  private sealed record PythonLauncher(string Command, IReadOnlyList<string> Arguments);

  private sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

  private sealed record DependencyProbePayload(
    IReadOnlyList<string> Missing,
    IReadOnlyList<BrokenDependencyPayload> Broken);

  private sealed record BrokenDependencyPayload(
    string Module,
    string Error);
}

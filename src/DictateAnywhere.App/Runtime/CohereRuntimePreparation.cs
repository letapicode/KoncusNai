using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;
using DictateAnywhere.Models;

namespace DictateAnywhere.App.Runtime;

/// <summary>Only explicit model preparation runs downloads/conversion/calibration.</summary>
internal static class CohereRuntimePreparation
{
  internal static FileStream AcquireIdleWorkerLease()
  {
    Directory.CreateDirectory(CohereRuntimePresentation.Root);
    FileStream lease = File.Open(Path.Combine(CohereRuntimePresentation.Root, "inference.lock"),
      FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
    try { lease.Lock(0, 1); return lease; }
    catch (IOException)
    {
      lease.Dispose();
      throw new InvalidOperationException("Another dictation worker is active. Close other Koncus Nai instances before deleting this model.");
    }
  }

  internal static void DeleteConvertedCache(string cacheRoot, string modelDirectory)
  {
    string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
      Encoding.UTF8.GetBytes(Path.GetFullPath(modelDirectory).ToLowerInvariant()))).ToLowerInvariant()[..24];
    string root = Path.GetFullPath(cacheRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    string target = Path.GetFullPath(Path.Combine(root, key));
    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Converted model cache escaped its runtime directory.");
    if (Directory.Exists(target))
    {
      if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
        throw new InvalidOperationException("Converted model cache contains an unsupported reparse point.");
      Directory.Delete(target, recursive: true);
    }
  }

  internal static bool Supports(TranscriptionModelSelection selection) =>
    selection.ProviderId == TranscriptionProviderIds.CohereLocal && selection.ModelId == "cohere-transcribe-03-2026";

  internal static string ResolveModelPath(TranscriptionModelSelection selection)
  {
    foreach (string? root in new[] { ModelManagerOptions.Default.ModelsRootPath, ModelManagerOptions.Default.BundledModelsRootPath })
    {
      if (root is null) continue;
      string path = Path.Combine(root, selection.ProviderId, selection.ModelId);
      if (File.Exists(Path.Combine(path, "model.safetensors"))) return path;
    }
    throw new InvalidOperationException("Download and verify the selected Cohere model before preparing acceleration.");
  }

  internal static async Task PrepareAsync(TranscriptionModelSelection selection, IProgress<string>? progress,
    bool recalibrate, CancellationToken cancellationToken)
  {
    if (!Supports(selection)) return;
    string script = LocalModelScriptPathResolver.Resolve("prepare_cohere_runtime.py");
    string python = Environment.GetEnvironmentVariable("DICTATEANYWHERE_LOCAL_MODEL_PYTHON")
      ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DictateAnywhere", "local-model-runtime", ".venv", "Scripts", "python.exe");
    if (!File.Exists(python))
      throw new InvalidOperationException("Prepare the local Python model runtime first, then retry acceleration setup.");
    ProcessStartInfo startInfo = new(python)
    {
      UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
      StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
    };
    foreach (string argument in new[] { "-u", script, "--model-dir", ResolveModelPath(selection) })
      startInfo.ArgumentList.Add(argument);
    if (recalibrate) startInfo.ArgumentList.Add("--recalibrate");
    await RunPreparationProcessAsync(startInfo, progress, cancellationToken).ConfigureAwait(false);
  }

  internal static async Task EnsurePythonAsync(IProgress<string>? progress, CancellationToken cancellationToken)
  {
    string? customPython = Environment.GetEnvironmentVariable("DICTATEANYWHERE_LOCAL_MODEL_PYTHON");
    if (!string.IsNullOrWhiteSpace(customPython))
    {
      if (!File.Exists(customPython)) throw new InvalidOperationException("The configured Python runtime does not exist. Correct DICTATEANYWHERE_LOCAL_MODEL_PYTHON and retry.");
      return;
    }
    string setup = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(LocalModelScriptPathResolver.Resolve("prepare_cohere_runtime.py"))!,
      "..", "prepare-cohere-python-runtime.ps1"));
    if (!File.Exists(setup)) throw new InvalidOperationException("The Python preparation component is missing. Repair the installation.");
    ProcessStartInfo startInfo = new("powershell.exe")
    {
      UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
      StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
    };
    foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", setup })
      startInfo.ArgumentList.Add(argument);
    await RunPreparationProcessAsync(startInfo, progress, cancellationToken).ConfigureAwait(false);
  }

  private static async Task RunPreparationProcessAsync(ProcessStartInfo startInfo, IProgress<string>? progress,
    CancellationToken cancellationToken)
  {
    using Process process = new() { StartInfo = startInfo };
    cancellationToken.ThrowIfCancellationRequested();
    if (!process.Start()) throw new InvalidOperationException("Cannot start local acceleration preparation.");
    using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    deadline.CancelAfter(TimeSpan.FromMinutes(35));
    string lastMessage = "Acceleration preparation failed. Retry preparation; the original provider remains available.";
    async Task ReadProgressAsync()
    {
      while (await process.StandardOutput.ReadLineAsync(deadline.Token).ConfigureAwait(false) is { } line)
      {
        if (line.StartsWith("Preparation failed:", StringComparison.Ordinal)) lastMessage = line;
        if (line.Length <= 500) progress?.Report(line);
      }
    }
    // Drain stderr without retaining conversion logs/model paths in settings state.
    async Task DrainErrorsAsync()
    {
      while (await process.StandardError.ReadLineAsync(deadline.Token).ConfigureAwait(false) is not null) { }
    }
    Task output = ReadProgressAsync();
    Task errors = DrainErrorsAsync();
    try
    {
      await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
      await Task.WhenAll(output, errors).ConfigureAwait(false);
      if (process.ExitCode != 0) throw new InvalidOperationException(lastMessage);
    }
    finally
    {
      if (!process.HasExited) process.Kill(entireProcessTree: true);
      await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
      try { await Task.WhenAll(output, errors).ConfigureAwait(false); }
      catch (OperationCanceledException) { }
    }
  }
}

/// <summary>Wraps the existing verified/authenticated model manager; failures retain the original provider.</summary>
internal sealed class AutomaticDictationModelManager : IModelManager
{
  private readonly IModelManager inner;
  internal Func<Func<Task>, CancellationToken, Task> PreparationCoordinator { get; set; }
  internal event EventHandler<string>? PreparationProgress;
  internal string? LastPreparationMessage { get; private set; }

  internal AutomaticDictationModelManager(IModelManager inner, Func<Func<Task>, CancellationToken, Task>? prepareExclusively = null)
  {
    this.inner = inner;
    PreparationCoordinator = prepareExclusively ?? PrepareWithoutSessionAsync;
  }

  private static async Task PrepareWithoutSessionAsync(Func<Task> action, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    using IDisposable captureReservation = ExclusiveAudioCaptureService.ReserveForRuntimePreparation();
    await action().ConfigureAwait(false);
  }

  public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default) => inner.GetModelsAsync(cancellationToken);
  public Task<ModelInfo?> GetActiveModelAsync(string providerId, CancellationToken cancellationToken = default) => inner.GetActiveModelAsync(providerId, cancellationToken);
  public Task SetActiveModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default) => inner.SetActiveModelAsync(selection, cancellationToken);
  public async Task DeleteModelAsync(TranscriptionModelSelection selection, CancellationToken cancellationToken = default)
  {
    if (!CohereRuntimePreparation.Supports(selection))
    {
      await inner.DeleteModelAsync(selection, cancellationToken).ConfigureAwait(false);
      return;
    }
    await PreparationCoordinator(async () =>
    {
      using FileStream lease = CohereRuntimePreparation.AcquireIdleWorkerLease();
      await inner.DeleteModelAsync(selection, cancellationToken).ConfigureAwait(false);
      CohereRuntimePreparation.DeleteConvertedCache(CohereRuntimePresentation.Root,
        Path.Combine(ModelManagerOptions.Default.ModelsRootPath, selection.ProviderId, selection.ModelId));
    }, cancellationToken).ConfigureAwait(false);
  }

  public async Task DownloadModelAsync(TranscriptionModelSelection selection, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
  {
    if (!CohereRuntimePreparation.Supports(selection))
    {
      await inner.DownloadModelAsync(selection, progress, cancellationToken).ConfigureAwait(false);
      return;
    }
    await PreparationCoordinator(async () =>
    {
      PreparationReporter reporter = new(message => Report(message, null));
      await CohereRuntimePreparation.EnsurePythonAsync(reporter, cancellationToken).ConfigureAwait(false);
      await inner.DownloadModelAsync(selection, progress, cancellationToken).ConfigureAwait(false);
      try { await CohereRuntimePreparation.PrepareAsync(selection, reporter, false, cancellationToken).ConfigureAwait(false); }
      catch (InvalidOperationException ex)
      {
        Report($"Acceleration unavailable: {ex.Message} Original runtime retained.", null);
      }
    }, cancellationToken).ConfigureAwait(false);
  }

  internal Task PrepareAsync(TranscriptionModelSelection selection, IProgress<string>? progress,
    bool recalibrate, CancellationToken cancellationToken) =>
    PreparationCoordinator(async () =>
    {
      PreparationReporter reporter = new(message => Report(message, progress));
      await CohereRuntimePreparation.EnsurePythonAsync(reporter, cancellationToken).ConfigureAwait(false);
      await CohereRuntimePreparation.PrepareAsync(selection, reporter, recalibrate, cancellationToken).ConfigureAwait(false);
    }, cancellationToken);

  private void Report(string message, IProgress<string>? progress)
  {
    LastPreparationMessage = message;
    progress?.Report(message);
    PreparationProgress?.Invoke(this, message);
  }

  private sealed class PreparationReporter(Action<string> report) : IProgress<string>
  {
    public void Report(string value) => report(value);
  }
}

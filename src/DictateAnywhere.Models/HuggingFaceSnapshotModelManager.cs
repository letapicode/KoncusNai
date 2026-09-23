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
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Models;

public sealed class HuggingFaceSnapshotModelManager : IProviderModelManager
{
  internal const string ProvenanceMarkerFileName = ".notype-provenance.json";
  private readonly string providerId;
  private readonly IReadOnlyList<RepositoryModelManifestEntry> entries;
  private readonly IReadOnlyDictionary<string, RepositoryModelManifestEntry> entriesByModelId;
  private readonly ModelManagerOptions options;
  private readonly string pythonExecutablePath;

  public HuggingFaceSnapshotModelManager(
    string providerId,
    IEnumerable<RepositoryModelManifestEntry> entries,
    ModelManagerOptions options,
    string pythonExecutablePath = "python")
  {
    if (string.IsNullOrWhiteSpace(providerId))
    {
      throw new ArgumentException("Provider id must not be empty.", nameof(providerId));
    }

    ArgumentNullException.ThrowIfNull(entries);
    this.options = options ?? throw new ArgumentNullException(nameof(options));

    if (string.IsNullOrWhiteSpace(this.options.ModelsRootPath))
    {
      throw new ArgumentException("Models root path must not be empty.", nameof(options));
    }

    this.providerId = providerId.Trim().ToLowerInvariant();
    this.pythonExecutablePath = string.IsNullOrWhiteSpace(pythonExecutablePath)
      ? "python"
      : pythonExecutablePath.Trim();

    List<RepositoryModelManifestEntry> normalizedEntries = new();
    Dictionary<string, RepositoryModelManifestEntry> indexed = new(StringComparer.OrdinalIgnoreCase);
    foreach (RepositoryModelManifestEntry entry in entries)
    {
      ArgumentNullException.ThrowIfNull(entry);

      if (string.IsNullOrWhiteSpace(entry.ModelId))
      {
        throw new ModelManagementException("Repository model manifest entry id cannot be empty.");
      }

      if (string.IsNullOrWhiteSpace(entry.DisplayName))
      {
        throw new ModelManagementException($"Repository model '{entry.ModelId}' display name cannot be empty.");
      }

      if (string.IsNullOrWhiteSpace(entry.RepositoryId))
      {
        throw new ModelManagementException($"Repository model '{entry.ModelId}' repository id cannot be empty.");
      }

      if (entry.SupportedLanguages is null || entry.SupportedLanguages.Count == 0)
      {
        throw new ModelManagementException($"Repository model '{entry.ModelId}' must declare supported languages.");
      }

      if (!indexed.TryAdd(entry.ModelId, entry))
      {
        throw new ModelManagementException($"Duplicate repository model id '{entry.ModelId}'.");
      }

      ValidateAuxiliaryRepositories(entry);
      ValidateImmutableRevision(entry.Revision, entry.ModelId);
      normalizedEntries.Add(entry);
    }

    if (normalizedEntries.Count == 0)
    {
      throw new ModelManagementException("At least one repository model manifest entry is required.");
    }

    this.entries = normalizedEntries;
    entriesByModelId = indexed;
  }

  public string ProviderId => providerId;

  public async Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default)
  {
    string? activeModelId = await ReadActiveModelIdAsync(cancellationToken).ConfigureAwait(false);
    List<ModelInfo> models = new(entries.Count);

    foreach (RepositoryModelManifestEntry entry in entries)
    {
      bool installed = IsInstalled(entry);
      bool isActive = installed && string.Equals(activeModelId, entry.ModelId, StringComparison.OrdinalIgnoreCase);
      models.Add(new ModelInfo(providerId, entry.ModelId, entry.DisplayName, installed, isActive, entry.SupportedLanguages));
    }

    return models;
  }

  public async Task<ModelInfo?> GetActiveModelAsync(CancellationToken cancellationToken = default)
  {
    string? activeModelId = await ReadActiveModelIdAsync(cancellationToken).ConfigureAwait(false);
    if (string.IsNullOrWhiteSpace(activeModelId)
        || !entriesByModelId.TryGetValue(activeModelId, out RepositoryModelManifestEntry? entry)
        || entry is null
        || !IsInstalled(entry))
    {
      return null;
    }

    return new ModelInfo(providerId, entry.ModelId, entry.DisplayName, true, true, entry.SupportedLanguages);
  }

  public async Task SetActiveModelAsync(string modelId, CancellationToken cancellationToken = default)
  {
    RepositoryModelManifestEntry entry = ResolveEntry(modelId);
    if (!IsInstalled(entry))
    {
      throw new ModelManagementException(
        string.Format(
          CultureInfo.InvariantCulture,
          "Model '{0}' is not installed. Download it before activation.",
          entry.ModelId));
    }

    string statePath = GetActiveModelStatePath();
    string stateDirectory = Path.GetDirectoryName(statePath)
      ?? throw new ModelManagementException("Model state path is invalid.");
    Directory.CreateDirectory(stateDirectory);

    string tempPath = statePath + ".tmp";
    await File.WriteAllTextAsync(tempPath, entry.ModelId, cancellationToken).ConfigureAwait(false);
    File.Move(tempPath, statePath, overwrite: true);
  }

  public async Task DownloadModelAsync(
    string modelId,
    IProgress<double>? progress = null,
    CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    RepositoryModelManifestEntry entry = ResolveEntry(modelId);
    string destinationPath = GetModelPath(entry);
    string tempPath = destinationPath + options.PartialDownloadExtension;
    string providerRootPath = GetProviderRootPath();

    Directory.CreateDirectory(providerRootPath);
    TryDeleteDirectory(tempPath);
    progress?.Report(0.0);

    try
    {
      await RunSnapshotDownloadAsync(entry, tempPath, cancellationToken).ConfigureAwait(false);
      int completedSnapshots = 1;
      int totalSnapshots = 1 + (entry.AuxiliaryRepositories?.Count ?? 0);
      progress?.Report((double)completedSnapshots / totalSnapshots);

      foreach (RepositoryModelAuxiliaryEntry auxiliary in entry.AuxiliaryRepositories ?? Array.Empty<RepositoryModelAuxiliaryEntry>())
      {
        string auxiliaryPath = GetAuxiliaryModelPath(tempPath, auxiliary);
        RepositoryModelManifestEntry auxiliarySnapshot = CreateAuxiliarySnapshotEntry(entry, auxiliary);
        await RunSnapshotDownloadAsync(auxiliarySnapshot, auxiliaryPath, cancellationToken).ConfigureAwait(false);
        completedSnapshots++;
        progress?.Report((double)completedSnapshots / totalSnapshots);
      }

      cancellationToken.ThrowIfCancellationRequested();
      PromoteSnapshotDirectory(tempPath, destinationPath);
    }
    catch
    {
      TryDeleteDirectory(tempPath);
      throw;
    }

    progress?.Report(1.0);
  }

  public async Task DeleteModelAsync(string modelId, CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();

    RepositoryModelManifestEntry entry = ResolveEntry(modelId);
    TryDeleteDirectory(GetModelPath(entry));
    TryDeleteDirectory(GetModelPath(entry) + options.PartialDownloadExtension);

    string? activeModelId = await ReadActiveModelIdAsync(cancellationToken).ConfigureAwait(false);
    if (string.Equals(activeModelId, entry.ModelId, StringComparison.OrdinalIgnoreCase))
    {
      TryDeleteFile(GetActiveModelStatePath());
    }
  }

  private RepositoryModelManifestEntry ResolveEntry(string modelId)
  {
    if (!entriesByModelId.TryGetValue(modelId ?? string.Empty, out RepositoryModelManifestEntry? entry)
        || entry is null)
    {
      throw new ModelManagementException($"Unsupported model id '{modelId}'.");
    }

    return entry;
  }

  private bool IsInstalled(RepositoryModelManifestEntry entry)
  {
    string modelPath = GetModelPath(entry);
    string configPath = Path.Combine(modelPath, "config.json");
    if (!Directory.Exists(modelPath)
        || !File.Exists(configPath)
        || !IsNonEmptyFile(configPath)
        || !HasMatchingProvenanceMarker(modelPath, entry.ModelId, entry.RepositoryId, entry.Revision!))
    {
      return false;
    }

    foreach (RepositoryModelAuxiliaryEntry auxiliary in entry.AuxiliaryRepositories ?? Array.Empty<RepositoryModelAuxiliaryEntry>())
    {
      string auxiliaryModelPath = GetAuxiliaryModelPath(modelPath, auxiliary);
      string requiredFilePath = Path.Combine(auxiliaryModelPath, auxiliary.RequiredFileName);
      if (!Directory.Exists(auxiliaryModelPath)
          || !File.Exists(requiredFilePath)
          || !IsNonEmptyFile(requiredFilePath)
          || !HasMatchingProvenanceMarker(
            auxiliaryModelPath,
            $"{entry.ModelId}:{NormalizeAuxiliaryDirectoryName(auxiliary.DirectoryName)}",
            auxiliary.RepositoryId,
            auxiliary.Revision!))
      {
        return false;
      }
    }

    return true;
  }

  private static bool IsNonEmptyFile(string path)
  {
    try
    {
      return new FileInfo(path).Length > 0;
    }
    catch (IOException)
    {
      return false;
    }
    catch (UnauthorizedAccessException)
    {
      return false;
    }
  }

  private static bool HasMatchingProvenanceMarker(
    string modelPath,
    string artifactId,
    string repository,
    string revision)
  {
    try
    {
      string markerPath = Path.Combine(modelPath, ProvenanceMarkerFileName);
      using JsonDocument marker = JsonDocument.Parse(File.ReadAllText(markerPath));
      JsonElement root = marker.RootElement;
      return root.TryGetProperty("schemaVersion", out JsonElement schemaVersion)
        && schemaVersion.GetInt32() == 1
        && root.TryGetProperty("artifactId", out JsonElement markerArtifactId)
        && string.Equals(markerArtifactId.GetString(), artifactId, StringComparison.OrdinalIgnoreCase)
        && root.TryGetProperty("repository", out JsonElement markerRepository)
        && string.Equals(markerRepository.GetString(), repository, StringComparison.Ordinal)
        && root.TryGetProperty("revision", out JsonElement markerRevision)
        && string.Equals(markerRevision.GetString(), revision, StringComparison.OrdinalIgnoreCase);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException or OverflowException)
    {
      return false;
    }
  }

  private static void ValidateImmutableRevision(string? revision, string identity)
  {
    if (revision is null
        || revision.Length != 40
        || revision.Any(character => !Uri.IsHexDigit(character)))
    {
      throw new ModelManagementException($"Model '{identity}' must use a full immutable repository revision.");
    }
  }

  private string GetProviderRootPath()
  {
    return Path.Combine(options.ModelsRootPath, providerId);
  }

  private string GetModelPath(RepositoryModelManifestEntry entry)
  {
    return Path.Combine(GetProviderRootPath(), entry.ModelId);
  }

  private static string GetAuxiliaryModelPath(string modelPath, RepositoryModelAuxiliaryEntry auxiliary)
  {
    return Path.Combine(modelPath, "auxiliary", NormalizeAuxiliaryDirectoryName(auxiliary.DirectoryName));
  }

  private string GetActiveModelStatePath()
  {
    return Path.Combine(GetProviderRootPath(), options.ActiveModelStateFileName);
  }

  private async Task<string?> ReadActiveModelIdAsync(CancellationToken cancellationToken)
  {
    string statePath = GetActiveModelStatePath();
    if (!File.Exists(statePath))
    {
      return null;
    }

    string value = (await File.ReadAllTextAsync(statePath, cancellationToken).ConfigureAwait(false)).Trim();
    return string.IsNullOrWhiteSpace(value) ? null : value;
  }

  private async Task RunSnapshotDownloadAsync(
    RepositoryModelManifestEntry entry,
    string destinationPath,
    CancellationToken cancellationToken)
  {
    string scriptPath = ResolveBundledScriptPath("download_snapshot.py");
    string provenancePath = ResolveBundledScriptPath("model-snapshot-provenance.json");
    string arguments = BuildSnapshotDownloadArguments(scriptPath, provenancePath, entry, destinationPath);

    ProcessStartInfo startInfo = new()
    {
      FileName = ResolvePythonExecutablePath(pythonExecutablePath),
      Arguments = arguments,
      WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? AppContext.BaseDirectory,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true,
      StandardOutputEncoding = Encoding.UTF8,
      StandardErrorEncoding = Encoding.UTF8,
    };

    using Process process = new()
    {
      StartInfo = startInfo,
      EnableRaisingEvents = true,
    };

    try
    {
      if (!process.Start())
      {
        throw new ModelManagementException($"Failed to start the Hugging Face download runtime for '{entry.ModelId}'.");
      }
    }
    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or FileNotFoundException or DirectoryNotFoundException)
    {
      throw new ModelManagementException(
        $"Unable to start the Hugging Face download runtime for '{entry.ModelId}'. {ex.Message}",
        ex);
    }

    Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync();
    Task<string> stdErrTask = process.StandardError.ReadToEndAsync();

    try
    {
      await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      await TryTerminateProcessAsync(process).ConfigureAwait(false);
      try
      {
        await Task.WhenAll(stdOutTask, stdErrTask)
          .WaitAsync(TimeSpan.FromSeconds(5))
          .ConfigureAwait(false);
      }
      catch (Exception ex) when (ex is IOException or ObjectDisposedException or TimeoutException)
      {
        Debug.WriteLine($"Model download output cleanup did not complete cleanly: {ex.Message}");
      }
      throw;
    }

    string stdOut = await stdOutTask.ConfigureAwait(false);
    string stdErr = await stdErrTask.ConfigureAwait(false);

    if (process.ExitCode != 0)
    {
      string errorText = string.IsNullOrWhiteSpace(stdErr)
        ? stdOut.Trim()
        : stdErr.Trim();
      throw new ModelManagementException(
        BuildSnapshotDownloadFailureMessage(entry, errorText, process.ExitCode));
    }
  }

  internal static string BuildSnapshotDownloadArguments(
    string scriptPath,
    string provenancePath,
    RepositoryModelManifestEntry entry,
    string destinationPath)
  {
    StringBuilder builder = new();
    builder.Append('"').Append(scriptPath).Append('"');
    builder.Append(" --repo-id ").Append('"').Append(entry.RepositoryId).Append('"');
    builder.Append(" --destination ").Append('"').Append(destinationPath).Append('"');
    builder.Append(" --provenance-manifest ").Append('"').Append(provenancePath).Append('"');
    builder.Append(" --artifact-id ").Append('"').Append(entry.ModelId).Append('"');
    if (!string.IsNullOrWhiteSpace(entry.Revision))
    {
      builder.Append(" --revision ").Append('"').Append(entry.Revision).Append('"');
    }

    AppendPatternArguments(builder, "--allow-pattern", entry.AllowPatterns);
    AppendPatternArguments(builder, "--ignore-pattern", entry.IgnorePatterns);
    if (entry.RequiresAuthentication)
    {
      builder.Append(" --requires-auth");
    }

    return builder.ToString();
  }

  private static void ValidateAuxiliaryRepositories(RepositoryModelManifestEntry entry)
  {
    foreach (RepositoryModelAuxiliaryEntry auxiliary in entry.AuxiliaryRepositories ?? Array.Empty<RepositoryModelAuxiliaryEntry>())
    {
      ValidateImmutableRevision(auxiliary.Revision, $"{entry.ModelId}/{auxiliary.DirectoryName}");
      if (string.IsNullOrWhiteSpace(auxiliary.DirectoryName))
      {
        throw new ModelManagementException($"Auxiliary repository for '{entry.ModelId}' must declare a directory name.");
      }

      if (string.IsNullOrWhiteSpace(auxiliary.DisplayName))
      {
        throw new ModelManagementException(
          $"Auxiliary repository '{auxiliary.DirectoryName}' for '{entry.ModelId}' must declare a display name.");
      }

      if (string.IsNullOrWhiteSpace(auxiliary.RepositoryId))
      {
        throw new ModelManagementException(
          $"Auxiliary repository '{auxiliary.DirectoryName}' for '{entry.ModelId}' must declare a repository id.");
      }

      _ = NormalizeAuxiliaryDirectoryName(auxiliary.DirectoryName);
    }
  }

  private static RepositoryModelManifestEntry CreateAuxiliarySnapshotEntry(
    RepositoryModelManifestEntry entry,
    RepositoryModelAuxiliaryEntry auxiliary)
  {
    return new RepositoryModelManifestEntry(
      $"{entry.ModelId}:{NormalizeAuxiliaryDirectoryName(auxiliary.DirectoryName)}",
      auxiliary.DisplayName,
      auxiliary.RepositoryId,
      entry.SupportedLanguages,
      Revision: auxiliary.Revision,
      AllowPatterns: auxiliary.AllowPatterns,
      IgnorePatterns: auxiliary.IgnorePatterns,
      RequiresAuthentication: auxiliary.RequiresAuthentication);
  }

  private static string NormalizeAuxiliaryDirectoryName(string directoryName)
  {
    string normalized = (directoryName ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(normalized)
        || normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
        || normalized.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
        || normalized.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
    {
      throw new ModelManagementException($"Auxiliary repository directory name '{directoryName}' is invalid.");
    }

    return normalized;
  }

  internal static string BuildSnapshotDownloadFailureMessage(
    RepositoryModelManifestEntry entry,
    string? errorText,
    int exitCode)
  {
    string normalizedError = NormalizeSnapshotDownloadError(entry, errorText);
    return string.IsNullOrWhiteSpace(normalizedError)
      ? $"Snapshot download failed for '{entry.ModelId}' with exit code {exitCode.ToString(CultureInfo.InvariantCulture)}."
      : $"Snapshot download failed for '{entry.ModelId}': {normalizedError}";
  }

  internal static string NormalizeSnapshotDownloadError(
    RepositoryModelManifestEntry entry,
    string? errorText)
  {
    string normalized = NormalizeWhitespace(errorText);
    if (string.IsNullOrWhiteSpace(normalized))
    {
      return string.Empty;
    }

    if (LooksLikeGatedRepositoryFailure(normalized))
    {
      return string.Format(
        CultureInfo.InvariantCulture,
        "Access to Hugging Face repo '{0}' is gated. Accept the model access terms, then sign in for this Windows user with 'hf auth login' or set HF_TOKEN before retrying.",
        entry.RepositoryId);
    }

    if (LooksLikeInsufficientDiskFailure(normalized))
    {
      return "There is not enough free disk space to download and stage this model. Free space on the model drive, then retry.";
    }

    if (LooksLikeConnectionFailure(normalized))
    {
      return "Koncus Nai could not reach Hugging Face. Check the network connection, proxy, or firewall, then retry.";
    }

    return "The local model downloader reported an unexpected error. Retry, then review the local diagnostics if it continues.";
  }

  private static void AppendPatternArguments(
    StringBuilder builder,
    string argumentName,
    IReadOnlyList<string>? patterns)
  {
    if (patterns is null)
    {
      return;
    }

    foreach (string pattern in patterns)
    {
      if (string.IsNullOrWhiteSpace(pattern))
      {
        continue;
      }

      builder.Append(' ')
        .Append(argumentName)
        .Append(' ')
        .Append('"')
        .Append(pattern.Trim())
        .Append('"');
    }
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

  private static bool LooksLikeGatedRepositoryFailure(string message)
  {
    return message.Contains("Cannot access gated repo", StringComparison.OrdinalIgnoreCase)
      || message.Contains("Please set a HF_TOKEN", StringComparison.OrdinalIgnoreCase)
      || message.Contains("You must have access to it and be authenticated to access it", StringComparison.OrdinalIgnoreCase)
      || message.Contains("Access to model", StringComparison.OrdinalIgnoreCase)
      || message.Contains("is restricted", StringComparison.OrdinalIgnoreCase)
      || message.Contains("401 Client Error", StringComparison.OrdinalIgnoreCase)
      || message.Contains("403 Client Error", StringComparison.OrdinalIgnoreCase);
  }

  private static bool LooksLikeInsufficientDiskFailure(string message)
  {
    return message.Contains("No space left on device", StringComparison.OrdinalIgnoreCase)
      || message.Contains("disk full", StringComparison.OrdinalIgnoreCase)
      || message.Contains("not enough space on the disk", StringComparison.OrdinalIgnoreCase)
      || message.Contains("There is not enough space", StringComparison.OrdinalIgnoreCase)
      || message.Contains("ENOSPC", StringComparison.OrdinalIgnoreCase);
  }

  private static bool LooksLikeConnectionFailure(string message)
  {
    return message.Contains("ConnectionError", StringComparison.OrdinalIgnoreCase)
      || message.Contains("ConnectTimeout", StringComparison.OrdinalIgnoreCase)
      || message.Contains("ReadTimeout", StringComparison.OrdinalIgnoreCase)
      || message.Contains("NameResolutionError", StringComparison.OrdinalIgnoreCase)
      || message.Contains("Temporary failure in name resolution", StringComparison.OrdinalIgnoreCase)
      || message.Contains("Network is unreachable", StringComparison.OrdinalIgnoreCase);
  }

  private static string ResolveBundledScriptPath(string scriptFileName)
  {
    string deployedPath = Path.Combine(AppContext.BaseDirectory, "local-models", scriptFileName);
    if (File.Exists(deployedPath))
    {
      return deployedPath;
    }

    string repoPath = Path.GetFullPath(
      Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "local-models", scriptFileName));
    if (File.Exists(repoPath))
    {
      return repoPath;
    }

    throw new ModelManagementException(
      $"Bundled local-model script '{scriptFileName}' was not found in the application output.");
  }

  private static string ResolvePythonExecutablePath(string configuredPath)
  {
    string? environmentOverride = Environment.GetEnvironmentVariable("DICTATEANYWHERE_LOCAL_MODEL_PYTHON");
    if (!string.IsNullOrWhiteSpace(environmentOverride))
    {
      return environmentOverride.Trim();
    }

    string localRuntimePath = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DictateAnywhere",
      "local-model-runtime",
      ".venv",
      "Scripts",
      "python.exe");
    if (File.Exists(localRuntimePath))
    {
      return localRuntimePath;
    }

    return configuredPath;
  }

  private static async Task TryTerminateProcessAsync(Process process)
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
    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or TimeoutException)
    {
      Debug.WriteLine($"Model download process cleanup did not complete cleanly: {ex.Message}");
    }
  }

  internal static void PromoteSnapshotDirectory(string tempPath, string destinationPath)
  {
    string backupPath = destinationPath + ".backup-" + Guid.NewGuid().ToString("N");
    bool existingSnapshotMoved = false;

    try
    {
      if (Directory.Exists(destinationPath))
      {
        Directory.Move(destinationPath, backupPath);
        existingSnapshotMoved = true;
      }

      Directory.Move(tempPath, destinationPath);
      if (existingSnapshotMoved)
      {
        TryDeleteDirectory(backupPath);
      }
    }
    catch
    {
      if (existingSnapshotMoved && Directory.Exists(backupPath))
      {
        TryDeleteDirectory(destinationPath);
        Directory.Move(backupPath, destinationPath);
      }

      throw;
    }
  }

  private static void TryDeleteDirectory(string path)
  {
    if (!Directory.Exists(path))
    {
      return;
    }

    try
    {
      Directory.Delete(path, recursive: true);
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
  }

  private static void TryDeleteFile(string path)
  {
    if (!File.Exists(path))
    {
      return;
    }

    try
    {
      File.Delete(path);
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
  }
}

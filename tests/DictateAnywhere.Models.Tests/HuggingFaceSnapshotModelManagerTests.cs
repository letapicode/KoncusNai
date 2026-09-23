using System;
using DictateAnywhere.Models;

namespace DictateAnywhere.Models.Tests;

public sealed class HuggingFaceSnapshotModelManagerTests
{
  [Xunit.Fact]
  public void BuildSnapshotDownloadArguments_IncludesPatternFiltersAndAuthFlag()
  {
    RepositoryModelManifestEntry entry = new(
      "cohere-transcribe-03-2026",
      "Cohere Transcribe 03/2026",
      "CohereLabs/cohere-transcribe-03-2026",
      ["en"],
      Revision: "32d9e4ba6271d78168c095c2f90bc173eaad97d2",
      AllowPatterns: ["config.json", "*.safetensors"],
      IgnorePatterns: [".eval_results/**", "demo/**"],
      RequiresAuthentication: true);

    string arguments = HuggingFaceSnapshotModelManager.BuildSnapshotDownloadArguments(
      @"C:\scripts\download_snapshot.py",
      @"C:\scripts\model-snapshot-provenance.json",
      entry,
      @"C:\models\cohere");

    Xunit.Assert.Contains("--repo-id \"CohereLabs/cohere-transcribe-03-2026\"", arguments, StringComparison.Ordinal);
    Xunit.Assert.Contains("--destination \"C:\\models\\cohere\"", arguments, StringComparison.Ordinal);
    Xunit.Assert.Contains("--revision \"32d9e4ba6271d78168c095c2f90bc173eaad97d2\"", arguments, StringComparison.Ordinal);
    Xunit.Assert.Contains("--provenance-manifest \"C:\\scripts\\model-snapshot-provenance.json\"", arguments, StringComparison.Ordinal);
    Xunit.Assert.Contains("--artifact-id \"cohere-transcribe-03-2026\"", arguments, StringComparison.Ordinal);
    Xunit.Assert.Contains("--allow-pattern \"config.json\"", arguments, StringComparison.Ordinal);
    Xunit.Assert.Contains("--allow-pattern \"*.safetensors\"", arguments, StringComparison.Ordinal);
    Xunit.Assert.Contains("--ignore-pattern \".eval_results/**\"", arguments, StringComparison.Ordinal);
    Xunit.Assert.Contains("--ignore-pattern \"demo/**\"", arguments, StringComparison.Ordinal);
    Xunit.Assert.Contains("--requires-auth", arguments, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public void BuildSnapshotDownloadFailureMessage_NormalizesGatedRepositoryErrors()
  {
    RepositoryModelManifestEntry entry = new(
      "cohere-transcribe-03-2026",
      "Cohere Transcribe 03/2026",
      "CohereLabs/cohere-transcribe-03-2026",
      ["en"],
      Revision: "32d9e4ba6271d78168c095c2f90bc173eaad97d2",
      RequiresAuthentication: true);

    string message = HuggingFaceSnapshotModelManager.BuildSnapshotDownloadFailureMessage(
      entry,
      """
      Warning: You are sending unauthenticated requests to the HF Hub. Please set a HF_TOKEN.
      401 Client Error.
      Cannot access gated repo for url https://huggingface.co/CohereLabs/cohere-transcribe-03-2026/resolve/main/config.json.
      """,
      exitCode: 1);

    Xunit.Assert.Contains("Access to Hugging Face repo 'CohereLabs/cohere-transcribe-03-2026' is gated.", message, StringComparison.Ordinal);
    Xunit.Assert.Contains("hf auth login", message, StringComparison.Ordinal);
    Xunit.Assert.Contains("HF_TOKEN", message, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("401 Client Error", message, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("Cannot access gated repo for url", message, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Theory]
  [Xunit.InlineData("401 Client Error: token expired")]
  [Xunit.InlineData("403 Client Error: access denied")]
  public void BuildSnapshotDownloadFailureMessage_ExpiredOrDeniedCredentialsUseSafeAuthGuidance(string error)
  {
    RepositoryModelManifestEntry entry = CreateGatedTestEntry();

    string message = HuggingFaceSnapshotModelManager.BuildSnapshotDownloadFailureMessage(entry, error, 1);

    Xunit.Assert.Contains("hf auth login", message, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain(error, message, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public void BuildSnapshotDownloadFailureMessage_OfflineFailureUsesSafeRetryGuidance()
  {
    RepositoryModelManifestEntry entry = CreateGatedTestEntry();

    string message = HuggingFaceSnapshotModelManager.BuildSnapshotDownloadFailureMessage(
      entry,
      "ConnectionError: Network is unreachable at a private proxy path",
      1);

    Xunit.Assert.Contains("could not reach Hugging Face", message, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("private proxy path", message, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public void BuildSnapshotDownloadFailureMessage_InsufficientDiskUsesSafeRecoveryGuidance()
  {
    RepositoryModelManifestEntry entry = CreateGatedTestEntry();

    string message = HuggingFaceSnapshotModelManager.BuildSnapshotDownloadFailureMessage(
      entry,
      "OSError: [Errno 28] No space left on device C:\\Users\\private",
      1);

    Xunit.Assert.Contains("not enough free disk space", message, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("Users", message, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public void BuildSnapshotDownloadFailureMessage_UnexpectedFailureDoesNotExposeRawDownloaderOutput()
  {
    RepositoryModelManifestEntry entry = CreateGatedTestEntry();

    string message = HuggingFaceSnapshotModelManager.BuildSnapshotDownloadFailureMessage(
      entry,
      "unexpected failure containing private-local-path and credential material",
      1);

    Xunit.Assert.Contains("unexpected error", message, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.DoesNotContain("private-local-path", message, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public void Constructor_RejectsInvalidAuxiliaryDirectoryName()
  {
    RepositoryModelManifestEntry entry = new(
      "gemma-4-E2B-it-mtp",
      "Gemma 4 E2B IT MTP",
      "google/gemma-4-E2B-it",
      ["en"],
      Revision: "1111111111111111111111111111111111111111",
      AuxiliaryRepositories:
      [
        new RepositoryModelAuxiliaryEntry(
          @"bad\path",
          "Gemma assistant",
          "google/gemma-4-E2B-it-assistant",
          Revision: "2222222222222222222222222222222222222222"),
      ]);

    Xunit.Assert.Throws<ModelManagementException>(() =>
      new HuggingFaceSnapshotModelManager(
        "gemma-local",
        [entry],
        ModelManagerOptions.Default));
  }

  [Xunit.Fact]
  public async Task DownloadModelAsync_WhenAlreadyCanceled_DoesNotMutateExistingFiles()
  {
    string root = Path.Combine(Path.GetTempPath(), $"notype-model-cancel-{Guid.NewGuid():N}");
    try
    {
      RepositoryModelManifestEntry entry = CreateGatedTestEntry();
      string providerId = "test-provider";
      string destination = Path.Combine(root, providerId, entry.ModelId);
      Directory.CreateDirectory(destination);
      string marker = Path.Combine(destination, "keep.txt");
      await File.WriteAllTextAsync(marker, "existing");
      HuggingFaceSnapshotModelManager manager = new(
        providerId,
        [entry],
        ModelManagerOptions.Default with { ModelsRootPath = root });
      using CancellationTokenSource canceled = new();
      canceled.Cancel();

      await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
        manager.DownloadModelAsync(entry.ModelId, cancellationToken: canceled.Token));

      Xunit.Assert.Equal("existing", await File.ReadAllTextAsync(marker));
    }
    finally
    {
      if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
  }

  private static RepositoryModelManifestEntry CreateGatedTestEntry() => new(
    "cohere-transcribe-03-2026",
    "Cohere Transcribe 03/2026",
    "CohereLabs/cohere-transcribe-03-2026",
    ["en"],
    Revision: "32d9e4ba6271d78168c095c2f90bc173eaad97d2",
    RequiresAuthentication: true);

  [Xunit.Fact]
  public async Task GetModelsAsync_WhenConfigJsonIsZeroBytes_TreatsModelAsNotInstalled()
  {
    string tempModelsRoot = Path.Combine(Path.GetTempPath(), $"notype-models-{Guid.NewGuid():N}");
    try
    {
      string modelId = "test-model";
      string providerId = "test-provider";
      RepositoryModelManifestEntry entry = new(
        modelId,
      "Test Model",
      "org/test-model",
      ["en"],
      Revision: "1111111111111111111111111111111111111111");

      HuggingFaceSnapshotModelManager manager = new(
        providerId,
        [entry],
        ModelManagerOptions.Default with { ModelsRootPath = tempModelsRoot });

      // Initially not installed
      var models = await manager.GetModelsAsync();
      Xunit.Assert.False(models[0].IsInstalled);

      // Create model directory with a corrupt 0-byte config.json
      string modelDir = Path.Combine(tempModelsRoot, providerId, modelId);
      Directory.CreateDirectory(modelDir);
      File.WriteAllBytes(Path.Combine(modelDir, "config.json"), []);

      // Should still be reported as not installed because config.json is empty/corrupt
      models = await manager.GetModelsAsync();
      Xunit.Assert.False(models[0].IsInstalled);

      // A legacy or partial snapshot with only config.json is not trusted.
      File.WriteAllText(Path.Combine(modelDir, "config.json"), "{\"model_type\": \"test\"}");

      models = await manager.GetModelsAsync();
      Xunit.Assert.False(models[0].IsInstalled);

      File.WriteAllText(
        Path.Combine(modelDir, HuggingFaceSnapshotModelManager.ProvenanceMarkerFileName),
        """{"schemaVersion":1,"artifactId":"test-model","repository":"org/test-model","revision":"1111111111111111111111111111111111111111"}""");

      models = await manager.GetModelsAsync();
      Xunit.Assert.True(models[0].IsInstalled);

      File.WriteAllText(
        Path.Combine(modelDir, HuggingFaceSnapshotModelManager.ProvenanceMarkerFileName),
        """{"schemaVersion":1,"artifactId":"test-model","repository":"org/test-model","revision":"2222222222222222222222222222222222222222"}""");
      models = await manager.GetModelsAsync();
      Xunit.Assert.False(models[0].IsInstalled);

      File.WriteAllText(
        Path.Combine(modelDir, HuggingFaceSnapshotModelManager.ProvenanceMarkerFileName),
        """{"schemaVersion":"not-an-integer","artifactId":"test-model"}""");
      models = await manager.GetModelsAsync();
      Xunit.Assert.False(models[0].IsInstalled);
    }
    finally
    {
      if (Directory.Exists(tempModelsRoot))
      {
        Directory.Delete(tempModelsRoot, recursive: true);
      }
    }
  }

  [Xunit.Fact]
  public void PromoteSnapshotDirectory_ReplacesExistingSnapshotAndRemovesBackup()
  {
    string root = Path.Combine(Path.GetTempPath(), $"notype-model-promotion-{Guid.NewGuid():N}");
    string destination = Path.Combine(root, "model");
    string partial = destination + ".partial";
    try
    {
      Directory.CreateDirectory(destination);
      File.WriteAllText(Path.Combine(destination, "config.json"), "old");
      Directory.CreateDirectory(partial);
      File.WriteAllText(Path.Combine(partial, "config.json"), "new");

      HuggingFaceSnapshotModelManager.PromoteSnapshotDirectory(partial, destination);

      Xunit.Assert.Equal("new", File.ReadAllText(Path.Combine(destination, "config.json")));
      Xunit.Assert.False(Directory.Exists(partial));
      Xunit.Assert.Empty(Directory.GetDirectories(root, "model.backup-*"));
    }
    finally
    {
      if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
  }

  [Xunit.Fact]
  public void PromoteSnapshotDirectory_WhenNoSnapshotExists_PromotesWithoutCreatingBackup()
  {
    string root = Path.Combine(Path.GetTempPath(), $"notype-model-first-promotion-{Guid.NewGuid():N}");
    string destination = Path.Combine(root, "model");
    string partial = destination + ".partial";
    try
    {
      Directory.CreateDirectory(partial);
      File.WriteAllText(Path.Combine(partial, "config.json"), "first");

      HuggingFaceSnapshotModelManager.PromoteSnapshotDirectory(partial, destination);

      Xunit.Assert.Equal("first", File.ReadAllText(Path.Combine(destination, "config.json")));
      Xunit.Assert.False(Directory.Exists(partial));
      Xunit.Assert.Empty(Directory.GetDirectories(root, "model.backup-*"));
    }
    finally
    {
      if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
  }

  [Xunit.Fact]
  public void PromoteSnapshotDirectory_WhenPromotionFails_RestoresExistingSnapshot()
  {
    string root = Path.Combine(Path.GetTempPath(), $"notype-model-rollback-{Guid.NewGuid():N}");
    string destination = Path.Combine(root, "model");
    string missingPartial = destination + ".missing";
    try
    {
      Directory.CreateDirectory(destination);
      File.WriteAllText(Path.Combine(destination, "config.json"), "still-installed");

      Xunit.Assert.Throws<DirectoryNotFoundException>(() =>
        HuggingFaceSnapshotModelManager.PromoteSnapshotDirectory(missingPartial, destination));

      Xunit.Assert.Equal("still-installed", File.ReadAllText(Path.Combine(destination, "config.json")));
      Xunit.Assert.Empty(Directory.GetDirectories(root, "model.backup-*"));
    }
    finally
    {
      if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
  }
}

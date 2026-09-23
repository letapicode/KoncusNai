using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.App.Runtime;

internal static class LocalOllamaRuntimeReadiness
{
  private const string InstallerUrl = "https://ollama.com/download/OllamaSetup.exe";
  private const string OllamaOrganization = "O=Ollama Inc.";
  private static readonly HashSet<string> ApprovedInstallerDownloadHosts = new(StringComparer.OrdinalIgnoreCase)
  {
    "ollama.com",
    "release-assets.githubusercontent.com",
  };
  internal const string Gemma4E4BDigest = "c6eb396dbd5992bbe3f5cdb947e8bbc0ee413d7c17e2beaae69f5d569cf982eb";
  private static readonly HttpClient HttpClient = new() { BaseAddress = new Uri("http://127.0.0.1:11434/") };

  public static async Task<LocalChatRuntimeReadiness> CheckAsync(string modelId, CancellationToken cancellationToken = default)
  {
    try
    {
      using HttpResponseMessage response = await HttpClient.GetAsync("api/tags", cancellationToken).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        return Unavailable("Ollama is running but did not return its installed models.");
      }

      using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
      bool installed = IsPinnedModelPresent(document.RootElement, modelId);
      return installed
        ? LocalChatRuntimeReadiness.Ready with { StatusMessage = $"Ollama is ready with {modelId}." }
        : new LocalChatRuntimeReadiness(false, $"Ollama is ready. Pull {modelId} to start chatting.", [], []);
    }
    catch (HttpRequestException)
    {
      return Unavailable("Ollama is not running. Start Ollama, then pull Gemma 4 from this screen.");
    }
  }

  internal static bool IsPinnedModelPresent(JsonElement root, string modelId)
  {
    string? expectedDigest = string.Equals(modelId, "gemma4:e4b", StringComparison.OrdinalIgnoreCase)
      ? Gemma4E4BDigest
      : null;
    return expectedDigest is not null
      && root.TryGetProperty("models", out JsonElement models)
      && models.EnumerateArray().Any(model =>
        model.TryGetProperty("name", out JsonElement name)
        && string.Equals(name.GetString(), modelId, StringComparison.OrdinalIgnoreCase)
        && model.TryGetProperty("digest", out JsonElement digest)
        && string.Equals(digest.GetString(), expectedDigest, StringComparison.OrdinalIgnoreCase));
  }

  public static async Task<LocalChatRuntimeReadiness> PullAsync(
    string modelId,
    IProgress<double>? progress,
    CancellationToken cancellationToken = default)
  {
    progress?.Report(0);
    try
    {
      await EnsureInstalledAndRunningAsync(progress, cancellationToken).ConfigureAwait(false);
      progress?.Report(0.25);
      using StringContent content = new(JsonSerializer.Serialize(new { model = modelId, stream = false }), Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await HttpClient.PostAsync("api/pull", content, cancellationToken).ConfigureAwait(false);
      string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        throw new InvalidOperationException($"Ollama could not pull {modelId}: {ReadError(body)}");
      }

      progress?.Report(1);
      return await CheckAsync(modelId, cancellationToken).ConfigureAwait(false);
    }
    catch (HttpRequestException ex)
    {
      throw new InvalidOperationException("Could not reach Ollama. Install and start Ollama before pulling Gemma 4.", ex);
    }
  }

  /// <summary>
  /// Starts the local Ollama service only after the person explicitly asks to
  /// use it. This does not load or pull a model; Ollama loads the model on the
  /// first chat request.
  /// </summary>
  public static async Task<LocalChatRuntimeReadiness> StartAsync(
    string modelId,
    IProgress<double>? progress,
    CancellationToken cancellationToken = default)
  {
    progress?.Report(0);
    await EnsureInstalledAndRunningAsync(progress, cancellationToken).ConfigureAwait(false);
    progress?.Report(1);
    return await CheckAsync(modelId, cancellationToken).ConfigureAwait(false);
  }

  /// <summary>Installs the official, signed per-user Ollama runtime only when it is absent.</summary>
  private static async Task EnsureInstalledAndRunningAsync(
    IProgress<double>? progress,
    CancellationToken cancellationToken)
  {
    _ = OllamaStartupShortcutPolicy.TryDisableAutomaticStartup();
    if (await IsApiReachableAsync(cancellationToken).ConfigureAwait(false))
    {
      return;
    }

    HashSet<int> existingOllamaProcessIds = Process.GetProcessesByName("ollama")
      .Select(process =>
      {
        int id = process.Id;
        process.Dispose();
        return id;
      })
      .ToHashSet();

    string executablePath = GetInstalledExecutablePath();
    if (!File.Exists(executablePath))
    {
      progress?.Report(0.02);
      string tempDirectory = Path.Combine(Path.GetTempPath(), "Notype");
      string installerPath = Path.Combine(tempDirectory, "OllamaSetup.exe");
      Directory.CreateDirectory(tempDirectory);
      try
      {
        await DownloadInstallerAsync(installerPath, progress, cancellationToken).ConfigureAwait(false);
        await VerifyOfficialInstallerAsync(installerPath, cancellationToken).ConfigureAwait(false);
        CreateOllamaUpgradeMarker();
        await RunInstallerAsync(installerPath, cancellationToken).ConfigureAwait(false);
      }
      finally
      {
        if (File.Exists(installerPath))
        {
          File.Delete(installerPath);
        }
      }

      executablePath = GetInstalledExecutablePath();
      if (!File.Exists(executablePath))
      {
        throw new InvalidOperationException("Ollama installation completed but its executable was not found.");
      }

      _ = OllamaStartupShortcutPolicy.TryDisableAutomaticStartup();
    }

    if (await WaitForApiAsync(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false))
    {
      // The official installer can start Ollama itself. Record only a process that appeared
      // during this explicit Koncus Nai operation so a pre-existing user service is never owned.
      OllamaProcessOwnership.TrackNewProcess(existingOllamaProcessIds);
    }
    else
    {
      StartOllamaServer(executablePath);
      if (!await WaitForApiAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false))
      {
        throw new InvalidOperationException("Ollama was installed but its local service did not start.");
      }
    }
  }

  private static string GetInstalledExecutablePath() => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Programs",
    "Ollama",
    "ollama.exe");

  private static async Task DownloadInstallerAsync(
    string installerPath,
    IProgress<double>? progress,
    CancellationToken cancellationToken)
  {
    using HttpResponseMessage response = await HttpClient.GetAsync(
      InstallerUrl,
      HttpCompletionOption.ResponseHeadersRead,
      cancellationToken).ConfigureAwait(false);
    ValidateInstallerDownloadSource(response.RequestMessage?.RequestUri);
    response.EnsureSuccessStatusCode();
    long totalBytes = response.Content.Headers.ContentLength ?? -1;
    await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    await using FileStream destination = new(installerPath, FileMode.Create, FileAccess.Write, FileShare.None);
    byte[] buffer = new byte[81_920];
    long downloadedBytes = 0;
    int read;
    while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
    {
      await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
      downloadedBytes += read;
      if (totalBytes > 0)
      {
        // Reserve the latter three quarters for install, service startup, and model pull.
        progress?.Report(0.02 + (0.18 * downloadedBytes / totalBytes));
      }
    }
  }

  private static async Task VerifyOfficialInstallerAsync(string installerPath, CancellationToken cancellationToken)
  {
    using X509Certificate signingCertificate = X509Certificate.CreateFromSignedFile(installerPath);
    using X509Certificate2 certificate = new(signingCertificate);
    if (!certificate.Subject.Contains(OllamaOrganization, StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidOperationException("The downloaded Ollama installer was not signed by Ollama Inc.");
    }

    ProcessStartInfo startInfo = new()
    {
      FileName = "powershell.exe",
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };
    startInfo.ArgumentList.Add("-NoProfile");
    startInfo.ArgumentList.Add("-NonInteractive");
    startInfo.ArgumentList.Add("-Command");
    startInfo.ArgumentList.Add(
      "$signature = Get-AuthenticodeSignature -LiteralPath '" + installerPath.Replace("'", "''", StringComparison.Ordinal) +
      "'; if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|, )O=Ollama Inc\\.(,|$)') { exit 1 }");

    using Process process = Process.Start(startInfo)
      ?? throw new InvalidOperationException("Could not verify the Ollama installer signature.");
    await WaitForOwnedProcessAsync(
      process,
      TimeSpan.FromMinutes(2),
      "Ollama installer signature verification timed out.",
      cancellationToken).ConfigureAwait(false);
    if (process.ExitCode != 0)
    {
      throw new InvalidOperationException("The Ollama installer signature could not be validated.");
    }
  }

  private static async Task RunInstallerAsync(string installerPath, CancellationToken cancellationToken)
  {
    ProcessStartInfo startInfo = new()
    {
      FileName = installerPath,
      UseShellExecute = false,
      CreateNoWindow = true,
    };
    startInfo.ArgumentList.Add("/VERYSILENT");
    startInfo.ArgumentList.Add("/NORESTART");
    startInfo.ArgumentList.Add("/SUPPRESSMSGBOXES");
    using Process process = Process.Start(startInfo)
      ?? throw new InvalidOperationException("Could not start the Ollama installer.");
    await WaitForOwnedProcessAsync(
      process,
      TimeSpan.FromMinutes(15),
      "Ollama installation timed out.",
      cancellationToken).ConfigureAwait(false);
    if (process.ExitCode != 0)
    {
      throw new InvalidOperationException($"Ollama installation failed with exit code {process.ExitCode}.");
    }
  }

  internal static void ValidateInstallerDownloadSource(Uri? finalUri)
  {
    if (finalUri is null
        || !string.Equals(finalUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        || !ApprovedInstallerDownloadHosts.Contains(finalUri.IdnHost)
        || !string.IsNullOrEmpty(finalUri.UserInfo))
    {
      throw new InvalidDataException("The Ollama installer was redirected to an unapproved source.");
    }
  }

  private static async Task WaitForOwnedProcessAsync(
    Process process,
    TimeSpan timeout,
    string timeoutMessage,
    CancellationToken cancellationToken)
  {
    using CancellationTokenSource timeoutSource = new(timeout);
    using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
      cancellationToken,
      timeoutSource.Token);
    try
    {
      await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      TryTerminateOwnedProcess(process);
      throw new InvalidOperationException(timeoutMessage);
    }
    catch (OperationCanceledException)
    {
      TryTerminateOwnedProcess(process);
      throw;
    }
  }

  private static void TryTerminateOwnedProcess(Process process)
  {
    try
    {
      if (!process.HasExited)
      {
        process.Kill(entireProcessTree: true);
        _ = process.WaitForExit(5_000);
      }
    }
    catch (InvalidOperationException)
    {
      // The owned process already exited between checks.
    }
    catch (System.ComponentModel.Win32Exception)
    {
      // Preserve cancellation/timeout as the authoritative failure.
    }
  }

  // Ollama's own Windows setup script creates this marker so a first install starts hidden.
  // That keeps the setup entirely within Koncus Nai instead of opening Ollama's onboarding window.
  private static void CreateOllamaUpgradeMarker()
  {
    string markerDirectory = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "Ollama");
    Directory.CreateDirectory(markerDirectory);
    File.WriteAllText(Path.Combine(markerDirectory, "upgraded"), string.Empty, Encoding.UTF8);
  }

  private static void StartOllamaServer(string executablePath)
  {
    ProcessStartInfo startInfo = new()
    {
      FileName = executablePath,
      UseShellExecute = false,
      CreateNoWindow = true,
    };
    startInfo.ArgumentList.Add("serve");
    Process process = Process.Start(startInfo)
      ?? throw new InvalidOperationException("Could not start the Ollama local service.");
    OllamaProcessOwnership.Track(process);
  }

  private static async Task<bool> WaitForApiAsync(TimeSpan timeout, CancellationToken cancellationToken)
  {
    Stopwatch stopwatch = Stopwatch.StartNew();
    while (stopwatch.Elapsed < timeout)
    {
      if (await IsApiReachableAsync(cancellationToken).ConfigureAwait(false))
      {
        return true;
      }

      await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
    }

    return false;
  }

  private static async Task<bool> IsApiReachableAsync(CancellationToken cancellationToken)
  {
    try
    {
      using HttpResponseMessage response = await HttpClient.GetAsync("api/tags", cancellationToken).ConfigureAwait(false);
      return response.IsSuccessStatusCode;
    }
    catch (HttpRequestException)
    {
      return false;
    }
  }

  private static LocalChatRuntimeReadiness Unavailable(string message) => new(false, message, [], []);

  private static string ReadError(string body)
  {
    try
    {
      using JsonDocument document = JsonDocument.Parse(body);
      return document.RootElement.TryGetProperty("error", out JsonElement error)
        ? error.GetString() ?? "unknown error"
        : body;
    }
    catch (JsonException)
    {
      return string.IsNullOrWhiteSpace(body) ? "unknown error" : body;
    }
  }
}

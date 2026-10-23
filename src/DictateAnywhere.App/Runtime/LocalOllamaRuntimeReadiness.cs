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
using DictateAnywhere.Inference;

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
  private static readonly HttpClient LocalClient = OllamaLocalHttp.CreateClient();
  private static readonly HttpClient DownloadClient = new() { Timeout = TimeSpan.FromMinutes(30) };
  private static readonly Uri LocalEndpoint = new("http://127.0.0.1:11434/");

  public static Task<LocalChatRuntimeReadiness> CheckAsync(string modelId, CancellationToken cancellationToken = default) =>
    OllamaProcessOwnership.Default.RunAsync(token => CheckCoreAsync(modelId, token), cancellationToken);

  private static async Task<LocalChatRuntimeReadiness> CheckCoreAsync(string modelId, CancellationToken cancellationToken)
  {
    try
    {
      using HttpRequestMessage request = new(HttpMethod.Get, new Uri(LocalEndpoint, "api/tags"));
      using HttpResponseMessage response = await LocalClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        return Unavailable("Ollama is running but did not return its installed models.");
      }

      using JsonDocument document = JsonDocument.Parse(await OllamaLocalHttp.ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false));
      bool installed = IsPinnedModelPresent(document.RootElement, modelId);
      return installed
        ? LocalChatRuntimeReadiness.Ready with { StatusMessage = $"Ollama is ready with {modelId}." }
        : new LocalChatRuntimeReadiness(false, $"Ollama is ready. Pull {modelId} to start chatting.", [], []) { IsInstalled = false };
    }
    catch (HttpRequestException)
    {
      return Unavailable("Ollama is not running. Start Ollama to check the installed model.");
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

  public static Task<LocalChatRuntimeReadiness> PullAsync(string modelId, IProgress<double>? progress,
    CancellationToken cancellationToken = default) => OllamaProcessOwnership.Default.RunAsync(
      token => PullCoreAsync(modelId, progress, token), cancellationToken);

  public static Task<LocalChatRuntimeReadiness> StartAsync(string modelId, IProgress<double>? progress,
    CancellationToken cancellationToken = default) => OllamaProcessOwnership.Default.RunAsync(
      token => StartCoreAsync(modelId, progress, token), cancellationToken);

  private static async Task<LocalChatRuntimeReadiness> PullCoreAsync(
    string modelId,
    IProgress<double>? progress,
    CancellationToken cancellationToken = default)
  {
    progress?.Report(0);
    try
    {
      await EnsureInstalledAndRunningAsync(progress, cancellationToken).ConfigureAwait(false);
      progress?.Report(0.25);
      cancellationToken.ThrowIfCancellationRequested();
      if (!OllamaListenerTrust.IsCurrentTrusted())
        throw new InvalidOperationException("The Ollama listener changed. Review it before downloading a model.");
      using HttpRequestMessage request = new(HttpMethod.Post, new Uri(LocalEndpoint, "api/pull"))
      {
        Content = new StringContent(JsonSerializer.Serialize(new { model = modelId, stream = false }), Encoding.UTF8, "application/json"),
      };
      using HttpResponseMessage response = await LocalClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        throw new InvalidOperationException($"Ollama could not pull {modelId} (HTTP {(int)response.StatusCode}).");
      }
      _ = await OllamaLocalHttp.ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false);

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
  private static async Task<LocalChatRuntimeReadiness> StartCoreAsync(
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
    await EnsureRunningAsync(OllamaProcessOwnership.Default, IsApiReachableAsync,
      PrepareInstallationAsync, StartOllamaServer,
      token => WaitForApiAsync(TimeSpan.FromSeconds(20), token),
      token => WaitForApiAsync(TimeSpan.FromSeconds(30), token),
      OllamaListenerTrust.ApproveManaged, progress, cancellationToken).ConfigureAwait(false);
  }

  internal static Task<bool> EnsureRunningAsync(OllamaProcessOwnership owner,
    Func<CancellationToken, Task<bool>> reachable,
    Func<IProgress<double>?, CancellationToken, Task<string>> prepare,
    Func<string, OllamaProcessOwnership.IProcess> launch,
    Func<CancellationToken, Task<bool>> waitExternal,
    Func<CancellationToken, Task<bool>> waitOwned,
    Action<OllamaProcessOwnership.IProcess> approve,
    IProgress<double>? progress, CancellationToken cancellationToken) => owner.RunAsync(async token =>
  {
    await owner.SetupGate.WaitAsync(token).ConfigureAwait(false);
    try
    {
      owner.ThrowIfStopping(token);
      if (await reachable(token).ConfigureAwait(false)) return false;
      string executable = await prepare(progress, token).ConfigureAwait(false);
      owner.ThrowIfStopping(token);
      // Polling and installer timing establish availability, never launch ownership.
      if (await waitExternal(token).ConfigureAwait(false)) return false;
      owner.ThrowIfStopping(token);
      OllamaProcessOwnership.OwnedProcess owned = owner.TrackProcess(launch(executable));
      try
      {
        owner.ThrowIfStopping(token);
        if (!await waitOwned(token).ConfigureAwait(false))
          throw new InvalidOperationException("Ollama was installed but its local service did not become ready.");
        owner.ThrowIfStopping(token);
        if (owned.Process.HasExited)
          throw new InvalidOperationException($"The directly launched Ollama service exited with code {owned.Process.ExitCode} before readiness completed.");
        approve(owned.Process);
        owner.ThrowIfStopping(token);
        return true;
      }
      catch
      {
        await CleanupLaunchedAsync(owned).ConfigureAwait(false);
        throw;
      }
    }
    finally { owner.SetupGate.Release(); }
  }, cancellationToken);

  [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Independent owned installer cleanup must preserve the original acquisition/launch failure.")]
  private static async Task<string> PrepareInstallationAsync(IProgress<double>? progress, CancellationToken cancellationToken)
  {
    string executablePath = GetInstalledExecutablePath();
    if (!File.Exists(executablePath))
    {
      progress?.Report(0.02);
      string tempDirectory = Path.Combine(Path.GetTempPath(), $"KoncusNai.OllamaSetup-{Guid.NewGuid():N}");
      string installerPath = Path.Combine(tempDirectory, "OllamaSetup.exe");
      Directory.CreateDirectory(tempDirectory);
      Exception? failure = null;
      try
      {
        await DownloadInstallerAsync(installerPath, progress, cancellationToken).ConfigureAwait(false);
        await VerifyOfficialInstallerAsync(installerPath, cancellationToken).ConfigureAwait(false);
        await RunInstallerAsync(installerPath, cancellationToken).ConfigureAwait(false);
      }
      catch (Exception error) { failure = error; throw; }
      finally
      {
        RemoveInstallerFiles(() => File.Delete(installerPath), () => Directory.Delete(tempDirectory), failure);
      }

      executablePath = GetInstalledExecutablePath();
      if (!File.Exists(executablePath))
      {
        throw new InvalidOperationException("Ollama installation completed but its executable was not found.");
      }

    }
    return executablePath;
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
    using HttpResponseMessage response = await DownloadClient.GetAsync(
      InstallerUrl,
      HttpCompletionOption.ResponseHeadersRead,
      cancellationToken).ConfigureAwait(false);
    ValidateInstallerDownloadSource(response.RequestMessage?.RequestUri);
    response.EnsureSuccessStatusCode();
    long totalBytes = response.Content.Headers.ContentLength ?? -1;
    await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    await using FileStream destination = new(installerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
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
    };
    startInfo.ArgumentList.Add("-NoProfile");
    startInfo.ArgumentList.Add("-NonInteractive");
    startInfo.ArgumentList.Add("-Command");
    startInfo.ArgumentList.Add(
      "$signature = Get-AuthenticodeSignature -LiteralPath '" + installerPath.Replace("'", "''", StringComparison.Ordinal) +
      "'; if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|, )O=Ollama Inc\\.(,|$)') { exit 1 }");

    await RunHelperAsync(OllamaProcessOwnership.Default, () => OllamaProcessOwnership.Launch(startInfo),
      TimeSpan.FromMinutes(2), "Ollama installer signature verification", cancellationToken).ConfigureAwait(false);
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
    await RunHelperAsync(OllamaProcessOwnership.Default, () => OllamaProcessOwnership.Launch(startInfo),
      TimeSpan.FromMinutes(15), "Ollama installation", cancellationToken).ConfigureAwait(false);
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

  internal static Task<int> RunHelperAsync(OllamaProcessOwnership owner,
    Func<OllamaProcessOwnership.IProcess> launch, TimeSpan timeout, string operation,
    CancellationToken cancellationToken,
    Func<Task, TimeSpan, CancellationToken, Task>? wait = null) => owner.RunAsync(async token =>
  {
    owner.ThrowIfStopping(token);
    OllamaProcessOwnership.OwnedProcess owned = owner.TrackProcess(launch());
    Exception? failure = null;
    try
    {
      owner.ThrowIfStopping(token);
      Task exit = owned.WaitForExitAsync();
      await (wait is null ? exit.WaitAsync(timeout, token) : wait(exit, timeout, token)).ConfigureAwait(false);
      owner.ThrowIfStopping(token);
      int code = owned.Process.ExitCode;
      if (code != 0) throw new InvalidOperationException($"{operation} failed with exit code {code}.");
      return code;
    }
    catch (TimeoutException error)
    {
      failure = new InvalidOperationException($"{operation} timed out.", error);
      throw failure;
    }
    catch (Exception error) { failure = error; throw; }
    finally
    {
      if (failure is null) await owned.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
      else await CleanupLaunchedAsync(owned).ConfigureAwait(false);
    }
  }, cancellationToken);

  [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Retain bounded cleanup and preserve the initiating readiness/cancellation/installer failure.")]
  private static async Task CleanupLaunchedAsync(OllamaProcessOwnership.OwnedProcess owned)
  {
    try { await owned.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); }
    catch (Exception) { DictateAnywhere.Core.Services.DiagnosticBoundary.RecordFailure(); }
  }

  [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Run independent owned temporary cleanup, preserving an existing acquisition or helper failure.")]
  internal static void RemoveInstallerFiles(Action deleteFile, Action deleteDirectory, Exception? original)
  {
    List<Exception> failures = [];
    try { deleteFile(); } catch (Exception error) { failures.Add(error); }
    try { deleteDirectory(); } catch (Exception error) { failures.Add(error); }
    if (failures.Count == 0) return;
    if (original is null) throw new AggregateException("Ollama installer temporary cleanup failed.", failures);
    DictateAnywhere.Core.Services.DiagnosticBoundary.RecordFailure();
  }

  private static OllamaProcessOwnership.IProcess StartOllamaServer(string executablePath)
  {
    ProcessStartInfo startInfo = new()
    {
      FileName = executablePath,
      UseShellExecute = false,
      CreateNoWindow = true,
    };
    startInfo.ArgumentList.Add("serve");
    return OllamaProcessOwnership.Launch(startInfo);
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
      using HttpRequestMessage request = new(HttpMethod.Get, new Uri(LocalEndpoint, "api/tags"));
      using HttpResponseMessage response = await LocalClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
      return response.IsSuccessStatusCode;
    }
    catch (HttpRequestException)
    {
      return false;
    }
  }

  private static LocalChatRuntimeReadiness Unavailable(string message) => new(false, message, [], []);

}

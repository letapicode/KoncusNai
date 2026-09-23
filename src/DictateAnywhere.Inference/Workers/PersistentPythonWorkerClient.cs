using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Inference;

internal sealed class PersistentPythonWorkerClient : IPersistentWorkerClient
{
  private static readonly TimeSpan ProcessShutdownTimeout = TimeSpan.FromSeconds(5);
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
  };

  private readonly string pythonExecutablePath;
  private readonly string scriptPath;
  private readonly string arguments;
  private readonly TimeSpan startupTimeout;
  private readonly SemaphoreSlim sync = new(1, 1);
  private readonly StringBuilder stderrBuffer = new();

  private Process? process;
  private BoundedWorkerLineReader? stdoutReader;
  private Task? stderrPump;

  public PersistentPythonWorkerClient(
    string pythonExecutablePath,
    string scriptPath,
    string arguments,
    TimeSpan startupTimeout)
  {
    this.pythonExecutablePath = string.IsNullOrWhiteSpace(pythonExecutablePath)
      ? "python"
      : pythonExecutablePath.Trim();
    this.scriptPath = scriptPath ?? throw new ArgumentNullException(nameof(scriptPath));
    this.arguments = arguments ?? string.Empty;
    this.startupTimeout = startupTimeout;
  }

  public async Task<TResponse> InvokeAsync<TResponse>(
    object request,
    TimeSpan requestTimeout,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);

    await sync.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
      Process activeProcess = process
        ?? throw new InvalidOperationException("Worker process is not available.");

      using CancellationTokenSource timeoutSource =
        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      timeoutSource.CancelAfter(requestTimeout);

      try
      {
        string requestJson = SerializeRequest(request);
        await activeProcess.StandardInput.WriteLineAsync(requestJson.AsMemory(), timeoutSource.Token).ConfigureAwait(false);
        await activeProcess.StandardInput.FlushAsync().ConfigureAwait(false);

        string? responseLine;
        responseLine = await stdoutReader!.ReadLineAsync(timeoutSource.Token).ConfigureAwait(false);
        if (responseLine is null)
        {
          string stderr = ConsumeStderr();
          await ResetProcessAsync().ConfigureAwait(false);
          throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(stderr)
              ? "Python worker exited before returning a response."
              : $"Python worker exited before returning a response. {stderr}");
        }

        WorkerEnvelope envelope;
        try
        {
          envelope = DeserializeEnvelope(responseLine);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
          await ResetProcessAsync().ConfigureAwait(false);
          throw;
        }
        if (!string.Equals(envelope.Status, "ok", StringComparison.OrdinalIgnoreCase))
        {
          string stderr = ConsumeStderr();
          string message = string.IsNullOrWhiteSpace(envelope.Error)
            ? "Python worker returned an error."
            : envelope.Error!;
          if (!string.IsNullOrWhiteSpace(envelope.Traceback))
          {
            // Keep implementation traces in the debugger, never in the reader
            // UI. A Python stack trace is not an actionable recovery message.
            Debug.WriteLine($"Python worker request traceback:{Environment.NewLine}{envelope.Traceback!.Trim()}");
          }

          if (!string.IsNullOrWhiteSpace(stderr))
          {
            message = $"{message} {stderr}";
          }

          throw new InvalidOperationException(message.Trim());
        }

        TResponse response;
        try
        {
          response = DeserializePayload<TResponse>(responseLine);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
          await ResetProcessAsync().ConfigureAwait(false);
          throw;
        }
        _ = ConsumeStderr();
        return response;
      }
      catch (IOException)
      {
        await ResetProcessAsync().ConfigureAwait(false);
        throw;
      }
      catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
      {
        await ResetProcessAsync().ConfigureAwait(false);
        throw new TimeoutException($"Python worker request timed out after {requestTimeout.TotalSeconds:F1}s.");
      }
      catch (OperationCanceledException)
      {
        await ResetProcessAsync().ConfigureAwait(false);
        throw;
      }
    }
    finally
    {
      sync.Release();
    }
  }

  public async Task StartAsync(CancellationToken cancellationToken = default)
  {
    await sync.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
    }
    finally
    {
      sync.Release();
    }
  }

  internal static string SerializeRequest(object request)
  {
    ArgumentNullException.ThrowIfNull(request);
    return JsonSerializer.Serialize(request, JsonOptions);
  }

  internal static TResponse DeserializePayload<TResponse>(string responseLine)
  {
    WorkerEnvelope envelope = DeserializeEnvelope(responseLine);
    if (!string.Equals(envelope.Status, "ok", StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidOperationException("Python worker response did not contain an ok payload.");
    }

    if (!envelope.Payload.HasValue)
    {
      throw new InvalidOperationException("Python worker returned an empty payload.");
    }

    TResponse? response = envelope.Payload.Value.Deserialize<TResponse>(JsonOptions);
    if (response is null)
    {
      throw new InvalidOperationException("Python worker returned an invalid payload.");
    }

    return response;
  }

  public async ValueTask DisposeAsync()
  {
    await sync.WaitAsync().ConfigureAwait(false);
    try
    {
      await ResetProcessAsync().ConfigureAwait(false);
    }
    finally
    {
      sync.Release();
      sync.Dispose();
    }
  }

  private async Task EnsureStartedAsync(CancellationToken cancellationToken)
  {
    if (process is { HasExited: false })
    {
      return;
    }

    await ResetProcessAsync().ConfigureAwait(false);

    ProcessStartInfo startInfo = new()
    {
      FileName = ResolvePythonExecutablePath(pythonExecutablePath, scriptPath),
      Arguments = $"\"{scriptPath}\" {arguments}".Trim(),
      WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? AppContext.BaseDirectory,
      RedirectStandardInput = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true,
      StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
      StandardOutputEncoding = Encoding.UTF8,
      StandardErrorEncoding = Encoding.UTF8,
    };

    Process candidate = new()
    {
      StartInfo = startInfo,
      EnableRaisingEvents = true,
    };

    try
    {
      bool started = ChildProcessErrorMode.RunWithSuppressedCrashDialogs(candidate.Start);
      if (!started)
      {
        candidate.Dispose();
        throw new InvalidOperationException("Python worker could not be started.");
      }
    }
    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or FileNotFoundException or DirectoryNotFoundException)
    {
      candidate.Dispose();
      throw new InvalidOperationException($"Unable to start python worker '{pythonExecutablePath}'. {ex.Message}", ex);
    }

    process = candidate;
    stdoutReader = new BoundedWorkerLineReader(candidate.StandardOutput);
    stderrPump = PumpStandardErrorAsync(candidate);

    using CancellationTokenSource startupSource =
      CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    startupSource.CancelAfter(startupTimeout);

    string? readyLine;
    try
    {
      readyLine = await stdoutReader.ReadLineAsync(startupSource.Token).ConfigureAwait(false);
    }
    catch (IOException)
    {
      await ResetProcessAsync().ConfigureAwait(false);
      throw;
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      string stderr = ConsumeStderr();
      await ResetProcessAsync().ConfigureAwait(false);
      throw new TimeoutException(
        string.IsNullOrWhiteSpace(stderr)
          ? $"Python worker startup timed out after {startupTimeout.TotalSeconds:F1}s."
          : $"Python worker startup timed out after {startupTimeout.TotalSeconds:F1}s. {stderr}");
    }
    catch (OperationCanceledException)
    {
      await ResetProcessAsync().ConfigureAwait(false);
      throw;
    }

    if (readyLine is null)
    {
      string stderr = ConsumeStderr();
      await ResetProcessAsync().ConfigureAwait(false);
      throw new InvalidOperationException(
        string.IsNullOrWhiteSpace(stderr)
          ? "Python worker exited during startup."
          : $"Python worker exited during startup. {stderr}");
    }

    WorkerEnvelope envelope;
    try
    {
      envelope = DeserializeEnvelope(readyLine);
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException)
    {
      await ResetProcessAsync().ConfigureAwait(false);
      throw;
    }
    if (!string.Equals(envelope.Status, "ready", StringComparison.OrdinalIgnoreCase))
    {
      string stderr = ConsumeStderr();
      string message = string.IsNullOrWhiteSpace(envelope.Error)
        ? "Python worker failed during startup."
        : envelope.Error!;
      if (!string.IsNullOrWhiteSpace(envelope.Traceback))
      {
        Debug.WriteLine($"Python worker startup traceback:{Environment.NewLine}{envelope.Traceback!.Trim()}");
      }

      if (!string.IsNullOrWhiteSpace(stderr))
      {
        message = $"{message} {stderr}";
      }

      await ResetProcessAsync().ConfigureAwait(false);
      throw new InvalidOperationException(message.Trim());
    }

    _ = ConsumeStderr();
  }

  private static string ResolvePythonExecutablePath(string configuredPath, string workerScriptPath)
  {
    if (!string.IsNullOrWhiteSpace(configuredPath)
        && !string.Equals(configuredPath, "python", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(configuredPath, "python.exe", StringComparison.OrdinalIgnoreCase))
    {
      return configuredPath;
    }

    string workerScriptName = Path.GetFileName(workerScriptPath);
    if (workerScriptName.Equals("indic_parler_tts_worker.py", StringComparison.OrdinalIgnoreCase))
    {
      string? parlerOverride = Environment.GetEnvironmentVariable("DICTATEANYWHERE_INDIC_PARLER_PYTHON");
      if (!string.IsNullOrWhiteSpace(parlerOverride))
      {
        return parlerOverride.Trim();
      }

      string parlerRuntimePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DictateAnywhere",
        "indic-parler-runtime",
        ".venv",
        "Scripts",
        "python.exe");
      if (File.Exists(parlerRuntimePath))
      {
        return parlerRuntimePath;
      }

      return configuredPath;
    }

    if (workerScriptName.Equals("kokoro_tts_worker.py", StringComparison.OrdinalIgnoreCase)
        || workerScriptName.Equals("kala_nepali_tts_worker.py", StringComparison.OrdinalIgnoreCase)
        || workerScriptName.Equals("rapidocr_worker.py", StringComparison.OrdinalIgnoreCase))
    {
      string? kokoroOverride = Environment.GetEnvironmentVariable("DICTATEANYWHERE_KOKORO_PYTHON");
      if (!string.IsNullOrWhiteSpace(kokoroOverride))
      {
        return kokoroOverride.Trim();
      }

      string kokoroRuntimePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DictateAnywhere",
        "kokoro-runtime",
        ".venv",
        "Scripts",
        "python.exe");
      if (File.Exists(kokoroRuntimePath))
      {
        return kokoroRuntimePath;
      }

      // Reader synthesis has its own runtime. Do not silently bind it to the main local-model
      // runtime, which exists for ASR and Gemma rather than audiobook playback.
      return configuredPath;
    }

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

  private async Task ResetProcessAsync()
  {
    Process? processToDispose = process;
    process = null;
    stdoutReader = null;

    if (processToDispose is not null)
    {
      try
      {
        if (!processToDispose.HasExited)
        {
          processToDispose.Kill(entireProcessTree: true);
          try
          {
            await processToDispose.WaitForExitAsync()
              .WaitAsync(ProcessShutdownTimeout)
              .ConfigureAwait(false);
          }
          catch (TimeoutException)
          {
            Debug.WriteLine("Timed out waiting for the terminated Python worker to exit.");
          }
        }
      }
      catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
      {
      }
      finally
      {
        processToDispose.Dispose();
      }
    }

    if (stderrPump is not null)
    {
      try
      {
        await stderrPump.WaitAsync(ProcessShutdownTimeout).ConfigureAwait(false);
      }
      catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or IOException or TimeoutException)
      {
        Debug.WriteLine($"Python worker stderr pump did not complete cleanly: {ex.Message}");
      }

      stderrPump = null;
    }
  }

  internal const int MaxStderrBufferLength = 65536;

  private async Task PumpStandardErrorAsync(Process activeProcess)
  {
    char[] buffer = new char[4096];
    while (true)
    {
      int count = await activeProcess.StandardError.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
      if (count == 0)
      {
        return;
      }

      AppendStderrLine(new string(buffer, 0, count));
    }
  }

  internal void AppendStderrLine(string line)
  {
    if (string.IsNullOrWhiteSpace(line))
    {
      return;
    }

    lock (stderrBuffer)
    {
      if (stderrBuffer.Length > 0)
      {
        stderrBuffer.Append(' ');
      }

      ReadOnlySpan<char> trimmed = line.AsSpan().Trim();
      if (trimmed.Length > MaxStderrBufferLength) trimmed = trimmed[^MaxStderrBufferLength..];
      stderrBuffer.Append(trimmed);
      if (stderrBuffer.Length > MaxStderrBufferLength)
      {
        int overflow = stderrBuffer.Length - MaxStderrBufferLength;
        stderrBuffer.Remove(0, overflow);
      }
    }
  }

  private string ConsumeStderr()
  {
    lock (stderrBuffer)
    {
      if (stderrBuffer.Length == 0)
      {
        return string.Empty;
      }

      string value = stderrBuffer.ToString();
      stderrBuffer.Clear();
      return value;
    }
  }

  private static WorkerEnvelope DeserializeEnvelope(string line)
  {
    WorkerEnvelope? envelope = JsonSerializer.Deserialize<WorkerEnvelope>(line, JsonOptions);
    if (envelope is null || string.IsNullOrWhiteSpace(envelope.Status))
    {
      throw new InvalidOperationException("Python worker returned malformed JSON.");
    }

    return envelope;
  }

  private sealed record WorkerEnvelope(
    string Status,
    JsonElement? Payload = null,
    string? Error = null,
    string? Traceback = null);
}

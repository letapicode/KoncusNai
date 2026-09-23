using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Inference;
using Xunit;

namespace DictateAnywhere.Inference.Tests;

[Trait("Category", "ProcessIntegration")]
public sealed class WorkerLifecycleFaultMatrixTests : IDisposable
{
  private readonly string testDirectory;

  public WorkerLifecycleFaultMatrixTests()
  {
    testDirectory = Path.Combine(Path.GetTempPath(), "notype-fault-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(testDirectory);
  }

  public void Dispose()
  {
    try
    {
      if (Directory.Exists(testDirectory))
      {
        Directory.Delete(testDirectory, recursive: true);
      }
    }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
  }

  [Fact]
  public async Task OversizedUnterminatedResponseTerminatesWorkerAndAllowsRestart()
  {
    string scriptPath = Path.Combine(testDirectory, "oversized.py");
    File.WriteAllText(scriptPath, "import sys,time\nprint('{\"status\":\"ready\"}', flush=True)\nsys.stdin.readline()\nsys.stdout.write('x' * (17 * 1024 * 1024))\nsys.stdout.flush()\ntime.sleep(30)\n");
    await using PersistentPythonWorkerClient client = new("python", scriptPath, string.Empty, TimeSpan.FromSeconds(5));
    await Assert.ThrowsAsync<InvalidDataException>(() => client.InvokeAsync<JsonElement>(new { }, TimeSpan.FromSeconds(10)));
    File.WriteAllText(scriptPath, "print('{\"status\":\"ready\"}', flush=True)\nimport time\ntime.sleep(30)\n");
    await client.StartAsync();
  }

  [Fact]
  public async Task PersistentPythonWorkerClient_ThrowsInvalidOperationException_WhenExecutableMissing()
  {
    string nonExistentPython = Path.Combine(testDirectory, "nonexistent-python.exe");
    string dummyScript = Path.Combine(testDirectory, "dummy.py");
    File.WriteAllText(dummyScript, "print('hello')");

    await using PersistentPythonWorkerClient client = new(
      nonExistentPython,
      dummyScript,
      arguments: string.Empty,
      startupTimeout: TimeSpan.FromSeconds(2));

    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
      () => client.StartAsync());

    Assert.Contains("Unable to start python worker", ex.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task PersistentPythonWorkerClient_ThrowsTimeoutException_WhenStartupTimesOut()
  {
    string scriptPath = Path.Combine(testDirectory, "hang_startup.py");
    File.WriteAllText(scriptPath, "import time\ntime.sleep(10)\n");

    await using PersistentPythonWorkerClient client = new(
      "python",
      scriptPath,
      arguments: string.Empty,
      startupTimeout: TimeSpan.FromMilliseconds(400));

    TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(
      () => client.StartAsync());

    Assert.Contains("startup timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task PersistentPythonWorkerClient_ThrowsInvalidOperationException_WhenWorkerCrashesAtStartup()
  {
    string scriptPath = Path.Combine(testDirectory, "crash_startup.py");
    File.WriteAllText(scriptPath, "import sys\nsys.stderr.write('Fatal: missing torch library\\n')\nsys.exit(1)\n");

    await using PersistentPythonWorkerClient client = new(
      "python",
      scriptPath,
      arguments: string.Empty,
      startupTimeout: TimeSpan.FromSeconds(3));

    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
      () => client.StartAsync());

    Assert.Contains("Fatal: missing torch library", ex.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task PersistentPythonWorkerClient_ThrowsJsonException_WhenStartupProtocolNotJson()
  {
    string scriptPath = Path.Combine(testDirectory, "malformed_startup.py");
    File.WriteAllText(scriptPath, "import sys\nsys.stdout.write('not-json-ready\\n')\nsys.stdout.flush()\n");

    await using PersistentPythonWorkerClient client = new(
      "python",
      scriptPath,
      arguments: string.Empty,
      startupTimeout: TimeSpan.FromSeconds(3));

    await Assert.ThrowsAsync<JsonException>(
      () => client.StartAsync());
  }

  [Fact]
  public async Task PersistentPythonWorkerClient_ThrowsInvalidOperationException_WhenStartupStatusEmpty()
  {
    string scriptPath = Path.Combine(testDirectory, "empty_status_startup.py");
    File.WriteAllText(scriptPath, "import sys\nsys.stdout.write('{\"status\":\"\"}\\n')\nsys.stdout.flush()\n");

    await using PersistentPythonWorkerClient client = new(
      "python",
      scriptPath,
      arguments: string.Empty,
      startupTimeout: TimeSpan.FromSeconds(3));

    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
      () => client.StartAsync());

    Assert.Contains("malformed JSON", ex.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task PersistentPythonWorkerClient_RestartsAfterMalformedStartupFromLiveWorker()
  {
    string markerFile = Path.Combine(testDirectory, "malformed_startup_count.txt");
    string scriptPath = Path.Combine(testDirectory, "malformed_then_ready.py");
    string escapedMarker = markerFile.Replace("\\", "\\\\");
    string scriptContent = "import sys, os, time\n"
      + "marker = r'" + escapedMarker + "'\n"
      + "count = 0\n"
      + "if os.path.exists(marker):\n"
      + "    with open(marker, 'r') as f:\n"
      + "        count = int(f.read().strip())\n"
      + "count += 1\n"
      + "with open(marker, 'w') as f:\n"
      + "    f.write(str(count))\n"
      + "if count == 1:\n"
      + "    sys.stdout.write('not-json-ready\\n')\n"
      + "else:\n"
      + "    sys.stdout.write('{\"status\":\"ready\"}\\n')\n"
      + "sys.stdout.flush()\n"
      + "while True:\n"
      + "    time.sleep(1)\n";
    File.WriteAllText(scriptPath, scriptContent);

    await using PersistentPythonWorkerClient client = new(
      "python",
      scriptPath,
      arguments: string.Empty,
      startupTimeout: TimeSpan.FromSeconds(3));

    await Assert.ThrowsAsync<JsonException>(() => client.StartAsync());
    await client.StartAsync();

    Assert.Equal("2", File.ReadAllText(markerFile));
  }

  [Fact]
  public async Task PersistentPythonWorkerClient_ThrowsInvalidOperationException_WhenStartupStatusIsError()
  {
    string scriptPath = Path.Combine(testDirectory, "error_startup.py");
    File.WriteAllText(scriptPath, "import sys\nsys.stdout.write('{\"status\":\"error\",\"error\":\"GPU VRAM exhausted\"}\\n')\nsys.stdout.flush()\n");

    await using PersistentPythonWorkerClient client = new(
      "python",
      scriptPath,
      arguments: string.Empty,
      startupTimeout: TimeSpan.FromSeconds(3));

    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
      () => client.StartAsync());

    Assert.Contains("GPU VRAM exhausted", ex.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task PersistentPythonWorkerClient_StderrFlood_CapsAt64KbAndRetainsLatestDiagnosticTail()
  {
    string dummyScript = Path.Combine(testDirectory, "dummy.py");
    File.WriteAllText(dummyScript, "pass");

    await using PersistentPythonWorkerClient client = new(
      "python",
      dummyScript,
      arguments: string.Empty,
      startupTimeout: TimeSpan.FromSeconds(2));

    // Append 500 lines of 200 characters each (> 100 KB total)
    for (int i = 0; i < 500; i++)
    {
      string padding = new('x', 180);
      client.AppendStderrLine($"LINE_{i:D4}_{padding}");
    }

    System.Reflection.FieldInfo? bufferField = typeof(PersistentPythonWorkerClient)
      .GetField("stderrBuffer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    Assert.NotNull(bufferField);

    System.Text.StringBuilder? buffer = bufferField.GetValue(client) as System.Text.StringBuilder;
    Assert.NotNull(buffer);

    Assert.True(buffer.Length <= PersistentPythonWorkerClient.MaxStderrBufferLength,
      $"Buffer length {buffer.Length} exceeded {PersistentPythonWorkerClient.MaxStderrBufferLength}");

    string content = buffer.ToString();
    // Earlier lines (e.g. LINE_0001) must have been evicted from head
    Assert.DoesNotContain("LINE_0001", content, StringComparison.Ordinal);
    // Recent lines (e.g. LINE_0499) must be retained in the tail
    Assert.Contains("LINE_0499", content, StringComparison.Ordinal);
  }

  [Fact]
  public async Task PersistentPythonWorkerClient_ThrowsTimeoutException_WhenRequestTimesOut()
  {
    string scriptPath = Path.Combine(testDirectory, "hang_request.py");
    string scriptContent = """
      import sys, time
      sys.stdout.write('{"status":"ready"}\n')
      sys.stdout.flush()
      line = sys.stdin.readline()
      time.sleep(10)
      """;
    File.WriteAllText(scriptPath, scriptContent);

    await using PersistentPythonWorkerClient client = new(
      "python",
      scriptPath,
      arguments: string.Empty,
      startupTimeout: TimeSpan.FromSeconds(3));

    TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(
      () => client.InvokeAsync<TestPayload>(new { text = "hello" }, requestTimeout: TimeSpan.FromMilliseconds(400)));

    Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task PersistentPythonWorkerClient_RespectsCancellation_AndResetsProcess()
  {
    string scriptPath = Path.Combine(testDirectory, "cancel_request.py");
    string scriptContent = """
      import sys, time
      sys.stdout.write('{"status":"ready"}\n')
      sys.stdout.flush()
      line = sys.stdin.readline()
      time.sleep(10)
      """;
    File.WriteAllText(scriptPath, scriptContent);

    await using PersistentPythonWorkerClient client = new(
      "python",
      scriptPath,
      arguments: string.Empty,
      startupTimeout: TimeSpan.FromSeconds(3));

    using CancellationTokenSource cts = new();
    cts.CancelAfter(TimeSpan.FromMilliseconds(200));

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => client.InvokeAsync<TestPayload>(new { text = "hello" }, requestTimeout: TimeSpan.FromSeconds(5), cancellationToken: cts.Token));
  }

  [Fact]
  public async Task PersistentPythonWorkerClient_ThrowsInvalidOperationException_WhenWorkerCrashesDuringRequest()
  {
    string scriptPath = Path.Combine(testDirectory, "crash_request.py");
    string scriptContent = """
      import sys
      sys.stdout.write('{"status":"ready"}\n')
      sys.stdout.flush()
      line = sys.stdin.readline()
      sys.stderr.write('Critical runtime abort: CUDA illegal memory access\n')
      sys.exit(2)
      """;
    File.WriteAllText(scriptPath, scriptContent);

    await using PersistentPythonWorkerClient client = new(
      "python",
      scriptPath,
      arguments: string.Empty,
      startupTimeout: TimeSpan.FromSeconds(3));

    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
      () => client.InvokeAsync<TestPayload>(new { text = "hello" }, requestTimeout: TimeSpan.FromSeconds(4)));

    Assert.Contains("CUDA illegal memory access", ex.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task PersistentPythonWorkerClient_RecoversAndRestarts_AfterFailure()
  {
    string markerFile = Path.Combine(testDirectory, "run_count.txt");
    string scriptPath = Path.Combine(testDirectory, "recovering_worker.py");
    string escapedMarker = markerFile.Replace("\\", "\\\\");
    string scriptContent = "import sys, os\n"
      + "marker = r'" + escapedMarker + "'\n"
      + "count = 0\n"
      + "if os.path.exists(marker):\n"
      + "    with open(marker, 'r') as f:\n"
      + "        count = int(f.read().strip())\n"
      + "count += 1\n"
      + "with open(marker, 'w') as f:\n"
      + "    f.write(str(count))\n"
      + "sys.stdout.write('{\"status\":\"ready\"}\\n')\n"
      + "sys.stdout.flush()\n"
      + "line = sys.stdin.readline()\n"
      + "if count == 1:\n"
      + "    sys.stderr.write('First run crash\\n')\n"
      + "    sys.exit(1)\n"
      + "else:\n"
      + "    sys.stdout.write('{\"status\":\"ok\",\"payload\":{\"text\":\"recovered successfully\"}}\\n')\n"
      + "    sys.stdout.flush()\n";
    File.WriteAllText(scriptPath, scriptContent);

    await using PersistentPythonWorkerClient client = new(
      "python",
      scriptPath,
      arguments: string.Empty,
      startupTimeout: TimeSpan.FromSeconds(3));

    // First call: worker crashes
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => client.InvokeAsync<TestPayload>(new { action = "first" }, requestTimeout: TimeSpan.FromSeconds(3)));

    // Second call: client recovers automatically by launching a new process
    TestPayload result = await client.InvokeAsync<TestPayload>(
      new { action = "second" },
      requestTimeout: TimeSpan.FromSeconds(3));

    Assert.Equal("recovered successfully", result.Text);
  }

  [Fact]
  public async Task PersistentPythonWorkerClient_RestartsAfterMalformedResponseFromLiveWorker()
  {
    string markerFile = Path.Combine(testDirectory, "malformed_response_count.txt");
    string scriptPath = Path.Combine(testDirectory, "malformed_then_valid_response.py");
    string escapedMarker = markerFile.Replace("\\", "\\\\");
    string scriptContent = "import sys, os, time\n"
      + "marker = r'" + escapedMarker + "'\n"
      + "count = 0\n"
      + "if os.path.exists(marker):\n"
      + "    with open(marker, 'r') as f:\n"
      + "        count = int(f.read().strip())\n"
      + "count += 1\n"
      + "with open(marker, 'w') as f:\n"
      + "    f.write(str(count))\n"
      + "sys.stdout.write('{\"status\":\"ready\"}\\n')\n"
      + "sys.stdout.flush()\n"
      + "line = sys.stdin.readline()\n"
      + "if count == 1:\n"
      + "    sys.stdout.write('not-json-response\\n')\n"
      + "    sys.stdout.flush()\n"
      + "    while True:\n"
      + "        time.sleep(1)\n"
      + "else:\n"
      + "    sys.stdout.write('{\"status\":\"ok\",\"payload\":{\"text\":\"recovered after malformed response\"}}\\n')\n"
      + "    sys.stdout.flush()\n";
    File.WriteAllText(scriptPath, scriptContent);

    await using PersistentPythonWorkerClient client = new(
      "python",
      scriptPath,
      arguments: string.Empty,
      startupTimeout: TimeSpan.FromSeconds(3));

    await Assert.ThrowsAsync<JsonException>(() => client.InvokeAsync<TestPayload>(
      new { action = "first" },
      requestTimeout: TimeSpan.FromSeconds(3)));
    TestPayload result = await client.InvokeAsync<TestPayload>(
      new { action = "second" },
      requestTimeout: TimeSpan.FromSeconds(3));

    Assert.Equal("recovered after malformed response", result.Text);
    Assert.Equal("2", File.ReadAllText(markerFile));
  }

  [Fact]
  public async Task PersistentPythonWorkerClient_DisposeAsync_TerminatesChildProcessTree()
  {
    string scriptPath = Path.Combine(testDirectory, "long_lived.py");
    string scriptContent = """
      import sys, time
      sys.stdout.write('{"status":"ready"}\n')
      sys.stdout.flush()
      while True:
          time.sleep(1)
      """;
    File.WriteAllText(scriptPath, scriptContent);

    PersistentPythonWorkerClient client = new(
      "python",
      scriptPath,
      arguments: string.Empty,
      startupTimeout: TimeSpan.FromSeconds(3));

    await client.StartAsync();

    System.Reflection.FieldInfo? processField = typeof(PersistentPythonWorkerClient)
      .GetField("process", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    Assert.NotNull(processField);

    Process? proc = processField.GetValue(client) as Process;
    Assert.NotNull(proc);
    int pid = proc.Id;
    Assert.False(proc.HasExited);

    // Dispose should terminate the process
    await client.DisposeAsync();

    // Verify process is terminated
    try
    {
      using Process checkProc = Process.GetProcessById(pid);
      Assert.True(checkProc.WaitForExit(3000));
      Assert.True(checkProc.HasExited);
    }
    catch (ArgumentException)
    {
      // Process already terminated and exited - expected!
    }
  }

  private sealed record TestPayload(string Text);
}

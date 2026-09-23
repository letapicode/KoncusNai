using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.Inference.Tests;

public sealed class LlamaCppChatServiceTests
{
  [Xunit.Fact]
  public void ForModel_UsesSelectedQwenFile_AndDisablesReasoningForCpuLatency()
  {
    LlamaCppChatOptions options = LlamaCppChatOptions.ForModel(LlamaCppModelCatalog.Qwen3FourBModelId);

    Xunit.Assert.EndsWith(LlamaCppModelCatalog.Qwen3FourBModelId, options.ModelPath, StringComparison.Ordinal);
    Xunit.Assert.True(options.DisableReasoning);
    Xunit.Assert.Contains(
      LlamaCppModelCatalog.GetAll(),
      model => model.ModelId == LlamaCppModelCatalog.Qwen3OnePointSevenBModelId);
  }

  [Xunit.Fact]
  public void QwenFast_UsesThePublisherThatHostsItsQ4Model()
  {
    LlamaCppModelDefinition model = LlamaCppModelCatalog.GetRequired(
      LlamaCppModelCatalog.Qwen3OnePointSevenBModelId);

    Xunit.Assert.Equal(
      "https://huggingface.co/ggml-org/Qwen3-1.7B-GGUF/resolve/daeb8e2d528a760970442092f6bf1e55c3b659eb/Qwen3-1.7B-Q4_K_M.gguf?download=true",
      model.DownloadUrl);
    Xunit.Assert.Matches("^[a-f0-9]{64}$", model.Sha256);
  }

  [Xunit.Fact]
  public void ProvisioningArtifacts_UseImmutableIdentitiesAndRejectBadIntegrityOrHosts()
  {
    Xunit.Assert.Equal("b10823", LlamaCppProvisioningService.RuntimeVersion);
    Xunit.Assert.DoesNotContain("latest", LlamaCppProvisioningService.RuntimeDownloadUrl, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.All(LlamaCppModelCatalog.GetAll(), model =>
    {
      Xunit.Assert.Matches(@"/resolve/[a-f0-9]{40}/", model.DownloadUrl);
      Xunit.Assert.Matches("^[a-f0-9]{64}$", model.Sha256);
    });

    string tempPath = Path.Combine(Path.GetTempPath(), $"notype-integrity-{Guid.NewGuid():N}.bin");
    try
    {
      File.WriteAllText(tempPath, "trusted fixture");
      Xunit.Assert.Throws<InvalidDataException>(() =>
        LlamaCppProvisioningService.VerifySha256(tempPath, new string('0', 64)));
    }
    finally
    {
      File.Delete(tempPath);
    }

    LlamaCppProvisioningService.ValidateDownloadHost(
      LlamaCppProvisioningService.RuntimeDownloadUrl,
      new Uri("https://release-assets.githubusercontent.com/release.zip"));
    Xunit.Assert.Throws<InvalidDataException>(() =>
      LlamaCppProvisioningService.ValidateDownloadHost(
        LlamaCppProvisioningService.RuntimeDownloadUrl,
        new Uri("https://example.invalid/release.zip")));
  }

  [Xunit.Fact]
  public void ProvisioningService_DoesNotReuseAnUnverifiedExistingModel()
  {
    string path = Path.Combine(Path.GetTempPath(), $"notype-llama-hash-{Guid.NewGuid():N}.gguf");
    try
    {
      File.WriteAllText(path, "not the curated model");
      Xunit.Assert.False(LlamaCppProvisioningService.MatchesSha256(
        path,
        LlamaCppModelCatalog.GetRequired(LlamaCppModelCatalog.Gemma3FourBModelId).Sha256));
    }
    finally
    {
      if (File.Exists(path)) File.Delete(path);
    }
  }

  [Xunit.Fact]
  public void ProvisioningMarkers_RejectMissingOrMismatchedRuntimeAndModelIdentity()
  {
    string directory = Path.Combine(Path.GetTempPath(), $"notype-llama-marker-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      string serverPath = Path.Combine(directory, "llama-server.exe");
      string modelPath = Path.Combine(directory, LlamaCppModelCatalog.Gemma3FourBModelId);
      File.WriteAllText(serverPath, "fixture");
      File.WriteAllText(modelPath, "fixture");
      LlamaCppChatOptions options = LlamaCppChatOptions.Default with
      {
        ServerExecutablePath = serverPath,
        ModelPath = modelPath,
      };

      Xunit.Assert.False(LlamaCppProvisioningService.IsVerifiedRuntime(options));
      File.WriteAllText(
        Path.Combine(directory, LlamaCppProvisioningService.RuntimeMarkerFileName),
        $"{LlamaCppProvisioningService.RuntimeVersion}\r\n{LlamaCppProvisioningService.RuntimeSha256}\r\n");
      Xunit.Assert.True(LlamaCppProvisioningService.IsVerifiedRuntime(options));

      Xunit.Assert.False(LlamaCppProvisioningService.HasVerifiedModelMarker(modelPath));
      File.WriteAllText(
        LlamaCppProvisioningService.GetModelMarkerPath(modelPath),
        LlamaCppModelCatalog.GetRequired(LlamaCppModelCatalog.Gemma3FourBModelId).Sha256 + "\r\n");
      Xunit.Assert.True(LlamaCppProvisioningService.HasVerifiedModelMarker(modelPath));
      File.WriteAllText(LlamaCppProvisioningService.GetModelMarkerPath(modelPath), new string('0', 64) + "\n");
      Xunit.Assert.False(LlamaCppProvisioningService.HasVerifiedModelMarker(modelPath));
    }
    finally
    {
      Directory.Delete(directory, recursive: true);
    }
  }

  [Xunit.Fact]
  public async Task CompleteAsync_UsesLongRequestTimeout_AddsOutputPolicy_AndReportsTruncation()
  {
    using LlamaCppHandler handler = new();
    using HttpClient client = new(handler, disposeHandler: false);
    await using LlamaCppChatService service = new(
      LlamaCppChatOptions.Default with
      {
        ServerBaseAddress = new Uri("http://127.0.0.1:8090/"),
        StartServer = false,
      },
      client);

    ChatCompletionResult result = await service.CompleteAsync(new ChatCompletionRequest(
    [
      new ChatMessage(ChatMessageRoles.User, "Implement a red-black tree.", DateTimeOffset.UtcNow),
    ],
    new ChatModelSelection(ChatProviderIds.LlamaCppLocal, "test.gguf")));

    Xunit.Assert.Equal("Core implementation", result.Text);
    Xunit.Assert.True(result.WasTruncated);
    Xunit.Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    Xunit.Assert.Contains("compact, runnable core implementation", handler.LastPostBody, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task CompleteAsync_WhenUnownedServerClaimsExpectedModel_RejectsWithoutSendingContent()
  {
    using LlamaCppHandler handler = new();
    using HttpClient client = new(handler, disposeHandler: false);
    await using LlamaCppChatService service = new(
      LlamaCppChatOptions.Default with
      {
        ServerBaseAddress = new Uri("http://127.0.0.1:8090/"),
        StartServer = true, // requested start if needed
        ServerExecutablePath = "C:\\nonexistent\\llama-server.exe",
        ModelPath = "C:\\nonexistent\\model.gguf",
      },
      client);

    await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(new ChatCompletionRequest(
    [
      new ChatMessage(ChatMessageRoles.User, "Hello", DateTimeOffset.UtcNow),
    ],
    new ChatModelSelection(ChatProviderIds.LlamaCppLocal, "test.gguf"))));

    Xunit.Assert.Equal(0, handler.PostCount);

    // The unowned process must remain untouched.
    System.Reflection.FieldInfo? processField = typeof(LlamaCppChatService)
      .GetField("ownedServerProcess", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    Xunit.Assert.NotNull(processField);
    Xunit.Assert.Null(processField.GetValue(service));
  }

  [Xunit.Fact]
  public async Task CompleteAsync_RejectsHealthyServerRunningDifferentModel()
  {
    using LlamaCppHandler handler = new("other-model.gguf");
    using HttpClient client = new(handler, disposeHandler: false);
    await using LlamaCppChatService service = new(
      LlamaCppChatOptions.Default with
      {
        ServerBaseAddress = new Uri("http://127.0.0.1:8090/"),
        StartServer = true,
        ServerExecutablePath = "C:\\nonexistent\\llama-server.exe",
        ModelPath = "C:\\models\\expected.gguf",
      }, client);

    InvalidOperationException error = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() =>
      service.CompleteAsync(new ChatCompletionRequest(
        [new ChatMessage(ChatMessageRoles.User, "Private question", DateTimeOffset.UtcNow)],
        new ChatModelSelection(ChatProviderIds.LlamaCppLocal, "expected.gguf"))));

    Xunit.Assert.Contains("Another local model server", error.Message, StringComparison.Ordinal);
    Xunit.Assert.Equal(0, handler.PostCount);
  }

  [Xunit.Fact]
  public async Task CompleteAsync_ThrowsInvalidOperationException_WhenServerExecutableMissing()
  {
    using UnhealthyLlamaCppHandler handler = new();
    using HttpClient client = new(handler, disposeHandler: false);
    await using LlamaCppChatService service = new(
      LlamaCppChatOptions.Default with
      {
        ServerBaseAddress = new Uri("http://127.0.0.1:8090/"),
        StartServer = true,
        ServerExecutablePath = "C:\\nonexistent\\llama-server.exe",
        ModelPath = "C:\\nonexistent\\model.gguf",
      },
      client);

    InvalidOperationException ex = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(
      () => service.CompleteAsync(new ChatCompletionRequest(
      [
        new ChatMessage(ChatMessageRoles.User, "Hello", DateTimeOffset.UtcNow),
      ],
      new ChatModelSelection(ChatProviderIds.LlamaCppLocal, "test.gguf"))));

    Xunit.Assert.Contains("llama-server.exe was not found", ex.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task CompleteAsync_ThrowsInvalidOperationException_WhenModelPathMissing()
  {
    string tempDir = Path.Combine(Path.GetTempPath(), "llama-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    try
    {
      string dummyExe = Path.Combine(tempDir, "fake-llama-server.exe");
      await File.WriteAllTextAsync(dummyExe, "dummy");

      using UnhealthyLlamaCppHandler handler = new();
      using HttpClient client = new(handler, disposeHandler: false);
      await using LlamaCppChatService service = new(
        LlamaCppChatOptions.Default with
        {
          ServerBaseAddress = new Uri("http://127.0.0.1:8090/"),
          StartServer = true,
          ServerExecutablePath = dummyExe,
          ModelPath = "C:\\nonexistent\\missing-model.gguf",
        },
        client);

      InvalidOperationException ex = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(
        () => service.CompleteAsync(new ChatCompletionRequest(
        [
          new ChatMessage(ChatMessageRoles.User, "Hello", DateTimeOffset.UtcNow),
        ],
        new ChatModelSelection(ChatProviderIds.LlamaCppLocal, "test.gguf"))));

      Xunit.Assert.Contains("No GGUF model was found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
    }
  }

  [Xunit.Fact]
  [Xunit.Trait("Category", "ProcessIntegration")]
  public async Task CompleteAsync_ThrowsTimeoutException_AndKillsProcess_WhenStartupTimesOut()
  {
    string tempDir = Path.Combine(Path.GetTempPath(), "llama-timeout-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    try
    {
      string dummyModel = Path.Combine(tempDir, "dummy.gguf");
      await File.WriteAllTextAsync(dummyModel, "dummy model");

      string fakeServerCmd = Path.Combine(tempDir, "fake-server.cmd");
      await File.WriteAllTextAsync(fakeServerCmd, "@echo off\r\nping -n 10 127.0.0.1 > nul\r\n");

      using UnhealthyLlamaCppHandler handler = new();
      using HttpClient client = new(handler, disposeHandler: false);
      await using LlamaCppChatService service = new(
        LlamaCppChatOptions.Default with
        {
          ServerBaseAddress = new Uri("http://127.0.0.1:8090/"),
          StartServer = true,
          ServerExecutablePath = fakeServerCmd,
          ModelPath = dummyModel,
          ServerStartupTimeout = TimeSpan.FromMilliseconds(400),
        },
        client);

      TimeoutException ex = await Xunit.Assert.ThrowsAsync<TimeoutException>(
        () => service.CompleteAsync(new ChatCompletionRequest(
        [
          new ChatMessage(ChatMessageRoles.User, "Hello", DateTimeOffset.UtcNow),
        ],
        new ChatModelSelection(ChatProviderIds.LlamaCppLocal, "test.gguf"))));

      Xunit.Assert.Contains("did not become ready before the startup timeout", ex.Message, StringComparison.OrdinalIgnoreCase);

      // Verify ownedServerProcess was cleaned up (set to null)
      System.Reflection.FieldInfo? processField = typeof(LlamaCppChatService)
        .GetField("ownedServerProcess", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
      Xunit.Assert.NotNull(processField);
      Xunit.Assert.Null(processField.GetValue(service));
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
    }
  }

  [Xunit.Fact]
  [Xunit.Trait("Category", "ProcessIntegration")]
  public async Task CompleteAsync_ThrowsInvalidOperationException_WhenServerExitsDuringStartup()
  {
    string tempDir = Path.Combine(Path.GetTempPath(), "llama-crash-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    try
    {
      string dummyModel = Path.Combine(tempDir, "dummy.gguf");
      await File.WriteAllTextAsync(dummyModel, "dummy model");

      string fakeExitCmd = Path.Combine(tempDir, "fake-exit.cmd");
      await File.WriteAllTextAsync(fakeExitCmd, "@echo off\r\nexit /b 1\r\n");

      using UnhealthyLlamaCppHandler handler = new();
      using HttpClient client = new(handler, disposeHandler: false);
      await using LlamaCppChatService service = new(
        LlamaCppChatOptions.Default with
        {
          ServerBaseAddress = new Uri("http://127.0.0.1:8090/"),
          StartServer = true,
          ServerExecutablePath = fakeExitCmd,
          ModelPath = dummyModel,
          ServerStartupTimeout = TimeSpan.FromSeconds(3),
        },
        client);

      InvalidOperationException ex = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(
        () => service.CompleteAsync(new ChatCompletionRequest(
        [
          new ChatMessage(ChatMessageRoles.User, "Hello", DateTimeOffset.UtcNow),
        ],
        new ChatModelSelection(ChatProviderIds.LlamaCppLocal, "test.gguf"))));

      Xunit.Assert.Contains("llama.cpp stopped during startup", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
    }
  }

  [Xunit.Fact]
  [Xunit.Trait("Category", "ProcessIntegration")]
  public async Task CompleteAsync_ReplacesAndTerminatesPreviouslyOwnedUnhealthyServer()
  {
    string tempDir = Path.Combine(Path.GetTempPath(), "llama-restart-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    int previousProcessId = 0;
    try
    {
      string dummyModel = Path.Combine(tempDir, "dummy.gguf");
      await File.WriteAllTextAsync(dummyModel, "dummy model");
      string fakeServerCmd = Path.Combine(tempDir, "fake-server.cmd");
      await File.WriteAllTextAsync(fakeServerCmd, "@echo off\r\nping -n 20 127.0.0.1 > nul\r\n");

      using Process previousProcess = Process.Start(new ProcessStartInfo(fakeServerCmd)
      {
        UseShellExecute = false,
        CreateNoWindow = true,
      }) ?? throw new InvalidOperationException("Could not start the test process.");
      previousProcessId = previousProcess.Id;

      using UnhealthyLlamaCppHandler handler = new();
      using HttpClient client = new(handler, disposeHandler: false);
      await using LlamaCppChatService service = new(
        LlamaCppChatOptions.Default with
        {
          ServerBaseAddress = new Uri("http://127.0.0.1:8090/"),
          StartServer = true,
          ServerExecutablePath = fakeServerCmd,
          ModelPath = dummyModel,
          ServerStartupTimeout = TimeSpan.FromMilliseconds(400),
        },
        client);

      System.Reflection.FieldInfo? processField = typeof(LlamaCppChatService)
        .GetField("ownedServerProcess", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
      Xunit.Assert.NotNull(processField);
      processField.SetValue(service, previousProcess);

      await Xunit.Assert.ThrowsAsync<TimeoutException>(() => service.CompleteAsync(new ChatCompletionRequest(
      [
        new ChatMessage(ChatMessageRoles.User, "Hello", DateTimeOffset.UtcNow),
      ],
      new ChatModelSelection(ChatProviderIds.LlamaCppLocal, "test.gguf"))));

      Xunit.Assert.True(WaitForProcessExit(previousProcessId, TimeSpan.FromSeconds(3)));
    }
    finally
    {
      if (previousProcessId != 0)
      {
        TryKillProcess(previousProcessId);
      }
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
    }
  }

  private static bool WaitForProcessExit(int processId, TimeSpan timeout)
  {
    try
    {
      using Process process = Process.GetProcessById(processId);
      return process.WaitForExit((int)timeout.TotalMilliseconds);
    }
    catch (ArgumentException)
    {
      return true;
    }
  }

  private static void TryKillProcess(int processId)
  {
    try
    {
      using Process process = Process.GetProcessById(processId);
      if (!process.HasExited)
      {
        process.Kill(entireProcessTree: true);
        process.WaitForExit(3000);
      }
    }
    catch (ArgumentException)
    {
    }
  }

  private sealed class UnhealthyLlamaCppHandler : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
  }

  private sealed class LlamaCppHandler(string modelId = "model.gguf") : HttpMessageHandler
  {
    public string LastPostBody { get; private set; } = string.Empty;
    public int PostCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      if (request.Method == HttpMethod.Post)
      {
        PostCount++;
        LastPostBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? string.Empty;
      }
      string json = request.RequestUri?.AbsolutePath switch
      {
        "/health" => "{}",
        "/v1/models" => $"{{\"data\":[{{\"id\":\"{modelId}\"}}]}}",
        _ => "{\"choices\":[{\"message\":{\"content\":\"Core implementation\"},\"finish_reason\":\"length\"}]}",
      };
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
      });
    }
  }
}

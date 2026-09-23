using System.Net;
using System.Text.Json;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;
using Xunit;

namespace DictateAnywhere.Inference.Tests;

public sealed class LlamaCppRequestBoundaryTests
{
  [Fact]
  public async Task Gemma3_HttpPayloadContainsAlternatingTurnsAndAllContext()
  {
    using Handler handler = new(async (request, token) =>
    {
      using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
      JsonElement messages = body.RootElement.GetProperty("messages");
      Assert.Equal(new[] { "user", "assistant", "user" }, messages.EnumerateArray().Select(message => message.GetProperty("role").GetString()));
      string first = messages[0].GetProperty("content").GetString()!;
      Assert.Contains("Answer directly", first);
      Assert.Contains("You are Koncus Nai", first);
      Assert.Contains("Attached file context", first);
      Assert.Contains("what is an algorithm", first);
      Assert.Contains("retry the question", first);
      Assert.Equal("An algorithm is a sequence of steps.", messages[1].GetProperty("content").GetString());
      Assert.Equal("Give an example", messages[2].GetProperty("content").GetString());
      return Response("{\"choices\":[{\"message\":{\"content\":\"A recipe.\"},\"finish_reason\":\"stop\"}]}");
    });
    using HttpClient client = new(handler);
    await using LlamaCppChatService service = new(Options(), client);
    ChatMessage[] history = [Message("system", "You are Koncus Nai"), Message("system", "Attached file context"),
      Message("user", "what is an algorithm"), Message("user", "retry the question"),
      Message("assistant", "An algorithm is a sequence of steps."), Message("user", "Give an example")];
    ChatCompletionResult result = await service.CompleteAsync(Request(history));
    Assert.Equal("A recipe.", result.Text);
    Assert.Equal("what is an algorithm", history[2].Content);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task Cancellation_DistinguishesUserStopFromConfiguredRequestDeadline(bool userStop)
  {
    TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    using Handler handler = new(async (_, token) =>
    {
      started.SetResult();
      await Task.Delay(Timeout.Infinite, token);
      return Response("{}");
    });
    using HttpClient client = new(handler);
    await using LlamaCppChatService service = new(Options() with
    {
      RequestTimeout = userStop ? TimeSpan.FromSeconds(30) : TimeSpan.FromMilliseconds(100),
    }, client);
    using CancellationTokenSource cancellation = new();
    Task<ChatCompletionResult> completion = service.CompleteAsync(Request([Message("user", "Question")]), cancellation.Token);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    if (userStop)
    {
      cancellation.Cancel();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => completion);
    }
    else
    {
      await Assert.ThrowsAsync<TimeoutException>(() => completion);
      Assert.False(cancellation.IsCancellationRequested);
    }
  }

  [Fact]
  public async Task RejectedRequest_PreservesHttpStatusForThePresentationBoundary()
  {
    using Handler handler = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
    { Content = new StringContent("template rejected") }));
    using HttpClient client = new(handler);
    await using LlamaCppChatService service = new(Options(), client);
    HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(() => service.CompleteAsync(Request([Message("user", "Question")])));
    Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
    Assert.DoesNotContain("template rejected", error.Message);
    Assert.Contains("400", error.Message);
  }

  [Theory]
  [InlineData("https://127.0.0.1:8090/")]
  [InlineData("http://example.com/")]
  [InlineData("http://127.0.0.1.example.com/")]
  [InlineData("http://user:password@127.0.0.1:8090/")]
  [InlineData("http://127.0.0.1:8090/proxy/")]
  [InlineData("http://127.0.0.1:8090/?token=secret")]
  public void InvalidLocalAddress_IsRejectedBeforeAnyNetworkRequest(string address)
  {
    Assert.Throws<ArgumentException>(() => new LlamaCppChatService(Options() with { ServerBaseAddress = new Uri(address) }));
  }

  private static LlamaCppChatOptions Options() => LlamaCppChatOptions.Default with { StartServer = false };
  private static ChatCompletionRequest Request(ChatMessage[] messages) => new(messages,
    new ChatModelSelection(ChatProviderIds.LlamaCppLocal, LlamaCppModelCatalog.Gemma3FourBModelId));
  private static ChatMessage Message(string role, string text) => new(role, text, DateTimeOffset.UtcNow);
  private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

  private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> post) : HttpMessageHandler
  {
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
      Justification = "The returned response transfers to LlamaCppChatService, which disposes it with a using declaration.")]
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
      request.Method == HttpMethod.Get ? Task.FromResult(Response("{}")) : post(request, token);
  }
}

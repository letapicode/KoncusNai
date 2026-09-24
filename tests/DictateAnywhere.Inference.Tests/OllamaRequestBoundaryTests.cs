using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.Inference.Tests;

public sealed class OllamaRequestBoundaryTests
{
  private const string Secret = "private prompt that must not be echoed";

  [Xunit.Theory]
  [Xunit.InlineData("http://example.com:11434/")]
  [Xunit.InlineData("https://127.0.0.1:11434/")]
  [Xunit.InlineData("http://127.0.0.2:11434/")]
  [Xunit.InlineData("http://127.0.0.1:9999/")]
  [Xunit.InlineData("http://user:password@127.0.0.1:11434/")]
  [Xunit.InlineData("http://127.0.0.1:11434/redirect")]
  [Xunit.InlineData("http://127.0.0.1:11434/?target=external")]
  public void Options_RejectEndpointsOutsideTheKnownListener(string endpoint)
    => Xunit.Assert.Throws<ArgumentException>(() => new OllamaChatService(
      new OllamaChatOptions("gemma4:e4b", new Uri(endpoint))));

  [Xunit.Fact]
  public async Task LocalTransport_DoesNotFollowRedirect()
  {
    using TcpListener listener = new(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    Task server = Task.Run(async () =>
    {
      using TcpClient socket = await listener.AcceptTcpClientAsync();
      using NetworkStream stream = socket.GetStream();
      using StreamReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
      while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
      byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 307 Temporary Redirect\r\nLocation: http://127.0.0.1:1/foreign\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
      await stream.WriteAsync(response);
    });

    using HttpClient client = OllamaLocalHttp.CreateClient();
    using HttpResponseMessage response = await client.GetAsync($"http://127.0.0.1:{port}/api/chat");
    Xunit.Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
    await server.WaitAsync(TimeSpan.FromSeconds(5));
  }

  [Xunit.Fact]
  public async Task ListenerReplacement_BlocksTheNextPrompt()
  {
    bool trusted = true;
    int calls = 0;
    using StubHandler handler = new(_ =>
    {
      calls++;
      return new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent("{\"message\":{\"content\":\"local answer\"}}"),
      };
    });
    using HttpClient client = new(handler, disposeHandler: false);
    OllamaChatService service = new(OllamaChatOptions.ForModel("gemma4:e4b"), () => trusted, client);
    _ = await service.CompleteAsync(Request());
    trusted = false;
    await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(Request()));
    Xunit.Assert.Equal(1, calls);
  }

  [Xunit.Fact]
  public async Task ErrorResponse_DoesNotEchoPromptIntoException()
  {
    using StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
    {
      Content = new StringContent($"error: {Secret}"),
    });
    using HttpClient client = new(handler, disposeHandler: false);
    OllamaChatService service = new(OllamaChatOptions.ForModel("gemma4:e4b"), () => true, client);
    InvalidOperationException error = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(Request()));
    Xunit.Assert.DoesNotContain(Secret, error.ToString(), StringComparison.Ordinal);
    Xunit.Assert.Contains("HTTP 400", error.Message, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public async Task OversizedResponse_IsRejectedBeforeJsonParsing()
  {
    using StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new ByteArrayContent(new byte[OllamaLocalHttp.MaximumResponseBytes + 1]),
    });
    using HttpClient client = new(handler, disposeHandler: false);
    OllamaChatService service = new(OllamaChatOptions.ForModel("gemma4:e4b"), () => true, client);
    await Xunit.Assert.ThrowsAsync<InvalidDataException>(() => service.CompleteAsync(Request()));
  }

  [Xunit.Fact]
  public async Task UnknownLengthStreamingResponse_IsStoppedAtTheByteLimit()
  {
    using GeneratedStream stream = new(OllamaLocalHttp.MaximumResponseBytes + 1);
    using HttpContent content = new StreamContent(stream);
    await Xunit.Assert.ThrowsAsync<InvalidDataException>(() =>
      OllamaLocalHttp.ReadBoundedAsync(content, CancellationToken.None));
  }

  private static ChatCompletionRequest Request() => new(
    [new ChatMessage(ChatMessageRoles.User, Secret, DateTimeOffset.UtcNow)],
    new ChatModelSelection(ChatProviderIds.OllamaLocal, "gemma4:e4b"));

  private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
      => Task.FromResult(send(request));
  }

  private sealed class GeneratedStream(long length) : Stream
  {
    private long remaining = length;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count)
    {
      int read = (int)Math.Min(count, remaining);
      Array.Fill(buffer, (byte)'x', offset, read);
      remaining -= read;
      return read;
    }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
      int read = (int)Math.Min(buffer.Length, remaining);
      buffer.Span[..read].Fill((byte)'x');
      remaining -= read;
      return ValueTask.FromResult(read);
    }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
  }
}

using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Inference;

/// <summary>Transport for private requests to the local Ollama API.</summary>
internal static class OllamaLocalHttp
{
  public const int MaximumResponseBytes = 16 * 1024 * 1024;

  public static HttpClient CreateClient()
  {
#pragma warning disable CA2000 // HttpClient takes ownership of the handler; the catch disposes it if construction fails.
    HttpClientHandler handler = new() { AllowAutoRedirect = false, UseProxy = false };
#pragma warning restore CA2000
    try
    {
      return new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromMinutes(10) };
    }
    catch
    {
      handler.Dispose();
      throw;
    }
  }

  public static async Task<string> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
  {
    if (content.Headers.ContentLength > MaximumResponseBytes)
      throw new InvalidDataException("The local Ollama response exceeded the size limit.");

    await using Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    using MemoryStream buffer = new();
    byte[] chunk = new byte[8192];
    int read;
    while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
    {
      if (read > MaximumResponseBytes - buffer.Length)
        throw new InvalidDataException("The local Ollama response exceeded the size limit.");
      buffer.Write(chunk, 0, read);
    }
    return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
  }
}

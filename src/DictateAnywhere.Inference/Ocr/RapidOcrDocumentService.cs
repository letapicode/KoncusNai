using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Inference;

/// <summary>Offline RapidOCR worker used for scanned documents and image imports.</summary>
public sealed class RapidOcrDocumentService : IDocumentOcrService
{
  public const string ProviderId = "rapidocr-local";
  private readonly SemaphoreSlim clientSync = new(1, 1);
  private IPersistentWorkerClient? client;

  public async Task<DocumentOcrResult> RecognizeAsync(
    DocumentOcrRequest request,
    CancellationToken cancellationToken = default)
  {
    DocumentOcrRequest normalized = (request ?? throw new ArgumentNullException(nameof(request))).Normalize();
    if (!File.Exists(normalized.ImagePath))
    {
      throw new FileNotFoundException("The image selected for text recognition was not found.", normalized.ImagePath);
    }
    if (new FileInfo(normalized.ImagePath).Length > 64L * 1024 * 1024)
      throw new InvalidDataException("The image exceeds the 64 MiB import limit.");

    IPersistentWorkerClient worker = await GetOrCreateClientAsync(cancellationToken).ConfigureAwait(false);
    RapidOcrWorkerResponse response = await worker.InvokeAsync<RapidOcrWorkerResponse>(
      new RapidOcrWorkerRequest(normalized.ImagePath, normalized.Language),
      TimeSpan.FromMinutes(5),
      cancellationToken).ConfigureAwait(false);
    List<DocumentOcrLine> lines = response.Lines
      .Where(line => !string.IsNullOrWhiteSpace(line.Text))
      .Select(line => new DocumentOcrLine(line.Text.Trim(), Math.Clamp(line.Confidence, 0d, 1d)))
      .ToList();
    return new DocumentOcrResult(lines, ProviderId);
  }

  public async ValueTask DisposeAsync()
  {
    await clientSync.WaitAsync().ConfigureAwait(false);
    try
    {
      if (client is not null)
      {
        await client.DisposeAsync().ConfigureAwait(false);
        client = null;
      }
    }
    finally
    {
      clientSync.Release();
      clientSync.Dispose();
    }
  }

  private async Task<IPersistentWorkerClient> GetOrCreateClientAsync(CancellationToken cancellationToken)
  {
    await clientSync.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      client ??= new PersistentPythonWorkerClient(
        "python",
        LocalModelScriptPathResolver.Resolve("rapidocr_worker.py"),
        string.Empty,
        TimeSpan.FromMinutes(3));
      return client;
    }
    finally
    {
      clientSync.Release();
    }
  }

  private sealed record RapidOcrWorkerRequest(string ImagePath, string Language);
  private sealed record RapidOcrWorkerLine(string Text, double Confidence);
  private sealed record RapidOcrWorkerResponse(IReadOnlyList<RapidOcrWorkerLine> Lines);
}

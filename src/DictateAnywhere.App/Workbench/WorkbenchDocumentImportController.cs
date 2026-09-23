using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;

namespace DictateAnywhere.App.Workbench;

internal sealed record WorkbenchDocumentImportProgress(string FileName, int Index, int Total);

internal sealed record WorkbenchDocumentImportResult(
  IReadOnlyList<ChatFileAttachment> Attachments,
  IReadOnlyList<string> FailedFileNames);

/// <summary>Owns lazy OCR lifetime and bounded, sequential Workbench document extraction.</summary>
internal sealed class WorkbenchDocumentImportController : IAsyncDisposable
{
  private readonly Func<IDocumentOcrService> documentOcrServiceFactory;
  private readonly IDiagnostics diagnostics;
  private IDocumentOcrService? documentOcrService;
  private bool disposed;

  public WorkbenchDocumentImportController(
    Func<IDocumentOcrService> documentOcrServiceFactory,
    IDiagnostics diagnostics)
  {
    this.documentOcrServiceFactory = documentOcrServiceFactory ?? throw new ArgumentNullException(nameof(documentOcrServiceFactory));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
  }

  public async Task<WorkbenchDocumentImportResult> ImportAsync(
    IReadOnlyList<string> files,
    string language,
    Action<WorkbenchDocumentImportProgress>? progress = null,
    CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    ArgumentNullException.ThrowIfNull(files);
    cancellationToken.ThrowIfCancellationRequested();
    IDocumentOcrService ocrService = documentOcrService ??= documentOcrServiceFactory();
    List<ChatFileAttachment> attachments = [];
    List<string> failures = [];
    for (int index = 0; index < files.Count; index++)
    {
      cancellationToken.ThrowIfCancellationRequested();
      string file = files[index];
      string fileName = Path.GetFileName(file);
      progress?.Invoke(new WorkbenchDocumentImportProgress(fileName, index + 1, files.Count));
      try
      {
        string extractedText = await ReadableDocumentTextExtractor.ExtractAsync(
            file,
            language,
            ocrService,
            progress: null,
            cancellationToken)
          .ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(extractedText))
        {
          attachments.Add(ChatFileAttachment.Create(file, extractedText));
        }
        else
        {
          failures.Add(fileName);
        }
      }
      catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException or TimeoutException)
      {
        diagnostics.Warning($"Chat file import failed for '{file}': {ex.Message}");
        failures.Add(fileName);
      }
    }

    return new WorkbenchDocumentImportResult(attachments, failures);
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    if (documentOcrService is IAsyncDisposable disposable)
    {
      await disposable.DisposeAsync().ConfigureAwait(true);
    }

    documentOcrService = null;
  }
}

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

[SuppressMessage(
  "Reliability",
  "CA2000:Dispose objects before losing scope",
  Justification = "WorkbenchDocumentImportController owns and disposes the OCR service produced by the test factory.")]
public sealed class WorkbenchDocumentImportControllerTests
{
  [Xunit.Fact]
  public async Task ImportAsync_ExtractsSequentialAttachmentsAndDisposesOcrService()
  {
    string first = CreateTempImage();
    string second = CreateTempImage();
    TrackingOcrService ocr = new();
    WorkbenchDocumentImportController controller = new(() => ocr, new RecordingDiagnostics());
    try
    {
      WorkbenchDocumentImportResult result = await controller.ImportAsync([first, second], "en");

      Xunit.Assert.Equal(2, result.Attachments.Count);
      Xunit.Assert.Empty(result.FailedFileNames);
      Xunit.Assert.Equal([first, second], ocr.Paths);
    }
    finally
    {
      await controller.DisposeAsync();
      File.Delete(first);
      File.Delete(second);
    }

    Xunit.Assert.True(ocr.Disposed);
  }

  private static string CreateTempImage()
  {
    string path = Path.Combine(Path.GetTempPath(), $"notype-import-{Guid.NewGuid():N}.png");
    File.WriteAllBytes(path, [0]);
    return path;
  }

  private sealed class TrackingOcrService : IDocumentOcrService
  {
    public List<string> Paths { get; } = new();
    public bool Disposed { get; private set; }

    public Task<DocumentOcrResult> RecognizeAsync(
      DocumentOcrRequest request,
      CancellationToken cancellationToken = default)
    {
      Paths.Add(request.ImagePath);
      return Task.FromResult(new DocumentOcrResult([new DocumentOcrLine("recognized", 1)], "fake"));
    }

    public ValueTask DisposeAsync()
    {
      Disposed = true;
      return ValueTask.CompletedTask;
    }
  }

  private sealed class RecordingDiagnostics : IDiagnostics
  {
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
  }
}

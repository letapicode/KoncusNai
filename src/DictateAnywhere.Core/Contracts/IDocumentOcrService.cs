using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Core.Contracts;

/// <summary>Extracts ordered text lines from a local image without uploading it.</summary>
public interface IDocumentOcrService : IAsyncDisposable
{
  Task<DocumentOcrResult> RecognizeAsync(DocumentOcrRequest request, CancellationToken cancellationToken = default);
}

public sealed record DocumentOcrRequest(string ImagePath, string Language = "en")
{
  public DocumentOcrRequest Normalize() => new(
    ImagePath?.Trim() ?? string.Empty,
    string.IsNullOrWhiteSpace(Language) ? "en" : Language.Trim().ToLowerInvariant());
}

public sealed record DocumentOcrLine(string Text, double Confidence);

public sealed record DocumentOcrResult(IReadOnlyList<DocumentOcrLine> Lines, string ProviderId)
{
  public string Text => string.Join(Environment.NewLine, Lines.Select(line => line.Text));
}

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace DictateAnywhere.App.Workbench;

/// <summary>One shared expansion budget, including repeated ZIP entries, for an import.</summary>
internal sealed class DocumentImportBudget(CancellationToken cancellationToken)
{
  internal const int MaximumCharacters = 8 * 1024 * 1024;
  internal const long MaximumFileBytes = 64 * 1024 * 1024;
  private int remaining = MaximumCharacters;

  public void CheckCancellation() => cancellationToken.ThrowIfCancellationRequested();

  public void CheckFile(string path)
  {
    CheckCancellation();
    if (new FileInfo(path).Length > MaximumFileBytes)
      throw new InvalidDataException("The document exceeds the 64 MiB import limit. Split it into smaller documents.");
  }

  public string ReadFile(string path)
  {
    using FileStream stream = OpenFile(path);
    return ReadText(stream);
  }

  public FileStream OpenFile(string path)
  {
    CheckCancellation();
    FileStream stream = File.OpenRead(path);
    if (stream.Length <= MaximumFileBytes) return stream;
    stream.Dispose();
    throw new InvalidDataException("The document exceeds the 64 MiB import limit. Split it into smaller documents.");
  }

  public string ReadEntry(ZipArchiveEntry entry)
  {
    CheckCancellation();
    // UTF-8/UTF-16/UTF-32 input cannot need more than four bytes per character.
    if (entry.Length > (long)remaining * 4 + 4) throw LimitExceeded();
    using Stream stream = entry.Open();
    return ReadText(stream);
  }

  public XDocument ReadXml(ZipArchiveEntry entry)
  {
    using StringReader text = new(ReadEntry(entry));
    using XmlReader reader = XmlReader.Create(text, new XmlReaderSettings
    {
      DtdProcessing = DtdProcessing.Prohibit,
      XmlResolver = null,
      MaxCharactersInDocument = MaximumCharacters,
    });
    XDocument document = XDocument.Load(reader);
    CheckCancellation();
    return document;
  }

  private string ReadText(Stream stream)
  {
    using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
    char[] buffer = new char[4096];
    StringBuilder text = new();
    while (true)
    {
      CheckCancellation();
      int count = reader.Read(buffer, 0, Math.Min(buffer.Length, remaining + 1));
      if (count == 0) return text.ToString();
      if (count > remaining) throw LimitExceeded();
      remaining -= count;
      text.Append(buffer, 0, count);
    }
  }

  private static InvalidDataException LimitExceeded() => new(
    "The document exceeds the expanded text import limit. Split it into smaller documents.");
}

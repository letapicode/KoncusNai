using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.App.History;

internal sealed record RawHistoryLine(long Offset, long Length, string? Text);

/// <summary>Bounds parsing memory while retaining byte ranges for lossless mutation.</summary>
internal static class RawHistoryLines
{
  internal const int MaximumRecordBytes = 16 * 1024 * 1024;
  private static readonly UTF8Encoding StrictUtf8 = new(false, true);

  public static async IAsyncEnumerable<RawHistoryLine> ReadAsync(Stream stream,
    [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    byte[] buffer = new byte[64 * 1024];
    using MemoryStream line = new();
    long offset = 0;
    long length = 0;
    int count;
    while ((count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
    {
      int start = 0;
      for (int i = 0; i < count; i++)
      {
        if (buffer[i] != (byte)'\n') continue;
        Append(buffer, start, i - start + 1);
        yield return new RawHistoryLine(offset, length, Decode());
        offset += length;
        length = 0;
        line.SetLength(0);
        start = i + 1;
      }
      Append(buffer, start, count - start);
    }
    if (length > 0) yield return new RawHistoryLine(offset, length, Decode());

    void Append(byte[] source, int start, int count)
    {
      length += count;
      if (length <= MaximumRecordBytes) line.Write(source, start, count);
    }

    string? Decode()
    {
      if (length > MaximumRecordBytes) return null;
      try
      {
        string text = StrictUtf8.GetString(line.GetBuffer(), 0, (int)line.Length);
        return offset == 0 && text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
      }
      catch (DecoderFallbackException) { return null; }
    }
  }

  public static async Task CopyAsync(FileStream input, Stream output, RawHistoryLine line, byte[] buffer, CancellationToken token)
  {
    input.Position = line.Offset;
    long remaining = line.Length;
    while (remaining > 0)
    {
      int count = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(remaining, buffer.Length)), token).ConfigureAwait(false);
      if (count == 0) throw new EndOfStreamException("History changed while copying a record.");
      await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
      remaining -= count;
    }
  }

  public static async Task EnsureBoundaryAsync(FileStream stream, CancellationToken token)
  {
    if (stream.Length == 0) return;
    stream.Position = stream.Length - 1;
    int last = stream.ReadByte();
    stream.Position = stream.Length;
    if (last != '\n') await stream.WriteAsync(new byte[] { (byte)'\n' }, token).ConfigureAwait(false);
  }
}

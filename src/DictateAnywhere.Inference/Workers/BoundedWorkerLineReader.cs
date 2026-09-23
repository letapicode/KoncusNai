using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Inference;

/// <summary>Retains read-ahead between messages without allocating an unbounded protocol line.</summary>
internal sealed class BoundedWorkerLineReader(TextReader reader, int maximumCharacters = 16 * 1024 * 1024)
{
  private readonly char[] buffer = new char[4096];
  private int position;
  private int count;

  public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
  {
    StringBuilder line = new();
    while (true)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (position == count)
      {
        count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        position = 0;
        if (count == 0) return line.Length == 0 ? null : line.ToString().TrimEnd('\r');
      }
      int newline = Array.IndexOf(buffer, '\n', position, count - position);
      int end = newline < 0 ? count : newline;
      int length = end - position;
      if (length > maximumCharacters - line.Length)
        throw new InvalidDataException("Python worker output exceeded the response size limit.");
      line.Append(buffer, position, length);
      position = newline < 0 ? count : newline + 1;
      if (newline >= 0) return line.ToString().TrimEnd('\r');
    }
  }
}

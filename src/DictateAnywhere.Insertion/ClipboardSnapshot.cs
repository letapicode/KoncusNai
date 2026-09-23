using System;

namespace DictateAnywhere.Insertion;

public sealed record ClipboardSnapshot(ClipboardSnapshotKind Kind, string? UnicodeText)
{
  public uint SequenceNumber { get; init; }
  public static ClipboardSnapshot Empty { get; } = new(ClipboardSnapshotKind.Empty, null);

  public static ClipboardSnapshot FromUnicodeText(string text)
  {
    ArgumentNullException.ThrowIfNull(text);
    return new ClipboardSnapshot(ClipboardSnapshotKind.UnicodeText, text);
  }
}

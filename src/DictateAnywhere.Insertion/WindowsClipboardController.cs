using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DictateAnywhere.Insertion;

public sealed class WindowsClipboardController : IClipboardController
{
  private static readonly ClipboardStaOperation Operations = new();
  private readonly int retryCount;
  private readonly TimeSpan retryDelay;
  private readonly int maxSnapshotTextCharacters;
  private readonly TimeSpan staOperationTimeout;

  public WindowsClipboardController() : this(6, TimeSpan.FromMilliseconds(40), 2_000_000, TimeSpan.FromSeconds(5)) { }

  internal WindowsClipboardController(int retryCount, TimeSpan retryDelay, int maxSnapshotTextCharacters, TimeSpan staOperationTimeout)
  {
    ArgumentOutOfRangeException.ThrowIfLessThan(retryCount, 1);
    ArgumentOutOfRangeException.ThrowIfLessThan(retryDelay, TimeSpan.Zero);
    ArgumentOutOfRangeException.ThrowIfLessThan(maxSnapshotTextCharacters, 1);
    ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(staOperationTimeout, TimeSpan.Zero);
    this.retryCount = retryCount;
    this.retryDelay = retryDelay;
    this.maxSnapshotTextCharacters = maxSnapshotTextCharacters;
    this.staOperationTimeout = staOperationTimeout;
  }

  public uint GetSequenceNumber() => GetClipboardSequenceNumber();

  public ClipboardSnapshot CaptureSnapshot() => Operations.Run(_ =>
  {
    try
    {
      uint sequence = GetSequenceNumber();
      IDataObject? data = Clipboard.GetDataObject();
      string[] formats = data?.GetFormats(autoConvert: false) ?? [];
      if (!CanPreserveFormats(formats))
        throw new ClipboardContentNotSupportedException("Clipboard contains rich or non-text data; use typing to preserve it.");
      string? text = data?.GetData(DataFormats.UnicodeText, autoConvert: true) as string;
      if (text?.Length > maxSnapshotTextCharacters)
        throw new ClipboardContentNotSupportedException("Clipboard text is too large to preserve safely.");
      if (GetSequenceNumber() != sequence)
        throw new ClipboardOperationException("Clipboard changed while its snapshot was being read.");
      return (text is null ? ClipboardSnapshot.Empty : ClipboardSnapshot.FromUnicodeText(text)) with { SequenceNumber = sequence };
    }
    catch (ExternalException ex) { throw new ClipboardOperationException("Clipboard snapshot could not be read.", ex); }
  }, staOperationTimeout);

  internal static bool CanPreserveFormats(string[] formats) => formats.All(format =>
    format == DataFormats.Text || format == DataFormats.UnicodeText || format == DataFormats.OemText
      || format == DataFormats.StringFormat || format == DataFormats.Locale);

  public uint SetUnicodeText(string text, uint? expectedSequence = null)
  {
    ArgumentNullException.ThrowIfNull(text);
    uint expected = expectedSequence ?? GetSequenceNumber();
    return Operations.Run(boundary => WriteNative(text, expected, boundary)
      ?? throw new ClipboardOperationException("Clipboard changed before text could be placed on it."), staOperationTimeout);
  }

  public uint? RestoreSnapshot(ClipboardSnapshot snapshot, uint expectedSequence)
  {
    ArgumentNullException.ThrowIfNull(snapshot);
    return Operations.Run(boundary => WriteNative(
      snapshot.Kind == ClipboardSnapshotKind.Empty ? null : snapshot.UnicodeText ?? string.Empty,
      expectedSequence, boundary), staOperationTimeout);
  }

  private uint? WriteNative(string? text, uint expected, ClipboardStaOperation.CommitBoundary boundary)
  {
    // Immediate native data avoids OLE delayed rendering on the mutation path.
    // Compare ownership and replace while OpenClipboard excludes other writers.
    using ClipboardOwner owner = new();
    bool opened = false;
    nint memory = 0;
    bool mutated = false;
    try
    {
      if (text is not null)
      {
        char[] characters = (text + '\0').ToCharArray();
        memory = GlobalAlloc(0x0042, checked((nuint)characters.Length * 2));
        if (memory == 0) throw new ClipboardOperationException("Unable to allocate clipboard text.");
        nint pointer = GlobalLock(memory);
        if (pointer == 0) throw new ClipboardOperationException("Unable to access clipboard text storage.");
        try { Marshal.Copy(characters, 0, pointer, characters.Length); }
        finally { _ = GlobalUnlock(memory); }
      }
      for (int attempt = 0; attempt < retryCount; attempt++)
      {
        opened = OpenClipboard(owner.Handle);
        if (opened) break;
        if (attempt + 1 < retryCount) Thread.Sleep(retryDelay);
      }
      if (!opened) throw new ClipboardOperationException("Clipboard is busy.");
      if (GetSequenceNumber() != expected) return null;
      boundary.Enter();
      if (!EmptyClipboard()) throw new ClipboardOperationException("Unable to replace clipboard contents.");
      mutated = true;
      if (memory != 0)
      {
        if (SetClipboardData(13, memory) == 0) throw new ClipboardOperationException("Unable to write clipboard text.") { MayHaveMutated = true };
        memory = 0;
      }
      return GetSequenceNumber();
    }
    catch (ExternalException ex)
    {
      throw new ClipboardOperationException("Native clipboard operation failed.", ex) { MayHaveMutated = mutated };
    }
    finally
    {
      if (opened) _ = CloseClipboard();
      if (memory != 0) _ = GlobalFree(memory);
    }
  }

  private sealed class ClipboardOwner : NativeWindow, IDisposable
  {
    public ClipboardOwner() => CreateHandle(new CreateParams { Parent = new nint(-3) });
    public void Dispose() => DestroyHandle();
  }

  [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
  [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(nint owner);
  [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
  [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EmptyClipboard();
  [DllImport("user32.dll")] private static extern nint SetClipboardData(uint format, nint memory);
  [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint flags, nuint bytes);
  [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
  [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(nint memory);
  [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint memory);
}

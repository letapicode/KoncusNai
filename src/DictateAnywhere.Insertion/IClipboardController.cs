namespace DictateAnywhere.Insertion;

public interface IClipboardController
{
  ClipboardSnapshot CaptureSnapshot();

  uint GetSequenceNumber();

  uint SetUnicodeText(string text, uint? expectedSequence = null);

  uint? RestoreSnapshot(ClipboardSnapshot snapshot, uint expectedSequence);
}

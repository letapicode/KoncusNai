using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Insertion;

namespace DictateAnywhere.Insertion.Tests;

public sealed class ClipboardOwnershipTests
{
  [Xunit.Fact]
  public async Task TimedOutPreparationCannotCommitLater_AndAdmissionRemainsBounded()
  {
    ClipboardStaOperation operations = new();
    using ManualResetEventSlim entered = new();
    using ManualResetEventSlim release = new();
    using ManualResetEventSlim finished = new();
    int mutations = 0;
    Task attempt = Task.Run(() => Xunit.Assert.Throws<ClipboardOperationException>(() => operations.Run(boundary =>
    {
      entered.Set();
      release.Wait();
      try { boundary.Enter(); Interlocked.Increment(ref mutations); return true; }
      finally { finished.Set(); }
    }, TimeSpan.FromMilliseconds(50))));
    try
    {
      Xunit.Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
      await attempt.WaitAsync(TimeSpan.FromSeconds(5));
      Xunit.Assert.Throws<ClipboardOperationException>(() => operations.Run(_ => true, TimeSpan.FromSeconds(1)));
    }
    finally { release.Set(); }
    Xunit.Assert.True(finished.Wait(TimeSpan.FromSeconds(5)));
    Xunit.Assert.Equal(0, mutations);
  }

  [Xunit.Theory]
  [Xunit.InlineData("HTML Format")]
  [Xunit.InlineData("Rich Text Format")]
  [Xunit.InlineData("application/custom")]
  public void UnicodeAlongsideRichData_IsNotAPlainTextSnapshot(string format)
  {
    Xunit.Assert.False(WindowsClipboardController.CanPreserveFormats(["UnicodeText", format]));
    Xunit.Assert.True(WindowsClipboardController.CanPreserveFormats(["UnicodeText", "Text"]));
  }
}

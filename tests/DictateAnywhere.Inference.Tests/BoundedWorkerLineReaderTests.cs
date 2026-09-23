using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Inference.Tests;

public sealed class BoundedWorkerLineReaderTests
{
  [Xunit.Fact]
  public async Task ReadAheadRetainsFollowingResponsesAndHandlesCrLfAndEof()
  {
    using StringReader input = new("ready\r\nok\nlast");
    BoundedWorkerLineReader reader = new(input, 6);
    Xunit.Assert.Equal("ready", await reader.ReadLineAsync(default));
    Xunit.Assert.Equal("ok", await reader.ReadLineAsync(default));
    Xunit.Assert.Equal("last", await reader.ReadLineAsync(default));
    Xunit.Assert.Null(await reader.ReadLineAsync(default));
  }

  [Xunit.Theory]
  [Xunit.InlineData("")]
  [Xunit.InlineData("\n")]
  public async Task OversizedResponseRejectedEvenWithoutNewline(string ending)
  {
    using StringReader input = new(new string('x', 9000) + ending);
    BoundedWorkerLineReader reader = new(input, 8192);
    await Xunit.Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadLineAsync(default));
  }

  [Xunit.Fact]
  public async Task CancellationCheckedBetweenBufferedResponses()
  {
    using StringReader input = new("first\nsecond\n");
    BoundedWorkerLineReader reader = new(input);
    _ = await reader.ReadLineAsync(default);
    await Xunit.Assert.ThrowsAsync<System.OperationCanceledException>(() => reader.ReadLineAsync(new CancellationToken(true)));
  }
}

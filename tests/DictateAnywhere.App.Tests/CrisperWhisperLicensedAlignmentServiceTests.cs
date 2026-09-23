using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class CrisperWhisperLicensedAlignmentServiceTests
{
  [Xunit.Fact]
  public async Task AlignAsync_WithoutCurrentAcceptance_RejectsBeforeInvokingModel()
  {
    await using FakeAlignmentService inner = new();
    await using CrisperWhisperLicensedAlignmentService service = new(
      new FakeSettingsStore(AppSettings.Default),
      inner);

    InvalidOperationException exception = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() =>
      service.AlignAsync(new SpeechAlignmentRequest("unused.wav", "hello")));

    Xunit.Assert.Contains("non-commercial research-only", exception.Message, StringComparison.Ordinal);
    Xunit.Assert.Equal(0, inner.Requests);
  }

  [Xunit.Fact]
  public async Task AlignAsync_WithCurrentAcceptance_InvokesModel()
  {
    await using FakeAlignmentService inner = new();
    AppSettings accepted = CrisperWhisperLicensePolicy.AcceptCurrentVersion(AppSettings.Default);
    await using CrisperWhisperLicensedAlignmentService service = new(
      new FakeSettingsStore(accepted),
      inner);

    SpeechAlignmentResult result = await service.AlignAsync(
      new SpeechAlignmentRequest("unused.wav", "hello"));

    Xunit.Assert.Equal("fake", result.ProviderId);
    Xunit.Assert.Equal(1, inner.Requests);
  }

  private sealed class FakeSettingsStore(AppSettings settings) : ISettingsStore
  {
    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
      Task.FromResult(settings);

    public Task SaveAsync(AppSettings value, CancellationToken cancellationToken = default) =>
      Task.CompletedTask;
  }

  private sealed class FakeAlignmentService : ISpeechAlignmentService
  {
    public int Requests { get; private set; }

    public Task<SpeechAlignmentResult> AlignAsync(
      SpeechAlignmentRequest request,
      CancellationToken cancellationToken = default)
    {
      Requests++;
      return Task.FromResult(new SpeechAlignmentResult([], "fake", TimeSpan.Zero));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }
}

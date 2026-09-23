using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Domain;
using DictateAnywhere.Core.Services;

namespace DictateAnywhere.Core.Tests;

public sealed class FaultTolerantOverlayServiceTests
{
  [Xunit.Fact]
  public async Task ShowStateAsync_WhenPresenterFails_LogsAndReturns()
  {
    ThrowingOverlayService inner = new();
    RecordingDiagnostics diagnostics = new();
    await using FaultTolerantOverlayService service = new(
      inner,
      diagnostics,
      TimeSpan.FromMilliseconds(100));

    await service.ShowStateAsync(DictationSessionState.Inserting);

    string warning = Xunit.Assert.Single(diagnostics.Warnings);
    Xunit.Assert.Contains("show Inserting", warning, StringComparison.Ordinal);
    Xunit.Assert.Contains("dictation will continue", warning, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task ShowStateAsync_WhenPresenterDoesNotComplete_TimesOutAndReturns()
  {
    HangingOverlayService inner = new();
    RecordingDiagnostics diagnostics = new();
    await using FaultTolerantOverlayService service = new(
      inner,
      diagnostics,
      TimeSpan.FromMilliseconds(25));

    await service.ShowStateAsync(DictationSessionState.Inserting)
      .WaitAsync(TimeSpan.FromSeconds(1));

    string warning = Xunit.Assert.Single(diagnostics.Warnings);
    Xunit.Assert.Contains("timed out", warning, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task ShowStateAsync_WhenCallerCancels_PropagatesCancellation()
  {
    HangingOverlayService inner = new();
    RecordingDiagnostics diagnostics = new();
    await using FaultTolerantOverlayService service = new(
      inner,
      diagnostics,
      TimeSpan.FromSeconds(1));
    using CancellationTokenSource cancellation = new();
    cancellation.Cancel();

    await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => service.ShowStateAsync(DictationSessionState.Recording, cancellationToken: cancellation.Token));

    Xunit.Assert.Empty(diagnostics.Warnings);
  }

  private sealed class ThrowingOverlayService : IOverlayService
  {
    public Task ShowStateAsync(
      DictationSessionState state,
      string? message = null,
      TimeSpan? elapsed = null,
      OverlayDisplayOptions? display = null,
      CancellationToken cancellationToken = default)
    {
      throw new InvalidOperationException("presenter failed");
    }

    public Task HideAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
  }

  private sealed class HangingOverlayService : IOverlayService
  {
    private readonly TaskCompletionSource<bool> completion =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task ShowStateAsync(
      DictationSessionState state,
      string? message = null,
      TimeSpan? elapsed = null,
      OverlayDisplayOptions? display = null,
      CancellationToken cancellationToken = default)
    {
      return completion.Task;
    }

    public Task HideAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
  }

  private sealed class RecordingDiagnostics : IDiagnostics
  {
    public List<string> Warnings { get; } = new();

    public void Info(string message)
    {
    }

    public void Warning(string message)
    {
      Warnings.Add(message);
    }

    public void Error(string message, Exception? exception = null)
    {
    }
  }
}

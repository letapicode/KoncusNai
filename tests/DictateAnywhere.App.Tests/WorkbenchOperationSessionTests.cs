using DictateAnywhere.App.Workbench;

namespace DictateAnywhere.App.Tests;

public sealed class WorkbenchOperationSessionTests
{
  [Xunit.Fact]
  public async Task TryBegin_ExposesTypedExclusiveImportState()
  {
    await using WorkbenchOperationSession session = new();

    using WorkbenchOperation? operation = session.TryBegin(WorkbenchOperationKind.FileImport);

    Xunit.Assert.NotNull(operation);
    Xunit.Assert.Equal(WorkbenchOperationKind.FileImport, operation.Kind);
    Xunit.Assert.True(session.IsBusy);
    Xunit.Assert.True(session.IsImportingFiles);
    Xunit.Assert.Null(session.TryBegin(WorkbenchOperationKind.Transcription));
  }

  [Xunit.Fact]
  public async Task CompletingAnOperation_AllowsTheNextOperation()
  {
    await using WorkbenchOperationSession session = new();
    WorkbenchOperation operation = Xunit.Assert.IsType<WorkbenchOperation>(
      session.TryBegin(WorkbenchOperationKind.FileImport));

    operation.Dispose();
    using WorkbenchOperation? nextOperation = session.TryBegin(WorkbenchOperationKind.SettingsApply);

    Xunit.Assert.False(session.IsImportingFiles);
    Xunit.Assert.NotNull(nextOperation);
    Xunit.Assert.Equal(WorkbenchOperationKind.SettingsApply, nextOperation.Kind);
  }

  [Xunit.Fact]
  public async Task TryBegin_PropagatesCallerCancellation()
  {
    await using WorkbenchOperationSession session = new();
    using CancellationTokenSource cancellationSource = new();
    using WorkbenchOperation operation = Xunit.Assert.IsType<WorkbenchOperation>(
      session.TryBegin(WorkbenchOperationKind.SettingsApply, cancellationSource.Token));

    cancellationSource.Cancel();

    Xunit.Assert.True(operation.CancellationToken.IsCancellationRequested);
  }

  [Xunit.Fact]
  public async Task TryBegin_RejectsAnAlreadyCancelledCaller()
  {
    await using WorkbenchOperationSession session = new();
    using CancellationTokenSource cancellationSource = new();
    cancellationSource.Cancel();

    Xunit.Assert.Throws<OperationCanceledException>(() =>
      session.TryBegin(WorkbenchOperationKind.SettingsApply, cancellationSource.Token));
    Xunit.Assert.False(session.IsBusy);
  }

  [Xunit.Fact]
  public async Task DisposeAsync_CancelsAndWaitsForTheActiveOperation()
  {
    WorkbenchOperationSession session = new();
    WorkbenchOperation operation = Xunit.Assert.IsType<WorkbenchOperation>(
      session.TryBegin(WorkbenchOperationKind.Transcription));

    ValueTask firstDisposal = session.DisposeAsync();
    ValueTask secondDisposal = session.DisposeAsync();

    Xunit.Assert.True(operation.CancellationToken.IsCancellationRequested);
    Xunit.Assert.False(firstDisposal.IsCompleted);
    Xunit.Assert.False(secondDisposal.IsCompleted);
    Xunit.Assert.Throws<ObjectDisposedException>(() => session.TryBegin(WorkbenchOperationKind.FileImport));

    operation.Dispose();
    await firstDisposal;
    await secondDisposal;
    Xunit.Assert.False(session.IsBusy);
  }

  [Xunit.Fact]
  public async Task OperationDispose_IsIdempotent()
  {
    await using WorkbenchOperationSession session = new();
    WorkbenchOperation operation = Xunit.Assert.IsType<WorkbenchOperation>(
      session.TryBegin(WorkbenchOperationKind.RecordingStart));

    operation.Dispose();
    operation.Dispose();

    Xunit.Assert.False(session.IsBusy);
  }
}

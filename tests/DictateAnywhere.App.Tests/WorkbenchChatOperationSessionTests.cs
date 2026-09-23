using DictateAnywhere.App.Workbench;

namespace DictateAnywhere.App.Tests;

public sealed class WorkbenchChatOperationSessionTests
{
  [Xunit.Fact]
  public async Task TryBegin_ExposesTypedExclusiveOperationState()
  {
    await using WorkbenchChatOperationSession session = new();

    using WorkbenchChatOperation? operation = session.TryBegin(WorkbenchChatOperationKind.Completion);

    Xunit.Assert.NotNull(operation);
    Xunit.Assert.Equal(WorkbenchChatOperationKind.Completion, operation.Kind);
    Xunit.Assert.True(session.IsBusy);
    Xunit.Assert.True(session.CanCancel);
    Xunit.Assert.Null(session.TryBegin(WorkbenchChatOperationKind.ModelSetup));
  }

  [Xunit.Fact]
  public async Task CompletingAnOperation_AllowsTheNextOperation()
  {
    await using WorkbenchChatOperationSession session = new();
    WorkbenchChatOperation operation = Xunit.Assert.IsType<WorkbenchChatOperation>(
      session.TryBegin(WorkbenchChatOperationKind.LocalReply));

    operation.Dispose();
    using WorkbenchChatOperation? nextOperation = session.TryBegin(WorkbenchChatOperationKind.ModelSetup);

    Xunit.Assert.True(session.CanCancel);
    Xunit.Assert.NotNull(nextOperation);
    Xunit.Assert.Equal(WorkbenchChatOperationKind.ModelSetup, nextOperation.Kind);
  }

  [Xunit.Fact]
  public async Task CancelActive_CancelsCompletionAndModelSetupButNotLocalReplies()
  {
    await using WorkbenchChatOperationSession session = new();
    using WorkbenchChatOperation completion = Xunit.Assert.IsType<WorkbenchChatOperation>(
      session.TryBegin(WorkbenchChatOperationKind.Completion));

    Xunit.Assert.Equal(WorkbenchChatOperationKind.Completion, session.CancelActive());
    Xunit.Assert.True(completion.CancellationToken.IsCancellationRequested);
    Xunit.Assert.Null(session.CancelActive());

    completion.Dispose();
    using WorkbenchChatOperation modelSetup = Xunit.Assert.IsType<WorkbenchChatOperation>(
      session.TryBegin(WorkbenchChatOperationKind.ModelSetup));
    Xunit.Assert.Equal(WorkbenchChatOperationKind.ModelSetup, session.CancelActive());
    Xunit.Assert.True(modelSetup.CancellationToken.IsCancellationRequested);

    modelSetup.Dispose();
    using WorkbenchChatOperation localReply = Xunit.Assert.IsType<WorkbenchChatOperation>(
      session.TryBegin(WorkbenchChatOperationKind.LocalReply));
    Xunit.Assert.Null(session.CancelActive());
    Xunit.Assert.False(localReply.CancellationToken.CanBeCanceled);
  }

  [Xunit.Fact]
  public async Task DisposeAsync_CancelsAndWaitsForActiveCompletion()
  {
    WorkbenchChatOperationSession session = new();
    WorkbenchChatOperation operation = Xunit.Assert.IsType<WorkbenchChatOperation>(
      session.TryBegin(WorkbenchChatOperationKind.Completion));

    ValueTask firstDisposal = session.DisposeAsync();
    ValueTask secondDisposal = session.DisposeAsync();

    Xunit.Assert.True(operation.CancellationToken.IsCancellationRequested);
    Xunit.Assert.False(firstDisposal.IsCompleted);
    Xunit.Assert.False(secondDisposal.IsCompleted);
    Xunit.Assert.Throws<ObjectDisposedException>(() => session.TryBegin(WorkbenchChatOperationKind.LocalReply));

    operation.Dispose();
    await firstDisposal;
    await secondDisposal;
    Xunit.Assert.False(session.IsBusy);
  }

  [Xunit.Fact]
  public async Task DisposeAsync_WaitsForNonCancelableLocalReply()
  {
    WorkbenchChatOperationSession session = new();
    WorkbenchChatOperation operation = Xunit.Assert.IsType<WorkbenchChatOperation>(
      session.TryBegin(WorkbenchChatOperationKind.LocalReply));

    ValueTask disposal = session.DisposeAsync();

    Xunit.Assert.False(disposal.IsCompleted);
    Xunit.Assert.False(session.CanCancel);
    operation.Dispose();
    await disposal;
  }

  [Xunit.Fact]
  public async Task OperationDispose_IsIdempotent()
  {
    await using WorkbenchChatOperationSession session = new();
    WorkbenchChatOperation operation = Xunit.Assert.IsType<WorkbenchChatOperation>(
      session.TryBegin(WorkbenchChatOperationKind.LocalReply));

    operation.Dispose();
    operation.Dispose();

    Xunit.Assert.False(session.IsBusy);
  }
}

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.History;

/// <summary>Serializes accepted History mutations and owns cancellation and shutdown.</summary>
internal sealed class HistoryCommandCoordinator : IAsyncDisposable
{
  private readonly object sync = new();
  private readonly Func<AppSettings, IDictationHistoryCommandStore> createDictationStore;
  private readonly Func<AppSettings, IChatHistoryCommandStore> createChatStore;
  private readonly SemaphoreSlim commandGate = new(1, 1);
  private readonly CancellationTokenSource lifetimeCancellationSource = new();
  private TaskCompletionSource? idleCompletion;
  private Task? disposalTask;
  private int pendingCommandCount;
  private bool disposed;

  internal HistoryCommandCoordinator(
    Func<AppSettings, IDictationHistoryCommandStore> createDictationStore,
    Func<AppSettings, IChatHistoryCommandStore> createChatStore)
  {
    this.createDictationStore = createDictationStore ?? throw new ArgumentNullException(nameof(createDictationStore));
    this.createChatStore = createChatStore ?? throw new ArgumentNullException(nameof(createChatStore));
  }

  public bool IsBusy
  {
    get
    {
      lock (sync)
      {
        return pendingCommandCount > 0;
      }
    }
  }

  public Task<HistoryCommandResult> RecordDictationAsync(
    AppSettings settings,
    DictationHistoryRecord record,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(settings);
    ArgumentNullException.ThrowIfNull(record);
    return ExecuteAsync(
      settings,
      async token =>
      {
        await createDictationStore(settings).RecordAsync(record.Normalize(), token).ConfigureAwait(true);
        return HistoryCommandResult.Succeeded();
      },
      cancellationToken);
  }

  public Task<HistoryCommandResult> UpdateDictationAsync(
    AppSettings settings,
    DictationHistoryRecord record,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(settings);
    ArgumentNullException.ThrowIfNull(record);
    return ExecuteAsync(
      settings,
      async token => await createDictationStore(settings)
        .UpdateAsync(record.Normalize(), token)
        .ConfigureAwait(true)
        ? HistoryCommandResult.Succeeded()
        : HistoryCommandResult.NotFound(),
      cancellationToken);
  }

  public Task<HistoryCommandResult> DeleteDictationSessionsAsync(
    AppSettings settings,
    IEnumerable<string> sessionIds,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(settings);
    ArgumentNullException.ThrowIfNull(sessionIds);
    string[] stableSessionIds = sessionIds.ToArray();
    return ExecuteAsync(
      settings,
      async token => ToDeleteResult(await createDictationStore(settings)
        .DeleteSessionsAsync(stableSessionIds, token)
        .ConfigureAwait(true)),
      cancellationToken);
  }

  public Task<HistoryCommandResult> SaveChatAsync(
    AppSettings settings,
    ChatHistoryRecord record,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(settings);
    ArgumentNullException.ThrowIfNull(record);
    return ExecuteAsync(
      settings,
      async token =>
      {
        await createChatStore(settings).SaveAsync(record.Normalize(), token).ConfigureAwait(true);
        return HistoryCommandResult.Succeeded();
      },
      cancellationToken);
  }

  public Task<HistoryCommandResult> DeleteChatConversationsAsync(
    AppSettings settings,
    IEnumerable<string> conversationIds,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(settings);
    ArgumentNullException.ThrowIfNull(conversationIds);
    string[] stableConversationIds = conversationIds.ToArray();
    return ExecuteAsync(
      settings,
      async token => ToDeleteResult(await createChatStore(settings)
        .DeleteConversationsAsync(stableConversationIds, token)
        .ConfigureAwait(true)),
      cancellationToken);
  }

  public ValueTask DisposeAsync()
  {
    Task pendingCompletion;
    lock (sync)
    {
      if (disposalTask is not null)
      {
        return new ValueTask(disposalTask);
      }

      disposed = true;
      pendingCompletion = idleCompletion?.Task ?? Task.CompletedTask;
      disposalTask = CompleteDisposalAsync(pendingCompletion);
    }

    TryCancel(lifetimeCancellationSource);
    return new ValueTask(disposalTask);
  }

  private Task<HistoryCommandResult> ExecuteAsync(
    AppSettings settings,
    Func<CancellationToken, Task<HistoryCommandResult>> command,
    CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    lock (sync)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      pendingCommandCount++;
      if (pendingCommandCount == 1)
      {
        idleCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      }
    }

    return RunAsync(settings, command, cancellationToken);
  }

  [SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "The command method owns and disposes the linked cancellation source.")]
  private async Task<HistoryCommandResult> RunAsync(
    AppSettings settings,
    Func<CancellationToken, Task<HistoryCommandResult>> command,
    CancellationToken callerCancellationToken)
  {
    using CancellationTokenSource commandCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
      callerCancellationToken,
      lifetimeCancellationSource.Token);
    bool ownsGate = false;
    try
    {
      await commandGate.WaitAsync(commandCancellationSource.Token).ConfigureAwait(true);
      ownsGate = true;
      HistoryCommandResult result = await command(commandCancellationSource.Token).ConfigureAwait(true);
      commandCancellationSource.Token.ThrowIfCancellationRequested();
      callerCancellationToken.ThrowIfCancellationRequested();
      return result;
    }
    catch (OperationCanceledException) when (commandCancellationSource.IsCancellationRequested)
    {
      callerCancellationToken.ThrowIfCancellationRequested();
      return HistoryCommandResult.Canceled();
    }
    catch (Exception ex) when (HistoryPersistenceFailureClassifier.IsExpected(ex))
    {
      return HistoryCommandResult.Unavailable(ex);
    }
    finally
    {
      if (ownsGate)
      {
        commandGate.Release();
      }

      CompletePendingCommand();
    }
  }

  private static HistoryCommandResult ToDeleteResult(int deleted)
  {
    return deleted > 0
      ? HistoryCommandResult.Succeeded(deleted)
      : HistoryCommandResult.NotFound();
  }

  private void CompletePendingCommand()
  {
    TaskCompletionSource? completion = null;
    lock (sync)
    {
      pendingCommandCount--;
      if (pendingCommandCount == 0)
      {
        completion = idleCompletion;
        idleCompletion = null;
      }
    }

    completion?.TrySetResult();
  }

  private async Task CompleteDisposalAsync(Task pendingCompletion)
  {
    await pendingCompletion.ConfigureAwait(false);
    commandGate.Dispose();
    lifetimeCancellationSource.Dispose();
  }

  private static void TryCancel(CancellationTokenSource cancellationSource)
  {
    try
    {
      cancellationSource.Cancel();
    }
    catch (ObjectDisposedException)
    {
      // Disposal already completed on a concurrent call.
    }
  }
}

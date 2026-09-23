using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Settings;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class SettingsAutoSaveCoordinatorTests
{
  [Xunit.Fact]
  public async Task Schedule_CollapsesRapidChangesToTheLatestSnapshot()
  {
    TaskCompletionSource<AppSettings> saved = new(TaskCreationOptions.RunContinuationsAsynchronously);
    await using SettingsAutoSaveCoordinator coordinator = new(
      (settings, _) =>
      {
        saved.TrySetResult(settings);
        return Task.CompletedTask;
      },
      TimeSpan.FromMilliseconds(25));

    coordinator.Schedule(AppSettings.Default with { ChatOutputFontSize = 13 });
    coordinator.Schedule(AppSettings.Default with { ChatOutputFontSize = 20 });

    AppSettings result = await saved.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Xunit.Assert.Equal(20, result.ChatOutputFontSize);
  }

  [Xunit.Fact]
  public async Task FlushAsync_CancelsDelayAndSavesImmediately()
  {
    List<AppSettings> saved = new();
    await using SettingsAutoSaveCoordinator coordinator = new(
      (settings, _) =>
      {
        saved.Add(settings);
        return Task.CompletedTask;
      },
      TimeSpan.FromMinutes(1));

    coordinator.Schedule(AppSettings.Default with { ChatOutputFontSize = 13 });
    bool success = await coordinator.FlushAsync(AppSettings.Default with { ChatOutputFontSize = 17 });

    Xunit.Assert.True(success);
    AppSettings result = Xunit.Assert.Single(saved);
    Xunit.Assert.Equal(17, result.ChatOutputFontSize);
  }

  [Xunit.Fact]
  public async Task FlushAsync_ReportsExpectedPersistenceFailures()
  {
    SettingsAutoSaveStatus? status = null;
    await using SettingsAutoSaveCoordinator coordinator = new(
      (_, _) => throw new InvalidOperationException("disk unavailable"),
      TimeSpan.Zero);
    coordinator.StatusChanged += (_, value) => status = value;

    bool success = await coordinator.FlushAsync(AppSettings.Default);

    Xunit.Assert.False(success);
    Xunit.Assert.NotNull(status);
    Xunit.Assert.Equal(SettingsAutoSaveState.Failed, status.State);
    Xunit.Assert.Equal("disk unavailable", status.ErrorMessage);
  }

  [Xunit.Fact]
  public async Task DisposeAsync_CancelsAndWaitsForActiveSave()
  {
    TaskCompletionSource<bool> saveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource<bool> allowSaveToFinish = new(TaskCreationOptions.RunContinuationsAsynchronously);
    SettingsAutoSaveCoordinator coordinator = new(
      async (_, _) =>
      {
        saveStarted.TrySetResult(true);
        await allowSaveToFinish.Task;
      },
      TimeSpan.Zero);

    coordinator.Schedule(AppSettings.Default);
    await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

    Task disposal = coordinator.DisposeAsync().AsTask();
    Xunit.Assert.False(disposal.IsCompleted);

    allowSaveToFinish.SetResult(true);
    await disposal.WaitAsync(TimeSpan.FromSeconds(2));
    await coordinator.DisposeAsync();
  }
}

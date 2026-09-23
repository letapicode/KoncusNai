using System;

namespace DictateAnywhere.App.Settings;

/// <summary>
/// Categories of asynchronous operations governed by the settings editing controller.
/// </summary>
public enum SettingsOperationKind
{
  Idle,
  Load,
  Save,
  RefreshModels,
  RefreshAudioDevices,
  DownloadModel,
  ActivateModel,
  DeleteModel,
  RunBenchmark,
  Import,
  Export,
}

/// <summary>
/// Lifecycle phase for a settings operation.
/// </summary>
public enum SettingsOperationPhase
{
  Idle,
  Pending,
  Running,
  Succeeded,
  Failed,
}

/// <summary>
/// Strongly-typed status record representing the observable state of a settings operation.
/// </summary>
public sealed record SettingsOperationStatus(
  SettingsOperationKind Kind,
  SettingsOperationPhase Phase,
  string Message)
{
  public bool IsBusy => Phase is SettingsOperationPhase.Running or SettingsOperationPhase.Pending;

  public static SettingsOperationStatus Idle { get; } =
    new(SettingsOperationKind.Idle, SettingsOperationPhase.Idle, "Ready");

  public static SettingsOperationStatus Running(SettingsOperationKind kind, string message) =>
    new(kind, SettingsOperationPhase.Running, message);

  public static SettingsOperationStatus Succeeded(SettingsOperationKind kind, string message) =>
    new(kind, SettingsOperationPhase.Succeeded, message);

  public static SettingsOperationStatus Failed(SettingsOperationKind kind, string message) =>
    new(kind, SettingsOperationPhase.Failed, message);
}

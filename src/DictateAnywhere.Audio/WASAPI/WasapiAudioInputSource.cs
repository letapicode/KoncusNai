using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DictateAnywhere.Audio.WASAPI;

public sealed class WasapiAudioInputSource : IAudioInputSource
{
  private readonly object sync = new();
  private readonly MMDeviceEnumerator enumerator = new();
  private WasapiCapture? capture;
  private NativeCaptureLifetime? captureLifetime;
  private Task captureDisposalTask = Task.CompletedTask;
  private TaskCompletionSource? disposal;
  private Task? disposalDriver;
  private volatile bool disposed;

  public event EventHandler<AudioRawDataEventArgs>? DataAvailable;
  public event EventHandler<AudioInputErrorEventArgs>? DeviceError;

  public IReadOnlyList<AudioInputDevice> GetInputDevices()
  {
    string? defaultId = GetDefaultInputDeviceId();
    MMDeviceCollection collection = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
    List<AudioInputDevice> devices = new(collection.Count);
    foreach (MMDevice device in collection)
    {
      devices.Add(new AudioInputDevice(
        DeviceId: device.ID,
        DisplayName: device.FriendlyName,
        IsDefault: string.Equals(device.ID, defaultId, StringComparison.Ordinal)));
    }

    return devices;
  }

  public string? GetDefaultInputDeviceId()
  {
    using MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
    return device.ID;
  }

  public async Task StartAsync(string? preferredDeviceId, CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    ThrowIfDisposed();
    NativeCaptureLifetime? startedLifetime = null;
    try
    {
      while (true)
      {
        Task previous;
        lock (sync) previous = captureDisposalTask;
        await previous.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (sync)
        {
          ThrowIfDisposed();
          cancellationToken.ThrowIfCancellationRequested();
          if (!ReferenceEquals(previous, captureDisposalTask)) continue;
          if (capture is not null) throw new AudioCaptureException("Audio input source is already capturing.");
          WasapiCapture created = new(ResolveDevice(preferredDeviceId));
          capture = created;
          startedLifetime = captureLifetime = new(() => Task.Run(created.StopRecording), () => Task.Run(created.Dispose));
          capture.DataAvailable += OnDataAvailable;
          capture.RecordingStopped += OnRecordingStopped;
          capture.StartRecording();
          break;
        }
      }
    }
    catch
    {
      if (startedLifetime is not null)
      {
        // NAudio initialization throws before launching its capture thread.
        startedLifetime.RecordingStopped();
        lock (sync)
        {
          if (ReferenceEquals(captureLifetime, startedLifetime)) DetachCaptureLocked();
          captureDisposalTask = startedLifetime.DisposeAsync();
        }
        await captureDisposalTask.ConfigureAwait(false);
      }
      throw;
    }
  }

  public async Task StopAsync(CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    ThrowIfDisposed();
    NativeCaptureLifetime? owner;
    lock (sync) owner = captureLifetime;
    // Once accepted, drain the actual calls and stopped acknowledgment. Caller
    // cancellation cannot abandon native work; the pipeline rejects its result.
    if (owner is not null) await owner.DisposeAsync().ConfigureAwait(false);
  }

  public ValueTask DisposeAsync()
  {
    TaskCompletionSource source;
    NativeCaptureLifetime? owner;
    lock (sync)
    {
      if (disposal is not null) return new ValueTask(disposal.Task);
      disposed = true;
      source = disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
      owner = captureLifetime;
    }
    disposalDriver = DisposeCoreAsync(source, owner);
    return new ValueTask(source.Task);
  }

  [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "Disposal reports native failure after its retained owner drains and releases the enumerator independently.")]
  private async Task DisposeCoreAsync(TaskCompletionSource source, NativeCaptureLifetime? owner)
  {
    List<Exception> failures = [];
    try { if (owner is not null) await owner.DisposeAsync().ConfigureAwait(false); }
    catch (Exception exception) { failures.Add(exception); }
    lock (sync) DetachCaptureLocked();
    try { enumerator.Dispose(); }
    catch (Exception exception) { failures.Add(exception); }
    if (failures.Count == 0) source.TrySetResult();
    else { source.TrySetException(new AggregateException(failures)); _ = source.Task.Exception; }
  }

  private void DetachCaptureLocked()
  {
    if (capture is null) return;
    capture.DataAvailable -= OnDataAvailable;
    capture.RecordingStopped -= OnRecordingStopped;
    capture = null;
  }

  private void OnDataAvailable(object? sender, WaveInEventArgs e)
  {
    WasapiCapture? currentCapture;
    lock (sync) currentCapture = disposed || !ReferenceEquals(capture, sender) ? null : capture;
    if (currentCapture is null || e.BytesRecorded <= 0) return;
    byte[] copy = new byte[e.BytesRecorded];
    Buffer.BlockCopy(e.Buffer, 0, copy, 0, e.BytesRecorded);
    DataAvailable?.Invoke(this, new AudioRawDataEventArgs(
      copy, e.BytesRecorded, currentCapture.WaveFormat.SampleRate,
      currentCapture.WaveFormat.Channels, currentCapture.WaveFormat.BitsPerSample,
      sourceFormatIsFloat: currentCapture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat));
  }

  private void OnRecordingStopped(object? sender, StoppedEventArgs e)
  {
    NativeCaptureLifetime? owner;
    lock (sync)
    {
      if (capture is null || !ReferenceEquals(capture, sender)) return;
      owner = captureLifetime;
      DetachCaptureLocked();
      if (owner is not null) captureDisposalTask = owner.DisposeAsync();
    }
    try
    {
      if (e.Exception is not null && !disposed) DeviceError?.Invoke(this, new AudioInputErrorEventArgs(e.Exception));
    }
    finally { owner?.RecordingStopped(); }
  }
  private MMDevice ResolveDevice(string? preferredDeviceId)
  {
    MMDeviceCollection devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
    string? defaultId = null;
    try
    {
      defaultId = GetDefaultInputDeviceId();
    }
    catch (InvalidOperationException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }

    IReadOnlyList<AudioInputDevice> inputDevices = devices
      .Select(device => new AudioInputDevice(
        device.ID,
        device.FriendlyName,
        string.Equals(device.ID, defaultId, StringComparison.Ordinal)))
      .ToArray();
    string? selectedId = AudioDeviceCatalog.ResolveInputDeviceId(inputDevices, preferredDeviceId, defaultId);
    MMDevice? selected = !string.IsNullOrWhiteSpace(selectedId)
      ? devices.FirstOrDefault(device => string.Equals(device.ID, selectedId, StringComparison.Ordinal))
      : null;

    return selected ?? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(disposed, this);
  }
}

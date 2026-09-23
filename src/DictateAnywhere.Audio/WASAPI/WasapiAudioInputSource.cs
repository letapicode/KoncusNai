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
  private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
  private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(3);
  private readonly object sync = new();
  private readonly MMDeviceEnumerator enumerator = new();
  private WasapiCapture? capture;
  private Task captureDisposalTask = Task.CompletedTask;
  private bool disposed;

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

  public Task StartAsync(string? preferredDeviceId, CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    ThrowIfDisposed();

    lock (sync)
    {
      if (capture is not null)
      {
        throw new AudioCaptureException("Audio input source is already capturing.");
      }

      capture = new WasapiCapture(ResolveDevice(preferredDeviceId));
      capture.DataAvailable += OnDataAvailable;
      capture.RecordingStopped += OnRecordingStopped;
      capture.StartRecording();
    }

    return Task.CompletedTask;
  }

  public async Task StopAsync(CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    ThrowIfDisposed();

    WasapiCapture? currentCapture;
    lock (sync)
    {
      currentCapture = capture;
    }

    if (currentCapture is null)
    {
      return;
    }

    try
    {
      await Task.Run(() => currentCapture.StopRecording(), cancellationToken)
        .WaitAsync(StopTimeout, cancellationToken)
        .ConfigureAwait(false);
      await WaitForStoppedCaptureDisposalAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (TimeoutException ex)
    {
      await ForceDisposeCaptureAsync(currentCapture).ConfigureAwait(false);
      throw new AudioCaptureException("Timed out while stopping WASAPI capture.", ex);
    }
    catch (InvalidOperationException ex)
    {
      await ForceDisposeCaptureAsync(currentCapture).ConfigureAwait(false);
      throw new AudioCaptureException("Failed to stop WASAPI capture cleanly.", ex);
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    WasapiCapture? captureToDispose = null;
    Task pendingCaptureDisposal;
    lock (sync)
    {
      if (capture is not null)
      {
        capture.DataAvailable -= OnDataAvailable;
        capture.RecordingStopped -= OnRecordingStopped;
        captureToDispose = capture;
        capture = null;
      }

      disposed = true;
      pendingCaptureDisposal = captureDisposalTask;
    }

    if (captureToDispose is not null)
    {
      await ForceDisposeCaptureAsync(captureToDispose).ConfigureAwait(false);
    }

    try
    {
      await pendingCaptureDisposal.WaitAsync(DisposeTimeout).ConfigureAwait(false);
    }
    catch (TimeoutException)
    {
      // A misbehaving native capture must not block process teardown indefinitely.
    }

    enumerator.Dispose();
  }

  private void OnDataAvailable(object? sender, WaveInEventArgs e)
  {
    WasapiCapture? currentCapture;
    lock (sync)
    {
      currentCapture = capture;
    }

    if (currentCapture is null || e.BytesRecorded <= 0)
    {
      return;
    }

    byte[] copy = new byte[e.BytesRecorded];
    Buffer.BlockCopy(e.Buffer, 0, copy, 0, e.BytesRecorded);
    DataAvailable?.Invoke(this, new AudioRawDataEventArgs(
      copy,
      e.BytesRecorded,
      currentCapture.WaveFormat.SampleRate,
      currentCapture.WaveFormat.Channels,
      currentCapture.WaveFormat.BitsPerSample,
      sourceFormatIsFloat: currentCapture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat));
  }

  private void OnRecordingStopped(object? sender, StoppedEventArgs e)
  {
    lock (sync)
    {
      if (capture is not null)
      {
        capture.DataAvailable -= OnDataAvailable;
        capture.RecordingStopped -= OnRecordingStopped;
        WasapiCapture toDispose = capture;
        capture = null;
        captureDisposalTask = DisposeStoppedCaptureAsync(captureDisposalTask, toDispose);
      }
    }

    if (e.Exception is not null)
    {
      DeviceError?.Invoke(this, new AudioInputErrorEventArgs(e.Exception));
    }
  }

  private static async Task DisposeStoppedCaptureAsync(Task previousDisposal, WasapiCapture captureToDispose)
  {
    await previousDisposal.ConfigureAwait(false);
    await Task.Run(() =>
    {
      try
      {
        captureToDispose.Dispose();
      }
      catch (ObjectDisposedException)
      {
      }
      catch (InvalidOperationException)
      {
      }
    }).ConfigureAwait(false);
  }

  private async Task WaitForStoppedCaptureDisposalAsync(CancellationToken cancellationToken)
  {
    Task pendingDisposal;
    lock (sync)
    {
      pendingDisposal = captureDisposalTask;
    }

    try
    {
      await pendingDisposal.WaitAsync(DisposeTimeout, cancellationToken).ConfigureAwait(false);
    }
    catch (TimeoutException)
    {
      // Device teardown remains bounded; the task is retained and observed by DisposeAsync.
    }
  }

  private async Task ForceDisposeCaptureAsync(WasapiCapture captureToDispose)
  {
    DetachCapture(captureToDispose);

    try
    {
      await Task.Run(captureToDispose.StopRecording)
        .WaitAsync(StopTimeout)
        .ConfigureAwait(false);
    }
    catch (InvalidOperationException)
    {
      // Already stopped; proceed to disposal.
    }
    catch (TimeoutException)
    {
      // Stop hang should not block process teardown.
    }

    try
    {
      await Task.Run(captureToDispose.Dispose)
        .WaitAsync(DisposeTimeout)
        .ConfigureAwait(false);
    }
    catch (ObjectDisposedException)
    {
    }
    catch (InvalidOperationException)
    {
    }
    catch (TimeoutException)
    {
      // Dispose hang should not block process teardown.
    }
  }

  private void DetachCapture(WasapiCapture expectedCapture)
  {
    lock (sync)
    {
      if (!ReferenceEquals(capture, expectedCapture))
      {
        return;
      }

      capture.DataAvailable -= OnDataAvailable;
      capture.RecordingStopped -= OnRecordingStopped;
      capture = null;
    }
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

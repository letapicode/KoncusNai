using System;

namespace DictateAnywhere.Core.Contracts;

public sealed record AudioCaptureResult(byte[] Pcm16Mono, int SampleRateHz, TimeSpan Duration);

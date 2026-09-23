using System;

namespace DictateAnywhere.Core.Contracts;

public sealed record TranscriptionResult(string Text, string ModelId, TimeSpan Duration);

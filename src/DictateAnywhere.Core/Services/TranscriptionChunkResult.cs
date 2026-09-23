using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Core.Services;

internal sealed record TranscriptionChunkResult(int SequenceNumber, TranscriptionResult Result);

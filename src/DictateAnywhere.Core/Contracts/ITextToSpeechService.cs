using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.Core.Contracts;

/// <summary>Produces a local WAV file from readable text.</summary>
public interface ITextToSpeechService
{
  Task<TextToSpeechResult> SynthesizeAsync(
    TextToSpeechRequest request,
    CancellationToken cancellationToken = default);
}

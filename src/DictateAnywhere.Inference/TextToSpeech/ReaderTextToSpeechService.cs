using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Inference;

/// <summary>Routes each reader language to its native local speech engine.</summary>
public sealed class ReaderTextToSpeechService : ITextToSpeechService, IAsyncDisposable
{
  private readonly ITextToSpeechService kokoroService;
  private readonly ITextToSpeechService indicParlerService;

  [SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "ReaderTextToSpeechService takes ownership of both provider services and disposes them.")]
  public ReaderTextToSpeechService()
    : this(new KokoroTextToSpeechService(), new IndicParlerTextToSpeechService())
  {
  }

  internal ReaderTextToSpeechService(ITextToSpeechService kokoroService, ITextToSpeechService indicParlerService)
  {
    this.kokoroService = kokoroService ?? throw new ArgumentNullException(nameof(kokoroService));
    this.indicParlerService = indicParlerService ?? throw new ArgumentNullException(nameof(indicParlerService));
  }

  public Task<TextToSpeechResult> SynthesizeAsync(
    TextToSpeechRequest request,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    TextToSpeechRequest normalized = request.Normalize();
    ITextToSpeechService selected = normalized.ProviderId switch
    {
      null or KokoroTextToSpeechService.ProviderId => kokoroService,
      IndicParlerTextToSpeechService.ProviderId => indicParlerService,
      _ => throw new ArgumentException($"Unknown text-to-speech provider '{normalized.ProviderId}'.", nameof(request)),
    };
    return selected.SynthesizeAsync(normalized, cancellationToken);
  }

  public async ValueTask DisposeAsync()
  {
    if (kokoroService is IAsyncDisposable disposableKokoro)
    {
      await disposableKokoro.DisposeAsync().ConfigureAwait(false);
    }

    if (indicParlerService is IAsyncDisposable disposableIndicParler)
    {
      await disposableIndicParler.DisposeAsync().ConfigureAwait(false);
    }
  }
}

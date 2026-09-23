using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Domain;

namespace DictateAnywhere.Core.Contracts;

public interface IOverlayService
{
  Task ShowStateAsync(
    DictationSessionState state,
    string? message = null,
    TimeSpan? elapsed = null,
    OverlayDisplayOptions? display = null,
    CancellationToken cancellationToken = default);

  Task HideAsync(CancellationToken cancellationToken = default);
}

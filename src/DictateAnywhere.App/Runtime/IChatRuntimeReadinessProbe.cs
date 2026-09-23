using System;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.App.Runtime;

internal interface IChatRuntimeReadinessProbe
{
  Task<LocalChatRuntimeReadiness> CheckGemmaChatAsync(CancellationToken cancellationToken = default);

  Task<LocalChatRuntimeReadiness> RepairGemmaChatAsync(
    IProgress<double>? progress = null,
    CancellationToken cancellationToken = default);
}

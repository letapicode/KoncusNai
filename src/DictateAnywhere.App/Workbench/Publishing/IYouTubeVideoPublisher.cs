using System;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.App.Workbench.Publishing;

internal interface IYouTubeVideoPublisher
{
  Task ConnectAsync(YouTubeOAuthConfiguration configuration, CancellationToken cancellationToken = default);
  Task<bool> HasStoredAuthorizationAsync(CancellationToken cancellationToken = default);
  Task DisconnectAsync(CancellationToken cancellationToken = default);
  Task<YouTubeUploadResult> UploadAsync(
    YouTubeOAuthConfiguration configuration,
    YouTubeUploadRequest request,
    IProgress<YouTubeUploadProgress>? progress = null,
    CancellationToken cancellationToken = default);
}

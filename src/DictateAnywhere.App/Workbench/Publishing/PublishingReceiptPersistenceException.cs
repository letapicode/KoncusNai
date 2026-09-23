using System;
using System.IO;

namespace DictateAnywhere.App.Workbench.Publishing;

internal sealed class PublishingReceiptPersistenceException(YouTubePublishingJob job, Exception innerException)
  : IOException("The video was uploaded, but its local receipt could not be saved. Check YouTube before starting another job.", innerException)
{
  public YouTubePublishingJob Job { get; } = job with { Episodes = job.Episodes.ToArray() };
}

using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Composition;

public interface ISettingsFileTransferService
{
  Task<AppSettings> ImportAsync(string path, CancellationToken cancellationToken = default);

  Task ExportAsync(string path, AppSettings settings, CancellationToken cancellationToken = default);
}

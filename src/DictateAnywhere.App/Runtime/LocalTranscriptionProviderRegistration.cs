using System;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Runtime;

internal sealed record LocalTranscriptionProviderRegistration(
  LocalTranscriptionProviderDefinition Definition,
  Func<AppSettings, IDiagnostics?, ITranscriptionService> Factory,
  Func<IProviderModelManager> ModelManagerFactory);

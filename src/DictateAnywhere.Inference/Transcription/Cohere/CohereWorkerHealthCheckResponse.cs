using System.Text.Json.Serialization;

namespace DictateAnywhere.Inference;

internal sealed class CohereWorkerHealthCheckResponse
{
  [JsonConstructor]
  public CohereWorkerHealthCheckResponse(string? healthCheck)
  {
    HealthCheck = healthCheck;
  }

  public string? HealthCheck { get; }
}

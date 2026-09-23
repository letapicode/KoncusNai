namespace DictateAnywhere.Inference;

internal sealed record CohereWorkerHealthCheckRequest(string Operation = "health_check");

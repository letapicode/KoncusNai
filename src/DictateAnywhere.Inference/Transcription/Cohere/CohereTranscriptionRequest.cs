namespace DictateAnywhere.Inference;

internal sealed record CohereTranscriptionRequest(
  string AudioPath,
  string Language,
  bool Punctuation);

namespace DictateAnywhere.Insertion;

public sealed record InputDispatchResult(bool Success, string? ErrorMessage)
{
  // False is an explicit guarantee, not an inference from a generic failure.
  public bool MayHaveDispatched { get; init; } = true;
  public static InputDispatchResult Ok { get; } = new(true, null);
}

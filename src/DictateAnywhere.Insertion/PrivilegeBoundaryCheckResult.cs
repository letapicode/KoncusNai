namespace DictateAnywhere.Insertion;

public sealed record PrivilegeBoundaryCheckResult(bool Allowed, string? ErrorMessage)
{
  public static PrivilegeBoundaryCheckResult AllowedResult { get; } = new(true, null);
}

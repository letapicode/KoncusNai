namespace DictateAnywhere.Core.Contracts;

public sealed record InsertionResult(InsertionOutcome Outcome, InsertionMethod MethodUsed, string? ErrorMessage)
{
  public InsertionBlockReason BlockReason { get; init; } = InsertionBlockReason.Unspecified;

  public bool RecoveryCopyAvailable { get; init; }

  public bool SafeToRetry { get; init; }

  public InsertionResult(bool success, InsertionMethod methodUsed, string? errorMessage)
    : this(success ? InsertionOutcome.VerifiedInserted : InsertionOutcome.UnknownOutcome, methodUsed, errorMessage)
  {
  }

  public bool Success => Outcome == InsertionOutcome.VerifiedInserted;

  public bool WasDispatched =>
    Outcome is InsertionOutcome.VerifiedInserted or InsertionOutcome.Dispatched or InsertionOutcome.UnknownOutcome;

  public bool RequiresVisibleConfirmation => Outcome == InsertionOutcome.Dispatched;

  public bool CanRecoverTranscript =>
    Outcome == InsertionOutcome.UnknownOutcome
    || (Outcome == InsertionOutcome.Blocked && BlockReason == InsertionBlockReason.TargetChanged);

  public static InsertionResult Verified(InsertionMethod methodUsed, string? errorMessage = null)
  {
    return new(InsertionOutcome.VerifiedInserted, methodUsed, errorMessage);
  }

  public static InsertionResult Dispatched(InsertionMethod methodUsed, string? errorMessage = null)
  {
    return new(InsertionOutcome.Dispatched, methodUsed, errorMessage);
  }

  public static InsertionResult Blocked(
    InsertionMethod methodUsed,
    string errorMessage,
    InsertionBlockReason blockReason = InsertionBlockReason.Unspecified)
  {
    return new InsertionResult(InsertionOutcome.Blocked, methodUsed, errorMessage)
    {
      BlockReason = blockReason,
    };
  }

  public static InsertionResult Unknown(InsertionMethod methodUsed, string? errorMessage)
  {
    return new(InsertionOutcome.UnknownOutcome, methodUsed, errorMessage);
  }
}

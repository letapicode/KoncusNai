namespace DictateAnywhere.Inference;

public enum InferenceFailureReason
{
  Unknown = 0,
  CapturePayloadEmpty = 1,
  InputFileMissing = 2,
  OutputPathInvalid = 3,
  ProcessStartFailed = 4,
  ProcessTimedOut = 5,
  ProcessExitedWithError = 6,
  ModelMissing = 7,
  RuntimeUnavailable = 8,
}

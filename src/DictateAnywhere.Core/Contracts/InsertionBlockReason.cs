namespace DictateAnywhere.Core.Contracts;

public enum InsertionBlockReason
{
  Unspecified = 0,
  TargetChanged = 1,
  NoActiveTarget = 2,
  PrivilegeBoundary = 3,
  BlockedApplication = 4,
  SecureField = 5,
  NonEditableTarget = 6,
}

using System.Collections.Generic;

namespace DictateAnywhere.Diagnostics;

public sealed record UserFacingDiagnosticError(
  DiagnosticFailureCategory Category,
  string Title,
  string Message,
  IReadOnlyList<string> NextSteps,
  string? RemediationCode = null);

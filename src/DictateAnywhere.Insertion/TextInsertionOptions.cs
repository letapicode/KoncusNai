using System;
using System.Collections.Generic;

namespace DictateAnywhere.Insertion;

public sealed record TextInsertionOptions(
  bool EnableSecureFieldDetection,
  IReadOnlyList<string> BlockedProcessNames,
  bool EnableElevatedInsertion,
  TimeSpan ClipboardConsumptionWindow = default,
  TimeSpan BrowserClipboardConsumptionWindow = default,
  TimeSpan VerificationPollingInterval = default,
  TimeSpan TypingVerificationWindow = default)
{
  public static TextInsertionOptions Default { get; } = new(
    EnableSecureFieldDetection: true,
    BlockedProcessNames: Array.Empty<string>(),
    EnableElevatedInsertion: false,
    ClipboardConsumptionWindow: TimeSpan.FromMilliseconds(175),
    BrowserClipboardConsumptionWindow: TimeSpan.FromMilliseconds(350),
    VerificationPollingInterval: TimeSpan.FromMilliseconds(35),
    TypingVerificationWindow: TimeSpan.FromMilliseconds(350));
}

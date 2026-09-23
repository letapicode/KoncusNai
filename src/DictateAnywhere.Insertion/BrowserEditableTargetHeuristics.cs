using System;
using System.Collections.Generic;
using System.Windows.Automation;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Insertion;

internal enum BrowserEditableTargetSource
{
  None = 0,
  RawFocusedElement = 1,
  FocusedAncestor = 2,
  FocusedDescendant = 3,
  ForegroundWindowSubtree = 4,
}

internal sealed record BrowserEditableTargetCandidate(
  string? AutomationId,
  string? AutomationName,
  string? ClassName,
  string? ControlTypeName,
  string? FrameworkId,
  ScreenBounds? Bounds,
  bool IsKeyboardFocusable,
  bool IsPasswordProtected,
  bool IsReadOnly,
  bool SupportsWritableValuePattern,
  bool SupportsTextPattern,
  string? VerificationMode,
  string? ObservedText,
  BrowserEditableTargetSource Source,
  int SearchOrder,
  AutomationElement? Element = null);

internal sealed record BrowserEditableTargetSelection(
  BrowserEditableTargetCandidate? Candidate,
  BrowserEditableTargetSource ResolutionSource,
  bool PromotedFromRawFocus);

internal static class BrowserEditableTargetHeuristics
{
  private static readonly string[] PreferredSemanticTokens =
  [
    "prompt",
    "message",
    "composer",
    "editor",
    "chat",
    "input",
    "textbox",
  ];
  private static readonly string[] AccessibilityPlaceholderTokens =
  [
    "not accessible at this time",
    "enable screen reader optimized",
    "screen reader optimized",
  ];

  internal static BrowserEditableTargetSelection Select(
    BrowserEditableTargetCandidate? rawCandidate,
    IReadOnlyList<BrowserEditableTargetCandidate> candidates,
    ScreenBounds? foregroundWindowBounds)
  {
    BrowserEditableTargetCandidate? bestCandidate = null;
    int bestScore = int.MinValue;

    if (rawCandidate is not null)
    {
      if (IsUrlBarCandidate(rawCandidate))
      {
        return new BrowserEditableTargetSelection(rawCandidate, rawCandidate.Source, PromotedFromRawFocus: false);
      }

      if (IsStrongEditableCandidate(rawCandidate))
      {
        return new BrowserEditableTargetSelection(rawCandidate, rawCandidate.Source, PromotedFromRawFocus: false);
      }
    }

    foreach (BrowserEditableTargetCandidate candidate in candidates)
    {
      if (!IsEditableCandidate(candidate))
      {
        continue;
      }

      int score = ScoreCandidate(candidate, foregroundWindowBounds);
      if (bestCandidate is null || score > bestScore)
      {
        bestCandidate = candidate;
        bestScore = score;
      }
    }

    if (bestCandidate is not null)
    {
      bool promoted = rawCandidate is not null && !MatchesTarget(rawCandidate, bestCandidate);
      return new BrowserEditableTargetSelection(bestCandidate, bestCandidate.Source, promoted);
    }

    return new BrowserEditableTargetSelection(rawCandidate, rawCandidate?.Source ?? BrowserEditableTargetSource.None, PromotedFromRawFocus: false);
  }

  internal static bool IsEditableCandidate(BrowserEditableTargetCandidate candidate)
  {
    ArgumentNullException.ThrowIfNull(candidate);

    if (candidate.IsPasswordProtected || candidate.IsReadOnly)
    {
      return false;
    }

    if (IsAccessibilityPlaceholder(candidate))
    {
      return false;
    }

    bool supportedTextSurface = candidate.IsKeyboardFocusable
      && IsEditableControlType(candidate.ControlTypeName)
      && HasVisibleBounds(candidate.Bounds);

    return candidate.SupportsWritableValuePattern
      || (candidate.SupportsTextPattern && supportedTextSurface)
      || supportedTextSurface;
  }

  private static bool IsStrongEditableCandidate(BrowserEditableTargetCandidate candidate)
  {
    return candidate.SupportsWritableValuePattern
      || (candidate.SupportsTextPattern
          && candidate.IsKeyboardFocusable
          && IsEditableControlType(candidate.ControlTypeName));
  }

  private static bool IsUrlBarCandidate(BrowserEditableTargetCandidate candidate)
  {
    return string.Equals(candidate.ControlTypeName, "ControlType.Edit", StringComparison.Ordinal)
           && (ContainsAny(candidate.AutomationId, "url", "address", "omnibox")
               || ContainsAny(candidate.AutomationName, "address and search bar", "address bar", "search or enter address")
               || ContainsAny(candidate.ClassName, "omnibox"));
  }

  private static int ScoreCandidate(
    BrowserEditableTargetCandidate candidate,
    ScreenBounds? foregroundWindowBounds)
  {
    int score = 0;

    if (candidate.SupportsWritableValuePattern)
    {
      score += 160;
    }

    if (candidate.SupportsTextPattern)
    {
      score += 120;
    }

    if (candidate.IsKeyboardFocusable)
    {
      score += 90;
    }

    if (string.Equals(candidate.ControlTypeName, "ControlType.Edit", StringComparison.Ordinal))
    {
      score += 70;
    }
    else if (string.Equals(candidate.ControlTypeName, "ControlType.Document", StringComparison.Ordinal))
    {
      score += 60;
    }

    if (ContainsAny(candidate.AutomationId, PreferredSemanticTokens)
        || ContainsAny(candidate.AutomationName, PreferredSemanticTokens)
        || ContainsAny(candidate.ClassName, PreferredSemanticTokens))
    {
      score += 35;
    }

    if (HasVisibleBounds(candidate.Bounds))
    {
      score += 20;
    }

    if (candidate.Bounds.HasValue
        && foregroundWindowBounds.HasValue
        && Intersects(candidate.Bounds.Value, foregroundWindowBounds.Value))
    {
      score += 15;
    }

    if (!string.IsNullOrWhiteSpace(candidate.ObservedText))
    {
      score += 10;
    }

    score += candidate.Source switch
    {
      BrowserEditableTargetSource.RawFocusedElement => 50,
      BrowserEditableTargetSource.FocusedAncestor => 40,
      BrowserEditableTargetSource.FocusedDescendant => 30,
      BrowserEditableTargetSource.ForegroundWindowSubtree => 20,
      _ => 0,
    };

    score -= candidate.SearchOrder;
    return score;
  }

  private static bool MatchesTarget(
    BrowserEditableTargetCandidate left,
    BrowserEditableTargetCandidate right)
  {
    return string.Equals(left.AutomationId, right.AutomationId, StringComparison.Ordinal)
           && string.Equals(left.AutomationName, right.AutomationName, StringComparison.Ordinal)
           && string.Equals(left.ClassName, right.ClassName, StringComparison.Ordinal)
           && string.Equals(left.ControlTypeName, right.ControlTypeName, StringComparison.Ordinal)
           && Nullable.Equals(left.Bounds, right.Bounds);
  }

  private static bool IsEditableControlType(string? controlTypeName)
  {
    return string.Equals(controlTypeName, "ControlType.Edit", StringComparison.Ordinal)
           || string.Equals(controlTypeName, "ControlType.Document", StringComparison.Ordinal);
  }

  private static bool HasVisibleBounds(ScreenBounds? bounds)
  {
    return bounds.HasValue && !bounds.Value.IsEmpty;
  }

  internal static bool IsAccessibilityPlaceholder(BrowserEditableTargetCandidate candidate)
  {
    return ContainsAny(candidate.AutomationName, AccessibilityPlaceholderTokens)
           || ContainsAny(candidate.ClassName, "native-edit-context")
              && ContainsAny(candidate.AutomationName, AccessibilityPlaceholderTokens);
  }

  private static bool Intersects(ScreenBounds left, ScreenBounds right)
  {
    int leftRight = left.Left + left.Width;
    int leftBottom = left.Top + left.Height;
    int rightRight = right.Left + right.Width;
    int rightBottom = right.Top + right.Height;

    return left.Left < rightRight
           && leftRight > right.Left
           && left.Top < rightBottom
           && leftBottom > right.Top;
  }

  private static bool ContainsAny(string? value, IReadOnlyList<string> tokens)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return false;
    }

    foreach (string token in tokens)
    {
      if (value.Contains(token, StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }

    return false;
  }

  private static bool ContainsAny(string? value, params string[] tokens)
  {
    return ContainsAny(value, (IReadOnlyList<string>)tokens);
  }
}

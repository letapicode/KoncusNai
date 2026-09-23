using System;
using System.Collections.Generic;
using System.Linq;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Inference;

/// <summary>
/// Chooses Ollama reasoning only when a prompt is likely to benefit from it.
/// The bias is intentionally toward a fast direct reply: the user can still explicitly ask to reason step by step.
/// </summary>
internal static class OllamaThinkingPlanner
{
  private static readonly string[] HigherEffortSignals =
  [
    "analyze", "analysis", "compare", "comparison", "tradeoff", "trade-off",
    "plan", "strategy", "design", "architecture", "debug", "diagnose", "root cause",
    "prove", "derive", "reason", "reasoning", "step by step", "step-by-step",
    "edge case", "edge-case", "security", "recursive", "complexity", "optimize",
    "refactor", "race condition", "algorithm",
  ];

  public static bool ShouldThink(IReadOnlyList<ChatMessage> messages)
  {
    ArgumentNullException.ThrowIfNull(messages);

    string prompt = messages
      .Select(message => message.Normalize())
      .LastOrDefault(message => string.Equals(message.Role, ChatMessageRoles.User, StringComparison.OrdinalIgnoreCase))?
      .Content
      .Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(prompt))
    {
      return false;
    }

    string normalized = prompt.ToLowerInvariant();
    if (normalized.Contains("don't think", StringComparison.Ordinal)
        || normalized.Contains("do not think", StringComparison.Ordinal)
        || normalized.Contains("no reasoning", StringComparison.Ordinal))
    {
      return false;
    }

    if (HigherEffortSignals.Any(signal => normalized.Contains(signal, StringComparison.Ordinal)))
    {
      return true;
    }

    int questionCount = prompt.Count(character => character == '?');
    int nonEmptyLineCount = prompt.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    return prompt.Length >= 420 || questionCount >= 2 || nonEmptyLineCount >= 4;
  }
}

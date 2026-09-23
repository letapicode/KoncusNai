using System;
using System.Collections.Generic;

namespace DictateAnywhere.App.Workbench;

internal static class WelcomeMessageSelector
{
  internal static IReadOnlyList<string> Messages { get; } = new[]
  {
    "What are we building?",
    "Your local AI is ready.",
    "Big idea or tiny task?",
    "Let’s make something useful.",
    "No cloud required.",
    "Bring the messy draft.",
    "Ask locally. Think freely.",
    "Your next idea starts here.",
    "Let’s untangle it.",
    "Ready when you are.",
  };

  public static string Select(Random? random = null)
  {
    Random source = random ?? Random.Shared;
    return Messages[source.Next(Messages.Count)];
  }
}

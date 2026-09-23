using System.Globalization;
using System.Text.RegularExpressions;

namespace DictateAnywhere.App.Workbench;

/// <summary>Calendar facts from the device clock, never from model training data.</summary>
internal static class LocalCalendarContext
{
  private static readonly Regex Question = new(
    @"^(?:(?:hi|hello|hey)[,!?.\s]+)?(?:if you don['’]t know[,]? say (?:it|i don['’]t know)[,!?.\s]+)?(?:what (?:day|date) (?:is|was) (?:it )?(?:today|tomorrow|yesterday)|what (?:day|date) will (?:it|tomorrow) be(?: tomorrow)?|(?:today|tomorrow|yesterday)['’]s date)[?!.\s]*(?:(?:hi|hello|hey)[?!.\s]*)?$",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

  public static bool CanAnswer(string prompt) => prompt.Length <= 200 && Question.IsMatch(prompt);

  public static string Answer(string prompt, DateTimeOffset now, TimeZoneInfo zone)
  {
    DateTime date = TimeZoneInfo.ConvertTime(now, zone).Date;
    int delta = prompt.Contains("tomorrow", StringComparison.OrdinalIgnoreCase) ? 1
      : prompt.Contains("yesterday", StringComparison.OrdinalIgnoreCase) ? -1 : 0;
    string day = date.AddDays(delta).ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);
    string prefix = delta == 1 ? "Tomorrow is" : delta == -1 ? "Yesterday was" : "Today is";
    return $"{prefix} {day}, according to your computer’s local calendar.";
  }

  public static string Instructions(DateTimeOffset now, TimeZoneInfo zone)
  {
    DateTimeOffset local = TimeZoneInfo.ConvertTime(now, zone);
    return $"Current device-local date and time: {local.ToString("yyyy-MM-dd dddd HH:mm zzz", CultureInfo.InvariantCulture)}. "
      + $"Time zone: {zone.Id}. Today: {local.Date:yyyy-MM-dd}; tomorrow: {local.Date.AddDays(1):yyyy-MM-dd}; yesterday: {local.Date.AddDays(-1):yyyy-MM-dd}. "
      + "Use these current calendar facts instead of dates guessed in earlier messages. This is the device clock, not live internet access. "
      + "If a requested external fact is unavailable, say so. Answer directly, without routine introductory filler or closing offers.";
  }
}

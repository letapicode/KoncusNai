namespace DictateAnywhere.App.Workbench;

internal static class PromptExpansionPolicy
{
  private const int LongPromptThreshold = 120;

  public static bool ShouldOfferExpansion(string? text, int visualLineCount)
  {
    return !string.IsNullOrWhiteSpace(text)
           && (text.Contains('\n')
               || visualLineCount > 1
               || text.Length > LongPromptThreshold);
  }
}

using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace DictateAnywhere.App.Presentation;

/// <summary>Scopes existing control styles to paper without changing the application theme.</summary>
internal static class PaperChatResources
{
  private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>
  {
    ["Brush.Text.Primary"] = "Brush.Paper.Ink",
    ["Brush.Text.Secondary"] = "Brush.Paper.Comment",
    ["Brush.Surface.Elevated"] = "Brush.Paper.Control",
    ["Brush.Surface.Base"] = "Brush.Paper.Control",
    ["Brush.Border.Subtle"] = "Brush.Paper.Border",
    ["Brush.Control.Input"] = "Brush.Paper.Control",
    ["Brush.Control.InputDisabled"] = "Brush.Paper.Composer",
    ["Brush.Control.Muted"] = "Brush.Paper.Control",
    ["Brush.Control.Hover"] = "Brush.Paper.Hover",
    ["Brush.Control.Pressed"] = "Brush.Paper.Hover",
  };

  internal static void Apply(FrameworkElement surface, bool enabled)
  {
    foreach ((string target, string source) in Aliases)
    {
      if (enabled && surface.TryFindResource(source) is Brush brush) surface.Resources[target] = brush;
      else surface.Resources.Remove(target);
    }
  }
}

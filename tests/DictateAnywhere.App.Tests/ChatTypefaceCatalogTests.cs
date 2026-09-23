using DictateAnywhere.App.Presentation;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class ChatTypefaceCatalogTests
{
  [Xunit.Theory]
  [Xunit.InlineData(ChatTypefaceIds.System, "Segoe UI")]
  [Xunit.InlineData(ChatTypefaceIds.BookSerif, "Georgia")]
  [Xunit.InlineData(ChatTypefaceIds.LiterarySerif, "Palatino Linotype")]
  [Xunit.InlineData(ChatTypefaceIds.ModernSerif, "Cambria")]
  [Xunit.InlineData(ChatTypefaceIds.Excalifont, "./Assets/Fonts/Excalifont/#Excalifont")]
  [Xunit.InlineData(ChatTypefaceIds.Kalam, "./Assets/Fonts/Kalam/#Kalam")]
  public void Resolve_ReturnsStableTypefaceMapping(string id, string expectedFamily)
  {
    ChatTypefaceOption option = ChatTypefaceCatalog.Resolve(id);

    Xunit.Assert.Equal(id, option.Id);
    Xunit.Assert.Equal(expectedFamily, option.FontFamilyName);
  }

  [Xunit.Fact]
  public void Resolve_UnknownTypefaceFallsBackToSystem()
  {
    Xunit.Assert.Equal(ChatTypefaceIds.System, ChatTypefaceCatalog.Resolve("unknown").Id);
  }
}

using DictateAnywhere.App.Presentation;
using Xunit;

namespace DictateAnywhere.App.Tests;

public sealed class ChatCodeHighlighterTests
{
  [Fact]
  public void Java_AnnotationsGenericsConstructorsAndIncompleteStringsPreserveSource()
  {
    const string source = "@Override public List<String> values() { var result = new ArrayList<String>(); char c = '\\n'; // comment\n return result; } String unfinished = \"unterminated";
    var tokens = ChatCodeHighlighter.Tokenize(source, "java");
    Assert.Equal(source, string.Concat(tokens.Select(token => token.Text)));
    foreach (string type in new[] { "Override", "List", "String", "ArrayList" })
      Assert.Contains(tokens, token => token.Text == type && token.Kind == ChatCodeTokenKind.Type);
    Assert.Contains(tokens, token => token.Text == "values" && token.Kind == ChatCodeTokenKind.Method);
    Assert.Contains(tokens, token => token.Text == "result" && token.Kind == ChatCodeTokenKind.Identifier);
    Assert.Contains(tokens, token => token.Kind == ChatCodeTokenKind.String && token.Text.Contains("unterminated", StringComparison.Ordinal));
  }

  [Fact]
  public void Java_NumbersAndArithmeticKeepTheirOriginalBoundaries()
  {
    const string source = "int a = -1; int b = a-1; double c = 1.5e-3; int hex = 0xff; return -2;";
    var tokens = ChatCodeHighlighter.Tokenize(source, "java");
    Assert.Equal(source, string.Concat(tokens.Select(token => token.Text)));
    foreach (string number in new[] { "-1", "1", "1.5e-3", "0xff", "-2" })
      Assert.Contains(tokens, token => token.Kind == ChatCodeTokenKind.Number && token.Text == number);
    Assert.Contains(tokens, token => token.Kind == ChatCodeTokenKind.Operator && token.Text == "-");
  }

  [Theory]
  [InlineData("java", "java")]
  [InlineData("Java title", "java")]
  [InlineData("cs", "csharp")]
  [InlineData("py", "python")]
  [InlineData("js", "javascript")]
  [InlineData("", "plain text")]
  public void NormalizeLanguage_UsesFenceTagAndAliases(string info, string expected) =>
    Assert.Equal(expected, ChatCodeHighlighter.NormalizeLanguage(info));

  [Fact]
  public void Tokenize_ColorsJavaWithoutChangingAnyCharacters()
  {
    const string source = "public class BinarySearch {\n  int value = 12; // note\n  String name = \"hi\";\n}";
    var tokens = ChatCodeHighlighter.Tokenize(source, "java");

    Assert.Equal(source, string.Concat(tokens.Select(token => token.Text)));
    Assert.Contains(tokens, token => token.Kind == ChatCodeTokenKind.Keyword && token.Text.Contains("public"));
    Assert.Contains(tokens, token => token.Kind == ChatCodeTokenKind.Type && token.Text.Contains("BinarySearch"));
    Assert.Contains(tokens, token => token.Kind == ChatCodeTokenKind.Number && token.Text.Contains("12"));
    Assert.Contains(tokens, token => token.Kind == ChatCodeTokenKind.Comment && token.Text.Contains("// note"));
    Assert.Contains(tokens, token => token.Kind == ChatCodeTokenKind.String && token.Text.Contains("\"hi\""));
  }

  [Fact]
  public void Tokenize_UnknownAndOversizedCodeRemainReadablePlainText()
  {
    Assert.Equal([new ChatCodeToken("a < b", ChatCodeTokenKind.Plain)],
      ChatCodeHighlighter.Tokenize("a < b", "unknown-language"));
    string large = new('x', 100_001);
    Assert.Equal([new ChatCodeToken(large, ChatCodeTokenKind.Plain)],
      ChatCodeHighlighter.Tokenize(large, "java"));
  }

  [Fact]
  public void Tokenize_UntaggedCodeUsesGenericSyntax()
  {
    const string source = "public class Example { int n = 15; }";
    var tokens = ChatCodeHighlighter.Tokenize(source, "");
    Assert.Equal(source, string.Concat(tokens.Select(token => token.Text)));
    Assert.Contains(tokens, token => token.Kind == ChatCodeTokenKind.Keyword && token.Text.Contains("public"));
    Assert.Contains(tokens, token => token.Kind == ChatCodeTokenKind.Number && token.Text.Contains("15"));
  }

  [Theory]
  [InlineData("public", ChatCodeTokenKind.Keyword)]
  [InlineData("static", ChatCodeTokenKind.Keyword)]
  [InlineData("int", ChatCodeTokenKind.Primitive)]
  [InlineData("BinarySearch", ChatCodeTokenKind.Type)]
  [InlineData("binarySearch", ChatCodeTokenKind.Method)]
  [InlineData("nums", ChatCodeTokenKind.Identifier)]
  [InlineData("target", ChatCodeTokenKind.Identifier)]
  [InlineData("mid", ChatCodeTokenKind.Identifier)]
  [InlineData("length", ChatCodeTokenKind.Member)]
  [InlineData("false", ChatCodeTokenKind.Literal)]
  public void Java_UsesMeaningfulCategoriesAndNeutralVariables(string word, object kind)
  {
    const string source = "public class BinarySearch { public static int binarySearch(int[] nums, int target) { int mid = nums.length / 2; if (false) return mid; } }";
    var tokens = ChatCodeHighlighter.Tokenize(source, "java");
    Assert.Equal(source, string.Concat(tokens.Select(token => token.Text)));
    Assert.All(tokens.Where(token => token.Text == word), token => Assert.Equal((ChatCodeTokenKind)kind, token.Kind));
    Assert.Contains(tokens, token => token.Text == word);
  }

  [Theory]
  [InlineData("java", "CLASS", ChatCodeTokenKind.Identifier)]
  [InlineData("python", "def", ChatCodeTokenKind.Keyword)]
  [InlineData("python", "None", ChatCodeTokenKind.Literal)]
  [InlineData("javascript", "const", ChatCodeTokenKind.Keyword)]
  [InlineData("sql", "SELECT", ChatCodeTokenKind.Keyword)]
  [InlineData("json", "true", ChatCodeTokenKind.Literal)]
  public void Tokenize_RespectsLanguageRules(string language, string word, object expected)
    => Assert.Equal((ChatCodeTokenKind)expected, Assert.Single(ChatCodeHighlighter.Tokenize(word, language)).Kind);
}

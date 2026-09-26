using System;
using System.Collections.Generic;
using System.Text;

namespace DictateAnywhere.App.Presentation;

internal enum ChatCodeTokenKind { Plain, Keyword, Identifier, Primitive, Type, Method, Member, Literal, Operator, Bracket, Punctuation, String, Comment, Number }

internal readonly record struct ChatCodeToken(string Text, ChatCodeTokenKind Kind);

/// <summary>Bounded display-only tokenization. Joining token text always recreates the input.</summary>
internal static class ChatCodeHighlighter
{
  private const int MaximumCharacters = 100_000;
  private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
  {
    "abstract", "and", "as", "async", "await", "base", "bool", "boolean", "break", "by",
    "byte", "case", "catch", "char", "class", "const", "continue", "def", "default",
    "delete", "do", "double", "elif", "else", "enum", "except", "export", "extends",
    "false", "final", "finally", "float", "for", "from", "function", "if", "implements",
    "import", "in", "instanceof", "int", "interface", "internal", "is", "let", "long",
    "namespace", "new", "not", "null", "of", "or", "override", "package", "private",
    "protected", "public", "readonly", "return", "sealed", "select", "short", "static",
    "string", "struct", "super", "switch", "this", "throw", "throws", "true", "try",
    "type", "typeof", "using", "var", "virtual", "void", "while", "with", "yield",
    "where", "order", "group", "insert", "update", "into", "values", "join", "on",
  };

  private static readonly IReadOnlyDictionary<string, HashSet<string>> LanguageKeywords = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
  {
    ["java"] = Words("abstract assert break case catch class const continue default do else enum extends final finally for if implements import instanceof interface native new package private protected public record return sealed static strictfp super switch synchronized this throw throws transient try var volatile while yield permits non-sealed"),
    ["csharp"] = Words("abstract as async await base break case catch checked class const continue default delegate do else enum event explicit extern finally fixed for foreach goto if implicit in interface internal is lock namespace new operator out override params private protected public readonly record ref required return sealed sizeof stackalloc static struct switch this throw try typeof unchecked unsafe using var virtual volatile while with yield"),
    ["javascript"] = Words("async await break case catch class const continue debugger default delete do else export extends finally for from function get if import in instanceof let new of return set static super switch this throw try typeof var void while with yield"),
    ["typescript"] = Words("abstract as async await break case catch class const continue declare default delete do else enum export extends finally for from function if implements import in infer instanceof interface keyof let namespace new of private protected public readonly return static super switch this throw try type typeof var while yield"),
    ["python"] = Words("and as assert async await break class continue def del elif else except finally for from global if import in is lambda nonlocal not or pass raise return try while with yield"),
    ["json"] = Words(""),
    ["bash"] = Words("if then else elif fi for do done while until case esac in function select time"),
    ["yaml"] = Words(""),
    ["sql"] = new HashSet<string>("select from where order by group having insert update delete into values join on as and or not null is create table drop alter set distinct limit union exists case when then else end".Split(' '), StringComparer.OrdinalIgnoreCase),
    ["go"] = Words("break case chan const continue default defer else fallthrough for func go goto if import interface map package range return select struct switch type var"),
    ["rust"] = Words("as async await break const continue crate dyn else enum extern fn for if impl in let loop match mod move mut pub ref return self Self static struct super trait type unsafe use where while"),
    ["kotlin"] = Words("as break class continue do else for fun if in interface is object package return super this throw try typealias typeof val var when while"),
    ["swift"] = Words("actor associatedtype break case catch class continue default defer deinit do else enum extension fallthrough for func guard if import in init internal let open operator private protocol public repeat return static struct subscript super switch throw throws try typealias var where while"),
    ["c"] = Words("auto break case const continue default do else enum extern for goto if register return sizeof static struct switch typedef union volatile while"),
    ["cpp"] = Words("alignas alignof auto break case catch class const constexpr continue default delete do else enum explicit export extern for friend goto if inline namespace new operator private protected public return sizeof static struct switch template this throw try typedef typename union using virtual volatile while"),
  };
  private static readonly HashSet<string> PrimitiveTypes = Words("int boolean bool byte char double float long short void string decimal object uint ulong ushort sbyte number bigint any unknown never i8 i16 i32 i64 u8 u16 u32 u64 usize isize f32 f64");
  private static readonly HashSet<string> BuiltinTypes = Words("String Object List Map Set Collection ArrayList HashMap HashSet Optional Integer Double Boolean Character Long Float Short Byte Exception RuntimeException Int Bool Double Float Character Array Dictionary Task Date Promise");
  private static readonly HashSet<string> Literals = Words("true false null nil None True False undefined");
  private static readonly HashSet<string> TypeIntroducers = Words("class interface enum record struct trait type typealias new extends implements instanceof");
  private static readonly HashSet<string> JavaPrimitives = Words("int boolean byte char double float long short void");
  private static readonly HashSet<string> PythonLiterals = Words("None True False");
  private static readonly HashSet<string> CommonLiterals = Words("true false null");

  private static HashSet<string> Words(string words) => new(words.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

  internal static string NormalizeLanguage(string? info)
  {
    string tag = (info ?? string.Empty).Trim().Split([' ', '\t', '{'], 2)[0].ToLowerInvariant();
    return tag switch
    {
      "js" or "jsx" => "javascript",
      "ts" or "tsx" => "typescript",
      "py" => "python",
      "cs" or "c#" => "csharp",
      "sh" or "shell" or "zsh" => "bash",
      "yml" => "yaml",
      "htm" => "html",
      "" => "plain text",
      _ => tag,
    };
  }

  internal static IReadOnlyList<ChatCodeToken> Tokenize(string code, string? info)
  {
    code ??= string.Empty;
    string language = string.IsNullOrWhiteSpace(info) ? "generic" : NormalizeLanguage(info);
    if (code.Length > MaximumCharacters || !IsSupported(language))
      return [new ChatCodeToken(code, ChatCodeTokenKind.Plain)];

    bool hashComments = language is "python" or "bash" or "yaml" or "generic";
    bool slashComments = language is "java" or "csharp" or "javascript" or "typescript" or "cpp" or "c" or "go" or "rust" or "kotlin" or "swift" or "generic";
    bool sqlComments = language == "sql";
    HashSet<string> keywords = LanguageKeywords.TryGetValue(language, out HashSet<string>? configured) ? configured : Keywords;
    HashSet<string> primitives = language == "java" ? JavaPrimitives : PrimitiveTypes;
    HashSet<string> literals = language == "python" ? PythonLiterals : language == "java" ? CommonLiterals : Literals;
    List<ChatCodeToken> tokens = [];
    StringBuilder pending = new();
    ChatCodeTokenKind pendingKind = ChatCodeTokenKind.Plain;
    for (int position = 0; position < code.Length;)
    {
      int start = position;
      char current = code[position];
      ChatCodeTokenKind kind;
      if ((hashComments && current == '#')
          || (slashComments && current == '/' && position + 1 < code.Length && code[position + 1] == '/')
          || (sqlComments && current == '-' && position + 1 < code.Length && code[position + 1] == '-'))
      {
        position = ScanToLineEnd(code, position);
        kind = ChatCodeTokenKind.Comment;
      }
      else if (slashComments && current == '/' && position + 1 < code.Length && code[position + 1] == '*')
      {
        position = Math.Min(code.Length, position + 2);
        while (position + 1 < code.Length && !(code[position] == '*' && code[position + 1] == '/')) position++;
        position = Math.Min(code.Length, position + 2);
        kind = ChatCodeTokenKind.Comment;
      }
      else if (current is '\'' or '"' or '`')
      {
        position++;
        while (position < code.Length)
        {
          if (code[position] == '\\' && position + 1 < code.Length) { position += 2; continue; }
          if (code[position++] == current) break;
        }
        kind = ChatCodeTokenKind.String;
      }
      else if (char.IsDigit(current) || current == '-' && position + 1 < code.Length && char.IsDigit(code[position + 1]) && IsUnarySign(code, position))
      {
        position = ScanNumber(code, position);
        kind = ChatCodeTokenKind.Number;
      }
      else if (char.IsLetter(current) || current is '_' or '$')
      {
        position++;
        while (position < code.Length && (char.IsLetterOrDigit(code[position]) || code[position] is '_' or '$')) position++;
        string word = code[start..position];
        kind = literals.Contains(word) ? ChatCodeTokenKind.Literal
          : primitives.Contains(word) && language is not "json" and not "yaml" and not "bash" ? ChatCodeTokenKind.Primitive
          : keywords.Contains(word) ? ChatCodeTokenKind.Keyword : ChatCodeTokenKind.Identifier;
      }
      else
      {
        position++;
        kind = current switch
        {
          '(' or ')' or '[' or ']' or '{' or '}' => ChatCodeTokenKind.Bracket,
          '.' or ',' or ';' or '@' => ChatCodeTokenKind.Punctuation,
          '=' or '+' or '-' or '*' or '/' or '<' or '>' or '!' or '&' or '|' or '^' or '%' or '~' or '?' or ':' => ChatCodeTokenKind.Operator,
          _ => ChatCodeTokenKind.Plain,
        };
      }

      string piece = code[start..position];
      if (pending.Length > 0 && (pendingKind != kind || kind is ChatCodeTokenKind.Identifier or ChatCodeTokenKind.Keyword or ChatCodeTokenKind.Bracket or ChatCodeTokenKind.Punctuation))
      {
        tokens.Add(new ChatCodeToken(pending.ToString(), pendingKind));
        pending.Clear();
      }
      pendingKind = kind;
      pending.Append(piece);
    }
    if (pending.Length > 0) tokens.Add(new ChatCodeToken(pending.ToString(), pendingKind));
    ClassifyIdentifiers(tokens, language);
    return tokens;
  }

  private static bool IsUnarySign(string code, int position)
  {
    int before = position - 1;
    while (before >= 0 && char.IsWhiteSpace(code[before])) before--;
    if (before < 0 || "=([{,:!<>?+-*/%".Contains(code[before], StringComparison.Ordinal)) return true;
    int end = before + 1;
    while (before >= 0 && char.IsLetter(code[before])) before--;
    return code[(before + 1)..end] is "return" or "case" or "yield";
  }

  private static int ScanNumber(string code, int position)
  {
    if (code[position] == '-') position++;
    if (position + 1 < code.Length && code[position] == '0' && code[position + 1] is 'x' or 'X' or 'b' or 'B')
    {
      bool binary = code[position + 1] is 'b' or 'B';
      position += 2;
      while (position < code.Length && (code[position] == '_' || (binary ? code[position] is '0' or '1' : Uri.IsHexDigit(code[position])))) position++;
    }
    else
    {
      while (position < code.Length && (char.IsDigit(code[position]) || code[position] == '_')) position++;
      if (position + 1 < code.Length && code[position] == '.' && char.IsDigit(code[position + 1]))
      {
        position++;
        while (position < code.Length && (char.IsDigit(code[position]) || code[position] == '_')) position++;
      }
      if (position < code.Length && code[position] is 'e' or 'E')
      {
        int exponent = position + 1;
        if (exponent < code.Length && code[exponent] is '+' or '-') exponent++;
        if (exponent < code.Length && char.IsDigit(code[exponent]))
        {
          position = exponent + 1;
          while (position < code.Length && (char.IsDigit(code[position]) || code[position] == '_')) position++;
        }
      }
    }
    while (position < code.Length && code[position] is 'f' or 'F' or 'd' or 'D' or 'm' or 'M' or 'l' or 'L' or 'u' or 'U') position++;
    return position;
  }

  private static void ClassifyIdentifiers(List<ChatCodeToken> tokens, string language)
  {
    if (language is "json" or "yaml" or "bash" or "sql") return;
    HashSet<string> knownTypes = new(BuiltinTypes, StringComparer.Ordinal);
    int[] previous = new int[tokens.Count];
    int[] next = new int[tokens.Count];
    int significant = -1;
    for (int i = 0; i < tokens.Count; i++)
    {
      previous[i] = significant;
      if (tokens[i].Kind != ChatCodeTokenKind.Comment && !string.IsNullOrWhiteSpace(tokens[i].Text)) significant = i;
    }
    significant = -1;
    for (int i = tokens.Count - 1; i >= 0; i--)
    {
      next[i] = significant;
      if (tokens[i].Kind != ChatCodeTokenKind.Comment && !string.IsNullOrWhiteSpace(tokens[i].Text)) significant = i;
    }
    for (int i = 0; i < tokens.Count; i++)
      if (tokens[i].Kind == ChatCodeTokenKind.Identifier && previous[i] >= 0 && TypeIntroducers.Contains(tokens[previous[i]].Text)) knownTypes.Add(tokens[i].Text);
    for (int i = 0; i < tokens.Count; i++)
    {
      if (tokens[i].Kind != ChatCodeTokenKind.Identifier) continue;
      string before = previous[i] >= 0 ? tokens[previous[i]].Text : string.Empty;
      string after = next[i] >= 0 ? tokens[next[i]].Text : string.Empty;
      ChatCodeTokenKind kind = knownTypes.Contains(tokens[i].Text) || before == "@" && language == "java" ? ChatCodeTokenKind.Type
        : after == "(" ? ChatCodeTokenKind.Method
        : before == "." ? ChatCodeTokenKind.Member : ChatCodeTokenKind.Identifier;
      tokens[i] = tokens[i] with { Kind = kind };
    }
  }

  internal static double EstimatePageWidth(string source, double fontSize)
  {
    int longest = 0;
    int current = 0;
    foreach (char character in source)
    {
      if (character == '\n') { longest = Math.Max(longest, current); current = 0; }
      else if (character == '\t') current += 4;
      else if (character != '\r') current++;
    }
    longest = Math.Max(longest, current);
    return Math.Clamp((longest + 2) * fontSize, 100d, 1_000_000d);
  }

  private static int ScanToLineEnd(string code, int position)
  {
    while (position < code.Length && code[position] != '\n') position++;
    return position;
  }

  private static bool IsSupported(string language) => language is
    "java" or "csharp" or "javascript" or "typescript" or "python" or "json" or
    "bash" or "yaml" or "sql" or "cpp" or "c" or "go" or "rust" or "kotlin" or "swift" or "generic";
}

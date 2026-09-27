using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Xunit;

namespace DictateAnywhere.App.Tests;

public sealed class WpfTaxonomyTests
{
  [Fact]
  public void AffectedFixturesClassifyExecutableStaAndWpfCallsWithoutClassifyingPureCases()
  {
    Type[] fixtures = [typeof(DictationUiRegressionTests), typeof(DictationUiLifecycleTests),
      typeof(PaperSurfaceTests), typeof(ChatPresentationRegressionTests), typeof(WpfTestStaTests)];
    int checkedUi = 0;
    foreach (Type fixture in fixtures)
    foreach (MethodInfo method in fixture.GetMethods().Where(m => m.GetCustomAttribute<FactAttribute>() is not null))
    {
      if (!CallsWpf(method)) continue;
      Assert.True(IsWpf(method), $"{fixture.Name}.{method.Name} requires Category=WindowsWpf.");
      checkedUi++;
    }
    Assert.True(checkedUi > 0);
    foreach (string name in new[] { "Calendar_UsesLocalCalendarDays", "Calendar_DoesNotHijackOtherRequests", "ClockContext_IsFreshAndNeverPersisted" })
      Assert.False(IsWpf(typeof(ChatPresentationRegressionTests).GetMethod(name)!));
    Assert.False(IsWpf(typeof(DictationUiRegressionTests).GetMethod("RecoveryDismissalRetainsUnsavedRecordsAndStaleActionsCannotAffectNewerNotice")!));
  }

  [Fact]
  public void FocusedAndCoverageCommandsUseTheSameDeterministicCategoryBoundary()
  {
    DirectoryInfo? root = new(AppContext.BaseDirectory);
    while (root is not null && !File.Exists(Path.Combine(root.FullName, "DictateAnywhere.sln"))) root = root.Parent;
    Assert.NotNull(root);
    string focused = File.ReadAllText(Path.Combine(root.FullName, "scripts/run-focused-tests.ps1"));
    string coverage = File.ReadAllText(Path.Combine(root.FullName, "scripts/run-coverage-gate.ps1"));
    string filter = Regex.Match(focused, "Deterministic = \"([^\"]+)\"").Groups[1].Value;
    Assert.NotEmpty(filter);
    Assert.Equal(filter, Regex.Match(coverage, "deterministicFilter = \"([^\"]+)\"").Groups[1].Value);
    Assert.Contains("Category!=WindowsWpf", filter, StringComparison.Ordinal);
  }

  private static bool IsWpf(MethodInfo method) => method.GetCustomAttributesData()
    .Concat(method.DeclaringType!.GetCustomAttributesData())
    .Any(trait => trait.AttributeType == typeof(TraitAttribute)
      && Equals(trait.ConstructorArguments[0].Value, "Category") && Equals(trait.ConstructorArguments[1].Value, "WindowsWpf"));

  // Decode executable call operands; source strings and imports are not execution requirements.
  private static bool CallsWpf(MethodInfo method)
  {
    byte[] body = method.GetMethodBody()!.GetILAsByteArray()!;
    Dictionary<short, OpCode> codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
      .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
      .ToDictionary(code => code.Value);
    for (int offset = 0; offset < body.Length;)
    {
      short value = body[offset++];
      if (value == 0xfe) value = unchecked((short)(0xfe00 | body[offset++]));
      OpCode code = codes[value];
      if (code.OperandType == OperandType.InlineMethod)
      {
        MethodBase called = method.Module.ResolveMethod(BitConverter.ToInt32(body, offset))!;
        Type? owner = called.DeclaringType;
        if (owner == typeof(WpfTestSta) && called.Name == "Run"
          || owner == typeof(Thread) && called.Name == "SetApartmentState"
          || owner is not null && typeof(DispatcherObject).IsAssignableFrom(owner)) return true;
      }
      offset += code.OperandType switch
      {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(body, offset),
        _ => 4
      };
    }
    return false;
  }
}

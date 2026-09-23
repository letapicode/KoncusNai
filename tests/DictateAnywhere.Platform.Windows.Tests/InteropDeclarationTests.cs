using System.Reflection;
using System.Runtime.InteropServices;
using DictateAnywhere.Platform.Windows.Interop;

namespace DictateAnywhere.Platform.Windows.Tests;

public sealed class InteropDeclarationTests
{
  [Xunit.Theory]
  [Xunit.InlineData("RegisterHotKeyNative", "RegisterHotKey")]
  [Xunit.InlineData("UnregisterHotKeyNative", "UnregisterHotKey")]
  [Xunit.InlineData("GetAsyncKeyStateNative", "GetAsyncKeyState")]
  [Xunit.InlineData("GetCurrentThreadIdNative", "GetCurrentThreadId")]
  public void DllImportEntryPoint_IsExplicitAndCorrect(string methodName, string expectedEntryPoint)
  {
    MethodInfo? method = typeof(User32HotkeyApi).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
    Xunit.Assert.NotNull(method);

    DllImportAttribute? attribute = method!.GetCustomAttribute<DllImportAttribute>();
    Xunit.Assert.NotNull(attribute);
    Xunit.Assert.Equal(expectedEntryPoint, attribute!.EntryPoint);
  }
}
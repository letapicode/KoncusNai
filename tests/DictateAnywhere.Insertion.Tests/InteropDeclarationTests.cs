using System;
using System.Reflection;
using System.Runtime.InteropServices;
using DictateAnywhere.Insertion;

namespace DictateAnywhere.Insertion.Tests;

public sealed class InteropDeclarationTests
{
  [Xunit.Theory]
  [Xunit.InlineData(typeof(WindowsWindowFocusProvider), "GetForegroundWindowNative", "GetForegroundWindow")]
  [Xunit.InlineData(typeof(WindowsInputDispatcher), "SendInputNative", "SendInput")]
  [Xunit.InlineData(typeof(WindowsPrivilegeBoundaryDetector), "GetWindowThreadProcessIdNative", "GetWindowThreadProcessId")]
  [Xunit.InlineData(typeof(WindowsPrivilegeBoundaryDetector), "OpenProcessNative", "OpenProcess")]
  [Xunit.InlineData(typeof(WindowsPrivilegeBoundaryDetector), "GetCurrentProcessNative", "GetCurrentProcess")]
  [Xunit.InlineData(typeof(WindowsPrivilegeBoundaryDetector), "CloseHandleNative", "CloseHandle")]
  [Xunit.InlineData(typeof(WindowsPrivilegeBoundaryDetector), "OpenProcessTokenNative", "OpenProcessToken")]
  [Xunit.InlineData(typeof(WindowsPrivilegeBoundaryDetector), "GetTokenInformationNative", "GetTokenInformation")]
  [Xunit.InlineData(typeof(WindowsPrivilegeBoundaryDetector), "GetSidSubAuthorityCountNative", "GetSidSubAuthorityCount")]
  [Xunit.InlineData(typeof(WindowsPrivilegeBoundaryDetector), "GetSidSubAuthorityNative", "GetSidSubAuthority")]
  public void DllImportEntryPoint_IsExplicitAndCorrect(Type declaringType, string methodName, string expectedEntryPoint)
  {
    MethodInfo? method = declaringType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
    Xunit.Assert.NotNull(method);

    DllImportAttribute? attribute = method!.GetCustomAttribute<DllImportAttribute>();
    Xunit.Assert.NotNull(attribute);
    Xunit.Assert.Equal(expectedEntryPoint, attribute!.EntryPoint);
  }

  [Xunit.Fact]
  public void InputStructSize_MatchesWindowsExpectations()
  {
    Type? inputType = typeof(WindowsInputDispatcher).GetNestedType(
      "INPUT",
      BindingFlags.NonPublic);

    Xunit.Assert.NotNull(inputType);

    int actualSize = Marshal.SizeOf(inputType!);
    int expectedSize = IntPtr.Size == 8 ? 40 : 28;
    Xunit.Assert.Equal(expectedSize, actualSize);
  }
}

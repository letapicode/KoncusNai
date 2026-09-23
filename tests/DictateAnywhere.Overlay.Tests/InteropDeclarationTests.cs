using System;
using System.Reflection;
using System.Runtime.InteropServices;
using DictateAnywhere.Overlay;

namespace DictateAnywhere.Overlay.Tests;

public sealed class InteropDeclarationTests
{
  [Xunit.Fact]
  public void OverlayWindow_ShowWindowEntryPoint_IsExplicit()
  {
    Assembly assembly = typeof(WindowsOverlayService).Assembly;
    Type? overlayWindowType = assembly.GetType("DictateAnywhere.Overlay.OverlayWindow", throwOnError: false, ignoreCase: false);
    Xunit.Assert.NotNull(overlayWindowType);

    MethodInfo? method = overlayWindowType!.GetMethod("ShowWindowNative", BindingFlags.NonPublic | BindingFlags.Static);
    Xunit.Assert.NotNull(method);

    DllImportAttribute? attribute = method!.GetCustomAttribute<DllImportAttribute>();
    Xunit.Assert.NotNull(attribute);
    Xunit.Assert.Equal("ShowWindow", attribute!.EntryPoint);
  }
}
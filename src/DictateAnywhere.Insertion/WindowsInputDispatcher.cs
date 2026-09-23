using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DictateAnywhere.Insertion;

public sealed class WindowsInputDispatcher : IInputDispatcher
{
  private const uint InputKeyboard = 1;
  private const uint KeyEventKeyUp = 0x0002;
  private const uint KeyEventUnicode = 0x0004;
  private const ushort VirtualKeyControl = 0x11;
  private const ushort VirtualKeyC = 0x43;
  private const ushort VirtualKeyV = 0x56;
  private const ushort VirtualKeyZ = 0x5A;
  private const int ErrorAccessDenied = 5;
  private const int MaxUnicodeBatchCharacters = 256;

  public InputDispatchResult SendCopyShortcut()
  {
    return SendControlShortcut(VirtualKeyC);
  }

  public InputDispatchResult SendPasteShortcut()
  {
    return SendControlShortcut(VirtualKeyV);
  }

  public InputDispatchResult SendUnicodeText(string text)
  {
    ArgumentNullException.ThrowIfNull(text);

    if (text.Length == 0)
    {
      return InputDispatchResult.Ok;
    }

    int offset = 0;
    while (offset < text.Length)
    {
      int count = Math.Min(MaxUnicodeBatchCharacters, text.Length - offset);
      List<INPUT> batchInputs = new(count * 2);

      for (int index = offset; index < offset + count; index++)
      {
        char character = text[index];
        batchInputs.Add(CreateUnicodeInput(character, keyUp: false));
        batchInputs.Add(CreateUnicodeInput(character, keyUp: true));
      }

      InputDispatchResult result = DispatchInputs(batchInputs.ToArray());
      if (!result.Success)
      {
        return result with { MayHaveDispatched = offset > 0 || result.MayHaveDispatched };
      }

      offset += count;
    }

    return InputDispatchResult.Ok;
  }

  public InputDispatchResult SendUndoShortcut()
  {
    return SendControlShortcut(VirtualKeyZ);
  }

  private static InputDispatchResult SendControlShortcut(ushort virtualKey)
  {
    INPUT[] inputs =
    [
      CreateVirtualKeyInput(VirtualKeyControl, keyUp: false),
      CreateVirtualKeyInput(virtualKey, keyUp: false),
      CreateVirtualKeyInput(virtualKey, keyUp: true),
      CreateVirtualKeyInput(VirtualKeyControl, keyUp: true),
    ];

    return DispatchInputs(inputs);
  }

  private static InputDispatchResult DispatchInputs(INPUT[] inputs)
  {
    int inputSize = Marshal.SizeOf<INPUT>();
    uint sent = SendInputNative((uint)inputs.Length, inputs, inputSize);
    if (sent == inputs.Length)
    {
      return InputDispatchResult.Ok;
    }

    int errorCode = Marshal.GetLastWin32Error();
    if (errorCode == ErrorAccessDenied)
    {
      return new InputDispatchResult(
        false,
        "Input injection was blocked by Windows privilege boundaries (UIPI).") { MayHaveDispatched = sent != 0 };
    }

    if (sent == 0)
    {
      return new InputDispatchResult(
        false,
        $"SendInput failed with Win32 error {errorCode} (inputSize={inputSize}).") { MayHaveDispatched = false };
    }

    return new InputDispatchResult(
      false,
      $"SendInput delivered {sent} of {inputs.Length} input events (inputSize={inputSize}).");
  }

  private static INPUT CreateVirtualKeyInput(ushort virtualKey, bool keyUp)
  {
    return new INPUT
    {
      Type = InputKeyboard,
      InputUnion = new INPUTUNION
      {
        KeyboardInput = new KEYBDINPUT
        {
          VirtualKey = virtualKey,
          ScanCode = 0,
          Flags = keyUp ? KeyEventKeyUp : 0,
          Time = 0,
          ExtraInfo = IntPtr.Zero,
        },
      },
    };
  }

  private static INPUT CreateUnicodeInput(char character, bool keyUp)
  {
    return new INPUT
    {
      Type = InputKeyboard,
      InputUnion = new INPUTUNION
      {
        KeyboardInput = new KEYBDINPUT
        {
          VirtualKey = 0,
          ScanCode = character,
          Flags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0),
          Time = 0,
          ExtraInfo = IntPtr.Zero,
        },
      },
    };
  }

  [DllImport("user32.dll", EntryPoint = "SendInput", SetLastError = true)]
  private static extern uint SendInputNative(uint inputCount, INPUT[] inputs, int inputSize);

  [StructLayout(LayoutKind.Sequential)]
  private struct INPUT
  {
    public uint Type;
    public INPUTUNION InputUnion;
  }

  [StructLayout(LayoutKind.Explicit)]
  private struct INPUTUNION
  {
    [FieldOffset(0)]
    public MOUSEINPUT MouseInput;

    [FieldOffset(0)]
    public KEYBDINPUT KeyboardInput;

    [FieldOffset(0)]
    public HARDWAREINPUT HardwareInput;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct MOUSEINPUT
  {
    public int Dx;
    public int Dy;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public IntPtr ExtraInfo;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct KEYBDINPUT
  {
    public ushort VirtualKey;
    public ushort ScanCode;
    public uint Flags;
    public uint Time;
    public IntPtr ExtraInfo;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct HARDWAREINPUT
  {
    public uint Message;
    public ushort ParamL;
    public ushort ParamH;
  }
}

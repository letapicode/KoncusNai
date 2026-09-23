namespace DictateAnywhere.Core.Contracts;

public readonly record struct ScreenBounds(int Left, int Top, int Width, int Height)
{
  public int Right => Left + Width;

  public int Bottom => Top + Height;

  public bool IsEmpty => Width <= 0 || Height <= 0;

  public override string ToString()
  {
    return IsEmpty
      ? "<empty>"
      : $"{Left},{Top},{Width}x{Height}";
  }
}

namespace DictateAnywhere.Core.Contracts;

public interface IOverlayAnchorProvider
{
  OverlayAnchorSnapshot GetCurrentAnchor();
}

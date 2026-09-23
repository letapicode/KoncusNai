namespace DictateAnywhere.App.Runtime;

internal sealed record LocalModelResourceRequirement(
  LocalModelResourceKind Kind,
  string Identifier,
  bool IsRequired,
  bool IsDownloadManaged,
  string Description);

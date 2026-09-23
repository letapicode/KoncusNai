using System;

namespace DictateAnywhere.Settings;

/// <summary>Settings whose structure cannot be safely interpreted must be preserved.</summary>
public sealed class UnrecognizedSettingsSchemaException : InvalidOperationException
{
  public UnrecognizedSettingsSchemaException()
    : base("The settings file has an unrecognized schema. It has been preserved; settings are read-only until the file is repaired or moved.")
  {
  }
}

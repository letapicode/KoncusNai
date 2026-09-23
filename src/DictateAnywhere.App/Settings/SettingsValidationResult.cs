using System;
using System.Collections.Generic;
using System.Linq;

namespace DictateAnywhere.App.Settings;

/// <summary>
/// Represents an individual field or property validation error.
/// </summary>
public sealed record SettingsValidationError(string PropertyName, string Message);

/// <summary>
/// Represents the result of validating a SettingsDraft.
/// </summary>
public sealed class SettingsValidationResult
{
  public static SettingsValidationResult Success { get; } = new(Array.Empty<SettingsValidationError>());

  public IReadOnlyList<SettingsValidationError> Errors { get; }

  public bool IsValid => Errors.Count == 0;

  public SettingsValidationResult(IEnumerable<SettingsValidationError> errors)
  {
    Errors = errors?.ToList().AsReadOnly() ?? (IReadOnlyList<SettingsValidationError>)Array.Empty<SettingsValidationError>();
  }

  public static SettingsValidationResult Failure(params SettingsValidationError[] errors) =>
    new(errors);

  public static SettingsValidationResult Failure(IEnumerable<SettingsValidationError> errors) =>
    new(errors);

  public bool HasErrorFor(string propertyName) =>
    Errors.Any(e => string.Equals(e.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase));

  public string? GetErrorFor(string propertyName) =>
    Errors.FirstOrDefault(e => string.Equals(e.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase))?.Message;

  public string GetSummary() =>
    IsValid ? "Settings are valid." : string.Join("; ", Errors.Select(e => e.Message));
}

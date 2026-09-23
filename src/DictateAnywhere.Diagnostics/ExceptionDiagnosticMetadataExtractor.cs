using System;
using System.Reflection;

namespace DictateAnywhere.Diagnostics;

internal sealed record ExceptionDiagnosticMetadata(
  string TypeName,
  string Message,
  string? ReasonName,
  string? DiagnosticSummary);

internal static class ExceptionDiagnosticMetadataExtractor
{
  public static ExceptionDiagnosticMetadata Extract(Exception? exception)
  {
    if (exception is null)
    {
      return new ExceptionDiagnosticMetadata(string.Empty, string.Empty, null, null);
    }

    Type exceptionType = exception.GetType();
    return new ExceptionDiagnosticMetadata(
      TypeName: exceptionType.Name,
      Message: exception.Message ?? string.Empty,
      ReasonName: TryGetReasonName(exception, exceptionType),
      DiagnosticSummary: TryGetDiagnosticSummary(exception, exceptionType));
  }

  public static bool ShouldPreferDiagnosticSummary(ExceptionDiagnosticMetadata metadata)
  {
    return !string.IsNullOrWhiteSpace(metadata.DiagnosticSummary)
           && !string.Equals(metadata.ReasonName, "Unknown", StringComparison.OrdinalIgnoreCase);
  }

  private static string? TryGetReasonName(Exception exception, Type exceptionType)
  {
    PropertyInfo? property = exceptionType.GetProperty("Reason", BindingFlags.Instance | BindingFlags.Public);
    if (property is null || !property.CanRead)
    {
      return null;
    }

    object? value = property.GetValue(exception);
    return value?.ToString();
  }

  private static string? TryGetDiagnosticSummary(Exception exception, Type exceptionType)
  {
    PropertyInfo? property = exceptionType.GetProperty("DiagnosticSummary", BindingFlags.Instance | BindingFlags.Public);
    if (property is null || !property.CanRead || property.PropertyType != typeof(string))
    {
      return null;
    }

    return property.GetValue(exception) as string;
  }
}

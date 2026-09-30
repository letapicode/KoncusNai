using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Runtime;

internal sealed record DictationRuntimeChoice(string Label, string Preference, string? Device = null);

internal static class CohereRuntimePresentation
{
  internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "DictateAnywhere", "cohere-auto", "0.2.4");

  internal static IReadOnlyList<DictationRuntimeChoice> Choices(TranscriptionModelSelection selection)
  {
    List<DictationRuntimeChoice> choices = [new("Automatic (measured)", "automatic"), new("CPU", "cpu"),
      new("GPU (best validated device)", "gpu"), new("Original runtime", "original")];
    if (!CohereRuntimePreparation.Supports(selection)) return choices;
    try
    {
      string model = CohereRuntimePreparation.ResolveModelPath(selection).ToLowerInvariant();
      string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(model))).ToLowerInvariant()[..24];
      using JsonDocument cache = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, key, "selection.json")));
      foreach (JsonElement result in cache.RootElement.GetProperty("results").EnumerateArray())
      {
        if (result.GetProperty("backend").GetString() != "vulkan" || !result.GetProperty("eligible").GetBoolean()) continue;
        string? device = result.GetProperty("device_key").GetString();
        if (device is not null) choices.Add(new(result.GetProperty("actual_device").GetString() ?? device, "gpu", device));
      }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException) { }
    return choices;
  }

  internal static string Status(string preference)
  {
    try
    {
      using JsonDocument status = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "last-runtime.json")));
      JsonElement value = status.RootElement;
      using Process worker = Process.GetProcessById(value.GetProperty("pid").GetInt32());
      if (worker.HasExited) return "Dictation runtime is idle. Actual backend appears when the model loads.";
      string result = $"Actual: {value.GetProperty("backend").GetString()} · {value.GetProperty("device").GetString()} · {value.GetProperty("precision").GetString()}";
      string? reason = value.GetProperty("fallback_reason").GetString();
      if (!string.IsNullOrEmpty(reason)) result += ". " + FormatReason(reason);
      if (value.GetProperty("mode").GetString() != preference) result += " Settings change pending worker restart.";
      return result;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or System.ComponentModel.Win32Exception)
    {
      return "Prepare acceleration to compare local CPU/GPU performance. The original runtime is available before preparation.";
    }
  }

  private static string FormatReason(string reason) => reason switch
  {
    "original_runtime_override" => "Original runtime selected.",
    "native_setting_unsupported" => "The language or punctuation setting uses the original runtime.",
    "insufficient_available_memory" => "Not enough available memory for acceleration. Close other apps and retry.",
    "insufficient_device_memory" => "Not enough free GPU memory. Close other GPU apps and retry.",
    "hardware_runtime_or_model_changed_prepare_again" or "selection_policy_changed_prepare_again" =>
      "Hardware, driver, runtime or model changed. Prepare acceleration again.",
    "original_retained_no_safe_measured_improvement" => "Calibration retained the original runtime for quality, latency or resource reasons.",
    _ when reason.Contains("NativeInputOutsideValidation", StringComparison.Ordinal) => "Recordings over 45 seconds use the original runtime.",
    _ when reason.StartsWith("native_", StringComparison.Ordinal) => "Native acceleration failed. The original runtime is active; retry preparation to repair acceleration.",
    _ => "Acceleration is not prepared or a validated device is unavailable. Prepare acceleration or choose the original runtime.",
  };
}

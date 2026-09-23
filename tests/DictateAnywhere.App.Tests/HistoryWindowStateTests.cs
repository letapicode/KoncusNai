using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DictateAnywhere.App.History;
using DictateAnywhere.Core.Contracts;
using Xunit;

namespace DictateAnywhere.App.Tests;

[Collection(WpfApplicationCollection.Name)]
[Trait("Category", "WindowsWpf")]
public sealed class HistoryWindowStateTests
{
  [Fact]
  public void HistoryPreflightPolicy_HasOwnedStateCoverageAndNotTestedOperatorCases()
  {
    string root = FindRepoRoot();
    string policyPath = Path.Combine(root, "scripts", "history-preflight-scenarios.json");
    using JsonDocument policy = JsonDocument.Parse(File.ReadAllText(policyPath));
    JsonElement policyRoot = policy.RootElement;

    Assert.Equal(1, policyRoot.GetProperty("schemaVersion").GetInt32());
    Assert.Equal("NotTested", policyRoot.GetProperty("manualEvidenceStatus").GetString());
    JsonElement[] owners = policyRoot.GetProperty("owners").EnumerateArray().ToArray();
    JsonElement[] states = policyRoot.GetProperty("states").EnumerateArray().ToArray();
    JsonElement[] operatorCases = policyRoot.GetProperty("operatorCases").EnumerateArray().ToArray();
    Assert.Equal(8, owners.Length);
    Assert.Equal(21, states.Length);
    Assert.Equal(9, operatorCases.Length);

    HashSet<string> ownerIds = owners
      .Select(owner => owner.GetProperty("id").GetString() ?? string.Empty)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);
    HashSet<string> stateIds = new(StringComparer.OrdinalIgnoreCase);
    foreach (JsonElement state in states)
    {
      string id = state.GetProperty("id").GetString() ?? string.Empty;
      Assert.False(string.IsNullOrWhiteSpace(id));
      Assert.True(stateIds.Add(id), $"Duplicate History state id: {id}");
      Assert.Contains(state.GetProperty("ownerId").GetString() ?? string.Empty, ownerIds);
      Assert.NotEmpty(state.GetProperty("coverageTests").EnumerateArray());
      JsonElement controls = state.GetProperty("controls");
      foreach (string control in new[] { "search", "list", "editor", "save", "delete" })
      {
        Assert.False(string.IsNullOrWhiteSpace(controls.GetProperty(control).GetString()));
      }
    }

    HashSet<string> caseIds = new(StringComparer.OrdinalIgnoreCase);
    foreach (JsonElement operatorCase in operatorCases)
    {
      string id = operatorCase.GetProperty("id").GetString() ?? string.Empty;
      Assert.True(caseIds.Add(id), $"Duplicate History operator case id: {id}");
      Assert.Equal("NotTested", operatorCase.GetProperty("status").GetString());
    }
  }

  [Fact]
  public async Task TestOwnedScratchStore_WritesOnlyInsideItsUniqueBoundaryAndCleansUp()
  {
    string? providedRoot = Environment.GetEnvironmentVariable("NOTYPE_HISTORY_PREFLIGHT_SCRATCH");
    bool ownsRoot = string.IsNullOrWhiteSpace(providedRoot);
    string root = ownsRoot
      ? Path.Combine(Path.GetTempPath(), "NotypeHistoryPreflightTests", Guid.NewGuid().ToString("N"))
      : Path.GetFullPath(providedRoot!);
    string runRoot = Path.Combine(root, Guid.NewGuid().ToString("N"));
    string filePath = Path.Combine(runRoot, "history.local.jsonl");
    Directory.CreateDirectory(runRoot);
    try
    {
      LocalDictationHistoryStore store = new(filePath);
      await store.RecordAsync(CreateRecord("Safe fixture"));

      Assert.StartsWith(
        Path.GetFullPath(root) + Path.DirectorySeparatorChar,
        Path.GetFullPath(filePath),
        StringComparison.OrdinalIgnoreCase);
      Assert.Single(await store.ReadRecentAsync(10));
    }
    finally
    {
      if (Directory.Exists(runRoot))
      {
        Directory.Delete(runRoot, recursive: true);
      }
      if (ownsRoot && Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }

    Assert.False(HistoryFileAccessCoordinator.IsTracked(filePath));
  }

  [Fact]
  public void SaveRequiresASelectedNonemptyDirtyEntry()
  {
    RunOnSta(() =>
    {
      DictationHistoryRecord record = CreateRecord("Safe fixture");
      using HistoryWindowHarness harness = new([record]);

      harness.Window.RefreshPersistedHistoryAsync().GetAwaiter().GetResult();
      harness.Window.HistoryListBox.SelectedIndex = 0;

      Assert.False(harness.Window.SaveEditsButton.IsEnabled);
      Assert.True(harness.Window.DeleteButton.IsEnabled);

      harness.Window.TranscriptTextBox.Text = "Changed safe fixture";
      Assert.True(harness.Window.SaveEditsButton.IsEnabled);

      harness.Window.TranscriptTextBox.Text = "   ";
      Assert.False(harness.Window.SaveEditsButton.IsEnabled);

      harness.Window.TranscriptTextBox.Text = record.FinalText;
      Assert.False(harness.Window.SaveEditsButton.IsEnabled);
    });
  }

  [Fact]
  public void RefreshPreservesDirtyEditorOnlyWhileTheSelectedRecordStillExists()
  {
    RunOnSta(() =>
    {
      DictationHistoryRecord record = CreateRecord("Safe fixture");
      using HistoryWindowHarness harness = new([record]);

      harness.Window.RefreshPersistedHistoryAsync().GetAwaiter().GetResult();
      harness.Window.HistoryListBox.SelectedIndex = 0;
      harness.Window.TranscriptTextBox.Text = "Unsaved safe fixture";
      Assert.NotEqual("Select a saved entry.", harness.Window.ActionStatusTextBlock.Text);
      Assert.True(harness.Window.DeleteButton.IsEnabled);
      Assert.True(harness.Window.SaveEditsButton.IsEnabled);

      harness.Window.RefreshPersistedHistoryAsync().GetAwaiter().GetResult();

      Assert.Single(harness.Window.HistoryListBox.Items);
      Assert.NotNull(harness.Window.HistoryListBox.SelectedItem);
      Assert.Equal("Unsaved safe fixture", harness.Window.TranscriptTextBox.Text);
      Assert.True(harness.Window.SaveEditsButton.IsEnabled);

      harness.Records = [];
      harness.Window.RefreshPersistedHistoryAsync().GetAwaiter().GetResult();

      Assert.Empty(harness.Window.TranscriptTextBox.Text);
      Assert.Equal("Select a saved entry.", harness.Window.ActionStatusTextBlock.Text);
      Assert.False(harness.Window.SaveEditsButton.IsEnabled);
      Assert.False(harness.Window.DeleteButton.IsEnabled);
    });
  }

  private static DictationHistoryRecord CreateRecord(string text) => new(
    new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero),
    "safe-fixture",
    TranscriptionProviderIds.CohereLocal,
    "safe-model",
    text,
    text,
    TimeSpan.Zero,
    TimeSpan.Zero,
    TimeSpan.Zero);

  private static string FindRepoRoot()
  {
    DirectoryInfo? current = new(AppContext.BaseDirectory);
    while (current is not null)
    {
      if (File.Exists(Path.Combine(current.FullName, "DictateAnywhere.sln")))
      {
        return current.FullName;
      }
      current = current.Parent;
    }
    throw new InvalidOperationException("Could not locate the repository root.");
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "The bounded STA helper transfers WPF failures to the asserting thread.")]
  private static void RunOnSta(Action action)
  {
    Exception? failure = null;
    Thread thread = new(() =>
    {
      try
      {
        if (Application.Current is null)
        {
          DictateAnywhere.App.App app = new();
          app.InitializeComponent();
        }

        action();
      }
      catch (Exception ex)
      {
        failure = ex;
      }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "History STA test timed out.");
    if (failure is not null)
    {
      throw new InvalidOperationException("History STA test failed.", failure);
    }
  }

  private sealed class HistoryWindowHarness : IDisposable
  {
    private readonly HistoryQueryCoordinator queryCoordinator;
    private readonly HistoryCommandCoordinator commandCoordinator;

    public HistoryWindowHarness(IReadOnlyList<DictationHistoryRecord> records)
    {
      Records = records.Select(record => record.Normalize()).ToArray();
      queryCoordinator = new HistoryQueryCoordinator((_, _, _) =>
        Task.FromResult(Records));
      commandCoordinator = new HistoryCommandCoordinator(
        _ => new NoOpDictationStore(),
        _ => new NoOpChatStore());
      Window = new HistoryWindow(
        AppSettings.Default,
        new NoOpDiagnostics(),
        queryCoordinator,
        commandCoordinator);
    }

    public HistoryWindow Window { get; }

    public IReadOnlyList<DictationHistoryRecord> Records { get; set; }

    public void Dispose()
    {
      Window.DisposeAsync().AsTask().GetAwaiter().GetResult();
      Window.Close();
    }
  }

  private sealed class NoOpDiagnostics : IDiagnostics
  {
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
  }

  private sealed class NoOpDictationStore : IDictationHistoryCommandStore
  {
    public Task RecordAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default) =>
      Task.CompletedTask;

    public Task<bool> UpdateAsync(DictationHistoryRecord record, CancellationToken cancellationToken = default) =>
      Task.FromResult(true);

    public Task<int> DeleteSessionsAsync(
      IEnumerable<string> sessionIds,
      CancellationToken cancellationToken = default) => Task.FromResult(1);
  }

  private sealed class NoOpChatStore : IChatHistoryCommandStore
  {
    public Task SaveAsync(ChatHistoryRecord record, CancellationToken cancellationToken = default) =>
      Task.CompletedTask;

    public Task<int> DeleteConversationsAsync(
      IEnumerable<string> conversationIds,
      CancellationToken cancellationToken = default) => Task.FromResult(1);
  }
}

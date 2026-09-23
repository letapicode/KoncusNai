using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class ReaderVoicePreviewSessionTests
{
  [Xunit.Fact]
  public async Task PrepareAsync_TransitionsToPlayingAndReusesTheExistingCache()
  {
    using PreviewTestWorkspace workspace = await PreviewTestWorkspace.CreateAsync();
    ImmediateSpeechService speech = new(workspace.SourcePath);
    using ReaderVoicePreviewSession session = workspace.CreateSession(speech);
    ReaderLanguageOption language = ReaderLanguageRegistry.Languages.First(option => option.Code == "hi");

    string first = await session.PrepareAsync(language, language.DefaultVoice);
    Xunit.Assert.Equal(ReaderVoicePreviewState.Playing, session.State);
    Xunit.Assert.True(session.FinishPlayback());

    string second = await session.PrepareAsync(language, language.DefaultVoice);

    Xunit.Assert.Equal(first, second);
    Xunit.Assert.Equal(1, speech.CallCount);
    Xunit.Assert.Equal(ReaderVoicePreviewState.Playing, session.State);
  }

  [Xunit.Fact]
  public async Task Stop_CancelsPreparationAndRejectsALateResolution()
  {
    using PreviewTestWorkspace workspace = await PreviewTestWorkspace.CreateAsync();
    ControlledSpeechService speech = new();
    using ReaderVoicePreviewSession session = workspace.CreateSession(speech);
    ReaderLanguageOption language = ReaderLanguageRegistry.DefaultLanguage;
    Task<string> preparation = session.PrepareAsync(language, language.DefaultVoice);
    await speech.Started.Task;

    Xunit.Assert.Equal(ReaderVoicePreviewState.Preparing, session.State);
    Xunit.Assert.True(session.CanStop);
    session.Stop();
    speech.Completion.SetResult(CreateSpeechResult(workspace.SourcePath));

    _ = await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparation);
    Xunit.Assert.Equal(ReaderVoicePreviewState.Idle, session.State);
    Xunit.Assert.False(session.CanStop);
  }

  [Xunit.Fact]
  public async Task NewPreparation_RejectsTheOlderResultWithoutOverwritingTheCurrentState()
  {
    using PreviewTestWorkspace workspace = await PreviewTestWorkspace.CreateAsync();
    SequencedSpeechService speech = new();
    using ReaderVoicePreviewSession session = workspace.CreateSession(speech);
    ReaderLanguageOption english = ReaderLanguageRegistry.DefaultLanguage;
    ReaderLanguageOption hindi = ReaderLanguageRegistry.Languages.First(option => option.Code == "hi");
    Task<string> older = session.PrepareAsync(english, english.DefaultVoice);
    await speech.FirstStarted.Task;

    Task<string> current = session.PrepareAsync(hindi, hindi.DefaultVoice);
    await speech.SecondStarted.Task;
    speech.SecondCompletion.SetResult(CreateSpeechResult(workspace.SourcePath));
    string currentPath = await current;
    speech.FirstCompletion.SetResult(CreateSpeechResult(workspace.SourcePath));

    _ = await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => older);
    Xunit.Assert.Equal(ReaderVoicePreviewState.Playing, session.State);
    Xunit.Assert.Contains("hi", Path.GetFileName(currentPath), StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task FailedPreparation_ReturnsToIdleAndCanBeRetried()
  {
    using PreviewTestWorkspace workspace = await PreviewTestWorkspace.CreateAsync();
    FailingSpeechService speech = new();
    using ReaderVoicePreviewSession session = workspace.CreateSession(speech);
    ReaderLanguageOption language = ReaderLanguageRegistry.DefaultLanguage;

    _ = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(
      () => session.PrepareAsync(language, language.DefaultVoice));

    Xunit.Assert.Equal(ReaderVoicePreviewState.Idle, session.State);
    Xunit.Assert.False(session.CanStop);
  }

  [Xunit.Fact]
  public async Task FinishPlayback_OnlyTransitionsAnActivePlayback()
  {
    using PreviewTestWorkspace workspace = await PreviewTestWorkspace.CreateAsync();
    using ReaderVoicePreviewSession session = workspace.CreateSession(new ImmediateSpeechService(workspace.SourcePath));
    ReaderLanguageOption language = ReaderLanguageRegistry.DefaultLanguage;

    Xunit.Assert.False(session.FinishPlayback());
    _ = await session.PrepareAsync(language, language.DefaultVoice);
    Xunit.Assert.True(session.FinishPlayback());
    Xunit.Assert.False(session.FinishPlayback());
    Xunit.Assert.Equal(ReaderVoicePreviewState.Idle, session.State);
  }

  [Xunit.Fact]
  public async Task Dispose_CancelsPreparationAndRejectsFutureWork()
  {
    using PreviewTestWorkspace workspace = await PreviewTestWorkspace.CreateAsync();
    ControlledSpeechService speech = new();
    ReaderVoicePreviewSession session = workspace.CreateSession(speech);
    ReaderLanguageOption language = ReaderLanguageRegistry.DefaultLanguage;
    Task<string> preparation = session.PrepareAsync(language, language.DefaultVoice);
    await speech.Started.Task;

    session.Dispose();
    speech.Completion.SetResult(CreateSpeechResult(workspace.SourcePath));

    _ = await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparation);
    _ = await Xunit.Assert.ThrowsAsync<ObjectDisposedException>(
      () => session.PrepareAsync(language, language.DefaultVoice));
  }

  private static TextToSpeechResult CreateSpeechResult(string sourcePath) => new(
    sourcePath,
    TimeSpan.FromSeconds(1),
    1,
    "test",
    "test");

  private sealed class ImmediateSpeechService(string sourcePath) : ITextToSpeechService
  {
    public int CallCount { get; private set; }

    public Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default)
    {
      CallCount++;
      return Task.FromResult(CreateSpeechResult(sourcePath));
    }
  }

  private sealed class ControlledSpeechService : ITextToSpeechService
  {
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource<TextToSpeechResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default)
    {
      Started.TrySetResult();
      return Completion.Task;
    }
  }

  private sealed class FailingSpeechService : ITextToSpeechService
  {
    public Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default) =>
      Task.FromException<TextToSpeechResult>(new InvalidOperationException("Preview synthesis failed."));
  }

  private sealed class SequencedSpeechService : ITextToSpeechService
  {
    private int callCount;

    public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource<TextToSpeechResult> FirstCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource<TextToSpeechResult> SecondCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default)
    {
      int call = Interlocked.Increment(ref callCount);
      if (call == 1)
      {
        FirstStarted.TrySetResult();
        return FirstCompletion.Task;
      }

      SecondStarted.TrySetResult();
      return SecondCompletion.Task;
    }
  }

  private sealed class PreviewTestWorkspace : IDisposable
  {
    private PreviewTestWorkspace(string directoryPath, string sourcePath)
    {
      DirectoryPath = directoryPath;
      SourcePath = sourcePath;
    }

    public string DirectoryPath { get; }

    public string SourcePath { get; }

    public static async Task<PreviewTestWorkspace> CreateAsync()
    {
      string directoryPath = Path.Combine(Path.GetTempPath(), $"notype-preview-session-{Guid.NewGuid():N}");
      string sourcePath = Path.Combine(directoryPath, "source.wav");
      Directory.CreateDirectory(directoryPath);
      await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);
      return new PreviewTestWorkspace(directoryPath, sourcePath);
    }

    public ReaderVoicePreviewSession CreateSession(ITextToSpeechService speechService) => new(
      speechService,
      new ReaderVoicePreviewCache(
        Path.Combine(DirectoryPath, "cache"),
        new ReaderVoicePreviewAssetStore(Path.Combine(DirectoryPath, "empty-assets"))));

    public void Dispose()
    {
      if (Directory.Exists(DirectoryPath))
      {
        Directory.Delete(DirectoryPath, recursive: true);
      }

    }
  }
}

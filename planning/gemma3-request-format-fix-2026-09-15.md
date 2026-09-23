# Gemma 3 request formatting fix

## Confirmed cause

The September 15 diagnostics recorded HTTP 400 from llama.cpp with
“Conversation roles must alternate user/assistant/user/assistant/...”. The final
reported failure took 8,385.87 ms. This was elapsed time before rejection, not an
eight-second timeout and not a generated model answer.

The workbench supplied a brand system message and the llama.cpp client prepended
another system message containing its response policy. Gemma 3's template rejected
that message sequence. Google's guidance places Gemma 3 system-level instructions
inside the initial user turn:
https://ai.google.dev/gemma/docs/core/prompt-structure

## Changes

- `LlamaCppPromptFormatter` combines policy, brand instructions, and file/system
  context into the first Gemma 3 user turn. Consecutive turns of the same role are
  combined in the outgoing copy to handle retries and canceled requests. Original
  conversation records remain untouched. Orphaned assistant-first conversations
  are rejected rather than silently dropping their content.
- Other llama.cpp models, including Qwen, retain a single initial system turn.
  No model, runtime, token budget, or production timeout setting was changed.
- `LlamaCppChatService` sends the adapted request, preserves HTTP status for error
  classification, translates its own expired deadline into `TimeoutException`,
  and preserves caller cancellation. Empty choice arrays produce the existing
  empty-response error rather than an indexing exception.
- Workbench error notices distinguish HTTP 400 rejection, other server errors,
  connection failures, and actual timeouts. User cancellation retains “Response
  stopped.” Notices identify themselves as app errors and no longer suggest that
  Continue or a smaller answer will fix an infrastructure failure.
- Old and new app failure notices are excluded from future model requests. Their
  existing display/history text is retained, including after reloading a record.
- Fixed the unfinished smoke test's `Elapsed` reference to the actual `Duration`
  property and its socket reservation's disposal.

## Verification

- Inference suite: **134 passed**; four opt-in integration tests skipped in the
  ordinary run. The Gemma integration test was then exercised explicitly as below.
- Focused workbench controller/send tests: **17 passed**. Covers pending files,
  preservation of stored context, old/new failure filtering, failure categories,
  cancellation, and existing conversation/service ownership behavior.
- Real installed Gemma 3 4B Q4_K_M through `LlamaCppChatService`: **passed**, using
  a separate loopback port and test-owned server. No model download occurred.
  The service disposed its own server after the test; app processes were not
  stopped or restarted, and user settings/history were not read or modified by
  the test.
- Exact test question: `what is an algorithm`
- Actual first response: “An algorithm is a set of well-defined instructions for
  solving a problem or accomplishing a task. It's like a recipe for a computer!”
- Follow-up: `Give one everyday example in two sentences.`
- Actual follow-up: “Making toast is a great example of an algorithm. You insert
  bread, set the timer, and then the toaster automatically executes the steps to
  brown the bread.”
- First completion duration: **3.97 seconds**, excluding server preparation.
  Whole integration case: approximately 12 seconds. The smoke test caps output at
  192 tokens and uses a three-minute test deadline; production settings retain
  their existing 1,536-token maximum and 15-minute request deadline.
- Release builds completed as part of the test runs. Git whitespace check passed.

Logs: `artifacts/gemma3-request-fix/real-model.log`, `inference-final.log`, and
`app-final.log`. Source Release output is rebuilt. No installer was regenerated.
An already running copy must be restarted to load the fix; the next source launch
uses the rebuilt version. Unrelated working-tree changes and staging were preserved.

## Repeat the real-model check

```powershell
$env:KONCUS_RUN_LLAMA_MODEL_TESTS = '1'
dotnet test tests/DictateAnywhere.Inference.Tests/DictateAnywhere.Inference.Tests.csproj -c Release --filter FullyQualifiedName~LlamaCppModelSmokeTests --logger "console;verbosity=detailed"
```

The environment variable applies to the current shell. Normal CI stays offline.

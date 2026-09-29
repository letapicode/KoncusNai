import argparse
import json
import os
import sys
import time
import traceback


def emit(payload: dict) -> None:
    print(json.dumps(payload, ensure_ascii=True), flush=True)


def get_value(payload: dict, *names: str, default=None):
    for name in names:
        if name in payload:
            return payload[name]

    return default


def decode_text(processor, outputs, audio_chunk_index, language: str) -> str:
    decode_kwargs = {"skip_special_tokens": True}
    if audio_chunk_index is not None:
        decode_kwargs["audio_chunk_index"] = audio_chunk_index
        decode_kwargs["language"] = language

    decoded = processor.decode(outputs, **decode_kwargs)
    if isinstance(decoded, list):
        return ((decoded[0] if decoded else "") or "").strip()

    return str(decoded or "").strip()


def ensure_complete(rows, eos_token_id):
    """A generation budget is not an end-of-speech indication."""
    eos_ids = set(eos_token_id if isinstance(eos_token_id, list) else [eos_token_id])
    if not rows or not eos_ids or None in eos_ids or any(not any(token in eos_ids for token in row[1:]) for row in rows):
        raise RuntimeError("Cohere output was truncated before end-of-sequence; no partial transcript will be inserted.")


def run_warmup(runtime, language, punctuation):
    warmup_audio = os.environ.get("DICTATEANYWHERE_COHERE_WARMUP_AUDIO")
    if warmup_audio:
        _, audio_seconds = runtime.transcribe(warmup_audio, language, punctuation)
        return audio_seconds
    runtime.warmup(language, punctuation)
    return 3.0


class TransformersRuntime:
    def __init__(self, model_dir):
        import torch
        import transformers
        from transformers import AutoProcessor, CohereAsrForConditionalGeneration
        self.processor = AutoProcessor.from_pretrained(model_dir, local_files_only=True)
        device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
        dtype = torch.float16 if torch.cuda.is_available() else torch.float32
        model_load_started = time.perf_counter()
        self.model = CohereAsrForConditionalGeneration.from_pretrained(
            model_dir, dtype=dtype, low_cpu_mem_usage=True, local_files_only=True)
        self.model.to(device)
        self.model.eval()
        model_load_ms = (time.perf_counter() - model_load_started) * 1000.0
        self.metadata = {"device": str(device), "dtype": str(dtype),
                         "backend": "transformers/" + str(device),
                         "model_load_ms": model_load_ms,
                         "torch_version": torch.__version__, "transformers_version": transformers.__version__,
                         "model_class": f"{self.model.__class__.__module__}.{self.model.__class__.__name__}",
                         "processor_class": f"{self.processor.__class__.__module__}.{self.processor.__class__.__name__}"}

    def infer(self, audio, language, punctuation, warmup=False):
        inputs = self.processor(audio, sampling_rate=16000, return_tensors="pt",
                                language=language, punctuation=punctuation)
        audio_chunk_index = inputs.get("audio_chunk_index")
        inputs.to(self.model.device, dtype=self.model.dtype)
        outputs = self.model.generate(**inputs, max_new_tokens=8 if warmup else 256)
        if warmup:
            return ""
        ensure_complete(outputs.tolist(), self.model.generation_config.eos_token_id)
        return decode_text(self.processor, outputs, audio_chunk_index, language)

    def transcribe(self, audio_path, language, punctuation):
        from transformers.audio_utils import load_audio
        audio = load_audio(str(audio_path), sampling_rate=16000)
        return self.infer(audio, language, punctuation), len(audio) / 16000.0

    def warmup(self, language, punctuation):
        import numpy as np
        # Exercise the real encoder and decoder without capturing speech or storing a recording.
        self.infer(np.zeros(48000, dtype=np.float32), language, punctuation, warmup=True)

    def close(self):
        pass


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-dir", required=True)
    parser.add_argument("--fixture-mode", action="store_true")
    args = parser.parse_args()

    model_dir = args.model_dir
    if args.fixture_mode:
        return run_fixture_worker()

    try:
        fallback_reason = None
        native_manifest = os.environ.get("DICTATEANYWHERE_COHERE_NATIVE_MANIFEST")
        runtime = None
        if native_manifest:
            try:
                from cohere_native_runtime import NativeRuntime
                runtime = NativeRuntime(native_manifest, model_dir)
            except Exception as exc:
                fallback_reason = "native_startup_failed:" + type(exc).__name__
        if runtime is None:
            runtime = TransformersRuntime(model_dir)
        warmed = False
        emit(
            {
                "status": "ready",
                "payload": {**runtime.metadata, "fallback_reason": fallback_reason},
            }
        )
    except Exception as exc:  # pragma: no cover - surfaced to parent process
        emit(
            {
                "status": "error",
                "error": str(exc),
                "traceback": traceback.format_exc(),
            }
        )
        return 1

    for raw_line in sys.stdin:
        line = raw_line.strip()
        if not line:
            continue

        try:
            request = json.loads(line)
            operation = str(get_value(request, "operation", "Operation", default="transcribe") or "transcribe").strip().lower()
            if operation == "health_check":
                emit(
                    {
                        "status": "ok",
                        "payload": {"health_check": "model_ready"},
                    }
                )
                continue
            if operation not in ("transcribe", "warmup"):
                raise ValueError("operation must be 'transcribe', 'warmup', or 'health_check'.")
            started = time.perf_counter()
            audio_path = get_value(request, "audio_path", "audioPath", "AudioPath")
            if operation == "transcribe" and not audio_path:
                raise ValueError("audio_path is required.")

            language = str(get_value(request, "language", "Language", default="en") or "en").strip().lower()
            punctuation = get_value(request, "punctuation", "Punctuation", default=True)
            if not isinstance(punctuation, bool):
                raise ValueError("punctuation must be a boolean.")
            if operation == "warmup" and warmed:
                emit({"status": "ok", "payload": {"duration_ms": 0, **runtime.metadata,
                                                     "fallback_reason": fallback_reason}})
                continue
            if not isinstance(runtime, TransformersRuntime) and not runtime.supports(language, punctuation):
                runtime.close()
                runtime = None  # Release native weights before loading the fallback.
                fallback_reason = "native_setting_unsupported"
                runtime = TransformersRuntime(model_dir)
                warmed = False
            try:
                if operation == "warmup":
                    text, audio_seconds = "", run_warmup(runtime, language, punctuation)
                else:
                    text, audio_seconds = runtime.transcribe(audio_path, language, punctuation)
            except Exception as exc:
                if isinstance(runtime, TransformersRuntime):
                    raise
                runtime.close()
                runtime = None
                fallback_reason = "native_request_failed:" + type(exc).__name__
                runtime = TransformersRuntime(model_dir)
                warmed = False
                if operation == "warmup":
                    text, audio_seconds = "", run_warmup(runtime, language, punctuation)
                else:
                    text, audio_seconds = runtime.transcribe(audio_path, language, punctuation)
            if operation == "warmup":
                warmed = True
            duration_ms = (time.perf_counter() - started) * 1000.0
            emit(
                {
                    "status": "ok",
                    "payload": {
                        "text": text,
                        "duration_ms": duration_ms,
                        "language": language,
                        "punctuation": punctuation,
                        "sample_rate_hz": 16000,
                        "audio_seconds": audio_seconds,
                        **runtime.metadata,
                        "fallback_reason": fallback_reason,
                    },
                }
            )
        except Exception as exc:  # pragma: no cover - surfaced to parent process
            emit(
                {
                    "status": "error",
                    "error": str(exc),
                    "traceback": traceback.format_exc(),
                }
            )
            if runtime is None:
                return 1

    runtime.close()
    return 0


def run_fixture_worker() -> int:
    emit(
        {
            "status": "ready",
            "payload": {
                "device": "fixture",
                "dtype": "fixture",
                "model_class": "fixture.CohereAsrForConditionalGeneration",
                "processor_class": "fixture.AutoProcessor",
            },
        }
    )

    for raw_line in sys.stdin:
        line = raw_line.strip()
        if not line:
            continue

        try:
            request = json.loads(line)
            operation = str(get_value(request, "operation", "Operation", default="transcribe") or "transcribe").strip().lower()
            if operation == "health_check":
                emit({"status": "ok", "payload": {"health_check": "model_ready"}})
                continue
            if operation == "warmup":
                emit({"status": "ok", "payload": {"text": "", "duration_ms": 0, "backend": "fixture", "dtype": "fixture"}})
                continue
            if operation != "transcribe":
                raise ValueError("operation must be 'transcribe' or 'health_check'.")
            started = time.perf_counter()
            audio_path = get_value(request, "audio_path", "audioPath", "AudioPath")
            if not audio_path:
                raise ValueError("audio_path is required.")

            language = str(get_value(request, "language", "Language", default="en") or "en").strip().lower()
            punctuation = get_value(request, "punctuation", "Punctuation", default=True)
            if not isinstance(punctuation, bool):
                raise ValueError("punctuation must be a boolean.")
            duration_ms = (time.perf_counter() - started) * 1000.0
            emit(
                {
                    "status": "ok",
                    "payload": {
                        "text": f"fixture cohere transcript in {language}",
                        "duration_ms": duration_ms,
                        "language": language,
                        "punctuation": punctuation,
                        "sample_rate_hz": 16000,
                        "audio_seconds": 0.0,
                    },
                }
            )
        except Exception as exc:  # pragma: no cover - surfaced to parent process
            emit(
                {
                    "status": "error",
                    "error": str(exc),
                    "traceback": traceback.format_exc(),
                }
            )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())

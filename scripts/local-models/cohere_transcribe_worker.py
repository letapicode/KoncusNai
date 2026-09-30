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
    def __init__(self, model_dir, force_cpu=False):
        import torch
        threads = int(os.environ.get("OMP_NUM_THREADS", "4"))
        torch.set_num_threads(threads)
        torch.set_num_interop_threads(1)
        import transformers
        from transformers import AutoProcessor, CohereAsrForConditionalGeneration
        self.processor = AutoProcessor.from_pretrained(model_dir, local_files_only=True)
        use_cuda = torch.cuda.is_available() and not force_cpu
        device = torch.device("cuda" if use_cuda else "cpu")
        dtype = torch.float16 if use_cuda else torch.float32
        model_load_started = time.perf_counter()
        self.model = CohereAsrForConditionalGeneration.from_pretrained(
            model_dir, dtype=dtype, low_cpu_mem_usage=True, local_files_only=True)
        self.model.to(device)
        self.model.eval()
        model_load_ms = (time.perf_counter() - model_load_started) * 1000.0
        self.metadata = {"device": str(device), "dtype": str(dtype),
                         "backend": "transformers/" + str(device),
                         "model_load_ms": model_load_ms, "threads": threads,
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


def default_original_threads():
    import psutil
    from cohere_runtime_selection import thread_candidates
    return thread_candidates({"cpu": {"physical": psutil.cpu_count(logical=False) or 1,
                                      "logical": os.cpu_count() or 1,
                                      "affinity": len(psutil.Process().cpu_affinity())}})[-1]


def run_worker_main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-dir", required=True)
    parser.add_argument("--fixture-mode", action="store_true")
    parser.add_argument("--runtime-mode", choices=("automatic", "cpu", "gpu", "original"), default="automatic")
    parser.add_argument("--runtime-device")
    parser.add_argument("--runtime-device-base64")
    parser.add_argument("--runtime-cache-root", default=os.environ.get("DICTATEANYWHERE_COHERE_SELECTION_ROOT"))
    parser.add_argument("--threads", type=int)
    args = parser.parse_args()
    if args.runtime_device_base64:
        import base64
        args.runtime_device = base64.b64decode(args.runtime_device_base64, validate=True).decode("utf-8")

    model_dir = args.model_dir
    if args.fixture_mode:
        return run_fixture_worker()

    if args.threads is None:
        args.threads = default_original_threads()
    if not 1 <= args.threads <= (os.cpu_count() or 1):
        raise ValueError("Worker thread count exceeds available processors.")
    os.environ["OMP_NUM_THREADS"] = str(args.threads)
    os.environ["MKL_NUM_THREADS"] = str(args.threads)
    os.environ["OPENBLAS_NUM_THREADS"] = "1"

    def load_original():
        # Native device tuning must not change the calibrated original-runtime thread budget.
        os.environ["OMP_NUM_THREADS"] = str(args.threads)
        os.environ["MKL_NUM_THREADS"] = str(args.threads)
        return TransformersRuntime(model_dir, force_cpu=True) if args.runtime_mode == "cpu" else TransformersRuntime(model_dir)

    try:
        fallback_reason = None
        native_manifest = os.environ.get("DICTATEANYWHERE_COHERE_NATIVE_MANIFEST") if args.runtime_mode == "automatic" else None
        if args.runtime_mode == "original":
            native_manifest = None
            fallback_reason = "original_runtime_override"
        elif not native_manifest:
            from cohere_runtime_selection import resolve_selection
            native_manifest, fallback_reason = resolve_selection(model_dir, args.runtime_mode, args.runtime_device, args.runtime_cache_root)
        runtime = None
        if native_manifest:
            try:
                from cohere_native_runtime import NativeRuntime
                runtime = NativeRuntime(native_manifest, model_dir)
            except Exception as exc:
                fallback_reason = "native_startup_failed:" + type(exc).__name__
        if runtime is None:
            runtime = load_original()
        warmed = False
        emit(
            {
                "status": "ready",
                "payload": {**runtime.metadata, "fallback_reason": fallback_reason},
            }
        )
        report_runtime_status(model_dir, args.runtime_mode, runtime.metadata, fallback_reason)
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
                runtime = load_original()
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
                runtime = load_original()
                warmed = False
                if operation == "warmup":
                    text, audio_seconds = "", run_warmup(runtime, language, punctuation)
                else:
                    text, audio_seconds = runtime.transcribe(audio_path, language, punctuation)
            if operation == "warmup":
                warmed = True
            duration_ms = (time.perf_counter() - started) * 1000.0
            report_runtime_status(model_dir, args.runtime_mode, runtime.metadata, fallback_reason)
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


def report_runtime_status(model_dir, mode, metadata, reason):
    if os.environ.get("DICTATEANYWHERE_COHERE_CALIBRATION") == "1":
        return
    from cohere_runtime_selection import atomic_json, root_directory
    try:
        atomic_json(root_directory() / "last-runtime.json", {"model_dir": model_dir, "mode": mode,
                    "pid": os.getpid(), "updated_utc": time.time(), "backend": metadata.get("backend"),
                    "device": metadata.get("device"), "precision": metadata.get("dtype"), "fallback_reason": reason})
    except OSError:
        pass  # Status rendering must never interrupt transcription.


def inference_lease():
    from cohere_runtime_selection import root_directory, exclusive_file
    import contextlib
    @contextlib.contextmanager
    def lease():
        if os.environ.get("DICTATEANYWHERE_COHERE_CALIBRATION") != "1":
            # A preparation process owns this lock until conversion/calibration is complete.
            with exclusive_file(root_directory() / "preparation.lock"):
                pass
        with exclusive_file(root_directory() / "inference.lock"):
            yield
    return lease()


def main() -> int:
    if "--fixture-mode" in sys.argv:
        return run_worker_main()
    try:
        with inference_lease():
            return run_worker_main()
    except RuntimeError as exc:
        emit({"status": "error", "error": str(exc)})
        return 1


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

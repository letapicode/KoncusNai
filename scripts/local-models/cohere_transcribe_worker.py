import argparse
import json
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


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-dir", required=True)
    parser.add_argument("--fixture-mode", action="store_true")
    args = parser.parse_args()

    model_dir = args.model_dir
    if args.fixture_mode:
        return run_fixture_worker()

    try:
        import torch
        import transformers
        from transformers import AutoProcessor, CohereAsrForConditionalGeneration
        from transformers.audio_utils import load_audio

        processor = AutoProcessor.from_pretrained(model_dir)
        device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
        dtype = torch.float16 if torch.cuda.is_available() else torch.float32
        model = CohereAsrForConditionalGeneration.from_pretrained(
            model_dir,
            dtype=dtype,
            low_cpu_mem_usage=True,
        )
        model.to(device)
        model.eval()
        emit(
            {
                "status": "ready",
                "payload": {
                    "device": str(device),
                    "dtype": str(dtype),
                    "torch_version": torch.__version__,
                    "transformers_version": transformers.__version__,
                    "model_class": f"{model.__class__.__module__}.{model.__class__.__name__}",
                    "processor_class": f"{processor.__class__.__module__}.{processor.__class__.__name__}",
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
            audio = load_audio(str(audio_path), sampling_rate=16000)
            audio_seconds = float(len(audio)) / 16000.0
            inputs = processor(
                audio,
                sampling_rate=16000,
                return_tensors="pt",
                language=language,
                punctuation=punctuation,
            )
            audio_chunk_index = inputs.get("audio_chunk_index")
            inputs.to(model.device, dtype=model.dtype)
            outputs = model.generate(**inputs, max_new_tokens=256)
            text = decode_text(processor, outputs, audio_chunk_index, language)
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

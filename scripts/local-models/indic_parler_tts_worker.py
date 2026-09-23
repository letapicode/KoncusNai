"""Persistent CPU-first Indic Parler-TTS worker for Koncus Nai.

Stdout is reserved for the JSON-lines protocol. Operational messages go to stderr.
"""

from __future__ import annotations

import argparse
import gc
import json
import os
import sys
import time
import traceback
import wave
from dataclasses import dataclass
from pathlib import Path
from typing import Any


ALLOWED_DEVICES = {"auto", "cpu", "cuda", "mps"}
ALLOWED_DTYPES = {"auto", "float32", "bfloat16", "float16"}
DEFAULT_MINIMUM_GPU_BYTES = 6 * 1024**3
MODEL_ID = "ai4bharat/indic-parler-tts"
MODEL_REVISION = "7b527af5ee8ed1f9a28d80b19703ed9bb8ba10ca"
DESCRIPTION_MODEL_ID = "google/flan-t5-large"
DESCRIPTION_MODEL_REVISION = "0613663d0d48ea86ba8cb3d7a44f0f65dc596a2a"


def emit(payload: dict[str, Any]) -> None:
    print(json.dumps(payload, ensure_ascii=True), flush=True)


def log(message: str) -> None:
    print(message, file=sys.stderr, flush=True)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-id", default="ai4bharat/indic-parler-tts")
    parser.add_argument("--cache-dir", required=True)
    parser.add_argument("--device", default=os.environ.get("TTS_DEVICE", "auto"))
    parser.add_argument("--dtype", default=os.environ.get("TTS_DTYPE", "auto"))
    parser.add_argument("--cpu-threads", type=int, default=_optional_positive_int("TTS_CPU_THREADS"))
    parser.add_argument("--minimum-gpu-bytes", type=int, default=DEFAULT_MINIMUM_GPU_BYTES)
    parser.add_argument("--fixture-mode", action="store_true")
    parser.add_argument("--fixture-cuda", choices=("unavailable", "cuda", "rocm"), default="unavailable")
    parser.add_argument("--fixture-mps", action="store_true")
    parser.add_argument("--fixture-bfloat16", action="store_true")
    parser.add_argument("--fixture-gpu-bytes", type=int, default=8 * 1024**3)
    parser.add_argument("--simulate-oom-once", action="store_true")
    return parser.parse_args()


def _optional_positive_int(name: str) -> int | None:
    raw = os.environ.get(name)
    if raw is None or not raw.strip():
        return None
    value = int(raw)
    if value <= 0:
        raise ValueError(f"{name} must be a positive integer.")
    return value


@dataclass(frozen=True)
class DeviceSelection:
    device: str
    backend: str
    dtype: str
    gpu_name: str | None = None
    fallback_occurred: bool = False
    fallback_reason: str | None = None


class TorchCapabilities:
    def __init__(self, torch_module: Any):
        self.torch = torch_module

    @property
    def cuda_available(self) -> bool:
        return bool(self.torch.cuda.is_available())

    @property
    def cuda_backend(self) -> str:
        return "rocm" if getattr(self.torch.version, "hip", None) else "cuda"

    @property
    def cuda_name(self) -> str:
        return str(self.torch.cuda.get_device_name(0))

    @property
    def cuda_memory_bytes(self) -> int | None:
        try:
            return int(self.torch.cuda.get_device_properties(0).total_memory)
        except (AttributeError, RuntimeError, TypeError, ValueError):
            return None

    @property
    def cuda_bfloat16(self) -> bool:
        checker = getattr(self.torch.cuda, "is_bf16_supported", None)
        return bool(checker()) if checker is not None else False

    @property
    def mps_available(self) -> bool:
        mps = getattr(getattr(self.torch, "backends", None), "mps", None)
        return bool(mps is not None and mps.is_available())


class FixtureCapabilities:
    def __init__(self, arguments: argparse.Namespace):
        self.arguments = arguments

    @property
    def cuda_available(self) -> bool:
        return self.arguments.fixture_cuda != "unavailable"

    @property
    def cuda_backend(self) -> str:
        return self.arguments.fixture_cuda if self.cuda_available else "cuda"

    @property
    def cuda_name(self) -> str:
        return "Fixture GPU"

    @property
    def cuda_memory_bytes(self) -> int | None:
        return self.arguments.fixture_gpu_bytes

    @property
    def cuda_bfloat16(self) -> bool:
        return self.arguments.fixture_bfloat16

    @property
    def mps_available(self) -> bool:
        return self.arguments.fixture_mps


def resolve_device(
    capabilities: Any,
    requested_device: str,
    requested_dtype: str,
    minimum_gpu_bytes: int,
) -> DeviceSelection:
    device_policy = str(requested_device or "auto").strip().lower()
    dtype_policy = str(requested_dtype or "auto").strip().lower()
    if device_policy not in ALLOWED_DEVICES:
        raise ValueError(f"TTS_DEVICE must be one of: {', '.join(sorted(ALLOWED_DEVICES))}.")
    if dtype_policy not in ALLOWED_DTYPES:
        raise ValueError(f"TTS_DTYPE must be one of: {', '.join(sorted(ALLOWED_DTYPES))}.")

    if device_policy == "cuda":
        if not capabilities.cuda_available:
            raise RuntimeError("TTS_DEVICE=cuda was requested, but PyTorch exposes no CUDA or ROCm accelerator.")
        _require_gpu_memory(capabilities.cuda_memory_bytes, minimum_gpu_bytes, forced=True)
        return _gpu_selection(capabilities, dtype_policy)

    if device_policy == "mps":
        if not capabilities.mps_available:
            raise RuntimeError("TTS_DEVICE=mps was requested, but Apple Metal/MPS is unavailable.")
        return _mps_selection(dtype_policy)

    if device_policy == "cpu":
        return _cpu_selection(dtype_policy, allow_precision_fallback=False)

    if capabilities.cuda_available:
        enough, reason = _require_gpu_memory(capabilities.cuda_memory_bytes, minimum_gpu_bytes, forced=False)
        if enough:
            return _gpu_selection(capabilities, dtype_policy)
        return _cpu_selection(dtype_policy, allow_precision_fallback=True, fallback_reason=reason)
    if capabilities.mps_available:
        return _mps_selection(dtype_policy)
    return _cpu_selection(
        dtype_policy,
        allow_precision_fallback=True,
        fallback_reason="No compatible CUDA, ROCm, or MPS accelerator was available.",
    )


def _require_gpu_memory(total_bytes: int | None, minimum_bytes: int, forced: bool) -> tuple[bool, str | None]:
    if total_bytes is None or minimum_bytes <= 0 or total_bytes >= minimum_bytes:
        return True, None
    reason = (
        f"The accelerator reports {total_bytes / 1024**3:.1f} GiB memory; "
        f"Indic Parler-TTS requires at least {minimum_bytes / 1024**3:.1f} GiB by policy."
    )
    if forced:
        raise RuntimeError(reason)
    return False, reason


def _gpu_selection(capabilities: Any, dtype_policy: str) -> DeviceSelection:
    if dtype_policy == "auto":
        dtype = "bfloat16" if capabilities.cuda_bfloat16 else "float16"
    elif dtype_policy == "bfloat16" and not capabilities.cuda_bfloat16:
        raise RuntimeError("TTS_DTYPE=bfloat16 was requested, but the selected accelerator does not support BF16.")
    else:
        dtype = dtype_policy
    return DeviceSelection("cuda:0", capabilities.cuda_backend, dtype, capabilities.cuda_name)


def _mps_selection(dtype_policy: str) -> DeviceSelection:
    if dtype_policy == "bfloat16":
        raise RuntimeError("TTS_DTYPE=bfloat16 is not enabled for the conservative MPS path.")
    dtype = "float16" if dtype_policy == "auto" else dtype_policy
    return DeviceSelection("mps", "mps", dtype, "Apple Metal")


def _cpu_selection(
    dtype_policy: str,
    allow_precision_fallback: bool,
    fallback_reason: str | None = None,
) -> DeviceSelection:
    if dtype_policy in {"float16", "bfloat16"}:
        if not allow_precision_fallback:
            raise RuntimeError(f"TTS_DTYPE={dtype_policy} is unsupported for the safe CPU path; use float32 or auto.")
        precision_reason = f"TTS_DTYPE={dtype_policy} is unsupported on the safe CPU path; FP32 was selected."
        fallback_reason = f"{fallback_reason} {precision_reason}".strip() if fallback_reason else precision_reason
    return DeviceSelection(
        "cpu",
        "cpu",
        "float32",
        fallback_occurred=bool(fallback_reason),
        fallback_reason=fallback_reason,
    )


def load_runtime(arguments: argparse.Namespace) -> dict[str, Any]:
    if arguments.cpu_threads is not None and arguments.cpu_threads <= 0:
        raise ValueError("--cpu-threads must be positive.")
    # Keep Hugging Face's credential home unchanged so a token saved for the
    # current Windows user by `hf auth login` remains discoverable. Model files
    # still stay inside Koncus Nai's private cache through each explicit
    # `cache_dir` argument below.
    os.environ.setdefault("HF_HUB_DISABLE_TELEMETRY", "1")
    Path(arguments.cache_dir).mkdir(parents=True, exist_ok=True)

    if arguments.fixture_mode:
        selection = resolve_device(
            FixtureCapabilities(arguments), arguments.device, arguments.dtype, arguments.minimum_gpu_bytes
        )
        log(_selection_message(selection))
        return {
            "fixture": True,
            "arguments": arguments,
            "selection": selection,
            "load_count": 1,
            "oom_pending": bool(arguments.simulate_oom_once and selection.device != "cpu"),
        }

    import torch
    import soundfile
    from parler_tts import ParlerTTSForConditionalGeneration
    from transformers import AutoTokenizer

    if arguments.cpu_threads:
        torch.set_num_threads(arguments.cpu_threads)
    selection = resolve_device(TorchCapabilities(torch), arguments.device, arguments.dtype, arguments.minimum_gpu_bytes)
    runtime = {
        "fixture": False,
        "arguments": arguments,
        "torch": torch,
        "soundfile": soundfile,
        "model_class": ParlerTTSForConditionalGeneration,
        "tokenizer_class": AutoTokenizer,
        "selection": selection,
        "load_count": 0,
        "oom_pending": False,
    }
    try:
        _load_model(runtime, selection)
    except Exception as exc:
        if (
            selection.device == "cpu"
            or str(arguments.device).lower() != "auto"
            or not _is_out_of_memory(torch, exc)
        ):
            raise
        reason = f"{selection.backend} out of memory while loading the model: {exc}"
        log(f"Indic Parler-TTS accelerator model load failed; reloading once on CPU. {reason}")
        _release_model(runtime)
        _load_model(
            runtime,
            DeviceSelection("cpu", "cpu", "float32", fallback_occurred=True, fallback_reason=reason),
        )
    log(_selection_message(runtime["selection"]))
    return runtime


def _load_model(runtime: dict[str, Any], selection: DeviceSelection) -> None:
    torch = runtime["torch"]
    arguments = runtime["arguments"]
    if arguments.model_id != MODEL_ID:
        raise ValueError("The Indic Parler worker was asked to load an unapproved model repository.")
    dtype = getattr(torch, selection.dtype)
    model = runtime["model_class"].from_pretrained(
        arguments.model_id,
        revision=MODEL_REVISION,
        cache_dir=arguments.cache_dir,
        torch_dtype=dtype,
        use_safetensors=True,
        # Transformers 5 changed weight tying during low-memory construction.
        # The compatibility-patched model is validated with ordinary loading.
        low_cpu_mem_usage=False,
    )
    model.to(selection.device)
    model.eval()
    prompt_tokenizer = runtime["tokenizer_class"].from_pretrained(
        arguments.model_id,
        revision=MODEL_REVISION,
        cache_dir=arguments.cache_dir,
    )
    if model.config.text_encoder._name_or_path != DESCRIPTION_MODEL_ID:
        raise ValueError("The Indic Parler model references an unapproved description encoder.")
    description_tokenizer = runtime["tokenizer_class"].from_pretrained(
        DESCRIPTION_MODEL_ID,
        revision=DESCRIPTION_MODEL_REVISION,
        cache_dir=arguments.cache_dir,
        # The pinned tokenizer-only snapshot intentionally has no config.json;
        # reuse the already verified text-encoder config from the main model.
        config=model.config.text_encoder,
    )
    runtime.update(
        model=model,
        prompt_tokenizer=prompt_tokenizer,
        description_tokenizer=description_tokenizer,
        selection=selection,
        load_count=runtime["load_count"] + 1,
    )


def _selection_message(selection: DeviceSelection) -> str:
    gpu = f", gpu={selection.gpu_name}" if selection.gpu_name else ""
    fallback = f", fallback={selection.fallback_reason}" if selection.fallback_occurred else ""
    return f"Indic Parler-TTS selected device={selection.device}, backend={selection.backend}, dtype={selection.dtype}{gpu}{fallback}"


def synthesize(runtime: dict[str, Any], request: dict[str, Any]) -> dict[str, Any]:
    text = str(request.get("text") or "").strip()
    language = str(request.get("language") or "").strip().lower()
    speaker = request.get("speaker")
    description = str(request.get("description") or "").strip()
    output_path = str(request.get("output_path") or "").strip()
    seed = int(request.get("seed", 42))
    if not text:
        raise ValueError("Text is required.")
    if not language:
        raise ValueError("Language is required.")
    if not description:
        raise ValueError("A voice description is required.")
    if not output_path:
        raise ValueError("Output path is required.")

    if speaker and str(speaker).lower() not in description.lower():
        description = f"{speaker} speaks clearly. {description}"

    if runtime["fixture"]:
        return _fixture_synthesize(runtime, output_path)

    try:
        return _model_synthesize(runtime, text, description, seed, output_path)
    except Exception as exc:
        selection = runtime["selection"]
        if not _is_out_of_memory(runtime["torch"], exc) or selection.device == "cpu":
            raise
        if str(runtime["arguments"].device).lower() != "auto":
            raise RuntimeError(
                f"Indic Parler-TTS ran out of memory on forced device {selection.device}; automatic CPU fallback is disabled."
            ) from exc

        reason = f"{selection.backend} out of memory: {exc}"
        log(f"Indic Parler-TTS accelerator generation failed; reloading once on CPU. {reason}")
        _release_model(runtime)
        cpu_selection = DeviceSelection(
            "cpu", "cpu", "float32", fallback_occurred=True, fallback_reason=reason
        )
        _load_model(runtime, cpu_selection)
        return _model_synthesize(runtime, text, description, seed, output_path)


def _model_synthesize(
    runtime: dict[str, Any], text: str, description: str, seed: int, output_path: str
) -> dict[str, Any]:
    torch = runtime["torch"]
    selection: DeviceSelection = runtime["selection"]
    if selection.device.startswith("cuda"):
        torch.cuda.reset_peak_memory_stats(0)
    torch.manual_seed(seed)
    if selection.device.startswith("cuda"):
        torch.cuda.manual_seed_all(seed)

    description_inputs = runtime["description_tokenizer"](
        description, return_tensors="pt"
    ).to(selection.device)
    prompt_inputs = runtime["prompt_tokenizer"](text, return_tensors="pt").to(selection.device)
    started = time.perf_counter()
    with torch.inference_mode():
        generation = runtime["model"].generate(
            input_ids=description_inputs.input_ids,
            attention_mask=description_inputs.attention_mask,
            prompt_input_ids=prompt_inputs.input_ids,
            prompt_attention_mask=prompt_inputs.attention_mask,
        )
    generation_seconds = time.perf_counter() - started
    audio = generation.detach().to("cpu", dtype=torch.float32).numpy().squeeze()
    sample_rate = int(runtime["model"].config.sampling_rate)
    duration_seconds = float(len(audio)) / sample_rate
    destination = Path(output_path)
    destination.parent.mkdir(parents=True, exist_ok=True)
    runtime["soundfile"].write(str(destination), audio, sample_rate, subtype="PCM_16")
    peak_memory = _peak_memory_bytes(runtime)
    return _response_payload(
        str(destination), sample_rate, duration_seconds, generation_seconds, selection, peak_memory, runtime["load_count"]
    )


def _fixture_synthesize(runtime: dict[str, Any], output_path: str) -> dict[str, Any]:
    selection: DeviceSelection = runtime["selection"]
    if runtime["oom_pending"]:
        runtime["oom_pending"] = False
        if str(runtime["arguments"].device).lower() != "auto":
            raise RuntimeError("Simulated accelerator out of memory on a forced device.")
        selection = DeviceSelection(
            "cpu",
            "cpu",
            "float32",
            fallback_occurred=True,
            fallback_reason="Simulated accelerator out of memory.",
        )
        runtime["selection"] = selection
        runtime["load_count"] += 1
    destination = Path(output_path)
    destination.parent.mkdir(parents=True, exist_ok=True)
    sample_rate = 24_000
    frames = b"\x00\x00" * 240
    with wave.open(str(destination), "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(sample_rate)
        output.writeframes(frames)
    return _response_payload(
        str(destination), sample_rate, 0.01, 0.001, selection, 64 * 1024**2, runtime["load_count"]
    )


def _response_payload(
    audio_path: str,
    sample_rate: int,
    duration_seconds: float,
    generation_seconds: float,
    selection: DeviceSelection,
    peak_memory_bytes: int | None,
    load_count: int,
) -> dict[str, Any]:
    return {
        "audio_path": audio_path,
        "sample_rate": sample_rate,
        "duration_seconds": duration_seconds,
        "selected_device": selection.device,
        "backend": selection.backend,
        "data_type": selection.dtype,
        "gpu_name": selection.gpu_name,
        "fallback_occurred": selection.fallback_occurred,
        "fallback_reason": selection.fallback_reason,
        "generation_seconds": generation_seconds,
        "real_time_factor": generation_seconds / duration_seconds if duration_seconds > 0 else 0.0,
        "peak_memory_bytes": peak_memory_bytes,
        "model_load_count": load_count,
    }


def _is_out_of_memory(torch: Any, exception: Exception) -> bool:
    oom_type = getattr(torch, "OutOfMemoryError", None)
    return bool((oom_type is not None and isinstance(exception, oom_type)) or "out of memory" in str(exception).lower())


def _release_model(runtime: dict[str, Any]) -> None:
    for key in ("model", "prompt_tokenizer", "description_tokenizer"):
        runtime.pop(key, None)
    gc.collect()
    torch = runtime["torch"]
    try:
        if torch.cuda.is_available():
            torch.cuda.empty_cache()
    except RuntimeError:
        pass
    mps = getattr(torch, "mps", None)
    try:
        if mps is not None and hasattr(mps, "empty_cache"):
            mps.empty_cache()
    except RuntimeError:
        pass


def _peak_memory_bytes(runtime: dict[str, Any]) -> int | None:
    selection: DeviceSelection = runtime["selection"]
    if selection.device.startswith("cuda"):
        try:
            return int(runtime["torch"].cuda.max_memory_allocated(0))
        except RuntimeError:
            return None
    try:
        import psutil

        memory = psutil.Process(os.getpid()).memory_info()
        peak_working_set = getattr(memory, "peak_wset", None)
        if peak_working_set is not None:
            return int(peak_working_set)
    except (ImportError, OSError, ValueError):
        pass
    try:
        import resource

        maximum_resident_set = int(resource.getrusage(resource.RUSAGE_SELF).ru_maxrss)
        # macOS reports bytes; Linux and other common Unix targets report KiB.
        return maximum_resident_set if sys.platform == "darwin" else maximum_resident_set * 1024
    except (ImportError, OSError, ValueError):
        return None


def main() -> int:
    arguments = parse_args()
    try:
        runtime = load_runtime(arguments)
    except Exception as exc:
        emit({"status": "error", "error": str(exc), "traceback": traceback.format_exc()})
        return 1

    selection: DeviceSelection = runtime["selection"]
    emit(
        {
            "status": "ready",
            "payload": {
                "selected_device": selection.device,
                "backend": selection.backend,
                "data_type": selection.dtype,
                "gpu_name": selection.gpu_name,
                "fallback_occurred": selection.fallback_occurred,
                "fallback_reason": selection.fallback_reason,
            },
        }
    )
    for raw_line in sys.stdin:
        try:
            request = json.loads(raw_line)
            emit({"status": "ok", "payload": synthesize(runtime, request)})
        except Exception as exc:
            emit({"status": "error", "error": str(exc), "traceback": traceback.format_exc()})
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

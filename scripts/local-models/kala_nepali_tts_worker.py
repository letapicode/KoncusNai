"""Persistent CPU-native Kala Nepali text-to-speech worker for Koncus Nai."""

import argparse
import json
import os
import sys
import traceback

MODEL_ID = "ampixa/real-nepali-v0.2-kala"
MODEL_REVISION = "90a66e818fbb4e19a8ba9b191da422a70e46a296"


SPEAKER_NAMES = {
    "kala": "kala",
    "barsha": "barsha",
    "slr143_f": "slr143_F",
    "slr43_0546": "slr43_0546",
    "slr43_2099": "slr43_2099",
}


def emit(payload: dict) -> None:
    print(json.dumps(payload, ensure_ascii=True), flush=True)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--cache-dir", required=True)
    parser.add_argument("--voice", default="kala")
    return parser.parse_args()


def load_runtime(arguments: argparse.Namespace):
    import huggingface_hub
    original_hf_hub_download = huggingface_hub.hf_hub_download

    def pinned_hf_hub_download(repo_id, *args, **kwargs):
        if repo_id != MODEL_ID:
            raise ValueError(f"Kala TTS requested an unapproved Hugging Face repository: {repo_id}")
        kwargs["revision"] = MODEL_REVISION
        return original_hf_hub_download(repo_id, *args, **kwargs)

    huggingface_hub.hf_hub_download = pinned_hf_hub_download
    os.environ["HF_HOME"] = arguments.cache_dir
    import kala_tts
    import numpy as np
    from kala_tts._api import _get_engine
    from kala_tts._infer import (
        G2P_PROFILE,
        SAMPLE_RATE,
        _audio_to_wav_bytes,
        _g2p,
        _phonemize_chunk,
        _silence,
        _split_chunks,
    )
    # Load the ONNX session during startup so the first Prepare click is predictable.
    kala_tts.list_speakers()
    return {
        "kala_tts": kala_tts,
        "np": np,
        "engine": _get_engine(),
        "default_voice": arguments.voice,
        "g2p": _g2p,
        "g2p_profile": G2P_PROFILE,
        "sample_rate": SAMPLE_RATE,
        "audio_to_wav_bytes": _audio_to_wav_bytes,
        "phonemize_chunk": _phonemize_chunk,
        "silence": _silence,
        "split_chunks": _split_chunks,
    }


def _run_chunk(runtime, text: str, speaker: str):
    np = runtime["np"]
    engine = runtime["engine"]
    phone_ids = runtime["phonemize_chunk"](text, engine._id_map)
    feed = {
        "input": np.array([phone_ids], dtype=np.int64),
        "input_lengths": np.array([len(phone_ids)], dtype=np.int64),
        "scales": np.array([0.333, 1.0, 0.667], dtype=np.float32),
    }
    if "sid" in engine._input_names:
        feed["sid"] = np.array([engine._speaker_map[speaker]], dtype=np.int64)

    raw_outputs = engine._session.run(None, feed)
    named_outputs = {
        output.name: value
        for output, value in zip(engine._session.get_outputs(), raw_outputs)
    }
    audio = np.asarray(named_outputs.get("output", raw_outputs[0])).reshape(-1).astype(np.float32)
    durations_value = named_outputs.get("durations")
    if durations_value is None:
        raise RuntimeError("The Nepali model did not return its native duration track.")
    durations = np.maximum(0.0, np.asarray(durations_value).reshape(-1).astype(np.float64))
    if len(durations) < len(phone_ids) or float(durations.sum()) <= 0:
        raise RuntimeError("The Nepali model returned an incomplete duration track.")
    return audio, durations[: len(phone_ids)]


def _soften_chunk_edges(runtime, audio):
    """Apply only a short click-safe edge fade; preserve the model's natural low-level transitions."""
    np = runtime["np"]
    result = np.nan_to_num(audio, nan=0.0, posinf=1.0, neginf=-1.0).astype(np.float32, copy=True)
    fade_samples = min(len(result) // 2, max(1, int(round(runtime["sample_rate"] * 0.012))))
    if fade_samples > 1:
        ramp = np.sin(np.linspace(0.0, np.pi / 2.0, fade_samples, dtype=np.float32)) ** 2
        result[:fade_samples] *= ramp
        result[-fade_samples:] *= ramp[::-1]
    return np.clip(result, -1.0, 1.0)


def _suppress_quiet_residual(runtime, audio):
    """Gently attenuate Kala's quiet vocoder residue without hard gating or changing timing."""
    np = runtime["np"]
    result = np.asarray(audio, dtype=np.float32)
    if len(result) < 256:
        return result

    sample_rate = runtime["sample_rate"]
    analysis_samples = max(32, int(round(sample_rate * 0.018)))
    smoothing_samples = max(32, int(round(sample_rate * 0.045)))
    analysis_kernel = np.full(analysis_samples, 1.0 / analysis_samples, dtype=np.float32)
    local_rms = np.sqrt(
        np.maximum(
            np.convolve(result * result, analysis_kernel, mode="same"),
            np.float32(1e-10),
        )
    )
    speech_reference = float(np.percentile(local_rms, 72.0))
    quiet_level = max(0.0022, speech_reference * 0.11)
    transition_end = max(quiet_level * 2.8, quiet_level + 1e-5)
    blend = np.clip((local_rms - quiet_level) / (transition_end - quiet_level), 0.0, 1.0)
    blend = blend * blend * (3.0 - 2.0 * blend)
    gain = 0.30 + (0.70 * blend)
    smoothing_kernel = np.full(smoothing_samples, 1.0 / smoothing_samples, dtype=np.float32)
    gain = np.convolve(gain, smoothing_kernel, mode="same")
    return (result * gain).astype(np.float32, copy=False)


def _word_timings(runtime, text: str, durations, audio_length: int, offset_seconds: float) -> list[dict]:
    engine = runtime["engine"]
    words = runtime["g2p"].phonemize_text(text, profile=runtime["g2p_profile"])
    # Duration values are decoder frames. Scaling by the rendered sample count is
    # robust to model/config changes and keeps the last boundary on the WAV itself.
    seconds_per_duration = audio_length / runtime["sample_rate"] / float(durations.sum())
    duration_cursor = 1  # Skip the beginning-of-speech token inserted by Kala.
    frame_cursor = float(durations[0])
    timings: list[dict] = []
    for word in words:
        token_count = 0
        for phone in word.phones or []:
            if phone in {".", "|"}:
                continue
            token_count += len(engine._id_map.get(phone, []))
        if token_count <= 0:
            continue
        token_end = min(len(durations), duration_cursor + token_count)
        start_frame = frame_cursor
        frame_cursor += float(durations[duration_cursor:token_end].sum())
        duration_cursor = token_end
        end_frame = max(start_frame, frame_cursor)
        timings.append(
            {
                "text": word.text,
                "start_seconds": offset_seconds + start_frame * seconds_per_duration,
                "end_seconds": offset_seconds + end_frame * seconds_per_duration,
            }
        )
    return timings


def synthesize(runtime, request: dict) -> dict:
    default_voice = runtime["default_voice"]
    text = " ".join(str(request.get("text") or "").split())
    output_path = str(request.get("output_path") or "").strip()
    voice_key = str(request.get("voice_id") or default_voice).strip().lower()
    speaker = SPEAKER_NAMES.get(voice_key)
    if not text:
        raise ValueError("Nepali text is required.")
    if not output_path:
        raise ValueError("Output path is required.")
    if speaker is None:
        raise ValueError(f"Unknown Nepali voice '{voice_key}'.")

    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    audio_parts = []
    timings: list[dict] = []
    timeline_seconds = 0.0
    for chunk in runtime["split_chunks"](text):
        audio, durations = _run_chunk(runtime, chunk.text, speaker)
        timings.extend(_word_timings(runtime, chunk.text, durations, len(audio), timeline_seconds))
        # kala-tts' frame gate modified roughly a quarter of Kala's samples and
        # could audibly chatter in low-energy inter-word transitions. The model
        # already emits clean audio, so retain its continuous sentence waveform.
        if speaker == "kala":
            audio = _suppress_quiet_residual(runtime, audio)
        audio_parts.append(_soften_chunk_edges(runtime, audio))
        timeline_seconds += len(audio) / runtime["sample_rate"]
        if chunk.pause_s > 0:
            silence = runtime["silence"](chunk.pause_s)
            audio_parts.append(silence)
            timeline_seconds += len(silence) / runtime["sample_rate"]

    np = runtime["np"]
    combined = np.concatenate(audio_parts) if audio_parts else np.array([], dtype=np.float32)
    with open(output_path, "wb") as output_file:
        output_file.write(runtime["audio_to_wav_bytes"](combined))
    return {
        "audio_path": output_path,
        "duration_seconds": len(combined) / runtime["sample_rate"],
        "word_timings": timings,
    }


def main() -> int:
    arguments = parse_args()
    try:
        runtime = load_runtime(arguments)
    except Exception as exc:
        emit({"status": "error", "error": str(exc), "traceback": traceback.format_exc()})
        return 1

    emit({"status": "ready"})
    for raw_line in sys.stdin:
        try:
            request = json.loads(raw_line)
            payload = synthesize(runtime, request)
            emit({"status": "ok", "payload": payload})
        except Exception as exc:
            emit({"status": "error", "error": str(exc), "traceback": traceback.format_exc()})
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

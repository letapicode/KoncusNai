"""Persistent local Kokoro-82M text-to-speech worker for Koncus Nai."""

import argparse
import json
import os
import re
import sys
import traceback
import unicodedata

MODEL_ID = "hexgrad/Kokoro-82M"
MODEL_REVISION = "f3ff3571791e39611d31c381e3a41a3af07b4987"


def emit(payload: dict) -> None:
    print(json.dumps(payload, ensure_ascii=True), flush=True)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-id", default="hexgrad/Kokoro-82M")
    parser.add_argument("--cache-dir", required=True)
    parser.add_argument("--voice", default="af_heart")
    return parser.parse_args()


def load_runtime(arguments: argparse.Namespace):
    if arguments.model_id != MODEL_ID:
        raise ValueError("The Kokoro worker was asked to load an unapproved model repository.")
    # KPipeline resolves model and voice assets through huggingface_hub. Set this before import so
    # all downloaded files stay in Koncus Nai's provider-specific local cache.
    os.environ["HF_HOME"] = arguments.cache_dir
    import numpy as np
    import soundfile as sf
    import huggingface_hub
    original_hf_hub_download = huggingface_hub.hf_hub_download

    def pinned_hf_hub_download(repo_id, *args, **kwargs):
        if repo_id != MODEL_ID:
            raise ValueError(f"Kokoro requested an unapproved Hugging Face repository: {repo_id}")
        kwargs["revision"] = MODEL_REVISION
        return original_hf_hub_download(repo_id, *args, **kwargs)

    huggingface_hub.hf_hub_download = pinned_hf_hub_download
    from kokoro import KPipeline

    pipelines = {"a": KPipeline(lang_code="a", repo_id=arguments.model_id)}
    # Loading the default voice during startup prevents the first button click from paying a second
    # download/load penalty after the model itself is ready.
    pipelines["a"].load_voice(arguments.voice)
    return pipelines, arguments.model_id, arguments.voice, np, sf


LANGUAGE_SPECS = {
    "en": ("a", "af_heart"),
    "en-us": ("a", "af_heart"),
    "en-gb": ("b", "bf_emma"),
    "ja": ("j", "jf_alpha"),
    "zh": ("z", "zf_xiaobei"),
    "zh-cn": ("z", "zf_xiaobei"),
    "es": ("e", "ef_dora"),
    "fr": ("f", "ff_siwis"),
    "hi": ("h", "hf_alpha"),
    "it": ("i", "if_sara"),
    "pt-br": ("p", "pf_dora"),
}


def resolve_language(language: str) -> tuple[str, str]:
    normalized = str(language or "en").strip().lower()
    if normalized in LANGUAGE_SPECS:
        return LANGUAGE_SPECS[normalized]
    supported = ", ".join(sorted({key for key in LANGUAGE_SPECS if "-" not in key or key == "pt-br"}))
    raise ValueError(f"Kokoro does not support '{normalized}' in Koncus Nai. Choose one of: {supported}.")


def split_for_model_budget(text: str, maximum_characters: int) -> list[str]:
    """Keep every Kokoro call below its silent token truncation boundary."""
    normalized = " ".join(str(text or "").split())
    if not normalized:
        return []

    sentences = re.split(r"(?<=[.!?…。！？।॥])\s+", normalized)
    chunks: list[str] = []
    pending = ""

    def flush() -> None:
        nonlocal pending
        if pending:
            chunks.append(pending)
            pending = ""

    for sentence in sentences:
        sentence = sentence.strip()
        if not sentence:
            continue
        pieces = [sentence]
        if len(sentence) > maximum_characters:
            pieces = []
            current = ""
            for word in sentence.split():
                if current and len(current) + 1 + len(word) > maximum_characters:
                    pieces.append(current)
                    current = ""
                current = word if not current else f"{current} {word}"
            if current:
                pieces.append(current)

        for piece in pieces:
            if pending and len(pending) + 1 + len(piece) > maximum_characters:
                flush()
            pending = piece if not pending else f"{pending} {piece}"

    flush()
    return chunks


def split_hindi_for_phoneme_budget(pipeline, text: str, maximum_phonemes: int = 480) -> list[tuple[str, str]]:
    """Prepare complete Hindi sentences using Kokoro's real 510-phoneme limit.

    Character limits are unreliable for Devanagari because one displayed grapheme can expand to
    several phonemes. Keeping each sentence independent gives the model an unambiguous prosodic
    arc; controlled joins below then avoid doubled silence between sentences.
    """
    normalized = unicodedata.normalize("NFC", " ".join(str(text or "").split()))
    normalized = re.sub(r"([।॥])(?=\S)", r"\1 ", normalized)
    if not normalized:
        return []

    sentences = [part.strip() for part in re.split(r"(?<=[.!?…。！？।॥])\s+", normalized) if part.strip()]

    def phonemize(value: str) -> str:
        phonemes, _ = pipeline.g2p(value)
        return phonemes

    def split_oversized_sentence(sentence: str) -> list[tuple[str, str]]:
        pieces: list[tuple[str, str]] = []
        pending_words: list[str] = []
        pending_phonemes = ""
        for word in sentence.split():
            candidate_words = [*pending_words, word]
            candidate_text = " ".join(candidate_words)
            candidate_phonemes = phonemize(candidate_text)
            if pending_words and len(candidate_phonemes) > maximum_phonemes:
                pieces.append((" ".join(pending_words), pending_phonemes))
                pending_words = [word]
                pending_phonemes = phonemize(word)
            else:
                pending_words = candidate_words
                pending_phonemes = candidate_phonemes

        if pending_words:
            if len(pending_phonemes) > maximum_phonemes:
                raise ValueError("A Hindi word exceeds Kokoro's phoneme budget and cannot be narrated safely.")
            pieces.append((" ".join(pending_words), pending_phonemes))
        return pieces

    units: list[tuple[str, str]] = []
    for sentence in sentences:
        sentence_phonemes = phonemize(sentence)
        if len(sentence_phonemes) <= maximum_phonemes:
            units.append((sentence, sentence_phonemes))
        else:
            units.extend(split_oversized_sentence(sentence))

    return units


def _ends_sentence(text: str) -> bool:
    return bool(re.search(r"[.!?…。！？।॥][\"'’”)]*$", text.rstrip()))


def _trim_audio_edges(audio, np, sample_rate: int = 24000):
    """Keep a small natural breath while removing duplicated model-edge silence."""
    threshold = 0.0025
    padding = int(sample_rate * 0.035)
    active = np.flatnonzero(np.abs(audio) > threshold)
    if active.size == 0:
        return audio
    start = max(0, int(active[0]) - padding)
    end = min(len(audio), int(active[-1]) + padding + 1)
    return audio[start:end]


def _join_pause_seconds(previous_text: str) -> float:
    stripped = previous_text.rstrip()
    if not _ends_sentence(stripped):
        return 0.025
    if re.search(r"[?？][\"'’”)]*$", stripped):
        return 0.20
    if re.search(r"[!！][\"'’”)]*$", stripped):
        return 0.17
    if re.search(r"॥[\"'’”)]*$", stripped):
        return 0.18
    return 0.14


def _append_result_timing(result, audio_offset_seconds: float, word_timings: list[dict]) -> None:
    """Copy Kokoro's model-native token timestamps into the worker response."""
    for token in result.tokens or []:
        start = getattr(token, "start_ts", None)
        end = getattr(token, "end_ts", None)
        token_text = str(getattr(token, "text", "") or "").strip()
        if token_text and start is not None and end is not None and end >= start:
            word_timings.append(
                {
                    "text": token_text,
                    "start_seconds": audio_offset_seconds + float(start),
                    "end_seconds": audio_offset_seconds + float(end),
                }
            )


def synthesize(runtime, request: dict) -> dict:
    pipelines, model_id, default_voice, np, soundfile = runtime
    text = str(request.get("text") or "").strip()
    output_path = str(request.get("output_path") or "").strip()
    if not text:
        raise ValueError("Text is required.")
    if not output_path:
        raise ValueError("Output path is required.")

    language, language_default_voice = resolve_language(request.get("language"))
    voice = str(request.get("voice_id") or language_default_voice or default_voice).strip().lower()
    if not voice:
        voice = default_voice
    if language not in pipelines:
        from kokoro import KPipeline
        pipelines[language] = KPipeline(lang_code=language, repo_id=model_id)
    pipeline = pipelines[language]
    pipeline.load_voice(voice)
    word_timings: list[dict] = []
    if language == "h":
        hindi_chunks = split_hindi_for_phoneme_budget(pipeline, text)
        generated_chunks = []
        for chunk_index, (chunk_text, chunk_phonemes) in enumerate(hindi_chunks):
            generated = [result.audio for result in pipeline.generate_from_tokens(chunk_phonemes, voice=voice, speed=1.0)]
            if not generated:
                raise RuntimeError("Kokoro returned no audio for part of the requested Hindi text.")
            audio = _trim_audio_edges(
                np.concatenate([np.asarray(part, dtype=np.float32) for part in generated]),
                np,
            )
            if chunk_index > 0:
                previous_text = hindi_chunks[chunk_index - 1][0]
                pause_samples = int(24000 * _join_pause_seconds(previous_text))
                generated_chunks.append(np.zeros(pause_samples, dtype=np.float32))
            generated_chunks.append(audio)
        chunks = generated_chunks
    else:
        request_chunks = split_for_model_budget(text, 500)
        chunks = []
        audio_offset_seconds = 0.0
        for request_chunk in request_chunks:
            generated = list(pipeline(request_chunk, voice=voice, speed=1.0, split_pattern=r"\n+"))
            if not generated:
                raise RuntimeError("Kokoro returned no audio for part of the requested text.")
            for result in generated:
                audio = np.asarray(result.audio, dtype=np.float32)
                _append_result_timing(result, audio_offset_seconds, word_timings)
                chunks.append(audio)
                audio_offset_seconds += len(audio) / 24000.0

    if not chunks:
        raise RuntimeError("Kokoro returned no audio.")

    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    combined_audio = np.concatenate(chunks)
    soundfile.write(output_path, combined_audio, 24000)
    return {
        "audio_path": output_path,
        "duration_seconds": len(combined_audio) / 24000.0,
        "word_timings": word_timings,
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

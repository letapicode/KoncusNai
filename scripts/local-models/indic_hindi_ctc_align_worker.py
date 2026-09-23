"""Exact-text Hindi CTC forced alignment worker for Reading Studio.

The reader already owns the text it asks Kokoro to speak.  A Hindi CTC acoustic
model scores that known text directly against the waveform, instead of asking a
general ASR model to guess a new transcript and then interpolating mismatches.
"""

import argparse
import json
import re
import sys
import time
import traceback
import unicodedata
from pathlib import Path

MODEL_ID = "Harveenchadha/vakyansh-wav2vec2-hindi-him-4200"
MODEL_REVISION = "e2568c3f7868d8aa3aaabcf28fa100d10d54c170"


def emit(payload: dict) -> None:
    print(json.dumps(payload, ensure_ascii=True), flush=True)


def reader_words(transcript: str) -> tuple[str, list[str]]:
    # Keep every letter, combining mark, and digit. Treat punctuation as a word
    # boundary, because most Hindi CTC vocabularies do not model punctuation.
    clean = []
    for character in unicodedata.normalize("NFC", transcript or ""):
        category = unicodedata.category(character)
        clean.append(character if character.isalnum() or category.startswith("M") else " ")
    alignment_text = " ".join("".join(clean).split())
    words = alignment_text.split()
    if not words:
        raise ValueError("Hindi alignment needs Hindi letters or numbers to align.")
    return alignment_text, words


def ctc_path(torch, emissions, token_ids, blank_id: int):
    """Find the highest-scoring legal CTC path for every expected token."""
    frame_count, token_count = emissions.shape[0], len(token_ids)
    tokens = torch.tensor(token_ids, dtype=torch.long, device=emissions.device)
    negative_infinity = torch.tensor(float("-inf"), device=emissions.device)
    trellis = emissions.new_full((frame_count + 1, token_count + 1), float("-inf"))
    # Leading silence is free; all expected text still has to be consumed.
    trellis[:, 0] = 0

    for frame in range(frame_count):
        stay = trellis[frame, 1:] + emissions[frame, blank_id]
        advance = trellis[frame, :-1] + emissions[frame, tokens]
        trellis[frame + 1, 1:] = torch.maximum(stay, advance)

    frame = int(torch.argmax(trellis[:, token_count]).item())
    if not torch.isfinite(trellis[frame, token_count]):
        raise RuntimeError("The Hindi acoustic model could not find a complete alignment path.")

    token_index = token_count
    path = []
    while token_index > 0:
        if frame <= 0:
            raise RuntimeError("The Hindi alignment path ended before the complete text was aligned.")
        stay = trellis[frame - 1, token_index] + emissions[frame - 1, blank_id]
        advance = trellis[frame - 1, token_index - 1] + emissions[frame - 1, tokens[token_index - 1]]
        if advance > stay:
            path.append((token_index - 1, frame - 1))
            token_index -= 1
        frame -= 1

    return list(reversed(path))


def expand_repeated_tokens(token_ids, blank_id: int):
    """Make CTC's required blank between equal labels explicit.

    The first implementation blocked a transition between identical adjacent
    labels. That made such a sequence impossible forever: it did not keep a
    distinct state for the intervening blank. Hindi naturally contains repeated
    graphemes in normal prose, so a full section could fail while a short demo
    happened to work. Representing that blank as a real target state fixes the
    CTC automaton without guessing timestamps.
    """
    expanded = []
    original_indexes = []
    previous = None
    for index, token_id in enumerate(token_ids):
        if previous is not None and token_id == previous:
            expanded.append(blank_id)
            original_indexes.append(None)
        expanded.append(token_id)
        original_indexes.append(index)
        previous = token_id
    return expanded, original_indexes


def word_times(tokenizer, token_ids, path, words, duration_seconds: float, frame_count: int):
    token_text = tokenizer.convert_ids_to_tokens(token_ids)
    separator = getattr(tokenizer, "word_delimiter_token", "|") or "|"
    starts: list[int | None] = [None] * len(words)
    ends: list[int | None] = [None] * len(words)
    word_index = 0

    for token_index, frame in path:
        token = str(token_text[token_index])
        if token == separator:
            word_index += 1
            continue
        if word_index >= len(words):
            raise RuntimeError("Hindi tokenization did not preserve the expected word boundaries.")
        starts[word_index] = frame if starts[word_index] is None else starts[word_index]
        ends[word_index] = frame

    if any(start is None or end is None for start, end in zip(starts, ends)):
        raise RuntimeError("Hindi tokenization did not produce an acoustic path for every displayed word.")

    frame_duration = duration_seconds / max(frame_count, 1)
    aligned = []
    for index, word in enumerate(words):
        start_frame = starts[index] or 0
        next_start = starts[index + 1] if index + 1 < len(starts) else None
        end_frame = next_start if next_start is not None else (ends[index] or start_frame) + 1
        aligned.append(
            {
                "text": word,
                "start_seconds": start_frame * frame_duration,
                "end_seconds": max((start_frame + 1) * frame_duration, end_frame * frame_duration),
            }
        )
    return aligned


def resolve_ctc_blank_id(torch, model, tokenizer, emissions) -> int:
    """Handle older Hindi checkpoints that use BOS as their CTC blank label."""
    pad_id = getattr(tokenizer, "pad_token_id", None)
    bos_id = getattr(model.config, "bos_token_id", None)
    if pad_id is None:
        if bos_id is None:
            raise RuntimeError("The Hindi CTC model does not define a blank token.")
        return int(bos_id)
    if bos_id is None or int(bos_id) == int(pad_id):
        return int(pad_id)

    greedy = torch.argmax(emissions, dim=-1)
    pad_count = int((greedy == int(pad_id)).sum().item())
    bos_count = int((greedy == int(bos_id)).sum().item())
    # Vakyansh's Hindi checkpoint labels acoustic blanks as <s>, despite its
    # declared pad token. Use evidence from the waveform, not a brittle model-
    # name special case.
    return int(bos_id) if bos_count > pad_count else int(pad_id)


def load_model(arguments):
    if arguments.model_id != MODEL_ID:
        raise ValueError("The Hindi alignment worker was asked to load an unapproved model repository.")
    import torch
    from transformers import AutoModelForCTC, Wav2Vec2Processor

    model_dir = Path(arguments.model_dir)
    source = str(model_dir) if (model_dir / "config.json").is_file() else arguments.model_id
    load_kwargs = (
        {
            "cache_dir": str(model_dir.parent),
            "revision": MODEL_REVISION,
        }
        if source == arguments.model_id
        else {}
    )
    # Some Hindi checkpoints publish a ProcessorWithLM.  We need the raw CTC
    # tokenizer and feature extractor for forced alignment, not an optional
    # language-model decoder, so deliberately load the plain processor.
    processor = Wav2Vec2Processor.from_pretrained(source, **load_kwargs)
    model = AutoModelForCTC.from_pretrained(source, **load_kwargs)
    if source == arguments.model_id:
        model_dir.mkdir(parents=True, exist_ok=True)
        processor.save_pretrained(model_dir)
        model.save_pretrained(model_dir)

    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    model.to(device)
    model.eval()
    return torch, processor, model, device


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-dir", required=True)
    parser.add_argument("--model-id", required=True)
    arguments = parser.parse_args()

    try:
        torch, processor, model, device = load_model(arguments)
        emit({"status": "ready", "payload": {"device": str(device), "model": arguments.model_id}})
    except Exception as exc:  # pragma: no cover - surfaced to the app
        emit({"status": "error", "error": str(exc), "traceback": traceback.format_exc()})
        return 1

    for raw_line in sys.stdin:
        if not raw_line.strip():
            continue
        try:
            request = json.loads(raw_line)
            audio_path = str(request.get("audio_path") or request.get("audioPath") or "").strip()
            transcript = str(request.get("transcript") or request.get("Transcript") or "").strip()
            if not audio_path or not transcript:
                raise ValueError("audio_path and transcript are required.")

            import librosa

            started = time.perf_counter()
            audio, sample_rate = librosa.load(audio_path, sr=16000, mono=True)
            alignment_text, words = reader_words(transcript)
            encoded = processor.tokenizer(alignment_text, add_special_tokens=False)
            token_ids = list(encoded.input_ids)
            if not token_ids:
                raise RuntimeError("Hindi tokenizer produced no alignment tokens.")
            unknown_id = processor.tokenizer.unk_token_id
            if unknown_id is not None and unknown_id in token_ids:
                raise RuntimeError("The Hindi alignment model cannot represent one or more characters in this text.")

            inputs = processor(audio, sampling_rate=sample_rate, return_tensors="pt", padding=True)
            input_values = inputs.input_values.to(device)
            with torch.inference_mode():
                emissions = torch.log_softmax(model(input_values).logits[0], dim=-1)
            blank_id = resolve_ctc_blank_id(torch, model, processor.tokenizer, emissions)
            expanded_token_ids, original_indexes = expand_repeated_tokens(token_ids, blank_id)
            expanded_path = ctc_path(torch, emissions, expanded_token_ids, blank_id)
            path = [
                (original_indexes[expanded_index], frame)
                for expanded_index, frame in expanded_path
                if original_indexes[expanded_index] is not None
            ]
            aligned = word_times(
                processor.tokenizer,
                token_ids,
                path,
                words,
                len(audio) / float(sample_rate),
                emissions.shape[0],
            )
            emit({
                "status": "ok",
                "payload": {
                    "words": aligned,
                    "duration_ms": (time.perf_counter() - started) * 1000,
                },
            })
        except Exception as exc:  # pragma: no cover - surfaced to the app
            emit({"status": "error", "error": str(exc), "traceback": traceback.format_exc()})

    return 0


if __name__ == "__main__":
    raise SystemExit(main())

import argparse
import difflib
import json
import time
import traceback
import unicodedata


def emit(payload: dict) -> None:
    print(json.dumps(payload, ensure_ascii=True), flush=True)


def get_value(payload: dict, *names: str, default=None):
    for name in names:
        if name in payload:
            return payload[name]
    return default


def normalize_alignment_word(value: str) -> str:
    normalized = []
    for character in unicodedata.normalize("NFKC", str(value or "")):
        category = unicodedata.category(character)
        if character.isalnum() or category.startswith("M"):
            normalized.append(character.casefold())
    return "".join(normalized)


def direct_coverage(reference_words, hypothesis_words) -> float:
    reference = [normalize_alignment_word(word) for word in reference_words]
    hypothesis = [normalize_alignment_word(word) for word in hypothesis_words]
    reference = [word for word in reference if word]
    hypothesis = [word for word in hypothesis if word]
    if not reference:
        return 1.0
    matcher = difflib.SequenceMatcher(a=reference, b=hypothesis, autojunk=False)
    direct = sum(size for _, _, size in matcher.get_matching_blocks())
    return direct / len(reference)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-dir", required=True)
    args = parser.parse_args()

    try:
        import torch
        from crisperwhisper import CrisperWhisperModel

        device = "cuda" if torch.cuda.is_available() else "cpu"
        compute_type = "float16" if device == "cuda" else "float32"
        # The pure Transformers backend works on Windows without the Linux-only
        # CTranslate2 fork and still provides intended mode plus long-form audio.
        model = CrisperWhisperModel(
            args.model_dir,
            backend="transformers",
            device=device,
            compute_type=compute_type,
        )
        emit({"status": "ready", "payload": {"device": device}})
    except Exception as exc:  # pragma: no cover - surfaced to parent process
        emit({"status": "error", "error": str(exc), "traceback": traceback.format_exc()})
        return 1

    for raw_line in __import__("sys").stdin:
        line = raw_line.strip()
        if not line:
            continue

        try:
            request = json.loads(line)
            audio_path = get_value(request, "audio_path", "audioPath", "AudioPath")
            transcript = get_value(request, "transcript", "Transcript")
            language = str(get_value(request, "language", "Language", default="en") or "en").lower()
            mode = str(get_value(request, "mode", "Mode", default="intended") or "intended").lower()
            if not audio_path:
                raise ValueError("audio_path is required.")
            if mode not in ("intended", "verbatim"):
                raise ValueError("mode must be 'intended' or 'verbatim'.")

            started = time.perf_counter()
            if transcript and str(transcript).strip():
                if language == "ne":
                    # CrisperWhisper's forced-align helper can interpolate an
                    # unrecognized Nepali utterance across its 30-second decoder
                    # window. For Nepali, use the model's actual acoustic word
                    # timestamps and retain a strict transcript-coverage gate.
                    hypothesis = model.transcribe(
                        str(audio_path),
                        language=language,
                        mode="verbatim",
                        longform_strategy="continuation",
                        hallucination_mitigation=False,
                        word_timestamps=True,
                    )
                    hypothesis_words = list(hypothesis.words or [])
                    emit({
                        "status": "ok",
                        "payload": {
                            "words": [
                                {
                                    "text": str(word.word),
                                    "start_seconds": float(word.start),
                                    "end_seconds": float(word.end),
                                }
                                for word in hypothesis_words
                            ],
                            "direct_coverage": direct_coverage(
                                str(transcript).split(),
                                [str(word.word) for word in hypothesis_words],
                            ),
                            "duration_ms": (time.perf_counter() - started) * 1000,
                        },
                    })
                    continue

                # The reader already knows the exact words it asked Kokoro to say.
                # A second full transcription pass used to double CPU work before
                # forced alignment and could exceed ten minutes. Align the known
                # transcript directly: this is the intended algorithm for generated
                # narration and returns the same audio-grounded word boundaries.
                result = model.forced_align(
                    str(audio_path),
                    str(transcript).strip(),
                    language=language,
                    mode="verbatim",
                    longform_strategy="continuation",
                    hallucination_mitigation=False,
                )
                emit({
                    "status": "ok",
                    "payload": {
                        "words": [
                            {
                                "text": str(word.word),
                                "start_seconds": float(word.start),
                                "end_seconds": float(word.end),
                            }
                            for word in (result.words or [])
                        ],
                        "duration_ms": (time.perf_counter() - started) * 1000,
                    },
                })
            else:
                result = model.transcribe(
                    str(audio_path),
                    language=language,
                    mode=mode,
                    longform_strategy="continuation",
                    # CrisperWhisper 2.0.2 imports the Linux-only CTranslate2
                    # package inside this optional feature even on its pure
                    # Transformers backend. Keep the Windows runtime functional.
                    hallucination_mitigation=False,
                    # Leave room for CrisperWhisper's nine-token control prompt;
                    # Whisper's decoder cannot exceed 448 total positions.
                    max_new_tokens=384,
                )
                emit({
                    "status": "ok",
                    "payload": {
                        "text": result.text.strip(),
                        "duration_ms": (time.perf_counter() - started) * 1000,
                    },
                })
        except Exception as exc:  # pragma: no cover - surfaced to parent process
            emit({"status": "error", "error": str(exc), "traceback": traceback.format_exc()})

    return 0


if __name__ == "__main__":
    raise SystemExit(main())

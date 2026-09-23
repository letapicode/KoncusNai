#!/usr/bin/env python3
"""Persistent, offline RapidOCR worker for Koncus Nai document imports."""

from __future__ import annotations

import json
import sys
import traceback
from contextlib import redirect_stdout
from pathlib import Path

from rapidocr import EngineType, LangDet, LangRec, ModelType, OCRVersion, RapidOCR


_engines: dict[str, RapidOCR] = {}


def _recognition_profile(language: str) -> tuple[LangRec, OCRVersion, ModelType]:
    base = (language or "en").lower().split("-", 1)[0]
    if base in {"hi", "ne"}:
        return LangRec.DEVANAGARI, OCRVersion.PPOCRV5, ModelType.MOBILE
    if base == "zh":
        return LangRec.CH, OCRVersion.PPOCRV6, ModelType.SMALL
    if base == "ja":
        return LangRec.JAPAN, OCRVersion.PPOCRV4, ModelType.MOBILE
    if base == "ko":
        return LangRec.KOREAN, OCRVersion.PPOCRV4, ModelType.MOBILE
    if base in {"es", "fr", "de", "it", "pt"}:
        return LangRec.LATIN, OCRVersion.PPOCRV4, ModelType.MOBILE
    return LangRec.EN, OCRVersion.PPOCRV4, ModelType.MOBILE


def _get_engine(language: str) -> RapidOCR:
    rec_language, rec_version, rec_model = _recognition_profile(language)
    key = f"{rec_language.value}:{rec_version.value}:{rec_model.value}"
    if key not in _engines:
        _engines[key] = RapidOCR(
            params={
                "Det.engine_type": EngineType.ONNXRUNTIME,
                # PP-OCRv6 detection is language agnostic, but RapidOCR's
                # model resolver registers the shared small detector as `ch`.
                "Det.lang_type": LangDet.CH,
                "Det.model_type": ModelType.SMALL,
                "Det.ocr_version": OCRVersion.PPOCRV6,
                "Rec.engine_type": EngineType.ONNXRUNTIME,
                "Rec.lang_type": rec_language,
                "Rec.model_type": rec_model,
                "Rec.ocr_version": rec_version,
            }
        )
    return _engines[key]


def _handle(payload: dict) -> dict:
    image_path = Path(str(payload.get("image_path", ""))).resolve()
    if not image_path.is_file():
        raise FileNotFoundError("The rendered document page could not be found.")

    # RapidOCR emits model/download diagnostics to stdout. The persistent
    # worker protocol reserves stdout for one JSON envelope per request, so
    # route all library diagnostics to stderr.
    with redirect_stdout(sys.stderr):
        result = _get_engine(str(payload.get("language", "en")))(str(image_path))
    texts = list(result.txts or [])
    scores = list(result.scores or [])
    lines = [
        {"text": str(text), "confidence": float(scores[index]) if index < len(scores) else 0.0}
        for index, text in enumerate(texts)
        if str(text).strip()
    ]
    return {"lines": lines}


print(json.dumps({"status": "ready", "payload": {"provider": "rapidocr-local"}}), flush=True)
for line in sys.stdin:
    try:
        request = json.loads(line)
        print(json.dumps({"status": "ok", "payload": _handle(request)}, ensure_ascii=False), flush=True)
    except Exception as exc:  # The C# host converts this into concise user-facing text.
        print(
            json.dumps(
                {"status": "error", "error": str(exc), "traceback": traceback.format_exc()},
                ensure_ascii=False,
            ),
            flush=True,
        )

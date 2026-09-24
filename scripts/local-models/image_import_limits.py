"""Cheap image preflight before decoding or model initialization for OCR."""

from pathlib import Path

from PIL import Image


MAX_IMAGE_BYTES = 64 * 1024 * 1024
MAX_IMAGE_PIXELS = 40_000_000
Image.MAX_IMAGE_PIXELS = MAX_IMAGE_PIXELS


def check_image(path: Path) -> None:
    if path.stat().st_size > MAX_IMAGE_BYTES:
        raise ValueError("The image exceeds the 64 MiB import limit.")
    # Reading image.size parses only headers; RapidOCR decodes after this check.
    with Image.open(path) as image:
        width, height = image.size
        if width <= 0 or height <= 0 or width * height > MAX_IMAGE_PIXELS:
            raise ValueError("The image exceeds the 40-megapixel OCR limit.")

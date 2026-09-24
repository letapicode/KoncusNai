"""Small hostile image headers; no image raster is expanded in these tests."""

import tempfile
import unittest
import zlib
from pathlib import Path

from PIL import Image

from image_import_limits import MAX_IMAGE_BYTES, check_image


class ImageImportLimitsTests(unittest.TestCase):
    def test_normal_small_image_passes(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "normal.png"
            Image.new("RGB", (8, 8), "white").save(path)
            check_image(path)

    def test_sparse_oversized_file_rejected_before_header_read(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "oversized.png"
            with path.open("wb") as output:
                output.truncate(MAX_IMAGE_BYTES + 1)
            with self.assertRaisesRegex(ValueError, "64 MiB"):
                check_image(path)

    def test_tiny_file_with_hostile_pixel_dimensions_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "hostile.png"
            Image.new("RGB", (1, 1), "white").save(path)
            data = bytearray(path.read_bytes())
            data[16:20] = (100_000).to_bytes(4, "big")
            data[20:24] = (100_000).to_bytes(4, "big")
            data[29:33] = (zlib.crc32(data[12:29]) & 0xFFFFFFFF).to_bytes(4, "big")
            path.write_bytes(data)
            with self.assertRaises((ValueError, Image.DecompressionBombError)):
                check_image(path)


if __name__ == "__main__":
    unittest.main()

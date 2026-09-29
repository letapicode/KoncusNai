"""Optional, explicitly provisioned transcribe.cpp backend. No downloads at runtime."""
import array
import hashlib
import json
import os
import sys
import time
import wave
from pathlib import Path

VERSION = "0.2.4"
HEADER_HASH = "7df72bf9e667b8c2"
COMMIT = "4807edaf210d0d7e8a6f7fb2a44b65966a2797f0"


class NativeInputOutsideValidation(RuntimeError):
    pass


def file_hash(path):
    with open(path, "rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


class NativeRuntime:
    def __init__(self, manifest_path, model_dir):
        manifest = json.loads(Path(manifest_path).read_text(encoding="utf-8-sig"))
        if (manifest["version"], manifest["commit"], manifest["header_hash"], manifest["precision"]) != (VERSION, COMMIT, HEADER_HASH, "Q8_0"):
            raise ValueError("Native runtime contract or precision is not supported.")
        # Bind the conversion to the configured checkpoint, never a different downloaded model.
        if file_hash(Path(model_dir) / "model.safetensors") != manifest["source_sha256"]:
            raise ValueError("Native conversion does not match the configured model weights.")
        if file_hash(manifest["model_path"]) != manifest["model_sha256"]:
            raise ValueError("Native model integrity check failed.")
        for path, expected in manifest["runtime_hashes"].items():
            if file_hash(path) != expected:
                raise ValueError("Native runtime integrity check failed.")
        sys.path.insert(0, manifest["bindings_path"])
        os.environ["TRANSCRIBE_LIBRARY"] = manifest["library_path"]
        import transcribe_cpp as tc
        from transcribe_cpp import _generated
        if tc.native_version() != VERSION or _generated.PUBLIC_HEADER_HASH != HEADER_HASH:
            raise ValueError("Native binary and bindings do not match the pinned ABI.")
        backend = manifest["backend"]
        if backend not in ("cpu", "vulkan"):
            raise ValueError("Native backend must be explicitly cpu or vulkan.")
        threads = manifest["threads"]
        if type(threads) is not int or not 1 <= threads <= (os.cpu_count() or 1):
            raise ValueError("Native thread count is invalid.")
        # Explicit device selection prevents a Vulkan request silently executing on CPU.
        device = next((d for d in tc.backends() if d.kind == backend), None)
        if device is None:
            raise ValueError("Requested native compute device is unavailable.")
        model_load_started = time.perf_counter()
        self.model = tc.Model(manifest["model_path"], backend=backend, device=device)
        try:
            if self.model.arch != "cohere_asr" or self.model.variant != "cohere-transcribe-03-2026":
                raise ValueError("Native file is not the configured Cohere model variant.")
            self.session = self.model.session(n_threads=threads)
        except Exception:
            self.model.close()
            raise
        self.metadata = {"backend": "transcribe.cpp/" + self.model.backend,
                         "dtype": "Q8_0 (mixed F16/F32)", "native_version": VERSION,
                         "native_header_hash": HEADER_HASH, "threads": threads,
                         "model_load_ms": (time.perf_counter() - model_load_started) * 1000.0,
                         "device": self.model.device.description}

    @staticmethod
    def supports(language, punctuation):
        # Cohere in 0.2.4 emits PNC by default but has no PNC-disable control.
        return punctuation and language == "en"  # Other languages retain the validated existing provider.

    def transcribe(self, audio_path, language, punctuation):
        if not self.supports(language, punctuation):
            raise ValueError("Configured language/punctuation requires Transformers.")
        with wave.open(str(audio_path), "rb") as audio:
            if (audio.getnchannels(), audio.getsampwidth(), audio.getframerate()) != (1, 2, 16000):
                raise ValueError("Native input requires PCM16 mono 16 kHz.")
            if audio.getnframes() / 16000.0 > 45.0:
                # 136 s repeated speech lost words despite EOS. Keep Transformers' long-form
                # processor/reassembly; independent shorter native calls are not a safe substitute.
                raise NativeInputOutsideValidation("Native Cohere is validated only up to 45 seconds.")
            samples = array.array("h", audio.readframes(audio.getnframes()))
        pcm = array.array("f", (sample / 32768.0 for sample in samples))
        # One sequential stdin loop owns one session/model. Never overlap compute.
        # Binding raises OutputTruncated/InputTooLong; no partial output is returned as success.
        result = self.session.run(pcm, language=language)
        return result.text.strip(), len(pcm) / 16000.0

    def warmup(self, language, punctuation):
        self.session.run(array.array("f", [0.0]) * 48000, language=language)

    def close(self):
        self.session.close()
        self.model.close()

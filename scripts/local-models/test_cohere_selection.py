"""Simulated hardware/persistence/integrity cases; these are not physical device evidence."""
import contextlib
import json
import os
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

import cohere_runtime_selection as selection
import cohere_native_runtime as native
import prepare_cohere_runtime as prepare


def hardware():
    return {"machine": "AMD64", "os": "simulated-windows", "cpu": {"physical": 12, "logical": 16, "affinity": 16},
            "drivers": {"id": "simulated", "version": "1"}, "total_memory": 16 * selection.GIB,
            "available_memory": 8 * selection.GIB, "available_commit": 12 * selection.GIB}


def candidate(backend, latency, tail=None, eligible=True, quality=True):
    return {"backend": backend, "threads": 4, "device_key": backend + ":simulated",
            "eligible": eligible, "quality_pass": quality, "score_ms": latency,
            "fixtures": {"short": {"p95_ms": tail or latency}, "medium": {"p95_ms": tail or latency}}}


class SelectionTests(unittest.TestCase):
    def test_gpu_presence_does_not_outweigh_measured_tail_regression(self):
        baseline = candidate("original", 100)
        gpu = candidate("vulkan", 60, tail=120)
        cpu = candidate("cpu", 80)
        self.assertEqual("cpu", selection.select_candidate([baseline, gpu, cpu])["backend"])

    def test_first_use_score_margin_quality_and_resource_rejections(self):
        baseline = candidate("original", 100)
        self.assertIs(baseline, selection.select_candidate([baseline, candidate("vulkan", 98)]))
        self.assertIs(baseline, selection.select_candidate([baseline, candidate("vulkan", 60, eligible=False, quality=False)]))
        self.assertIsNone(selection.select_candidate([candidate("vulkan", 60)]))
        self.assertEqual("cpu", selection.select_candidate([candidate("original", 100, eligible=False), candidate("cpu", 80)])["backend"])

    def test_gpu_must_improve_native_cpu_without_a_tail_regression(self):
        original, cpu = candidate("original", 200), candidate("cpu", 100)
        self.assertEqual("cpu", selection.select_candidate([original, cpu, candidate("vulkan", 70, tail=150)])["backend"])
        self.assertEqual("cpu", selection.select_candidate([original, cpu, candidate("vulkan", 97)])["backend"])
        self.assertEqual("vulkan", selection.select_candidate([original, cpu, candidate("vulkan", 70, tail=105)])["backend"])

    def test_resource_admission_and_thread_reserve(self):
        h = hardware()
        self.assertEqual([4, 12], selection.thread_candidates(h))
        h["cpu"]["affinity"] = 4
        self.assertEqual([2], selection.thread_candidates(h))
        h["available_memory"] = selection.GIB
        self.assertEqual("insufficient_available_memory", selection.admission(h))
        h = hardware()
        self.assertEqual("insufficient_device_memory", selection.admission(h, SimpleNamespace(device_type="gpu", memory_free=selection.GIB)))
        h["machine"] = "ARM64"
        self.assertEqual("unsupported_windows_architecture", selection.admission(h))

    def test_device_ids_are_stable_and_not_registry_indices(self):
        a = SimpleNamespace(kind="vulkan", device_id="PCI:one", name="Vulkan0", description="simulated")
        b = SimpleNamespace(kind="vulkan", device_id="PCI:one", name="Vulkan2", description="simulated")
        self.assertEqual(selection.device_key(a), selection.device_key(b))
        a.device_id = b.device_id = ""
        self.assertEqual(selection.device_key(a), selection.device_key(b))

    def test_cache_reuse_overrides_driver_model_and_policy_invalidation(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            model = root / "model"
            model.mkdir()
            for name in ("model.safetensors", "config.json", "generation_config.json", "tokenizer.model"):
                (model / name).write_bytes(b"simulated")
            lib = root / "transcribe.dll"
            q8 = root / "q8.gguf"
            lib.write_bytes(b"simulated dll")
            q8.write_bytes(b"simulated model")
            manifest = {"library_path": str(lib), "model_path": str(q8), "runtime_hashes": {str(lib): native.file_hash(lib)},
                        "source_sha256": native.file_hash(model / "model.safetensors"), "model_sha256": native.file_hash(q8), "commit": native.COMMIT}
            h = hardware()
            cpu, gpu = candidate("cpu", 80), candidate("vulkan", 60)
            cache = {"schema": selection.SCHEMA, "policy": selection.POLICY, "manifest": manifest,
                     "fingerprint": selection.fingerprint(model, manifest, h), "results": [cpu, gpu], "selected": gpu}
            path = root / selection.model_key(model) / "selection.json"
            selection.atomic_json(path, cache)
            with patch.object(selection, "hardware_snapshot", return_value=h):
                chosen, reason = selection.resolve_selection(model, cache_root=root)
                self.assertEqual("vulkan", chosen["backend"])
                self.assertIsNone(reason)
                self.assertEqual("cpu", selection.resolve_selection(model, "cpu", cache_root=root)[0]["backend"])
                self.assertIsNone(selection.resolve_selection(model, "gpu", "missing", root)[0])
                self.assertEqual((None, "original_runtime_override"), selection.resolve_selection(model, "original", cache_root=root))
                h["available_memory"] -= selection.GIB
                self.assertIsNotNone(selection.resolve_selection(model, cache_root=root)[0])
                h["drivers"]["version"] = "2"
                self.assertIn("changed", selection.resolve_selection(model, cache_root=root)[1])
                h["drivers"]["version"] = "1"
                (model / "preprocessor_config.json").write_bytes(b"new processor configuration")
                self.assertIn("changed", selection.resolve_selection(model, cache_root=root)[1])
                (model / "preprocessor_config.json").unlink()
                (model / "config.json").write_bytes(b"changed configuration")
                self.assertIn("changed", selection.resolve_selection(model, cache_root=root)[1])
                cache["policy"] = "old-policy"
                selection.atomic_json(path, cache)
                self.assertIn("policy_changed", selection.resolve_selection(model, cache_root=root)[1])
                path.write_text("broken json")
                self.assertIn("unavailable", selection.resolve_selection(model, cache_root=root)[1])

    def test_hash_contract_and_complete_dependency_coverage(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            package = root / "transcribe_cpp"
            package.mkdir()
            init = package / "__init__.py"
            dll = root / "transcribe.dll"
            init.write_bytes(b"binding")
            dll.write_bytes(b"dll")
            manifest = {"version": native.VERSION, "header_hash": native.HEADER_HASH, "commit": native.COMMIT,
                        "precision": "Q8_0", "bindings_path": str(root), "library_path": str(dll),
                        "runtime_hashes": {str(init): native.file_hash(init), str(dll): native.file_hash(dll)}}
            native.verify_manifest(manifest)
            dll.write_bytes(b"tampered")
            with self.assertRaisesRegex(ValueError, "integrity"):
                native.verify_manifest(manifest)
            manifest["runtime_hashes"].pop(str(dll))
            with self.assertRaisesRegex(ValueError, "every loaded"):
                native.verify_manifest(manifest)
            manifest["version"] = "0.2.5"
            with self.assertRaisesRegex(ValueError, "contract"):
                native.verify_manifest(manifest)

    def test_archive_traversal_and_links_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            archive = root / "bad.zip"
            with zipfile.ZipFile(archive, "w") as stream:
                stream.writestr("../escaped.dll", "bad")
            with self.assertRaisesRegex(ValueError, "escapes"):
                prepare.extract(archive, root / "extract")
            with zipfile.ZipFile(archive, "w") as stream:
                info = zipfile.ZipInfo("link.dll")
                info.external_attr = 0o120777 << 16
                stream.writestr(info, "outside.dll")
            with self.assertRaisesRegex(ValueError, "symbolic link"):
                prepare.extract(archive, root / "extract")

    def test_extraction_preserves_identity_and_repairs_modified_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            archive = root / "good.zip"
            with zipfile.ZipFile(archive, "w") as stream:
                stream.writestr("binding.py", "verified")
            prepare.extract(archive, root / "extract")
            target = root / "extract" / "binding.py"
            identity = selection.file_identity(target)
            prepare.extract(archive, root / "extract")
            self.assertEqual(identity, selection.file_identity(target))
            target.write_text("modified")
            prepare.extract(archive, root / "extract")
            self.assertEqual("verified", target.read_text())
            (root / "extract" / "injected.py").write_text("untrusted")
            with self.assertRaisesRegex(ValueError, "Unexpected executable"):
                prepare.extract(archive, root / "extract")

    def test_download_digest_corruption_and_resume_range(self):
        import io
        import hashlib
        class Response(io.BytesIO):
            status = 206
            url = "https://files.pythonhosted.org/pinned.whl"
            headers = {"Content-Range": "bytes 3-5/6"}
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "asset.whl"
            path.with_suffix(".whl.partial").write_bytes(b"abc")
            with patch.object(prepare.urllib.request, "urlopen", return_value=Response(b"def")):
                prepare.download(path, Response.url, hashlib.sha256(b"abcdef").hexdigest())
            self.assertEqual(b"abcdef", path.read_bytes())
            path.unlink()
            def corrupt(*args, **kwargs):
                response = Response(b"corrupt")
                response.status = 200
                return response
            with patch.object(prepare.urllib.request, "urlopen", side_effect=corrupt):
                with self.assertRaisesRegex(ValueError, "checksum"):
                    prepare.download(path, Response.url, "0" * 64)
            self.assertFalse(path.exists())

    @unittest.skipUnless(os.name == "nt", "Windows process/locking lifecycle")
    def test_cross_process_lock_and_cancellation_cleanup(self):
        import psutil
        with tempfile.TemporaryDirectory() as directory:
            lock = Path(directory) / "inference.lock"
            with selection.exclusive_file(lock):
                with self.assertRaisesRegex(RuntimeError, "busy"):
                    with selection.exclusive_file(lock):
                        pass
            with selection.exclusive_file(lock):
                pass
            process = subprocess.Popen([sys.executable, "-u", "-c", "import time; print('ready',flush=True); time.sleep(60)"],
                                       stdout=subprocess.PIPE, creationflags=subprocess.CREATE_NO_WINDOW)
            try:
                self.assertEqual(b"ready\r\n", process.stdout.readline())
                descendants = psutil.Process(process.pid).children(recursive=True)
                prepare.kill_process_tree(process)
                process.wait(timeout=5)
                self.assertTrue(all(not p.is_running() for p in descendants))
            finally:
                if process.poll() is None:
                    prepare.kill_process_tree(process)
                process.stdout.close()


if __name__ == "__main__":
    unittest.main()

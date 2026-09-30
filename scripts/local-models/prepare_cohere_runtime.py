"""Explicit, cancellable/resumable Windows x64 preparation; never imported by inference."""
import argparse
import hashlib
import json
import math
import os
import queue
import re
import shutil
import subprocess
import sys
import tarfile
import threading
import time
import urllib.request
import wave
import zipfile
from pathlib import Path

from cohere_native_runtime import COMMIT, HEADER_HASH, VERSION, file_hash
from cohere_runtime_selection import (GIB, POLICY, SCHEMA, admission, atomic_json, device_key,
                                     exclusive_file, fingerprint, hardware_snapshot, model_key,
                                     root_directory, select_candidate, thread_candidates)

RELEASE = "https://github.com/handy-computer/transcribe.cpp/releases/download/v0.2.4/"
ASSETS = {
    "native.tar.gz": (RELEASE + "transcribe-native-0.2.4-windows-x86_64-cpu-vulkan.tar.gz",
                      "09705f54218817c065602ada8fd0f4d13b3f7fbb9d94929eeb3789c6c2b1f34a"),
    "source.zip": ("https://codeload.github.com/handy-computer/transcribe.cpp/zip/" + COMMIT,
                   "3b01f2569f656b7ad075f3cfa7684ec715f83b5e002135f9a397f1c7cff9d940"),
    "gguf.whl": ("https://files.pythonhosted.org/packages/5e/0c/e0f1eae7535a97476fb903f65301e35da2a66182b8161066b7eb312b2cb8/gguf-0.18.0-py3-none-any.whl",
                 "af93f7ef198a265cbde5fa6a6b3101528bca285903949ab0a3e591cd993a1864"),
}
ALLOWED_HOSTS = {"github.com", "codeload.github.com", "release-assets.githubusercontent.com", "files.pythonhosted.org"}


def progress(message):
    print(message, flush=True)


def download(path, url, expected):
    """Keep partial bytes across cancellation; accept only an entire pinned digest."""
    if path.exists() and file_hash(path) == expected:
        return
    partial = path.with_suffix(path.suffix + ".partial")
    for attempt in range(3):
        try:
            offset = partial.stat().st_size if partial.exists() else 0
            request = urllib.request.Request(url, headers={"Range": f"bytes={offset}-"} if offset else {})
            with urllib.request.urlopen(request, timeout=30) as response:
                from urllib.parse import urlparse
                final = urlparse(response.url)
                if final.scheme != "https" or final.hostname not in ALLOWED_HOSTS:
                    raise ValueError("Runtime download redirected outside approved upstream hosts.")
                append = response.status == 206 and offset > 0
                if append and not response.headers.get("Content-Range", "").startswith(f"bytes {offset}-"):
                    raise ValueError("Runtime download returned an invalid resume range.")
                with partial.open("ab" if append else "wb") as stream:
                    count = 0
                    while chunk := response.read(1024 * 1024):
                        stream.write(chunk)
                        count += len(chunk)
                        if count % (8 * 1024 * 1024) == 0:
                            progress(f"Downloaded {(offset if append else 0) + count} bytes of {path.name}")
            if file_hash(partial) != expected:
                partial.unlink()
                raise ValueError("Runtime archive checksum mismatch; retry preparation to repair it.")
            partial.replace(path)
            return
        except (OSError, ValueError):
            if attempt == 2:
                raise


def validate_member(root, name):
    target = (root / name).resolve()
    if not target.is_relative_to(root.resolve()):
        raise ValueError("Archive entry escapes its extraction directory.")


def extract(archive, destination, prefixes=None):
    destination.mkdir(parents=True, exist_ok=True)
    expected_files = set()
    if archive.name.endswith(".tar.gz"):
        with tarfile.open(archive) as stream:
            for member in stream.getmembers():
                validate_member(destination, member.name)
                if not (member.isfile() or member.isdir()):
                    raise ValueError("Runtime archive contains links or special files.")
            for member in stream.getmembers():
                target = destination / member.name
                if member.isdir():
                    target.mkdir(parents=True, exist_ok=True)
                else:
                    expected_files.add(target.resolve())
                    with stream.extractfile(member) as content:
                        restore_file(target, content.read())
    else:
        with zipfile.ZipFile(archive) as stream:
            for member in stream.infolist():
                validate_member(destination, member.filename)
                if prefixes and not any(member.filename.startswith(p) for p in prefixes):
                    continue
                if (member.external_attr >> 16) & 0o170000 == 0o120000:
                    raise ValueError("Runtime archive contains a symbolic link.")
            for member in stream.infolist():
                if prefixes and not any(member.filename.startswith(p) for p in prefixes):
                    continue
                target = destination / member.filename
                if member.is_dir():
                    target.mkdir(parents=True, exist_ok=True)
                else:
                    expected_files.add(target.resolve())
                    restore_file(target, stream.read(member))
    # Never bless executable files injected into an earlier extraction by hashing them.
    for path in destination.rglob("*"):
        if path.is_file() and path.suffix.lower() in (".py", ".dll", ".pyd") and path.resolve() not in expected_files:
            raise ValueError("Unexpected executable in runtime cache. Remove this runtime cache and retry preparation.")


def restore_file(target, content):
    if target.exists() and file_hash(target) == hashlib.sha256(content).hexdigest():
        return
    target.parent.mkdir(parents=True, exist_ok=True)
    temporary = target.with_name(target.name + ".tmp")
    temporary.write_bytes(content)
    temporary.replace(target)


def run_required(arguments, env, timeout=900):
    process = subprocess.Popen(arguments, env=env, creationflags=subprocess.CREATE_NO_WINDOW)
    try:
        if process.wait(timeout=timeout):
            raise RuntimeError("Conversion failed. Check available memory/storage and retry preparation.")
    finally:
        if process.poll() is None:
            kill_process_tree(process)
        process.wait()


def kill_process_tree(process):
    import psutil
    try:
        children = psutil.Process(process.pid).children(recursive=True)
        for child in reversed(children):
            try:
                child.kill()
            except psutil.Error:
                pass
        process.kill()
        psutil.wait_procs(children, timeout=5)
    except psutil.Error:
        if process.poll() is None:
            process.kill()


def provision(model_dir, root, hardware):
    root.mkdir(parents=True, exist_ok=True)
    reason = admission(hardware)
    if reason:
        raise RuntimeError(reason + "; the original provider remains available.")
    for name, (url, digest) in ASSETS.items():
        progress("Verifying/downloading pinned " + name)
        download(root / name, url, digest)
    # Always restore source/bindings from pinned bytes; a modified cache cannot become trusted.
    source_prefix = "transcribe.cpp-" + COMMIT + "/"
    extract(root / "source.zip", root / "source", [source_prefix + p for p in ("scripts/", "bindings/python/", "LICENSE")])
    extract(root / "native.tar.gz", root / "native")
    extract(root / "gguf.whl", root / "converter-deps")
    source = root / "source" / ("transcribe.cpp-" + COMMIT)
    native = root / "native" / "transcribe-native-windows-x86_64-cpu-vulkan"
    contract = json.loads((native / "contract.json").read_text())
    if (contract["version"], contract["header_hash"]) != (VERSION, HEADER_HASH):
        raise ValueError("Pinned Windows release ABI contract mismatch.")
    key_root = root / model_key(model_dir)
    key_root.mkdir(exist_ok=True)
    # All model inputs affect reuse, including tokenizer/configuration (not just weights).
    model_files = sorted({model_dir / name for name in
                          ("model.safetensors", "config.json", "generation_config.json", "tokenizer.model")}
                         | {p for p in model_dir.iterdir() if p.is_file() and p.suffix in (".json", ".model", ".py")})
    model_hashes = {p.name: file_hash(p) for p in model_files}
    identity = {"inputs": model_hashes, "commit": COMMIT, "quantization_policy": POLICY}
    q8 = key_root / "cohere-Q8_0.gguf"
    receipt_path = key_root / "conversion-receipt.json"
    reuse = False
    try:
        receipt = json.loads(receipt_path.read_text())
        reuse = receipt["identity"] == identity and q8.exists() and file_hash(q8) == receipt["model_sha256"]
    except (OSError, ValueError, KeyError):
        pass
    if not reuse:
        if shutil.disk_usage(key_root).free < 10 * GIB:
            raise RuntimeError("At least 10 GiB free storage is required for same-checkpoint conversion. Free space and retry.")
        env = dict(os.environ, PYTHONPATH=str(root / "converter-deps"), OMP_NUM_THREADS="1", MKL_NUM_THREADS="1")
        bf16 = key_root / "cohere-BF16.gguf.tmp"
        progress("Converting the configured checkpoint locally (no model substitution)")
        run_required([sys.executable, "-u", str(source / "scripts" / "convert-cohere.py"), str(model_dir), str(bf16)], env)
        temporary = key_root / "cohere-Q8_0.gguf.tmp"
        progress("Quantizing Q8_0 with the pinned ggml DLL; no compiler required")
        run_required([sys.executable, "-u", str(Path(__file__).with_name("cohere_quantize.py")), str(bf16),
                      str(temporary), str(native / "ggml-base.dll")], env)
        temporary.replace(q8)
        atomic_json(receipt_path, {"identity": identity, "model_sha256": file_hash(q8)})
        bf16.unlink(missing_ok=True)
    hashes = {str(p): file_hash(p) for p in native.rglob("*") if p.is_file()}
    for name in ("msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll"):
        dependency = Path(os.environ["SystemRoot"]) / "System32" / name
        if not dependency.exists():
            raise RuntimeError("Microsoft Visual C++ x64 runtime is missing. Run prepare-cohere-python-runtime.ps1 and retry.")
        hashes[str(dependency)] = file_hash(dependency)
    bindings = source / "bindings" / "python" / "src"
    hashes.update({str(p): file_hash(p) for p in (bindings / "transcribe_cpp").rglob("*.py")})
    manifest = {"version": VERSION, "commit": COMMIT, "header_hash": HEADER_HASH, "precision": "Q8_0",
                "backend": "cpu", "threads": thread_candidates(hardware)[0], "model_path": str(q8),
                "model_sha256": file_hash(q8), "source_sha256": model_hashes["model.safetensors"],
                "source_hashes": model_hashes, "library_path": str(native / "transcribe.dll"),
                "bindings_path": str(bindings), "runtime_hashes": hashes}
    atomic_json(key_root / "native-manifest.json", manifest)
    return manifest


def normalized_words(text):
    # Fixed synthetic references avoid numerical spelling ambiguity; include full sentences.
    return re.findall(r"[a-z0-9]+", text.lower())


def word_errors(reference, hypothesis):
    previous = list(range(len(hypothesis) + 1))
    for i, word in enumerate(reference, 1):
        row = [i]
        for j, other in enumerate(hypothesis, 1):
            row.append(min(row[-1] + 1, previous[j] + 1, previous[j - 1] + (word != other)))
        previous = row
    return previous[-1]


def fixtures(root):
    references = {"short": "Please open the meeting notes.",
                  "medium": "Sarah and Michael will meet in Boston on Tuesday. Please bring the updated project schedule."}
    script = ("$ErrorActionPreference='Stop'; $v=New-Object -ComObject SAPI.SpVoice; $v.Rate=0; "
              "$voices=$v.GetVoices(); $english=$null; for($i=0;$i -lt $voices.Count;$i++){ "
              "$token=$voices.Item($i); $lang=($token.GetAttribute('Language') -split ';')[0]; "
              "if (([Convert]::ToInt32($lang,16) -band 1023) -eq 9) { $english=$token; break } }; "
              "if($null -eq $english){throw 'Install an English desktop voice'}; $v.Voice=$english; ")
    for name, text in references.items():
        path = str(root / (name + ".wav")).replace("'", "''")
        script += ("$s=New-Object -ComObject SAPI.SpFileStream; $s.Format.Type=18; "
                   f"$s.Open('{path}',3,$false); $v.AudioOutputStream=$s; $null=$v.Speak('{text}'); $s.Close(); ")
    result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", script],
                            capture_output=True, timeout=30, creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        raise RuntimeError("Windows speech synthesis is unavailable. Install an English desktop voice and retry preparation.")
    return references


def percentile(values, fraction):
    return sorted(values)[max(0, math.ceil(len(values) * fraction) - 1)]


def calibrate_candidate(model_dir, root, manifest, candidate, references, deadline):
    import psutil
    started = time.perf_counter()
    env = dict(os.environ, DICTATEANYWHERE_COHERE_CALIBRATION="1",
               DICTATEANYWHERE_COHERE_INFERENCE_WARMUP="0", OMP_NUM_THREADS=str(candidate["threads"]),
               MKL_NUM_THREADS=str(candidate["threads"]), OPENBLAS_NUM_THREADS="1", HF_HUB_OFFLINE="1")
    env.pop("DICTATEANYWHERE_COHERE_WARMUP_AUDIO", None)
    env.pop("DICTATEANYWHERE_COHERE_NATIVE_MANIFEST", None)
    if candidate["backend"] != "original":
        path = root / "candidate-manifest.json"
        atomic_json(path, dict(manifest, **{k: candidate[k] for k in ("backend", "threads", "device_key")}))
        env["DICTATEANYWHERE_COHERE_NATIVE_MANIFEST"] = str(path)
    process = subprocess.Popen([sys.executable, "-u", str(Path(__file__).with_name("cohere_transcribe_worker.py")),
                                "--model-dir", str(model_dir), "--threads", str(candidate["threads"]),
                                "--runtime-mode", "original" if candidate["backend"] == "original" else "automatic"],
                               stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                               text=True, encoding="utf-8", env=env, creationflags=subprocess.CREATE_NO_WINDOW)
    lines = queue.Queue(maxsize=16)
    def read_lines():
        for line in process.stdout:
            lines.put(line)
        lines.put(None)
    def drain_errors():
        for _ in process.stderr:
            pass  # Native diagnostics stay out of the speech-free selection receipt.
    threading.Thread(target=read_lines, daemon=True).start()
    threading.Thread(target=drain_errors, daemon=True).start()
    stop = threading.Event()
    samples = []
    probes = []
    def monitor():
        expected = time.perf_counter() + .05
        while not stop.wait(.05):
            now = time.perf_counter()
            probes.append(max(0, (now - expected) * 1000))
            expected = now + .05
            try:
                launcher = psutil.Process(process.pid)
                memory = [p.memory_info() for p in [launcher, *launcher.children(recursive=True)]]
                samples.append((sum(m.rss for m in memory), sum(getattr(m, "private", m.vms) for m in memory)))
            except psutil.Error:
                pass
    monitor_thread = threading.Thread(target=monitor, daemon=True)
    monitor_thread.start()
    def receive():
        remaining = min(120, deadline - time.perf_counter())
        if remaining <= 0:
            raise TimeoutError("calibration_budget_exhausted")
        line = lines.get(timeout=remaining)
        if line is None:
            raise RuntimeError("candidate_worker_exited")
        response = json.loads(line)
        if response.get("status") not in ("ready", "ok"):
            raise RuntimeError("candidate_worker_failed")
        payload = response["payload"]
        if payload.get("fallback_reason") and candidate["backend"] != "original":
            raise RuntimeError("candidate_fell_back")
        if candidate["backend"] != "original" and not payload.get("backend", "").startswith("transcribe.cpp/"):
            raise RuntimeError("candidate_backend_mismatch")
        return payload
    try:
        metadata = receive()
        ready_ms = (time.perf_counter() - started) * 1000
        buckets = {}
        all_errors = []
        for name, reference in references.items():
            runs = []
            for _ in range(6):
                tick = time.perf_counter()
                process.stdin.write(json.dumps({"audio_path": str(root / (name + ".wav")),
                                                "language": "en", "punctuation": True}) + "\n")
                process.stdin.flush()
                response = receive()
                errors = word_errors(normalized_words(reference), normalized_words(response["text"]))
                all_errors.append(errors)
                runs.append((time.perf_counter() - tick) * 1000)
            with wave.open(str(root / (name + ".wav"))) as audio:
                seconds = audio.getnframes() / audio.getframerate()
            buckets[name] = {"first_ms": runs[0], "p50_ms": percentile(runs[1:], .5),
                             "p95_ms": percentile(runs[1:], .95), "warm_samples": 5,
                             "audio_seconds": seconds, "rtf": percentile(runs[1:], .5) / (seconds * 1000),
                             "word_errors": all_errors[-6:], "reference_words": len(normalized_words(reference)),
                             "audio_sha256": file_hash(root / (name + ".wav"))}
        peak_private = max((m[1] for m in samples), default=0)
        lag = percentile(probes, .95) if probes else 1000
        eligible = not any(all_errors) and lag <= 100 and peak_private <= psutil.virtual_memory().total * .85
        # Amortize startup/first use over a short 20-dictation session; tail matters separately.
        score = sum(b["p50_ms"] * .5 + b["p95_ms"] * .5 + b["first_ms"] / 20 for b in buckets.values()) / len(buckets)
        score += ready_ms / 20
        return dict(candidate, eligible=eligible, quality_pass=not any(all_errors), rejected_reason=None if eligible else "quality_memory_or_responsiveness",
                    ready_ms=ready_ms, model_load_ms=metadata.get("model_load_ms"), fixtures=buckets,
                    peak_private_bytes=peak_private, peak_working_set_bytes=max((m[0] for m in samples), default=0),
                    scheduler_lag_p95_ms=lag, score_ms=score, actual_backend=metadata.get("backend"),
                    actual_device=metadata.get("device"), precision=metadata.get("dtype"))
    except (OSError, ValueError, KeyError, RuntimeError, TimeoutError, queue.Empty) as exc:
        return dict(candidate, eligible=False, rejected_reason=type(exc).__name__ + ":" + str(exc)[:100])
    finally:
        stop.set()
        if process.poll() is None:
            kill_process_tree(process)
        process.wait(timeout=10)
        monitor_thread.join(timeout=1)
        for stream in (process.stdin, process.stdout, process.stderr):
            stream.close()


def prepare(args):
    setup_started = time.perf_counter()
    model_dir = Path(args.model_dir).resolve()
    root = Path(args.runtime_dir).resolve() if args.runtime_dir else root_directory()
    for ancestor in Path(__file__).resolve().parents:
        if (ancestor / ".git").exists():
            if root.is_relative_to(ancestor) and not root.is_relative_to(ancestor / "artifacts"):
                raise ValueError("Runtime data must stay under ignored artifacts/ or outside the checkout.")
            break
    root.mkdir(parents=True, exist_ok=True)
    with exclusive_file(root_directory() / "preparation.lock"):
        # Refuse concurrent inference before allocating conversion resources.
        with exclusive_file(root_directory() / "inference.lock"):
            hardware = hardware_snapshot()
            progress("Checking CPU topology, drivers, available memory and storage")
            manifest = provision(model_dir, root, hardware)
        provision_ms = (time.perf_counter() - setup_started) * 1000
        key_root = root / model_key(model_dir)
        if args.provision_only:
            if not 1 <= args.threads <= hardware["cpu"]["affinity"]:
                raise ValueError("Manual thread count exceeds available processors.")
            manual = dict(manifest, backend=args.backend, threads=args.threads)
            if args.device_key:
                manual["device_key"] = args.device_key
            atomic_json(root / "native-manifest.json", manual)
            progress("Native runtime prepared: " + str(root / "native-manifest.json"))
            return
        current_fingerprint = fingerprint(model_dir, manifest, hardware)
        selection_path = key_root / "selection.json"
        try:
            cache = json.loads(selection_path.read_text())
            if not args.recalibrate and cache.get("fingerprint") == current_fingerprint and cache.get("policy") == POLICY:
                progress("Verified existing setup and measured selection; calibration reused")
                return
        except (OSError, ValueError):
            pass
        # Probe the actual registered DLL backends in isolation: driver crashes cannot crash the app.
        progress("Probing usable native devices and pinned ABI")
        probe = subprocess.run([sys.executable, str(Path(__file__).with_name("cohere_native_runtime.py")),
                                "--probe", str(key_root / "native-manifest.json")],
                               capture_output=True, text=True, timeout=30, creationflags=subprocess.CREATE_NO_WINDOW)
        devices = json.loads(probe.stdout) if probe.returncode == 0 else []
        progress("Generating English synthetic calibration speech (microphone is never opened)")
        references = fixtures(key_root)
        threads = thread_candidates(hardware)
        candidates = [{"backend": "original", "threads": threads[-1], "device_key": None}]
        rejected = []
        for device in devices:
            if device["kind"] not in ("cpu", "vulkan"):
                continue
            if device["kind"] == "vulkan" and device["device_type"] == "gpu" and device["memory_free"] and device["memory_free"] < 3 * GIB:
                rejected.append({"device_key": device["key"], "reason": "insufficient_device_memory"})
                continue
            counts = threads if device["kind"] == "cpu" else [threads[0]]
            for count in counts:
                candidates.append({"backend": device["kind"], "threads": count, "device_key": device["key"]})
        deadline = time.perf_counter() + args.calibration_seconds
        results = []
        for candidate in candidates[:7]:
            if time.perf_counter() >= deadline:
                results.append(dict(candidate, eligible=False, rejected_reason="calibration_budget_exhausted"))
                continue
            progress(f"Measuring {candidate['backend']} {candidate['device_key'] or ''} ({candidate['threads']} threads)")
            result = calibrate_candidate(model_dir, key_root, manifest, candidate, references, deadline)
            results.append(result)
            progress("Candidate accepted" if result["eligible"] else "Candidate rejected: " + result["rejected_reason"])
        selected = select_candidate(results)
        reason = ("measured_native_improvement" if selected and selected["backend"] != "original"
                  else "original_retained_no_safe_measured_improvement")
        atomic_json(selection_path, {"schema": SCHEMA, "policy": POLICY, "fingerprint": current_fingerprint,
                                    "manifest": manifest, "hardware": hardware, "devices": devices,
                                    "results": results, "selected": selected, "reason": reason, "rejected_devices": rejected,
                                    "unmeasured_devices": [c for c in candidates[7:]],
                                    "provision_ms": provision_ms, "calibration_ms": (time.perf_counter() - deadline + args.calibration_seconds) * 1000,
                                    "total_setup_ms": (time.perf_counter() - setup_started) * 1000,
                                    "metric": "worker response latency; scheduler lag is not UI frame rate"})
        progress("Prepared: " + (selected["backend"] if selected else "original") + " — " + reason)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-dir", required=True)
    parser.add_argument("--runtime-dir")
    parser.add_argument("--recalibrate", action="store_true")
    parser.add_argument("--provision-only", action="store_true")
    parser.add_argument("--backend", choices=("cpu", "vulkan"), default="cpu")
    parser.add_argument("--threads", type=int, default=max(1, min(4, (os.cpu_count() or 1) - 2)))
    parser.add_argument("--device-key")
    parser.add_argument("--calibration-seconds", type=int, choices=range(30, 601), default=300)
    args = parser.parse_args()
    try:
        prepare(args)
        return 0
    except (OSError, ValueError, KeyError, RuntimeError, ImportError, subprocess.SubprocessError) as exc:
        progress("Preparation failed: " + str(exc))
        try:
            atomic_json(root_directory() / "preparation-status.json", {"error": str(exc), "updated_utc": time.time()})
        except OSError:
            pass
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

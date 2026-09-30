"""Offline selection, identity and resource policy shared by preparation and workers.

Only explicit preparation downloads or calibrates. Cache files contain no speech.
"""
import contextlib
import hashlib
import json
import os
import platform
import subprocess
from pathlib import Path

SCHEMA = 1
POLICY = "cohere-q8-calibration-v1"
GIB = 1024 ** 3


def root_directory():
    return Path(os.environ.get("LOCALAPPDATA") or Path.home()) / "DictateAnywhere" / "cohere-auto" / "0.2.4"


def model_key(model_dir):
    return hashlib.sha256(str(Path(model_dir).resolve()).lower().encode()).hexdigest()[:24]


def atomic_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + "." + str(os.getpid()) + ".tmp")
    with temporary.open("w", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2, allow_nan=False)
        stream.flush()
        os.fsync(stream.fileno())
    temporary.replace(path)


def file_identity(path):
    path = Path(path)
    stat = path.stat()
    return [str(path.resolve()), stat.st_size, stat.st_mtime_ns]


def hardware_snapshot():
    import psutil
    memory = psutil.virtual_memory()
    swap = psutil.swap_memory()
    cpu = {"physical": psutil.cpu_count(logical=False) or 1,
           "logical": psutil.cpu_count() or 1, "affinity": len(psutil.Process().cpu_affinity())}
    # PNP id + driver version, not GPU marketing name, invalidates the selection.
    command = ("$ErrorActionPreference='Stop'; @{cpu=@(Get-CimInstance Win32_Processor | "
               "Select-Object Name,NumberOfCores,NumberOfLogicalProcessors); "
               "drivers=@(Get-CimInstance Win32_VideoController | "
               "Select-Object PNPDeviceID,DriverVersion,DriverDate,Status)} | ConvertTo-Json -Depth 4 -Compress")
    drivers = None
    if os.name == "nt":
        result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", command],
                                capture_output=True, text=True, timeout=12,
                                creationflags=subprocess.CREATE_NO_WINDOW)
        if result.returncode:
            raise RuntimeError("Cannot verify display drivers. Repair Windows WMI and prepare again.")
        drivers = json.loads(result.stdout)
    return {"os": platform.platform(), "machine": platform.machine(), "cpu": cpu,
            "drivers": drivers, "total_memory": memory.total, "available_memory": memory.available,
            "available_commit": memory.available + swap.free}


def fingerprint(model_dir, manifest, hardware):
    files = [Path(model_dir) / name for name in
             ("model.safetensors", "config.json", "generation_config.json", "tokenizer.model")]
    files += [Path(model_dir) / name for name in sorted(manifest.get("source_hashes", {}))
              if name not in ("model.safetensors", "config.json", "generation_config.json", "tokenizer.model")]
    files = sorted(set(files) | {p for p in Path(model_dir).iterdir()
                                if p.is_file() and p.suffix in (".json", ".model", ".py")})
    files += [Path(manifest["model_path"]), Path(manifest["library_path"])]
    files += [Path(path) for path in sorted(manifest["runtime_hashes"])]
    # Available memory is a live admission check, not a reason to recalibrate every launch.
    stable_hardware = {k: v for k, v in hardware.items()
                       if k not in ("available_memory", "available_commit")}
    runtime_packages = []
    import importlib.metadata
    for name in ("torch", "transformers", "numpy", "psutil"):
        try:
            runtime_packages.append([name, importlib.metadata.version(name)])
        except importlib.metadata.PackageNotFoundError:
            runtime_packages.append([name, None])
    value = {"schema": SCHEMA, "policy": POLICY, "hardware": stable_hardware,
             "files": [file_identity(p) for p in files], "python": platform.python_version(),
             "packages": runtime_packages, "source_sha256": manifest["source_sha256"],
             "model_sha256": manifest["model_sha256"], "commit": manifest["commit"]}
    value["application_runtime"] = {name: hashlib.sha256(Path(__file__).with_name(name).read_bytes()).hexdigest()
                                    for name in ("cohere_transcribe_worker.py", "cohere_native_runtime.py",
                                                 "cohere_runtime_selection.py", "cohere_quantize.py", "prepare_cohere_runtime.py")}
    return hashlib.sha256(json.dumps(value, sort_keys=True).encode()).hexdigest()


def thread_candidates(hardware):
    cpu = hardware["cpu"]
    # Leave capacity for the UI/capture; never stack Torch/OpenMP/BLAS pools.
    limit = max(1, min(cpu["physical"], cpu["affinity"] - 2, 12))
    return sorted({min(4, limit), limit})


def device_key(device):
    # A backend enumeration label (Vulkan0/1) can change between launches.
    # Without a UUID, duplicate descriptions deliberately remain ambiguous and fail closed at load.
    return device.kind + ":" + (device.device_id or device.description or device.name)


def admission(hardware, device=None):
    if hardware["machine"].lower() not in ("amd64", "x86_64") or os.name != "nt":
        return "unsupported_windows_architecture"
    if hardware["available_memory"] < 4 * GIB or hardware["available_commit"] < 5 * GIB:
        return "insufficient_available_memory"
    if device and device.device_type == "gpu" and device.memory_free and device.memory_free < 3 * GIB:
        return "insufficient_device_memory"
    return None


def select_candidate(results):
    eligible = [r for r in results if r.get("eligible")]
    if not eligible:
        return None
    original = next((r for r in results if r["backend"] == "original" and r.get("quality_pass")), None)
    # A failed/untested original cannot establish quality parity for automatic adoption.
    if original is None:
        return None
    native = [r for r in eligible if r["backend"] != "original"
              and r["score_ms"] <= original["score_ms"] * .95
              and all(r["fixtures"][k]["p95_ms"] <= original["fixtures"][k]["p95_ms"] * 1.10
                      for k in original["fixtures"])]
    cpu = min((r for r in native if r["backend"] == "cpu"), key=lambda r: r["score_ms"], default=None)
    baseline = cpu or original
    gpu = [r for r in native if r["backend"] == "vulkan"
           and r["score_ms"] <= baseline["score_ms"] * .95
           and all(r["fixtures"][k]["p95_ms"] <= baseline["fixtures"][k]["p95_ms"] * 1.10
                   for k in baseline["fixtures"])]
    return min(gpu, key=lambda r: r["score_ms"]) if gpu else baseline


def resolve_selection(model_dir, mode="automatic", requested_device=None, cache_root=None):
    if mode == "original":
        return None, "original_runtime_override"
    root = Path(cache_root) if cache_root else root_directory()
    try:
        cache = json.loads((root / model_key(model_dir) / "selection.json").read_text(encoding="utf-8"))
        if cache["schema"] != SCHEMA or cache["policy"] != POLICY:
            return None, "selection_policy_changed_prepare_again"
        manifest = cache["manifest"]
        hardware = hardware_snapshot()
        if fingerprint(model_dir, manifest, hardware) != cache["fingerprint"]:
            return None, "hardware_runtime_or_model_changed_prepare_again"
        reason = admission(hardware)
        if reason:
            return None, reason
        if mode == "automatic":
            candidate = cache.get("selected")
        else:
            candidate = next((r for r in sorted(cache["results"], key=lambda r: r.get("score_ms", float("inf")))
                              if r.get("eligible") and r["backend"] == ("cpu" if mode == "cpu" else "vulkan")
                              and (not requested_device or r.get("device_key") == requested_device)), None)
        if not candidate or candidate["backend"] == "original":
            return None, cache.get("reason", "no_validated_candidate_prepare_again")
        selected = dict(manifest, backend=candidate["backend"], threads=candidate["threads"],
                        device_key=candidate["device_key"])
        return selected, None
    except (OSError, ValueError, KeyError, TypeError, RuntimeError, subprocess.SubprocessError):
        return None, "automatic_setup_unavailable_prepare_in_settings"


@contextlib.contextmanager
def exclusive_file(path):
    """Windows byte-range locks release automatically when a crashed process exits."""
    import msvcrt
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a+b") as stream:
        if stream.tell() == 0:
            stream.write(b"0")
            stream.flush()
        stream.seek(0)
        try:
            msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
        except OSError as exc:
            raise RuntimeError("Dictation runtime is busy. Finish dictation or close other Koncus Nai instances, then retry preparation.") from exc
        try:
            yield
        finally:
            stream.seek(0)
            msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)

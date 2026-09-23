import argparse
import json
import os
import shutil
import sys
import hashlib

os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")

PROVENANCE_MARKER = ".notype-provenance.json"

from huggingface_hub import get_token, snapshot_download
from huggingface_hub.errors import GatedRepoError, HfHubHTTPError


def _clean_patterns(patterns: list[str] | None) -> list[str] | None:
    if not patterns:
        return None

    cleaned = [pattern.strip() for pattern in patterns if pattern and pattern.strip()]
    return cleaned or None


def _build_auth_error(repo_id: str) -> str:
    return (
        f"Access to Hugging Face repo '{repo_id}' is gated. "
        "Accept the model access terms, then sign in for this Windows user with "
        "'hf auth login' or set HF_TOKEN before retrying."
    )


def _verify_snapshot(destination: str, provenance_path: str, artifact_id: str, repo_id: str, revision: str) -> None:
    with open(provenance_path, "r", encoding="utf-8-sig") as stream:
        manifest = json.load(stream)

    matches = [item for item in manifest.get("artifacts", []) if item.get("id", "").casefold() == artifact_id.casefold()]
    if len(matches) != 1:
        raise ValueError(f"Provenance must contain exactly one artifact '{artifact_id}'.")
    artifact = matches[0]
    if artifact.get("repository") != repo_id or artifact.get("revision") != revision:
        raise ValueError(f"Provenance identity does not match '{artifact_id}'.")
    if not revision or len(revision) != 40 or any(character not in "0123456789abcdefABCDEF" for character in revision):
        raise ValueError(f"Artifact '{artifact_id}' does not use an immutable revision.")

    expected = {}
    for entry in artifact.get("files", []):
        relative_path = str(entry.get("path", "")).replace("\\", "/")
        normalized_key = relative_path.casefold()
        if not relative_path or relative_path.startswith("/") or ".." in relative_path.split("/"):
            raise ValueError(f"Artifact '{artifact_id}' has an unsafe file path.")
        if normalized_key in expected:
            raise ValueError(f"Artifact '{artifact_id}' has duplicate file identities.")
        sha256 = str(entry.get("sha256", ""))
        if len(sha256) != 64 or any(character not in "0123456789abcdefABCDEF" for character in sha256):
            raise ValueError(f"Artifact '{artifact_id}' has an invalid SHA-256.")
        expected[normalized_key] = (relative_path, int(entry.get("size", -1)), sha256.lower())
    if not expected:
        raise ValueError(f"Artifact '{artifact_id}' has no required files.")

    actual = {}
    for root, directories, files in os.walk(destination):
        directories[:] = [name for name in directories if name.casefold() != ".cache"]
        for file_name in files:
            absolute_path = os.path.join(root, file_name)
            relative_path = os.path.relpath(absolute_path, destination).replace("\\", "/")
            key = relative_path.casefold()
            if key in actual:
                raise ValueError(f"Artifact '{artifact_id}' contains case-colliding files.")
            actual[key] = absolute_path

    if set(actual) != set(expected):
        raise ValueError(f"Artifact '{artifact_id}' file membership does not match provenance.")
    for key, (relative_path, expected_size, expected_sha256) in expected.items():
        absolute_path = actual[key]
        if os.path.getsize(absolute_path) != expected_size:
            raise ValueError(f"Artifact '{artifact_id}' file '{relative_path}' has the wrong size.")
        with open(absolute_path, "rb") as stream:
            prefix = stream.read(128)
            if prefix.startswith(b"version https://git-lfs.github.com/spec/v1"):
                raise ValueError(f"Artifact '{artifact_id}' file '{relative_path}' is a Git LFS pointer.")
            digest = hashlib.sha256(prefix)
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)
        if digest.hexdigest() != expected_sha256:
            raise ValueError(f"Artifact '{artifact_id}' file '{relative_path}' failed its integrity check.")

    marker_path = os.path.join(destination, PROVENANCE_MARKER)
    marker_temp = marker_path + ".tmp"
    with open(marker_temp, "w", encoding="utf-8", newline="\n") as stream:
        json.dump(
            {
                "schemaVersion": 1,
                "artifactId": artifact_id,
                "repository": repo_id,
                "revision": revision,
            },
            stream,
            sort_keys=True,
        )
        stream.write("\n")
    os.replace(marker_temp, marker_path)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-id", required=True)
    parser.add_argument("--destination", required=True)
    parser.add_argument("--revision")
    parser.add_argument("--allow-pattern", action="append")
    parser.add_argument("--ignore-pattern", action="append")
    parser.add_argument("--requires-auth", action="store_true")
    parser.add_argument("--provenance-manifest", required=True)
    parser.add_argument("--artifact-id", required=True)
    args = parser.parse_args()

    destination = os.path.abspath(args.destination)
    if os.path.isdir(destination):
        shutil.rmtree(destination)

    os.makedirs(destination, exist_ok=True)
    allow_patterns = _clean_patterns(args.allow_pattern)
    ignore_patterns = _clean_patterns(args.ignore_pattern)

    try:
        if args.requires_auth and not get_token():
            print(_build_auth_error(args.repo_id), file=sys.stderr, flush=True)
            return 1

        snapshot_download(
            repo_id=args.repo_id,
            revision=args.revision,
            local_dir=destination,
            allow_patterns=allow_patterns,
            ignore_patterns=ignore_patterns,
        )
        _verify_snapshot(
            destination,
            args.provenance_manifest,
            args.artifact_id,
            args.repo_id,
            args.revision,
        )
    except GatedRepoError:
        print(_build_auth_error(args.repo_id), file=sys.stderr, flush=True)
        return 1
    except HfHubHTTPError as exc:
        if getattr(getattr(exc, "response", None), "status_code", None) in (401, 403):
            print(_build_auth_error(args.repo_id), file=sys.stderr, flush=True)
            return 1

        print(str(exc), file=sys.stderr, flush=True)
        return 1
    except Exception as exc:  # pragma: no cover - surfaced to parent process
        print(str(exc), file=sys.stderr, flush=True)
        return 1

    print(json.dumps({"status": "ok", "repo_id": args.repo_id, "destination": destination}), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

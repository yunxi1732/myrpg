"""Prepare local test fixtures; never change ServerData/latest.json or publish them."""

import hashlib
import json
from pathlib import Path
import shutil
from datetime import datetime, timezone


PROJECT = Path(__file__).resolve().parents[1]
SERVER = PROJECT / "ServerData"


def main():
    current = json.loads((SERVER / "latest.json").read_text(encoding="utf-8"))
    release_id = "qa-corrupt-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    output = PROJECT / "Build/HotUpdateAcceptance" / release_id
    prefix = "releases/" + release_id + "/StandaloneWindows64/"
    original_prefix = "releases/" + current["version"] + "/StandaloneWindows64/"
    candidate = json.loads(json.dumps(current))
    candidate["version"] = release_id
    for entry in [candidate, candidate["hotUpdateDll"], *candidate["aotMetadata"]]:
        field = "catalogPath" if entry is candidate else "path"
        entry[field] = entry[field].replace(original_prefix, prefix, 1)
    required = [(current["catalogPath"], candidate["catalogPath"])]
    original_hash = Path(current["catalogPath"]).with_suffix(".hash").as_posix()
    if (SERVER / original_hash).is_file():
        required.append((original_hash, Path(candidate["catalogPath"]).with_suffix(".hash").as_posix()))
    required.extend((old["path"], new["path"])
                    for old, new in zip(current["aotMetadata"], candidate["aotMetadata"]))
    for old, new in required:
        destination = output / new
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(SERVER / old, destination)

    original_dll = (SERVER / current["hotUpdateDll"]["path"]).read_bytes()
    expected_bytes = original_dll + b"integrity-probe-v1"
    corrupt_bytes = original_dll + b"integrity-probe-v2"
    assert len(expected_bytes) == len(corrupt_bytes)
    dll = output / candidate["hotUpdateDll"]["path"]
    dll.parent.mkdir(parents=True, exist_ok=True)
    dll.write_bytes(corrupt_bytes)
    candidate["hotUpdateDll"]["bytes"] = len(expected_bytes)
    candidate["hotUpdateDll"]["sha256"] = hashlib.sha256(expected_bytes).hexdigest()
    (output / "corrupt-latest.json").write_text(json.dumps(candidate, indent=2) + "\n", encoding="utf-8")

    incompatible = json.loads(json.dumps(current))
    incompatible["clientVersion"] = "0.0.0-test-only"
    (output / "incompatible-latest.json").write_text(json.dumps(incompatible, indent=2) + "\n", encoding="utf-8")
    (output / "README.txt").write_text(
        "Local acceptance fixtures only. The corrupt release intentionally fails SHA-256 and must never be published as a normal release.\n"
        "Its copied catalog still references the currently published resource bundles.\n"
        "Keep a backup of ServerData/latest.json and restore it after each client test.\n",
        encoding="utf-8")
    print("Prepared local failure fixtures in", output)
    print("Expected fallback version:", current["version"])


if __name__ == "__main__":
    main()

"""Stage a DLL-only release using an unchanged Addressables catalog.

Run after HybridCLR/CompileDll/Win64:
python Tools/package_code_release.py --base-release-id BASE_ID --release-id NEW_ID
"""

import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys


PROJECT = Path(__file__).resolve().parents[1]
TARGET = "StandaloneWindows64"


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-release-id", required=True)
    parser.add_argument("--release-id", required=True)
    parser.add_argument("--server-root", type=Path, default=PROJECT / "ServerData")
    parser.add_argument("--hot-update-dll", type=Path,
                        default=PROJECT / "Library/HybridCLR/HotUpdateDlls/StandaloneWindows64/Rpg.HotUpdate.dll")
    args = parser.parse_args()
    for value in (args.base_release_id, args.release_id):
        if not re.fullmatch(r"[A-Za-z0-9_-]{1,80}", value):
            parser.error("Release IDs may contain only letters, digits, _ or -")
    if args.base_release_id == args.release_id:
        parser.error("The new release ID must differ from the base release ID")

    base_record = PROJECT / "Build/ResourceRelease" / args.base_release_id / "latest.json"
    state = base_record.parent / "addressables_content_state.bin"
    if not base_record.is_file() or not state.is_file():
        parser.error("The archived base manifest and content state are required")
    manifest = json.loads(base_record.read_text(encoding="utf-8"))
    if manifest.get("version") != args.base_release_id or "hotUpdateDll" not in manifest:
        parser.error("The base release is not a compatible code release")
    if not args.hot_update_dll.is_file():
        parser.error(f"HybridCLR hot-update DLL is missing: {args.hot_update_dll}")
    if sha256(args.hot_update_dll) == manifest["hotUpdateDll"]["sha256"]:
        parser.error("The hot-update DLL is unchanged; run HybridCLR/CompileDll/Win64 after editing code")

    base_aot = []
    for entry in manifest.get("aotMetadata", []):
        path = args.server_root / entry["path"]
        if not path.is_file() or path.stat().st_size != entry["bytes"] or sha256(path) != entry["sha256"]:
            parser.error(f"The base client's AOT metadata is missing or damaged: {path}")
        base_aot.append(path)

    source = args.server_root / manifest["catalogPath"]
    if not source.is_file() or sha256(source) != manifest["catalogSha256"]:
        parser.error("The base catalog is missing or damaged")
    destination = args.server_root / "releases" / args.release_id / TARGET / source.name
    destination.parent.mkdir(parents=True, exist_ok=True)
    for old in (source, source.with_suffix(".hash")):
        if not old.is_file():
            continue
        new = destination.parent / old.name
        if new.exists() and sha256(new) != sha256(old):
            parser.error(f"New release already contains different bytes: {new}")
        if not new.exists():
            shutil.copy2(old, new)

    command = [sys.executable, str(PROJECT / "Tools/package_resource_release.py"),
               "--release-id", args.release_id,
               "--base-release-id", args.base_release_id,
               "--content-state", str(state),
               "--server-root", str(args.server_root),
               "--hot-update-dll", str(args.hot_update_dll),
               "--no-generated-aot"]
    for path in base_aot:
        command.extend(("--aot-metadata", str(path)))
    subprocess.run(command, cwd=PROJECT, check=True)
    print("Code-only release uses the unchanged catalog from", args.base_release_id)


if __name__ == "__main__":
    main()

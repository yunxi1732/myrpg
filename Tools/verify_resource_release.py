"""Verify a staged release and every file listed in its review record."""

import argparse
import hashlib
import json
from pathlib import Path
import re


PROJECT = Path(__file__).resolve().parents[1]


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--release-id", required=True)
    parser.add_argument("--server-root", type=Path, default=PROJECT / "ServerData")
    args = parser.parse_args()
    if not re.fullmatch(r"[A-Za-z0-9_-]{1,80}", args.release_id):
        parser.error("Invalid release ID")
    staged = PROJECT / "Build/ResourceRelease" / args.release_id
    manifest = json.loads((staged / "latest.json").read_text(encoding="utf-8"))
    record = json.loads((staged / "release-record.json").read_text(encoding="utf-8"))
    if manifest.get("version") != args.release_id or record.get("release") != manifest:
        raise ValueError("Release ID or manifest differs from the review record")
    files = record.get("files")
    if not isinstance(files, list) or not files:
        raise ValueError("Review record has no files")
    prefix = f"releases/{args.release_id}/StandaloneWindows64/"
    listed = {}
    for entry in files:
        path = entry["path"]
        if not path.startswith(prefix) or ".." in path or "\\" in path or path in listed:
            raise ValueError(f"Unsafe or duplicate release path: {path}")
        file = args.server_root / path
        if not file.is_file() or file.stat().st_size != entry["bytes"] or sha256(file) != entry["sha256"]:
            raise ValueError(f"Missing or damaged release file: {path}")
        listed[path] = entry
    release_dir = args.server_root / prefix
    actual_paths = {path.relative_to(args.server_root).as_posix()
                    for path in release_dir.rglob("*") if path.is_file()}
    if actual_paths != set(listed):
        raise ValueError("Release directory has unrecorded files or the record lists missing files")
    for name, field in (("addressables_content_state.bin", "contentStateSha256"),
                        ("buildlayout.json", "buildLayoutSha256")):
        archive = staged / name
        if not archive.is_file() or sha256(archive) != record[field]:
            raise ValueError(f"Archived {name} does not match the review record")
    required = [{"path": manifest["catalogPath"], "sha256": manifest["catalogSha256"]},
                manifest["hotUpdateDll"], *manifest["aotMetadata"]]
    for entry in required:
        actual = listed.get(entry["path"])
        if actual is None or actual["sha256"] != entry["sha256"] or \
                ("bytes" in entry and actual["bytes"] != entry["bytes"]):
            raise ValueError(f"Manifest file is missing from the review record: {entry['path']}")
    print(f"Verified release {args.release_id}: {len(files)} files, "
          f"1 hot DLL, {len(manifest['aotMetadata'])} AOT metadata files")


if __name__ == "__main__":
    main()

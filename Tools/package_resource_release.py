"""Stage a built Addressables release for review without publishing latest.json.

Run: python Tools/package_resource_release.py --release-id RELEASE_ID
"""

import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess


PROJECT = Path(__file__).resolve().parents[1]
TARGET = "StandaloneWindows64"


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def required_match(path, expression):
    match = re.search(expression, path.read_text(encoding="utf-8"), re.MULTILINE)
    if match is None:
        raise ValueError(f"Expected version was not found in {path}")
    return match.group(1)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--release-id", required=True)
    parser.add_argument("--server-root", type=Path, default=PROJECT / "ServerData")
    parser.add_argument("--content-state", type=Path,
                        default=PROJECT / "Assets/AddressableAssetsData/Windows/addressables_content_state.bin")
    parser.add_argument("--base-release-id", help="Release whose content state was used for a content update")
    args = parser.parse_args()
    if not re.fullmatch(r"[A-Za-z0-9_-]{1,80}", args.release_id):
        parser.error("--release-id must contain only letters, digits, _ or -")
    if args.base_release_id and not re.fullmatch(r"[A-Za-z0-9_-]{1,80}", args.base_release_id):
        parser.error("--base-release-id must contain only letters, digits, _ or -")
    if args.base_release_id:
        if args.content_state.name != "addressables_content_state.bin" or args.content_state.parent.name != args.base_release_id:
            parser.error(f"Use addressables_content_state.bin from the archived base release {args.base_release_id}")
    release = args.server_root / "releases" / args.release_id / TARGET
    if not release.is_dir():
        parser.error(f"Addressables output does not exist: {release}")
    catalogs = sorted(path for path in release.glob("catalog_*.*") if path.suffix in {".bin", ".json"})
    if len(catalogs) != 1:
        parser.error(f"Expected exactly one catalog_*.bin or catalog_*.json in {release}; found {len(catalogs)}")
    if not args.content_state.is_file():
        parser.error(f"Content state file is missing: {args.content_state}")
    build_layout = PROJECT / "Library/com.unity.addressables/buildlayout.json"
    if not build_layout.is_file():
        parser.error(f"Addressables build layout is missing: {build_layout}")

    client_version = required_match(PROJECT / "ProjectSettings/ProjectSettings.asset", r"^\s*bundleVersion:\s*(\S+)")
    protocol_version = int(required_match(PROJECT / "Assets/Scripts/Networking/Protocol/ProtocolMessages.cs",
                                          r"public const int SchemaVersion\s*=\s*(\d+);"))
    try:
        commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=PROJECT, text=True).strip()
        dirty = bool(subprocess.check_output(["git", "status", "--porcelain", "--untracked-files=normal"],
                                             cwd=PROJECT, text=True).strip())
    except (OSError, subprocess.CalledProcessError):
        commit = "unknown"
        dirty = None

    staging = PROJECT / "Build" / "ResourceRelease" / args.release_id
    staging.mkdir(parents=True, exist_ok=True)
    catalog = catalogs[0]
    manifest = {
        "version": args.release_id,
        "clientVersion": client_version,
        "protocolVersion": protocol_version,
        "catalogPath": (Path("releases") / args.release_id / TARGET / catalog.name).as_posix(),
        "catalogSha256": sha256(catalog),
    }
    files = [{"path": path.relative_to(args.server_root).as_posix(), "sha256": sha256(path),
              "bytes": path.stat().st_size} for path in sorted(release.rglob("*")) if path.is_file()]
    staged_manifest = staging / "latest.json"
    staged_record = staging / "release-record.json"
    staged_state = staging / "addressables_content_state.bin"
    if staged_manifest.is_file() and json.loads(staged_manifest.read_text(encoding="utf-8")) != manifest:
        parser.error(f"Release {args.release_id} is already staged with a different catalog")
    if staged_record.is_file() and json.loads(staged_record.read_text(encoding="utf-8"))["files"] != files:
        parser.error(f"Release {args.release_id} already has a different file list")
    if staged_state.is_file() and sha256(staged_state) != sha256(args.content_state):
        parser.error(f"Release {args.release_id} already has a different content state; refusing to overwrite it")
    staged_manifest.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    record = {"release": manifest, "baseReleaseId": args.base_release_id,
              "gitCommit": commit, "gitDirty": dirty,
              "buildLayoutSha256": sha256(build_layout), "contentStateSha256": sha256(args.content_state),
              "files": files}
    staged_record.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    if args.content_state.resolve() != staged_state.resolve():
        shutil.copy2(args.content_state, staged_state)
    shutil.copy2(build_layout, staging / "buildlayout.json")
    print(f"Staged {len(files)} files for review in {staging}")
    print("Publish manually: copy latest.json to the HTTP root only after checking every release file.")


if __name__ == "__main__":
    main()

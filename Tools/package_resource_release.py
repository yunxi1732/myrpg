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
    parser.add_argument("--hot-update-dll", type=Path,
                        default=PROJECT / "Library/HybridCLR/HotUpdateDlls/StandaloneWindows64/Rpg.HotUpdate.dll")
    parser.add_argument("--aot-metadata", type=Path, action="append", default=[],
                        help="A stripped AOT DLL for HybridCLR metadata; repeat for each required assembly")
    parser.add_argument("--no-generated-aot", action="store_true",
                        help="Use only explicit --aot-metadata files, including an intentionally empty list")
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
    if not args.hot_update_dll.is_file():
        parser.error(f"HybridCLR hot-update DLL is missing: {args.hot_update_dll}")
    if not args.aot_metadata and not args.no_generated_aot:
        references = PROJECT / "Assets/HybridCLR/Generate/AOTGenericReferences.cs"
        if not references.is_file():
            parser.error(f"Generated HybridCLR AOT reference list is missing: {references}")
        match = re.search(r"PatchedAOTAssemblyList\s*=\s*new List<string>\s*\{([^}]*)\}",
                          references.read_text(encoding="utf-8"), re.DOTALL)
        if match is None:
            parser.error(f"Could not read PatchedAOTAssemblyList from {references}")
        names = re.findall(r'"([A-Za-z0-9_.-]+\.dll)"', match.group(1))
        args.aot_metadata = [PROJECT / "Library/HybridCLR/AssembliesPostIl2CppStrip" / TARGET / name
                             for name in names]
    if any(not path.is_file() for path in args.aot_metadata):
        parser.error("Every --aot-metadata path must be an existing DLL")
    if len({path.name for path in args.aot_metadata}) != len(args.aot_metadata):
        parser.error("AOT metadata filenames must be unique")

    client_version = required_match(PROJECT / "ProjectSettings/ProjectSettings.asset", r"^\s*bundleVersion:\s*(\S+)")
    protocol_version = int(required_match(PROJECT / "Assets/Scripts/Bootstrap/ProtocolSchema.cs",
                                          r"public const int SchemaVersion\s*=\s*(\d+);"))
    script_assembly_version = int(required_match(PROJECT / "Assets/Scripts/Bootstrap/ResourceUpdateController.cs",
                                                 r"public const int ScriptAssemblyVersion\s*=\s*(\d+);"))
    if script_assembly_version >= 1 and client_version == "1.0":
        parser.error("Script assembly layout changed; set a new Player Settings Version before packaging resources")
    if args.base_release_id:
        base_manifest = args.content_state.with_name("latest.json")
        if not base_manifest.is_file():
            parser.error(f"Archived base release manifest is missing: {base_manifest}")
        base = json.loads(base_manifest.read_text(encoding="utf-8"))
        if base.get("version") != args.base_release_id or base.get("clientVersion") != client_version or \
                base.get("protocolVersion") != protocol_version or \
                base.get("scriptAssemblyVersion") != script_assembly_version:
            parser.error("Base content state belongs to an incompatible client, protocol, or script assembly layout")

    def copy_immutable(source, destination):
        destination.parent.mkdir(parents=True, exist_ok=True)
        if destination.exists():
            if sha256(source) != sha256(destination):
                parser.error(f"Release file already exists with different bytes: {destination}")
        else:
            shutil.copy2(source, destination)

    code_dir = release / "code"
    hot_dll = code_dir / "Rpg.HotUpdate.dll"
    copy_immutable(args.hot_update_dll, hot_dll)
    aot_files = []
    for source in args.aot_metadata:
        target = code_dir / "aot" / source.name
        copy_immutable(source, target)
        aot_files.append(target)

    def release_file(path):
        return {"path": path.relative_to(args.server_root).as_posix(),
                "sha256": sha256(path), "bytes": path.stat().st_size}
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
        "scriptAssemblyVersion": script_assembly_version,
        "catalogPath": (Path("releases") / args.release_id / TARGET / catalog.name).as_posix(),
        "catalogSha256": sha256(catalog),
        "hotUpdateDll": release_file(hot_dll),
        "aotMetadata": [release_file(path) for path in sorted(aot_files)],
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

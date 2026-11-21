"""Inventory project assets and serialized dependencies of both player scenes.

Run: python Tools/resource_inventory.py
The CSV is an audit aid; Unity AssetDatabase.GetDependencies remains authoritative.
"""

from collections import Counter, deque
from pathlib import Path
import csv
import re


PROJECT = Path(__file__).resolve().parents[1]
ASSETS = PROJECT / "Assets"
SCENES = {"menu": ASSETS / "Scenes/MainMenu.unity", "game": ASSETS / "Scenes/MainScene.unity"}
GUID = re.compile(r"\bguid: ([0-9a-f]{32})\b")
OUTPUT = PROJECT / "Docs/HotUpdateResourceInventory.csv"


def reachable(root, by_guid):
    visited = set()
    unresolved = set()
    pending = deque([root])
    while pending:
        asset = pending.popleft()
        if asset in visited or not asset.is_file():
            continue
        visited.add(asset)
        try:
            source = asset.read_text(encoding="utf-8")
        except (UnicodeError, OSError):
            continue
        for guid in GUID.findall(source):
            dependency = by_guid.get(guid)
            if dependency is None:
                unresolved.add(guid)
            elif dependency not in visited and dependency.is_file():
                pending.append(dependency)
    return visited, unresolved


def main():
    assets = sorted(p for p in ASSETS.rglob("*") if p.is_file() and p.suffix != ".meta")
    by_guid = {}
    for meta in ASSETS.rglob("*.meta"):
        match = GUID.search(meta.read_text(encoding="utf-8", errors="replace"))
        if match:
            by_guid[match.group(1)] = Path(str(meta)[:-5])
    menu, menu_missing = reachable(SCENES["menu"], by_guid)
    game, game_missing = reachable(SCENES["game"], by_guid)

    with OUTPUT.open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(("path", "extension", "serialized_reachability", "planned_location"))
        for asset in assets:
            path = asset.relative_to(PROJECT).as_posix()
            if path.startswith("Assets/AddressableAssetsData/"):
                category, location = "addressables_editor_config", "not_published"
            elif path.startswith("Assets/Editor/"):
                category, location = "editor_only", "not_published"
            elif asset.suffix == ".cs":
                category, location = "code", "client_assembly"
            elif asset in menu and asset in game:
                category, location = "menu_and_game", "menu_client_and_game_remote"
            elif asset in menu:
                category, location = "menu", "client"
            elif asset in game:
                category, location = "game", "remote"
            else:
                category, location = "not_reached_from_player_scenes", "review_not_published"
            if "/Resources/" in path:
                location = "client_resources"
            writer.writerow((path, asset.suffix.lower(), category, location))

    counts = Counter()
    with OUTPUT.open(encoding="utf-8-sig", newline="") as handle:
        for row in csv.DictReader(handle):
            counts[row["serialized_reachability"]] += 1
    print(f"Wrote {OUTPUT.relative_to(PROJECT)}: {len(assets)} assets")
    for name, count in sorted(counts.items()):
        print(f"  {name}: {count}")
    print(f"Unresolved GUIDs: {len(menu_missing | game_missing)} (includes Unity/package builtins)")


if __name__ == "__main__":
    main()

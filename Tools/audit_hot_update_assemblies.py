"""Regenerate the runtime script migration inventory from source and Unity GUIDs."""

import csv
from pathlib import Path
import re


PROJECT = Path(__file__).resolve().parents[1]
ASSETS = PROJECT / "Assets"
SCRIPTS = ASSETS / "Scripts"
BOOTSTRAP = SCRIPTS / "Bootstrap"
OUTPUT = PROJECT / "Docs/HotUpdateAssemblyInventory.csv"

BOOTSTRAP_REASONS = {
    "AudioManager.cs": "Component serialized in the base menu and existing resource assets; delegates behavior to the hot runtime",
    "BasePanel.cs": "Stable panel view base type used across the built-in menu and remote UI",
    "CodeUpdateLoader.cs": "Downloads, verifies, and loads the hot DLL before gameplay code exists",
    "GameData.cs": "Stable save-data contract consumed by the built-in SaveManager component",
    "HotUpdateScenes.cs": "Stable Addressables scene entry used before and after hot code registration",
    "IBasePanel.cs": "Stable panel contract shared by built-in and remote UI",
    "IMainMenuRuntime.cs": "Stable menu runtime contract for the built-in MainMenu component",
    "ISaveManager.cs": "Stable save component contract",
    "ISaveManagerRuntime.cs": "Stable save runtime contract between the built-in component and hot logic",
    "MainMenuRuntimeRegistry.cs": "Stable factory registered after the hot DLL loads",
    "ProtocolSchema.cs": "Client protocol compatibility gate needed before joining a room",
    "ResourceUpdateController.cs": "Built-in startup, resource version selection, retry, and rollback UI",
    "RoomPanel.cs": "Component serialized in the built-in MainMenu scene; display state only",
    "SaveManager.cs": "Component serialized in existing assets; delegates save behavior to the hot runtime",
    "SaveManagerRuntimeRegistry.cs": "Stable factory registered after the hot DLL loads",
    "SerializableDictionary.cs": "Serialized save-data field type used by built-in GameData",
    "UI_MainMenu.cs": "Built-in MainMenu component that waits for code and resources",
}


def main():
    if not (SCRIPTS / "Rpg.HotUpdate.asmdef").is_file() or not (BOOTSTRAP / "Rpg.Bootstrap.asmdef").is_file():
        raise SystemExit("Hot-update and Bootstrap assembly definitions are required")
    assets = [path for path in ASSETS.rglob("*")
              if path.suffix in {".unity", ".prefab", ".asset"} and "_Recovery" not in path.parts]
    serialized = "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in assets)
    rows = []
    for script in sorted(ASSETS.rglob("*.cs")):
        relative = script.relative_to(PROJECT).as_posix()
        if (ASSETS / "Editor") in script.parents:
            continue
        if BOOTSTRAP in script.parents:
            assembly = "Rpg.Bootstrap"
            reason = BOOTSTRAP_REASONS.get(script.name)
            if reason is None:
                raise SystemExit(f"Bootstrap script needs a documented reason: {relative}")
        elif SCRIPTS in script.parents:
            assembly = "Rpg.HotUpdate"
            reason = "Gameplay logic, UI flow, networking, or component loaded after the DLL"
        elif relative == "Assets/HybridCLR/Generate/AOTGenericReferences.cs":
            assembly = "Assembly-CSharp"
            reason = "HybridCLR generated AOT generic reference list compiled into the client"
        else:
            raise SystemExit(f"Unclassified runtime script: {relative}")
        meta = Path(str(script) + ".meta")
        if not meta.is_file():
            raise SystemExit(f"Script metadata is missing: {relative}")
        match = re.search(r"^guid: ([0-9a-f]{32})$", meta.read_text(encoding="utf-8"), re.MULTILINE)
        if match is None:
            raise SystemExit(f"Script GUID is missing: {relative}")
        rows.append((relative, assembly, match.group(1), serialized.count("guid: " + match.group(1)), reason))
    with OUTPUT.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(("script", "assembly", "guid", "serialized_asset_references", "reason"))
        writer.writerows(rows)
    counts = {name: sum(row[1] == name for row in rows) for name in ("Rpg.Bootstrap", "Rpg.HotUpdate", "Assembly-CSharp")}
    print("Wrote", len(rows), "runtime scripts to", OUTPUT.relative_to(PROJECT))
    print(counts)


if __name__ == "__main__":
    main()

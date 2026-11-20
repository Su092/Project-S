"""Validate a music weapon delivery and stage only engine inputs. Never writes Assets."""
import argparse
import hashlib
import json
import shutil
from pathlib import Path

EXPECTED = {
    "SW01_Violin": "rifle", "SW02_Kalimba": "smg", "SW03_FrameDrum": "shotgun",
    "SW04_Cello": "sniper", "SW05_Harp": "beam", "SW06_Accordion": "launcher",
}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path, help="MusicWeapons delivery directory containing six SWxx folders")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    destination = root / "_Inbox/weapons/MusicWeapons_v001"
    manifests, selected = [], []
    for asset_id, gun_id in EXPECTED.items():
        folder = (args.source / asset_id).resolve()
        manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8-sig"))
        if manifest["assetId"] != asset_id or manifest["gunId"] != gun_id:
            raise ValueError("Unexpected asset mapping: " + asset_id)
        hashes = {x["path"]: x["sha256"] for x in manifest["inputs"]}
        files = ["Exports/" + asset_id + "_Model.fbx"]
        files += [x["file"] for x in manifest["animations"]]
        files += [x["file"] for x in manifest["textures"]]
        if len(files) != len(set(files)):
            raise ValueError("Duplicate input path: " + asset_id)
        for relative in files:
            path = (folder / relative).resolve()
            if Path(relative).is_absolute() or ".." in Path(relative).parts or not path.is_relative_to(folder):
                raise ValueError("Unsafe input path: " + relative)
            if path.suffix.lower() not in (".fbx", ".png"):
                raise ValueError("Unexpected input type: " + relative)
            actual = hashlib.sha256(path.read_bytes()).hexdigest()
            if actual != hashes.get(relative):
                raise ValueError("Hash mismatch: " + str(path))
            selected.append((path, destination / asset_id / relative))
        manifests.append(manifest)
        selected.append((folder / "manifest.json", destination / asset_id / "manifest.json"))
        if (folder / "validation.md").exists():
            selected.append((folder / "validation.md", destination / asset_id / "validation.md"))
    # Only mutate staging once every asset has passed.
    if not args.dry_run:
        for source, target in selected:
            target.parent.mkdir(parents=True, exist_ok=True)
            if source != target.resolve():
                shutil.copy2(source, target)
        (destination / "import-plan.json").write_text(
            json.dumps({"assets": manifests}, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"status": "validated" if args.dry_run else "staged", "assets": len(manifests),
                      "animations": sum(len(m["animations"]) for m in manifests),
                      "textures": sum(len(m["textures"]) for m in manifests)}, ensure_ascii=False))

if __name__ == "__main__":
    main()

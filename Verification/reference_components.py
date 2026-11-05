"""Maintain private per-screenshot component maps without guessing reconstruction data."""

import argparse
import hashlib
import json
from pathlib import Path
import struct
import sys


REPO = Path(__file__).resolve().parents[1]
UNKNOWN = "unverified"
COMPONENT_FIELDS = (
    "componentId", "assetKey", "sourceAtlas", "sourceRect", "destinationRect",
    "origin", "pivot", "stretchMode", "clipRect", "zIndex", "fontId", "text",
    "lineBreaks", "alignment", "spacing", "shadow", "visibleWhen", "interactionId",
    "animationId", "verification",
)


def unresolved_component():
    return dict.fromkeys(COMPONENT_FIELDS, UNKNOWN)


def screenshot_identity(path):
    data = Path(path).read_bytes()
    if len(data) < 24 or data[:16] != b"\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR":
        raise ValueError("Reference screenshot must have a PNG IHDR header")
    if struct.unpack(">II", data[16:24]) != (480, 272):
        raise ValueError("Reference viewport must be native 480x272; no automatic crop or rescale")
    return hashlib.sha256(data).hexdigest()


def new_map(path, evidence_root):
    relative = Path(path).resolve().relative_to(Path(evidence_root).resolve()).as_posix()
    return {
        "schemaVersion": 1,
        "screen": UNKNOWN,
        "state": UNKNOWN,
        "referenceSha256": screenshot_identity(path),
        "viewport": [480, 272],
        "reference": "private:" + relative,
        "attemptedScenario": Path(relative).parent.as_posix(),
        "componentInventoryComplete": False,
        "components": [unresolved_component()],
        "verification": UNKNOWN,
        "notes": ["Pending component inventory; the placeholder is not a measured screen component.",
                  "Scenario filenames are provenance, not verified screen/state identities."],
    }


def validate_map(mapping, screenshot):
    for field in ("screen", "state", "referenceSha256", "viewport", "components", "verification", "componentInventoryComplete"):
        if field not in mapping:
            raise ValueError(f"Component map is missing {field}")
    if mapping["referenceSha256"] != screenshot_identity(screenshot):
        raise ValueError("Reference SHA256 changed; preserve the existing map and review the changed evidence")
    if mapping["viewport"] != [480, 272]:
        raise ValueError("Component map viewport must be [480, 272]")
    if not isinstance(mapping["components"], list) or not mapping["components"]:
        raise ValueError("Use an explicit unverified placeholder until components are inventoried")
    for component in mapping["components"]:
        missing = set(COMPONENT_FIELDS) - component.keys()
        if missing:
            raise ValueError("Component is missing fields: " + ", ".join(sorted(missing)))
    if mapping["componentInventoryComplete"] and any(c["componentId"] == UNKNOWN for c in mapping["components"]):
        raise ValueError("An unresolved component placeholder cannot complete the inventory")


def private_directory(path):
    resolved = Path(path).resolve()
    if resolved.is_relative_to(REPO.resolve()):
        raise ValueError("Original reference maps and screenshots must stay outside the repository")
    return resolved


def maintain_maps(evidence_root, output_root=None, check=False):
    evidence_root = private_directory(evidence_root)
    output_root = private_directory(output_root or evidence_root / "component-maps")
    screenshots = sorted(evidence_root.rglob("render.png"))
    if not screenshots:
        raise ValueError("No retained scenario screenshots named render.png found")
    counts = {"screenshots": len(screenshots), "created": 0, "preserved": 0, "pendingInventories": 0}
    for screenshot in screenshots:
        private_directory(screenshot)
        relative = screenshot.relative_to(evidence_root)
        target = output_root / relative.with_suffix(".components.json")
        private_directory(target)
        if target.exists():
            mapping = json.loads(target.read_text(encoding="utf-8"))
            counts["preserved"] += 1
        else:
            if check:
                raise ValueError(f"Missing component map for {relative.as_posix()}")
            mapping = new_map(screenshot, evidence_root)
            target.parent.mkdir(parents=True, exist_ok=True)
            with target.open("x", encoding="utf-8") as output:
                json.dump(mapping, output, indent=2, ensure_ascii=False)
                output.write("\n")
            counts["created"] += 1
        validate_map(mapping, screenshot)
        counts["pendingInventories"] += not mapping["componentInventoryComplete"]
    return counts


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("evidence_root", type=Path, help="private original evidence directory outside the repository")
    parser.add_argument("--output-root", type=Path)
    parser.add_argument("--check", action="store_true", help="check coverage, required fields and hashes without writing")
    args = parser.parse_args(argv)
    try:
        counts = maintain_maps(args.evidence_root, args.output_root, args.check)
        print(json.dumps(counts))
        print("Map integrity checked; unknowns remain unverified. This does not pass the Phase 1 gate.")
        return 0
    except (OSError, ValueError, TypeError, KeyError) as error:
        print(f"Component map check failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())

"""Private two-decoder comparison. Agreement never implies PSP verification.

Inputs: the retained GIM parity audit and source-bound extraction ledger.
Writes metadata only, never images. Run with --help for paths.
"""
import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image


def sha(data):
    return hashlib.sha256(data).hexdigest()


def compare(left, right, dimensions_left, dimensions_right):
    for data, (width, height) in ((left, dimensions_left), (right, dimensions_right)):
        if width <= 0 or height <= 0 or len(data) != width * height * 4:
            raise ValueError("Invalid tightly packed RGBA dimensions")
    same_size = dimensions_left == dimensions_right
    width, height = dimensions_left
    # Tile boundaries and partial tiles, as well as corners and center.
    xs = sorted({x for x in (0, 7, 8, 15, 16, width // 2, width - 1) if x < width})
    ys = sorted({y for y in (0, 7, 8, 15, 16, height // 2, height - 1) if y < height})
    samples = []
    for y in ys:
        for x in xs:
            at = (y * width + x) * 4
            samples.append(dict(x=x, y=y, left=list(left[at:at + 4]),
                                right=list(right[at:at + 4]) if same_size else None))
    equal = same_size and left == right
    return dict(dimensions=[dimensions_left, dimensions_right],
                pixelSha256=[sha(left), sha(right)], alphaSha256=[sha(left[3::4]), sha(right[3::4])],
                differingChannels=[sum(a != b for a, b in zip(left[c::4], right[c::4]))
                                   for c in range(4)] if same_size else None,
                samples=samples, result="decoder-agreement" if equal else "rejected-disagreement",
                paletteInterpolation="unverified", ppssppRuntimeComparison="unverified",
                referenceVerification="unverified")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--audit-root", required=True, type=Path)
    parser.add_argument("--ledger", required=True, type=Path)
    parser.add_argument("--output-root", required=True, type=Path)
    args = parser.parse_args()
    root = args.audit_root.resolve()
    out = args.output_root.resolve()
    # Both outputs and original-media inputs must stay outside Git worktrees.
    for path in (root, args.ledger.resolve(), out):
        if any((parent / ".git").exists() for parent in (path, *path.parents)):
            raise ValueError("Private evidence must remain outside Git worktrees")
    inputs = json.loads((root / "inputs.json").read_text(encoding="utf-8"))
    lookup = {r["relative"]: r for r in inputs["inputs"]}
    origins = {}
    with args.ledger.open(encoding="utf-8") as handle:
        for line in handle:
            row = json.loads(line)
            digest = row.get("stored_sha256")
            if digest:
                origins.setdefault(digest, []).append(row["origin"])
    managed = json.loads((root / "managed-palette0/results.json").read_text(encoding="utf-8"))
    rows = []
    for row in managed:
        if row["status"] != "decoded":
            rows.append(dict(relative=row["relative"], result="not-compared", diagnostic=row))
            continue
        source = lookup[row["relative"]]
        source_bytes = Path(source["path"]).read_bytes()
        png_bytes = Path(source["png"]).read_bytes()
        rgba = (root / "managed-palette0" / row["rgbaFile"]).read_bytes()
        if (sha(source_bytes) != source["sha256"] or sha(png_bytes) != source["pngSha256"]
                or sha(rgba) != row["rgbaSha256"]):
            raise ValueError("Retained comparison input changed: " + row["relative"])
        with Image.open(source["png"]) as image:
            size = list(image.size)
            reference = image.convert("RGBA").tobytes()
        rows.append(dict(relative=row["relative"], sourceSha256=source["sha256"],
                         sourceOrigins=origins.get(source["sha256"], []),
                         imageIndex=0, frameIndex=0, mipLevel=0, paletteLevel=0,
                         swizzled=row["Swizzled"], format=row["Format"],
                         **compare(rgba, reference, [row["Width"], row["Height"]], size)))
    summary = dict(compared=sum(r["result"] != "not-compared" for r in rows),
                   disagreements=sum(r["result"] == "rejected-disagreement" for r in rows),
                   missingSourceOrigins=sum(not r.get("sourceOrigins") for r in rows if "sourceSha256" in r),
                   reportedNarrowSwizzleCandidates=19, candidateIdentities="unverified",
                   candidatesResolved=0, referenceVerified=0,
                   note="The reported 19 candidates have no located identity list; not equated with the full discrepancy count.")
    out.mkdir(parents=True, exist_ok=False)
    with (out / "comparison.jsonl").open("x", encoding="utf-8") as handle:
        for row in rows:
            handle.write(json.dumps(row, sort_keys=True) + "\n")
    (out / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(json.dumps(summary))


if __name__ == "__main__":
    main()

"""Verify a public projection against private historical getter evidence.

This checks file identities and selected recorded fields. It does not replay
PPSSPP, authenticate the historical capture, or certify native machine code.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys


class VerificationError(ValueError):
    pass


def _relative_file(root, name):
    if not isinstance(name, str) or not name or "\\" in name or ":" in name:
        raise VerificationError("Evidence path must be a portable relative path")
    relative = Path(name)
    if relative.is_absolute() or ".." in relative.parts:
        raise VerificationError("Evidence path must stay within the repository")
    candidate = (root / relative).resolve()
    if not candidate.is_relative_to(root):
        raise VerificationError("Evidence path escapes the repository")
    return candidate


def _hashed_file(root, name, expected):
    data = _relative_file(root, name).read_bytes()
    actual = hashlib.sha256(data).hexdigest()
    if not isinstance(expected, str) or expected != actual:
        raise VerificationError(f"SHA-256 mismatch: {name}")
    return data


def _unique_rows(rows, key):
    result = {}
    for row in rows:
        identity = row[key]
        if type(identity) is not int or not 0 <= identity <= 0xFFFFFFFF:
            raise VerificationError(f"Invalid unsigned {key}")
        if identity in result:
            raise VerificationError(f"Duplicate {key}: {identity}")
        result[identity] = row
    return result


def verify(root, fixture_path="config/reference/card-attack.json"):
    root = Path(root).resolve()
    fixture = json.loads(_relative_file(root, fixture_path).read_bytes())
    if fixture["schemaVersion"] != 1 or fixture["module"] != "EBOOT.BIN":
        raise VerificationError("Unsupported fixture schema or module")
    if fixture["functionAddress"] != "0x0881F334":
        raise VerificationError("Unsupported getter address")
    capture_info, input_info = fixture["capture"], fixture["packedInputs"]
    capture = json.loads(_hashed_file(root, capture_info["path"], capture_info["sha256"]))
    inputs = json.loads(_hashed_file(root, input_info["path"], input_info["sha256"]))
    reproduction = fixture["reproduction"]
    _hashed_file(root, reproduction["localCaptureScript"], reproduction["scriptSha256"])
    if capture["binary_sha256"] != fixture["sourceSha256"]:
        raise VerificationError("Capture module identity mismatch")
    if (inputs["sourceSha256"] != input_info["sourceSha256"]
            or inputs["sourceName"] != input_info["sourceName"]):
        raise VerificationError("Packed-input archive identity mismatch")
    if inputs.get("sourceSha256After", inputs["sourceSha256"]) != inputs["sourceSha256"]:
        raise VerificationError("Packed-input archive changed during extraction")
    observed = _unique_rows(capture["samples"], "card_id")
    packed = _unique_rows(inputs["index"]["rows"], "cardId")
    samples = _unique_rows(fixture["samples"], "cardId")
    if not samples:
        raise VerificationError("Fixture has no projected samples")
    for identity, sample in samples.items():
        if identity not in observed or identity not in packed:
            raise VerificationError(f"Missing historical sample: {identity}")
        runtime = observed[identity]["runtime"]["attack"]
        word = packed[identity]["rawPropertyWord0"]
        projected_runtime, projected_word = sample["runtimeV0"], sample["rawPropertyWord0"]
        if (any(type(value) is not int or not 0 <= value <= 0xFFFFFFFF
                for value in (runtime, word, projected_runtime, projected_word))
                or runtime != projected_runtime or word != projected_word):
            raise VerificationError(f"Historical projection mismatch: {identity}")
    return {
        "status": "pass",
        "scope": "Historical recorded projection only; no fresh capture, authenticity attestation or native match.",
        "samples": len(samples),
        "verifiedProvenanceFiles": 3,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--fixture", default="config/reference/card-attack.json")
    args = parser.parse_args()
    try:
        report = verify(args.root, args.fixture)
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f"Getter fixture verification failed: {error}", file=sys.stderr)
        return 1
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

"""Index private extraction evidence without converting media or granting runtime approval."""
import argparse
import collections
import gzip
import hashlib
import json
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import wave
from datetime import datetime, timezone

REPO = Path(__file__).resolve().parents[1]
UNKNOWN = "unverified"
STAGES = ("raw", "decoded", "classified", "mapped", "runtime_approved", "reference_verified")


def digest(data):
    return hashlib.sha256(data).hexdigest()


def file_hash(path):
    h = hashlib.sha256()
    with Path(path).open("rb") as source:
        for data in iter(lambda: source.read(1024 * 1024), b""):
            h.update(data)
    return h.hexdigest()


def range_hash(source, offset, length):
    source.seek(0, 2)
    if offset < 0 or length < 0 or offset > source.tell() or length > source.tell() - offset:
        raise ValueError("Raw source extent exceeds verified image bounds")
    source.seek(offset)
    h = hashlib.sha256()
    while length:
        block = source.read(min(length, 1024 * 1024))
        if not block:
            raise ValueError("Truncated raw source extent")
        h.update(block)
        length -= len(block)
    return h.hexdigest()


def private_path(path, root=None):
    path = Path(path).resolve()
    if path.is_relative_to(REPO.resolve()) or root is not None and not path.is_relative_to(root):
        raise ValueError("Original extraction records and outputs must stay inside the private root, outside the repository")
    if any((parent / ".git").exists() for parent in (path, *path.parents)):
        raise ValueError("Private ledger paths must stay outside Git worktrees")
    return path


def classify(prefix, name):
    """Signatures classify containers; filename-only native model/font labels remain candidates."""
    suffix = Path(name).suffix.lower()
    if prefix.startswith(b"\x1f\x8b"):
        return "gzip", "classified", "gzip-signature"
    if prefix.startswith(b"EHP\x03"):
        return "ehp", "classified", "ehp-v3-signature"
    if prefix.startswith(b"MIG.00.1PSP\0"):
        return "gim", "classified", "psp-gim-signature"
    for signature, family in ((b"CPJ\x1a", "card-artwork-cpj"), (b"CIP\x1a", "card-thumbnails"),
                              (b"CPM\x1a", "card-field-icons"), (b"AFS2", "adx2-wave-bank"),
                              (b"PSMF", "psmf-movie"), (b"HCA\0", "hca"), (b"\x89PNG\r\n\x1a\n", "png")):
        if prefix.startswith(signature):
            return family, "classified", "binary-signature"
    if prefix.startswith(b"@UTF"):
        return ("adx2-cue-bank" if suffix == ".acb" else "cri-utf"), "classified", "utf-signature"
    if suffix in (".tms", ".tmt", ".tma", ".gmo"):
        return "model-animation-candidate", "candidate", "filename-only; native format/conversion unverified"
    if suffix in (".pgf", ".fnt", ".ttf", ".otf", ".bwfon"):
        return "font-candidate", "candidate", "filename-only; original glyph source unverified"
    if suffix in (".at3", ".at9", ".vag", ".adx", ".ac3", ".wav", ".ogg"):
        return "other-audio-candidate", "candidate", "filename-only; codec not inferred"
    if "_voiceidchgtbl.bin" in name:
        return "character-voice-index", "candidate", "filename-only until bounded table evidence"
    if name.startswith("strTbl_") or name.startswith(("EveI", "EveL", "SysMsgI", "SysMsgL")):
        return "text-table-candidate", "candidate", "filename-only until bounded table evidence"
    return "native-binary-unresolved", UNKNOWN, "format-not-established"


def new_record(origin, family, classification, basis, raw_ref=None, sha256=None):
    identity = digest(json.dumps(origin, sort_keys=True, separators=(",", ":")).encode())
    stages = {stage: {"state": UNKNOWN, "evidence": []} for stage in STAGES}
    stages["raw"] = {"state": "retained" if raw_ref else UNKNOWN, "evidence": [raw_ref] if raw_ref else []}
    stages["classified"] = {"state": classification, "evidence": [raw_ref] if raw_ref else []}
    return dict(schema_version=1, record_id=identity, origin=origin, family=family,
                classification_basis=basis, stored_sha256=sha256, evidence=stages,
                outputs=[], details={"screen_usage": UNKNOWN}, unknowns=[])


def unknown_paths(value, prefix=""):
    if isinstance(value, dict):
        return [p for key, item in value.items() if key != "unknowns"
                for p in unknown_paths(item, prefix + "/" + key)]
    if isinstance(value, list):
        return [p for i, item in enumerate(value) for p in unknown_paths(item, prefix + "/" + str(i))]
    return [prefix] if value == UNKNOWN else []


def validate_record(record):
    if set(record["evidence"]) != set(STAGES):
        raise ValueError("Every record requires separate evidence stages")
    # This collector has no approval workflow. Package integrity/imported media never imply approval.
    for stage in ("mapped", "runtime_approved", "reference_verified"):
        if record["evidence"][stage]["state"] != UNKNOWN:
            raise ValueError("Ledger collection cannot promote mapping, runtime or reference approval")
    if record["unknowns"] != sorted(unknown_paths(record)):
        raise ValueError("Every unresolved field must be enumerated")


class Ledger:
    def __init__(self, root):
        self.root = private_path(root)
        self.records, self.exceptions, self.provenance = [], [], {}
        self.hashes = {}
        self.package_context = None
        self.iso_stream = None

    def path(self, relative):
        return private_path(self.root / relative, self.root)

    def ref(self, relative, selector=""):
        return "private:" + Path(relative).as_posix() + ("#" + selector if selector else "")

    def hashed(self, relative):
        path = self.path(relative)
        if str(path) not in self.hashes:
            self.hashes[str(path)] = file_hash(path)
        return self.hashes[str(path)]

    def load(self, relative):
        self.provenance[relative] = self.hashed(relative)
        return json.loads(self.path(relative).read_text(encoding="utf-8"))

    def issue(self, record, code, detail, fields=None):
        rid = record["record_id"] if record else None
        fields = sorted(fields or [])
        self.exceptions.append(dict(exception_id=digest(json.dumps([rid, code, fields], sort_keys=True).encode()),
                                    record_id=rid, code=code, fields=fields, detail=detail))

    def output(self, record, relative, expected, kind):
        try:
            actual = self.hashed(relative)
            if expected is not None and actual != expected:
                raise ValueError("Output hash differs from its audit")
            record["outputs"].append(dict(path=self.ref(relative), sha256=actual, kind=kind))
            return True
        except (ValueError, OSError) as error:
            self.issue(record, "output-missing-or-changed", str(error))
            return False

    def add(self, record):
        record["unknowns"] = sorted(unknown_paths(record))
        validate_record(record)
        if record["unknowns"]:
            self.issue(record, "unverified-fields", "No evidence promotion; listed fields remain unresolved", record["unknowns"])
        self.records.append(record)

    def collect(self):
        base = "extracted/dual-strategy-20261104"
        raw_rows = self.load(base + "/managed/raw-files.json")
        ehp = self.load("extracted/ehp-coverage-20261104-bounded/members.json")
        members = {(r["archive"], r["Name"]): dict(r, storedSha256=r["storedSha256"]) for r in ehp}
        for r in self.load("dialogue-characters-20261104/members.json"):
            key = (r["archive"], r["Name"])
            if key in members and members[key]["storedSha256"] != r["sha256"]:
                raise ValueError("Member audit identities disagree")
            members[key] = dict(r, storedSha256=r["sha256"])
        inputs = self.load("extracted/gim-parity-20261104/inputs.json")["inputs"]
        gim_inputs = {r["relative"]: r for r in inputs}
        gim_by_hash = {}
        for r in self.load("extracted/gim-parity-20261104/comparison-palette0/pixel-comparison.json"):
            key = gim_inputs[r["relative"]]["sha256"]
            if key not in gim_by_hash or r["status"] == "decoded":
                gim_by_hash[key] = r
        archive_notes = {r["path"]: r["Diagnostics"] for r in self.load("extracted/ehp-coverage-20261104-bounded/archives.json")}
        for r in self.load("dialogue-characters-20261104/archives.json"):
            archive_notes[r["path"]] = r["Diagnostics"]

        def decorate(record):
            family = record["family"]
            if family == "gim":
                record["details"].update(native_dimensions=UNKNOWN, source_rect=UNKNOWN, anchor_pivot=UNKNOWN,
                    nine_slice=UNKNOWN, alpha_policy="preserve-source", multiple_surfaces=UNKNOWN)
                audit = gim_by_hash.get(record["stored_sha256"])
                if audit and audit["status"] == "decoded":
                    relative = "extracted/gim-parity-20261104/managed-palette0/" + audit["rgbaFile"]
                    if self.output(record, relative, audit["rgbaSha256"], "straight-rgba"):
                        record["evidence"]["decoded"] = {"state": "decoded", "evidence": [self.ref("extracted/gim-parity-20261104/comparison-palette0/pixel-comparison.json")]}
                        record["details"].update(native_dimensions=[audit["Width"], audit["Height"]],
                            swizzled=audit["Swizzled"], palette_level=0, picture_frame_level="0/0/0 only",
                            toolkit_pixel_comparison="equal" if audit["equal"] else "mismatch", decoder_diagnostics=audit["Diagnostics"])
                        if not audit["equal"]:
                            self.issue(record, "gim-pixel-discrepancy", "Exact RGBA comparison differs; no normalization to force agreement")
                        for note in audit["Diagnostics"]:
                            self.issue(record, "gim-decoder-diagnostic", note)
                elif audit:
                    record["evidence"]["decoded"]["state"] = "rejected" if audit["status"] == "rejected" else UNKNOWN
                    self.issue(record, "gim-not-decoded", audit.get("diagnostic", audit["status"]))
            elif family == "model-animation-candidate":
                record["details"].update(mesh=UNKNOWN, skeleton=UNKNOWN, animation_clips=UNKNOWN)
                self.issue(record, "native-model-conversion-unresolved", "TMS/TMT/TMA/GMO candidates are not converted by image extraction")
            elif family == "font-candidate":
                record["details"].update(glyph_map=UNKNOWN, glyph_metrics=UNKNOWN, japanese_coverage=UNKNOWN, english_patch_coverage=UNKNOWN)
            elif family in ("ehp", "gzip"):
                record["details"]["nested_member_inventory"] = UNKNOWN

        for row in raw_rows:
            relative = base + "/managed/raw/" + row["path"]
            if self.path(relative).stat().st_size != row["length"] or self.hashed(relative) != row["sha256"]:
                raise ValueError("Raw file no longer matches the retained managed inventory")
            if range_hash(self.iso_stream, row["offset"], row["length"]) != row["sha256"]:
                raise ValueError("Retained raw file differs from the verified ISO extent")
            with self.path(relative).open("rb") as source:
                prefix = source.read(64)
            family, state, basis = classify(prefix, Path(row["path"]).name)
            record = new_record(dict(kind="iso-file", source_path=row["path"], offset=row["offset"], length=row["length"]),
                                family, state, basis, self.ref(relative), row["sha256"])
            if row["path"] in archive_notes:
                record["details"]["archive_diagnostics"] = archive_notes[row["path"]]
                for note in archive_notes[row["path"]]:
                    self.issue(record, "archive-diagnostic", note)
            decorate(record)
            if family == "gzip":
                try:
                    with gzip.open(self.path(relative), "rb") as stream:
                        contained = stream.read(64)
                    inner, _, _ = classify(contained, Path(row["path"]).name[:-3])
                    record["details"]["decompressed_prefix_family"] = inner
                except (OSError, EOFError) as error:
                    self.issue(record, "gzip-prefix-unreadable", str(error))
                self.issue(record, "gzip-payload-inventory-unverified", "Only a bounded signature prefix inspected; full gzip/nested member decoding not established")
            self.add(record)

        # Read member bytes only inside already hash-checked source archives; no outputs are generated.
        archive_path = None
        stream = None
        try:
            for (archive, name), row in sorted(members.items()):
                if archive != archive_path:
                    if stream:
                        stream.close()
                    stream = self.path(base + "/managed/raw/" + archive).open("rb")
                    archive_path = archive
                stream.seek(row["Offset"])
                data = stream.read(row["Length"])
                if len(data) != row["Length"] or digest(data) != row["storedSha256"]:
                    raise ValueError("Member range differs from retained managed evidence")
                family, state, basis = classify(data[:64], name)
                record = new_record(dict(kind="ehp-member", source_path=archive, member=name, offset=row["Offset"], length=row["Length"]),
                                    family, state, basis, self.ref(base + "/managed/raw/" + archive), row["storedSha256"])
                decorate(record)
                self.add(record)
        finally:
            if stream:
                stream.close()
        for archive in self.load("dialogue-characters-20261104/archives.json"):
            for row in archive["rejected"]:
                record = new_record(dict(kind="ehp-member", source_path=archive["path"], member=row["Name"], offset=row["Offset"], length=row["Length"]),
                                    "native-binary-unresolved", UNKNOWN, "declared range rejected")
                record["evidence"]["raw"]["state"] = "rejected"
                self.issue(record, "member-out-of-bounds", "Declared member exceeds archive; no clipped payload retained")
                self.add(record)

        # Explicit card-directory identities; never a thumbnail alias for CPJ artwork.
        slots = {(r["archive"], r["cardId"]): r for r in self.load(base + "/comparison/card-slots.json")}
        for archive in self.load(base + "/managed/card-indexes.json"):
            path = archive["path"]
            family = {"CIP": "card-thumbnails", "CPJ": "card-artwork-cpj", "CPM": "card-field-icons"}[archive["index"]["signature"]]
            with self.path(base + "/managed/raw/" + path).open("rb") as source:
                for entry in archive["index"]["entries"]:
                    identity = slots[(path, entry["cardId"])]
                    source.seek(entry["offset"]); data = source.read(entry["storedSlotBytes"])
                    if digest(data) != identity["managedSha256"] or not identity["equal"]:
                        raise ValueError("Card slot differs from the independent stored-byte comparison")
                    record = new_record(dict(kind="card-slot", source_path=path, card_id=entry["cardId"], offset=entry["offset"], length=entry["storedSlotBytes"]),
                                        family, "classified", "managed directory plus independent stored-byte comparison",
                                        self.ref(base + "/managed/raw/" + path), identity["managedSha256"])
                    record["details"].update(card_id_artwork_mapping=UNKNOWN, orientation=UNKNOWN, alpha=UNKNOWN, native_dimensions=UNKNOWN)
                    if family == "card-thumbnails":
                        root_bytes = struct.unpack_from("<I", data, 20)[0]
                        record["details"].update(expected_output="partial-thumbnail-only", available_bytes=len(data),
                                                 declared_gim_root_bytes=root_bytes, declared_gim_file_extent=16 + root_bytes)
                        self.issue(record, "thumbnail-truncated-no-pixel-invention", "Stored slot does not contain a complete declared GIM; ledger creates no thumbnail")
                    elif family == "card-artwork-cpj":
                        record["details"].update(expected_output="full-size-card-artwork", header_reconstruction=UNKNOWN, expected_tfsp_dimensions_per_card=UNKNOWN)
                        self.issue(record, "cpj-full-artwork-unverified", "Raw CPJ slot validation is not independent high-quality JPEG header/pixel validation")
                    else:
                        record["details"]["expected_output"] = "smaller-field-icon-candidate"
                    self.add(record)

        self.collect_audio()
        self.collect_text()
        movie = self.load("movie-audit-20261104/summary.json")
        for record in self.records:
            if record["family"] != "psmf-movie":
                continue
            # Already collected; refresh its machine-visible unknowns after attaching movie evidence.
            with wave.open(str(self.path("movie-audit-20261104/audio.wav")), "rb") as wav:
                pcm = hashlib.sha256()
                while block := wav.readframes(65536):
                    pcm.update(block)
                if pcm.hexdigest() != movie["audio"]["pcmSha256"]:
                    raise ValueError("Movie PCM changed since its independent audit")
            good_video = self.output(record, "movie-audit-20261104/pmf-video.framehash", None, "decoded-video-frame-hashes")
            good_audio = self.output(record, "movie-audit-20261104/audio.wav", None, "decoded-audio-wav")
            if good_video and good_audio:
                record["evidence"]["decoded"] = {"state": "decoded", "evidence": [self.ref("movie-audit-20261104/summary.json")]}
            record["details"].update(video=movie["video"], audio=movie["audio"], av_sync=UNKNOWN, aspect_scaling_acceptance=UNKNOWN, playback_skip=UNKNOWN)
            record["unknowns"] = sorted(unknown_paths(record))
            self.exceptions = [r for r in self.exceptions if not (r["record_id"] == record["record_id"] and r["code"] == "unverified-fields")]
            self.issue(record, "unverified-fields", "Movie decode does not approve playback timing or behavior", record["unknowns"])
        if not any(r["family"] == "font-candidate" for r in self.records):
            self.issue(None, "original-font-resources-unidentified", "A font PRX or text table is not a glyph atlas/metrics extraction",
                       ["font/source", "font/glyph_map", "font/metrics", "font/japanese_coverage", "font/english_patch_coverage"])
        if not any(r["family"] == "other-audio-candidate" for r in self.records):
            self.issue(None, "other-audio-scope-unverified", "No standalone other-audio candidate in the indexed scope; compressed/nested resources remain incomplete",
                       ["other_audio/codec_inventory", "other_audio/loop_trim_metadata"])
        package = self.load("presentation-package-audit-20261104/report.json")
        receipt_path = "presentation-package-audit-20261104/" + package["integrityReceipt"].replace("\\", "/")
        receipt = self.load(receipt_path)
        assembly = REPO / "TagForceSpecial.Assets/bin/Debug/net10.0/TagForceSpecial.Assets.dll"
        matches = file_hash(assembly) == receipt["DecoderSha256"]
        self.package_context = dict(package_id=package["PackageId"], scope=package["scope"],
                                    resources=package["resources"], receipt=self.ref(receipt_path),
                                    claim=receipt["Claim"], current_decoder_assembly_matches=matches)
        if not matches:
            self.issue(None, "package-decoder-version-mismatch", "Historical integrity receipt pins an older decoder assembly; regenerate package before current-reader reuse",
                       ["package_context/current_decoder_assembly_matches"])
        for record in self.records:
            validate_record(record)
        return self

    def collect_audio(self):
        summary = self.load("audio-semantics-20261104/summary.json")
        cues = self.load("audio-semantics-20261104/cues.json")
        cue_rows = collections.defaultdict(list)
        for row in cues:
            cue = row["cue"]
            for wave in cue["WaveformRows"]:
                cue_rows[(row["bank"], wave)].append({k: cue[k] for k in ("CueId", "CueIndex", "Name")})
            if cue["Diagnostics"]:
                self.issue(None, "audio-cue-graph-unresolved", "; ".join(cue["Diagnostics"]), [row["bank"] + "/cue/" + str(cue["CueId"])])
        wav_path = "audio-semantics-20261104/wav-audit/results.jsonl"
        self.provenance[wav_path] = self.hashed(wav_path)
        wavs = {r["sha256"]: r for line in self.path(wav_path).read_text().splitlines() if (r := json.loads(line))}
        for row in self.load("audio-semantics-20261104/waveforms.json"):
            relative = "audio-semantics-20261104/" + row["export"]
            record = new_record(dict(kind="hca-waveform", source_path=row["sourceFile"], bank=row["bank"], waveform_row=row["row"],
                                     wave_id=row["WaveId"], offset=row["sourceOffset"], length=row["sourceSize"]),
                                "hca", "classified", "ACB waveform to actual bank ID; HCA metadata validated", self.ref(relative), row["sha256"])
            if self.hashed(relative) != row["sha256"]:
                raise ValueError("Encoded audio changed since source audit")
            with self.path("extracted/dual-strategy-20261104/managed/raw/" + row["sourceFile"]).open("rb") as original:
                original.seek(row["sourceOffset"])
                if digest(original.read(row["sourceSize"])) != row["sha256"]:
                    raise ValueError("Audio export differs from its retained source range")
            wav = wavs[row["sha256"]]
            output = private_path(wav["wav"], self.root).relative_to(self.root).as_posix()
            if wav["valid"] and self.output(record, output, wav["wavSha256"], "pcm16-wav"):
                record["evidence"]["decoded"] = {"state": "decoded", "evidence": [self.ref(wav_path), self.ref("audio-semantics-20261104/wav-audit/managed-wav.json")]}
            record["details"].update(source_cues=cue_rows[(row["bank"], row["row"])], header=row["header"],
                duration_encoded_seconds=row["header"]["EncodedSamples"] / row["header"]["SampleRate"],
                loop_points=UNKNOWN, trim_policy="encoded samples; encoder delay/padding retained, not trimmed", runtime_codec_license_packaging=UNKNOWN,
                original_cue_timing_mix=UNKNOWN, prior_reference_ranges_resolved=summary["referencesResolvedToExternalAwb"])
            self.add(record)

    def collect_text(self):
        evidence = "dialogue-characters-20261104/text-tables.json"
        for table in self.load(evidence):
            for row in table["content"]["Records"]:
                record = new_record(dict(kind="text-pointer", source_path=table["archive"], table=table["table"], index=row["Index"], source_offset=row["SourceOffset"]),
                                    "indexed-text", "classified", "bounded source pointer table", self.ref(evidence))
                record["details"].update(offset_unit=table["content"]["OffsetUnit"], end_marker=row["IsEndMarker"],
                    semantic_row_role=UNKNOWN, actual_language_coverage=UNKNOWN, route_branches_triggers_world_map=UNKNOWN)
                if row["Text"] is not None:
                    record["evidence"]["decoded"] = {"state": "decoded", "evidence": [self.ref(evidence)]}
                    record["details"]["utf16_sha256"] = digest(row["Text"].encode("utf-16-le"))
                elif row["Diagnostic"]:
                    record["evidence"]["decoded"]["state"] = "rejected"
                    self.issue(record, "text-pointer-rejected", row["Diagnostic"])
                self.add(record)
        for row in self.load("dialogue-characters-20261104/character-voice-tables.json"):
            voice = row["voice"]
            record = new_record(dict(kind="character-voice-table", source_path=row["archive"], member=row["member"], internal_id=voice["InternalId"]),
                                "character-voice-index", "classified", "bounded numeric voice-reference groups", self.ref("dialogue-characters-20261104/character-voice-tables.json"), row["sha256"])
            record["details"].update(groups=len(voice["Groups"]), offset_words=voice["OffsetWords"],
                display_name_mapping=UNKNOWN, voice_event_label_mapping=UNKNOWN, complete_roster=UNKNOWN)
            record["evidence"]["decoded"] = {"state": "decoded", "evidence": [self.ref("dialogue-characters-20261104/comparison-report.json")]}
            self.add(record)
        for row in self.load("dialogue-characters-20261104/table-failures.json"):
            self.issue(None, "text-table-directory-rejected", row["diagnostic"], [row["archive"] + "/" + row["member"]])


def build(private_root, output_root, dotnet=None, source_host=None):
    ledger = Ledger(private_root)
    output = private_path(output_root, ledger.root)
    if output.exists():
        raise ValueError("Use a fresh ledger output directory; existing snapshots are never overwritten")
    executable = shutil.which(dotnet or "dotnet")
    host = Path(source_host or REPO / "TagForceSpecial.Tools/bin/Debug/net10.0/TagForceSpecial.Tools.dll").resolve()
    if not executable or not host.is_file():
        raise ValueError("Built source-check host and dotnet executable are required")
    iso = ledger.path("source/original.iso")
    command = [executable, str(host), "source-check", str(iso)]
    result = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", timeout=600, shell=False)
    if result.returncode:
        raise ValueError("Source validator failed; no ledger generated")
    identity = json.loads(result.stdout)
    if not identity.get("isValid"):
        raise ValueError("Source validator rejected the image; no cache-manifest bypass")
    with iso.open("rb") as stream:
        ledger.iso_stream = stream
        ledger.collect()
    ledger.iso_stream = None
    # Bind every resource/exception to this exact validated build, including stable row identities.
    ids = {}
    for record in ledger.records:
        previous = record["record_id"]
        record["source"] = dict(build_id=identity["buildId"], iso_sha256=identity["sourceSha256"],
                                profile_fingerprint=identity["profileFingerprint"])
        record["record_id"] = digest((identity["profileFingerprint"] + ":" + previous).encode())
        ids[previous] = record["record_id"]
    for problem in ledger.exceptions:
        if problem["record_id"] is not None:
            problem["record_id"] = ids[problem["record_id"]]
        problem["exception_id"] = digest(json.dumps([problem["record_id"], problem["code"], problem["fields"], problem["detail"]], sort_keys=True).encode())
    if file_hash(iso) != identity["sourceSha256"]:
        raise ValueError("Source image changed during ledger generation")
    records = sorted(ledger.records, key=lambda r: r["record_id"])
    if len({r["record_id"] for r in records}) != len(records):
        raise ValueError("Duplicate ledger resource identity")
    summary = dict(schema_version=1, records=len(records), exceptions=len(ledger.exceptions),
                   by_origin=dict(collections.Counter(r["origin"]["kind"] for r in records)),
                   by_family=dict(collections.Counter(r["family"] for r in records)),
                   stages={s: dict(collections.Counter(r["evidence"][s]["state"] for r in records)) for s in STAGES},
                   package_context=ledger.package_context,
                   source=identity, source_unchanged=True, generated_media_files=0,
                   complete_extraction=False, full_runtime_approval=False, psp_fidelity=UNKNOWN)
    output.mkdir(parents=True, exist_ok=False)
    for filename, rows in (("resources.jsonl", records), ("exceptions.jsonl", sorted(ledger.exceptions, key=lambda r: r["exception_id"]))):
        with (output / filename).open("x", encoding="utf-8", newline="\n") as stream:
            for row in rows:
                stream.write(json.dumps(row, sort_keys=True, separators=(",", ":")) + "\n")
    provenance = dict(created_utc=datetime.now(timezone.utc).isoformat(), source_check_command=command,
                      source_check_host_sha256=file_hash(host), script_sha256=file_hash(Path(__file__)),
                      input_manifest_sha256=ledger.provenance,
                      resource_ledger_sha256=file_hash(output / "resources.jsonl"),
                      exceptions_sha256=file_hash(output / "exceptions.jsonl"))
    for filename, data in (("summary.json", summary), ("provenance.json", provenance)):
        (output / filename).write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    return summary


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--private-root", required=True)
    parser.add_argument("--output-root", required=True)
    parser.add_argument("--dotnet")
    parser.add_argument("--source-check-host")
    args = parser.parse_args()
    try:
        print(json.dumps(build(args.private_root, args.output_root, args.dotnet, args.source_check_host), indent=2))
    except (OSError, ValueError, KeyError, subprocess.SubprocessError) as error:
        print("Extraction ledger failed: " + str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

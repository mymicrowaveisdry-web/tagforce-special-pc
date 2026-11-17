# Import formats and limits

| Resource | Supported operation |
|---|---|
| ISO9660 / extracted directory | Read-only inventory; trusted image/tree required for game-ready conversion |
| PARAM.SFO / PSF | Bounded metadata; recognition alone does not establish support |
| EHP v3 | Existing stored-range index; no automatic member decompression |
| CIP / CPJ / CPM | Existing image index/presentation conversion; full-catalog artwork export remains outside scope |
| GIM | Existing bounded decoder, frame-zero preview and PNG export; unsupported layouts report errors |
| Card tables | Existing linked-ID/name/detail readers and pinned normalized catalog |
| Prepared recipes | `recipes/viewer.json` and checked RGBA blobs; not encrypted PSP saves |
| ELF, gzip, CRI audio, movies, fonts, unknowns | Signature/provenance/header inspection; no new disassembler/audio pipeline |

For the measured English v2.1 source, `PSP_GAME/USRDIR/duelsys/cardinfo_jpn.ehp/CARD_Prop.bin` contains **7,255 eight-byte rows**. Internal row zero is the all-zero missing-ID sentinel. `CardInfoReader` validates it and emits rows 1 onward: **7,254 linked cards**, all retained in the normalized catalog. This reconciles raw versus normalized counts for this source; an external toolkit's counting algorithm was not independently audited. Dashboard's Audit card counts action repeats the measurement. Synthetic regression cases cover sentinel exclusion and rejection of a nonzero sentinel; unit execution remains manual.

Source/output SHA-256 pins live in `GameBuildProfile.English21`; extracted-tree pins live in `DirectoryPins`. A signature or readable archive index does not establish full payload decoding/validation. Unknown files retain path, size, hash, header and classification reason.

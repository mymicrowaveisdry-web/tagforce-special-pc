# 13A–13B: source/cache and deck foundations

These APIs precede the native setup/editor. The supported input is the exact
user-supplied English v2.1 image recorded below; disc title or disc ID alone does
not establish compatibility. Other builds/patches and CSO need separate reviewed
profiles rather than relaxed validation.

| Identity | Current value |
| --- | --- |
| Build | `tfsp-npjh00142-english-2.1-e99cff` |
| Whole ISO | 767,053,824 bytes; SHA-256 `e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707` |
| Cache schema | `1` |
| Mapping | `tfsp-runtime-inputs-v1` |
| Conversion | `cards-json-v1-initial14-v1;cardh-stored-v1` |
| Catalog schema | `1`; source cardinfo SHA-256 `6cd21aa6ca4fb82289f329400c9645da0532bf5a5092a604bef4c3e1d82d9060` |

`Assets.GameSource.GameBuildProfile` owns immutable expected source/output hashes
and a fingerprint of the entire profile. Profiles are trusted application
configuration, never supplied by a cache to define what should be trusted.
Changing source, mapping, converter or catalog bindings requires a compatible
profile/version and cache validation.

`GameSourceValidator.Open` checks image size/hash before required ISO9660 files.
PARAM.SFO, UMD_DATA, EBOOT, DuelEng, cardinfo and the card archive each have pinned
lengths/hashes. Only success returns a read-only source handle, which stays open
while importing. A final source hash check precedes publication.

`Import.GameCache.Create` requires that handle and the already converted catalog
package. It stages mapped raw cardinfo/cardh files and the catalog in a unique
sibling directory, validates every output, then publishes by directory rename.
This phase stores the card archive unchanged; texture/audio conversion remains separate.
Interactive ISO setup is later work. No original executable becomes a runtime
dependency or cache artifact. Existing caches are not overwritten. Cancellation
and failure leave no completed destination. Valid caches can be reused without
reopening the ISO after their trusted profile and artifacts validate.

Validation rejects wrong source/schema/mapping/conversion identities, missing,
changed or unexpected files and links. Expected hashes come from the profile,
so rewriting a cache's own checksums cannot bless altered content. Diagnostics
have stable codes and ordinal ordering; failure exposes no usable cache.

Original copyrighted assets/catalogs stay local, supplied through each user's
lawful import process. They are not bundled without permission. The duel engine
is independently reconstructed in C#. Images, caches and research outputs remain
excluded from Git/distributables.

## Decks and legality

`Core.Decks.DeckDefinition` snapshots schema/revision, owner profile, source build,
catalog identity/revision, regulation identity/revision and ordered Main/Extra/Side
IDs. `PlayerCollection` separately versions quantities and matching owner/source/
catalog. Owner keys are local profiles, not authenticated accounts. `PC.Decks.DeckFiles`
persists local JSON; unsupported schemas and malformed inputs are rejected, and
existing revision files are not overwritten. Cards still owns definitions only.

`DeckLegalityPolicy` provides explicit initial placements, alias/copy groups and
limits across all three sections. `tfsp-built-in-seed-preview/1` covers only the
14 preview IDs: Main 40–60, Extra up to 15, Side up to 15, combined three-copy
limit. These selected cards start in Main or Side. The size/copy envelope also
appears in the official [Konami deck-construction rules](https://img.yugioh-card.com/en/downloads/rulebook/SD_RuleBook_EN_10.pdf);
later Link/field rules from that document are not applied here.

The baseline uses static source interpretation of the built-in EBOOT seed,
not active save/DLC or story-unlock regulation. EBOOT SHA-256 is
`8087681f81f87a87471591752b3c0c9bc3cec48ffff15a115a1e2525f670c975`.
The 152-entry candidate at file `0xd27e0` has table SHA-256
`ea2aa9fa9c493a82374d0b64ed7628f7e5906f69178d3533c5e0c45afba09009`;
none of the selected 14 IDs occurs. This is bounded policy evidence, not new
runtime PSP verification. Unknown overrides and other card restrictions remain
unresolved rather than inferred from names or packed kind codes.

Results are `Legal`, `Illegal` or `Indeterminate`; only `Legal` passes. Unknown
policy/build versions never use another policy's banlist. Unreviewed placement,
aliases, restrictions or collections do not default to unrestricted/owned.
Generic policies can represent Forbidden (0), Limited (1), Semi-Limited (2),
unrestricted (3) and shared alias limits explicitly.

`SelectedDecks.Validate` requires a validated cache and both legal, correctly
owned decks matching that cache's source/catalog revision. Only success exposes
`ValidatedDeckSelection` for future application setup. Legality is separate from
complete gameplay support and PSP fidelity.

## Local commands

```powershell
./rtk.ps1 bypass dotnet run --project TagForceSpecial.Tools --no-build -- source-check <iso>
./rtk.ps1 bypass dotnet run --project TagForceSpecial.Tools --no-build -- cache-create <iso> <catalog-package> <new-cache>
./rtk.ps1 bypass dotnet run --project TagForceSpecial.Tools --no-build -- cache-check <cache>
./rtk.ps1 bypass dotnet run --project TagForceSpecial.Tools --no-build -- foundation-examples <new-directory>
./rtk.ps1 bypass dotnet run --project TagForceSpecial.Tools --no-build -- deck-check <cache> <deck.json> <collection.json>
```

Examples contain an explicit collection and legal, illegal (combined copy excess)
and unknown-regulation decks; they are acceptance data, not imported player saves.
Keep generated files local/ignored. CLI checks return 0 only on success/legal,
and 1 for rejection/indeterminate. The private source test accepts `TAGFORCE_ISO`;
portable CI excludes `Category=LocalReference` and uses authored synthetic inputs.

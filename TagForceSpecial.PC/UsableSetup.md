# Local setup and supported deck editor

## Native walkthrough

1. Launch the PC application normally; choose **Local Game Setup** (or **Decks**
   on a fresh profile). Choose **Select Original ISO** and select the complete
   English v2.1 ISO in the Windows file picker. Other builds and CSO are rejected.
2. Watch progress while identity, required resources, card conversion and cache
   publication run in a worker. Escape/B requests safe cancellation. **Resume
   Import** revalidates the original source and reuses completed, verified work.
3. Open **Decks**. Up/down browses the supported catalog; right adds and left
   removes a card from the current section. Tab/Y cycles Main/Extra/Side. Counts,
   card details and live legality feedback appear without opening research tools.
4. Enter/A opens the editor menu: name, search, filter, new empty deck, initial
   supported template, save new revision, load saved revision, or play. Name and
   search fields accept keyboard text; Enter applies and Escape cancels. Filters
   use explicit reviewed Monster/Spell metadata rather than packed type codes.
5. Save a revision. Invalid drafts can be retained, but **Play This Deck** checks
   legality, revalidates the active cache, saves/reloads the revision, then passes
   validated immutable card IDs to the supported duel factory. No editor list
   or mutable live-duel instance is persisted as a deck.
6. Preferences controls the opponent seed and integer scaling. Reset restores
   those preferences while retaining the active cache and selected deck.

The initial pool is the 14 reviewed IDs from `PlayablePreview`. Main requires
40-60 cards under the bounded original-era policy, with combined Main/Extra/Side
copy/ownership limits and reviewed placement. With this three-copy pool, at most
42 reviewed cards are available. Extra placements are unsupported for these IDs;
other card rules, aliases, save/DLC overrides and regulations are not inferred.
The collection remains explicitly authored, not an imported save or unlock claim.

## Private storage and manifests

Default: `%LOCALAPPDATA%/TagForceSpecial`. `--data-root` overrides this for isolated
testing. No game assets, images, converted catalogs or private paths are bundled
or uploaded. Source paths exist only in private preferences/checkpoints, not
cache manifests or release diagnostics. Only the read-only ISO stream is opened.

| Path / schema | Contract |
| --- | --- |
| `preferences.json`, schema 1 | Atomic active-generation pointer, source path, selected deck revision, seed/scaling; required fields and version validation |
| `preferences.backup.json` | Previous accepted preferences; malformed current settings never replace a valid backup |
| `import-journal.json`, schema 1 | Private source location plus trusted profile fingerprint; resume always revalidates source identity |
| `work/` | Verified source/card-image inputs and generated card package; incomplete artifacts are rehashed before reuse |
| `caches/<generation>/cache/cache.json`, schema 1 | Existing trusted build, mappings/converter version and exact expected file lengths/SHA-256; no relaxed profile |
| `caches/<generation>/resources.json`, schema 1 | `cip-slots-v1` index: card IDs, original relative source file, slot offset/length and explicit content gaps |
| `decks/<group>/<revision>.json`, schema 1 | New revision files only; ordered Main/Extra/Side IDs and original source/catalog/policy/owner identities |
| `retained/work-<id>/` | Explicitly restarted work retained separately; no recursive removal or original-media changes |

The [build manifest](Foundations.md) remains authoritative: source SHA-256
`e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707`, exact
767,053,824-byte image, `tfsp-runtime-inputs-v1` mapping and
`cards-json-v1-initial14-v1;cardh-stored-v1` converter. The catalog reader now lives
in `Assets.Import`; Cards still owns the catalog/package models independently of
binary readers. Tools delegates to the same converter. Setup needs no executable,
Python, toolkit, external database or already converted package.

The generated 7,254-card catalog must reproduce both pinned content and manifest
hashes. Raw cardinfo/cardh artifacts also retain their pinned hashes. The resource
index is regenerated from the verified card archive and compared on cache reuse;
its own claims/checksums cannot grant trust. Imported runtime inputs exclude EBOOT
and PRX modules even though source validation uses their identities.

Resource status explicitly reports remaining content gaps: card art is indexed
in its stored format, while texture decoding, original fonts, audio and dialogue
scenes remain unfinished. No unrelated artwork or downloadable substitutes are
used to hide those gaps. The supported duel uses imported card text/statistics and
the existing authored rendering fallback.

## Recovery and error taxonomy

| Code / situation | User action and safety contract |
| --- | --- |
| `SourceUnavailable` | Reconnect the source drive or select the ISO again; existing cache is retained |
| `SourceLengthMismatch`, `SourceHashMismatch` | Select the exact supported ISO; disc name/ID alone never grants compatibility |
| Required source files missing/damaged | Select the complete original; nothing is published from an incomplete build |
| `ImportCancelled` | Resume; only completed inputs/conversion outputs passing expected hashes are reused |
| `ImportFailed`, storage errors | Check access/free space, retry, or restart interrupted work; original and active cache stay intact |
| `SourceChanged` | Revalidate/reselect the original; final hash check prevents publication |
| Cache version/length/hash/structure failures | Verify/rebuild from original; saved decks and older generations remain |
| `ResourceIndexMismatch` | Rebuild the derived index/cache from the original; no self-rehashed index is accepted |
| `PreferencesInvalid` | Restore accepted backup or reset preferences explicitly; no automatic search/substitution of other caches |
| Invalid/unsupported deck revision | Select an earlier revision or create a supported draft; bad revisions remain visible as unreadable |

Import holds a private work lease. Rebuilds publish a fresh cache generation;
only after validation and resource indexing does an atomic preference replacement
select it. Old generations/revisions remain for recovery. A crash before that
switch leaves the old active generation; after it, the target is complete.
Startup checks the active cache and index without reopening the ISO. Missing
source media therefore does not prevent valid cache reuse. Links/path escapes
and unsupported/duplicate private document fields are rejected.

## Acceptance and remaining checks

`UsableSetupTests` covers first-launch preferences, required/duplicate/version
fields, backup recovery, missing/unsupported source, cancellation, retained work,
source-hash cancellation, native editor operations, independent legality cases,
revision save/reload and unknown rules. Its private original-source case imports
directly from the ISO, cancels/resumes, verifies exact catalog/resource identity,
launches a saved deck, reuses cache with a disconnected source path, rejects a
tampered index/cache, rebuilds and independently rehashes the unchanged ISO.

The Windows setup smoke runs the actual window/import worker, editor save/reload,
legality gate, persisted-deck factory, opponent and terminal result. Its scripted
editor path is not a physical gamepad acceptance claim. Local captures and the
first-launch/recovery report live under ignored `analysis/stage13c/`; portable CI
continues to exclude original-source cases. Native source selection and physical
controller interaction need their own observed checks; renderer-fidelity gates
remain separate.

```powershell
dotnet run --project TagForceSpecial.PC
dotnet test TagForceSpecial.Tests --filter FullyQualifiedName~UsableSetupTests
# Isolated native acceptance only; normal use selects the source in the GUI.
dotnet run --project TagForceSpecial.PC --no-build -- --smoke --setup-smoke <iso> --data-root <private-test-root> --capture-editor <new.png> --replay <new.json>
```

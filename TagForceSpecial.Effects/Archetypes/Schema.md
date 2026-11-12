# Archetype authoring documents

These contracts describe content data without loading packs or registering gameplay. `ArchetypePackJson.Read` reads five separate JSON documents and validates them against explicit source-card identities and previously validated dependency exports; `ReadDocument` only parses an individual document.

| Document type | Required fields |
| --- | --- |
| `ArchetypePackManifest` | `schemaVersion`, `packId`, `version`, `kind`, `profiles`, `dependencies`, `evidence` |
| `ArchetypeDefinitionsDocument` | `schemaVersion`, `definitions`, `relationships` |
| `ArchetypeMembershipsDocument` | `schemaVersion`, `memberships` |
| `ArchetypeIdentityRulesDocument` | `schemaVersion`, `rules` |
| `NativeArchetypeMappingsDocument` | `schemaVersion`, `mappings` |
| `NativeArchetypeTableDocument` | `schemaVersion`, `sourceSha256`, `catalogSha256`, `membershipCount`, `tokenCardIds`, `groups` |
| `NativeArchetypeLabelsDocument` | `schemaVersion`, `labels` |

All document schemas currently use version 1. JSON field names and string enum values use camel case; unknown/duplicate properties, missing required constructor fields and numeric enums are rejected. Each document is limited to 16 MiB and 32 nesting levels. Explicit empty lists are valid where no declarations are needed; null lists/items are invalid.

Archetype, pack and source-card IDs are distinct types serialized as lowercase namespaced strings, such as `synthetic:little-stars` and `tfs:11310`. Their grammar is `[a-z][a-z0-9.-]*:[a-z0-9][a-z0-9._/-]*`, with a maximum length of 160. Display names and aliases do not determine membership. Versions are exact revision tokens (at most 80 characters, no whitespace), not version ranges. External IDs are opaque system/value pairs; no setcode matching is inferred.

Every definition, relationship, membership, identity rule and native mapping has a `review` status and `evidence` containing a reference, SHA-256 digest and reason. Hashes identify input material; they do not establish authenticity or correctness. `ReviewedContent` rejects candidates; `Diagnostic` preserves them for investigation. Both modes enforce structural and reference validity.

Dependencies must be declared with exact versions and supplied explicitly; undeclared exports cannot resolve references. Local definitions cannot replace dependency IDs. Subset relationships mean child members also belong to the parent, and must be acyclic. Validation checks declarations but does not expand membership or evaluate relationships.

Memberships list distinct archetype IDs for an explicitly known source card. Native mappings retain numeric historical group IDs and permit a null semantic ID for unnamed slots; expansion packs cannot redefine these mappings or target `tfsClassic`. The domain imposes no native group-count or numeric source-card-width limit.

Identity rules declare either `permanentName` with only `name`, or `permanentArchetype` with only `archetype`. Contradictory permanent names for one source card are invalid. These declarations do not change static membership or evaluate conditional duel state.

The document records are authoring DTOs. `ArchetypeContentSnapshot.Compile(profile, packs, cards)` validates an explicit in-memory pack selection and card facts, deep-copies all collections, and publishes read-only static indexes. File discovery/loading, conditional identity evaluation and gameplay consumer migration are separate work.

`NativeArchetypeIndex` deep-copies a historical table into a read-only numeric adapter: complete ordered slots, contiguous offsets and sorted positive signed-halfword IDs, including Tokens. Its native format bounds do not restrict universal archetype or source-card identifiers. Semantic import requires explicit unique reviewed IDs; candidate labels retain no semantic ID and remain diagnostics. The generated semantic memberships exclude Tokens, while the native table and explicit catalog Token list preserve them. Native evidence and semantic label evidence remain separate.

## Static content snapshots

The snapshot implements `IArchetypeRegistry` and `IArchetypeMembership`, keyed by typed IDs. `GetArchetypes` and `GetMembers` are reciprocal, sorted by ordinal ID value; `IsMember` uses the compiled set. Unknown IDs return empty lists/false (registry and card metadata lookups return null). `GetDirectArchetypes` preserves declared membership; ordinary lookup also includes ancestors of explicitly declared subset relationships. Names, aliases and external setcodes never infer relationships, and identity rules are retained without being evaluated.

Every selected pack must support the requested profile and have its exact declared dependencies present. Compilation rejects candidates, duplicate pack/definition/rule IDs, ambiguous names/aliases, conflicting external IDs or permanent names, and dependency/subset cycles across the entire selection. Declared dependencies export their own definitions; references to ancestors must declare those dependencies explicitly. Pack views are in deterministic dependency order; input selection order never decides which conflicting declaration wins.

`ArchetypeCardFacts` supplies source identity, known/unknown kind, optional Level or Rank, and a Token flag. Only monsters can have Level/Rank/Token metadata, and Level and Rank cannot both be supplied. No original-game width/range restriction is imposed on universal facts. `ArchetypeMemberFilter` independently narrows kind, Level range, Rank range and Token status; missing Level/Rank never satisfies its corresponding range. Filtering does not alter group membership or silently imply monster-only scope.

`Packs`, `Cards`, definitions, nested collections and query results are read-only snapshots. The versioned SHA-256 `Fingerprint` covers the profile, canonical pack documents and card facts with length-delimited UTF-8 framing; reordering input collections does not change it. A retained snapshot stays unchanged when inputs or later selections change. This is a static content contract, not a claim that a duel is already using the new registry or that dynamic identity has been resolved.

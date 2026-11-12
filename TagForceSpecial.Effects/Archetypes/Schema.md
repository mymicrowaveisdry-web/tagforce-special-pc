# Archetype authoring documents

These contracts describe content data without loading packs or registering gameplay. `ArchetypePackJson.Read` reads five separate JSON documents and validates them against explicit source-card identities and previously validated dependency exports; `ReadDocument` only parses an individual document.

| Document type | Required fields |
| --- | --- |
| `ArchetypePackManifest` | `schemaVersion`, `packId`, `version`, `kind`, `profiles`, `dependencies`, `evidence` |
| `ArchetypeDefinitionsDocument` | `schemaVersion`, `definitions`, `relationships` |
| `ArchetypeMembershipsDocument` | `schemaVersion`, `memberships` |
| `ArchetypeIdentityRulesDocument` | `schemaVersion`, `rules` |
| `NativeArchetypeMappingsDocument` | `schemaVersion`, `mappings` |

All document schemas currently use version 1. JSON field names and string enum values use camel case; unknown/duplicate properties, missing required constructor fields and numeric enums are rejected. Each document is limited to 16 MiB and 32 nesting levels. Explicit empty lists are valid where no declarations are needed; null lists/items are invalid.

Archetype, pack and source-card IDs are distinct types serialized as lowercase namespaced strings, such as `synthetic:little-stars` and `tfs:11310`. Their grammar is `[a-z][a-z0-9.-]*:[a-z0-9][a-z0-9._/-]*`, with a maximum length of 160. Display names and aliases do not determine membership. Versions are exact revision tokens (at most 80 characters, no whitespace), not version ranges. External IDs are opaque system/value pairs; no setcode matching is inferred.

Every definition, relationship, membership, identity rule and native mapping has a `review` status and `evidence` containing a reference, SHA-256 digest and reason. Hashes identify input material; they do not establish authenticity or correctness. `ReviewedContent` rejects candidates; `Diagnostic` preserves them for investigation. Both modes enforce structural and reference validity.

Dependencies must be declared with exact versions and supplied explicitly; undeclared exports cannot resolve references. Local definitions cannot replace dependency IDs. Subset relationships mean child members also belong to the parent, and must be acyclic. Validation checks declarations but does not expand membership or evaluate relationships.

Memberships list distinct archetype IDs for an explicitly known source card. Native mappings retain numeric historical group IDs and permit a null semantic ID for unnamed slots; expansion packs cannot redefine these mappings or target `tfsClassic`. The domain imposes no native group-count or numeric source-card-width limit.

Identity rules declare either `permanentName` with only `name`, or `permanentArchetype` with only `archetype`. Contradictory permanent names for one source card are invalid. These declarations do not change static membership or evaluate conditional duel state.

The document records are authoring DTOs, not immutable published snapshots. Runtime pack composition, cross-pack relationship checks, static indexes, conditional identity evaluation and consumer migration are separate work.

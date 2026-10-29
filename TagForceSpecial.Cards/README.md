# Card database models

Immutable card records and a catalog keyed by nonzero `uint` identity. IDs are
opaque catalog keys, with no PSP packed-ID ceiling. Nonnegative ATK/DEF values
are stored directly; variable values are null. Levels have no source-format bit
limit. Gameplay eligibility remains an explicit Core rule/effect configuration.

Imported numeric codes and raw source rows are optional provenance. Authored
cards need neither those fields nor a fictitious container hash. Every catalog
supplies an origin profile, which is attached to cards lacking their own origin.
Supplied file hashes must still be valid SHA-256 values and are normalized.

```csharp
var additions = new CardCatalog(new(null, "my-cards-v1"), [
    new CardDefinition(100_000, "Additional monster", "Authored card text.",
        new CardStatistics(9999, 5501, 4))
]);
var combined = importedCatalog.Merge(additions);
```

`Merge` produces a new immutable catalog and rejects ID collisions. Per-card
origins and raw imported rows remain attached to their original cards; the
combined catalog does not claim to come from one original container. No game
assets are bundled and no effect is inferred from card text or numeric codes.

The local PSP import adapter is in TagForceSpecial.Tools; binary readers remain
in TagForceSpecial.Assets. `PspCardCatalogValidator` in Tools retains the packed
ID/source-row, bit-width and encoded-stat checks before a PSP import enters the
generic catalog. This project has no binary-reader, renderer or emulator dependency.
Original-game research fixtures and visual acceptance reports remain local and
are not included in this branch.

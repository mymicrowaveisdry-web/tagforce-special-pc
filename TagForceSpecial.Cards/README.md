# Card catalog

Immutable records keyed by nonzero `uint` IDs, with direct nonnegative stats
and nullable variable values. Gameplay requires explicit Core rules/effects.

```csharp
var additions = new CardCatalog(new(null, "my-cards-v1"), [
    new CardDefinition(100_000, "Additional monster", "Authored card text.",
        new CardStatistics(9999, 5501, 4))
]);
var combined = importedCatalog.Merge(additions);
```

`Merge` rejects ID collisions and preserves per-card origins and imported rows.
Authored cards need no PSP metadata; supplied hashes must be valid SHA-256.
Tools validates PSP encoding at import. Cards has no binary-reader or emulator dependency.

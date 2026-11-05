# Card catalog

Immutable definitions keyed by nonzero `uint` IDs, with printed stats, raw provenance,
static classification and effect-reference IDs; `Unknown`, `None` and `Referenced`
are distinct. Core owns duel instances/rules; Effects resolves behavior.

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

`CatalogStorage.CardCatalogPackage.Load(directory)` validates version, checksum,
counts and definitions before publishing an immutable snapshot; `Write` creates
a new version without overwriting. `CardCatalogProvider.Reload` preserves the last
valid catalog on failure. The JSON contract is [catalog.schema](CatalogStorage/catalog.schema).

```powershell
./rtk.ps1 bypass dotnet run --project TagForceSpecial.Tools -- catalog-import <cardinfo.ehp> 3459 11657 TagForceSpecial.Cards/Data/Local/tfsp/<source-hash>/v1
./rtk.ps1 bypass dotnet run --project TagForceSpecial.Tools -- catalog-check <package-directory>
```

Each package holds `manifest.json` and `cards.json`; the manifest identifies its
revision, locale, source build/decoder and content SHA-256. Checksums detect
corruption, not authenticity. Other imports require explicit `catalog-id revision locale`
arguments; `Merge` rejects collisions rather than replacing a version implicitly.
Original data stays in ignored `Data/Local`, excluded
from build/publish/pack content; the PC setup imports each user's own lawfully
acquired ISO locally. Original assets/catalogs are not bundled without permission.

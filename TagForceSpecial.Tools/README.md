# Development commands

After the [quick start](../README.md#quick-start), run the synthetic demo:

```powershell
dotnet run --project TagForceSpecial.Tools --no-build -- duel-demo
```

`ehp-list`, `card-images-list`, `cardinfo-list`, `card-details` and `card-catalog`
inspect local inputs and emit JSON. `CardCatalogImporter` validates PSP encoding
before creating a format-independent catalog; authored cards use the Cards API directly.
`catalog-import <file> <min> <max> <new-directory>` stores a versioned local package;
`catalog-check <directory>` validates it. `card-support` emits per-source support
manifests; `preview-duel` accepts either a package directory or the file/ID-range arguments.
`pool-duel <package-directory> [duels] [seed] [report.json]` plays seeded duels over every supported card, checking engine invariants and replay.
`source-check`, `cache-create`, `cache-check`, `foundation-examples` and `deck-check`
exercise the source/cache and deck foundations with deterministic diagnostics.

Legacy `progress` and `research-check` require ignored research configs, evidence
and the former documentation layout. They are outside the clean-checkout workflow.
[CI](../.github/workflows/progress.yml) publishes test results without rewriting documentation.

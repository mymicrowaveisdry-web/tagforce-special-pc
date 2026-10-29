# Development commands

After the [quick start](../README.md#quick-start), run the synthetic demo:

```powershell
dotnet run --project TagForceSpecial.Tools --no-build -- duel-demo
```

`ehp-list`, `card-images-list`, `cardinfo-list`, `card-details` and `card-catalog`
inspect local inputs and emit JSON. `CardCatalogImporter` validates PSP encoding
before creating a format-independent catalog; authored cards use the Cards API directly.

Legacy `progress` and `research-check` require ignored research configs, evidence
and the former documentation layout. They are outside the clean-checkout workflow.
[CI](../.github/workflows/progress.yml) publishes test results without rewriting documentation.

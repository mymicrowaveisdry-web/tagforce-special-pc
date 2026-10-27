# Development commands

After building with the [root quick start](../README.md#quick-start), run:

```powershell
dotnet run --project TagForceSpecial.Tools --no-build -- duel-demo
```

This uses invented cards and needs no ISO or local research files.

The asset commands (`ehp-list`, `card-images-list`, `cardinfo-list`,
`card-details` and `card-catalog`) inspect user-supplied local inputs and emit
JSON. They do not bundle or download original game assets.

## Legacy research commands

`progress` and `research-check` retain the earlier evidence-reporting
implementation. They require ignored local configuration such as
`config/progress.json`, `config/coverage.json`, `config/research-queue.json`,
`config/native-matches.json`, and the inventories/evidence those files reference.
The progress writer also expects the former documentation layout, including
`docs/STATUS.md` and progress markers in the target documents. The current root
README is maintained manually and does not contain those markers.

These commands are not supported by a clean checkout of this branch and are
not run by CI. The [C# workflow](../.github/workflows/progress.yml) publishes test
results directly; it does not generate documentation or research metrics.

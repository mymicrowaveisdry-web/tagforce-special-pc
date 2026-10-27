# PC application host

The entry point is currently a placeholder. Rendering, input, private-information
views and the playable sample-duel flow remain to be connected to the C# engine.
A supported duel must reach a win/loss through the application before the host
can be considered playable.

## Required first-launch asset setup

The distributed application must contain no original game assets, disc images,
executable modules or pre-extracted game data. On first launch, before gameplay,
show an asset-setup menu where the user selects their legally obtained Tag Force
Special ISO. The application must validate the supported source identity and
required contents, then import the assets locally without modifying the ISO.

Show import progress and useful errors for unsupported, corrupt or incomplete
inputs. Build a versioned per-user asset cache; subsequent launches validate and
reuse it. If assets are missing or invalid, return to asset setup. Provide an
option to select another ISO or rebuild the cache. Do not upload the ISO or
imported assets, download replacement game assets, or include a user's cache in
release packages.

This is a release requirement; the first-launch menu and importer are not yet
implemented. Packaging acceptance must test a fresh installation with no assets,
successful import, rejected inputs, interrupted-import recovery, cache reuse,
unchanged source ISO and a package inspection for original game material.

# Engine preview

`PlayablePreview` is the executable release manifest: 14 original game-local IDs
in three behavior families. Both decks must contain 40–60 pool cards with at most
three copies per ID. Deck order is explicit; no shuffle or opponent AI is implied.
Names, descriptions and printed values come from the immutable Cards catalog;
the preview retains IDs and behavior profiles, and validates their static references.

| Family | Cards and IDs |
| --- | --- |
| Ordinary Normal Monsters | Feral Imp 4012; Skull Servant 4030; Battle Ox 4032; Beaver Warrior 4033; Silver Fang 4071; Axe Raider 4084; Luster Dragon 5644; Gene-Warped Warwolf 6960 |
| Fixed LP recovery | Red Medicine 4345 (+500); Dian Keto 4348 (+1000); Blue Medicine 4840 (+400) |
| Fixed effect damage | Sparks 4349 (200); Hinotama 4350 (500); Ookazi 4352 (800) |

Normal Monsters use ordinary summon/set, flip, position, attack, battle and
Graveyard rules. Spells have no cost, target, choice or special use limit, activate
from hand or set in either own Main Phase, and enter a Spell Speed 1 chain. A
Normal Spell can activate on the turn it was set. Two passes close responses;
the active player advances resolution. Both negation kinds skip its effect;
destroying the source alone does not negate it. Surviving sources leave once at
chain cleanup. Effect damage causes immediate terminal defeat at zero LP,
before cleanup; recovery uses the existing integer saturation policy.

Only interactions reachable with these decks are advertised. Extra card effects,
damage replacement, tags, original banlists, assets, UI and opponent policy remain
outside this pool. Synthetic interruption probes test adjacent engine contracts
without advertising their invented response cards as supported originals.

## Local commands

```powershell
./rtk.ps1 bypass dotnet run --project TagForceSpecial.Tools --no-build -- card-support <cardinfo.ehp> 3459 11657
./rtk.ps1 bypass dotnet run --project TagForceSpecial.Tools --no-build -- preview-duel <cardinfo.ehp> 3459 11657
./rtk.ps1 bypass dotnet run --project TagForceSpecial.Tools --no-build -- preview-duel <package-directory>
./rtk.ps1 dotnet test TagForceSpecial.Tests --filter 'FullyQualifiedName~NormalSpellTests|FullyQualifiedName~PlayablePreviewTests'
```

Redirect imported manifests and scenario logs into an ignored local folder; they
contain local card text. `card-support` records original text, IDs/provenance,
conditions, operations, costs, choices, limits, tests and evidence separately for
the preview and full pool (the full pool also includes the 665-card Normal Monster
family, the five chain cards, the 13 LP cards, the 10
Deck-operation cards, the 15 removal cards, the 8 Graveyard cards, the 9 battle Traps, the 5 summon Traps, the 3 summon negators and the 13 monster triggers,
and excludes 95 Tokens).
Unreviewed cards remain unsupported. Unclassified effects require later family inventory.

The reviewed cardinfo hash is
`6cd21aa6ca4fb82289f329400c9645da0532bf5a5092a604bef4c3e1d82d9060`.
Its 7,255 property rows contain one zero sentinel and 7,254 distinct nonzero IDs;
artwork variants retain distinct IDs. Alternate source hashes fail closed.

The six Spell amounts have static PRX corroboration from the pinned toolkit and
Ghidra signed table at historical `0x09c98ec4`, through handler `0x0990d710`.
This is a static interpretation (`reaEvidence=false`), not observed execution.
Every preview card's PSP-verified flag is false. Tests prove the declared engine
subset; full PSP compatibility and broader playability remain open.

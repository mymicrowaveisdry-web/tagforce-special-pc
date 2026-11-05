# Stage 12: first opponent

## Authorized action contract (12A)

Core's `Views/DuelActionSource` is the trusted adapter shared by human input and
the opponent. `Observe(player)` produces immutable `DuelObservation` values:
revision, acting player, turn/phase, player-specific projections, owned decision
options, supported legal actions and terminal outcome. It caches by revision.

Opponent hands/set identities are redacted (no card/instance IDs, text or stats).
Both main decks and face-down Extra Decks expose counts only. An owned search
decision exposes only authorized card options, sorted by card/instance identity,
with no deck-position property. Handles carry no deck-order semantics. Public
field/Graveyard/face-up Extra cards and the actor's own hand/set cards are visible.

The adapter generates candidates from own cards and public zone addresses and
filters them using the same `CheckCommand` transition as human preflight. Policy
code receives neither `DuelState`, `DuelEngine`, catalogs, draw order, raw events
nor executable commands. Each `LegalAction` has an opaque token/key, semantic
kind and authorized own-card/zone/phase parameters. Commands containing private
target bindings remain inside the adapter. `Execute(player, revision, token)`
rejects stale/illegal handles atomically; success calls `DuelEngine.Execute` once.

`Ready`, `Waiting`, `Finished` and `Unsupported` are distinct. No supported action
means an explicit unsupported continuation, never a win or successful no-op.
Discovery and ranking are bounded at 4,096 candidates/actions. All access is on
the owning game thread. Core's command probe relies on immutable engine storage.

## Policy and decision matrix (12B-12D)

`initial-opponent-v1` scans one authorized action list. Priorities: draw and
mandatory choices; responses/pass/resolution; normal spells; strongest own normal
summon; favorable public battles/direct attacks; flips/attack position; phase
progression and end turn. Discards prefer weaker own cards. A set defender gets
a fixed unknown-card priority; its hidden stats never affect attack ranking.
Visible losing/ineffective attacks rank below phase progression. There is no
engine simulation/lookahead inside the policy and no implicit card behavior.

Equal scores use versioned FNV-1a/avalanche over policy version, unsigned seed,
revision, player and action key. No `Random`, runtime string hashes or mutable RNG
state is used. Identical authorized observations, version and seed yield the same
choice, regardless of previous policy calls. This is a PC policy, not PSP AI.

| Situation | Adapter command/ownership | Supported boundary |
| --- | --- | --- |
| Draw, phases, End Phase | ResolveDrawStep, ChangePhase, EndTurn; active player | Includes Core hand limit and terminal deck-out |
| Hand/field play | NormalSummon, SetMonster/SpellTrap, ActivateSpell/SetSpell, ChangeMonsterPosition | Explicit preview profiles only |
| Authorized card/cost search | ChooseDecisionCard; pending decision owner | Only current offered options, including authorized source cards |
| Position/empty zone/target | ChooseDecisionPosition/Zone/Target; decision owner | Current ID plus retained target binding; no hidden target identity in policy |
| Normal spell chain | PassChainResponse, ResolveNextChainLink | Current responder; active player steps resolution |
| Registered simple set responses | ActivateChainResponse/BattleResponse | Offered only if Core accepts without additional unsupported parameters |
| Staged battle | DeclareAttack, PassBattleResponse, AdvanceBattle | Current responder while open; active player after two passes |
| Attack replay | ChooseAttackReplay or cancel | Current ID and legal public targets; synthetic replay boundary tested |
| Materials, tribute/procedure/effect summons, summon negation, trigger commitments/order | Explicitly outside this adapter/host pool | Required specialized workflows remain unsupported; never silently auto-resolved |

`OpponentDriver` executes at most one command per call and stops after its
12,000-command budget or three consecutive rejections. It refreshes from the
current observation each call. Pause/cancellation, waiting, unsupported, budget
exhaustion and terminal states do not advance Core. The session caps total human
and AI commands at 24,000; the native smoke caps the entire run at 6,000 commands.
These are logical step bounds, not real-time performance guarantees.

## Supported decks and PC integration (12E)

`Effects.InitialOpponentDecks` is manifest `tfsp-initial-opponent-decks-v1`.
It contains three ordered variants of the same 40-card preview, each retaining
the original 14-ID pool and maximum three copies per ID. IDs:
`4012, 4030, 4032, 4033, 4071, 4084, 5644, 6960, 4345, 4348, 4349, 4350, 4352, 4840`.
No Extra/Side/unsupported cards are added. These are authored orders/collections,
not original character decks, imported saves or shuffles. The build/catalog/policy
identities are retained by `InitialDeckPolicy` and the cache gate.

The same `SupportedDuelFactory` used by deck selection requires a validated
cache and both legal deck/collection selections before creating the preview
engine with staged battle enabled. The PC game thread steps the opponent at
350 ms intervals, including out-of-turn chain/battle responses. No background
thread mutates Core. Inactive windows stop AI scheduling. Escape/B opens a pause
menu; resume retains state/seed and resets the next deadline, with no catch-up
burst. Result screens use `DuelOutcome` exclusively. Public notifications name
the action category without revealing a private hand card's name.

## Replays and acceptance (12F)

`DuelReplay` schema 1 records the policy version, seed, action-contract version,
deck manifest, exact input deck orders, battle mode, accepted command sequence
and engine outcome. Each entry records before/after revisions, actor, semantic
key/kind and current decision ID. `ReplayCommands.Apply` resolves that entry
against a fresh authorized legal-action list and rejects unknown, stale or
ambiguous entries. Reconstruct with the same catalog/profiles and recorded decks;
human entries record what the user chose and are not claims about policy output.
The PC controller permits export only after a terminal outcome. Replays stay private: actor-authorized choices collected across both players
are unsuitable as live opponent observations.

Acceptance covers hidden-state substitution, immutable discovery, same-seed
repeat/replay, multiple seeds (0, 1, 7, 42, 4294967295), stale/illegal/wrong-owner
rejection, card/position/zone/cost/target choices, chain cleanup, staged battle,
synthetic attack replay, pause/cancellation, budgets, unsupported cards and real
terminal wins/losses. Portable tests run in the existing CI test command; local
cache and Windows rendering checks are separate. No PSP AI fidelity claim follows.

```powershell
./rtk.ps1 dotnet test TagForceSpecial.Tests --filter FullyQualifiedName~OpponentTests
./rtk.ps1 bypass dotnet run --project TagForceSpecial.Tools --no-build -- opponent-acceptance <cache> <new-private-output-directory>
./rtk.ps1 bypass dotnet run --project TagForceSpecial.PC --no-build -- --cache <cache> --seed 7
./rtk.ps1 bypass dotnet run --project TagForceSpecial.PC --no-build -- --cache <cache> --smoke --seed 7 --deck 2 --replay <new-private.json> --capture-result <new-private.png>
```

`--deck 0` smoke exercises a scripted human LP loss; `--deck 2` exercises a
scripted human deck-out victory at seed 7. Both run through the real PC controller
and window; automated smoke does not certify physical gamepad interaction.
Replay/result output flags save the first completed duel per process; existing
files are never overwritten. Original assets, texture/font conversion
and exact PSP GUI matching remain Stage 11 work; original character/tag fidelity
remains Stage 14 work.

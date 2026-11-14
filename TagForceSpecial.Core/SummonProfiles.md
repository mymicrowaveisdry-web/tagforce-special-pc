# Explicit summon profiles

Core supports bounded, authored hand Normal Summon/Set procedures. All profiles
default off and require trusted `DuelCardRules`; catalog categories or card text
never grant eligibility. These contracts have synthetic tests, not PSP tribute
execution comparisons or complete real-card support.

| Constructor registration | Contract |
| --- | --- |
| `experimentalSummonLevelModifiers` | `SummonLevelModifierProfile` adds a signed hand-level delta for each matching live face-up, unnegated monster source, with Controller/Opponent/Any relation and explicit target IDs. |
| `experimentalTributeProcedures` | `TributeProcedureProfile` specifies a target card, unique procedure ID, exact count 0–5, optional donor-ID allowlist, face-up requirement and Set permission. |
| `experimentalSummonWindows` | `SummonWindowProfile` opts an eligible Normal or explicitly profiled Special Summon procedure into a pre-success face-up window. Tribute Set does not open it. |

`AllowsNormalSummon` still gates all these hand procedures. `AllowsTributeSummon`
and `AllowsTributeForSummon` gate targets and donors. A named procedure never
bypasses either. Set `RequiresRegisteredTributeProcedure` to forbid default
Normal Summon/Set and level-based procedures for that target; construction then
requires a matching registered procedure. Otherwise named procedures are optional
alternatives, and their restrictions apply only when selected.

## Levels and tribute selection

`GetHandSummonLevel(player, instanceId)` queries the current hand value. Sum all
eligible deltas, then clamp once to 1 through uint.MaxValue. A raw level of zero
stays zero and remains unsupported. These adjustments are limited to summon/set
eligibility, not battle statistics, Ritual/Synchro materials or a full level system.
Level-setting, multiplicative, lingering, cyclic and precedence-sensitive effects
remain unsupported.

Default counts use that effective level: zero at 1–4, one at 5–6, two at 7+.
Standard `NormalSummon`/`SetMonster` accepts an eligible target reduced to 1–4;
raised targets need explicit tribute eligibility. `TributeSummon` is the high-level
or named-alternative API: without a named procedure, raw or effective level must
be at least 5. Zero-tribute placement still needs an empty destination.

```csharp
var command = new TributeSummon(player, engine.State.Revision, handInstanceId,
    destinationZone, selectedMonsterBindings, faceDown: false, procedureId: "three-tributes");
var result = engine.Execute(command);
```

Counts and donor policies are checked against the complete pre-command snapshot,
before removing modifier sources or spending the allowance. Invalid answers do
not mutate state or emit events. Selected donors bind instance, controller, slot
and placement; freed slots are valid destinations. Named exact-count procedures
override level-based counts but do not implement double-value tributes, alternate
resource payments, opponent tributes, tokens, Pendulum departures or arbitrary
card-specific requirements.

## Pre-success negation

With a matching window profile, commit hand departure, selected tributes and the
shared summon/set allowance, then expose immutable `State.PendingSummon`.
The pending card is held by the attempt and occupies no field zone; it provides
no continuous effects and cannot be a field target. `SummonAttempted` is distinct
from `MonsterNormalSummoned`. Graveyard triggers from tributes wait until the
attempt and any negation chain finish.

Use `PassSummonResponse` or `ActivateSummonNegation` with the current decision ID
and responder. Two consecutive passes complete the summon. An accepted negator
opens a Spell Speed 3 chain and pays its fixed LP cost immediately; LP must remain
positive. During this chain only registered activation/effect-negation responses
of sufficient speed are supported; their existing discard decisions still apply.
Resolve through `PassChainResponse`, `ActivateChainResponse` and
`ResolveNextChainLink`. Summon success follows chain cleanup if the negator was
itself negated. No second root negation window opens.

If negation resolves, the target goes to its owner's Graveyard and emits
`SummonNegated`, with generic destruction/Graveyard occurrences whose subject has
zone -1. It never emits `MonsterDestroyed` from a field zone or a successful
summon event. Tribute movement, allowance and paid costs remain committed.
Successful completion creates a fresh placement and opens supported summon
triggers; optional When tribute triggers miss the later action. Both outcomes
release deferred triggers through the existing collector and keep old snapshots
immutable.

This window covers opposing set Counter Traps and Normal/Tribute Summons; [Special Summon procedures](SpecialSummons.md) use it through a separate kind opt-in.
Self-negation, summon protection, variable/fractional LP payments, monster negators,
Flip Summon windows and general fast-effect windows after success are
outside it. Negating an effect that summons uses the existing effect-negation
contract, not this window. See Konami's [summon/effect distinction](https://www.yugioh-card.com/en/play/psct/psct-5/)
and [fast-effect timing](https://www.yugioh-card.com/en/play/fast-effect-timing/);
these are rules references, not evidence of PSP implementation.

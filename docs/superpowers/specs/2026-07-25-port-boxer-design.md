# Boxer Warrior strain port design

**Status:** Approved for implementation by the user on 2026-07-25.

## Goal

Port the historical CMSS13 Boxer Warrior strain to CMU/RMC as an isolated strain prototype with the same gameplay contracts: Boxer Punch, Jab, Uppercut, KO buildup, target locking, Clear Head charges, stat changes, and the removal of Fling/Lunge.

## Architecture

`CMXenoWarriorBoxer` will inherit from `CMXenoWarrior` and override its sprite, action list, strain metadata, thresholds, armor, and components. The behavior will live in a dedicated `XenoBoxerComponent`/`XenoBoxerSystem` pair under `Content.Shared/_RMC14/Xenonids/Boxer`, with dedicated action event types so the generic `XenoPunchSystem` remains unchanged.

The server-authoritative component stores the current KO target, KO meter, last-hit time, Clear Head charges, and next charge regeneration time. A small pure rules layer exposes threshold and timer calculations so the historical numbers can be covered by unit tests without requiring a live entity map.

## Historical contracts to preserve

- Boxer is a Warrior strain with +60 health thresholds and +5 xeno armor.
- Fling and Lunge are removed; Punch, Jab, and Uppercut are available.
- Normal melee hits add 0.5 KO; Boxer Punch and Jab add 1 KO.
- KO is capped at 15, tied to one target, and resets after target change or five seconds without a qualifying hit.
- Uppercut requires the current KO target to be adjacent, applies the historical thresholded damage/knockback/knockdown/knockout effects, heals based on KO, then resets KO and relevant cooldowns.
- Clear Head starts with three charges, regenerates one charge every 15 seconds, cancels ordinary daze/stun/knockdown effects, and does not cancel forced effects. Xeno-vs-xeno Boxer starts with zero charges.
- Punch/Jab cooldown interactions follow the historical 20% reduction/reset behavior.
- Legacy target-specific punch damage branches are represented in the Boxer action code rather than changing generic punch behavior.

## RMC adaptations

- RMC status APIs are used for Dazed, Stun, KnockedDown, and Unconscious effects. `force` bypasses the existing `BeforeStatusEffectAddedEvent`, preserving Clear Head's forced-effect exception.
- RMC `RMCSizeStunSystem.KnockBack` is used for Uppercut. The old BYOND explosion throw power is mapped to bounded RMC throw distance/speed values rather than interpreted as literal tiles.
- The existing Boxer sprite is reused. It contains alive/sleeping/crit/dead states but no dedicated walk/run states.
- KO and Clear Head state are networked. The owner-facing HUD uses the existing xeno HUD/alert conventions where possible; no global target information is exposed to other players.

## Files

- `Content.Shared/_RMC14/Xenonids/Boxer/XenoBoxerComponent.cs` — networked state and tunable defaults.
- `Content.Shared/_RMC14/Xenonids/Boxer/XenoBoxerRules.cs` — pure KO/Clear Head/Uppercut rules.
- `Content.Shared/_RMC14/Xenonids/Boxer/XenoBoxerActionEvents.cs` — Boxer Punch, Jab, and Uppercut events.
- `Content.Shared/_RMC14/Xenonids/Boxer/XenoBoxerSystem.cs` — melee tracking, actions, status immunity, cooldowns, healing, and knockback.
- `Content.Tests/Shared/_RMC14/Xenonids/XenoBoxerRulesTest.cs` — red/green unit coverage for the rules layer.
- `Resources/Prototypes/_RMC14/Entities/Mobs/Xeno/strain_additions.yml` — `CMXenoWarriorBoxer` prototype.
- `Resources/Prototypes/_RMC14/Entities/Mobs/Xeno/warrior.yml` — register Boxer in Warrior strain evolution.
- `Resources/Prototypes/_RMC14/Actions/Xeno/xeno_offense_actions.yml` — action prototypes and cooldowns.
- `Resources/Locale/en-US/_RMC14/xeno/xeno-strains.ftl` and action localization — Boxer text.

## Verification

Unit tests will cover KO cap, target switching, inactivity reset, Uppercut thresholds, Clear Head charges/regen/forced behavior, and the xeno-vs-xeno charge rule. The implementation will be validated with the focused NUnit test filter, YAML/prototype validation through the Content test project, and a final `dotnet build`/`dotnet test` run appropriate to the repository state.

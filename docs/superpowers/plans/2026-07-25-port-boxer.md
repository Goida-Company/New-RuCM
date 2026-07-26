# Boxer Warrior strain port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port the historical CMSS13 Boxer Warrior strain to CMU/RMC as an isolated, test-covered strain with dedicated actions and stateful KO/Clear Head behavior.

**Architecture:** Add a `CMXenoWarriorBoxer` derived entity prototype and three dedicated action event types. Keep generic Warrior/Punch systems unchanged; implement Boxer state and effects in `XenoBoxerComponent`, `XenoBoxerRules`, and `XenoBoxerSystem`, with client-visible state following existing networked xeno conventions.

**Tech Stack:** C# shared entity systems, Robust Entity prototypes/YAML, NUnit, Fluent localization, existing RMC xeno action/status/damage/knockback APIs.

## Global Constraints

- Work only in branch `codex/port-boxer`.
- Preserve existing unrelated changes in the primary checkout and do not modify generic Warrior/Punch behavior unless a compile-safe integration point requires it.
- Use historical Boxer values: +60 health thresholds, +5 armor, KO max 15, five-second KO timeout, Clear Head max 3, fifteen-second charge regeneration.
- Production behavior must be preceded by a focused failing test for the pure rules being introduced.
- Do not expose KO target/meter state to other players through global overlays.

---

### Task 1: Establish the pure Boxer rules contract

**Files:**
- Create: `Content.Shared/_RMC14/Xenonids/Boxer/XenoBoxerRules.cs`
- Create: `Content.Tests/Shared/_RMC14/Xenonids/XenoBoxerRulesTest.cs`

**Interfaces:**
- Produces pure functions for KO accumulation/reset, Uppercut stage selection, Clear Head charge use/regen, and the xeno-vs-xeno starting-charge rule.
- Later tasks consume these functions from `XenoBoxerSystem`.

- [ ] **Step 1: Write failing NUnit tests** for KO capping, target switching, timeout reset, Uppercut thresholds at 1/3/6/9/15, Clear Head consumption, forced-effect non-consumption, regeneration, and xeno-vs-xeno zero starting charges.
- [ ] **Step 2: Run the focused test filter** and confirm it fails because `XenoBoxerRules` does not exist.
- [ ] **Step 3: Implement the smallest pure rules API** with explicit constants and no EntitySystem dependencies.
- [ ] **Step 4: Run the same focused filter** and confirm all rules tests pass.
- [ ] **Step 5: Refactor names/constants only while keeping the focused tests green.**

### Task 2: Add Boxer state and action event types

**Files:**
- Create: `Content.Shared/_RMC14/Xenonids/Boxer/XenoBoxerComponent.cs`
- Create: `Content.Shared/_RMC14/Xenonids/Boxer/XenoBoxerActionEvents.cs`

**Interfaces:**
- `XenoBoxerComponent` exposes networked KO target/meter and Clear Head state, plus data-backed timings and damage/effect tuning.
- Action events are `EntityTargetActionEvent` derivatives named `XenoBoxerPunchActionEvent`, `XenoBoxerJabActionEvent`, and `XenoBoxerUppercutActionEvent`.

- [ ] **Step 1: Add compile-focused tests** asserting the three events target entities and the component is networked/auto-state generated.
- [ ] **Step 2: Run the focused tests** and confirm they fail for missing types.
- [ ] **Step 3: Implement the component and event types** using existing RMC component/access patterns.
- [ ] **Step 4: Run the focused tests** and confirm they pass.

### Task 3: Implement melee KO tracking and Clear Head

**Files:**
- Create: `Content.Shared/_RMC14/Xenonids/Boxer/XenoBoxerSystem.cs`
- Modify: no generic status or melee systems unless required by compiler integration.

**Interfaces:**
- Subscribe to `MeleeHitEvent` on `XenoBoxerComponent` and add 0.5 KO for each real hit.
- Subscribe to `BeforeStatusEffectAddedEvent` and cancel ordinary Dazed, Stun, KnockedDown, and Unconscious effects while consuming one charge; forced legacy stun calls bypass this hook.
- Run server-authoritative timer updates for KO timeout and Clear Head regeneration.

- [ ] **Step 1: Add failing tests** for a real melee hit adding 0.5, a second target resetting the meter, timeout clearing the target, and status immunity consuming only one charge per status attempt.
- [ ] **Step 2: Run the focused tests** and confirm the behavior is absent.
- [ ] **Step 3: Implement event handlers and authoritative update logic** using the existing timing, network dirtying, and status APIs.
- [ ] **Step 4: Run focused tests plus the existing xeno tests** and confirm no regressions.

### Task 4: Implement Boxer Punch, Jab, and Uppercut

**Files:**
- Modify: `Content.Shared/_RMC14/Xenonids/Boxer/XenoBoxerSystem.cs`
- Modify: `Content.Shared/_RMC14/Xenonids/Boxer/XenoBoxerComponent.cs`

**Interfaces:**
- Boxer Punch validates the target through existing xeno attack rules, applies historical damage branches and hit effects, adds 1 KO, and reduces Jab cooldown by 20%.
- Jab moves/attacks according to historical range and timing, applies Daze/slow without generic Punch side effects, adds 1 KO, and reduces Punch cooldown by 20%.
- Uppercut validates the current KO target and adjacency, applies stage effects and heal, then resets meter and action cooldowns.

- [ ] **Step 1: Add failing tests** for Punch/Jab KO increments, cooldown reduction, Uppercut target validation, each Uppercut threshold, meter reset, and xeno-vs-xeno healing multiplier.
- [ ] **Step 2: Run focused tests** and verify they fail for the missing handlers.
- [ ] **Step 3: Implement the minimal action handlers** using `_xeno.CanAbilityAttackTarget`, `DamageableSystem`, `RMCDazedSystem`, `SharedStunSystem`, `RMCSizeStunSystem`, and `SharedRMCActionsSystem`.
- [ ] **Step 4: Run focused tests** and then the relevant xeno action tests.
- [ ] **Step 5: Refactor duplicate cooldown/target validation helpers** while retaining green tests.

### Task 5: Register actions and the derived strain prototype

**Files:**
- Modify: `Resources/Prototypes/_RMC14/Actions/Xeno/xeno_offense_actions.yml`
- Modify: `Resources/Prototypes/_RMC14/Entities/Mobs/Xeno/strain_additions.yml`
- Modify: `Resources/Prototypes/_RMC14/Entities/Mobs/Xeno/warrior.yml`

**Interfaces:**
- New action prototypes expose the dedicated Boxer event types with historical delays/ranges and RMC action ordering.
- `CMXenoWarriorBoxer` inherits `CMXenoWarrior`, overrides action IDs, strain metadata, thresholds, armor, sprite, and `XenoBoxer` component.
- Base Warrior lists `CMXenoWarriorBoxer` under `XenoEvolution.strains`.

- [ ] **Step 1: Add a failing prototype test** that loads the Boxer entity and asserts its strain, action IDs, thresholds, armor, and absence of Fling/Lunge.
- [ ] **Step 2: Run the prototype test** and verify the prototype is missing.
- [ ] **Step 3: Add the action definitions and derived entity YAML** using Bulwark as the inheritance/action-list pattern.
- [ ] **Step 4: Run the focused prototype test** and confirm it passes.

### Task 6: Add localization and owner-facing state presentation

**Files:**
- Modify: `Resources/Locale/en-US/_RMC14/xeno/xeno-strains.ftl`
- Modify or create: the existing RMC xeno action localization file selected by the repository's naming convention.
- Create or modify: the smallest existing xeno HUD/alert integration needed to show Boxer KO/Clear Head state to the Boxer owner only.

**Interfaces:**
- Strain, action names, descriptions, popups, and status text are localized.
- KO target/meter and Clear Head charges are visible to the Boxer owner without leaking target state globally.

- [ ] **Step 1: Add failing localization/prototype coverage** for every referenced LocId and action icon state.
- [ ] **Step 2: Run the focused content tests** and confirm missing localization/prototype keys fail.
- [ ] **Step 3: Add localization and reuse the existing xeno HUD/alert path** rather than introducing a new global overlay.
- [ ] **Step 4: Run focused content tests and client compilation.**

### Task 7: Verify the complete branch

**Files:**
- Modify only files required by compiler/test diagnostics from Tasks 1–6.

- [ ] **Step 1: Run the focused Boxer rules/prototype tests.**
- [ ] **Step 2: Run the existing CMU xeno tests that share action/status/knockback code.**
- [ ] **Step 3: Build `Content.Shared`, `Content.Server`, `Content.Client`, and `Content.Tests` with the repository's normal command.**
- [ ] **Step 4: Inspect `git diff --check` and `git status --short` for accidental files or whitespace errors.**
- [ ] **Step 5: Commit the implementation with a focused message** such as `feat: port boxer warrior strain`.

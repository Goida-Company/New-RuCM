# Yautja Localization and Hardcode Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fully localize the Yautja feature for Russian and remove direct English user-facing strings from its code and action definitions.

**Architecture:** Keep localization data in the existing `_CMU14/yautja` Fluent files. Replace user-facing C# defaults and action prototype text with stable localization IDs, resolving them through `Loc.GetString` at the point where text is shown; preserve dynamic values and markup as Fluent arguments. Keep prototype fallback names/descriptions unchanged where the engine already resolves `ent-<prototype>-name/desc` locale overrides, and verify actual visible text through key-parity and hardcode scans.

**Tech Stack:** Fluent (`.ftl`), YAML prototypes, C# RobustToolbox localization APIs, PowerShell/`rg` validation, dotnet build/test commands.

## Global Constraints

- Preserve every Fluent key, variable, markup tag, and event/prototype identifier.
- Do not modify the user's unrelated dirty files in the main checkout.
- Russian terminology must follow the existing Yautja locale: «яутжа», «наруч», «биомаска», «трофей», «добыча», «кровавый ритуал».
- Any direct user-facing text left in Yautja code must be either a player-provided/dynamic value, an intentional proper name, a numeric choice, or an admin/developer diagnostic.

---

### Task 1: Establish the missing-key and hardcode baseline

**Files:**
- Read: `Resources/Locale/en-US/_CMU14/yautja/*.ftl`
- Read: `Resources/Locale/ru-RU/_CMU14/yautja/*.ftl`
- Read: `Content.Shared/_CMU14/Yautja/*.cs`
- Read: `Content.Server/_CMU14/Yautja/*.cs`
- Read: `Content.Client/_CMU14/Yautja/*.cs`
- Read: `Resources/Prototypes/_CMU14/Threats/Yautja/Actions/actions.yml`

**Interfaces:**
- Produces the exact missing Fluent key list, code-referenced key list, and direct user-facing string list used by later tasks.

- [x] **Step 1: Run the baseline key-parity check**

Compare keys in the three English Yautja locale files with all six Russian Yautja locale files and record missing Russian keys; separately check literal `Loc.GetString` IDs referenced by Yautja C#.

- [x] **Step 2: Run the baseline hardcode scan**

Scan Yautja C# for `PushMarkup`, `OpenOptions`, popup/chat calls, and component defaults containing English prose; scan action prototypes for literal `name`/`description` values.

- [x] **Step 3: Confirm the baseline is non-zero**

Expected baseline: 464 missing Russian Yautja Fluent keys, one missing English key (`cmu-yautja-hivebreaker-requires-recent-death`), literal action labels/descriptions, and direct C# user-facing prose.

### Task 2: Complete Yautja Fluent localization

**Files:**
- Modify: `Resources/Locale/en-US/_CMU14/yautja/yautja.ftl`
- Modify: `Resources/Locale/ru-RU/_CMU14/yautja/yautja.ftl`

**Interfaces:**
- Produces complete English/Russian coverage for the current Yautja message keys, including the Hivebreaker key referenced by server code.

- [x] **Step 1: Add the missing English Hivebreaker fallback**

Add `cmu-yautja-hivebreaker-requires-recent-death` beside the existing Hivebreaker requirement messages, preserving the wording used by the code path.

- [x] **Step 2: Add Russian translations for every currently missing key**

Translate all 464 missing entries, keeping every `{$variable}`, `[bold]...[/bold]`, color tag, and escaped Fluent attribute intact. Include radio labels, menus, action descriptions, equipment feedback, hunting consoles, thrall/youngblood flows, and dynamic combat/examine messages.

- [x] **Step 3: Validate Fluent parity and syntax**

Run the repository's available locale validation/build command and a custom key/variable/markup comparison. Expected result: no missing Russian keys among current English Yautja keys, no duplicate keys, and no variable/markup mismatches.

### Task 3: Verify action prototype localization overrides

**Files:**
- Modify: `Resources/Prototypes/_CMU14/Threats/Yautja/Actions/actions.yml`
- Modify: `Resources/Locale/en-US/_CMU14/yautja/yautja.ftl`
- Modify: `Resources/Locale/ru-RU/_CMU14/yautja/yautja.ftl`

**Interfaces:**
- Action prototypes consume Fluent IDs instead of literal English prose; the client action system resolves these IDs to the active language.

- [x] **Step 1: Verify each Yautja action `name`/`description` has an `ent-<prototype>` locale override**

Cover visor, mask zoom, cloak, light, bracer controls, tracking, translator/audio panel, crystals, traps, marks, thralls, recall/self-destruct, weapons, leap/gorge, owner finding, and hunt marking. Leave already-localized action IDs unchanged.

- [x] **Step 2: Add missing Russian action overrides and translations where needed**

Use concise imperative Russian labels and descriptions matching existing Yautja terminology.

- [x] **Step 3: Verify no visible action fallback lacks EN/RU coverage**

Scan `actions.yml` for Yautja action `name`/`description` values that are not localization IDs and confirm all new IDs exist in both locales.

### Task 4: Remove direct C# user-facing Yautja strings

**Files:**
- Modify: `Content.Shared/_CMU14/Yautja/YautjaComponents.cs`
- Modify: `Content.Shared/_CMU14/Yautja/YautjaPowerSystem.cs`
- Modify: `Content.Shared/_CMU14/Yautja/YautjaCasterSystem.cs`
- Modify: `Content.Shared/_CMU14/Yautja/YautjaSpikeLauncherSystem.cs`
- Modify: `Content.Server/_CMU14/Yautja/YautjaCannonPackSystem.cs`
- Modify: `Content.Server/_CMU14/Yautja/YautjaPlasmaWeaponSystem.cs`
- Modify: `Content.Server/_CMU14/Yautja/YautjaItemSystem.cs`
- Modify: `Content.Server/_CMU14/Yautja/YautjaAttachmentSystem.cs`
- Modify: `Content.Server/_CMU14/Yautja/YautjaHuntConsoleSystem.cs`
- Modify: `Content.Server/_CMU14/Yautja/YautjaChainGauntletSystem.cs`
- Modify: `Content.Server/_CMU14/Yautja/YautjaTrophySystem.cs`
- Modify: `Content.Server/_CMU14/Yautja/YautjaThrallSystem.cs`
- Modify: `Content.Server/_CMU14/Yautja/YautjaTrapSystem.cs`
- Modify: `Resources/Locale/en-US/_CMU14/yautja/yautja.ftl`
- Modify: `Resources/Locale/ru-RU/_CMU14/yautja/yautja.ftl`

**Interfaces:**
- Component text fields become `LocId` where they are configured localization messages; systems call `Loc.GetString` before pushing markup, chat, or dialogs.

- [x] **Step 1: Add failing hardcode assertions to the audit command**

The audit must fail if Yautja user-facing calls contain English prose or if configured message fields remain raw English defaults.

- [x] **Step 2: Convert component defaults and configured message fields to localization IDs**

Cover reinforced/damaged examine text, Hivebreaker consent title/message, scalp description, chain-gauntlet speech, and plasma fire-mode examine text without changing network-visible behavior or dynamic data.

- [x] **Step 3: Replace direct examine/popup/dialog prose with `Loc.GetString`**

Cover charge/spike counts, caster mode, bracer attachment/bad-blood text, shoulder placement, chain-gauntlet help, escape-console Open/Close, bracer slot Right/Left, Hivebreaker Yes/No, and the generated scalp narrative.

- [x] **Step 4: Run focused compilation/tests**

Build the shared/server/client projects or run the repository's focused Yautja test target. Expected result: no type/serialization errors after `string` to `LocId` changes and no hardcoded user-facing Yautja call sites remain.

### Task 5: Localize Yautja lobby profile display names

**Files:**
- Modify: `Content.Shared/_CMU14/Yautja/YautjaCharacterProfile.cs`
- Modify: `Content.Client/_CMU14/Yautja/Lobby/YautjaProfileEditor.cs`
- Modify: `Resources/Locale/en-US/_CMU14/yautja/yautja.ftl`
- Modify: `Resources/Locale/ru-RU/_CMU14/yautja/yautja.ftl`

**Interfaces:**
- Shared profile data exposes stable display-key data; client lobby code resolves it with `Loc.GetString` before putting text in controls.

- [x] **Step 1: Add a focused test/validation for profile labels**

Enumerate every `YautjaCharacterProfile.Get*DisplayName` output used by the lobby and assert that the final UI string is localized through a known key rather than an English suffix/interpolation.

- [x] **Step 2: Replace English suffix composition with localization templates**

Localize legacy/unique/clan armor, masks, greaves, bracers, caster names, cape styles, mask accessories, materials, translator/sound modes, legacy/unique set names, skin/eye/quill labels, and pattern numbers.

- [x] **Step 3: Verify the lobby source contains no English display prose**

Scan profile display helpers and their callers; allow only localization IDs, enum/prototype IDs, and dynamic values.

### Task 6: Full verification and handoff

**Files:**
- Read: all changed files
- Verify: `docs/superpowers/specs/2026-07-25-yautja-localization-design.md`

- [x] **Step 1: Run key/variable/markup/duplicate checks**

Expected: zero duplicate locale keys, zero missing Russian keys for English Yautja keys, zero code-referenced missing keys, and zero Fluent argument/markup mismatches.

- [x] **Step 2: Run hardcode scans**

Expected: no direct English prose in user-facing Yautja C# calls, no literal Yautja action labels/descriptions, and only intentional diagnostics/dynamic/proper-name strings remain.

- [x] **Step 3: Run the proportional repository build/test**

Use the repository's available validation target; report exact command and result, including any unrelated pre-existing failure.

- [x] **Step 4: Review the diff and commit the completed localization**

Confirm only the isolated worktree changes are included, then commit with a focused message such as `fix: complete Yautja Russian localization`.

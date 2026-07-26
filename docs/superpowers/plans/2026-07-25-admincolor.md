# Admin group OOC colors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task with review checkpoints.

**Goal:** Add a Host-only F7 EUI for assigning OOC chat colors to existing admin groups, with live application to online members and a personal-color fallback.

**Architecture:** Store a nullable normalized HEX color on `AdminRank`; hydrate it into shared `AdminData` when admin permissions load; resolve group color before personal `AdminOOCColor`; expose CRUD-for-color only through a Host-validated EUI and an `adminooccolors` command in the existing AdminTab.

**Tech Stack:** C#, RobustToolbox EUI, NetSerializable messages, EF Core migrations for SQLite/PostgreSQL, NUnit, FluentLocalization.

## Global Constraints

- Work only on branch `feat/admincolor`.
- Preserve pre-existing unrelated changes in `RobustToolbox`, `.codex_make_warrior_preview.py`, `cmss13-ref/`, and `cmss13-ref-full/`; never stage them.
- The UI may show existing `AdminRank` records but must not create/delete groups or edit group membership/flags.
- `Host` is enforced on the server at command execution, EUI open, and color update.
- `AdminRank.OOCColor` is nullable; empty input clears it.
- Normalize valid colors through `Color.ToHex()` before persistence and transmission.
- Group color takes precedence over personal `PlayerPreferences.AdminOOCColor`; personal color remains the fallback only for admins with `AdminFlags.NameColor`.
- Run focused tests after each testable slice and a solution-level build or the narrowest viable project build before completion.

## Task 1: Add and test the color resolution rule

**Files:**
- Create `Content.Shared/_AU14/Administration/AdminOOCColorResolver.cs`
- Create `Content.Tests/Shared/Administration/AdminOOCColorResolverTest.cs`

1. Write failing NUnit tests for: group color wins, personal color is used when group color is absent and `NameColor` is present, and no override is returned otherwise. Include an invalid group HEX case that falls back safely.
2. Run `dotnet test Content.Tests/Content.Tests.csproj --filter FullyQualifiedName~AdminOOCColorResolverTest` and confirm the new tests fail because the resolver does not exist or does not implement the rule.
3. Implement a small pure resolver returning `Color?`, parsing only valid stored HEX values and applying the precedence in the design.
4. Re-run the focused test filter and confirm it passes.

## Task 2: Persist group OOC colors and hydrate admin state

**Files:**
- Modify `Content.Server.Database/Model.cs`
- Modify `Content.Server/Database/ServerDbBase.cs`
- Modify `Content.Shared/Administration/AdminData.cs`
- Modify `Content.Server/Administration/Managers/AdminManager.cs`
- Create SQLite and PostgreSQL migration files under `Content.Server.Database/Migrations/`

1. Add nullable `AdminRank.OOCColor` and nullable shared `AdminData.OOCColor`.
2. Update `UpdateAdminRankAsync` to save the color together with rank name and flags.
3. Load `dbData.AdminRank?.OOCColor` into `AdminData` for normal database admins; special-login admins remain without a group color.
4. Add matching nullable `ooc_color` columns to SQLite and PostgreSQL migrations with reversible `Down` methods. Keep provider-specific migration namespaces and metadata consistent with adjacent migrations.
5. Build the shared/server/database projects enough to catch model, migration, and serialization errors.

## Task 3: Apply group colors in OOC chat

**Files:**
- Modify `Content.Server/Chat/Managers/ChatManager.cs`

1. Replace the current personal-only OOC color selection with the shared resolver using current `AdminData` and cached player preferences.
2. Preserve the admin OOC enable/disable gate and all existing message construction behavior.
3. Run the focused resolver tests and a server build.

## Task 4: Add the Host-only EUI protocol and server implementation

**Files:**
- Create `Content.Shared/_AU14/Administration/AdminOOCColorEuiState.cs`
- Create `Content.Server/_AU14/Administration/AdminOOCColorEui.cs`
- Create `Content.Server/_AU14/Administration/AdminOOCColorCommand.cs`

1. Define a NetSerializable EUI state containing existing group IDs, names, and nullable colors, plus an update message containing rank ID and nullable HEX color.
2. Implement the server EUI with async DB loading, state refresh, Host checks, HEX validation/normalization, rank update, `ReloadAdminsWithRank`, and close-on-permission-loss behavior.
3. Implement the `adminooccolors` console command with `[AdminCommand(AdminFlags.Host)]` that opens the EUI for the invoking player.
4. Ensure stale/unknown rank IDs and invalid colors are rejected without mutating the database.
5. Build `Content.Server` and `Content.Shared`.

## Task 5: Add the F7 client window and localization

**Files:**
- Create `Content.Client/_AU14/Administration/AdminOOCColorEui.cs`
- Create `Content.Client/_AU14/Administration/AdminOOCColorWindow.cs`
- Modify `Content.Client/Administration/UI/Tabs/AdminTab/AdminTab.xaml`
- Create `Resources/Locale/en-US/administration/ui/tabs/admin-tab/admin-ooc-colors.ftl`
- Create `Resources/Locale/ru-RU/administration/ui/tabs/admin-tab/admin-ooc-colors.ftl`

1. Add the command button to the existing AdminTab; rely on `CommandButton` command visibility for non-Host admins.
2. Implement the client EUI lifecycle and a compact window listing every existing group with HEX input, color preview, save, and reset controls.
3. Validate input client-side for usability but keep server validation authoritative.
4. Add English and Russian strings for the button, title, controls, empty state, and invalid color message.
5. Build `Content.Client` and run the focused tests.

## Task 6: Final verification and handoff

1. Inspect `git diff` and `git status --short` to ensure only intended files are staged/changed.
2. Run focused resolver tests, then the relevant `Content.Server`, `Content.Client`, `Content.Shared`, and `Content.Server.Database` builds (or the repository’s available aggregate build command).
3. Check migration naming/provider namespaces and confirm unrelated pre-existing files remain untouched.
4. Report implementation, verification results, branch name, and any limitations.

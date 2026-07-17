# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 wray-git
# SPDX-License-Identifier: AGPL-3.0-only
# Building overhaul (z-level) - Phase 1: structural support graph
au-zsupport-unsupported = This section is no longer supported!
au-zsupport-admin-alert = Z-level structure collapsed (lost support) - likely caused by { $culprit }.

# Building overhaul (z-level) - underground cave-ins
au-cavein-warning = The ceiling here groans and cracks - it's about to cave in!
au-cavein-admin-alert = Underground cave-in ({ $count } tiles) - likely caused by { $culprit }.

# Building overhaul (z-level) - structural scanner
au-scanner-on = You switch the structural scanner on.
au-scanner-off = You switch the structural scanner off.

# Building overhaul (z-level) - mapper opt-out condition
construction-step-condition-au14-zbuild-allowed = Vertical building must be allowed on this map.

## Z-Level Toggles admin tool (construction menu > Tools)
au-zlevel-toggles-title = Z-Level Toggles
au-zlevel-toggles-search = Search maps...
au-zlevel-toggles-hint = Yes = players can z-build on this map. Persists across rounds.
au-zlevel-toggles-yes = Yes
au-zlevel-toggles-no = No
au-zlevel-toggles-map-loaded = {$map} (loaded)
au-zlevel-toggle-enabled = Z-level building ALLOWED on {$map}.
au-zlevel-toggle-disabled = Z-level building DENIED on {$map}.

## Debug and admin commands
cmd-au-zsupport-desc = Recompute the z-level structural support graph and report supported/unsupported counts.
cmd-au-zsupport-help = Usage: au_zsupport [all]
cmd-au-zsupport-recomputed-all = Recomputed { $grids } grid(s).
cmd-au-zsupport-player-only = Run this as an in-game player, or use 'au_zsupport all'.
cmd-au-zsupport-not-on-grid = You are not standing on a grid. Try 'au_zsupport all'.
cmd-au-zsupport-recomputed-grid = Recomputed your grid { $grid }.
cmd-au-zsupport-report = { $prefix } Supports: { $supported } supported, { $unsupported } unsupported.

cmd-au-dig-player-only = This command must be run by an in-game player.
cmd-au-digup-desc = Dig straight up one z-level, surfacing at your current horizontal position.
cmd-au-digup-help = Usage: au_digup
cmd-au-digup-success = Dug up a level.
cmd-au-digup-failed = Could not dig up here (nothing above, a wall blocks the spot above, or the feature is disabled).
cmd-au-digdown-desc = Dig straight down, creating/descending into a stone z-level beneath you.
cmd-au-digdown-help = Usage: au_digdown
cmd-au-digdown-success = Dug down a level.
cmd-au-digdown-failed = Could not dig down here (map opted out, feature disabled, or a hand-authored level is already below).

cmd-au-multiz-desc = List maps with their AU14 Multi Z-Level (vertical building) status, or toggle it per map / globally.
cmd-au-multiz-help = au_multiz (list) | au_multiz <mapId> <on|off> | au_multiz global <on|off>
cmd-au-multiz-enabled = ENABLED
cmd-au-multiz-disabled = DISABLED
cmd-au-multiz-yes = Yes
cmd-au-multiz-no = No
cmd-au-multiz-global-status = Global AU14 z-building: { $state } (toggle: au_multiz global on|off)
cmd-au-multiz-map-status = MapId { $id } { $map } - Multi Z-Level: { $state }
cmd-au-multiz-usage = Usage: au_multiz <mapId|global> <on|off>
cmd-au-multiz-invalid-state = Second argument must be 'on' or 'off'.
cmd-au-multiz-global-changed = Global AU14 z-building is now { $state }.
cmd-au-multiz-invalid-map = Map argument must be a numeric MapId (run 'au_multiz' to list them) or 'global'.
cmd-au-multiz-map-not-found = No map with MapId { $id }.
cmd-au-multiz-can-build = can now
cmd-au-multiz-cannot-build = can no longer
cmd-au-multiz-map-changed = Map { $id } Multi Z-Level set to { $state }. Players { $permission } build AU14 z-level stairs/floors here.

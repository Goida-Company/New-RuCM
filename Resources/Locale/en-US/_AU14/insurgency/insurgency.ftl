# INSFOR faction featureset

# Shown to each member when their faction definition is applied for the round.
insfor-faction-applied-popup = Your cell has been organized under { $title }.

# Debug apply command feedback.
cmd-insforapplytest-desc = Applies a minimal test INSFOR faction so the apply pipeline can be checked in-game.
cmd-insforapplytest-help = Usage: insforapplytest [title]
cmd-insforapplytest-applied = Applied test INSFOR faction "{ $title }" to { $count } member(s).
cmd-insforapplytest-default-title = Test Liberation Cell
cmd-insforapplytest-default-description = A ragtag cell testing the INSFOR apply pipeline.
cmd-insforapplytest-default-roleplay = Play it scrappy and improvised. You are locals, not soldiers.

cmd-insforeditor-desc = Opens the INSFOR Default-faction editor.
cmd-insforeditor-help = Usage: insforeditor
cmd-insforeditor-player-only = This command can only be run by a player.
cmd-insforeditor-not-whitelisted = You are not whitelisted for the INSFOR editor.

cmd-insforfactiondbtest-desc = Saves, reads back, and deletes a test faction to verify the DB round-trip.
cmd-insforfactiondbtest-help = Usage: insforfactiondbtest
cmd-insforfactiondbtest-title = DB Round-Trip Test
cmd-insforfactiondbtest-description = Written by insforfactiondbtest.
cmd-insforfactiondbtest-roleplay = Delete me if I linger.
cmd-insforfactiondbtest-saved = Saved test faction with id { $id }.
cmd-insforfactiondbtest-read-error = ERROR: could not read the faction back.
cmd-insforfactiondbtest-read = Read back: "{ $title }" (schema v{ $version }).
cmd-insforfactiondbtest-deleted = Deleted the test faction. Round-trip OK.
cmd-insforfactiondbtest-delete-error = ERROR: delete reported no row.
cmd-insforfactiondbtest-failed = DB round-trip failed: { $message }

# A Package loadout delivery.
insfor-a-package-received = You have received a package. Use it in hand when you are ready.

# Heavy Cell Kit deployment.
insfor-cell-kit-title = Heavy Cell Kit
insfor-cell-kit-deploy = Deploy
insfor-cell-kit-no-faction = The cell has no orders yet. Wait until your faction is organized.
insfor-cell-kit-empty = The cell kit is empty.
insfor-cell-kit-deployed = You set out a piece of the cell's equipment. { $remaining } left.

# Leader faction selection popup.
insfor-select-title = Choose Your Cell's Faction
insfor-select-default-header = Factions (click a name to see details)
insfor-select-govfor = Opposing GOVFOR faction: { $name }
insfor-select-govfor-unknown = Opposing GOVFOR faction: not chosen yet
insfor-select-empty = No factions are available.
insfor-select-not-opposed = Does not oppose this round's GOVFOR faction.
insfor-select-choose = Choose this faction
insfor-select-untitled = (untitled faction)
insfor-select-unavailable-tag = [ unavailable this round ]
insfor-select-playstyle-header = Playstyle
insfor-select-cellkit-header = Cell kit contents
insfor-select-cellkit-empty = Nothing listed.

# In-viewport button to reopen the selection popup after it was closed.
insfor-reopen-faction-select-button = Choose Faction

# Faction reveal popup, shown to members once a faction is applied.
insfor-reveal-title = Your Faction
insfor-reveal-untitled = Unnamed Cell
insfor-reveal-roleplay-header = How to play this faction
insfor-reveal-about-header = About
insfor-reveal-close = Got it

# Faction editor pickers.
insfor-picker-search = Search...
insfor-picker-entity-title = Select an entity
insfor-picker-job-title = Select a job
insfor-picker-platoon-title = Select a GOVFOR faction (platoon)
insfor-picker-icon-title = Select a status icon
insfor-picker-flag-title = Select a flag

# Marker job used only as an INSFOR editor whitelist key.
au14-job-name-insfor-editor = INSFOR Editor Access

# Built-in vanilla CLF faction.
insfor-builtin-clf-title = Colonial Liberation Front
insfor-builtin-clf-description = The standard CLF cell. No special doctrine, no custom arsenal.
insfor-builtin-clf-roleplay = Play as a classic CLF insurgent cell.
insfor-builtin-clf-vendor-requisitions = CLF Requisitions Rack
insfor-builtin-clf-vendor-medical = CLF medical cache
insfor-builtin-clf-vendor-tools = CLF tool cache
insfor-builtin-clf-vendor-recruitment = CLF recruitment cache
insfor-builtin-clf-vendor-clothing = CLF civilian clothing rack
insfor-builtin-clf-section-first-aid = First Aid
insfor-builtin-clf-section-field-tools = Field Tools
insfor-builtin-clf-section-recruitment = Recruitment
insfor-builtin-clf-section-footwear = Footwear
insfor-builtin-clf-section-jumpsuits = Jumpsuits
insfor-builtin-clf-section-jackets = Jackets and Coats
insfor-builtin-clf-section-headwear = Headwear and Eyewear
insfor-builtin-clf-section-bags = Bags and Gloves

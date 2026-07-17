# INSFOR faction editor.
insfor-editor-title = INSFOR Faction Editor
insfor-editor-custom-title = INSFOR Custom Faction Editor
insfor-editor-help-button = Help - what do these fields mean?
insfor-editor-factions-heading = Factions
insfor-editor-new-faction = New faction
insfor-editor-export-template = Export blank sheet (for a player)
insfor-editor-import-sheet = Import filled sheet
insfor-editor-untitled-id = (untitled #{ $id })
insfor-editor-untitled = (untitled)
insfor-editor-editing = Editing: { $title }
insfor-editor-field-title = Title
insfor-editor-field-recruited-message = Recruited message
insfor-editor-field-description = Description
insfor-editor-field-roleplay-style = Roleplay style
insfor-editor-field-status-icon = Status icon
insfor-editor-field-dollars-rate = Dollars to points rate
insfor-editor-default-faction = Default faction (host-authored, DB stored)
insfor-editor-opposed-govfor = Opposed GOVFOR factions
insfor-editor-cell-kit-placeables = Cell kit: other placeable entities
insfor-editor-include-dollars = Also accept plain dollars for points
insfor-editor-tab-faction-info = Faction Info
insfor-editor-tab-economy = Economy
insfor-editor-tab-cell-kit = Cell Kit
insfor-editor-tab-vendors = Vendors
insfor-editor-tab-loadouts = Loadouts
insfor-editor-save-server-custom = Save (server / Custom)
insfor-editor-save-server-default = Save (server / Default)
insfor-editor-save-local-custom = Save as local Custom
insfor-editor-faction-file-name = faction
insfor-editor-export-sheet = Export to sheet
insfor-editor-apply-round = Apply for round
insfor-editor-delete = Delete
insfor-editor-clear = Clear
insfor-editor-add = + Add
insfor-editor-analyzer-heading = Analyzer: submittable for points (empty = plain dollars)
insfor-editor-items-per-point = items per point
insfor-editor-points-per-item = points per item
insfor-editor-placeholder-ratio = ratio
insfor-editor-add-submittable = + Add submittable item
insfor-editor-vendors-heading = Cell kit: vendors
insfor-editor-vendor-name = Vendor name
insfor-editor-vendor-base-model = Base model
insfor-editor-vendor-wrenchable = Wrenchable (can be wrenched down and moved)
insfor-editor-vendor-invulnerable = Invulnerable (base entity won't break / change on damage)
insfor-editor-vendor-intel-points = Uses cell intel points (money at the intel computer stocks this vendor)
insfor-editor-vendor-use-base-arsenal = Use base model's own arsenal (ignore the sections below)
insfor-editor-remove-vendor = Remove vendor
insfor-editor-add-vendor = + Add vendor
insfor-editor-sections = Sections
insfor-editor-section-name = Section name
insfor-editor-placeholder-per-player = per-player
insfor-editor-placeholder-global = global
insfor-editor-category-limit = Category limit
insfor-editor-remove-section = Remove section
insfor-editor-add-section = + Add section
insfor-editor-items-heading = Items (pick entity / points / amount / max)
insfor-editor-placeholder-points = points
insfor-editor-placeholder-amount = amount
insfor-editor-placeholder-max = max
insfor-editor-add-item = + Add item
insfor-editor-loadouts-heading = Role loadouts (A Package contents)
insfor-editor-role-job = Role (job)
insfor-editor-contents = Contents
insfor-editor-remove-loadout = Remove loadout
insfor-editor-add-loadout = + Add loadout
insfor-editor-job-icons-heading = Per-job status icons (empty = all jobs use the faction icon above)
insfor-editor-add-job-icon = + Add per-job icon
insfor-editor-machine-analyzer = Analyzer machine
insfor-editor-machine-intel-computer = CLF intel computer
insfor-editor-machine-objectives-console = CLF objectives console
insfor-editor-machine-tech-tree-console = CLF tech tree console
insfor-editor-machine-fax = Fax machine
insfor-editor-machines-heading = Default cell-kit machines
insfor-editor-choose = Choose...

# Faction spreadsheet.
insfor-sheet-faction = Faction
insfor-sheet-opposed-govfor = OpposedGovfor
insfor-sheet-job-icons = JobIcons
insfor-sheet-points-submissions = PointsSubmissions
insfor-sheet-placeables = Placeables
insfor-sheet-vendors = Vendors
insfor-sheet-vendor-sections = VendorSections
insfor-sheet-vendor-entries = VendorEntries
insfor-sheet-role-loadouts = RoleLoadouts
insfor-sheet-help = Help
insfor-sheet-header-field = Field
insfor-sheet-header-value = Value
insfor-sheet-header-platoon = Platoon
insfor-sheet-header-role = Role
insfor-sheet-header-icon = Icon
insfor-sheet-header-entity = Entity
insfor-sheet-header-points-per-item-mode = PointsPerItemMode
insfor-sheet-header-amount-per-point = AmountPerPoint
insfor-sheet-header-points-per-item = PointsPerItem
insfor-sheet-header-name = Name
insfor-sheet-header-base-model = BaseModel
insfor-sheet-header-wrenchable = Wrenchable
insfor-sheet-header-invulnerable = Invulnerable
insfor-sheet-header-uses-intel-points = UsesIntelPoints
insfor-sheet-header-use-base-sections = UseBaseModelSections
insfor-sheet-header-vendor = Vendor
insfor-sheet-header-section = Section
insfor-sheet-header-per-player-limit = PerPlayerLimit
insfor-sheet-header-global-limit = GlobalLimit
insfor-sheet-header-entity-id = EntityId
insfor-sheet-header-points = Points
insfor-sheet-header-amount = Amount
insfor-sheet-header-max = Max
insfor-sheet-header-content = Content
insfor-sheet-field-title = Title
insfor-sheet-field-description = Description
insfor-sheet-field-roleplay-text = RoleplayText
insfor-sheet-field-recruited-message = RecruitedMessage
insfor-sheet-field-status-icon = StatusIcon
insfor-sheet-field-flag-entity = FlagEntity
insfor-sheet-field-dollars-rate = DollarsToPointsRate
insfor-sheet-field-include-dollars = IncludeDollars
insfor-sheet-help-title = INSFOR Faction - how to fill this in
insfor-sheet-help-intro = Fill in the sheets, then send this file back to the host to import. Every id field is a dropdown: click the cell and pick by name - never type a raw id. Add a new entry on the next empty row of a sheet. Leave a sheet empty if the faction does not use it.
insfor-sheet-help-faction = Title: the faction name. Description / RoleplayText: shown in the briefing and reveal popup. RecruitedMessage: briefing a freshly recruited member reads (blank = default CLF line). StatusIcon: the membership icon. FlagEntity: an optional in-world flag prop. DollarsToPointsRate: how intel dollars convert to vendor points. IncludeDollars: TRUE keeps cash working even with custom submittables below.
insfor-sheet-help-opposed-govfor = The GOVFOR platoons (USMC, TWE RMC, UPP, ...) this faction may oppose. If the round's GOVFOR is listed, the faction is offered to the leader. One platoon per row.
insfor-sheet-help-job-icons = Optional per-job status-icon overrides: members of that Role show that Icon instead of the faction icon. One row per job.
insfor-sheet-help-points-submissions = What the analyzer accepts for points beyond plain cash. Entity is the item. PointsPerItemMode: FALSE = it takes AmountPerPoint of the item to make one point (cheap goods); TRUE = one item is worth PointsPerItem points (valuable goods). Leave empty to keep plain dollars only.
insfor-sheet-help-placeables = Single entities the leader can free-place from the Heavy Cell Kit (lamps, props, machines). One per row.
insfor-sheet-help-vendors = Each deployable vendor. Name: shown on the vendor. BaseModel: an existing vendor entity used only for its sprite; its arsenal is replaced by your sections. Wrenchable: can be moved after placing. Invulnerable: will not break on damage. UsesIntelPoints: paid from the cell's shared intel points. UseBaseModelSections: keep the base entity's own stock and ignore your sections (only for reusing a fully-made vendor like the CLF rack).
insfor-sheet-help-vendor-sections = Sections (categories) inside a vendor. Vendor must match a Name from the Vendors sheet. Section is the category name. PerPlayerLimit / GlobalLimit: optional caps on how many one player, or everyone together, may take from this category.
insfor-sheet-help-vendor-entries = Items inside a section. Vendor and Section must match rows above. EntityId is the item. Points: its cost (0 or blank = free). Amount: stock. Max: restock ceiling.
insfor-sheet-help-role-loadouts = Each role's 'A Package' contents, delivered after spawn. One row per item: pick the Role (job) and one Content entity. Repeat the same Role on several rows to give it several items.

# INSFOR faction editor help window.
insfor-editor-help-title = INSFOR Faction Editor - Help
insfor-editor-help-intro = An INSFOR faction is one insurgent cell the CLF leader can pick after spawning. You fill in who they are, what money buys them points, what their leader's Heavy Cell Kit can drop, and what each role gets in their "A Package". Nothing here needs a prototype id typed by hand: every entity, job, and icon is chosen from a searchable picker. The server re-checks and clamps everything you save, so you cannot break the round with a bad value.

insfor-editor-help-list-heading = The faction list (left) and the  *  mark
insfor-editor-help-list-body = The left column lists every saved faction plus the built-in vanilla CLF at the top. A faction shows a  *  next to its name when it is set to oppose the GOVFOR side the current round rolled, i.e. it is a valid pick this round. No star just means it does not target this round's GOVFOR; it is still fine to edit. Click a faction to edit it, or New faction to start blank.

insfor-editor-help-identity-heading = Identity
insfor-editor-help-identity-body = Title: the faction's name, shown in the pick list and the reveal popup.
    Recruited message: the briefing a freshly recruited member reads (for example via the tattoo gun). Blank uses the default CLF line.
    Description / Roleplay style: shown in the antag briefing and the reveal popup so members know who they are and how they are meant to play.
    Flag entity: an in-world flag prop, picked from the catalog (optional).
    Status icon: the faction membership icon members show to each other, picked from the icon list.

insfor-editor-help-default-heading = Default faction (checkbox)
insfor-editor-help-default-body = On: this faction is host-authored and saved in the server database; it is offered to leaders whose GOVFOR matches the Opposed list below. Off: it is a personal/Custom faction. The Save buttons at the bottom control where it is written.

insfor-editor-help-opposed-heading = Opposed GOVFOR factions
insfor-editor-help-opposed-body = The GOVFOR platoons (USMC, TWE RMC, UPP, and so on) this faction is allowed to oppose. If the round's GOVFOR is in this list, the faction is offered to the leader and gets the  *  in the list. Add as many as you like.

insfor-editor-help-economy-heading = Economy - dollars to points
insfor-editor-help-economy-body = Dollars to points rate: how intel dollars convert to the cell's vendor points.
    Also accept plain dollars: when ticked, cash still converts at the analyzer even if you add custom submittables below. Untick it for a faction whose economy should ignore money entirely.

insfor-editor-help-analyzer-heading = Analyzer - submittable for points
insfor-editor-help-analyzer-body = What the analyzer machine accepts and turns into cell points, beyond plain cash. Each row is an item (picked, never typed) and a ratio with two modes:
      - items per point: it takes that many of the item to make one point (good for cheap goods).
      - points per item: one item is worth that many points (good for valuable goods).
    Leave the list empty to keep the plain-dollars behavior. The value is always at least 1 so a submission can never mint free points.

insfor-editor-help-machines-heading = Default cell-kit machines
insfor-editor-help-machines-body = Tick the well-known CLF machines (analyzer, intel computer, objectives console, tech tree console, fax) you want the leader's Heavy Cell Kit to be able to place. Their money-to-points wiring is the normal CLF behavior; no extra setup is needed.

insfor-editor-help-placeables-heading = Cell kit - other placeable entities
insfor-editor-help-placeables-body = Any additional single entities the leader can free-place from the Heavy Cell Kit (lamps, barricades, props, and so on). Each is picked from the entity picker.

insfor-editor-help-vendors-heading = Cell kit - vendors
insfor-editor-help-vendors-body = Each vendor the leader can deploy from the kit. Per vendor:
      - Vendor name: the name shown on the deployed vendor and in the kit list.
      - Base model: an existing vendor entity used only for its sprite/collision; its arsenal is replaced by your sections.
      - Wrenchable: can be wrenched down and moved after placing.
      - Invulnerable: the placed vendor will not break or change on damage.
      - Uses cell intel points: items are paid from the cell's shared intel points (money at the intel computer stocks it) instead of the buyer's own points.
      - Use base model's own arsenal: ignore the sections below and keep the base entity's built-in stock. Only for reusing a fully-made vendor (like the CLF requisitions rack); leave off for a normal custom vendor.

insfor-editor-help-vendor-items-heading = Vendor sections and items
insfor-editor-help-vendor-items-body = A vendor is split into sections (categories). Per section:
      - Section name.
      - Category limit: two optional caps - how many one player may take from this category, and how many all players together may.
    Inside a section, each item row is:
      - the entity (picked),
      - points: its cost (0 = free),
      - amount: how many are in stock,
      - max: the ceiling it restocks to.
    Leave points blank to make an item free-by-stock only.

insfor-editor-help-loadouts-heading = Role loadouts - A Package
insfor-editor-help-loadouts-body = Because the faction is chosen after players spawn, each role's kit is delivered afterwards as an "A Package" box. Add a row per role: pick the Role (job) and the Contents (entities) it hands out.

insfor-editor-help-saving-heading = Saving and applying
insfor-editor-help-saving-body = Save (server / Default): writes it to the server database as a host faction.
    Save as local Custom: writes it to your machine only, so it shows up in the leader's Custom list.
    Apply for round: immediately applies this faction to the current round's cell.
    Delete: removes a saved faction (the built-in vanilla CLF cannot be deleted).

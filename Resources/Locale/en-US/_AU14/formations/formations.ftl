# Formation control window.
formation-window-title = Formation Control
formation-status-none = No active formation.
formation-status-planning = Placement active - click tiles to drop squad markers.
formation-status-active = Formation active - { $count } member(s) in formation.
formation-status-open-slots = { $count } open slot(s) - waiting for soldiers to report in.
formation-status-staged = Markers staged. Hit Activate when ready.
formation-status-start = No active formation. Start with Step 1.

formation-step-mark = Step 1 - Mark your positions
formation-place-marker = Place Squad Marker
formation-place-marker-tooltip = Place a position marker for any soldier. Click a tile to drop it; the arrow faces the direction your soldier is facing.
formation-undo-last = Undo Last
formation-staged-markers = Staged markers:
formation-pending-dot =   { $type } ({ $x }, { $y }) facing { $facing }
formation-dot-type-leader = [Leader]
formation-dot-type-squad = [Squad]
formation-none-staged =   (none staged)

formation-dot-lifetime-standard = Dot lifetime: Standard (2 min)
formation-dot-lifetime-extended = Dot lifetime: Extended (15 min)  [ACTIVE]
formation-dot-lifetime-tooltip = Standard: formation markers expire after 2 minutes - this is correct for almost every situation. Extended: markers last 15 minutes. Only activate this for prolonged static defensive positions where slots genuinely need to stay open for a long time.
formation-extended-warning-title = ! EXTENDED LIFETIME ACTIVE !
formation-extended-warning-duration = Markers will persist for 15 minutes on the map.
formation-extended-warning-rare = This is reserved for rare, prolonged static operations.
formation-extended-warning-abuse = Abuse of this mode WILL result in consequences.
formation-extended-warning-reset = Switch back to Standard as soon as possible.

formation-step-activate = Step 2 - Activate the formation
formation-activate = Activate
formation-activate-tooltip = Spawn all staged markers on the map. Soldiers have 2 minutes to walk up and slot in (or 15 minutes if extended lifetime is on).
formation-clear-staged = Clear Staged
formation-clear-staged-tooltip = Throw out all staged markers without spawning them.

formation-step-manage = Step 3 - Manage your formation
formation-march = March Formation
formation-halt = Halt Formation
formation-halt-tooltip = Halt stops your movement from being passed to the formation. March resumes it. The formation starts halted so you have time to brief before moving out.
formation-follow-mode = Follow mode
formation-hold = Hold
formation-hold-active = [Active] Hold
formation-hold-tooltip = Hold - followers move exactly one tile each time you do. Clean and synchronized on open ground.
    
    Tip: Switch to Chase first if people have fallen behind, then flip back to Hold once the formation is tight again.
formation-chase = Chase
formation-chase-active = [Active] Chase
formation-chase-tooltip = Chase - followers close toward their slot every tick regardless of whether you moved. Gaps close fast after turns or sprints.
    
    Tip: Best when reforming after a quick advance or when members got scattered. Flip back to Hold once everyone is back in position.

formation-collision = Collision
formation-collisions-off = Collisions: Off
formation-collisions-on = Collisions: On
formation-collisions-tooltip = Off - members can pass through each other freely, which keeps movement smooth in tight corridors. On restores normal physics so members block each other.
formation-remove-open-slots = Remove Open Slots
formation-remove-open-slots-tooltip = Delete any markers that haven't been claimed by a soldier yet.
formation-disband = Disband
formation-disband-tooltip = Kick everyone out of the formation and remove all markers.
formation-counts-zero = Open slots: 0  |  Members: 0
formation-counts = Open slots: { $slots }  |  Members: { $members }
formation-debug = Debug
formation-show-slots = Show Slot Positions
formation-hide-slots = Hide Slot Positions
formation-show-slots-tooltip = Show persistent white markers at each member's computed target position. Handy for checking formation shape after a turn.

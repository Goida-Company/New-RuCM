# Fix-O-Vein Case And Deployable Ladder Design

## Context

The repo already has a CMU Fix-O-Vein item prototype at `Resources/Prototypes/_CMU14/Medical/items/fix_o_vein.yml` with the prototype ID `CMUFixOVein`.

The base RMC surgical case lives in `Resources/Prototypes/_RMC14/Entities/Objects/Medical/surgical_case.yml`. Its storage whitelist already reserves a TODO for Fix-O-Vein, and the filled case prototype `CMSurgicalCaseFilled` contains the standard surgical tools.

CMU z-level ladder structures already exist in `Resources/Prototypes/_RMC14/Entities/Structures/rmc_ladder.yml`. The useful deployed prototypes are:

- `CMUZLevelLadderThroughUp3`: ladder that moves the user one Z level up.
- `CMUZLevelLadderThroughDown3`: ladder that moves the user one Z level down.

AU14 combat technician gear racks are defined in AU14 vendor files such as `Resources/Prototypes/_AU14/Entities/Vendors/marinevendors.yml`, `rmcvendors.yml`, and `uppvendors.yml`.

## Requirements

1. Corpsman surgical cases should include a filled Fix-O-Vein surgical case item by adding `CMUFixOVein` to the standard filled surgical case.
2. Surgical cases should be able to store `CMUFixOVein`.
3. AU14 combat technicians should be able to vend a deployable ladder from their combat technician vendor.
4. Deploying the ladder should create a usable z-level ladder pair:
   - an up ladder on the deployer's current Z level,
   - a down ladder at the matching location one Z level above.
5. Deployment must fail safely when no matching Z level exists or placement is blocked.

## Design

The medical change is data-only:

- Add the `CMUFixOVein` tag to the surgical case storage whitelist.
- Add `CMUFixOVein` to `CMSurgicalCaseFilled` contents.

The ladder change uses CMU-owned code and data:

- Add a shared CMU component for deployable z-level ladder items. The component stores the deployed lower and upper ladder prototype IDs.
- Add a server CMU system that handles tile interaction with the deployable item. Using the item in-hand will deploy at the user's current tile as a fallback. The system projects the target coordinates one Z level up using the existing CMU z-level API, validates both placement locations, spawns the lower up-ladder and upper down-ladder, then consumes the deployable item.
- Add a deployable ladder item prototype under CMU/AU14-owned prototypes. The item uses existing ladder sprites and names it as a deployable ladder.
- Add the deployable ladder to AU14 combat technician gear racks in their Engineering Supplies sections.

## Error Handling

Deployment will not consume the item unless both ladder entities can be placed.

The system will show a caution popup when:

- the current map is not part of a z-level network,
- there is no map one Z level above,
- either target tile is invalid or blocked,
- a ladder already occupies the target tile.

## Testing

Use test-driven development where a narrow automated test can cover the new code. The target behavior to test is that deployment requires a valid upper Z level and spawns both configured ladder prototypes only on success.

Run the narrowest available content/prototype validation after YAML changes. If the code path touches shared/server compilation, run the relevant build or targeted test project as well.

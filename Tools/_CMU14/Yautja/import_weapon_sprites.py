"""Losslessly import CMSS13 weapon states; --check verifies the actual pixels.

Usage: python Tools/_CMU14/Yautja/import_weapon_sprites.py cmss13-ref-full [--check]
Source revision: 52be8a8ceeb165c6a0becc77ecc3c9a644b88fd2.
"""
import argparse
import json
import re
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
TEXTURES = ROOT / "Resources/Textures/_CMU14/Yautja"
REVISION = "52be8a8ceeb165c6a0becc77ecc3c9a644b88fd2"
# local: object, in-hand, on-back (None means no source back state)
WEAPONS = {
    "harpoon": ("spike", "harpoon", None),
    "spike": ("spike", None, None),
    "chainwhip": ("whip", "whip", None),
    "clan_sword": ("clansword", "clansword", "clansword"),
    "rending_sword": ("clansword_alt", "clansword_alt", "clansword_alt"),
    "piercing_sword": ("clansword_alt2", "clansword_alt2", "clansword_alt2"),
    "severing_sword": ("clansword_alt3", "clansword_alt3", "clansword_alt3"),
    "dual_war_scythe": ("predscythe", "scythe_dual", "predscythe"),
    "double_war_scythe": ("predscythe_alt", "scythe_dual", "predscythe_alt"),
    "cruel_staff": ("staff", "staff", None),
    "combistick": ("combistick", "combistick", "combistick"),
    "combistick_folded": ("combistick_f", "combistick_f", None),
    "war_axe": ("war_axe", "war_axe", "war_axe"),
    "ceremonial_dagger": ("predknife", "knife", None),
    "hunter_spear": ("spearhunter", "spearhunter", "spearhunter"),
    "war_glaive": ("glaive_alt", "glaive_alt", "glaive_alt"),
    "cleaving_glaive": ("glaive", "glaive", "glaive"),
    "cleaving_glaive_skull": ("glaive_skull", None, "glaive_skull"),
    "ancient_war_glaive": ("glaive_alt", "glaive_alt", "glaive_alt"),
    "longaxe": ("longaxe", "longaxe", "longaxe"),
    "duelling_blade": ("duelling_sword", "duelling_sword", None),
    "duelling_club": ("duelling_club", "duelling_club", None),
    "duelling_hatchet": ("duelling_hatchet", "duelling_hatchet", None),
    "duelling_knife": ("duelling_knife", "duelling_knife", None),
    "clan_shield": ("shield", "shield", "shield"),
    "clan_shield_ready": ("shield_ready", "shield_ready", None),
}
WIELDED = {"combistick", "hunter_spear", "war_glaive", "cleaving_glaive", "ancient_war_glaive", "longaxe"}


def read_dmi(path):
    original = Image.open(path)
    data = original.info["Description"]
    image = original.convert("RGBA")
    width, height = [int(re.search(rf"\b{key} = (\d+)", data)[1]) for key in ("width", "height")]
    states, offset = {}, 0
    for block in re.split(r'\nstate = ', data)[1:]:
        name = re.match(r'"(.*?)"', block)[1]
        directions = int(re.search(r"dirs = (\d+)", block)[1])
        count = int(re.search(r"frames = (\d+)", block)[1])
        frames = []
        for index in range(offset, offset + directions * count):
            x, y = index % (image.width // width) * width, index // (image.width // width) * height
            frames.append(image.crop((x, y, x + width, y + height)))
        offset += directions * count
        if not re.search(r"movement = 1", block):
            delay = re.search(r"delay = ([^\n]+)", block)
            delays = [float(n) / 10 for n in delay[1].split(",")] if delay else [0.1] * count
            # DMI interleaves directions per frame; RSI groups frames per direction.
            frames = [frames[f * directions + d] for d in range(directions) for f in range(count)]
            states[name] = (directions, frames, delays)
    return states


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    source = args.source / "icons"
    obj = read_dmi(source / "obj/items/hunter/pred_gear.dmi")
    hands = {side: read_dmi(source / f"mob/humans/onmob/hunter/items_{side}hand.dmi") for side in ("left", "right")}
    back = read_dmi(source / "mob/humans/onmob/hunter/pred_gear.dmi")
    changes, checked = [], 0
    metadata = {}

    def transfer(folder, name, state):
        nonlocal checked
        directions, frames, delays = state
        path = TEXTURES / folder / f"{name}.png"
        expected = Image.new("RGBA", (frames[0].width * len(frames), frames[0].height))
        for i, frame in enumerate(frames):
            expected.paste(frame, (i * frame.width, 0))
        same = path.exists() and Image.open(path).convert("RGBA").size == expected.size and Image.open(path).convert("RGBA").tobytes() == expected.tobytes()
        if not same:
            changes.append(str(path.relative_to(ROOT)))
            if not args.check:
                expected.save(path)
        checked += 1
        meta_path = path.parent / "meta.json"
        if meta_path not in metadata:
            metadata[meta_path] = json.loads(meta_path.read_text(encoding="utf-8-sig"))
        entries = metadata[meta_path]["states"]
        size = {"x": frames[0].width, "y": frames[0].height}
        if metadata[meta_path]["size"] != size:
            if args.check:
                changes.append(f"{meta_path.relative_to(ROOT)}: frame size")
            else:
                metadata[meta_path]["size"] = size
        entry = next((s for s in entries if s["name"] == name), None)
        desired = {"name": name, **({"directions": directions} if directions > 1 else {})}
        if len(delays) > 1:
            desired["delays"] = [delays] * directions
        if entry != desired:
            if args.check:
                changes.append(f"{meta_path.relative_to(ROOT)}: {name}")
            elif entry is None:
                entries.append(desired)
            else:
                entry.clear()
                entry.update(desired)

    for local, (ground, held, worn) in WEAPONS.items():
        transfer("weapons.rsi", local, obj[ground])
        if held:
            for side, states in hands.items():
                transfer("weapons.rsi", f"{local}-inhand-{side}", states[held])
                if local in WIELDED:
                    transfer("weapons.rsi", f"{local}_wielded-inhand-{side}", states[held + "_w"])
        if worn:
            transfer("pred_gear_worn.rsi", worn, back[worn])
            if local in {"rending_sword", "piercing_sword", "dual_war_scythe", "double_war_scythe"}:
                transfer("pred_gear_custom_worn.rsi", local, back[worn])

    shields = read_dmi(source / "obj/items/weapons/melee/shields.dmi")
    for side in ("left", "right"):
        held_shields = read_dmi(source / f"mob/humans/onmob/inhands/weapons/melee/shields_{side}hand.dmi")
        for shield in ("ancient_shield", "ancient_shield_alt", "ancient_shield_temple"):
            for suffix in ("", "_ready"):
                name = shield + suffix
                transfer("weapons.rsi", name, shields[name])
                transfer("weapons.rsi", f"{name}-inhand-{side}", held_shields[name])
            if shield in back:
                transfer("pred_gear_worn.rsi", shield, back[shield])
                if shield == "ancient_shield_temple":
                    transfer("pred_gear_custom_worn.rsi", shield, back[shield])

    guns = read_dmi(source / "obj/items/weapons/guns/guns_by_faction/pred.dmi")
    for entry in json.loads((TEXTURES / "pred_guns.rsi/meta.json").read_text(encoding="utf-8-sig"))["states"]:
        name = entry["name"]
        transfer("pred_guns.rsi", name, guns[name])
    aliases = {"spike_launcher": "spikelauncher", "plasma_rifle": "plasmarifle", "plasma_pistol": "plasmapistol"}
    for alias, name in aliases.items():
        transfer("guns.rsi", alias, guns[name])
        transfer("guns.rsi", alias + "_empty", guns[name + "_e"])
    gun_back = read_dmi(source / "mob/humans/onmob/clothing/back/guns_by_type/pred_guns.dmi")
    for name, state in gun_back.items():
        transfer("pred_guns_back.rsi", name + "-equipped-BACKPACK", state)
    for side in ("left", "right"):
        held_guns = read_dmi(source / f"mob/humans/onmob/inhands/weapons/guns/pred_guns_{side}hand.dmi")
        for entry in json.loads((TEXTURES / "pred_guns_inhands.rsi/meta.json").read_text(encoding="utf-8-sig"))["states"]:
            name = entry["name"]
            if name.endswith(f"-inhand-{side}"):
                transfer("pred_guns_inhands.rsi", name, held_guns[name.removesuffix(f"-inhand-{side}")])
        for alias, name in aliases.items():
            transfer("gun_inhands.rsi", f"{alias}-inhand-{side}", held_guns[name])
            transfer("gun_inhands.rsi", f"{alias}_wielded-inhand-{side}", held_guns[name + "_w"])
        transfer("wrist_blades.rsi", f"inhand-{side}", hands[side]["wristblade"])
        transfer("bracer_shield.rsi", f"inhand-{side}", hands[side]["bracer_shield"])
        transfer("smart_disc.rsi", f"inhand-{side}", hands[side]["pred_disc"])
        transfer("scimitar.rsi", f"inhand-{side}", hands[side]["scim"])
        transfer("scimitar.rsi", f"alt-inhand-{side}", hands[side]["scim_alt"])
        for state in ("bow", "bow_w", "bow_loaded", "bow_loaded_w", "bow_expl", "bow_expl_w", "bow_emp", "bow_emp_w"):
            transfer("bow.rsi", f"{state}-inhand-{side}", hands[side][state])
    bow = read_dmi(source / "obj/items/hunter/bow.dmi")
    transfer("bow.rsi", "unwielded", bow["bow_e"])
    transfer("bow.rsi", "wielded", bow["bow"])
    for name in ("bow_loaded", "bow_expl", "bow_emp", "bow_trap", "arrow_trap", "arrow_trap_active"):
        transfer("bow.rsi", name, bow[name])
    transfer("wrist_blades.rsi", "icon", obj["wrist"])
    transfer("smart_disc.rsi", "icon", obj["disc"])
    transfer("smart_disc.rsi", "active", obj["disc_active"])
    transfer("scimitar.rsi", "icon", obj["scim"])
    transfer("scimitar.rsi", "alt", obj["scim_alt"])
    suit = read_dmi(source / "mob/humans/onmob/hunter/suit_storage.dmi")
    transfer("plasma_caster.rsi", "icon", obj["plasma_ebony"])
    transfer("plasma_caster.rsi", "equipped-SUITSTORAGE", suit["plasma_wear_ebony"])
    for side in ("left", "right"):
        transfer("plasma_caster.rsi", f"inhand-{side}", hands[side]["plasma_wear_ebony"])
    for material in ("retro", "ebony", "bronze", "silver", "crimson", "bone"):
        folder = f"plasma_caster_{material}.rsi"
        transfer(folder, "icon", obj[f"plasma_{material}"])
        for side in ("left", "right"):
            transfer(folder, f"inhand-{side}", hands[side][f"plasma_wear_{material}"])
        if f"plasma_wear_{material}" in suit:
            transfer(folder, "equipped-SUITSTORAGE", suit[f"plasma_wear_{material}"])
    for alias, name in {"icon": "bracer_shield_off", "active": "bracer_shield", "ready": "bracer_shield_ready"}.items():
        transfer("bracer_shield.rsi", alias, obj[name])
    if not args.check:
        for path, meta in metadata.items():
            attribution = f" Weapon states verified against CMSS13 {REVISION} by import_weapon_sprites.py."
            if attribution not in meta["copyright"]:
                meta["copyright"] += attribution
            path.write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8")
    print(f"Verified {checked} source states; {'mismatches' if args.check else 'replaced'}: {len(changes)}")
    print("\n".join(changes))
    if args.check and changes:
        raise SystemExit(1)


if __name__ == "__main__":
    main()

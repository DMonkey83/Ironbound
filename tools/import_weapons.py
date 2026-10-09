#!/usr/bin/env python3
"""Turn the owner's pasted weapon tables into weapon and catalogue files.

    python3 tools/import_weapons.py [reference/pf1e] [src/Ironbound.Game/content]

Reads `weapons.psv` (the main table: category|hands|name|cost|dmgS|dmgM|crit|range|weight|type|
special), `firearms.psv`, `special-weapons.psv`, `siege-engines.psv`, `ammunition.psv` and
`weapon-mods.psv`, and writes:

- `content/weapons/<id>.json`, one per weapon, gun and siege engine;
- `content/catalogue/<id>.json`, one per table of things that are not weapons at all —
  ammunition, firearm gear, siege ammunition, weapon modifications.

Re-runnable and idempotent: the same tables always write the same files, byte for byte, and a
file is only rewritten when its text would change. The hand-made weapons (`HAND_MADE`) are never
touched. The tables come from the Archives of Nethys and stay in the gitignored `reference/`
folder; what this writes is our own: the numbers are facts, and every description is written
here, in our words.
"""

import json
import re
import sys
from pathlib import Path

# Written by hand and left alone: natural attacks sized by their stat blocks, the ogre's axe, and
# the unarmed strike, which no table row covers.
HAND_MADE = {"bite", "rat-bite", "ogre-axe", "unarmed-strike"}

# ---- groups -------------------------------------------------------------------------------------
#
# The fighter weapon groups each weapon belongs to, from the books' group lists (Core Rulebook for
# its own weapons, the later books and the Weapon Master's Handbook for theirs). Keyed by the
# table's own name. A weapon missing from here is reported when the script runs.

GROUPS = {
    # simple, light
    "Battle aspergillum": ["hammers"],
    "Brass knife": ["light-blades"],
    "Brass knuckles": ["close", "monk"],
    "Cestus": ["close", "monk"],
    "Dagger": ["light-blades", "thrown", "tribal"],
    "Gauntlet": ["close"],
    "Hanbo": ["monk"],
    "Hook hand": ["close"],
    "Kunai": ["light-blades", "thrown"],
    "Light mace": ["hammers"],
    "Punching dagger": ["close"],
    "Sickle": ["light-blades"],
    "Spider-leg sickle": ["light-blades"],
    "Spiked gauntlet": ["close"],
    "Spring blade": ["light-blades"],
    "Traveling kettle": ["hammers", "monk"],
    "Wooden stake": ["close", "thrown"],
    # simple, one-handed
    "Baston fighting stick": ["hammers", "monk"],
    "Club": ["hammers", "thrown", "tribal"],
    "Heavy mace": ["hammers"],
    "Mere club": ["hammers"],
    "Morningstar": ["flails"],
    "Shortspear": ["spears", "thrown", "tribal"],
    # simple, two-handed
    "Bayonet": ["spears"],
    "Boar spear": ["spears"],
    "Boarding pike": ["polearms", "spears"],
    "Kumade": ["polearms"],
    "Kumade, collapsible": ["polearms"],
    "Lantern staff": ["polearms"],
    "Longspear": ["spears"],
    "Quarterstaff": ["double", "monk"],
    "Spear": ["spears", "thrown", "tribal"],
    "Weighted spear": ["double", "spears"],
    # simple, ranged
    "Blowgun": ["thrown"],
    "Dart": ["thrown"],
    "Heavy crossbow": ["crossbows"],
    "Javelin": ["spears", "thrown"],
    "Light crossbow": ["crossbows"],
    "Sling": ["thrown"],
    "Stingchuck": ["thrown"],
    "Stonebow": ["crossbows"],
    "Underwater heavy crossbow": ["crossbows"],
    "Underwater light crossbow": ["crossbows"],
    # martial, light
    "Blade boot": ["light-blades"],
    "Boarding axe": ["axes"],
    "Butterfly sword": ["light-blades", "monk"],
    "Cat-o'-nine-tails": ["flails"],
    "Dogslicer": ["light-blades"],
    "Dueling dagger": ["light-blades"],
    "Gladius": ["light-blades"],
    "Handaxe": ["axes", "tribal"],
    "Iron brush": ["light-blades"],
    "Jutte": ["light-blades", "monk"],
    "Katar, tri-bladed": ["close"],
    "Kerambit": ["light-blades"],
    "Kukri": ["light-blades"],
    "Light hammer": ["hammers", "thrown"],
    "Light pick": ["axes"],
    "Light shield": ["close", "tribal"],
    "Lungchuan tamo": ["light-blades", "monk"],
    "Machete": ["light-blades"],
    "Sap": ["close"],
    "Sea-knife": ["light-blades"],
    "Shang gou": ["light-blades", "monk"],
    "Short sword": ["light-blades"],
    "Spiked armor": ["close"],
    "Spiked light shield": ["close"],
    "Starknife": ["light-blades", "thrown"],
    "Switchblade knife": ["light-blades"],
    "Tailblade, ratfolk": ["light-blades"],
    "Throwing axe": ["axes", "thrown", "tribal"],
    "Tonfa": ["close", "monk"],
    "War razor": ["light-blades"],
    "Wushu dart": ["monk", "thrown"],
    # martial, one-handed
    "Ankus": ["polearms"],
    "Battleaxe": ["axes"],
    "Combat scabbard": ["close"],
    "Combat scabbard, sharpened": ["close"],
    "Cutlass": ["heavy-blades"],
    "Double chicken saber": ["heavy-blades", "monk"],
    "Gandasa": ["axes"],
    "Heavy pick": ["axes"],
    "Heavy shield": ["close", "tribal"],
    "Klar": ["close"],
    "Light flail": ["flails"],
    "Longsword": ["heavy-blades"],
    "Manople": ["heavy-blades"],
    "Nine-ring broadsword": ["heavy-blades", "monk"],
    "Rapier": ["light-blades"],
    "Scimitar": ["heavy-blades"],
    "Scizore": ["close"],
    "Sibat": ["spears", "thrown"],
    "Spiked heavy shield": ["close"],
    "Sword cane": ["light-blades"],
    "Terbutje": ["heavy-blades"],
    "Terbutje, steel": ["heavy-blades"],
    "Trident": ["spears", "thrown"],
    "Warhammer": ["hammers"],
    # martial, two-handed
    "Bardiche": ["polearms"],
    "Bec de corbin": ["polearms"],
    "Bill": ["polearms"],
    "Earth breaker": ["hammers"],
    "Falchion": ["heavy-blades"],
    "Glaive": ["polearms"],
    "Glaive-guisarme": ["polearms"],
    "Greataxe": ["axes"],
    "Greatclub": ["hammers", "tribal"],
    "Greatsword": ["heavy-blades"],
    "Guisarme": ["polearms"],
    "Halberd": ["polearms"],
    "Heavy flail": ["flails"],
    "Hooked lance": ["spears"],
    "Horsechopper": ["polearms"],
    "Lance": ["spears"],
    "Lucerne hammer": ["hammers", "polearms"],
    "Mattock": ["axes"],
    "Monk's spade": ["double", "monk", "polearms"],
    "Naginata": ["polearms"],
    "Nodachi": ["heavy-blades"],
    "Ogre hook": ["polearms"],
    "Planson": ["polearms"],
    "Ranseur": ["polearms"],
    "Rhomphaia": ["heavy-blades", "polearms"],
    "Sansetsukon": ["flails", "monk"],
    "Sarissa": ["spears"],
    "Scythe": ["heavy-blades"],
    "Syringe spear": ["spears"],
    "Tepoztopilli": ["polearms"],
    "Tiger fork": ["monk", "spears"],
    "Tri-point double-edged sword": ["polearms"],
    # martial, ranged
    "Amentum": ["spears", "thrown"],
    "Atlatl": ["spears", "thrown"],
    "Chakram": ["thrown"],
    "Composite longbow": ["bows"],
    "Composite shortbow": ["bows"],
    "Gastraphetes": ["crossbows"],
    "Hunga munga": ["thrown"],
    "Hurlbat": ["axes", "thrown"],
    "Jolting dart": ["thrown"],
    "Longbow": ["bows"],
    "Phaleros": ["thrown"],
    "Pilum": ["spears", "thrown"],
    "Reflex bow": ["bows"],
    "Shortbow": ["bows"],
    "Spear-sling": ["spears", "thrown"],
    "Throwing arrow cord": ["thrown"],
    "Tube arrow shooter": ["bows"],
    # exotic, light
    "Aklys": ["thrown"],
    "Axe-gauntlet, dwarven light": ["axes", "close"],
    "Barbazu beard": ["axes"],
    "Battle poi": ["flails"],
    "Bich'hwa": ["light-blades", "monk"],
    "Butterfly knife": ["light-blades"],
    "Dan bong": ["close", "monk"],
    "Deer horn knife": ["light-blades", "monk", "thrown"],
    "Dwarven maulaxe": ["axes", "hammers", "thrown"],
    "Elven leafblade": ["light-blades"],
    "Emei piercer": ["close", "monk"],
    "Fighting fan": ["light-blades", "monk"],
    "Flying talon": ["flails"],
    "Gnome pincher": ["close"],
    "Halfling rope-shot": ["flails"],
    "Helmet, dwarven boulder": ["close"],
    "Kama": ["light-blades", "monk"],
    "Kasatha spinal sword": ["light-blades"],
    "Knuckle axe": ["axes", "close"],
    "Madu (leather/steel)": ["close"],
    "Nunchaku": ["flails", "monk"],
    "Pata": ["light-blades"],
    "Quadrens": ["light-blades"],
    "Razor, drow": ["light-blades"],
    "Rope gauntlet": ["close"],
    "Sai": ["light-blades", "monk"],
    "Sanpkhang": ["light-blades"],
    "Scorpion whip": ["flails"],
    "Siangham": ["light-blades", "monk"],
    "Sica": ["light-blades"],
    "Swordbreaker dagger": ["light-blades"],
    "Tekko-kagi": ["close", "monk"],
    "Thorn bracer": ["close"],
    "Wakizashi": ["light-blades"],
    "War-shield, dwarven": ["close"],
    "Waveblade": ["light-blades", "monk"],
    # exotic, one-handed
    "Aldori dueling sword": ["heavy-blades"],
    "Axe-gauntlet, dwarven heavy": ["axes", "close"],
    "Bastard sword": ["heavy-blades"],
    "Broken-back seax": ["heavy-blades"],
    "Doru": ["spears"],
    "Dwarven double waraxe": ["axes"],
    "Dwarven waraxe": ["axes"],
    "Elven thornblade": ["light-blades"],
    "Estoc": ["heavy-blades"],
    "Falcata": ["heavy-blades"],
    "Flindbar": ["flails"],
    "Gnome flick-mace": ["flails"],
    "Great terbutje": ["heavy-blades"],
    "Hooked axe": ["axes"],
    "Katana": ["heavy-blades"],
    "Khopesh": ["heavy-blades"],
    "Knobkerrie": ["hammers", "thrown"],
    "Nine-section whip": ["flails", "monk"],
    "Ram hammer, dwarven": ["hammers"],
    "Rhoka sword": ["heavy-blades"],
    "Sawtooth sabre": ["heavy-blades", "light-blades"],
    "Shotel": ["heavy-blades"],
    "Sickle-sword": ["heavy-blades"],
    "Spiral rapier": ["light-blades"],
    "Split-blade sword": ["heavy-blades"],
    "Taiaha": ["double"],
    "Temple sword": ["heavy-blades", "monk"],
    "Tongi": ["axes"],
    "Urumi": ["flails"],
    "Wahaika": ["hammers"],
    "Whip": ["flails"],
    # exotic, two-handed
    "Barbed spear": ["spears"],
    "Battle ladder, gnome": ["double"],
    "Bladed scarf": ["flails"],
    "Bo staff": ["double", "monk"],
    "Boarding gaff": ["double", "polearms"],
    "Butchering axe": ["axes"],
    "Chain spear": ["double", "flails", "spears"],
    "Chain-hammer": ["double", "flails"],
    "Crook": ["polearms"],
    "Dire flail": ["double", "flails"],
    "Dorn-dergar, dwarven": ["flails"],
    "Double spear": ["double", "spears"],
    "Double walking stick katana": ["heavy-blades"],
    "Double-chained kama": ["double", "flails", "monk"],
    "Dwarven urgrosh": ["axes", "double", "spears"],
    "Elven branched spear": ["spears"],
    "Elven curve blade": ["heavy-blades"],
    "Fauchard": ["polearms"],
    "Flailpole": ["flails", "polearms"],
    "Flambard": ["heavy-blades"],
    "Flask pike": ["spears"],
    "Flying blade": ["polearms"],
    "Garrote": ["close"],
    "Giant-sticker, dwarven": ["spears"],
    "Gnome hooked hammer": ["double", "hammers"],
    "Harpoon": ["spears", "thrown"],
    "Injection spear": ["spears"],
    "Kusarigama": ["double", "flails", "monk"],
    "Kyoketsu shoge": ["flails", "monk"],
    "Longaxe, dwarven": ["axes", "polearms"],
    "Longhammer, dwarven": ["hammers", "polearms"],
    "Mancatcher": ["polearms"],
    "Meteor hammer": ["flails"],
    "Orc double axe": ["axes", "double"],
    "Orc skull ram": ["hammers"],
    "Piston maul, gnome": ["hammers"],
    "Ripsaw glaive, gnome": ["polearms"],
    "Seven-branched sword": ["heavy-blades"],
    "Sphinx hammer, dwarven": ["hammers", "thrown"],
    "Spiked chain": ["flails"],
    "Switchscythe": ["heavy-blades"],
    "Tetsubo": ["hammers"],
    "Totem spear": ["spears", "thrown"],
    "Two-bladed sword": ["double", "heavy-blades"],
    # exotic, ranged
    "Bolas": ["thrown"],
    "Boomerang": ["thrown"],
    "Crystal chakram": ["thrown"],
    "Dire bolas": ["thrown"],
    "Double crossbow": ["crossbows"],
    "Grappling hook": ["thrown"],
    "Halfling double sling": ["thrown"],
    "Halfling sling staff": ["thrown"],
    "Halfling stitched sling": ["thrown"],
    "Hand crossbow": ["crossbows"],
    "Heavy crank crossbow": ["crossbows"],
    "Heavy wrist launcher": ["crossbows"],
    "Hornbow, orc": ["bows"],
    "Horse bow": ["bows"],
    "Kestros": ["thrown"],
    "Light crank crossbow": ["crossbows"],
    "Net": ["thrown"],
    "Pelletbow, dwarven heavy": ["crossbows"],
    "Pelletbow, dwarven light": ["crossbows"],
    "Repeating hand crossbow": ["crossbows"],
    "Repeating heavy crossbow": ["crossbows"],
    "Repeating light crossbow": ["crossbows"],
    "Rope dart": ["flails", "monk", "thrown"],
    "Shoanti bolas": ["thrown"],
    "Shrillshaft javelin": ["spears", "thrown"],
    "Shuriken (5)": ["monk", "thrown"],
    "Sling glove": ["thrown"],
    "Stormshaft javelin": ["spears", "thrown"],
    "Thorn bow": ["bows"],
    "Throwing shield": ["close", "thrown"],
}

# The tail attachments and claw blades belong to no group the books list.
NO_GROUP = {"Claw blades", "Kobold tail, long lash", "Kobold tail, pounder", "Kobold tail, razored",
            "Kobold tail, spiked", "Kobold tail, sweeper"}

# Ranged rows that are thrown rather than shot: Strength to damage, five increments at most.
THROWN = {
    "Dart", "Javelin", "Stingchuck", "Amentum", "Atlatl", "Chakram", "Hunga munga", "Hurlbat",
    "Jolting dart", "Phaleros", "Pilum", "Spear-sling", "Throwing arrow cord", "Bolas",
    "Boomerang", "Crystal chakram", "Dire bolas", "Grappling hook", "Net", "Rope dart",
    "Shoanti bolas", "Shrillshaft javelin", "Shuriken (5)", "Stormshaft javelin", "Throwing shield",
}

# Projectile weapons that still put the wielder's whole Strength into the shot: the slings. Their
# files say so with an explicit "scale", the one override a ranged weapon needs.
SLINGS = {"Sling", "Halfling double sling", "Halfling sling staff", "Halfling stitched sling",
          "Sling glove", "Kestros"}

# Weapons Weapon Finesse can aim besides the light ones, which it always can.
FINESSE = {"Rapier", "Whip", "Spiked chain", "Elven curve blade", "Aldori dueling sword"}

# Words in a name kept as written rather than lower-cased: peoples, places, makers.
PROPER = {"Aldori", "Shoanti", "Kasatha", "Nagant", "Mosin-Nagant", "Madsen", "Maxim",
          "Lawrence", "Hotchkiss", "Aasen", "Thark"}

# What the old hand-made files said about the weapons the party carries, kept as their lines.
KEPT = {
    "Dagger": "The weapon of last resort, and of people who were not expecting a fight.",
    "Greatsword": "Two hands, two dice: a steadier average than the greataxe and a wider threat range.",
    "Light crossbow": "Further and flatter than a bow, and it threatens on a 19. Reloading is not in the game yet.",
    "Shortbow": "Sixty feet before the arrow starts to wander, and ten increments before it will not carry.",
    "Shortspear": "One-handed, so an orc can carry it and still have a fist free for the prisoner.",
    "Short sword": "A light blade for somebody who would rather be quick than strong.",
}

# ---- descriptions -------------------------------------------------------------------------------
#
# One line each, our own words. Every "see text" row has one, saying what the book's paragraph is
# about and that the game does not do it yet. Nothing here is copied from any book.

NOTES = {
    "Battle aspergillum": "A mace with a reservoir of holy water that splashes whatever it hits; the holy water is not in the game yet.",
    "Brass knuckles": "A monk can strike with these at her unarmed damage, and the hand can still hold things; neither is in the game yet.",
    "Cestus": "A monk's weapon that strikes at her unarmed damage and makes fine work with that hand clumsy; neither is in the game yet.",
    "Traveling kettle": "A cooking pot on a chain, good for more than cooking; the rest of its entry is not in the game yet.",
    "Baston fighting stick": "Made to be fought with in pairs; fighting with two at once is not in the game yet.",
    "Boar spear": "A crossbar keeps a skewered foe from running up the haft; bracing against a charge is not in the game yet.",
    "Lantern staff": "A staff with a lantern on one end that can be flashed at a foe; only the staff is in the game.",
    "Stingchuck": "A pot on a cord that is thrown and pulled back again; what the pot holds is not in the game yet.",
    "Blade boot": "A blade hidden in the heel, kicked into a fight without being drawn; the hidden draw is not in the game yet.",
    "Dueling dagger": "Kept in the off hand to help with feints and with fighting defensively; neither help is in the game yet.",
    "Combat scabbard": "A reinforced scabbard swung like a club, with tricks for the blade inside it; only the club is in the game.",
    "Combat scabbard, sharpened": "A scabbard with an edge ground on it; its tricks with the blade inside are not in the game yet.",
    "Klar": "A shield with a blade along its rim, worn and swung at once; only the blade is in the game.",
    "Bardiche": "A heavy cleaving polearm that is better at breaking things; that bonus is not in the game yet.",
    "Bec de corbin": "A hammer-and-beak polearm made for armoured foes; its edge against them is not in the game yet.",
    "Bill": "A hooked polearm for pulling riders down; that use is not in the game yet.",
    "Glaive-guisarme": "A glaive with a hook added for pulling riders down; that use is not in the game yet.",
    "Lucerne hammer": "A spiked hammer on a long haft made for armoured foes; its edge against them is not in the game yet.",
    "Sarissa": "A very long pike that reaches further than other polearms and is clumsy up close; the extra length is not in the game yet.",
    "Syringe spear": "A spear that can inject a dose of poison or potion into what it strikes; the injection is not in the game yet.",
    "Jolting dart": "A dart that delivers a shock as it lands; the shock is not in the game yet.",
    "Pilum": "A heavy javelin that lodges in a shield and drags it down; the lodging is not in the game yet.",
    "Reflex bow": "A bow made to be drawn and loosed quickly; its quicker draw is not in the game yet.",
    "Spear-sling": "A sling that hurls a short spear further than an arm could; its range is in its entry, so it is not usable yet.",
    "Dan bong": "A short stick held across the fist for pressure points and locks; those tricks are not in the game yet.",
    "Elven leafblade": "A leaf-shaped elven blade with a trick of its own; the trick is not in the game yet.",
    "Emei piercer": "A spike worn on a finger ring; how it is concealed and spun is not in the game yet.",
    "Flying talon": "A hooked blade on a long chain that can be cast to reach, trip or disarm; it fights here as an ordinary reach weapon.",
    "Gnome pincher": "A gnomish grabbing tool that clamps onto a weapon; the clamp is not in the game yet.",
    "Helmet, dwarven boulder": "A heavy helm built for butting; charging with it is not in the game yet.",
    "Razor, drow": "A slim drow blade favoured for its cruel cuts; the rest of its entry is not in the game yet.",
    "Sanpkhang": "A short-hafted blade a monk can fight with; the rest of its entry is not in the game yet.",
    "Swordbreaker dagger": "A notched dagger that catches a blade to disarm or break it; neither is in the game yet.",
    "Tekko-kagi": "Claws worn on the back of the hand that help with climbing and disarming; neither is in the game yet.",
    "War-shield, dwarven": "A shield with a spike, worn and swung at once; only the spike is in the game.",
    "Waveblade": "A wavy-edged blade that opens wounds; the rest of its entry is not in the game yet.",
    "Broken-back seax": "A heavy-backed blade with a rule of its own for hard hits; the rule is not in the game yet.",
    "Dwarven double waraxe": "A dwarven axe with a blade on each side of the head; the rest of its entry is not in the game yet.",
    "Elven thornblade": "A curved elven blade with a trick of its own; the trick is not in the game yet.",
    "Knobkerrie": "A throwing club with a heavy knob that can stagger; the stagger is not in the game yet.",
    "Sawtooth sabre": "Can be fought with as a light or a one-handed weapon in two-weapon fighting; that is not in the game yet.",
    "Sickle-sword": "A hooked sword good at pulling shields aside; that use is not in the game yet.",
    "Spiral rapier": "A twisted rapier with a trick for parrying; the trick is not in the game yet.",
    "Split-blade sword": "A sword that splits into two blades; the split is not in the game yet.",
    "Barbed spear": "A barbed head that stays in the wound; the barbs are not in the game yet.",
    "Battle ladder, gnome": "A ladder made into a double weapon, also usable as a ladder; it fights here with one end.",
    "Butchering axe": "A massive cleaver too heavy for most people to swing well; its weight penalty is not in the game yet.",
    "Chain-hammer": "Two hammers on a chain, one of which can be hurled; it fights here with one end.",
    "Dorn-dergar, dwarven": "A dwarven ball and chain that can be fought with short or at reach; it fights here at reach.",
    "Flask pike": "A pike that carries a flask to smash on a foe; the flask is not in the game yet.",
    "Garrote": "A cord for strangling, used once something is grappled; grappling is not in the game yet.",
    "Harpoon": "A thrown spear on a line, for dragging what it hits; the line is not in the game yet.",
    "Mancatcher": "A pole with a sprung collar for catching people, not wounding them; it cannot be fought with yet.",
    "Piston maul, gnome": "A gnomish maul driven by a piston; the piston is not in the game yet.",
    "Ripsaw glaive, gnome": "A glaive with a running saw blade; the saw is not in the game yet.",
    "Boomerang": "A thrown blade that comes back if it misses; the return is not in the game yet.",
    "Double crossbow": "Two crossbows on one stock that loose together; the second bolt is not in the game yet.",
    "Halfling double sling": "A sling with two pouches that loose together; the second stone is not in the game yet.",
    "Net": "A weighted net that entangles rather than wounds; it cannot be fought with yet.",
    "Shrillshaft javelin": "A javelin that shrieks in flight to frighten; the shriek is not in the game yet.",
    "Stormshaft javelin": "A javelin that carries an electric shock; the shock is not in the game yet.",
    "Crystal chakram": "A glassy throwing ring; the table gives it no range, so it is not usable yet.",
    "Halfling stitched sling": "A sling sewn shut around its stone and swung like a flail; the table gives it no range, so it is not usable yet.",
    "Claw blades": "Blades a catfolk wears over its claws; they need a catfolk wielder and the rest of their entry, so they are not usable yet.",
}

# Weapons that pass themselves off as something else or need a particular people to use them.
WIELDERS = {
    "Kobold tail, long lash": "A weapon strapped to a kobold's tail; it needs a kobold to wield it, which the game does not check.",
    "Kobold tail, pounder": "A weapon strapped to a kobold's tail; it needs a kobold to wield it, which the game does not check.",
    "Kobold tail, razored": "A weapon strapped to a kobold's tail; it needs a kobold to wield it, which the game does not check.",
    "Kobold tail, spiked": "A weapon strapped to a kobold's tail; it needs a kobold to wield it, which the game does not check.",
    "Kobold tail, sweeper": "A weapon strapped to a kobold's tail; it needs a kobold to wield it, which the game does not check.",
}

SPECIAL_WORDS = {
    "trip", "reach", "disarm", "sunder", "brace", "double", "monk", "nonlethal", "performance",
    "blocking", "fragile", "grapple", "distracting", "deadly", "improvised", "see-text",
    "scatter", "automatic",
}

TYPE_LETTERS = {"B": "Bludgeoning", "P": "Piercing", "S": "Slashing", "fire": "Fire", "acid": "Acid"}


# ---- parsing helpers ----------------------------------------------------------------------------

def strip_junk(name):
    """The paste carries a stray "3.5 Material" in front of a few names."""
    return re.sub(r"^3\.5 Material", "", name).strip()


def base_name(name):
    """The name without a pack count or a material note: "Shuriken (5)" is a shuriken."""
    return re.sub(r"\s*\([^)]*\)\s*$", "", name).strip()


def display_name(name, keep_parenthesis=False):
    """"Battle ladder, gnome" reads as "gnome battle ladder"; lower case but for proper nouns."""
    if not keep_parenthesis:
        name = base_name(name)
    parts = [part.strip() for part in name.split(",")]
    joined = " ".join(reversed(parts)) if len(parts) > 1 else parts[0]

    def word(w):
        if w in PROPER or any(c.isdigit() for c in w):
            return w
        return w.lower()

    return " ".join(word(w) for w in joined.split())


def kebab(text):
    text = text.lower().replace("'", "").replace("’", "")
    return re.sub(r"[^a-z0-9]+", "-", text).strip("-")


def weapon_id(name):
    return kebab(display_name(name))


def cost_of(text):
    """"15 gp" is 15, "2 sp" 0.2, "5 cp" 0.05, "+50 gp" 50, "3 gp/9 gp" the first, "—" nothing."""
    text = text.strip()
    match = re.search(r"([\d,]+(?:\.\d+)?)\s*(gp|sp|cp)", text)
    if not match:
        return 0
    amount = float(match.group(1).replace(",", ""))
    amount *= {"gp": 1, "sp": 0.1, "cp": 0.01}[match.group(2)]
    return round(amount, 2)


def weight_of(text):
    """"4 lbs." is 4, "1/2 lb." 0.5, "5 lbs./6 lbs." the first, "—" and "special" nothing."""
    fraction = re.match(r"\s*\+?(\d+)/(\d+)\s*lb", text)
    if fraction:
        return int(fraction.group(1)) / int(fraction.group(2))
    whole = re.match(r"\s*\+?(\d+(?:\.\d+)?)\s*lb", text)
    return float(whole.group(1)) if whole else 0


def number(value):
    """Whole numbers as ints, so the files read "4" and not "4.0"."""
    return int(value) if float(value).is_integer() else value


def dice_of(text):
    """"1d6" or "1"; None for "—" and for "see description"."""
    text = text.strip().replace(" fire", "")
    return text if re.fullmatch(r"\d+(d\d+)?", text) else None


def crit_of(text):
    """"19-20/x2" is (19, 2), "x3" (20, 3); None for "—"."""
    text = text.strip()
    match = re.fullmatch(r"(?:(\d+)-20/)?[x×](\d+)", text)
    if not match:
        return None
    return int(match.group(1) or 20), int(match.group(2))


def range_of(text):
    """"10 ft." is 10, "1 mile" 5280, "200 ft. (50 ft. min.)" 200; None for "—", "special", "see text"."""
    text = text.strip()
    if "mile" in text:
        return 5280
    match = re.match(r"(\d+)\s*ft", text)
    return int(match.group(1)) if match else None


def types_of(text):
    """"P or S" is either, "B and P" or "P & S" both, "B (or S)" either; "fire" is fire."""
    text = text.strip()
    if text in ("—", ""):
        return [], None
    rule = None
    if " or " in text or "(or" in text:
        rule = "either"
    if " and " in text or "&" in text or "," in text:
        rule = "both"
    letters = re.findall(r"\b(B|P|S|fire|acid)\b", text)
    types = []
    for letter in letters:
        if TYPE_LETTERS[letter] not in types:
            types.append(TYPE_LETTERS[letter])
    return types, (rule if len(types) > 1 else None)


def specials_of(text):
    text = text.strip()
    if text in ("—", ""):
        return []
    words = []
    for part in re.split(r",\s*", text):
        part = part.strip().lower()
        if part == "disarm or trip":
            words += ["disarm", "trip"]
        elif part in ("see text", "see description"):
            words.append("see-text")
        elif part == "double (see text)":
            words += ["double", "see-text"]
        elif part == "catfolk only":
            continue
        elif part in SPECIAL_WORDS:
            words.append(part)
        else:
            raise ValueError(f"unknown special '{part}'")
    order = sorted(SPECIAL_WORDS)
    return sorted(set(words), key=order.index)


def read_blocks(path):
    """'## <table>' blocks of header-plus-rows; the main table has none and is one block."""
    blocks = []
    current = None
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.rstrip("\n")
        if not line.strip() or (line.startswith("#") and not line.startswith("## ")):
            continue
        if line.startswith("## "):
            current = {"table": line[3:].strip(), "header": None, "rows": []}
            blocks.append(current)
            continue
        cells = [cell.strip() for cell in line.split("|")]
        if current["header"] is None:
            current["header"] = cells
        else:
            current["rows"].append(dict(zip(current["header"], cells + [""] * (len(current["header"]) - len(cells)))))
    return blocks


# ---- building one weapon file -------------------------------------------------------------------

def weapon(name, category, hands, cost, small, medium, crit, rng, weight, kind, special, note=None, groups=None):
    """The JSON for one weapon, in a fixed key order so the files never reshuffle."""
    out = {"kind": "weapon", "id": weapon_id(name), "name": display_name(name),
           "category": category, "hands": hands}

    usable = True
    heads_small = small.split("/") if small else []
    heads_medium = medium.split("/") if medium else []
    # "x2/x3" is a crit per end of a double weapon; "19-20/x2" is one crit.
    if re.fullmatch(r"[x×]\d+/[x×]\d+", crit.strip()):
        crits = [crit_of(part) for part in crit.strip().split("/")]
    else:
        crits = [crit_of(crit)]

    first_medium = dice_of(heads_medium[0]) if heads_medium else None
    first_small = dice_of(heads_small[0]) if heads_small else None
    if first_medium is None or crits[0] is None:
        usable = False

    if first_medium is not None:
        if first_small is not None:
            out["damageSmall"] = first_small
        out["damageMedium"] = first_medium
    if crits[0] is not None:
        out["threatsOn"], out["multiplier"] = crits[0]

    types, rule = types_of(kind)
    if types:
        out["damageTypes"] = types
    if rule:
        out["damageRule"] = rule

    feet = range_of(rng) if rng else None
    if feet:
        out["range"] = feet
    if hands == "ranged":
        if name in THROWN:
            out["thrown"] = True
        if name in SLINGS:
            out["scale"] = "Full"
        if not feet:
            usable = False

    specials = specials_of(special)
    if specials:
        out["specials"] = specials

    if len(heads_medium) > 1:
        second_crit = crits[1] if len(crits) > 1 else crits[0]
        head = {}
        if len(heads_small) > 1 and dice_of(heads_small[1]):
            head["damageSmall"] = dice_of(heads_small[1])
        head["damageMedium"] = dice_of(heads_medium[1])
        if second_crit:
            head["threatsOn"], head["multiplier"] = second_crit
        out["secondHead"] = head

    groups = groups if groups is not None else GROUPS.get(name)
    if groups is None and name not in NO_GROUP:
        raise KeyError(f"no groups for '{name}'")
    out["groups"] = groups or []

    if name in FINESSE:
        out["finesse"] = True

    out["cost"] = number(cost_of(cost))
    out["weight"] = number(weight_of(weight))

    description = note or NOTES.get(name) or WIELDERS.get(name) or KEPT.get(name)
    if "see-text" in specials and not description:
        raise KeyError(f"'{name}' says see text and has no description")
    if description:
        out["description"] = description

    if not usable:
        out["usable"] = False
    return out


def firearm(row, era, grip):
    name = row["Name"]
    small = row.get("Dmg (S)", "")
    medium = row.get("Dmg (M)", "")
    special = row.get("Special", "—")
    misfire_text = row.get("Misfire", "")
    misfire = re.match(r"(?:\d+-)?(\d+)", misfire_text)
    explosion = re.search(r"\((\d+)\s*ft", misfire_text)
    capacity = row.get("Capacity", "")
    rng = row.get("Range", "")

    # The firearm rules themselves — touch AC, misfires, capacity — are described from the data;
    # a line is only wanted for what the data cannot say.
    notes = []
    if "scatter" in special:
        notes.append("Its scattering cone is not in the game: it fires as a single shot.")
    if "see text" in special or "(see text)" in misfire_text:
        notes.append("The rest of its entry is not in the game yet.")
    if range_of(rng) is None:
        notes = ["A gun whose range is a cone of shot; scattering shot is not in the game yet, so it is not usable."]
    note = " ".join(notes) or None

    special = ", ".join(part for part in re.split(r",\s*", special) if part.strip() not in ("—", ""))
    out = weapon(name, "exotic", "ranged", row["Cost"], small, medium, row["Critical"], rng,
                 row["Weight"], row["Type"], special or "—", note, groups=["firearms"])
    out["firearm"] = "early" if era == "early" else "advanced"
    if misfire:
        out["misfire"] = int(misfire.group(1))
    if capacity.isdigit():
        out["capacity"] = int(capacity)
    if explosion:
        out["explosion"] = int(explosion.group(1))
    out["grip"] = grip
    return reorder(out)


def catalogue_only_weapon(row, table, grip=None, group="firearms"):
    """A modern gun, a siege engine, an explosive: written down, never fought with."""
    name = row["Name"]
    medium = dice_of(row.get("Dmg (M)", row.get("Dmg", "")) or "")
    crit = crit_of(row.get("Critical", "") or "")
    out = {"kind": "weapon", "id": weapon_id(name), "name": display_name(name, keep_parenthesis=True),
           "category": "exotic", "hands": "ranged" if group != "siege-engines" or row.get("Range") else "two-handed"}
    out["id"] = kebab(display_name(name, keep_parenthesis=True))
    if medium:
        out["damageMedium"] = medium
    if crit:
        out["threatsOn"], out["multiplier"] = crit
    types, rule = types_of(row.get("Type", "") or "")
    if types:
        out["damageTypes"] = types
    if rule:
        out["damageRule"] = rule
    feet = range_of(row.get("Range", "") or "")
    if feet:
        out["range"] = feet
    out["groups"] = [group]
    out["cost"] = number(cost_of(row.get("Cost", "")))
    out["weight"] = number(weight_of(row.get("Weight", "") or ""))

    details = []
    for key in ("Crew", "Aim", "Load", "Speed", "Capacity", "Misfire"):
        if row.get(key) not in (None, "", "—"):
            details.append(f"{key.lower()} {row[key]}")
    minimum = re.search(r"\((\d+) ft\. min\.\)", row.get("Range", "") or "")
    if minimum:
        details.append(f"at least {minimum.group(1)} ft away")
    what = ("An explosive" if "Explosives" in table
            else "A siege gun" if "Siege" in table and group == "firearms"
            else "A siege engine" if group == "siege-engines"
            else "A gun")
    table_words = table.lower()
    description = f"{what} from the {table_words} table"
    if details:
        description += f" ({', '.join(details)})"
    description += "; kept for the catalogue only, as nothing in the game can use it."
    out["description"] = description
    if grip:
        out["grip"] = grip
    out["usable"] = False
    return reorder(out)


KEY_ORDER = ["kind", "id", "name", "category", "hands", "grip", "damageSmall", "damageMedium",
             "threatsOn", "multiplier", "damageTypes", "damageRule", "range", "thrown", "scale",
             "specials", "secondHead", "firearm", "misfire", "capacity", "explosion", "groups",
             "finesse", "cost", "weight", "description", "usable"]


def reorder(out):
    return {key: out[key] for key in KEY_ORDER if key in out}


# ---- catalogue tables ---------------------------------------------------------------------------

def catalogue(catalogue_id, name, description, rows, table_note=None, notes=None):
    entries = []
    seen = set()
    for row in rows:
        entry_name = strip_junk(row["Name"])
        # Written as the table writes it, lower-cased: "arrow, iron-tipped distance (20)" reads
        # better in a list of arrows than anything turned round.
        written = " ".join(w if w in PROPER or any(c.isdigit() for c in w) else w.lower() for w in entry_name.split())
        entry_id = kebab(written)
        if entry_id in seen:
            continue
        seen.add(entry_id)
        entry = {"id": entry_id, "name": written,
                 "cost": number(cost_of(row.get("Cost", ""))),
                 "weight": number(weight_of(row.get("Weight", "") or ""))}
        stats = {}
        for key, value in row.items():
            if key in ("Name", "Cost", "Weight") or value in ("", "—", None):
                continue
            stats[key] = value
        if stats:
            entry["stats"] = stats
        if notes and entry_name in notes:
            entry["description"] = notes[entry_name]
        entries.append(entry)
    out = {"kind": "catalogue", "id": catalogue_id, "name": name, "description": description}
    if table_note:
        out["rule"] = table_note
    out["entries"] = entries
    return out


MOD_NOTES = {
    "Brutally weighted": "Weighted for heavier blows.",
    "Dual-balanced": "Balanced for fighting with a weapon in each hand.",
    "Jagged hooks": "Hooks along the blade for catching and pulling.",
    "Razor-sharp": "An edge honed far past the ordinary.",
    "Serrated edge": "A saw-toothed edge that tears.",
    "Tactically adapted": "Fitted for a particular fighting trick.",
    "Versatile design": "Built to be held and used more than one way.",
}


# ---- writing ------------------------------------------------------------------------------------

def render(data, comment):
    text = json.dumps(data, indent=2, ensure_ascii=False)
    # Short lists on one line, as the hand-made files write them.
    text = re.sub(r"\[\s*\n\s*([^\[\]{}]*?)\s*\n\s*\]",
                  lambda m: "[" + re.sub(r",\s*\n\s*", ", ", m.group(1)) + "]", text)
    return f"// {comment}\n{text}\n"


def write(path, text, written):
    path.parent.mkdir(parents=True, exist_ok=True)
    written.add(path)
    if path.exists() and path.read_text(encoding="utf-8") == text:
        return False
    path.write_text(text, encoding="utf-8")
    return True


def add(weapons, data):
    if data["id"] in weapons:
        raise KeyError(f"two rows make '{data['id']}'")
    weapons[data["id"]] = data


def main():
    reference = Path(sys.argv[1] if len(sys.argv) > 1 else "reference/pf1e")
    content = Path(sys.argv[2] if len(sys.argv) > 2 else "src/Ironbound.Game/content")
    generated = "Generated by tools/import_weapons.py from the weapon tables; edit the script, not this file."

    weapons = {}

    # The main table: one block with its header in the comment line.
    for line in (reference / "weapons.psv").read_text(encoding="utf-8").splitlines():
        if not line.strip() or line.startswith("#"):
            continue
        category, hands, name, cost, small, medium, crit, rng, weight, kind, special = \
            [cell.strip() for cell in line.split("|")]
        out = weapon(name, category, hands, cost, small, medium, crit, rng, weight, kind, special)
        add(weapons, reorder(out))

    for block in read_blocks(reference / "special-weapons.psv"):
        for row in block["rows"]:
            out = weapon(row["Name"], "exotic", "light", row["Cost"], row["Dmg (S)"], row["Dmg (M)"],
                         row["Critical"], row["Range"], row["Weight"], row["Type"], row["Special"])
            add(weapons, reorder(out))

    firearm_gear = []
    for block in read_blocks(reference / "firearms.psv"):
        table = block["table"]
        grip = "two-handed" if "Two-Handed" in table else "one-handed"
        for row in block["rows"]:
            if table.endswith("(Early)"):
                out = firearm(row, "early", grip)
            elif table.endswith("(Advanced)"):
                out = firearm(row, "advanced", grip)
            elif table.startswith("Firearm Ammunition"):
                firearm_gear.append(row)
                continue
            elif table.startswith("Siege Firearms"):
                out = catalogue_only_weapon(row, table, group="firearms")
            else:
                out = catalogue_only_weapon(row, table, grip if "Explosives" not in table else None)
            add(weapons, out)

    siege_ammunition = []
    for block in read_blocks(reference / "siege-engines.psv"):
        if block["table"].startswith("Siege Engines (Ammunition)"):
            siege_ammunition += block["rows"]
            continue
        for row in block["rows"]:
            out = catalogue_only_weapon(row, block["table"], group="siege-engines")
            add(weapons, out)

    written = set()
    changed = 0
    for weapon_id_, data in sorted(weapons.items()):
        if weapon_id_ in HAND_MADE:
            continue
        changed += write(content / "weapons" / f"{weapon_id_}.json", render(data, generated), written)

    ammunition = []
    for block in read_blocks(reference / "ammunition.psv"):
        if block["table"] == "Ammunition":
            ammunition += block["rows"]
        else:
            siege_ammunition = block["rows"] + siege_ammunition

    mods = [row for block in read_blocks(reference / "weapon-mods.psv") for row in block["rows"]]

    catalogues = [
        catalogue("ammunition", "Ammunition",
                  "Arrows, bolts, stones and darts. Kept for the catalogue only: nothing in the game counts ammunition.",
                  ammunition),
        catalogue("firearm-gear", "Firearm ammunition and gear",
                  "Bullets, powder and the tools of the gun. Kept for the catalogue only: nothing in the game loads a gun.",
                  firearm_gear),
        catalogue("siege-ammunition", "Siege ammunition",
                  "What siege engines throw. Kept for the catalogue only: there are no siege rules.",
                  siege_ammunition),
        catalogue("weapon-modifications", "Weapon modifications",
                  "Alterations a smith can make to a weapon. Kept for the catalogue only: none can be applied yet.",
                  mods,
                  table_note="A modified weapon is one step harder to use: a simple weapon becomes martial, a martial one exotic.",
                  notes=MOD_NOTES),
    ]
    for data in catalogues:
        changed += write(content / "catalogue" / f"{data['id']}.json",
                         render(data, "Generated by tools/import_weapons.py; edit the script, not this file."),
                         written)

    print(f"{len(weapons)} weapons, {len(catalogues)} catalogues; {changed} file(s) changed.")


if __name__ == "__main__":
    main()

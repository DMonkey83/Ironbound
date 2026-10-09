# Where this art came from

Every model here is placeholder until the game is released, but the licences differ sharply and
that difference is the point of this file.

## Characters — `valeria.glb`, `karn.glb`, `merrin.glb`

Built from **modular_rpg_characters** by **System G6 (Qoma)**. The pack's own readme says:

> Copyright/Attribution Notice: You don't need to credit me, but if you like you can mention me
> as System G6 or Qoma.

Credit is optional and given anyway. A line in the credits screen costs nothing and the pack is
worth more than that.

**The source archives are no longer on this machine** (they were in `~/Downloads` and are gone),
so `tools/build_character.py` cannot currently rebuild these three files. The committed `.glb`s
are complete and the game needs nothing else; but a new party member, or a change of armour,
needs the pack again. If a copy exists anywhere, keep it somewhere that is not `~/Downloads`.

These three `.glb` files are **built, not copied**. The pack is a parts bin — a body, several
heads and hairstyles, and armour in light, medium and heavy with matching boots, gloves and
helmets — and `tools/build_character.py` assembles a named set of parts into one character. The
tiers are Pathfinder's own armour categories, so what a creature wears is a content decision:

| | wears | parts | animations |
| --- | --- | --- | --- |
| Valeria | chain shirt | `light_armor` | 47 |
| Karn | breastplate | `medium_armor` | 50 |
| Merrin | — | `light_armor_3` | 47 |

Roughly 2,000 triangles each. To reskin one, edit the part list and run the script again; to
repaint one, edit the `.png` Godot extracted beside the `.glb`.

## Goblins — `goblin.glb`, `goblin-archer.glb`, `hobgoblin.glb`

**Generated**, not downloaded. `tools/generate_goblin.py` builds all three, and each creature's
file names the result. The cleanest provenance in this directory: nothing to licence, and the
models are a diff rather than a binary you cannot inspect.

| Variant | Size | Kit | Triangles |
| --- | --- | --- | --- |
| `goblin` | Small | notched single-edged cleaver, spiked plank shield, crossed straps on bare hide, a layered riveted pauldron, fang necklace, banded wraps, ragged loincloth | ~19,200 |
| `goblin-archer` | Small | shortbow in hand, quiver, sheathed scimitar, studded vest, banded wraps, red headband | ~18,100 |
| `hobgoblin` | Medium | longsword, painted heavy shield, mail shirt with hanging skirt panels, gorget, elbow cops, greaves, red tabard, cheek-guarded helm with a horsehair crest, cloak in strips | ~22,400 |

Shape, proportion, palette and kit follow a concept sheet the project owner supplied
(`Goblin Warrior Game Art Turntable Sheet.png`, an image-generator render used as reference only —
nothing was extracted from it). What was taken from it: the oversized head, the long cupped ears
red on the inside, small eyes sunk under a shelf of brow, the hooked nose and underbite, a wiry
build on a wide bowed stance, olive hide blotched red-brown at the joints, and scavenged rusty
iron. The hobgoblin is built heavier rather than taller — a separate `bulk` from his height — and
his hide is slate grey, so one look says he is not another goblin.

**The body is grown, not assembled.** It is one continuous skin converted from metaballs, which
merge where primitives only touch: the deltoid runs into the arm and the brow into the skull.
The stance — stooped, long-armed, bow-legged — is most of what makes it a goblin at forty pixels.
Flesh, skeleton and kit are all placed from one table of landmarks (`frame()`), so a bone runs
down the middle of the limb it moves and a bracer sits on the forearm it was made for.

**The hide has no texture.** Its shading is ambient occlusion baked into vertex colours with the
kit already on, times a skin tone that pales on the belly and reddens at the ears and nose.
Godot imports those colours and then ignores them — `Main.HonourVertexColours` switches them on,
and without it every goblin is a chalk statue.

Held things are aimed for the idle, not the rest pose, because the game plays the idle from the
first frame and nobody ever sees the rest pose. Every bone is rolled to one convention (+X swings
forward), which is what lets the animation tables be read by a person. All four clips were
re-authored for it, and the death now ends on the ground.

Six clips each: `idle_combat`, `attack_melee`, `run`, `death`, and — because these creatures
fight dirty and the human pack has nothing of the kind — `trip` (down low, sweeping the weapon arm
across the shins; the werewolf goes in with his jaws) and `shove` (coiled behind the shield arm,
then the whole body into them). The death walks the root forward as it tips back, so a body comes
to rest on the square it died in rather than laid out across the one behind: measured, the fallen
body's middle is within 0.12 of its square's centre for all four generated creatures.

The ceiling is a stylised sculpt. The pitted iron, the pores and the leather grain *are* now
produced by a script — `tools/surface.py` bakes procedural materials into a colour, roughness,
metallic and normal atlas per model, embedded in the `.glb` — but they are procedural noise
shaped by rules, not a sculptor's wrinkles, and the difference shows close up.

## Weapons — `weapons/*.glb`

**Generated**, by `tools/generate_weapons.py` from the recipe table in `tools/weapon_recipes.py`:
one small static mesh per weapon in the catalogue that is a thing you can hold, 326 in all. The
sixteen that characters held before the catalogue are textured by the same bake as the
creatures, so each is 0.5–2 MB. Every other one carries no textures, only a few materials named
for what they are (`Steel`, `DarkIron`, `Bronze`, `Wood`, `Wrap`, `Bone`, `Cloth`, `Gold`,
`Stone`, `Horn`, `Crystal`, `Obsidian`, the special metals, and the glowing `Rune1`–`Rune5` and
`Radium`), for the game to put its own shared materials on; those are 5–130 KB each. Each item's
content file names its own model, so the silvered longsword and the plain one are different
models — which blade Valeria is holding is the point of the werewolf fight, and now it shows.

No model, on purpose: the siege engines (41 rows) and the modern and siege firearms and
explosives (the Nagant revolver, the Mosin-Nagant, the Madsen and Maxim guns, the Lawrence
flamethrower, the Aasen mortar, the Hotchkiss gun, the gas cylinder and the grenades), which the
catalogue keeps but nothing in the game uses; the natural attacks (`bite`, `rat-bite`) and
`unarmed-strike`, which are not objects; `spiked-armor`, which is armour; and `ogre-axe`, whose
item already uses `greataxe.glb`.

Every weapon with a striking edge or head carries two empty nodes, `FX_Start` and `FX_End`,
along it — a blade from its base to its tip, an axe's or a mace's head from bottom to top, a
bow from nock to nock, a crossbow's bolt, a gun's barrel — for the game to run flame, frost and
shock along. They were written into the sixteen baked models' files directly, without
rebuilding them, so those meshes and textures are byte for byte as they were.

They hang on the `wep_pos_R` / `wep_pos_L` socket bones the human pack's skeleton already had.
The generated creatures have no sockets and carry their own modelled kit, so they are untouched.

One convention for all of them: grip at the origin, business end up; a shield faces forward
from its strap; anything you *point* (crossbows, firearms) is built lying forward, with a gun's
origin at its firing hand; double weapons are held at their middle; anything worn on the hand
has its knuckle line where a grip would be and strikes forward along the forearm. The catalogue's
new kinds of grip (pistols, gauntlets, double weapons) are not in `Armoury.cs`'s table yet. How each kind sits
in a hand was then **measured**, not found by looking: a scratch Godot scene posed each
character in the idle clip for each grip, found each fist from its finger bones, aimed the item
through it — through both fists for a two-handed weapon, the left fist for a bow — and read the
result back in the socket bone's space. That table is in `Armoury.cs`. Only the weapon a
character fights with is in their hands; a shield goes on the forearm when the left hand is free
and on the back when it is not, and anything else worn is slung across the back. The old single
"+90 degrees about X" was right for none of them, and two main-hand items used to share one fist.

The seven swords were rebuilt from the recipe table after the owner's verdict on the first
icon sheet ("some of those swords look way too similar, some dont look
correct"): a dagger with a narrow, evenly tapering diamond-section blade and a scent-stopper
pommel; a gladius-like short sword with parallel edges, a short angular point, an oval hilt-guard
and a big rounded pommel, in plain iron with a wound grip (the catalogue's `gladius` is the same
blade with bronze fittings and a ridged bone grip); a slender longsword with a two-thirds fuller, a long straight
cross, a hand-and-a-half grip and a wheel pommel; a zweihander greatsword with a leather-wrapped
ricasso, parrying lugs, side rings and a forearm-long grip; a scimitar with a clip point at the
end of a flared, curving single-edged blade. The silvered longsword is that longsword in bright
mirror silver, fittings and all; the +1 greatsword is gilt with a faint emissive rune line down
its fuller (its own `Rune1` material, not baked). Their origins and length axes are unchanged and
each is within 6% of its old length. The cold-iron greataxe kept its shape and is now near-black
with a blue temper sheen; its icon used to read as plain steel.

## Named weapons — `weapons/named-*.glb`

**Generated**, by `tools/named_weapons.py` (one function each, rows in `weapon_recipes.py`): the
Holy Avenger, Flame Tongue, Frost Brand, Life-Drinker, Dwarven Thrower and Oathbow, built after
the owner asked for weapons "way more creative, like epic named weapons" and named the axes of
it: "shades, glows, colours, cracks, widths, handle designs". Each keeps its base weapon's size,
grip origin and axes, so the existing hands hold it, and adds a shape no mundane weapon has —
gilt angel's wings, flame quillons on a flamberge, ice growing out of the steel, a black crescent
pair over a spine of vertebrae, an anvil with knotwork, swans' heads on a recurve. Ornament is
geometry: sculpted metaball pieces, swept feathers and filigree, faceted stones in claws and
bezels, runes and knotwork standing proud of the metal. They are baked at 2048 with a fourth,
emission atlas; checked in Godot 4.7, the imported material has its emission texture and its
energy (`KHR_materials_emissive_strength`). No item files yet: their rules wait for the owner's
Specific Weapons pages.

## Weapon icons — `icons/weapons/*.png`

**Rendered**, by `tools/render_icons.py`, from the same `.glb` files the game loads, one for
every model: 256² with a transparent background, grip bottom left, under one fixed light. Sized
by hands class, not filled to the slot, so a dagger stays smaller than a greatsword. Models with
the shared named materials are drawn with procedural stand-ins for them.

## Werewolf — `werewolf.glb`

**Generated**, by `tools/generate_werewolf.py`, which borrows the goblin generator's machinery —
skeleton, skinning, animation format, export, validation — and supplies only the creature. About
55,000 triangles, most of them the coat: some five thousand hair cards (`tools/cards.py`), one
mesh and one alpha-tested material, so the pelt is a single draw call. That is heavy for a
goblin and fine for the one boss on the board.

He stands on his toes. The goblin's bone names already fit a wolf's hind leg — thigh forward to
the knee, shin *back* to a raised hock, a long foot down to the toes — so the same skeleton and
the same four clip names work and nothing in the game needed to know. His attack is a lunge in
behind the jaws rather than a swing, because the rules give him a bite.

Shape, stance and palette follow a second concept sheet from the project owner
(`Werewolf Captain` / `Werewolf`, an image-generator render used as reference only): the
unarmoured werewolf in its lower half. The armoured Captain above it is an obvious later variant.

## Props — `props/*.glb`

**Generated**, by `tools/generate_props.py` with its helpers `props_parts.py`,
`props_surface.py` and `props_cloth.py`: no source art and nothing to licence — the model is the
script. Containers (`crate`, `crate-b`, `chest`, `strongbox`, `barrel`, `sack`, `sack-open`,
`cart`, `cart-overturned`, `weapon-rack`, `pile`, `niche`) and furniture (`bed`, `table`), each
baked by `surface.py` into one atlas like the creatures.

The weapon rack's contents are copies of three of the game's own models — `weapons/shortspear.glb`,
`weapons/greataxe.glb`, `weapons/longsword.glb` — unchanged in shape, their own baked textures
re-baked into the rack's atlas. If those weapons are rebuilt, rebuild the rack too.

## Item icons — `icons/items/*.png`

**Rendered**, by `tools/render_item_icons.py`, under `render_icons.py`'s own lights, sky and
camera, so they sit with the weapon icons. Armour, rings, coins, valuables and the party's bag
are modelled in that script (no source art, nothing to licence); `container-*.png` are drawn from
`props/*.glb` with the lid open. Sized by family, not filled to the slot.

## Terrain — `Grass_Flat`, `Prop_Tree_Cedar_1`, `Sand_Flat`, `Prop_Cliff_Rock_1`

Taken from a "modular terrain collections" pack that shipped **no licence and no readme**. Until
that is established these are placeholder art and nothing more: fine to look at while
playtesting, not fine for anything released.

Before the game is distributed, one of two things has to happen — the pack's licence is found and
recorded here, or these four are replaced. Nothing in the code cares which: every model is named
by a content file, so swapping the art is a text edit.

| File | Used by |
| --- | --- |
| `Grass_Flat.obj` | `terrain/woodland` — the ground |
| `Prop_Tree_Cedar_1.obj` | `terrain/woodland` — blocked squares |
| `Sand_Flat.obj` | `terrain/rocky` — the ground |
| `Prop_Cliff_Rock_1.obj` | `terrain/rocky` — blocked squares |

Each `.obj` carries a `mtllib` line naming the `.mtl` beside it, which is flat colours only —
there are no textures to go missing.

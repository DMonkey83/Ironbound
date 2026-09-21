# Where this art came from

Every model here is placeholder until the game is released, but the licences differ sharply and
that difference is the point of this file.

## Characters — `valeria.glb`, `karn.glb`, `merrin.glb`

Built from **modular_rpg_characters** by **System G6 (Qoma)**. The pack's own readme says:

> Copyright/Attribution Notice: You don't need to credit me, but if you like you can mention me
> as System G6 or Qoma.

Credit is optional and given anyway. A line in the credits screen costs nothing and the pack is
worth more than that.

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

The ceiling is a stylised sculpt. Pitted iron, pores and stitched leather come from sculpting and
painted textures, and no script produces those.

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

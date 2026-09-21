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

## Goblin — `goblin.glb`

**Generated**, not downloaded. `tools/generate_goblin.py` builds it out of Blender primitives —
spheres, cylinders, cones — with its own 18-bone armature, and the game's `goblin` names the
result. The cleanest provenance in this directory by some way: nothing to licence, and the model
is a diff rather than a binary you cannot inspect.

Three defects were fixed on the way in, all of them provable by rendering the result: the render
engine it asked for no longer exists under that name and the script died outright; the belt was
built twice in the same place; and the sword hung in the air in front of the hip because `side`
had leaked out of an earlier loop and was multiplying its position by zero.

Known and deliberate: about 6,800 triangles, which is three times the human characters for a
simpler-looking figure, and the equipment is not weighted to the skeleton. Neither matters until
the goblins animate — there are no animations in the file at all.

`goblin-archer` and `hobgoblin-sergeant` are still capsules. The archer carries a bow, and this
model is a swordsman; drawing him with a sword and shield would be a worse lie than a capsule.

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

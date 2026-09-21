# Where these models came from

Eight files, taken from a "modular terrain collections" pack downloaded to this machine. The
pack shipped **no licence and no readme**, and until that is established these are placeholder
art and nothing more: fine for looking at while playtesting, not fine for anything released.

Before the game is distributed, one of two things has to happen — the pack's licence is found
and recorded here, or these are replaced. Nothing in the code cares which: every model is named
by a content file under `content/terrain/`, so swapping the art is a text edit.

| File | Used by |
| --- | --- |
| `Grass_Flat.obj` | `terrain/woodland` — the ground |
| `Prop_Tree_Cedar_1.obj` | `terrain/woodland` — blocked squares |
| `Sand_Flat.obj` | `terrain/rocky` — the ground |
| `Prop_Cliff_Rock_1.obj` | `terrain/rocky` — blocked squares |

Each `.obj` carries a `mtllib` line naming the `.mtl` beside it, which is flat colours only —
there are no textures to go missing.

# Art pipeline

Three Blender scripts, all run headless. None of them is part of the build: art is converted
once and the result is committed, so a clone needs no Blender to play the game.

```sh
blender -b --factory-startup <in.blend> --python tools/<script>.py -- <args>
```

`--factory-startup` matters. Without it Blender loads your own startup file and quietly adds
whatever is in it to the export.

## `build_character.py` — assemble one character from a modular pack

```sh
blender -b --factory-startup human_female.blend --python tools/build_character.py -- \
    src/Ironbound.Game/art/valeria.glb \
    human_female_body_default,human_female_head_default,human_female_hair_2,\
human_female_light_armor,human_female_light_boots,human_female_light_gloves
```

Keeps the named parts, drops everything else, rewires the materials, binds the loose parts to
the skeleton, and exports glTF with every animation.

## `fixup_legacy_model.py` — rescue a pre-2.80 model

Drops the author's workspace objects, bakes transforms, fixes the winding on mirrored meshes,
folds a separate alpha mask into the colour texture, recentres on the origin and exports.

## `generate_goblin.py` — grow a goblin

```sh
blender -b --factory-startup --python tools/generate_goblin.py -- <variant> <out.glb>
```

`variant` is `goblin`, `goblin-archer`, `hobgoblin`, `orc` or `ogre` (the last two for the Caves
of Shadow; the dire rat is `generate_rat.py`, a quadruped on the same bone names). No source art and nothing to licence: the
model is the script. The body is grown from metaballs, the kit is placed on it from the same
landmarks, and every surface is baked to textures by `surface.py` before export. Each build
takes about two minutes, nearly all of it the bake. It prints a self-check parsed back out of the exported GLB — mesh count,
skinned count, triangles, joints, animation names, bounding box — and ends in PASS or FAIL on
whether every primitive carries `JOINTS_0`. Trust that, not the absence of a traceback.

Blender API changes that bite this script in particular: the render engine enum is renamed
between versions and assigning an unknown one throws; `Action.fcurves` moved into
layers -> strips -> channelbags in 4.4 and was removed in 5.x; and `bpy.ops.object.convert`
inherits whatever is selected, so deselect before converting a curve or the conversion is
refused and the object reaches the exporter unweighted.

## `villager.py` — people

```sh
blender -b --factory-startup --python tools/generate_goblin.py -- tobin <out.glb>
blender -b --factory-startup --python tools/generate_goblin.py -- pedlar <out.glb>
```

The merchants: Tobin the carter and a pedlar. Run through `generate_goblin.py`, which hands
these two variants to `villager.py`; everything that is not specifically a person (skeleton,
bone names and roll, skinning, export, the GLB self-check) is the goblins'. Build into a scratch
directory and copy the `.glb`: the build saves a `.blend` beside its output.

- **Body.** Metaballs on a table of landmarks (`frame`) in the goblins' names, at the party's
  proportions; `tobin` is stooped, `pedlar` narrower in the shoulder and wider at the hip. Chains
  are spaced at under half their radius, or limbs come out as strings of beads.
- **Face.** Kept small and plain: an egg of a skull, a brow, a nose, a mouth line, and dark eyes
  set back in the sockets. White eyeballs and big features made the first heads caricatures.
- **Clothes.** Fitted pieces (coat, bodice, sleeves, trousers, boots) are the body's own skin
  where the right bones are nearest (`piece`), relaxed so the muscles under them do not show,
  pushed out and thickened (`shell`). What hangs free is lofted round the body's outline:
  `skirt` (the outline of torso and legs, never the hanging arms, or it takes in the hands) and
  `drape` (each ring at least as wide as the one above, so a cloak falls from the shoulders).
  The pedlar's hood is lofted from arches; cut from the head it fitted like a swimming cap.
  The skin the clothes cover for good is deleted before the bake (`strip_hidden`).
- **Surfaces.** `wool` for cloth: `surface.cloth`'s weave is finer than a 1024 atlas can hold
  over a whole coat and bakes to moire. Skin is `skin_recipe`, the goblins' hide without warts.
- **Clip.** One, `<variant>_idle`: breathing, weight shift, a turn of the head. The game finds it
  by name (`Breathe`, `Stage.Idle`).

`validate_glb` takes the clips to expect (`clips=("idle",)` here); the goblins' default is
unchanged.

## `generate_werewolf.py` — the chapter-two boss

```sh
blender -b --factory-startup --python tools/generate_werewolf.py -- <out.glb>
```

Imports `generate_goblin.py` for everything that is not specifically a wolf, so a fix to the
skeleton, the exporter or the validator lands in both. The hide is displaced into clumps before
it is thinned, and the coat is several hundred lofted locks, jittered so they do not repeat,
joined into one mesh per bone. New creatures should start the same way:
a `frame()` of landmarks, a body grown from them, and whatever is sharp or bright added on top.

## `generate_weapons.py` — every weapon in the catalogue

```sh
blender -b --factory-startup --python tools/generate_weapons.py -- src/Ironbound.Game/art/weapons [id ...]
blender -b --factory-startup --python tools/generate_weapons.py -- src/Ironbound.Game/art/weapons --fx-only [id ...]
```

One static `.glb` per weapon id in `content/weapons/` that is a thing you can hold (see
`art/PROVENANCE.md` for the ones that get none, and why). An item's content file names its
model with `"model"`, and `Armoury.cs` puts the weapon a character fights with in their hands,
using a table of grips measured per idle stance. Shapes are lofted from cross-sections
(`gg.loft`) — a blade has a fuller and tapers two ways at once, which no primitive does.

New weapons are rows, not functions. Three files:

- `weapon_recipes.py` — the table, `RECIPES`: every id, its family (the sheet it belongs to:
  swords, sabres, knives, axes, hammers, spears, polearms, flails, bows, crossbows, firearms,
  thrown, worn, shields, double), its builder and its parameters. `like` starts a row from
  another and is merged in all the way down, so a named magic weapon later is a few lines —
  `dict(like="longsword", guard=dict(span=0.4), fittings="Gold", extra=[...], variant="+2")`;
  `extra` adds unique parts on top of the base's.
- `weapon_families.py` — the builders (`straight`, `curved` for swords and knives; `hafted`
  for anything on a haft or nothing at all; `bow`, `crossbow`, `firearm`, `shield`), the head
  generators (`axe`, `hammer`, `pick`, `spear`, `glaive`, `hook`, `prongs`, `flanged`...), the
  named materials, and the variants (`cold-iron`, `silver`, `adamantine`, `mithral`,
  `+1`…`+5`).
- `weapon_parts.py` — the primitives: blades swept along a path, plates ground to an edge,
  lathes, tubes, chains, cords, guards, grips, pommels.

Conventions, beyond grip-at-origin and business-end-up: the flat of a blade faces Y and an
edge or a head's business side faces -X; crossbows and firearms are built standing and laid
forward along +Y (`pointed`); a firearm's origin is its firing hand (a pistol's grip, a long
gun's wrist); double weapons and staves are held at their middle; shields have their strap at
the origin and face -Y; anything worn on the hand (`worn`) has the knuckle line along Z, strikes
along +Y and runs up the forearm along -Y. Every weapon with a striking edge or head carries two
empty nodes, `FX_Start` and `FX_End`, along it, for flame, frost and shock effects; `--fx-only`
writes them into existing files without rebuilding them.

The sixteen items characters held before the catalogue (`BAKED`) are textured by `surface.py`
like every model they stand next to. Everything else exports with a few materials named exactly
as in `weapon_families.MATERIALS` (`Steel`, `DarkIron`, `Bronze`, `Wood`, `Wrap`, `Bone`,
`Cloth`, `Gold`, `Stone`, `Horn`, `Crystal`, `Obsidian` and the special metals) and no textures,
for the game to swap for shared ones: about 30 KB a weapon instead of 3 MB. A glow — an
enhancement's rune line (`Rune1`…`Rune5`), radium — is its own emissive material and never
baked. Budgets: 3,000 triangles for a light or one-handed weapon, 150 KB a file.

## `named_weapons.py` — named weapons

The top of the loot ladder, built to a higher standard than the catalogue: Holy Avenger, Flame
Tongue, Frost Brand, Life-Drinker, Dwarven Thrower, Oathbow (`named-<slug>`). Each is a row in
`weapon_recipes.py` (family `named`, its `base` the weapon it is in the hand) and a function in
`named_weapons.py`; `generate_weapons.py` builds them like any other id. Each has a shape no
mundane weapon has, three to five contrasting materials, glow placed where the eye should go,
and its ornament as geometry: sculpted pieces (wing arms, swans, a skull, vertebrae) are
metaballs converted to a mesh — every metaball named uniquely, because Blender melts together
metaballs whose names share a stem — feathers, flames and filigree are swept curves, stones are
faceted lathes, runes and knotwork stand proud of the surface. Gradients across a blade (temper
colours from spine to edge, a white-gold bevel, crack veins near the edge) come from per-vertex
attributes the materials read: `Edge`, `Along`, `Vein`.

They are baked like the held items, at 2048 and with a fourth atlas: `surface.finish(...,
emission=strength)` bakes what each material emits (its Emission Color at strength 1, so the
brightness is in the colour) and the exported material glows with it at the row's `glow`
strength (glTF `emissiveTexture` and `KHR_materials_emissive_strength`; Godot 4 imports both as
an emission texture and energy). Keep emissive colours dark (well under 1): multiplied by the
strength they still have a hue, where a bright one comes out white.

## `weapon_variety.py` — a variety sample

Eighteen scratch weapons, `sample-<id>-a/b/c` for the longsword, dagger, battleaxe, spear, heavy
mace and shortbow: how far one catalogue weapon can vary between two drops without magic —
shade, colour, wear, width and profile, handle. Not exported to the game. The handle library they
use is the swords' own (`_hilt`/`_grip`/`_pommel` in `weapon_families.py`): grips spiral,
cord, ridged, diamond, crisscross, ray skin under a diamond wrap, wire-wound, ringed, riveted
scales, with a tassel; pommels wheel, scent-stopper, pear, ball, cap, ring, faceted, claw and
stone, skull, beast; guards cross, bar, disc, tsuba, recurved, S, swept, knuckle, shell, basket,
parry, crescent. Blades take twin fullers, a waist, a swell toward the point and nicks.

## `render_icons.py` — inventory icons

```sh
blender -b --factory-startup --python tools/render_icons.py -- src/Ironbound.Game/art/weapons src/Ironbound.Game/art/icons/weapons [id ...]
```

One 256² transparent PNG per `.glb`, drawn from the model the game uses: grip bottom left,
turned 22° about its length, under a fixed three-light setup. Icons keep relative size — the
frame is set by the weapon's hands class (`weapon_families.hands`), so a dagger fills about
60% of the slot's diagonal, a longsword 85% and a greatsword or a polearm all of it; bows and
crossbows about 90%. Some families are turned to show what makes them themselves: a bow to face
the camera with its curve, a firearm onto its lock side, a spiked shield further round, a
gauntlet to show the back of the hand. A model with the shared named materials is drawn with
each name's procedural recipe (edge wear turned down, as the game's tileable materials have
none), so its icon is not flat grey. Judge a batch on a contact sheet of dark slots, and at
64 px as well as full size.

## `surface.py` — the textures

Not run on its own: the three generators call `surface.finish()` on their parts before they
export. It swaps each flat material for a procedural one by name — `Leather` gets grain and
rubbed-pale edges, `Rusty_Iron` gets dents and rust in its hollows, `Shield_Wood` gets a
figure, `Goblin_Hide` gets pores and warts over the painted vertex colours, `Wolf_Fur` gets
streaks — then unwraps every part of a group into one atlas and bakes colour, roughness,
metallic and a tangent-space normal map into it. The parts leave wearing one ordinary
textured material each, which is what the glTF exporter and Godot both understand without
help.

The recipes sample the model's *world position*, never its UVs, so there are no seams and no
stretching however a part was unwrapped; the UVs only receive the bake. `surface.SCALE` tells
the recipes how big a model unit is next to a goblin's, so the weapons — built at human size —
get the same pore spacing. To add a material, give it a recipe in `RECIPES`; one that is not
there stays flat, which is right for some things.

Atlases are 1024 square: a creature is a few hundred pixels tall at most on the board, and a
2048 set would double each `.glb` to 20 MB for nothing anybody could see.

## `cards.py` — hair and fur

Not run on its own. Fur and hair are *cards*: ribbons of a few quads wearing an alpha-tested
texture of strands, scattered over a body by rules (`plan(point, normal)` → density, length,
the way it lies, palette) and skinned from the body vertex nearest each root. The werewolf's
coat and the goblins' crests are made this way. Solid locks were tried first and read as
spikes and leaves at every density; the difference is that a card shows strands and the dark
hide between them.

The material exports as glTF `alphaMode: MASK`, double-sided, and Godot draws it with alpha
scissor. Tufts are sparse on purpose — a full card is a leaf — and solid only in the bottom
few percent, so the roots survive mipmapping. Cards are kept out of `surface.py`'s bake: they
carry their own UVs and texture.

## Hands, mouths, and finding things by ray

`generate_goblin.grips()` decides once where each hand closes and round what (centre, grip
axis, knuckle direction, back of the hand); `fist()` grows the metaball fist round it and the
weapons are put through the same point. The grip is kept square to the forearm: a blade laid
along the hand is what the flat-palmed version looked like.

Teeth are not placed by coordinates. The mouth is cut open with a negative metaball, and rays
cast from inside it (goblins) or straight down through the open jaws (`generate_werewolf.
crossings`) find the gums; each tooth grows from just inside the gum toward the gap. So teeth
are always inside the lips whatever the metaballs did to the face, and where the jaws have
closed there is simply no tooth. The same gum points mark the inside of the mouth for painting.

Armour is cut from the body, not placed over it. `torso_piece()` takes the goblin's own skin
nearest the spine and collarbones (by distance to the bones, so the armhole falls round the
shoulder joint), cuts it clean at the waist and neck, and `fitted_shell()` pushes it out and gives
it thickness; straps are bands laid along that surface (`ribbon()`), studs sit on it, and all of
it takes its skinning from the body under it. The ellipsoid shells this replaced let the
shoulder blades through on one side and floated off the ribs on the other.

`IRONBOUND_FAST=1` skips the texture bake, which is nearly all of a build's time — use it while
iterating on shape, never for what goes in `art/`.

## `preview_model.py` — render what actually came out

```sh
blender -b --factory-startup --python tools/preview_model.py -- out.glb preview.png [action]
```

Imports the `.glb` into an empty scene and renders it from two angles. **Use it.** Every one of
the traps below produced a build log that claimed success.

## `render_keyart.py` — the front door

```sh
KEYART_PICTURE=shot.png blender -b --factory-startup --python tools/render_keyart.py -- desk src/Ironbound.Game/art/menu/title.png 1920 1080
blender -b --factory-startup --python tools/render_keyart.py -- parchment src/Ironbound.Game/art/menu/parchment.png 1024 1024
```

The title screen is laid out after Pathfinder: Wrath of the Righteous: a desk by candlelight seen
from straight above, the menu written on the right-hand page of an open book, the title on a
note, and a framed picture of the game (`KEYART_PICTURE`, an in-game shot). The camera is
orthographic at 100 pixels to the unit, so `Menu.cs` can place words on the paper by the numbers
in `DESK_PAGE` and `DESK_NOTE`; move them together. The adventure cards
(`art/menu/<campaign id>.png`) are crops of in-game shots. `parchment` is the paper every page,
the hotbar and the log are printed on.

## `import_weapons.py` — the weapon catalogue

```sh
python3 tools/import_weapons.py [reference/pf1e] [src/Ironbound.Game/content]
```

Not Blender: plain Python, and the one tool here that writes content rather than art. It reads
the weapon tables the owner pasted into the gitignored `reference/pf1e/` (`weapons.psv`,
`firearms.psv`, `special-weapons.psv`, `siege-engines.psv`, `ammunition.psv`,
`weapon-mods.psv`) and writes a file per weapon to `content/weapons/` and a file per table of
things that are not weapons — ammunition, firearm gear, siege ammunition, weapon modifications —
to `content/catalogue/`. Re-run it whenever the script changes; it only rewrites a file whose text
would change, so a second run reports nothing changed. `bite`, `rat-bite`, `ogre-axe` and
`unarmed-strike` are hand-made and listed in the script as files it never touches.

What the tables do not say is in the script, in our own words: the fighter weapon groups of
every weapon, which ranged weapons are thrown rather than shot, which slings put Strength into
the shot, and a line for every weapon whose Special column says *see text*, saying what that text
is about and that the game does not do it yet. Edit the script, not the generated files.

A save keeps *which* weapon a character holds, and whether it is broken or thrown, but not its
numbers: loading builds every weapon again from its item and these files. So a change made here
reaches games already in progress the next time they are loaded. It also mended a real save,
written by a build that was half-way through this change, where every weapon had come out as a
1d6 club and Sylwen's bow as one too.

## `generate_props.py` — containers and furniture

```sh
blender -b --factory-startup --python tools/generate_props.py -- src/Ironbound.Game/art/props [id ...]
```

One `.glb` per prop in `art/props/`: `crate`, `crate-b`, `chest`, `strongbox`, `barrel`, `sack`,
`sack-open`, `cart`, `cart-overturned`, `weapon-rack`, `pile`, `niche`, and the furniture `bed`
and `table`. Each is baked by `surface.py` into one 1024 atlas (colour, ORM, normal) like the
creatures, with the colour and ORM stored as JPEG (and a normal atlas too, when cloth or rock
noise would make it over a megabyte as PNG): 1.1–2.7 MB a file. A build takes three to five
minutes, nearly all of it the bake. It prints a self-check read back out of the GLB — node
names, triangles per node, the box in Godot's frame, the size — and ends in PASS or FAIL.

Scale is the board's: one unit a five-foot square, a man 1.4 (`Main.CapsuleHeight`), so a
crate comes to his hip. Blender's Z is up (Godot's Y), the front of every prop faces -Y (Godot's
+Z, the side the default camera sees), and the origin is on the floor in the middle of the
footprint — except `sack-open`, whose origin is the sack's, with its coin spilling off it.

**Nodes.** A container's state is shown by hiding and turning nodes of one model:

| Node | What it is |
| --- | --- |
| body, named after the prop | everything that does not move |
| `Lid` | its origin on the hinge line; open by turning it about its local X. Extras `hinge` and `open` say how: `"x"` and `-100` for the chest and strongbox (the front rises), `"x"` and `80` for the niche's stone (it tips out toward the viewer), `"none"` and `"lift"` for the crate and barrel, whose lids have their origin at their own centre and are lifted off |
| `Contents` | what shows inside when it is open; hidden once it is empty |
| `Lock` | the padlock (chest) or the hasp (strongbox); hidden once picked or broken |
| `FX_Glint` | an empty at the top centre, for the lootable sparkle |

What every prop is made of lives in three helpers:

- `props_parts.py` — the geometry. A board (`board_mesh`) is never a box: bevelled, cut a little
  out of true, its edges rubbed in unevenly, chips scooped out of its corners, bowed, its ends not
  square. Nails and rivets are forged heads; iron bands are swept round the faces they bind; rope
  is three strands laid round each other; straw is a packed bed and loose stalks; coin heaps are
  a mound of the metal under loose coins at every angle.
- `props_surface.py` — the materials, added to `surface.RECIPES` by name. Wood (`plank`) grows
  rings round each board's own pith, from a `grain` attribute every board carries, so a plank
  lying across a crate has its grain along it and its end grain on its end; each board has its
  own tone; mould and damp climb the foot; the chest's paint wears through on every edge; the
  cart's wheels carry mud. After the bake, `weather()` paints rust down the wood from every nail,
  rivet and hoop the builder recorded, in numpy, from a baked map of world position.
- `props_cloth.py` — sacks and bundles, sculpted as cloth slumps and gathers: a lathed bag moved
  by named rules (slump, lean, pleats converging on the tie, sag creases, a tuft that flops), with
  the weave laid on before it is moved so the threads follow every fold. Blender's cloth
  simulation with pressure was tried first and either collapsed the bag flat or let it skate off
  across the floor, at every mass and pressure tried; it is kept (`drape`) for loose cloth.

Three things the bake does that the creatures' does not: each node is moved apart while it
bakes, so the inside of a box is not baked black under its lid and the contents are not darkened
by walls; a floor is put under the body, so its foot darkens; and loose straw stalks, too thin to
get a texel of their own, are pointed at a spot on the straw bed they lie on (`borrow_uvs`).
Roughness is held at 0.22 or above in every atlas (`floor_roughness`): a sliver of a bevel too
thin to be given a texel kept the cleared value, roughness 0, and shone as a white line along a
board's edge under a lamp.

The weapon rack holds three of the game's own weapons (`shortspear`, `greataxe`, `longsword`),
imported from `art/weapons/` unchanged and scaled as a hand holds them (1.4 / 4.3); their own
baked textures are read through their own UVs and baked into the rack's atlas, so the rack is
one material and not four sets of textures.

`IRONBOUND_FAST=1` skips the bake, as for the creatures. `IRONBOUND_KEEP=<dir>` keeps the atlases
as files there for looking at.

**The niche is part of a wall.** `Loot.cs` puts it at the middle of a wall square, turned to face
the open floor (its -Y, Godot's +Z). In a face-on wall the relief (`LevelLook.BuildGround`) is 0
on the edge beside the floor and 0.50–0.75 at the middle of the square and beyond: a ramp of up
to 1.5 in 1, then a ridge. So the model is a loft across the square between two sections, `FACE`
(a near-vertical face at the square's edge, its brow at about 0.63, the top running back at ridge
height) and `UNDER` (just below the lowest the relief can lie, `relief_low`), blended by how near
the side each column is, so the sides and the back sink into the relief along a ragged line. The
face is broken into tilted beds with undercut lips, vertical joints and chips; the top and the
gentle flanks stay nearly as smooth as the relief they run into, which is what keeps it from
reading as a boulder. The hole is wedge-shaped, deep under its roof and shallow at its floor,
because its floor is kept above the steepest the relief can climb (`relief_high`): a hole cut
straight back would have the game's ramp showing, lit, through its back half (the cave's lights
cast no shadows). Its rock is `niche_rock`, painted as `level_ground.gdshader` paints the relief:
banded with height, darker the higher it climbs, the tops faster than the faces.

## `props_sheet.py` — the props as the game will show them

```sh
blender -b --factory-startup --python tools/props_sheet.py -- src/Ironbound.Game/art/props out/ chest:locked,closed,open,empty \
    --closeup chest:0.0,-0.25,0.3:0.16 --scale crate=src/Ironbound.Game/art/karn.glb
```

Renders each prop from the game camera's default angle (`CameraRig`: 37.5 degrees up, from the
south-east) on a dark flagstone floor under a torch, a lantern and a cold rim, in each state its
nodes allow, using the `hinge`/`open` extras to open the lid the way the game will. `--scale`
stands Karn beside a prop at the height the game gives him.

## `render_item_icons.py` — icons for everything that is not a weapon

```sh
blender -b --factory-startup --python tools/render_item_icons.py -- src/Ironbound.Game/art/icons/items [id ...]
```

Armour, rings, coins, valuables, the party's bag (`party-bag`, the HUD's inventory button), and
`container-<name>` for each prop above, drawn with its lid open for the loot window's title.
Drawn exactly as `render_icons.py` draws the weapons — its lights, sky, camera, size and margin
are imported from it, not copied. The `consumables` family (potions, oil, alchemy) fills 0.6 of
the slot at its largest and is modelled to read at 48 pixels: each bottle its own shape, clear
glass that gives back a little light at its rim (`glass`), and the liquid a solid inside it lit
a little from within (`liquid`; `core` for a glow at the heart, `sparkle` for motes). AgX bleaches
bright saturated emission toward pastel, so keep glows modest. `container-niche` is drawn without
the parts of the niche the relief hides (`under_the_wall`). Most of these things have no model in the game, so each is
modelled here: armour is hung on a man's trunk (`TRUNK`) as panels wrapped round it, mail is a
ring pattern on the garment's own UVs so it follows the drape, scale mail is several hundred
overlapping leaves, coins are struck with a rim and a device. Every icon in a family is drawn at
one scale (`FAMILIES`), so armour fills the slot, a ring does not, and dice stand beside rings.


## Menus, pages and experience

- **Title** (`Menu.cs`): Continue, New Game, Load Game, Quit, inked on the book's page.
  **New Game** opens a card per campaign; `-- --menu adventures` opens that screen directly.
  `-- --campaign <id>` skips the menu altogether, as do `--autoplay` and the other test switches.
- **Pages** (`Pages.cs`): what the content files say in prose — a room's intro and outro, a
  cache's contents, the bridge — is shown on parchment over the board, and nothing moves until it
  is put away. Unattended runs skip them.
- **Experience** is given for every fight won, every place found for the first time, and every
  door opened, chasm crossed and cache searched. Each award goes in the log and up on a slip at
  the top of the screen; a level earned says so. Areas and features take an `"xp"` in their level
  file; without one, a place is worth 50, a door 50, a crossing 100, a cache 25.

## Levels: walking between the fights

A campaign with a `"level"` (`content/levels/*.json`) is played on one connected map instead of
a chain of separate boards. The party walks it in real time — click to walk, the selected member
leading and the rest falling in round them; click a door, the bridge or a cache to go and use it
— and the game turns turn-based only when somebody steps into a room where something is
waiting. The fight is fought on the same ground, everyone where they stood; when it is won the
bodies stay where they fell and the walking resumes.

The map is ASCII: `#` rock, `.` stone floor, `,` grass, `T` tree, `R` standing rock, `~` chasm,
`+` door, `b` `t` `c` bed, table, crate. Areas are rectangles over the floor with their foes,
story and loot; features name the squares they sit on. The rules own every fact (`CampaignLevel.cs`
in Simulation); the Game draws them (`Exploration.cs`, `LevelLook.cs`):

- The ground and the rock are one heightfield, two points to a square: walkable squares stay
  flat, rock climbs out of the wall squares and darkens as it rises, a lone `R` is a boulder, a
  chasm is a trench. `level_ground.gdshader` paints it — flagstones underground, earth and
  gravel in the open, grass by the vertex colour — so nothing is tiled.
- A level that is a third or more rock is a cave: night outside, a torch on the head wall of
  every room under the rock, and a lantern carried by the party. Open levels keep the daylight.
- The grid and the nameplates show only during a fight, and on a level the grid is drawn at
  half strength.

**Loot (content).** A feature of `"kind": "container"` (an old `"cache"` still loads as one) is a
crate, chest, cart or sack: `"look"` (`crate`, `chest`, `strongbox`, `barrel`, `sack`, `cart`,
`weaponRack`, `pile`; default `crate`), `"loot"` as item ids or `{ "item": "bloodstone", "count":
2 }`, `"coins"` as `{ "pp": 0, "gp": 12, "sp": 30, "cp": 4 }`, `"lockDc"`/`"breakDc"` to lock it
(Disable Device, trained only, or Strength, as a door), `"hiddenDc"` to hide it (each of the party
gets one Perception roll the first time they come within 10 ft), and `"xp"` for opening it the
first time. It may sit on furniture, in a wall niche beside the floor, or on open floor. An area's
`"loot"` becomes a pile in the middle of the room when it is won, or at `"lootAt": { "x", "y" }`;
the fallen become bodies where they fell. Items take `"type"` (`valuable`, `wondrous`; weapons,
armour, shields and natural weapons are worked out), `"cost"` in gold and `"weight"` in pounds
(a weapon item takes both from its weapon; armour writes the armour table's), `"masterwork"`, and
`"stackable"` to override the default (plain gear and valuables stack). Creatures take
`"purse"` (`{ "sp": "2d6", "cp": "3d10" }`, rolled from the seed when the body is made),
`"treasure"` (`none`, `incidental`, `standard`, `double`, `triple`, `npc-gear`; data only) and
`"quadruped": true`. `ContentLibrary.TreasureOf(level)` adds up each room's treasure against the
book's treasure-per-encounter table as information; the Rules test
`TreasureYardstickTests` prints it.

**Picking.** Between fights a click on a portrait or a figure picks that character; Shift adds
or removes; Ctrl+A or the ALL plate before the portraits picks everybody (the default). The picked
wear a green ring and their portraits are lit. Whoever is picked walks when the ground is
clicked, the first of them leading, and the first of them is who opens a door, crosses the
bridge, searches a chest or a body, and levels up. A double click on a portrait opens the
character window. In a fight, whoever's turn it is acts. `--explore @Pip 21,42 @all ...` picks on the way
(`Selection.cs`).

**Loot** (`Loot.cs`). Containers are drawn where the level puts them (`art/props/<look>.glb`, a
stand-in shape until a model exists), with a gold mark over anything still shut or not yet empty;
the fallen keep that mark while they have something on them. Clicking one walks the picked
leader there and opens it — a lock asks for Disable Device or Strength, as a door does — and the
loot window shows what is inside: click a thing to take it, or Take all. After a fight the camp
row's **Loot (n)** opens every open container within twelve squares at once. Taken things go
into the party's shared bag; nothing is put on by taking it. Hidden containers appear, with a
line in the log, when somebody notices them. An `--autoplay` run takes everything and puts on
what suits each character (`Outfitter.EquipBest`); `--show-loot` keeps the window up instead.

**The character window** (`Inventory.cs`, I or a double-click on a portrait). Slots round the
figure itself, armed as it is on the board; the party's bag as a grid with filters; the purse
and the party's load under it, and what the picked character carries and moves under beside the
figure. Click a thing to read it, double-click or drag it to put it on or take it off, or use
the buttons: give, hang on the belt, drop (it lands as a pile on the ground). Green and red
arrows say whether a thing in the bag is better or worse for the picked character than what
they have on (`Outfitter.Compare`). The Sheet tab is the old character sheet. `--character`
opens the window at start, for looking at it under `xvfb-run`.

**Using things in a fight** (`Items.cs`). Only what is on a character's belt can be reached
mid-fight — hang potions and flasks there in the character window before it starts ("Hang on
the belt", "Hang all"; the belt shows stacks as ×N). The Items button (9) lists the actor's belt:
with a potion picked, click the actor to drink it (standard action, provokes) or a fallen friend
beside them to give it (full round); with a flask, click somebody or, for acid, fire and
thunderstones, an empty square. Throws are ranged touch attacks; a miss lands astray, and the
board shows the arc, the hop and a burst in the flask's colour. "Break free" and "Put out the
flames" appear when a tanglefoot bag or alchemist's fire has left something to get out of.
Between fights a potion can be drunk from the window at no cost.

**Merchants** (`Merchants.cs`). Tobin by his overturned cart at the cave mouth (once the orcs
there are dead) and Wenna's cart where the Long Road starts. Click one like a chest: the leader
walks over and the trade window opens — what they sell on the left, the party's bag on the
right, the price or the offer under each thing. Click buys or sells one, Shift-click all of it.
They sell at the book price and buy at half, gems and art at their full worth, and pay at most
500 gp for any one thing (a thorp's purchase limit). Worn and belt things go back in the bag
before they can be sold. Autoplay never trades; `--show-trade` keeps the window up on such a run.

**Hover.** Between fights, whatever a click would use — a container still shut or not yet empty,
a body with something on it, a shut door, a merchant — glows gold with a lit outline and its name over it,
and shows as a faint silhouette through anything in front of it (`Highlight.cs`).

**Walking** between fights goes through friends where it has to (the one-square rope bridge):
the figure passes, the rules move it on the next free square, and nobody stops on a square
somebody else is standing on.

**Saves** (`Saves.cs`). Every save is its own file in `user://saves`, with a small `.card`
beside it saying what and where it is: a quicksave (F5), three rotating autosaves (as a fight
opens, once it is won, at the start of an adventure and when the window is closed) and saves
made with "Save new". The title's Continue loads the newest of all of them; Load Game, and F9 in
play, list them. The single `user://ironbound.save` of earlier builds is listed and loaded where
it is, never moved. Runs started with arguments (autoplay, captures) never write saves.
`--menu load` opens the load page at start.

**Moving.** Hovering draws the path the click will take as a glowing pipe from the mover's feet
(`PathPreview.cs`, `path.gdshader`): the rules' own path and the longest part of it this turn
pays for, red past that, a ring where the move stops, an orange mark over each square whose
leaving provokes, and the distance. A click too far away moves as far as the move allows along
that path. Fights only: between them the party just walks, with no line. The ground within
reach is a faint wash with a line round its edge. `-- --hover x,y` holds the pointer over a
square, for checking all of this under `xvfb-run`.

`-- --explore 6,37 6,30 17,23 ...` walks the party to each square in turn, using any feature it
names, once the last walk, page and fight are done. With `--autoplay` a whole level plays itself;
that, under `xvfb-run`, is how both levels were checked end to end.

## The interface

After Wrath's HUD (`src/Ironbound.Game/Hud.cs`): along the foot of the screen, a strip of
parchment in an iron frame holding the acting character and their actions, and under it the
party's portraits in a row; the log on parchment bottom-right; the system buttons in an iron
block bottom-left; the campaign and the turn order top-right.

| Key | Does |
| --- | --- |
| 1 – 9 | Move, Attack, Full attack, Trip, Shove, Demoralize, Help, Cast, Items |
| Z / X / C / M | Power Attack, Combat Expertise, Fight defensively, Deadly Aim |
| R | Rage, or let it go (barbarians only; greyed while fatigued or out of rounds) |
| V / B / N | Rage powers declared before a blow: powerful blow, surprise accuracy, strength surge |
| G / Space | Stand up, End turn |
| L / I | Log, character window: inventory and sheet (or double-click a portrait) |
| Ctrl+A | Between fights: pick the whole party |
| F5 / F9 | Quicksave, the list of saved games |

**Class features.** The Cast list (8) holds more than prepared spells:

- each spell with its slots left, plus a domain or school slot it may use instead
  ("2 + 1 school");
- the same spells again, marked *empowered*, for a caster with Empower Spell and a slot two
  levels up;
- a good cleric's cures, cast in place of a prepared spell of that level or higher;
- class powers with their uses left today (channel energy, Battle Rage, Rebuke Death, Force
  Missile, Acid Dart, bonded spells).

Channel energy is aimed by clicking the cleric herself. Taking a level asks for whatever the
class table asks for at that level (a fighter's bonus feat, a weapon group, a rogue talent or
rage power) and lists the features that come with it, plus the ability raised every fourth
level, the favoured-class hit point or skill rank, and what a feat such as Weapon Focus or Skill
Focus is taken for. Feats the character cannot take are listed with why. The character sheet has
a *Class features* section with what each one is worth right now.

A caster an enemy is standing over gets a **Defensively** toggle beside the Cast list, with the
chance the concentration check holds; ticked, the spell draws no attacks of opportunity and is
lost if the check fails.

Portraits are not art: each is a small viewport with its own copy of the creature's model and a
camera on its face, so anything with a model has a portrait. Icons are drawn in code
(`DrawGlyph`), which is the one method real icon art would replace.

## The camera

| Input | Does |
| --- | --- |
| Mouse wheel | Zoom toward the cursor — what is under the mouse stays under it |
| Middle-drag | Drag the board |
| W A S D | Pan |
| Q / E | Turn 45 degrees about what you are looking at |
| Home or F | Frame the whole board again |

The board is fitted to whatever shape the window is, in the part of the screen the HUD leaves
free, until you take the camera; Home gives it back. While a turn plays out the view follows
anybody acting off-screen.

`-- --camera-tour` drives all of that from a script, for checking it without a hand on the mouse.
`-- --tour-party` aims that tour's zoom at the party instead of the enemy.
`-- --pass-turns` has the party end each turn after a pause, which walks the interface through
everybody's turn — how the per-character button row was checked.

## Seeing it in the game without a screenshot

```sh
godot --path src/Ironbound.Game --write-movie /tmp/frames/f.png --fixed-fps 30 --quit-after 75
```

Add `-- --autoplay` and the party is driven by the same heuristic as the enemy, so a whole fight
plays out — or is recorded, with `--write-movie fight.avi` — without a click. That is how the
animation layer was checked: a recording, and frames pulled out of it with ffmpeg. An autoplayed
run also prints the whole log to the console, so what the frames cannot show (who raged, which
blow carried sneak attack, who failed the jump) can be read off afterwards; that is how the class
features were checked in play.

Renders frames to disk at the project's own 1920x1080 — **if** the window really is that shape.
A tiling window manager will not leave it so: Hyprland tiled the game to 950x1049 and the
recording came out as a squashed slice with half the HUD missing, which looked exactly like a
bug in the game. Run it in a private display instead, which also keeps it off your desktop and
your GPU:

```sh
env -u WAYLAND_DISPLAY xvfb-run -a -s "-screen 0 1920x1080x24" \
    godot --display-driver x11 --rendering-driver opengl3 --path src/Ironbound.Game --resolution 1920x1080
```

and take frames with `import -window root shot.png` from inside the same `xvfb-run`. Run one at
a time: two `xvfb-run -a` started together can land on the same display, and both sets of frames
then show whichever game drew last. Any window
shape can be tested this way; that is how the half-width fit was checked. This is what
caught the goblins importing with white skin: Blender cannot show you what Godot does with a
material, and a tiled half-width window clips the far side of the board.

## What goes wrong, and why

- **Materials from before Blender 2.80 arrive empty.** Blender Internal died in 2.80 and took
  every texture link with it. The images survive; nothing references them.
- **Modular parts ship unbound.** The weights are painted — all 41 vertex groups — but only the
  default parts carry an armature modifier. Miss this and the armour exports as a static prop
  that ignores all 47 animations.
- **Never scale the armature.** glTF ignores a skinned mesh node's transform, so the scale
  survives only in the joint matrices: the character exports twice, once at native size and once
  correctly, and the bound parts shred into ribbons. Export at native size and scale on the
  Godot side, where `Main.Model` already derives it from `CreatureSize`.
- **Export the scene, not a selection.** In background mode these objects refuse to select, and
  an export by selection drops meshes without a word.
- **Link alpha only where a texture uses it.** These sheets are all RGBA and nearly all solid.
  Linking alpha unconditionally marks the whole character transparent, which renders correctly
  in Blender and then loses a dress in Godot, where blended surfaces sort per object.
- **glTF has no standalone alpha map.** Transparency lives in the base colour texture's alpha
  channel and nowhere else, so a mask must be folded in before export.
- **Check the mask polarity.** One pack paints black as opaque, the opposite of the convention.

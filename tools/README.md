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

## `generate_werewolf.py` — the chapter-two boss

```sh
blender -b --factory-startup --python tools/generate_werewolf.py -- <out.glb>
```

Imports `generate_goblin.py` for everything that is not specifically a wolf, so a fix to the
skeleton, the exporter or the validator lands in both. The hide is displaced into clumps before
it is thinned, and the coat is several hundred lofted locks, jittered so they do not repeat,
joined into one mesh per bone. New creatures should start the same way:
a `frame()` of landmarks, a body grown from them, and whatever is sharp or bright added on top.

## `generate_weapons.py` — what the party holds

```sh
blender -b --factory-startup --python tools/generate_weapons.py -- src/Ironbound.Game/art/weapons [name ...]
```

One static `.glb` per hand-held item; an item's content file names it with `"model"`, and
`Armoury.cs` puts the weapon a character fights with in their hands, using a table of grips
measured per idle stance (see `art/PROVENANCE.md`), and slings the rest on their back. Read the convention at the top of the script before adding one: grip at the origin,
business end up, and things you point are built lying forward. Shapes are lofted from
cross-sections (`gg.loft`) — a blade has a fuller and tapers two ways at once, which no
primitive does — and textured by `surface.py` like everything else.

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
| 1 – 7 | Move, Attack, Full attack, Trip, Shove, Help, Cast |
| Z / X / C | Power Attack, Combat Expertise, Fight defensively |
| G / Space | Stand up, End turn |
| L / I | Log, character sheet (or click a portrait) |
| F5 / F9 | Save, Load |

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
animation layer was checked: a recording, and frames pulled out of it with ffmpeg.

Renders frames to disk at the project's own 1920x1080 — **if** the window really is that shape.
A tiling window manager will not leave it so: Hyprland tiled the game to 950x1049 and the
recording came out as a squashed slice with half the HUD missing, which looked exactly like a
bug in the game. Run it in a private display instead, which also keeps it off your desktop and
your GPU:

```sh
env -u WAYLAND_DISPLAY xvfb-run -a -s "-screen 0 1920x1080x24" \
    godot --display-driver x11 --rendering-driver opengl3 --path src/Ironbound.Game --resolution 1920x1080
```

and take frames with `import -window root shot.png` from inside the same `xvfb-run`. Any window
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

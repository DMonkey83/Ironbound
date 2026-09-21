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

## `generate_goblin.py` — build a goblin out of primitives

```sh
blender -b --factory-startup --python tools/generate_goblin.py -- <variant> <out.glb>
```

`variant` is `goblin`, `goblin-archer` or `hobgoblin`. No source art and nothing to licence: the
model is the script. It prints a self-check parsed back out of the exported GLB — mesh count,
skinned count, triangles, joints, animation names, bounding box — and ends in PASS or FAIL on
whether every primitive carries `JOINTS_0`. Trust that, not the absence of a traceback.

Blender API changes that bite this script in particular: the render engine enum is renamed
between versions and assigning an unknown one throws; `Action.fcurves` moved into
layers -> strips -> channelbags in 4.4 and was removed in 5.x; and `bpy.ops.object.convert`
inherits whatever is selected, so deselect before converting a curve or the conversion is
refused and the object reaches the exporter unweighted.

## `preview_model.py` — render what actually came out

```sh
blender -b --factory-startup --python tools/preview_model.py -- out.glb preview.png [action]
```

Imports the `.glb` into an empty scene and renders it from two angles. **Use it.** Every one of
the traps below produced a build log that claimed success.

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

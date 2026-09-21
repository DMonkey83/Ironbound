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
blender -b --factory-startup --python tools/generate_goblin.py
```

Writes `ironbound_goblin.blend` and `.glb` beside the working directory. No source art and
nothing to licence: the model is the script.

## `preview_model.py` — render what actually came out

```sh
blender -b --factory-startup --python tools/preview_model.py -- out.glb preview.png [action]
```

Imports the `.glb` into an empty scene and renders it from two angles. **Use it.** Every one of
the traps below produced a build log that claimed success.

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

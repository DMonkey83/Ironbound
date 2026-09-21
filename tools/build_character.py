"""Assemble one character out of a modular pack and export it, animations and all.

Run: blender -b <in.blend> --python build.py -- <out.glb> <part,part,...> [pose_action]
"""
import sys, bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
OUT, WANTED = argv[0], set(argv[1].split(","))
POSE = argv[2] if len(argv) > 2 else None
TARGET_HEIGHT = 1.20   # a six-foot human against a five-foot square


def log(*a):
    print("BUILD:", *a)


def uses_alpha(image, threshold=0.99):
    """Whether any texel is actually see-through, rather than merely having a fourth channel."""
    import numpy as np

    w, h = image.size
    if w == 0 or h == 0:
        return False

    buffer = np.empty(w * h * 4, dtype=np.float32)
    image.pixels.foreach_get(buffer)
    return bool(buffer[3::4].min() < threshold)


kept, dropped = [], 0
for o in list(bpy.data.objects):
    if o.type != "MESH":
        continue
    if o.name in WANTED:
        # A modular pack ships every part in the file with all but the defaults hidden. Hidden
        # objects cannot be selected, and an export by selection drops them without a word.
        o.hide_set(False)
        o.hide_viewport = False
        o.hide_render = False
        kept.append(o)
    else:
        bpy.data.objects.remove(o, do_unlink=True)
        dropped += 1

missing = WANTED - {o.name for o in kept}
if missing:
    log("!! no such part:", sorted(missing))

log(f"kept {len(kept)} parts, dropped {dropped}:",
    ", ".join(f"{o.name.split('_', 2)[-1]}({len(o.data.polygons)})" for o in kept))
log("total", sum(len(o.data.polygons) for o in kept), "tris")

# Same 2.79-era trap as before: nodes exist, image links do not.
for o in kept:
    for mat in o.data.materials:
        if not mat:
            continue
        has_image = mat.use_nodes and any(n.type == "TEX_IMAGE" for n in mat.node_tree.nodes)
        if has_image:
            continue
        # The skirt is a second material slot cut from the same sheet as the armour it hangs
        # off, so it has no texture of its own to find.
        stem = mat.name[4:]
        names = [stem, stem[:-6]] if stem.endswith("_skirt") else [stem]
        # A few male parts were never given their own sheet and borrow the female one, which
        # the pack itself does for the helmets.
        names += [n.replace("human_male", "human_female") for n in list(names)]
        image = next(
            (img for n in names
             for img in (bpy.data.images.get("tex_" + n), bpy.data.images.get("tex_" + n + ".png"))
             if img is not None),
            None)
        if image is None:
            # The male medium boots and gloves borrow the female sheets, which are on disk but
            # not loaded as datablocks in the male file. Fetch them by path.
            import os
            folder = os.path.join(os.path.dirname(bpy.data.filepath), "textures")
            for n in names:
                candidate = os.path.join(folder, f"tex_{n}.png")
                if os.path.exists(candidate):
                    image = bpy.data.images.load(candidate)
                    log(f"  loaded {os.path.basename(candidate)} from disk")
                    break

        if image is None:
            log("!! no texture found for", mat.name, "(tried", ", ".join(names) + ")")
            continue

        mat.use_nodes = True
        tree = mat.node_tree
        tree.nodes.clear()
        out = tree.nodes.new("ShaderNodeOutputMaterial"); out.location = (400, 0)
        bsdf = tree.nodes.new("ShaderNodeBsdfPrincipled"); bsdf.location = (120, 0)
        tex = tree.nodes.new("ShaderNodeTexImage"); tex.image = image; tex.location = (-220, 0)
        tree.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
        tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])

        # Only where the sheet actually uses its alpha. Every texture in this pack is RGBA and
        # almost all of them are solid, so linking alpha unconditionally marks the whole
        # character as transparent — which renders fine in Blender and then loses Merrin's dress
        # in Godot, where blended surfaces are sorted per object rather than per pixel.
        if uses_alpha(image):
            tree.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
            for attr, value in (("blend_method", "HASHED"), ("surface_render_method", "DITHERED")):
                if hasattr(mat, attr):
                    try:
                        setattr(mat, attr, value)
                    except TypeError:
                        pass
            log(f"  {mat.name} keeps its cut-out")
        else:
            for attr, value in (("blend_method", "OPAQUE"), ("surface_render_method", "DITHERED")):
                if hasattr(mat, attr):
                    try:
                        setattr(mat, attr, value)
                    except TypeError:
                        pass
        bsdf.inputs["Roughness"].default_value = 0.85
        bsdf.inputs["Metallic"].default_value = 0.0
        log(f"  rewired {mat.name} <- {image.name}")

rig = next((o for o in bpy.data.objects if o.type == "ARMATURE"), None)

# The file was saved mid-pose. An armature modifier binds against whatever the rig is doing at
# the time, so every part has to be hooked up from the rest position or the weights are baked
# against a bent skeleton — which is what turns the gloves into ribbons.
if rig:
    if rig.animation_data:
        rig.animation_data.action = None
    for bone in rig.pose.bones:
        bone.matrix_basis.identity()
    bpy.context.view_layer.update()
    log("cleared the saved pose before binding")

# Every part already carries all 41 vertex groups — the weights are painted. Only the two
# default parts are wired to the skeleton; the swappable ones are left loose for you to hook up,
# which is the one step that turns a pile of parts into a character. Without it the armour
# exports as a static prop that ignores all 47 animations.
if rig:
    # Blender's own "parent to armature with existing groups" rather than hand-building the
    # modifier: the operator sets up the bind the same way the author's two wired parts were
    # set up, and a hand-rolled modifier plus a plain parent assignment does not.
    loose = [o for o in kept if not any(m.type == "ARMATURE" for m in o.modifiers)]
    for o in loose:
        mod = o.modifiers.new("Armature", "ARMATURE")
        mod.object = rig
        o.parent = rig
    log(f"bound {len(loose)} loose parts to the skeleton")

lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3)
for o in kept:
    for v in o.data.vertices:
        p = o.matrix_world @ v.co
        for i in range(3):
            lo[i] = min(lo[i], p[i]); hi[i] = max(hi[i], p[i])
log(f"as modelled: {hi.x-lo.x:.3f} x {hi.y-lo.y:.3f} x {hi.z-lo.z:.3f} (Z-up), feet at z={lo.z:.3f}")

# Deliberately NOT scaled here. glTF ignores a skinned mesh node's transform, so scaling the
# armature object survives only in the joint matrices and the round trip renders the character
# twice — once at native size and once correctly. The glTF stays canonical at the size it was
# modelled; Godot sets the scale on the instantiated scene, where it is one line and visible.
log(f"exported at native size; Godot should scale by {TARGET_HEIGHT / (hi.z - lo.z):.4f} "
    f"for a {TARGET_HEIGHT}-unit figure")

if rig and POSE:
    if POSE in bpy.data.actions:
        if not rig.animation_data:
            rig.animation_data_create()
        rig.animation_data.action = bpy.data.actions[POSE]
        bpy.context.scene.frame_set(int(bpy.data.actions[POSE].frame_range[0]) + 4)
        log("posed on", POSE)
    else:
        log("!! no action called", POSE)

# The author's lamps and camera are workspace, not character.
for o in list(bpy.data.objects):
    if o.type in {"LIGHT", "CAMERA", "EMPTY"}:
        bpy.data.objects.remove(o, do_unlink=True)

# Exporting the whole scene rather than a selection. Everything unwanted has already been
# deleted, and selection is the more fragile of the two: these parts refused to select even
# unhidden, and an export by selection says nothing when it drops four of your six meshes.
exported = [o.name for o in bpy.context.scene.objects]
log(f"exporting {len(exported)} objects:", ", ".join(sorted(exported)))

bpy.ops.export_scene.gltf(
    filepath=OUT, export_format="GLB", use_selection=False, export_yup=True,
    export_animations=True, export_materials="EXPORT",
)
log("wrote", OUT)

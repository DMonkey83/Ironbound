"""Turn a 2013 Blender Internal character into a game-ready glTF.

Run:  blender -b <in.blend> --python fixup.py -- <out.glb> [invert_alpha]
"""
import sys, bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
OUT = argv[0]
# This pack paints its masks the other way round: black is opaque, white is cut away. Proved
# by rendering both and diffing — with the usual polarity the torn skirt becomes a pale membrane.
INVERT_ALPHA = not (len(argv) > 1 and argv[1] == "straight")

# One Godot unit is one five-foot square; Main.CapsuleHeight gives a Medium creature 1.4.
TARGET_HEIGHT = 1.30

# Material -> the packed images that belong to it. Paired by UV island against the sheets:
# 'capgonna' is capelli + gonna, hair and skirt sharing one 512 sheet; the body has its own.
MAPPING = {
    "Material.007": ("corpoDefinitivoOpt.pn", None),
    "capelli":      ("capgonnacol.png", "capgonnaAlph.png"),
    "Material":     ("capgonnacol.png", "capgonnaAlph.png"),
}


def log(*a):
    print("FIXUP:", *a)


def purge_junk():
    """The camera, the two lamps and the stray empty are the author's workspace, not the model."""
    for o in list(bpy.data.objects):
        if o.type in {"CAMERA", "LIGHT", "EMPTY"}:
            log("dropping", o.name, o.type)
            bpy.data.objects.remove(o, do_unlink=True)


def flatten():
    """Unparent, drop the rig, and bake every object transform into the vertices."""
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]

    for o in meshes:
        for m in list(o.modifiers):
            # The rest pose deforms nothing, so removing the modifier loses no shape.
            o.modifiers.remove(m)

    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")

    for o in list(bpy.data.objects):
        if o.type == "ARMATURE":
            log("dropping rig", o.name, "(no actions in the file)")
            bpy.data.objects.remove(o, do_unlink=True)

    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    # Two of the three were mirrored with a negative X scale. Baking that in reverses their
    # winding, which reads as a model turned inside out.
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")

    bpy.ops.object.join()
    joined = bpy.context.view_layer.objects.active
    joined.name = "Fleur"
    joined.data.name = "Fleur"
    log("joined into", joined.name,
        f"{len(joined.data.vertices)} verts, {len(joined.data.polygons)} polys,",
        f"{len(joined.data.materials)} materials")
    return joined


def merge_alpha(colour_name, alpha_name):
    """Fold a separate greyscale mask into the colour texture's alpha channel."""
    import numpy as np

    key = f"{colour_name}+alpha"
    if key in bpy.data.images:
        return bpy.data.images[key]

    colour = bpy.data.images[colour_name]
    mask = bpy.data.images[alpha_name]
    mask.colorspace_settings.name = "Non-Color"

    if tuple(colour.size) != tuple(mask.size):
        log("!! mask", alpha_name, mask.size, "does not match", colour_name, colour.size)
        return colour

    w, h = colour.size
    rgba = np.empty(w * h * 4, dtype=np.float32)
    colour.pixels.foreach_get(rgba)
    cut = np.empty(w * h * 4, dtype=np.float32)
    mask.pixels.foreach_get(cut)

    keep = cut[0::4]
    if INVERT_ALPHA:
        keep = 1.0 - keep
    rgba[3::4] = keep

    merged = bpy.data.images.new(key, w, h, alpha=True)
    merged.pixels.foreach_set(rgba)
    merged.pack()
    log(f"merged {alpha_name} into {colour_name} alpha "
        f"(opaque {float((keep > 0.5).mean()) * 100:.0f}% of the sheet)")
    return merged


def rebuild_material(mat):
    """Blender Internal died in 2.80 and took every texture link with it. Wire them back."""
    colour_name, alpha_name = MAPPING.get(mat.name, (None, None))
    if colour_name is None:
        log("!! no mapping for material", mat.name)
        return

    mat.use_nodes = True
    tree = mat.node_tree
    tree.nodes.clear()

    out = tree.nodes.new("ShaderNodeOutputMaterial")
    out.location = (500, 0)
    bsdf = tree.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (200, 0)
    tree.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])

    # Flat-painted 2013 textures. Any specular at all makes them look like wet plastic.
    bsdf.inputs["Roughness"].default_value = 0.85
    bsdf.inputs["Metallic"].default_value = 0.0

    colour = tree.nodes.new("ShaderNodeTexImage")
    colour.image = bpy.data.images[colour_name]
    colour.location = (-300, 100)
    tree.links.new(colour.outputs["Color"], bsdf.inputs["Base Color"])

    if alpha_name:
        # glTF has nowhere to put a standalone alpha map: transparency lives in the alpha
        # channel of the base colour texture and nowhere else. Linking the mask to the
        # Principled BSDF's Alpha input renders correctly in Blender and then vanishes on
        # export, which is exactly the trap that produced two pixel-identical test renders.
        merged = merge_alpha(colour_name, alpha_name)
        colour.image = merged
        tree.links.new(colour.outputs["Alpha"], bsdf.inputs["Alpha"])

        # Cut-out rather than blended: hair cards sorted by depth flicker, and a hard edge is
        # what the mask was painted for anyway.
        for attr, value in (("blend_method", "CLIP"), ("surface_render_method", "DITHERED")):
            if hasattr(mat, attr):
                try:
                    setattr(mat, attr, value)
                except TypeError:
                    pass

    log(f"material {mat.name!r} <- {colour_name}" + (f" + alpha {alpha_name}" if alpha_name else ""))


def stand_on_the_origin(obj):
    """Feet at zero, centred, and scaled to a Medium creature."""
    lo = Vector((1e9,) * 3)
    hi = Vector((-1e9,) * 3)
    for v in obj.data.vertices:
        p = obj.matrix_world @ v.co
        for i in range(3):
            lo[i] = min(lo[i], p[i])
            hi[i] = max(hi[i], p[i])

    height = hi.z - lo.z
    scale = TARGET_HEIGHT / height
    log(f"was {hi.x-lo.x:.2f} x {hi.y-lo.y:.2f} x {height:.2f} (Z-up), scaling by {scale:.5f}")

    # Blender is Z-up; the glTF exporter turns it into Godot's Y-up on the way out.
    shift = Vector((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z))
    for v in obj.data.vertices:
        v.co = (v.co + shift) * scale

    lo = Vector((1e9,) * 3)
    hi = Vector((-1e9,) * 3)
    for v in obj.data.vertices:
        for i in range(3):
            lo[i] = min(lo[i], v.co[i])
            hi[i] = max(hi[i], v.co[i])
    log(f"now {hi.x-lo.x:.3f} x {hi.y-lo.y:.3f} x {hi.z-lo.z:.3f}, "
        f"feet at z={lo.z:.4f}, centred at ({(lo.x+hi.x)/2:+.4f}, {(lo.y+hi.y)/2:+.4f})")


purge_junk()
fleur = flatten()
for mat in fleur.data.materials:
    rebuild_material(mat)
stand_on_the_origin(fleur)

bpy.ops.object.select_all(action="DESELECT")
fleur.select_set(True)
bpy.ops.export_scene.gltf(
    filepath=OUT,
    export_format="GLB",
    use_selection=True,
    export_yup=True,
    export_apply=True,
    export_materials="EXPORT",
)
log("wrote", OUT)

"""Inventory icons: every hand-held model drawn the same way, for chests, loot and the sheet.

    blender -b --factory-startup --python tools/render_icons.py -- <glb-directory> <output-directory> [id ...]

One picture per .glb, named after it: src/Ironbound.Game/art/weapons/longsword.glb becomes
<output>/longsword.png, 256 pixels square with a transparent background, so whatever slot the
interface puts behind it shows through.

Rendered from the models the game already holds rather than painted, because there are a few
hundred weapons and a painted icon that disagrees with the sword in Valeria's hand is worse than
no icon. The pose is the one inventories have always used: grip at the bottom left, business end
at the top right, turned a little about its own length so a blade shows its bevel and fuller
rather than a flat silhouette. Shields face the viewer instead, a little turned.

The light is fixed for every icon — a warm key from the upper left, a cool rim from behind, and a
sky that is bright above and dark below for steel to reflect — so a row of icons reads as one set.

ICONS KEEP RELATIVE SIZE. Scaling every model to fill its slot made a dagger as big as a
greatsword. The frame is set by the weapon's hands class instead (`weapon_families.hands`): a
light weapon fills 60-70% of the slot's diagonal, a one-handed one 80-85%, two-handed weapons and
polearms all of it, bows and crossbows 90%, shields as before. Within a class a longer weapon
takes the larger share, so a short sword still stands over a dagger.

A model exported with the shared named materials (`Steel`, `Wood`, `Wrap`...) and no textures
is drawn with the procedural recipe for each name, so its icon is steel and leather and not
flat grey; a baked model is drawn as it is.
"""
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import weapon_families as wf  # noqa: E402

SIZE = 256
MARGIN = 1.12

# Share of the slot's diagonal by hands class, from the shortest to the longest of the class,
# and the lengths (model units) those two ends belong to.
FILL = {
    "light": (0.60, 0.70, 0.9, 1.7),
    "one": (0.80, 0.85, 1.9, 2.5),
    "two": (1.0, 1.0, 0, 1),
    "polearm": (1.0, 1.0, 0, 1),
    "bow": (0.9, 0.9, 0, 1),
    "crossbow": (0.9, 0.9, 0, 1),
    "shield": (1.0, 1.0, 0, 1),
}


def fill(name, length):
    lo, hi, short, long_ = FILL[wf.hands(name)]
    k = min(1.0, max(0.0, (length - short) / max(long_ - short, 1e-6)))
    return lo + (hi - lo) * k


def clear():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.lights, bpy.data.cameras):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def bounds(objects):
    dg = bpy.context.evaluated_depsgraph_get()
    lo = Vector((1e9,) * 3)
    hi = Vector((-1e9,) * 3)
    for o in objects:
        for v in o.evaluated_get(dg).data.vertices:
            p = o.matrix_world @ v.co
            for i in range(3):
                lo[i] = min(lo[i], p[i])
                hi[i] = max(hi[i], p[i])
    return lo, hi


def pose(objects, name):
    """Turns the model into the inventory pose, centred on the origin."""
    lo, hi = bounds(objects)
    size = hi - lo

    if "shield" in name:
        # Its face looks down -Y, which is where the camera is: a quarter turn off square, so
        # it reads as a thing with a rim and not a disc.
        turn = Matrix.Rotation(math.radians(-24), 4, "Z") @ Matrix.Rotation(math.radians(10), 4, "X")
    else:
        # Everything is modelled grip at the origin and length up +Z, except what is pointed
        # rather than swung (a crossbow), which runs down +Y. Stand that one up first.
        upright = Matrix.Rotation(math.radians(90), 4, "X") if size.y > size.z else Matrix.Identity(4)
        # Twist about the length so the flat of a blade catches the key light at an angle, then
        # lean the whole thing over to the diagonal.
        turn = (
            Matrix.Rotation(math.radians(45), 4, "Y")
            @ Matrix.Rotation(math.radians(22), 4, "Z")
            @ upright
        )

    for o in objects:
        o.matrix_world = turn @ o.matrix_world

    lo, hi = bounds(objects)
    centre = (lo + hi) / 2
    for o in objects:
        o.matrix_world = Matrix.Translation(-centre) @ o.matrix_world

    lo, hi = bounds(objects)
    return max(hi.x - lo.x, hi.z - lo.z)


def light(scene):
    world = bpy.data.worlds.new("Icon_Sky")
    scene.world = world
    world.use_nodes = True
    nt = world.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputWorld")
    bg = nt.nodes.new("ShaderNodeBackground")
    coords = nt.nodes.new("ShaderNodeTexCoord")
    split = nt.nodes.new("ShaderNodeSeparateXYZ")
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    nt.links.new(coords.outputs["Generated"], split.inputs[0])
    nt.links.new(split.outputs["Z"], ramp.inputs[0])
    nt.links.new(ramp.outputs[0], bg.inputs["Color"])
    nt.links.new(bg.outputs[0], out.inputs[0])
    ramp.color_ramp.elements[0].position = 0.35
    ramp.color_ramp.elements[0].color = (0.015, 0.014, 0.013, 1)
    ramp.color_ramp.elements[1].position = 0.75
    ramp.color_ramp.elements[1].color = (0.55, 0.52, 0.47, 1)
    bg.inputs["Strength"].default_value = 0.9

    def lamp(name, location, energy, colour, size):
        data = bpy.data.lights.new(name, "AREA")
        data.energy = energy
        data.color = colour
        data.size = size
        obj = bpy.data.objects.new(name, data)
        scene.collection.objects.link(obj)
        obj.location = location
        direction = -Vector(location)
        obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()

    lamp("Key", (-4.0, -5.0, 5.0), 900, (1.0, 0.93, 0.82), 4.0)
    lamp("Rim", (4.5, 4.0, 2.5), 700, (0.75, 0.85, 1.0), 3.0)
    lamp("Fill", (2.0, -6.0, -2.0), 180, (1.0, 1.0, 1.0), 5.0)


def camera(scene, extent, share=1.0):
    data = bpy.data.cameras.new("Icon_Camera")
    data.type = "ORTHO"
    data.ortho_scale = extent * MARGIN / share
    obj = bpy.data.objects.new("Icon_Camera", data)
    scene.collection.objects.link(obj)
    obj.location = (0, -20, 0)
    obj.rotation_euler = (math.radians(90), 0, 0)
    scene.camera = obj


def render(glb, out):
    clear()
    scene = bpy.context.scene
    bpy.ops.import_scene.gltf(filepath=glb)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    if not meshes:
        print("ICON: !! nothing to draw in", glb)
        return

    name = os.path.splitext(os.path.basename(glb))[0]
    for o in meshes:
        for slot in o.material_slots:
            stand_in = wf.procedural(slot.material.name) if slot.material else None
            if stand_in is not None:
                slot.material = stand_in
    lo, hi = bounds(meshes)
    length = max(hi - lo)
    extent = pose(meshes, name)
    light(scene)
    camera(scene, extent, fill(name, length))

    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    scene.render.film_transparent = True
    scene.render.resolution_x = SIZE
    scene.render.resolution_y = SIZE
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "AgX"
    scene.render.filepath = out
    bpy.ops.render.render(write_still=True)
    print(f"ICON {name:24} -> {out}")


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if len(args) < 2:
        raise RuntimeError("Usage: blender -b --factory-startup --python render_icons.py -- GLB_DIRECTORY OUTPUT_DIRECTORY [id ...]")
    source, target = args[0], args[1]
    os.makedirs(target, exist_ok=True)
    wanted = args[2:] or sorted(f[:-4] for f in os.listdir(source) if f.endswith(".glb"))
    for item in wanted:
        render(os.path.join(source, f"{item}.glb"), os.path.join(target, f"{item}.png"))

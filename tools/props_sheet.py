"""Renders of the props as the game will show them, for judging by eye.

    blender -b --factory-startup --python tools/props_sheet.py -- <glb-dir> <out-dir> <id>[:states] ... [--closeup id:x,y,z:size] [--scale karn.glb]

Each prop is drawn from the game camera's default angle (`CameraRig`: 37.5 degrees above the
horizon, from the south-east) on a dark flagstone floor, under a warm torch and a little cool
fill, in each state its nodes allow:

- `locked`: lid shut, lock on;     - `closed`: lid shut, lock gone (picked);
- `open`: lid opened as the game will (a hinged lid turned -100 degrees about its hinge, a
  loose lid lifted and set down beside), contents in;
- `empty`: open, contents hidden.

Writes <out-dir>/<id>_<state>.png at 512 square. Nothing here is part of the build.
"""
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

PITCH = math.atan2(13.0, 12.0 * math.sqrt(2.0))
YAW = math.pi / 4


def clear():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.lights, bpy.data.cameras):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def floor(scene):
    bpy.ops.mesh.primitive_plane_add(size=12, location=(0, 0, 0))
    f = bpy.context.active_object
    f.name = "Floor"
    m = bpy.data.materials.new("Flagstone")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    co = nt.nodes.new("ShaderNodeTexCoord")
    vor = nt.nodes.new("ShaderNodeTexVoronoi")
    vor.feature = "DISTANCE_TO_EDGE"
    vor.inputs["Scale"].default_value = 2.2
    nt.links.new(co.outputs["Object"], vor.inputs[0])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.0
    ramp.color_ramp.elements[0].color = (0.02, 0.02, 0.022, 1)
    ramp.color_ramp.elements[1].position = 0.05
    ramp.color_ramp.elements[1].color = (0.11, 0.105, 0.11, 1)
    nt.links.new(vor.outputs[0], ramp.inputs[0])
    nz = nt.nodes.new("ShaderNodeTexNoise")
    nz.inputs["Scale"].default_value = 6.0
    nt.links.new(co.outputs["Object"], nz.inputs[0])
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.blend_type = "MULTIPLY"
    mix.inputs[0].default_value = 0.6
    nt.links.new(ramp.outputs[0], mix.inputs[6])
    nt.links.new(nz.outputs[1], mix.inputs[7])
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.85
    f.data.materials.append(m)
    return f


def lights(scene, target=Vector((0, 0, 0.3))):
    world = bpy.data.worlds.new("Cave")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.020, 0.024, 0.032, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 1.0

    def lamp(name, kind, loc, energy, colour, size=0.2):
        d = bpy.data.lights.new(name, kind)
        d.energy = energy
        d.color = colour
        if kind in ("POINT", "SPOT"):
            d.shadow_soft_size = size
        else:
            d.size = size
        o = bpy.data.objects.new(name, d)
        scene.collection.objects.link(o)
        o.location = loc
        o.rotation_euler = (target - Vector(loc)).to_track_quat("-Z", "Y").to_euler()

    # A torch on the wall up and to the left of the camera, a lantern behind the party, and
    # the cold of the cave as the sky.
    lamp("Torch", "POINT", (-1.6, -1.3, 2.0), 260, (1.0, 0.62, 0.32), 0.15)
    lamp("Lantern", "POINT", (2.2, -2.4, 1.5), 90, (1.0, 0.80, 0.55), 0.3)
    lamp("Rim", "AREA", (1.0, 2.5, 1.8), 40, (0.6, 0.7, 1.0), 2.0)


def camera(scene, centre, radius, distance_scale=1.0):
    d = Vector((math.sin(YAW) * math.cos(PITCH), -math.cos(YAW) * math.cos(PITCH), math.sin(PITCH)))
    cam = bpy.data.cameras.new("Cam")
    cam.lens = 50
    o = bpy.data.objects.new("Cam", cam)
    scene.collection.objects.link(o)
    fov = 2 * math.atan(18.0 / cam.lens)
    dist = radius / math.sin(fov / 2) * 1.05 * distance_scale
    o.location = Vector(centre) + d * dist
    o.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    scene.camera = o
    return o


def setup(scene, res=512, samples=64):
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.render.resolution_x = res
    scene.render.resolution_y = res
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.exposure = 0.0


def load(glb):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=glb)
    new = [o for o in bpy.data.objects if o not in before]
    return new


def node(objects, name):
    for o in objects:
        if o.name == name or o.name.startswith(name + "."):
            return o
    return None


def world_box(objects):
    lo = Vector((1e9,) * 3)
    hi = Vector((-1e9,) * 3)
    for o in objects:
        if o.type != "MESH" or o.hide_render:
            continue
        for v in o.bound_box:
            p = o.matrix_world @ Vector(v)
            lo = Vector(map(min, lo, p))
            hi = Vector(map(max, hi, p))
    return lo, hi


def set_state(objects, state):
    lid, contents, lock = node(objects, "Lid"), node(objects, "Contents"), node(objects, "Lock")
    if lid is not None:
        lid["home"] = lid.get("home", list(lid.location))
        lid.location = Vector(lid["home"])
        lid.rotation_mode = "XYZ"
        lid.rotation_euler = (0, 0, 0)
    if lock is not None:
        lock.hide_render = state != "locked"
    if contents is not None:
        contents.hide_render = state == "empty"
    if state in ("open", "empty") and lid is not None:
        hinge = lid.get("hinge", "x")
        opening = lid.get("open", -100)
        if hinge == "none" or opening == "lift":
            # Lifted off and set down leaning on the side away from the camera.
            lo, hi = world_box([lid])
            width = hi.x - lo.x
            lid.location = Vector(lid["home"]) + Vector((-width * 0.25, (hi.y - lo.y) * 0.9 + 0.05, 0.0))
            lid.rotation_euler = (math.radians(-65), 0, math.radians(6))
        else:
            lid.rotation_euler = (math.radians(float(opening)), 0, 0)


def render(scene, path):
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("SHEET", path)


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    source, out = args[0], args[1]
    os.makedirs(out, exist_ok=True)
    jobs, closeups, scale_with = [], [], None
    i = 2
    while i < len(args):
        if args[i] == "--closeup":
            closeups.append(args[i + 1])
            i += 2
        elif args[i] == "--scale":
            scale_with = args[i + 1]
            i += 2
        else:
            jobs.append(args[i])
            i += 1
    scene = bpy.context.scene
    for job in jobs:
        name, _, states = job.partition(":")
        states = states.split(",") if states else ["locked", "closed", "open", "empty"]
        clear()
        setup(scene)
        floor(scene)
        objects = load(os.path.join(source, f"{name}.glb"))
        have_lock = node(objects, "Lock") is not None
        have_lid = node(objects, "Lid") is not None
        lights(scene)
        set_state(objects, "open")
        bpy.context.view_layer.update()
        lo, hi = world_box(objects)
        set_state(objects, "closed")
        bpy.context.view_layer.update()
        lo2, hi2 = world_box(objects)
        lo, hi = Vector(map(min, lo, lo2)), Vector(map(max, hi, hi2))
        centre = (lo + hi) / 2
        radius = max((hi - lo).length / 2, 0.3)
        camera(scene, centre, radius)
        for state in states:
            if state == "locked" and not have_lock:
                continue
            if state in ("open", "empty") and not have_lid and node(objects, "Contents") is None:
                continue
            set_state(objects, state)
            render(scene, os.path.join(out, f"{name}_{state}.png"))
    for spec in closeups:
        name, at, size = spec.split(":")
        clear()
        setup(scene, 512, 96)
        floor(scene)
        objects = load(os.path.join(source, f"{name}.glb"))
        lights(scene)
        set_state(objects, "locked")
        centre = Vector(tuple(float(v) for v in at.split(",")))
        camera(scene, centre, float(size))
        render(scene, os.path.join(out, f"{name}_closeup_{at.replace(',', '_')}.png"))
    if scale_with:
        name, _, figure = scale_with.partition("=")
        clear()
        setup(scene, 640, 64)
        floor(scene)
        objects = load(os.path.join(source, f"{name}.glb"))
        set_state(objects, "closed")
        for o in objects:
            if o.parent is None:
                o.location.x += 0.35
        man = load(figure)
        meshes = [o for o in man if o.type == "MESH" and o.parent is not None]
        for o in man:
            if o.type == "MESH" and o.parent is None and o.name.startswith("Icosphere"):
                o.hide_render = True
        lo, hi = world_box(meshes)
        k = 1.4 / (hi.z - lo.z)
        root = [o for o in man if o.parent is None and not o.name.startswith("Icosphere")]
        for o in root:
            o.matrix_world = Matrix.Translation((-0.62, -0.02, -lo.z * k)) @ Matrix.Rotation(math.radians(-20), 4, "Z") @ Matrix.Scale(k, 4) @ o.matrix_world
        lights(scene, Vector((0, 0, 0.7)))
        camera(scene, Vector((-0.05, 0.1, 0.62)), 0.95)
        render(scene, os.path.join(out, f"{name}_scale.png"))


if __name__ == "__main__":
    main()

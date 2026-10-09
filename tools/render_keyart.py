"""Key art for the menus, rendered from the game's own models.

    blender -b --factory-startup --python tools/render_keyart.py -- <scene> <out.png> [width height]

`scene` is `title`, `the-long-road` or `caves-of-shadow`. Each poses the creatures the game
already ships, in their own idle clips, on generated ground under lights chosen for the mood,
and renders with Cycles. Nothing here is drawn or borrowed: the picture on the front door is
the game's own cast, which is the only art the project can be sure it owns.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

ART = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "Ironbound.Game", "art")


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def creature(name, at, height, facing_deg, clip="idle", frame=6):
    """Imports art/<name>.glb, poses it on `clip`, stands it `height` tall at `at`, turned to face
    `facing_deg` (0 faces the camera's -Y, as the models are built)."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(ART, f"{name}.glb"))
    new = [o for o in bpy.data.objects if o not in before]
    rig = next((o for o in new if o.type == "ARMATURE"), None)
    if rig is not None:
        action = next((a for a in bpy.data.actions if clip in a.name and a.users >= 0 and a.name.split("_")[0] in name.replace("-", "_") + "_rat_goblin_werewolf_human"), None)
        action = action or next((a for a in bpy.data.actions if clip in a.name), None)
        if action is not None:
            rig.animation_data_create()
            rig.animation_data.action = action
            rig["frame"] = frame
    holder = bpy.data.objects.new(f"{name}_root", None)
    bpy.context.scene.collection.objects.link(holder)
    for o in new:
        if o.parent is None:
            o.parent = holder
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    lo, hi = bounds([o for o in new if o.type == "MESH"])
    scale = height / max(hi.z - lo.z, 1e-6)
    holder.scale = (scale,) * 3
    holder.rotation_euler = (0, 0, math.radians(facing_deg))
    holder.location = (at[0], at[1], -lo.z * scale)
    return holder


def bounds(meshes):
    dg = bpy.context.evaluated_depsgraph_get()
    lo = Vector((1e9,) * 3)
    hi = Vector((-1e9,) * 3)
    for o in meshes:
        ev = o.evaluated_get(dg)
        for v in ev.data.vertices:
            p = o.matrix_world @ v.co
            for i in range(3):
                lo[i] = min(lo[i], p[i])
                hi[i] = max(hi[i], p[i])
    return lo, hi


def ground(colour, rock=0.5, size=60):
    """A broken, stony ground: noise for colour and for a bump, no tiles and no seams."""
    bpy.ops.mesh.primitive_plane_add(size=size)
    plane = bpy.context.object
    m = bpy.data.materials.new("Ground")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    co = nt.nodes.new("ShaderNodeTexCoord").outputs["Object"]
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 0.6
    noise.inputs["Detail"].default_value = 8
    nt.links.new(co, noise.inputs[0])
    vor = nt.nodes.new("ShaderNodeTexVoronoi")
    vor.inputs["Scale"].default_value = 2.5
    nt.links.new(co, vor.inputs[0])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (*[c * 0.45 for c in colour], 1)
    ramp.color_ramp.elements[1].color = (*colour, 1)
    nt.links.new(noise.outputs[0], ramp.inputs[0])
    nt.links.new(ramp.outputs[0], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.95
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = rock
    nt.links.new(vor.outputs[0], bump.inputs["Height"])
    nt.links.new(bump.outputs[0], bsdf.inputs["Normal"])
    plane.data.materials.append(m)
    return plane


def boulder(at, size, colour):
    """A lump of rock: an icosphere roughened by displacement."""
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=4, radius=size, location=at)
    rock = bpy.context.object
    tex = bpy.data.textures.new(f"Rock_{len(bpy.data.textures)}", "VORONOI")
    tex.noise_scale = size * 0.6
    mod = rock.modifiers.new("Rough", "DISPLACE")
    mod.texture = tex
    mod.strength = size * 0.35
    rock.scale = (1.0, 0.8, 0.65)
    m = bpy.data.materials.new("Rock")
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*colour, 1)
    m.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.9
    rock.data.materials.append(m)
    for p in rock.data.polygons:
        p.use_smooth = True
    return rock


def light(kind, at, aim, energy, colour, size=1.0):
    data = bpy.data.lights.new(kind, type=kind)
    data.energy = energy
    data.color = colour
    if kind in ("AREA",):
        data.size = size
    if kind == "POINT":
        data.shadow_soft_size = size
    obj = bpy.data.objects.new(kind, data)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = at
    obj.rotation_euler = (Vector(aim) - Vector(at)).to_track_quat("-Z", "Y").to_euler()
    return obj


def sky(top, bottom, strength=1.0):
    world = bpy.data.worlds.new("Sky")
    bpy.context.scene.world = world
    world.use_nodes = True
    nt = world.node_tree
    bg = nt.nodes["Background"]
    grad = nt.nodes.new("ShaderNodeTexGradient")
    mapping = nt.nodes.new("ShaderNodeMapping")
    co = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(co.outputs["Generated"], sep.inputs[0])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (*bottom, 1)
    ramp.color_ramp.elements[1].color = (*top, 1)
    nt.links.new(sep.outputs["Z"], ramp.inputs[0])
    nt.links.new(ramp.outputs[0], bg.inputs[0])
    bg.inputs[1].default_value = strength


def camera(at, aim, lens=50):
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    bpy.context.scene.collection.objects.link(cam)
    cam.location = at
    cam.rotation_euler = (Vector(aim) - Vector(at)).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = lens
    bpy.context.scene.camera = cam
    return cam


def moon(at, radius):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=radius, location=at)
    disc = bpy.context.object
    m = bpy.data.materials.new("Moon")
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs[0].default_value = (0.85, 0.90, 1.0, 1)
    em.inputs[1].default_value = 6.0
    nt.links.new(em.outputs[0], out.inputs[0])
    disc.data.materials.append(m)


def scene_title():
    """The werewolf on a rock under the moon, on the right; the left is left dark for the menu."""
    sky((0.010, 0.016, 0.040), (0.040, 0.050, 0.080), 1.0)
    ground((0.10, 0.10, 0.11), 0.6)
    boulder((3.2, 1.2, 0.0), 1.6, (0.16, 0.16, 0.18))
    boulder((5.6, 3.5, 0.0), 2.4, (0.12, 0.12, 0.14))
    boulder((-1.0, 6.0, 0.0), 3.0, (0.10, 0.10, 0.12))
    wolf = creature("werewolf", (3.0, 1.0), 2.5, 25, "idle", 7)
    wolf.location.z += 0.9
    creature("goblin", (0.6, -1.2), 1.1, -20, "idle", 3)
    creature("goblin-archer", (-0.4, 0.2), 1.1, -10, "idle", 9)
    moon((8.0, 30.0, 14.0), 3.2)
    light("SUN", (6, 20, 12), (2.5, 0.8, 1.5), 2.2, (0.65, 0.75, 1.0))
    light("AREA", (-4.0, -3.0, 2.5), (2.6, 1.0, 1.8), 900, (1.0, 0.55, 0.25), 2.0)
    light("AREA", (4.5, 5.0, 4.0), (3.0, 1.0, 2.2), 600, (0.5, 0.65, 1.0), 3.0)
    camera((-0.6, -9.5, 2.3), (1.6, 1.0, 1.7), 38)


def scene_long_road():
    """Goblins and their sergeant on stony ground at dusk."""
    sky((0.30, 0.16, 0.10), (0.70, 0.40, 0.20), 0.9)
    ground((0.30, 0.24, 0.17), 0.5)
    boulder((-2.6, 2.5, 0.0), 1.4, (0.25, 0.20, 0.16))
    boulder((3.0, 3.5, 0.0), 1.8, (0.22, 0.18, 0.15))
    creature("hobgoblin", (0.0, 0.6), 1.9, 10, "idle", 5)
    creature("goblin", (-1.4, -0.2), 1.15, 25, "idle", 2)
    creature("goblin-archer", (1.5, 0.4), 1.15, -20, "idle", 11)
    light("SUN", (-6, 12, 3), (0, 0, 1), 3.0, (1.0, 0.62, 0.35))
    light("AREA", (-3.5, -4.0, 2.5), (0, 0.3, 1.0), 500, (1.0, 0.8, 0.6), 3.0)
    light("AREA", (4.0, -2.0, 1.5), (0, 0.3, 1.0), 200, (0.5, 0.6, 1.0), 2.0)
    camera((0.0, -6.8, 1.6), (0.0, 0.4, 1.0), 42)


def scene_caves():
    """The ogre chief and his orcs in a torch-lit cave, the rat at their feet."""
    sky((0.008, 0.006, 0.006), (0.015, 0.012, 0.010), 1.0)
    ground((0.18, 0.15, 0.12), 0.7)
    for at, size in (((-3.5, 2.5, 0.5), 2.2), ((3.8, 2.8, 0.6), 2.4), ((0.0, 6.0, 1.0), 4.0), ((-5.0, -1.0, 0.4), 1.8), ((5.2, -0.5, 0.4), 1.6)):
        boulder(at, size, (0.16, 0.14, 0.12))
    creature("ogre", (0.0, 1.4), 2.9, 0, "idle", 5)
    creature("orc", (-1.5, 0.2), 1.9, 25, "idle", 2)
    creature("orc", (1.6, 0.4), 1.9, -25, "idle", 9)
    creature("dire-rat", (0.6, -0.9), 0.55, -35, "idle", 4)
    light("POINT", (-2.4, -0.6, 2.2), (0, 0, 1), 900, (1.0, 0.55, 0.22), 0.3)
    light("POINT", (2.8, 2.6, 2.6), (0, 0, 1), 500, (1.0, 0.50, 0.20), 0.3)
    light("AREA", (0.0, 5.0, 3.5), (0, 1.0, 1.5), 300, (0.4, 0.5, 0.8), 3.0)
    camera((0.0, -7.0, 1.7), (0.0, 0.6, 1.3), 40)


def _material(name, colour, rough=0.8, metal=0.0, emission=None, strength=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*colour, 1)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if emission:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1)
        bsdf.inputs["Emission Strength"].default_value = strength
    return m


def _paper(name, tint=(0.80, 0.68, 0.48), ink=0.22, lines=True):
    """Old paper: a warm off-white, stained toward the edges and in blotches, with faint rows of
    something written long ago — enough texture that the menu written over it reads as ink on a
    page and not as type on a card."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    co = nt.nodes.new("ShaderNodeTexCoord").outputs["UV"]
    stain = nt.nodes.new("ShaderNodeTexNoise")
    stain.inputs["Scale"].default_value = 3.5
    stain.inputs["Detail"].default_value = 6
    nt.links.new(co, stain.inputs[0])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (*[c * 0.72 for c in tint], 1)
    ramp.color_ramp.elements[1].color = (*tint, 1)
    ramp.color_ramp.elements[0].position = 0.35
    ramp.color_ramp.elements[1].position = 0.65
    nt.links.new(stain.outputs[0], ramp.inputs[0])
    colour = ramp.outputs[0]
    if lines:
        # Rows of scrawl: a wave texture across the page, broken up by noise so it reads as
        # writing rather than ruled lines.
        mapping = nt.nodes.new("ShaderNodeMapping")
        nt.links.new(co, mapping.inputs[0])
        wave = nt.nodes.new("ShaderNodeTexWave")
        wave.wave_type = "BANDS"
        wave.bands_direction = "Y"
        wave.inputs["Scale"].default_value = 22
        wave.inputs["Distortion"].default_value = 6
        wave.inputs["Detail"].default_value = 4
        wave.inputs["Detail Scale"].default_value = 6
        nt.links.new(mapping.outputs[0], wave.inputs[0])
        scrawl = nt.nodes.new("ShaderNodeTexNoise")
        scrawl.inputs["Scale"].default_value = 60
        nt.links.new(co, scrawl.inputs[0])
        cut = nt.nodes.new("ShaderNodeMath")
        cut.operation = "MULTIPLY"
        nt.links.new(wave.outputs[1], cut.inputs[0])
        nt.links.new(scrawl.outputs[0], cut.inputs[1])
        mask = nt.nodes.new("ShaderNodeValToRGB")
        mask.color_ramp.elements[0].position = 0.42
        mask.color_ramp.elements[1].position = 0.55
        nt.links.new(cut.outputs[0], mask.inputs[0])
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        nt.links.new(mask.outputs[0], mix.inputs[0])
        f = nt.nodes.new("ShaderNodeMath")
        f.operation = "MULTIPLY"
        nt.links.new(mask.outputs[0], f.inputs[0])
        f.inputs[1].default_value = ink
        nt.links.new(f.outputs[0], mix.inputs[0])
        mix.inputs[6].default_value = (1, 1, 1, 1)
        nt.links.new(colour, mix.inputs[6])
        mix.inputs[7].default_value = (0.25, 0.18, 0.22, 1)
        colour = mix.outputs[2]
    nt.links.new(colour, bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.9
    return m


def _sheet(name, size, at, turn_deg, material, curl=0.0, bend=0.0):
    """A sheet of paper: a subdivided plane, its corner lifted and the whole thing gently bent."""
    bpy.ops.mesh.primitive_grid_add(x_subdivisions=24, y_subdivisions=24, size=1.0)
    sheet = bpy.context.object
    sheet.name = name
    for v in sheet.data.vertices:
        x, y = v.co.x, v.co.y
        v.co.x *= size[0]
        v.co.y *= size[1]
        v.co.z = bend * (x * x) + curl * max(0.0, x + y - 0.6) ** 2 * 2.0
    bpy.ops.object.shade_smooth()
    sheet.location = at
    sheet.rotation_euler = (0, 0, math.radians(turn_deg))
    sheet.data.materials.append(material)
    return sheet


def _image_material(name, path):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(path)
    bsdf = nt.nodes["Principled BSDF"]
    nt.links.new(tex.outputs[0], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.35
    return m


def _frame(at, size, turn_deg, picture):
    """A gilt frame with a picture in it, lying on the desk."""
    w, h = size
    border = 0.16
    gold = _material("Gilt", (0.78, 0.55, 0.22), 0.35, 0.9)
    parts = []
    for name, loc, dims in (("Top", (0, h / 2, 0), (w + border * 2, border, 0.08)), ("Bottom", (0, -h / 2, 0), (w + border * 2, border, 0.08)),
                            ("Left", (-w / 2, 0, 0), (border, h, 0.08)), ("Right", (w / 2, 0, 0), (border, h, 0.08))):
        bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
        bar = bpy.context.object
        bar.scale = dims
        bev = bar.modifiers.new("Bevel", "BEVEL")
        bev.width = 0.035
        bev.segments = 3
        bar.data.materials.append(gold)
        parts.append(bar)
    for cx, cy in ((-1, -1), (-1, 1), (1, -1), (1, 1)):
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.11, location=(cx * w / 2, cy * h / 2, 0.04))
        knob = bpy.context.object
        knob.data.materials.append(gold)
        parts.append(knob)
    bpy.ops.mesh.primitive_plane_add(size=1, location=(0, 0, 0.0))
    canvas = bpy.context.object
    canvas.scale = (w, h, 1)
    bpy.ops.object.transform_apply(scale=True)
    canvas.data.materials.append(_image_material("Picture", picture))
    parts.append(canvas)
    holder = bpy.data.objects.new("Frame", None)
    bpy.context.scene.collection.objects.link(holder)
    for p in parts:
        p.parent = holder
    holder.location = at
    holder.rotation_euler = (0, 0, math.radians(turn_deg))
    return holder


def _mesh(name, verts, faces, material):
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    obj.data.materials.append(material)
    return obj


def _surface(name, x0, x1, y0, y1, nx, ny, height, material):
    """A grid over a rectangle whose height at each point is `height(u, v)`, u and v in 0..1."""
    verts, faces = [], []
    for j in range(ny + 1):
        for i in range(nx + 1):
            u, v = i / nx, j / ny
            verts.append((x0 + (x1 - x0) * u, y0 + (y1 - y0) * v, height(u, v)))
    for j in range(ny):
        for i in range(nx):
            a = j * (nx + 1) + i
            faces.append((a, a + 1, a + nx + 2, a + nx + 1))
    obj = _mesh(name, verts, faces, material)
    uv = obj.data.uv_layers.new(name="UV")
    for p in obj.data.polygons:
        p.use_smooth = True
        for li in p.loop_indices:
            k = obj.data.loops[li].vertex_index
            uv.data[li].uv = ((k % (nx + 1)) / nx, (k // (nx + 1)) / ny)
    return obj


def _book(x0, x1, y0, y1):
    """The right half of an open book, as Wrath shows it: the page the menu is written on, its
    stack of leaves rising out of the gutter on the left, leather boards and brass corners under
    it, pennant bookmarks standing out of the fore-edge between the leaves, and two ribbons hanging
    off the foot."""
    leather = _material("Leather_Boards", (0.10, 0.035, 0.045), 0.55)
    brass = _material("Brass", (0.72, 0.50, 0.22), 0.32, 0.9)
    bpy.ops.mesh.primitive_cube_add(size=1, location=((x0 + x1) / 2 + 0.1, (y0 + y1) / 2, 0.05))
    boards = bpy.context.object
    boards.scale = (x1 - x0 + 0.65, y1 - y0 + 0.55, 0.10)
    bev = boards.modifiers.new("Bevel", "BEVEL")
    bev.width = 0.06
    bev.segments = 3
    boards.data.materials.append(leather)
    for cy in (y0 - 0.2, y1 + 0.2):
        bpy.ops.mesh.primitive_cube_add(size=1, location=(x1 + 0.3, cy, 0.11))
        corner = bpy.context.object
        corner.scale = (0.7, 0.7, 0.04)
        corner.rotation_euler = (0, 0, math.radians(45))
        corner.data.materials.append(brass)

    # The leaves: high and flat across the page, curving down into the gutter at the left, the
    # outer edge a little proud and the bottom corner lifted the way a thumbed page lifts.
    def leaf(u, v):
        gutter = 0.30 * (1.0 - math.exp(-u * 9.0))
        swell = 0.05 * math.sin(u * math.pi)
        corner = 0.22 * max(0.0, (u - 0.80) / 0.20) ** 2 * max(0.0, (0.18 - v) / 0.18) ** 2
        return 0.14 + gutter + swell + corner

    paper = _paper("Page_Paper", (0.86, 0.79, 0.66), 0.14)
    # The gutter in shadow: darker toward the spine, as the leaves fold down into it.
    nt = paper.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    base = bsdf.inputs["Base Color"].links[0].from_socket
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(nt.nodes.new("ShaderNodeTexCoord").outputs["UV"], sep.inputs[0])
    fold = nt.nodes.new("ShaderNodeValToRGB")
    fold.color_ramp.elements[0].color = (0.16, 0.11, 0.09, 1)
    fold.color_ramp.elements[1].position = 0.15
    nt.links.new(sep.outputs["X"], fold.inputs[0])
    shade = nt.nodes.new("ShaderNodeMix")
    shade.data_type = "RGBA"
    shade.blend_type = "MULTIPLY"
    shade.inputs[0].default_value = 1.0
    nt.links.new(base, shade.inputs[6])
    nt.links.new(fold.outputs[0], shade.inputs[7])
    nt.links.new(shade.outputs[2], bsdf.inputs["Base Color"])
    page = _surface("Page", x0, x1, y0, y1, 80, 80, leaf, paper)
    page.data.materials.append(_material("Page_Edges", (0.70, 0.62, 0.50), 0.9))
    solid = page.modifiers.new("Leaves", "SOLIDIFY")
    solid.thickness = 0.16
    solid.offset = -1
    solid.material_offset_rim = 1
    # The page under it, a sliver showing past the top edge.
    _surface("Page_Under", x0 + 0.05, x1 + 0.06, y0 - 0.06, y1 + 0.04, 8, 8, lambda u, v: 0.10 + 0.25 * (1 - math.exp(-u * 9)), _material("Page_Under", (0.72, 0.64, 0.50), 0.9))

    # Pennant bookmarks with a notched tail, standing out of the fore-edge on the right, each one
    # tucked in at a different depth of the leaves. They first came out of the spine side, where
    # no bookmark can: that edge is bound shut. The owner spotted it.
    colours = [(0.50, 0.10, 0.09), (0.74, 0.62, 0.44), (0.30, 0.18, 0.46), (0.70, 0.60, 0.42), (0.36, 0.22, 0.52), (0.55, 0.14, 0.11)]
    for i, colour in enumerate(colours):
        y = y1 - 1.6 - i * (y1 - y0 - 3.0) / (len(colours) - 1)
        length, width, notch = 1.25, 0.42, 0.20
        verts = [(0, -width / 2, 0), (length, -width / 2, 0), (length - notch, 0, 0), (length, width / 2, 0), (0, width / 2, 0)]
        flag = _mesh(f"Pennant_{i}", verts, [(0, 1, 2, 4), (2, 3, 4)], _material(f"Pennant_{i}", colour, 0.6))
        sol = flag.modifiers.new("Cloth", "SOLIDIFY")
        sol.thickness = 0.02
        flag.location = (x1 - 0.55, y, 0.30 + 0.022 * i)
        flag.rotation_euler = (0, math.radians(3), math.radians(-6 + 3 * i))
    for i, (dx, colour) in enumerate(((0.9, (0.30, 0.18, 0.46)), (1.35, (0.50, 0.10, 0.09)))):
        bpy.ops.mesh.primitive_plane_add(size=1, location=(x0 + dx, y0 - 0.35, 0.22))
        ribbon = bpy.context.object
        ribbon.scale = (0.22, 1.2, 1)
        ribbon.rotation_euler = (math.radians(-8), 0, math.radians(6 - 10 * i))
        ribbon.data.materials.append(_material(f"Ribbon_{i}", colour, 0.5))


def _candle(at):
    """A fat candle burned low on a footed brass dish, its top a pool of melt with a rim of
    drips: seen from above, the pool and the rim are what read."""
    x, y = at
    brass = _material("Candle_Brass", (0.70, 0.48, 0.20), 0.3, 0.9)
    bpy.ops.mesh.primitive_cylinder_add(radius=1.25, depth=0.16, location=(x, y, 0.08), vertices=64)
    dish = bpy.context.object
    bev = dish.modifiers.new("Bevel", "BEVEL")
    bev.width = 0.05
    bev.segments = 3
    dish.data.materials.append(brass)
    bpy.ops.mesh.primitive_torus_add(major_radius=1.22, minor_radius=0.07, location=(x, y, 0.18))
    bpy.context.object.data.materials.append(brass)

    wax = _material("Wax", (0.93, 0.86, 0.70), 0.4)
    sss = wax.node_tree.nodes["Principled BSDF"]
    sss.inputs["Subsurface Weight"].default_value = 0.35
    sss.inputs["Subsurface Radius"].default_value = (0.4, 0.2, 0.1)

    bpy.ops.mesh.primitive_cylinder_add(radius=0.78, depth=1.1, location=(x, y, 0.75), vertices=48)
    body = bpy.context.object
    # Drips down the sides; the pool of melt on top is a disc of its own.
    tex = bpy.data.textures.new("Drips", "CLOUDS")
    tex.noise_scale = 0.18
    disp = body.modifiers.new("Drips", "DISPLACE")
    disp.texture = tex
    disp.strength = 0.08
    sub = body.modifiers.new("Smooth", "SUBSURF")
    sub.levels = 2
    body.modifiers.move(1, 0)
    body.data.materials.append(wax)
    bpy.ops.object.shade_smooth()
    bpy.ops.mesh.primitive_cylinder_add(radius=0.55, depth=0.06, location=(x, y, 1.29), vertices=48)
    pool = bpy.context.object
    pool.data.materials.append(_material("Melt", (0.95, 0.80, 0.55), 0.08, 0.0, (1.0, 0.55, 0.18), 0.4))
    for k in range(7):
        a = k * 2.0 * math.pi / 7 + 0.4
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.09 + 0.03 * (k % 3), location=(x + 0.76 * math.cos(a), y + 0.76 * math.sin(a), 0.85 - 0.15 * (k % 2)))
        drip = bpy.context.object
        drip.scale = (0.8, 0.8, 3.0)
        drip.data.materials.append(wax)
        bpy.ops.object.shade_smooth()
    bpy.ops.mesh.primitive_cylinder_add(radius=0.025, depth=0.25, location=(x, y, 1.40))
    bpy.context.object.data.materials.append(_material("Wick", (0.05, 0.04, 0.03), 0.9))
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.11, location=(x, y, 1.62))
    flame = bpy.context.object
    flame.scale = (0.8, 0.8, 2.4)
    flame.data.materials.append(_material("Flame", (1.0, 0.7, 0.3), 0.5, 0.0, (1.0, 0.62, 0.22), 80.0))
    light("POINT", (x, y, 2.2), (0, 0, 0), 900, (1.0, 0.58, 0.26), 0.25)


def _quill(at, turn_deg, length=4.4):
    """A long feather: a pale shaft, a vane on each side that is widest a third of the way from
    the tip and frays into barbs, and a dark metal nib."""
    vane = bpy.data.materials.new("Quill_Vane")
    vane.use_nodes = True
    nt = vane.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    co = nt.nodes.new("ShaderNodeTexCoord").outputs["UV"]
    barbs = nt.nodes.new("ShaderNodeTexWave")
    barbs.wave_type = "BANDS"
    barbs.inputs["Scale"].default_value = 60
    barbs.inputs["Distortion"].default_value = 2
    nt.links.new(co, barbs.inputs[0])
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(co, sep.inputs[0])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (0.85, 0.82, 0.76, 1)
    ramp.color_ramp.elements[1].color = (0.10, 0.08, 0.10, 1)
    ramp.color_ramp.elements[0].position = 0.25
    nt.links.new(sep.outputs["X"], ramp.inputs[0])
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.blend_type = "MULTIPLY"
    mix.inputs[0].default_value = 0.35
    nt.links.new(ramp.outputs[0], mix.inputs[6])
    nt.links.new(barbs.outputs[0], mix.inputs[7])
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.6
    bsdf.inputs["Sheen Weight"].default_value = 0.4

    verts, faces, uvs = [], [], []
    n = 40
    start = 0.18
    for i in range(n + 1):
        t = start + (1 - start) * i / n
        x = -length / 2 + length * t
        bow = 0.10 * math.sin(t * math.pi)
        w = 0.42 * math.sin(math.pi * min(1.0, (t - start) / (1 - start))) ** 0.7 * (1.0 - 0.35 * t)
        for side in (-1, 1):
            ragged = 0.015 * math.sin(t * 91.0 + side) if t > 0.3 else 0.0
            verts.append((x, bow + side * (w * (0.75 if side > 0 else 1.0) + ragged), 0.02 + 0.06 * t))
        verts.append((x, bow, 0.04 + 0.06 * t))
    for i in range(n):
        a, b = i * 3, (i + 1) * 3
        faces.append((a, a + 2, b + 2, b))
        faces.append((a + 2, a + 1, b + 1, b + 2))
    feather = _mesh("Quill_Feather", verts, faces, vane)
    feather.data.uv_layers.new(name="UV")
    for poly in feather.data.polygons:
        poly.use_smooth = True
        for li in poly.loop_indices:
            vi = feather.data.loops[li].vertex_index
            vx, vy, _ = verts[vi]
            feather.data.uv_layers[0].data[li].uv = ((vx + length / 2) / length, vy + 0.5)
    bpy.ops.mesh.primitive_cylinder_add(radius=0.035, depth=length, location=(0, 0, 0.05))
    shaft = bpy.context.object
    shaft.rotation_euler = (0, math.radians(90), 0)
    shaft.data.materials.append(_material("Quill_Shaft", (0.88, 0.85, 0.78), 0.45))
    bpy.ops.mesh.primitive_cone_add(radius1=0.06, radius2=0.0, depth=0.42, location=(-length / 2 - 0.2, 0, 0.05))
    nib = bpy.context.object
    nib.rotation_euler = (0, math.radians(-90), 0)
    nib.data.materials.append(_material("Nib", (0.25, 0.22, 0.20), 0.25, 1.0))
    holder = bpy.data.objects.new("Quill", None)
    bpy.context.scene.collection.objects.link(holder)
    for p in (feather, shaft, nib):
        p.parent = holder
    holder.location = (*at, 0.25)
    holder.rotation_euler = (0, math.radians(-2), math.radians(turn_deg))


# The desk is shot square from above with an orthographic camera, 100 pixels to the unit at
# 1920x1080, so a point (x, y) on the desk lands on pixel (960 + 100x, 540 - 100y). Menu.cs
# writes onto the page and the note by those numbers; move them together.
DESK_PAGE = (2.1, 8.4, -4.9, 5.0)     # x0 x1 y0 y1: pixels 1170..1800 across, 40..1030 down
DESK_NOTE = (-5.4, -2.75, 6.6, 4.5, 3)  # centre, size, turn: the note the title is written on


def scene_desk():
    """The front door, after Wrath's: a desk by candlelight seen from above — the right page of
    an open book that the menu is written on, a framed picture of the game, a candle, a note the
    title is written on, and a quill."""
    sky((0.02, 0.015, 0.03), (0.02, 0.015, 0.03), 1.0)
    bpy.ops.mesh.primitive_plane_add(size=60)
    desk = bpy.context.object
    m = bpy.data.materials.new("Desk")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    co = nt.nodes.new("ShaderNodeTexCoord").outputs["Object"]
    # Planks along x: grain is noise stretched along the plank, seams are a band every 2.4 units.
    stretch = nt.nodes.new("ShaderNodeMapping")
    stretch.inputs[3].default_value = (0.06, 1.4, 1.0)
    nt.links.new(co, stretch.inputs[0])
    grain = nt.nodes.new("ShaderNodeTexNoise")
    grain.inputs["Scale"].default_value = 3.0
    grain.inputs["Detail"].default_value = 12
    grain.inputs["Distortion"].default_value = 2.0
    nt.links.new(stretch.outputs[0], grain.inputs[0])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (0.045, 0.025, 0.03, 1)
    ramp.color_ramp.elements[1].color = (0.17, 0.09, 0.07, 1)
    nt.links.new(grain.outputs[0], ramp.inputs[0])
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(co, sep.inputs[0])
    seam = nt.nodes.new("ShaderNodeMath")
    seam.operation = "PINGPONG"
    seam.inputs[1].default_value = 1.2
    nt.links.new(sep.outputs["Y"], seam.inputs[0])
    edge = nt.nodes.new("ShaderNodeMath")
    edge.operation = "LESS_THAN"
    edge.inputs[1].default_value = 0.025
    nt.links.new(seam.outputs[0], edge.inputs[0])
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    nt.links.new(edge.outputs[0], mix.inputs[0])
    nt.links.new(ramp.outputs[0], mix.inputs[6])
    mix.inputs[7].default_value = (0.01, 0.006, 0.006, 1)
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.5
    desk.data.materials.append(m)

    _book(*DESK_PAGE)
    picture = os.environ.get("KEYART_PICTURE", "")
    if picture and os.path.exists(picture):
        _frame((-5.0, 2.5, 0.06), (5.8, 3.3), 7, picture)
    _candle((0.25, 3.7))
    (nx, ny), (nw, nh), nturn = DESK_NOTE[:2], DESK_NOTE[2:4], DESK_NOTE[4]
    _sheet("Note_B", (4.6, 3.8), (-0.6, -3.6, 0.03), -14, _paper("Note_B_Paper", (0.80, 0.72, 0.58), 0.25), 0.15, 0.01)
    _sheet("Note_A", (nw, nh), (nx, ny, 0.06), nturn, _paper("Note_A_Paper", (0.86, 0.79, 0.66), 0.08), 0.35, 0.006)
    _quill((0.2, -0.9), 58)

    # Light from the sides, low, so the bow of the page and the curl of the notes show from
    # straight above: cool from the lower left, warm over the book from the right.
    light("AREA", (-11.0, -7.0, 6.0), (-3.0, -1.0, 0), 900, (0.62, 0.55, 0.95), 8.0)
    light("AREA", (15.0, 1.0, 5.0), (6.0, 0.0, 0), 1100, (1.0, 0.86, 0.68), 6.0)
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    bpy.context.scene.collection.objects.link(cam)
    cam.location = (0, 0, 30)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 19.2
    cam.data.clip_end = 100
    bpy.context.scene.camera = cam


def scene_parchment():
    """A flat sheet of old paper, square on, evenly lit: the surface the interface's pages are
    printed on. Faint scrawl only — anything darker would fight the text laid over it."""
    sky((0.5, 0.5, 0.5), (0.5, 0.5, 0.5), 1.0)
    bpy.ops.mesh.primitive_grid_add(x_subdivisions=2, y_subdivisions=2, size=2.0)
    sheet = bpy.context.object
    sheet.data.materials.append(_paper("Parchment", (0.88, 0.83, 0.72), 0.06))
    light("AREA", (0, 0, 6), (0, 0, 0), 300, (1.0, 0.97, 0.92), 8.0)
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    bpy.context.scene.collection.objects.link(cam)
    cam.location = (0, 0, 5)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 2.0
    bpy.context.scene.camera = cam


SCENES = {"title": scene_title, "the-long-road": scene_long_road, "caves-of-shadow": scene_caves, "desk": scene_desk, "parchment": scene_parchment}


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    which, out = args[0], args[1]
    width, height = (int(args[2]), int(args[3])) if len(args) > 3 else (1920, 1080)
    clear()
    SCENES[which]()
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 96
    scene.cycles.use_denoising = True
    scene.render.resolution_x = width
    scene.render.resolution_y = height
    scene.view_settings.view_transform = "Standard" if which == "parchment" else "AgX"
    if which != "parchment":
        scene.view_settings.look = "AgX - Punchy"
    for rig in [o for o in bpy.data.objects if o.type == "ARMATURE" and "frame" in o]:
        pass
    scene.render.filepath = out
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    main()

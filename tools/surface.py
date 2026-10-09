"""Surfaces: what a sculpted asset has that a scripted one did not.

The generators build shape well enough — a stooped goblin, a wolf on its toes — and then paint
every part one flat colour, and a flat colour is what says "toy" at any distance. Real skin has
pores and a blotch; leather has grain and goes pale where it is rubbed; iron is pitted and
rusts in its hollows; wood has a figure. This module gives each of the generators' materials a
procedural version of that, and then *bakes* it — colour, roughness, metallic and a normal map
— into one texture atlas per group of parts, so the exported GLB carries ordinary PBR textures
and the game's importer has nothing clever to do.

    import surface
    surface.finish([("hide", [body, *ears], 1024, False), ("kit", kit, 1024, True)])

Each group is (name, objects, texture size, bake occlusion). Occlusion is baked into the colour
of kit — a strap grimes the chest under it — and left off the hide, whose vertex colours were
painted with their own occlusion already.

Every recipe reads the model's *world position* for its noise, not UVs, so the texture has no
seams, no stretching, and the same grain on every plank however each one was unwrapped. The
UVs exist only to receive the bake. SCALE says how big a model unit is next to a goblin's, so
the weapons — modelled at human size — get the same pore spacing as a hand.
"""
import contextlib
import os
import tempfile

import bpy
import numpy as np

SCALE = 1.0

# Node sockets, by index, for the nodes whose names repeat. Blender's Mix node has a Factor,
# an A and a B per data type, so by name they are ambiguous.
MIX_FAC, MIX_A, MIX_B, MIX_OUT = 0, 6, 7, 2
BUMP_STRENGTH, BUMP_DISTANCE, BUMP_HEIGHT, BUMP_NORMAL = 0, 1, 3, 4


# --- node helpers --------------------------------------------------------------------------

def _tree(name):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.name = "BSDF"
    nt.links.new(bsdf.outputs[0], out.inputs[0])
    return m, nt, bsdf


def _plug(nt, socket, value):
    """Connect a socket to another, or set it to a constant: recipes do not care which."""
    if isinstance(value, bpy.types.NodeSocket):
        nt.links.new(value, socket)
    else:
        socket.default_value = value


def _coords(nt):
    return nt.nodes.new("ShaderNodeNewGeometry").outputs["Position"]


def _stretch(nt, co, scale):
    """Coordinates squashed along an axis, so a noise sampled through them runs in streaks."""
    m = nt.nodes.new("ShaderNodeMapping")
    nt.links.new(co, m.inputs[0])
    m.inputs[3].default_value = scale
    return m.outputs[0]


def _noise(nt, co, scale, detail=2.0, rough=0.5, distortion=0.0):
    n = nt.nodes.new("ShaderNodeTexNoise")
    nt.links.new(co, n.inputs[0])
    n.inputs["Scale"].default_value = scale / SCALE
    n.inputs["Detail"].default_value = detail
    n.inputs["Roughness"].default_value = rough
    n.inputs["Distortion"].default_value = distortion
    return n.outputs[0]


def _voronoi(nt, co, scale, feature="DISTANCE_TO_EDGE", randomness=1.0):
    n = nt.nodes.new("ShaderNodeTexVoronoi")
    n.feature = feature
    nt.links.new(co, n.inputs[0])
    n.inputs["Scale"].default_value = scale / SCALE
    n.inputs["Randomness"].default_value = randomness
    return n.outputs[0]


def _wave(nt, co, scale, distortion=0.0, detail=2.0, direction="X"):
    n = nt.nodes.new("ShaderNodeTexWave")
    n.wave_type = "BANDS"
    n.bands_direction = direction
    nt.links.new(co, n.inputs[0])
    n.inputs["Scale"].default_value = scale / SCALE
    n.inputs["Distortion"].default_value = distortion
    n.inputs["Detail"].default_value = detail
    return n.outputs[1]


def _ramp(nt, fac, stops, interpolation="LINEAR"):
    """stops: [(position, value)], where a value is a float or an (r, g, b)."""
    n = nt.nodes.new("ShaderNodeValToRGB")
    n.color_ramp.interpolation = interpolation
    nt.links.new(fac, n.inputs[0])
    elements = n.color_ramp.elements
    while len(elements) < len(stops):
        elements.new(0.5)
    for element, (pos, value) in zip(elements, stops):
        element.position = pos
        if isinstance(value, (int, float)):
            value = (value, value, value)
        element.color = (*value, 1.0)
    return n.outputs[0]


def _math(nt, op, a, b=0.0, clamp=False):
    n = nt.nodes.new("ShaderNodeMath")
    n.operation = op
    n.use_clamp = clamp
    _plug(nt, n.inputs[0], a)
    _plug(nt, n.inputs[1], b)
    return n.outputs[0]


def _span(nt, fac, lo, hi):
    """fac in 0..1 mapped onto lo..hi."""
    return _math(nt, "ADD", _math(nt, "MULTIPLY", fac, hi - lo), lo)


def _mix(nt, a, b, fac, blend="MIX"):
    n = nt.nodes.new("ShaderNodeMix")
    n.data_type = "RGBA"
    n.blend_type = blend
    n.clamp_factor = True
    _plug(nt, n.inputs[MIX_FAC], fac)
    _plug(nt, n.inputs[MIX_A], _rgba(a))
    _plug(nt, n.inputs[MIX_B], _rgba(b))
    return n.outputs[MIX_OUT]


def _rgba(value):
    if isinstance(value, bpy.types.NodeSocket):
        return value
    if isinstance(value, (int, float)):
        return (value, value, value, 1.0)
    return (*value[:3], 1.0)


def _shade(nt, colour, fac):
    """Multiply a colour by a 0..1 factor, grey."""
    return _mix(nt, colour, fac, 1.0, "MULTIPLY")


def _bump(nt, height, strength, distance, previous=None):
    n = nt.nodes.new("ShaderNodeBump")
    n.inputs[BUMP_STRENGTH].default_value = strength
    n.inputs[BUMP_DISTANCE].default_value = distance / SCALE
    nt.links.new(height, n.inputs[BUMP_HEIGHT])
    if previous is not None:
        nt.links.new(previous, n.inputs[BUMP_NORMAL])
    return n.outputs[0]


def _edges(nt, gain=6.0):
    """Where the mesh is convex, 0..1: the corners that get rubbed and the rims that catch light.

    Cycles' pointiness is 0.5 on a flat face and climbs over a ridge. It is a weak signal on a
    low-poly mesh, so it is amplified hard and clamped.
    """
    p = nt.nodes.new("ShaderNodeNewGeometry").outputs["Pointiness"]
    return _math(nt, "MULTIPLY", _math(nt, "SUBTRACT", p, 0.5), gain, clamp=True)


def _attribute(nt, name):
    n = nt.nodes.new("ShaderNodeAttribute")
    n.attribute_name = name
    return n.outputs[0]


def _finish(nt, bsdf, colour, rough, normal=None, metallic=0.0):
    _plug(nt, bsdf.inputs["Base Color"], _rgba(colour))
    _plug(nt, bsdf.inputs["Roughness"], rough)
    _plug(nt, bsdf.inputs["Metallic"], metallic)
    if normal is not None:
        nt.links.new(normal, bsdf.inputs["Normal"])


# --- recipes --------------------------------------------------------------------------------
# Each takes the colour of the flat material it replaces and returns a material.

def skin(colour=None):
    """Hide: a blotch, pores, a fine web of creases and a scatter of warts. colour=None reads
    the painted vertex colours, which already carry the toning and the occlusion."""
    m, nt, bsdf = _tree("Surface_Skin")
    co = _coords(nt)
    base = _attribute(nt, "Col") if colour is None else colour

    blotch = _span(nt, _ramp(nt, _noise(nt, co, 6, 3), [(0.35, 0.0), (0.65, 1.0)]), 0.78, 1.18)
    tone = _shade(nt, base, blotch)
    # Creases: fine, and only a little darker in their floor. At the size of scales they made
    # him a lizard.
    creases = _ramp(nt, _voronoi(nt, co, 85), [(0.0, 0.0), (0.05, 1.0)])
    tone = _shade(nt, tone, _span(nt, creases, 0.90, 1.0))
    warts = _ramp(nt, _voronoi(nt, co, 14, "F1", 1.0), [(0.0, 1.0), (0.07, 0.0)], "EASE")
    tone = _shade(nt, tone, _span(nt, warts, 1.0, 0.82))

    rough = _span(nt, _noise(nt, co, 34, 2), 0.50, 0.80)
    n = _bump(nt, _noise(nt, co, 4, 2), 0.35, 0.03)                       # soft lumps of flesh
    n = _bump(nt, _noise(nt, _stretch(nt, co, (1.0, 1.0, 0.3)), 30, 2), 0.2, 0.01, n)   # wrinkles
    n = _bump(nt, creases, 0.18, 0.005, n)
    n = _bump(nt, warts, 0.5, 0.008, n)
    n = _bump(nt, _noise(nt, co, 160, 3), 0.12, 0.004, n)                 # pores
    _finish(nt, bsdf, tone, rough, n)
    return m


def fur(colour=None):
    """A pelt: streaked down the body, clumped, dull. colour=None reads the vertex colours."""
    m, nt, bsdf = _tree("Surface_Fur")
    co = _coords(nt)
    base = _attribute(nt, "Col") if colour is None else colour

    streak = _noise(nt, _stretch(nt, co, (1.0, 1.0, 0.07)), 28, 3, 0.6)
    clump = _noise(nt, co, 9, 3)
    tone = _shade(nt, base, _span(nt, _ramp(nt, streak, [(0.3, 0.0), (0.7, 1.0)]), 0.72, 1.30))
    tone = _shade(nt, tone, _span(nt, clump, 0.80, 1.10))
    rough = _span(nt, streak, 0.80, 0.96)
    n = _bump(nt, streak, 0.70, 0.012)
    n = _bump(nt, clump, 0.35, 0.03, n)
    _finish(nt, bsdf, tone, rough, n)
    return m


def leather(colour):
    m, nt, bsdf = _tree("Surface_Leather")
    co = _coords(nt)
    worn = _edges(nt)
    pale = tuple(min(1.0, c * 1.9 + 0.08) for c in colour)

    grain = _noise(nt, co, 14, 4, 0.55)
    cracks = _ramp(nt, _voronoi(nt, co, 48), [(0.0, 0.62), (0.07, 1.0)])
    tone = _shade(nt, colour, _span(nt, grain, 0.72, 1.18))
    tone = _shade(nt, tone, cracks)
    tone = _mix(nt, tone, pale, _math(nt, "MULTIPLY", worn, 0.6))     # rubbed pale at the edges

    rough = _span(nt, _noise(nt, co, 22, 2), 0.62, 0.92)
    n = _bump(nt, cracks, 0.30, 0.008)
    n = _bump(nt, _noise(nt, co, 95, 3), 0.12, 0.004, n)
    n = _bump(nt, _noise(nt, co, 30, 2), 0.18, 0.012, n)
    _finish(nt, bsdf, tone, rough, n)
    return m


def cloth(colour):
    m, nt, bsdf = _tree("Surface_Cloth")
    co = _coords(nt)
    weave = _math(nt, "MULTIPLY", _wave(nt, co, 230, 0.3, 1, "X"), _wave(nt, co, 230, 0.3, 1, "Z"))
    dirt = _noise(nt, co, 6, 3)
    tone = _shade(nt, colour, _span(nt, weave, 0.84, 1.06))
    tone = _shade(nt, tone, _span(nt, dirt, 0.78, 1.08))
    tone = _mix(nt, tone, tuple(c * 1.5 + 0.1 for c in colour), _math(nt, "MULTIPLY", _edges(nt), 0.4))
    rough = _span(nt, dirt, 0.88, 1.0)
    n = _bump(nt, weave, 0.35, 0.004)
    n = _bump(nt, _noise(nt, co, 40, 2), 0.15, 0.01, n)
    _finish(nt, bsdf, tone, rough, n)
    return m


def iron(colour, rust=0.5):
    """Forged iron: hammer-dented, pitted, rusting in its hollows and ground bright on its
    edges. `rust` is how much of it has gone."""
    m, nt, bsdf = _tree("Surface_Iron")
    co = _coords(nt)
    worn = _edges(nt)
    rusty = (0.30, 0.13, 0.05)
    bright = (0.62, 0.61, 0.60)

    mask = _ramp(nt, _noise(nt, co, 8, 4, 0.6), [(0.62 - 0.30 * rust, 0.0), (0.72 - 0.30 * rust, 1.0)])
    mask = _math(nt, "MULTIPLY", mask, _math(nt, "SUBTRACT", 1.0, worn), clamp=True)   # edges stay bare
    dents = _voronoi(nt, co, 16, "F1")
    scratches = _wave(nt, co, 70, 2.5, 3, "Z")
    metal = _shade(nt, colour, _span(nt, _noise(nt, co, 40, 3), 0.78, 1.1))
    metal = _mix(nt, metal, bright, _math(nt, "MULTIPLY", worn, 0.8))
    rusted = _shade(nt, rusty, _span(nt, _noise(nt, co, 30, 3), 0.6, 1.3))
    tone = _mix(nt, metal, rusted, mask)

    metallic = _mix(nt, 0.35, 0.0, mask)
    rough = _mix(nt, _span(nt, _noise(nt, co, 50, 2), 0.38, 0.58), 0.88, mask)
    n = _bump(nt, dents, 0.45, 0.02)
    n = _bump(nt, _math(nt, "MULTIPLY", _noise(nt, co, 90, 3), mask), 0.45, 0.008, n)
    n = _bump(nt, scratches, 0.08, 0.003, n)
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(metallic, sep.inputs[0])
    sep_r = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(rough, sep_r.inputs[0])
    _finish(nt, bsdf, tone, sep_r.outputs[0], n, sep.outputs[0])
    return m


def steel(colour, polish=0.45):
    """Ground and polished: brushed along its length, a few nicks, barely coloured."""
    m, nt, bsdf = _tree("Surface_Steel")
    co = _coords(nt)
    brush = _wave(nt, co, 160, 1.2, 2, "Z")
    # Nicks are rare. A web of them over the whole blade was a crackle glaze.
    sparse = _ramp(nt, _noise(nt, co, 9, 2), [(0.58, 0.0), (0.68, 1.0)])
    nicks = _math(nt, "MULTIPLY", _ramp(nt, _voronoi(nt, co, 30), [(0.0, 1.0), (0.04, 0.0)]), sparse)
    tone = _shade(nt, colour, _span(nt, _noise(nt, co, 20, 3), 0.88, 1.06))
    tone = _shade(nt, tone, _span(nt, nicks, 1.0, 0.7))
    tone = _mix(nt, tone, tuple(min(1.0, c * 1.25) for c in colour), _math(nt, "MULTIPLY", _edges(nt), 0.7))
    rough = _span(nt, _noise(nt, co, 45, 2), 0.42 - polish * 0.35, 0.62 - polish * 0.35)
    rough = _math(nt, "ADD", rough, _math(nt, "MULTIPLY", nicks, 0.3))
    n = _bump(nt, brush, 0.06, 0.002)
    n = _bump(nt, nicks, 0.5, 0.006, n)
    n = _bump(nt, _noise(nt, co, 14, 2), 0.12, 0.01, n)                    # a hammered waviness
    _finish(nt, bsdf, tone, rough, n, 0.45)
    return m


def chain(colour):
    """Mail: a mesh of rings, dark where they overlap."""
    m, nt, bsdf = _tree("Surface_Chain")
    co = _coords(nt)
    rings = _ramp(nt, _voronoi(nt, co, 64, "DISTANCE_TO_EDGE"), [(0.0, 0.0), (0.18, 1.0)])
    tone = _shade(nt, colour, _span(nt, rings, 0.45, 1.05))
    tone = _shade(nt, tone, _span(nt, _noise(nt, co, 12, 3), 0.8, 1.1))
    tone = _mix(nt, tone, (0.28, 0.14, 0.06), _ramp(nt, _noise(nt, co, 7, 3), [(0.55, 0.0), (0.7, 0.5)]))
    rough = _span(nt, rings, 0.75, 0.55)
    n = _bump(nt, rings, 0.7, 0.01)
    _finish(nt, bsdf, tone, rough, n, 0.3)
    return m


def wood(colour, dark=None):
    """Grain along Z: hafts, limbs and planks are all built standing up."""
    m, nt, bsdf = _tree("Surface_Wood")
    co = _coords(nt)
    dark = dark or tuple(c * 0.45 for c in colour)
    figure = _noise(nt, _stretch(nt, co, (1.0, 1.0, 0.045)), 22, 4, 0.55, 0.4)
    bands = _ramp(nt, figure, [(0.3, 0.0), (0.45, 0.6), (0.55, 0.2), (0.7, 1.0)])
    tone = _mix(nt, dark, colour, bands)
    tone = _shade(nt, tone, _span(nt, _noise(nt, co, 5, 2), 0.78, 1.1))
    tone = _mix(nt, tone, tuple(min(1.0, c * 1.4 + 0.05) for c in colour), _math(nt, "MULTIPLY", _edges(nt), 0.5))
    rough = _span(nt, bands, 0.85, 0.65)
    n = _bump(nt, bands, 0.35, 0.008)
    n = _bump(nt, _noise(nt, _stretch(nt, co, (1.0, 1.0, 0.02)), 80, 2), 0.2, 0.003, n)
    _finish(nt, bsdf, tone, rough, n)
    return m


def bone(colour):
    m, nt, bsdf = _tree("Surface_Bone")
    co = _coords(nt)
    tone = _shade(nt, colour, _span(nt, _noise(nt, _stretch(nt, co, (1.0, 1.0, 0.2)), 30, 3), 0.84, 1.06))
    tone = _shade(nt, tone, _span(nt, _noise(nt, co, 8, 2), 0.82, 1.0))
    tone = _mix(nt, tone, (0.36, 0.27, 0.14), _ramp(nt, _noise(nt, co, 12, 3), [(0.55, 0.0), (0.75, 0.6)]))  # stained at the root
    n = _bump(nt, _noise(nt, _stretch(nt, co, (1.0, 1.0, 0.2)), 60, 2), 0.15, 0.004)
    _finish(nt, bsdf, tone, 0.5, n)
    return m


def gold(colour):
    m, nt, bsdf = _tree("Surface_Gold")
    co = _coords(nt)
    tarnish = _ramp(nt, _voronoi(nt, co, 70), [(0.0, 0.0), (0.06, 1.0)])
    tone = _shade(nt, colour, _span(nt, tarnish, 0.78, 1.0))
    tone = _shade(nt, tone, _span(nt, _ramp(nt, _noise(nt, co, 10, 3), [(0.45, 0.0), (0.7, 1.0)]), 0.72, 1.0))   # dull in patches
    tone = _shade(nt, tone, _span(nt, _noise(nt, co, 25, 2), 0.85, 1.08))
    n = _bump(nt, _voronoi(nt, co, 20, "F1"), 0.3, 0.01)
    _finish(nt, bsdf, tone, _span(nt, tarnish, 0.55, 0.32), n, 0.45)
    return m


def wet(colour, rough=0.3):
    """Eyes, noses, the inside of a mouth: smooth and flat, given a little life by a bump."""
    m, nt, bsdf = _tree("Surface_Wet")
    co = _coords(nt)
    tone = _shade(nt, colour, _span(nt, _noise(nt, co, 40, 2), 0.85, 1.05))
    _finish(nt, bsdf, tone, rough, _bump(nt, _noise(nt, co, 120, 2), 0.1, 0.003))
    return m


def flat(colour, roughness=0.7, metallic=0.0):
    m, nt, bsdf = _tree("Surface_Flat")
    _finish(nt, bsdf, colour, roughness, None, metallic)
    return m


# Which recipe each of the generators' flat materials gets, by its name. A material that is not
# here is kept as it was, which is the right answer for anything genuinely plain.
RECIPES = {
    "Goblin_Hide": lambda c, m, r: skin(None),
    "Goblin_Skin": lambda c, m, r: skin(c),
    "Goblin_Skin_Dark": lambda c, m, r: skin(c),
    "Leather": lambda c, m, r: leather(c),
    "Leather_Light": lambda c, m, r: leather(c),
    "Cloth": lambda c, m, r: cloth(c),
    "Cloth_Red": lambda c, m, r: cloth(c),
    "Fletching": lambda c, m, r: cloth(c),
    "Bow_String": lambda c, m, r: cloth(c),
    "Worn_Metal": lambda c, m, r: iron(c, 0.22),
    "Rusty_Iron": lambda c, m, r: iron(c, 0.60),
    "Cold_Iron": lambda c, m, r: iron(c, 0.12),
    "Ground_Edge": lambda c, m, r: steel(c, 0.35),
    "Sword_Steel": lambda c, m, r: steel(c, 0.5),
    "Silvered_Steel": lambda c, m, r: steel(c, 0.85),
    "Chain_Shirt": lambda c, m, r: chain(c),
    "Shield_Wood": lambda c, m, r: wood(c),
    "Bow_Wood": lambda c, m, r: wood(c),
    "Ash_Wood": lambda c, m, r: wood(c),
    "Plank_Seam": lambda c, m, r: wood(c, (0.03, 0.015, 0.005)),
    "Gold": lambda c, m, r: gold(c),
    "Goblin_Teeth": lambda c, m, r: bone(c),
    "Wolf_Fang": lambda c, m, r: bone(c),
    "Claw": lambda c, m, r: bone(c),
    "Wolf_Talon": lambda c, m, r: bone(c),
    "Hair": lambda c, m, r: fur(c),
    "Wolf_Fur": lambda c, m, r: fur(c),
    "Goblin_Eye": lambda c, m, r: wet(c, 0.25),
    "Wolf_Eye": lambda c, m, r: wet(c, 0.25),
    "Goblin_Pupil": lambda c, m, r: wet(c, 0.2),
    "Wolf_Nose": lambda c, m, r: wet(c, 0.35),
    "Wolf_Maw": lambda c, m, r: wet(c, 0.3),
}


# --- the bake -------------------------------------------------------------------------------

def _procedural(old, cache):
    if old.name in cache:
        return cache[old.name]
    bsdf = old.node_tree.nodes.get("Principled BSDF") if old.node_tree else None
    if bsdf is None or old.name not in RECIPES:
        cache[old.name] = old
        return old
    colour = tuple(bsdf.inputs["Base Color"].default_value[:3])
    new = RECIPES[old.name](colour, bsdf.inputs["Metallic"].default_value, bsdf.inputs["Roughness"].default_value)
    new.name = f"Proc_{old.name}"
    cache[old.name] = new
    return new


def _select(objects):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects:
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]


def _apply_modifiers(objects):
    """Bevels and the like become geometry now, so what is unwrapped is what is baked and
    exported. The armature stays: it is the one modifier that is not shape."""
    for o in objects:
        for mod in list(o.modifiers):
            if mod.type == "ARMATURE":
                continue
            _select([o])
            bpy.ops.object.modifier_apply(modifier=mod.name)


def _unwrap(objects, size):
    _select(objects)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.002, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.pack_islands(rotate=True, margin=6.0 / size)
    bpy.ops.object.mode_set(mode="OBJECT")


def _materials_of(objects):
    seen = {}
    for o in objects:
        for m in o.data.materials:
            if m is not None:
                seen[m.name] = m
    return list(seen.values())


def _target(materials, image):
    for m in materials:
        nt = m.node_tree
        n = nt.nodes.get("BakeTarget") or nt.nodes.new("ShaderNodeTexImage")
        n.name = "BakeTarget"
        n.image = image
        nt.nodes.active = n


@contextlib.contextmanager
def _emitting(materials, socket):
    """Temporarily route one Principled input straight to the output as emission, so a bake
    of type EMIT reads it back: that is how roughness and metallic get into a texture."""
    restore = []
    for m in materials:
        nt = m.node_tree
        bsdf = nt.nodes.get("BSDF") or nt.nodes.get("Principled BSDF")
        out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
        emit = nt.nodes.new("ShaderNodeEmission")
        src = bsdf.inputs[socket]
        if src.is_linked:
            nt.links.new(src.links[0].from_socket, emit.inputs[0])
        else:
            v = src.default_value
            emit.inputs[0].default_value = (v, v, v, 1.0) if isinstance(v, float) else v
        nt.links.new(emit.outputs[0], out.inputs[0])
        restore.append((nt, bsdf, out, emit))
    try:
        yield
    finally:
        for nt, bsdf, out, emit in restore:
            nt.links.new(bsdf.outputs[0], out.inputs[0])
            nt.nodes.remove(emit)


def _bake(objects, materials, image, kind, samples, **extra):
    _target(materials, image)
    _select(objects)
    scene = bpy.context.scene
    scene.cycles.samples = samples
    bpy.ops.object.bake(type=kind, target="IMAGE_TEXTURES", margin=8, use_clear=True, **extra)


def _pixels(image):
    buf = np.empty(image.size[0] * image.size[1] * 4, dtype=np.float32)
    image.pixels.foreach_get(buf)
    return buf.reshape(-1, 4)


def _image(name, size, data=False):
    img = bpy.data.images.new(name, size, size, alpha=False)
    if data:
        img.colorspace_settings.name = "Non-Color"
    return img


def finish(groups, samples=16, keep=None, emission=None):
    """Bake every group to its own atlas and leave the objects wearing the result.

    `keep` is a directory to save the atlases into as PNGs for looking at; they are packed
    into the .blend regardless, which is how they reach the GLB.

    `emission`, when given, is the strength of a fourth atlas: whatever the materials emit
    (their Emission Color at strength 1, so a vein's brightness is in its colour) is baked
    too, and the baked material glows with it at that strength. The glTF exporter writes it as
    an emissive texture with KHR_materials_emissive_strength. Off by default: nothing that
    was baked before glows.
    """
    if os.environ.get("IRONBOUND_FAST"):
        # For iterating on shape: the bake is nearly all of a build's two minutes.
        print("SURFACE skipped (IRONBOUND_FAST)")
        return
    scene = bpy.context.scene
    engine = scene.render.engine
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.render.bake.use_selected_to_active = False
    scene.render.bake.use_pass_direct = False
    scene.render.bake.use_pass_indirect = False
    scene.render.bake.use_pass_color = True

    cache = {}
    for name, objects, size, occlusion in groups:
        objects = [o for o in objects if o.type == "MESH" and len(o.data.polygons) > 0]
        if not objects:
            continue
        for o in objects:
            for slot in o.material_slots:
                if slot.material is not None:
                    slot.material = _procedural(slot.material, cache)
        _apply_modifiers(objects)
        _unwrap(objects, size)
        materials = _materials_of(objects)

        colour = _image(f"{name}_colour", size)
        normal = _image(f"{name}_normal", size, data=True)
        rough = _image(f"{name}_rough", size, data=True)
        metal = _image(f"{name}_metal", size, data=True)

        _bake(objects, materials, colour, "DIFFUSE", samples, pass_filter={"COLOR"})
        _bake(objects, materials, normal, "NORMAL", 1)
        with _emitting(materials, "Roughness"):
            _bake(objects, materials, rough, "EMIT", 1)
        with _emitting(materials, "Metallic"):
            _bake(objects, materials, metal, "EMIT", 1)

        c, r, mt = _pixels(colour), _pixels(rough), _pixels(metal)
        if occlusion:
            ao = _image(f"{name}_ao", size, data=True)
            _bake(objects, materials, ao, "AO", samples * 2)
            a = _pixels(ao)[:, 0:1]
            c[:, :3] *= 0.35 + 0.65 * a ** 1.5
            bpy.data.images.remove(ao)
        glow = None
        if emission:
            glow = _image(f"{name}_emission", size)
            _bake(objects, materials, glow, "EMIT", 1)
        orm = _image(f"{name}_orm", size, data=True)
        packed = np.ones_like(c)
        packed[:, 1] = r[:, 0]
        packed[:, 2] = mt[:, 0]
        orm.pixels.foreach_set(packed.ravel())
        colour.pixels.foreach_set(c.ravel())
        bpy.data.images.remove(rough)
        bpy.data.images.remove(metal)

        for m in materials:
            node = m.node_tree.nodes.get("BakeTarget")
            if node:
                m.node_tree.nodes.remove(node)

        baked = _baked_material(name, colour, orm, normal, glow, emission or 0.0)
        for o in objects:
            o.data.materials.clear()
            o.data.materials.append(baked)

        folder = keep or tempfile.mkdtemp(prefix="ironbound_atlas_")
        for img in (colour, orm, normal) + ((glow,) if glow else ()):
            img.filepath_raw = os.path.join(folder, f"{img.name}.png")
            img.file_format = "PNG"
            img.save()
            img.pack()
        print(f"SURFACE {name:8} {len(objects):3} parts  {len(materials):2} materials  {size}px")

    scene.render.engine = engine


def _baked_material(name, colour, orm, normal, glow=None, strength=0.0):
    """One Principled material reading the atlases, in the shape the glTF exporter
    recognises: colour, a roughness/metallic image through Separate Color, a normal map, and
    an emission image at a strength if there is one."""
    m, nt, bsdf = _tree(f"Baked_{name}")

    def tex(img):
        n = nt.nodes.new("ShaderNodeTexImage")
        n.image = img
        n.interpolation = "Linear"
        return n

    nt.links.new(tex(colour).outputs[0], bsdf.inputs["Base Color"])
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(tex(orm).outputs[0], sep.inputs[0])
    nt.links.new(sep.outputs[1], bsdf.inputs["Roughness"])
    nt.links.new(sep.outputs[2], bsdf.inputs["Metallic"])
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(tex(normal).outputs[0], nm.inputs[1])
    nt.links.new(nm.outputs[0], bsdf.inputs["Normal"])
    if glow is not None:
        nt.links.new(tex(glow).outputs[0], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = strength
    return m

"""Weapon families: one recipe table, a builder per kind of weapon, a dozen named materials.

    import weapon_families as wf
    parts, info = wf.build("longsword")       # Blender objects, standing up, grip at the origin

Imported by `generate_weapons.py`, which owns the export, and by `render_icons.py`, which reads
the hands classes and the material table. Not run on its own. The parts are in
`weapon_parts.py`; the table itself, every weapon in the catalogue, is `weapon_recipes.py`.

WHY FAMILIES. The first sword was one function at four lengths, and the owner could not tell a
longsword from a greatsword: "some of those swords look way too similar, some dont look correct,
for what they are." A weapon is told apart by the parts that make it what it is — a zweihander
by its ricasso, lugs and forearm-long grip, a gladius by its parallel edges and short point —
so each family builds from named parts, and the recipe says which, and how big, for each id.
Two ids share a silhouette only when the real weapons do.

THE RECIPE FORMAT. `RECIPES[id]` is a dict:

    family    the sheet it belongs to: swords, sabres, knives, axes, hammers, spears, polearms,
              flails, bows, crossbows, firearms, thrown, worn, shields, double
    build     the builder: straight | curved | hafted | bow | crossbow | firearm | shield
    hands     the inventory size class: light | one | two | polearm | bow | crossbow | shield
    like      another id to start from. This row is merged over it all the way down, so a
              named weapon can be "a longsword, with this guard": dict(like="longsword",
              guard=dict(span=0.40), metal="Gold")
    extra     parts added on top of whatever the base has (the same vocabulary as `parts`)
    variant   a special material or an enhancement: cold-iron | silver | adamantine | mithral | +1..+5
    pointed   built standing up, then laid forward along +Y: crossbows and firearms
    metal, fittings, grip_material     the named materials of the business end, the furniture, the grip

and the builder's own keys: blade/guard/grip/pommel for swords; haft/wraps/butt/head/parts for
anything on a haft; and so on, documented on each builder.

PARTS. Everything that is not a sword's blade is described as a list of part specs, each a dict
naming one primitive from `weapon_parts` and its arguments — {"sweep": path, "wl": .., "wr":
..}, {"plate": outline, ...}, {"lathe": profile}, {"spike": (base, tip)}, {"chain": path} — with
"m" the material, "role" what it is, "at"/"rot"/"dir" where it goes and "mirror" for a copy
across X. HEADS turn a few numbers into such lists for the common heads: axe, hammer, pick,
spear, glaive, hook, prongs, flanged and spiked maces.

EFFECTS LINE. Every bladed, hafted or pointed weapon gets two empty nodes, FX_Start and FX_End,
along its striking edge or head (blade base to tip; head bottom to top for axes and maces), for
the game to run flame, frost and shock along. A builder may say where; otherwise it is the
bounding box of the parts whose role is edge or head, along its longest side.

MATERIALS. Catalogue weapons are not baked: each mesh carries a few materials named exactly as
in MATERIALS, which the game swaps for its own shared tileable ones. The sixteen items characters
hold today are still baked by `surface.py`, and the names are kept up to the bake so a recipe
here is also how they are textured. Glows (runes, radium) are never baked.
"""
import copy
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

import generate_goblin as gg
import surface
from weapon_parts import (Y, arc, band, box_ring_yz, box_ring_z, chain, cord, cross_guard, crisp, curved_blade,
                          ellipse, grip, haft, lathe, lerp, orient, place, plate, prism, ring, smoothstep,
                          spike, spiked_ball, spline, straight_blade, sweep, tag, torus, tube, wheel)


# --- materials ---------------------------------------------------------------------------------
# Base colours are linear, as Blender and Godot both take them. Metallic stays under 0.7 on
# purpose, as on every model in the game: the board is lit by a flat ambient colour with no sky
# to reflect, and a fully metallic surface with nothing to reflect is a dark hole.

def _silver(colour):
    """Alchemical silver: polished to a mirror, cool, nearly without a mark on it.

    Less metallic than steel, which is not physics: a true mirror shows what is round it, and
    neither the icon's studio nor the board has much round it to show, so a metallic silver
    came out as dark as plain steel. Bright in its colour and glossy, it reads as silver."""
    m, nt, bsdf = surface._tree("Surface_Silver")
    co = surface._coords(nt)
    tone = surface._shade(nt, colour, surface._span(nt, surface._noise(nt, co, 10, 2), 0.95, 1.02))
    tone = surface._mix(nt, tone, (1.0, 1.0, 1.0), surface._math(nt, "MULTIPLY", surface._edges(nt), 0.6))
    rough = surface._span(nt, surface._noise(nt, co, 30, 2), 0.05, 0.10)
    # No brushing: a mirror has none, and fine bands alias into a ladder when baked.
    n = surface._bump(nt, surface._noise(nt, co, 18, 2), 0.05, 0.006)
    surface._finish(nt, bsdf, tone, rough, n, 0.38)
    return m


def _cold_iron(colour):
    """Cold iron: forged without heat, near black, with a faint blue where the light slides off
    it. The blue is in the colour and the low roughness together; mottled forge scale stops it
    reading as paint, and the edges are ground a shade lighter and bluer, never bright."""
    m, nt, bsdf = surface._tree("Surface_ColdIron")
    co = surface._coords(nt)
    scale = surface._noise(nt, co, 7, 4, 0.6)
    tone = surface._shade(nt, colour, surface._span(nt, scale, 0.70, 1.25))
    tone = surface._shade(nt, tone, surface._span(nt, surface._noise(nt, co, 45, 3), 0.85, 1.08))
    # Drifts of temper blue across the black, as on a blued blade: the sheen, where no sky is.
    blue = surface._ramp(nt, surface._noise(nt, co, 4, 3, 0.55), [(0.40, 0.0), (0.62, 0.85)])
    tone = surface._mix(nt, tone, (0.075, 0.105, 0.20), blue)
    edge = (0.20, 0.25, 0.36)
    tone = surface._mix(nt, tone, edge, surface._math(nt, "MULTIPLY", surface._edges(nt), 0.85))
    rough = surface._span(nt, scale, 0.26, 0.44)
    n = surface._bump(nt, surface._voronoi(nt, co, 16, "F1"), 0.35, 0.02)          # hammer dents
    n = surface._bump(nt, surface._noise(nt, co, 90, 3), 0.15, 0.004, n)
    surface._finish(nt, bsdf, tone, rough, n, 0.60)
    return m


def _bronze(colour):
    """Cast bronze: warm, a little uneven, a green-brown patina settled in the low places."""
    m, nt, bsdf = surface._tree("Surface_Bronze")
    co = surface._coords(nt)
    tone = surface._shade(nt, colour, surface._span(nt, surface._noise(nt, co, 12, 3), 0.80, 1.10))
    patina = surface._ramp(nt, surface._noise(nt, co, 9, 4, 0.6), [(0.58, 0.0), (0.72, 0.65)])
    patina = surface._math(nt, "MULTIPLY", patina, surface._math(nt, "SUBTRACT", 1.0, surface._edges(nt)), clamp=True)
    tone = surface._mix(nt, tone, (0.16, 0.20, 0.12), patina)
    tone = surface._mix(nt, tone, tuple(min(1.0, c * 1.5) for c in colour), surface._math(nt, "MULTIPLY", surface._edges(nt), 0.6))
    rough = surface._span(nt, patina, 0.36, 0.75)
    n = surface._bump(nt, surface._noise(nt, co, 60, 3), 0.15, 0.004)
    surface._finish(nt, bsdf, tone, rough, n, 0.55)
    return m


def _stone(colour):
    m, nt, bsdf = surface._tree("Surface_Stone")
    co = surface._coords(nt)
    tone = surface._shade(nt, colour, surface._span(nt, surface._noise(nt, co, 8, 4), 0.75, 1.15))
    flecks = surface._ramp(nt, surface._voronoi(nt, co, 60, "F1"), [(0.0, 1.0), (0.12, 0.0)])
    tone = surface._shade(nt, tone, surface._span(nt, flecks, 1.0, 0.7))
    n = surface._bump(nt, surface._noise(nt, co, 30, 4), 0.5, 0.01)
    surface._finish(nt, bsdf, tone, 0.9, n)
    return m


def _crystal(colour):
    """Glass, crystal, obsidian: glossy, a little see-through in the icon."""
    m, nt, bsdf = surface._tree("Surface_Crystal")
    co = surface._coords(nt)
    tone = surface._shade(nt, colour, surface._span(nt, surface._noise(nt, co, 6, 2), 0.8, 1.1))
    surface._finish(nt, bsdf, tone, 0.06, surface._bump(nt, surface._voronoi(nt, co, 12, "F1"), 0.15, 0.01), 0.0)
    bsdf.inputs["Transmission Weight"].default_value = 0.45
    return m


def _lustre(polish, metallic):
    """A dark or bright special metal: its own colour, its own polish, faintly streaked."""
    def recipe(colour):
        m, nt, bsdf = surface._tree("Surface_Lustre")
        co = surface._coords(nt)
        tone = surface._shade(nt, colour, surface._span(nt, surface._noise(nt, co, 14, 3), 0.85, 1.10))
        tone = surface._mix(nt, tone, tuple(min(1.0, c * 1.8 + 0.02) for c in colour), surface._math(nt, "MULTIPLY", surface._edges(nt), 0.6))
        rough = surface._span(nt, surface._noise(nt, co, 40, 2), polish, polish + 0.1)
        n = surface._bump(nt, surface._noise(nt, surface._stretch(nt, co, (1.0, 1.0, 0.08)), 30, 2), 0.06, 0.004)
        surface._finish(nt, bsdf, tone, rough, n, metallic)
        return m
    return recipe


# name: (base colour, metallic, roughness, procedural recipe for the bake and the icon)
MATERIALS = {
    "Steel":      ((0.50, 0.51, 0.53), 0.45, 0.40, lambda c: surface.steel(c, 0.45)),
    "DarkIron":   ((0.15, 0.145, 0.14), 0.40, 0.55, lambda c: surface.iron(c, 0.08)),
    "Bronze":     ((0.52, 0.33, 0.14), 0.55, 0.42, _bronze),
    "Wood":       ((0.42, 0.26, 0.12), 0.00, 0.80, lambda c: surface.wood(c)),
    "Wrap":       ((0.15, 0.08, 0.045), 0.00, 0.85, lambda c: surface.leather(c)),
    "Bone":       ((0.72, 0.64, 0.48), 0.00, 0.55, lambda c: surface.bone(c)),
    "Cloth":      ((0.32, 0.24, 0.16), 0.00, 0.95, lambda c: surface.cloth(c)),
    "Gold":       ((0.83, 0.58, 0.18), 0.60, 0.35, lambda c: surface.gold(c)),
    "Stone":      ((0.40, 0.38, 0.35), 0.00, 0.90, _stone),
    "Horn":       ((0.17, 0.12, 0.08), 0.00, 0.45, lambda c: surface.bone(c)),
    "Crystal":    ((0.55, 0.78, 0.88), 0.00, 0.06, _crystal),
    "Obsidian":   ((0.025, 0.025, 0.03), 0.00, 0.08, _crystal),
    # The special materials. Each must be told from steel at a glance, in an icon.
    "Silver":     ((0.88, 0.93, 1.00), 0.38, 0.08, _silver),
    "ColdIron":   ((0.050, 0.058, 0.085), 0.60, 0.35, _cold_iron),
    "Adamantine": ((0.055, 0.085, 0.065), 0.62, 0.28, _lustre(0.20, 0.62)),
    "Mithral":    ((0.72, 0.82, 0.98), 0.60, 0.16, _lustre(0.10, 0.60)),
}

# Glowing materials: emissive, never baked. An enhancement's rune line, brighter with the
# bonus; the green of radium in the advanced firearms that run on it.
RUNE_COLOUR = (0.30, 0.66, 1.00)
RUNE_STRENGTH = {1: 1.4, 2: 2.2, 3: 3.2, 4: 4.4, 5: 6.0}
GLOWS = {"Radium": ((0.10, 0.30, 0.05), (0.45, 1.00, 0.30), 2.0)}

for _name, (_colour, _metal, _rough, _recipe) in MATERIALS.items():
    surface.RECIPES[_name] = (lambda r: lambda c, m, rough: r(c))(_recipe)


def material(name):
    """The named material, made once per Blender session."""
    if name.startswith("Rune") or name in GLOWS:
        if name in GLOWS:
            base, glow, strength = GLOWS[name]
        else:
            base, glow, strength = (0.05, 0.12, 0.22), RUNE_COLOUR, RUNE_STRENGTH[int(name[4:])]
        m = gg.mat(name, base, 0.0, 0.3)
        bsdf = m.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Emission Color"].default_value = (*glow, 1.0)
        bsdf.inputs["Emission Strength"].default_value = strength
        return m
    colour, metallic, rough, _ = MATERIALS[name]
    return gg.mat(name, colour, metallic, rough)


def procedural(name):
    """The procedural stand-in for a named material, for drawing an icon of an unbaked model.

    Built with the edge wear turned well down. The recipes find edges by Cycles' pointiness,
    which on a coarse low-poly head is high everywhere and washed a forged axe pale; and the
    shared tileable materials the game will put on these meshes have no wear masks at all."""
    base = name.split(".")[0]
    if base not in MATERIALS:
        return None
    edges = surface._edges
    surface._edges = lambda nt, gain=6.0: edges(nt, gain * 0.25)
    try:
        m = MATERIALS[base][3](MATERIALS[base][0])
    finally:
        surface._edges = edges
    m.name = f"Icon_{base}"
    return m


UNBAKED = ("Rune", "Radium")


def is_unbaked(obj):
    return any(m is not None and m.name.startswith(UNBAKED) for m in obj.data.materials)


# --- the part vocabulary -----------------------------------------------------------------------

DEFAULT_ROLE = {"sweep": "edge", "plate": "edge", "spike": "edge", "blade": "edge", "curved": "edge", "ball": "head",
                "prism": "head", "lathe": "fitting", "tube": "fitting", "torus": "fitting", "chain": "chain",
                "cord": "cord", "grip": "grip", "haft": "haft", "guard": "fitting", "wheel": "fitting"}
KINDS = tuple(DEFAULT_ROLE)
FX_ROLES = ("edge", "head")


def mirrored(obj):
    """A copy of a part reflected across X, its faces turned the right way out again."""
    me = obj.data.copy()
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.transform(bm, matrix=Matrix.Diagonal((-1.0, 1.0, 1.0, 1.0)) @ obj.matrix_world, verts=bm.verts)
    bmesh.ops.reverse_faces(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(obj.name + "_M", me)
    bpy.context.collection.objects.link(o)
    o["role"] = obj.get("role", "fitting")
    return o


_count = [0]


def make(spec):
    """Realise one part spec (see the module docstring) as Blender objects."""
    kind = next(k for k in KINDS if k in spec)
    _count[0] += 1
    name = spec.get("name", f"{kind.title()}_{_count[0]}")
    m = material(spec.get("m", "Steel"))
    v = spec[kind]
    if kind == "sweep":
        objs = [sweep(name, v, spec.get("wl", 0.03), spec.get("wr", 0.03), spec.get("t", 0.012), m, tuple(spec.get("sharp", (True, True))),
                      spec.get("tip", "point"), spec.get("per", 6), spec.get("bevel", 0.34), spec.get("tip_offset", 0.0))]
    elif kind == "plate":
        made = plate(name, v, spec.get("centre", (0, 0)), spec.get("thick", 0.03), m, spec.get("edge", 0.003), spec.get("y", 0.0), spec.get("closed", False),
                     material(spec["cheek"]) if spec.get("cheek") else None)
        objs = made if isinstance(made, list) else [made]
    elif kind == "lathe":
        objs = [lathe(name, v, m, spec.get("n", 12), spec.get("oval", 1.0), spec.get("faceted", False), spec.get("x", 0.0), spec.get("phase", 0.0))]
    elif kind == "tube":
        objs = [tube(name, spline(v, spec.get("per", 1)) if spec.get("per") else v, spec.get("r", 0.012), m, spec.get("n", 8), spec.get("closed", False),
                     spec.get("oval", 1.0), spec.get("section"))]
    elif kind == "torus":
        objs = [torus(name, v, spec.get("major", 0.05), spec.get("minor", 0.01), m, spec.get("tilt", 0.0), spec.get("n", 18), spec.get("sides", 6), spec.get("plane", "XZ"))]
    elif kind == "spike":
        objs = [spike(name, v[0], v[1], spec.get("r", 0.02), m, spec.get("n", 6), spec.get("waist", 0.55))]
    elif kind == "ball":
        objs = spiked_ball(name, v, spec.get("r", 0.08), m, spec.get("spikes", 0), spec.get("length", 0.6), spec.get("spike_r", 0.35), spec.get("n", 12))
    elif kind == "prism":
        objs = [prism(name, v, m, spec.get("n", 12), spec.get("square", 0.55), spec.get("sharp", 35))]
    elif kind == "chain":
        objs = chain(name, v, m, spec.get("link", 0.07), spec.get("wire", 0.011), spec.get("per", 8))
    elif kind == "cord":
        objs = [cord(name, v, spec.get("r", 0.012), m, spec.get("per", 6), spec.get("n", 6), spec.get("twist", 0.18))]
    elif kind == "grip":
        z0, z1, r = v
        objs = [grip(name, z0, z1, r, m, spec.get("style", "spiral"), spec.get("turns", 6), spec.get("profile", "straight"), spec.get("bend", 0.0),
                     spec.get("oval", 0.86), spec.get("n", 10))]
    elif kind == "haft":
        z0, z1, r0 = v[:3]
        objs = [haft(name, z0, z1, r0, m, v[3] if len(v) > 3 else None, spec.get("n", 8), spec.get("swell", 0.06), spec.get("steps", 6), spec.get("oval", 1.0))]
    elif kind == "blade":
        objs = [straight_blade(name, v, m)[0]]
    elif kind == "curved":
        objs = [curved_blade(name, v, m)[0]]
    elif kind == "guard":
        objs = [cross_guard(name, v, spec.get("span", 0.2), spec.get("height", 0.015), spec.get("depth", 0.02), m, spec.get("ends", "knob"),
                            spec.get("droop", 0.0), spec.get("centre", 0.4), spec.get("taper", 0.3), spec.get("knob", 0.75))]
    elif kind == "wheel":
        objs = [wheel(name, v, spec.get("r", 0.06), spec.get("half", 0.02), m)]
    role = spec.get("role", DEFAULT_ROLE[kind])
    for o in objs:
        o["role"] = o.get("role", role) if kind in ("chain",) else role
    if "dir" in spec:
        orient(objs, spec.get("at", (0, 0, 0)), spec["dir"], spec.get("roll", 0.0))
    elif "at" in spec or "rot" in spec or "scale" in spec:
        at = spec.get("at", (0, 0, 0))
        place(objs, (at[0], 0.0, at[1]) if len(at) == 2 else at, spec.get("rot", (0, 0, 0)), spec.get("scale", (1, 1, 1)))
    if spec.get("around"):
        # Copies turned about the Z axis: flanges, prongs, studs.
        count = spec["around"]
        base = list(objs)
        for k in range(1, count):
            for o in base:
                c = o.copy()
                c.data = o.data.copy()
                bpy.context.collection.objects.link(c)
                c.matrix_world = Matrix.Rotation(2 * math.pi * k / count, 4, "Z") @ o.matrix_world
                objs.append(c)
    if spec.get("mirror"):
        objs = objs + [mirrored(o) for o in objs]
    return objs


def make_all(specs):
    parts = []
    for s in specs:
        parts += make(s)
    return parts


# --- heads: a few numbers to a list of part specs ----------------------------------------------
# Every head is built on a haft running up Z, its business side toward -X (the side a sword's
# edge faces), the flat of anything bladed facing Y.

AXE_OUTLINES = {
    # (out from the haft, up from the eye), for a head of size 1 (a battleaxe's): an open curve
    # from the top of the socket round the edge and back to its foot.
    "bearded": [(0.0, 0.07), (0.10, 0.08), (0.20, 0.13), (0.29, 0.17), (0.33, 0.08), (0.34, -0.04), (0.31, -0.16), (0.26, -0.28), (0.18, -0.22), (0.09, -0.12), (0.0, -0.07)],
    "broad": [(0.0, 0.08), (0.12, 0.10), (0.24, 0.20), (0.32, 0.24), (0.36, 0.10), (0.37, -0.02), (0.36, -0.14), (0.32, -0.27), (0.24, -0.23), (0.12, -0.12), (0.0, -0.08)],
    "hatchet": [(0.0, 0.06), (0.12, 0.07), (0.21, 0.11), (0.25, 0.05), (0.26, -0.04), (0.24, -0.13), (0.15, -0.09), (0.0, -0.06)],
    "crescent": [(0.0, 0.07), (0.08, 0.09), (0.16, 0.20), (0.25, 0.32), (0.31, 0.20), (0.34, 0.0), (0.31, -0.20), (0.25, -0.32), (0.16, -0.20), (0.08, -0.09), (0.0, -0.07)],
    "dwarven": [(0.0, 0.09), (0.13, 0.10), (0.22, 0.22), (0.35, 0.29), (0.40, 0.13), (0.41, 0.0), (0.40, -0.15), (0.35, -0.31), (0.22, -0.25), (0.13, -0.12), (0.0, -0.09)],
    "cleaver": [(0.0, 0.10), (0.25, 0.12), (0.52, 0.14), (0.58, 0.04), (0.59, -0.18), (0.55, -0.36), (0.40, -0.40), (0.20, -0.34), (0.0, -0.18)],
    "gandasa": [(0.0, 0.07), (0.15, 0.09), (0.30, 0.14), (0.42, 0.24), (0.47, 0.10), (0.46, -0.08), (0.40, -0.22), (0.28, -0.30), (0.14, -0.20), (0.0, -0.08)],
    "fan": [(0.0, 0.06), (0.10, 0.10), (0.20, 0.24), (0.27, 0.30), (0.31, 0.12), (0.32, 0.0), (0.31, -0.12), (0.27, -0.30), (0.20, -0.24), (0.10, -0.10), (0.0, -0.06)],
    "bardiche": [(0.0, 0.10), (0.08, 0.14), (0.18, 0.40), (0.24, 0.62), (0.22, 0.78), (0.26, 0.60), (0.30, 0.30), (0.30, 0.0), (0.26, -0.22), (0.16, -0.36), (0.06, -0.42), (0.0, -0.40)],
    "halberd": [(0.0, 0.06), (0.10, 0.07), (0.22, 0.10), (0.30, 0.14), (0.31, 0.02), (0.30, -0.12), (0.26, -0.22), (0.14, -0.14), (0.0, -0.10)],
}


def axe(z, shape="bearded", size=1.0, thick=0.045, socket=(0.12, 0.055, 0.05), back=None, back_len=0.16, top=0.0, langets=0.0,
        side=-1, m="Steel", hr=0.045, root=None, rivets=True, outline=None, cheek="DarkIron"):
    """An axe head on the haft at height z: a plate ground to an edge, a socket round the haft,
    and on the back a spike, a hammer, a second blade, or nothing. Forged dark (`cheek`) with a
    bright ground edge (`m`)."""
    hz, hx, hy = socket
    root = hx * 0.85 if root is None else root
    body = cheek or m
    pts = [(side * (root + x * size), z + zz * size) for x, zz in (outline or AXE_OUTLINES[shape])]
    specs = [dict(plate=pts, centre=(side * root, z), thick=thick, m=m, cheek=cheek, role="edge")]
    specs.append(dict(prism=[(z - hz, hx * 0.92, hy * 0.92), (z - hz + 0.012, hx, hy), (z + hz - 0.012, hx, hy), (z + hz, hx * 0.92, hy * 0.92)], m=body, role="head"))
    if back == "blade":
        specs.append(dict(plate=[(-side * (root + x * size), z + zz * size) for x, zz in (outline or AXE_OUTLINES[shape])], centre=(-side * root, z), thick=thick, m=m, cheek=cheek, role="edge"))
    elif back == "spike":
        specs.append(dict(sweep=[(-side * hx * 0.6, z + 0.005), (-side * (hx + back_len * 0.6), z - 0.01), (-side * (hx + back_len), z - 0.05)],
                          wl=lambda s: 0.026 * (1 - s) + 0.002, wr=lambda s: 0.026 * (1 - s) + 0.002, t=lambda s: 0.022 * (1 - s) + 0.002,
                          sharp=(False, False), m=m, role="edge", per=4))
    elif back == "hammer":
        f = hy * 1.05
        specs.append(dict(prism=[(0.0, hy * 0.85, hy * 0.85), (back_len * 0.6, hy * 0.8, hy * 0.8), (back_len * 0.85, f, f), (back_len, f, f)], m=body, role="head",
                          rot=(0, side * -90, 0), at=(-side * hx * 0.7, 0, z)))
    if top:
        specs.append(dict(spike=((0, 0, z + hz - 0.01), (0, 0, z + hz + top)), r=hr * 0.8, m=m, role="edge"))
    if langets:
        for sy in (1, -1):
            specs.append(dict(prism=[(z - hz - langets, 0.011, 0.004, 0, sy * (hr + 0.002)), (z - hz + 0.01, 0.014, 0.004, 0, sy * (hr + 0.002))], m=body, role="fitting"))
    if rivets:
        for sy in (1, -1):
            specs.append(dict(lathe=[(0, 0.012), (0.006, 0.012), (0.011, 0.0)], m=m, role="fitting", dir=(0, sy, 0), at=(0, sy * hy * 0.98, z)))
    return specs


def hammer(z, length=0.15, face=0.045, back="spike", back_len=0.18, top=0.0, side=-1, m="Steel", hr=0.045, socket=None, round_face=False, claw=False):
    """A hammer's head across the haft: a block out to a flat face on the business side, and on
    the back a spike, a claw, a second face or nothing."""
    sq = 0.95 if round_face else 0.45
    hz, hx, hy = socket or (face * 1.25, face * 1.1, face * 0.95)
    specs = [dict(prism=[(z - hz, hx, hy), (z + hz, hx, hy)], m=m, role="head", square=0.5)]
    specs.append(dict(prism=[(0.0, face * 0.8, face * 0.8), (length * 0.55, face * 0.78, face * 0.78), (length * 0.78, face * 1.02, face * 1.02), (length * 0.97, face, face), (length, face * 0.9, face * 0.9)],
                      m=m, role="head", square=sq, rot=(0, side * 90, 0), at=(0, 0, z)))
    if back == "spike":
        specs.append(dict(sweep=[(-side * hx * 0.7, z + 0.01), (-side * (hx + back_len * 0.55), z - 0.01), (-side * (hx + back_len), z - 0.07)],
                          wl=lambda s: face * 0.75 * (1 - s) + 0.002, wr=lambda s: face * 0.75 * (1 - s) + 0.002, t=lambda s: face * 0.6 * (1 - s) + 0.002,
                          sharp=(False, False), m=m, role="edge", per=4))
    elif back == "claw":
        for sy in (1, -1):
            specs.append(dict(sweep=[(-side * hx * 0.7, z + 0.01), (-side * (hx + back_len * 0.5), z - 0.02), (-side * (hx + back_len), z - 0.10)],
                              wl=lambda s: face * 0.5 * (1 - s) + 0.002, wr=lambda s: face * 0.5 * (1 - s) + 0.002, t=lambda s: face * 0.25 * (1 - s) + 0.002,
                              sharp=(False, False), m=m, role="edge", per=4, at=(0, sy * face * 0.35, 0)))
    elif back == "face":
        specs.append(dict(prism=[(0.0, face * 0.8, face * 0.8), (length * 0.55, face * 0.78, face * 0.78), (length * 0.78, face * 1.02, face * 1.02), (length * 0.97, face, face), (length, face * 0.9, face * 0.9)],
                          m=m, role="head", square=sq, rot=(0, -side * 90, 0), at=(0, 0, z)))
    if top:
        specs.append(dict(spike=((0, 0, z + hz - 0.01), (0, 0, z + hz + top)), r=min(hx, hy) * 0.8, m=m, role="edge"))
    return specs


def pick(z, length=0.32, drop=0.08, thick=0.022, back=None, back_len=0.12, side=-1, m="Steel", hr=0.045, socket=(0.07, 0.05, 0.042), face=0.035):
    """A pick's beak: a square-sectioned spike curving down from a socket, with a hammer face,
    an adze or nothing on the back."""
    hz, hx, hy = socket
    specs = [dict(prism=[(z - hz, hx, hy), (z + hz, hx, hy)], m=m, role="head", square=0.5)]
    specs.append(dict(sweep=[(side * hx * 0.6, z + 0.005), (side * (hx + length * 0.5), z - drop * 0.25), (side * (hx + length), z - drop)],
                      wl=lambda s: thick * 1.3 * (1 - s) ** 0.8 + 0.002, wr=lambda s: thick * 1.3 * (1 - s) ** 0.8 + 0.002,
                      t=lambda s: thick * (1 - s) ** 0.8 + 0.002, sharp=(False, False), m=m, role="edge", per=5))
    if back == "hammer":
        specs.append(dict(prism=[(0.0, face * 0.8, face * 0.8), (back_len * 0.7, face * 0.9, face * 0.9), (back_len, face, face)], m=m, role="head", rot=(0, -side * 90, 0), at=(0, 0, z)))
    elif back == "adze":
        # An adze: a blade across the line of the haft, flat side down, edge out.
        specs.append(dict(sweep=[(-side * hx * 0.6, 0.0), (-side * (hx + back_len), 0.0)],
                          wl=lambda s: 0.035 + 0.03 * s, wr=lambda s: 0.035 + 0.03 * s, t=lambda s: 0.018 * (1 - s) + 0.002, sharp=(False, False), tip=None,
                          m=m, role="edge", per=1, rot=(90, 0, 0), at=(0, 0, z - 0.01)))
    return specs


def spear(z, length=0.55, width=0.10, shape="leaf", t=0.022, socket=0.20, hr=0.042, m="Steel", wings=0.0, barbs=0, ferrule=True, size=1.0):
    """A spearhead on top of the haft at z: a socket, then a blade with a midrib, both edges
    sharp, in one of a few outlines. `wings` puts lugs below it; `barbs` hooks at its base.
    `size` scales the head (not the socket's fit): heads are drawn larger than life so that
    they still tell one polearm from another on a slot sixty-four pixels across."""
    length, width, t, socket, wings = length * size, width * size, t * size ** 0.7, socket * size, wings * size
    def w(s):
        if shape == "leaf":
            return width * math.sin(math.pi * min(1.0, s * 1.15) ** 0.8) * (1 - 0.15 * s) + 0.004
        if shape == "broad":
            return width * math.sin(math.pi * min(1.0, s * 1.35 + 0.08) ** 0.7) * (1 - 0.1 * s) + 0.004
        if shape == "triangle":
            return width * (1 - s) ** 0.9 * smoothstep(0.0, 0.08, s) + 0.004
        if shape == "lozenge":
            return width * min(s / 0.32, (1 - s) / 0.68) + 0.004
        if shape == "needle":
            return width * (1 - s) ** 0.8 * smoothstep(0.0, 0.06, s) + 0.003
        if shape == "awl":
            if s < 0.84:
                return width * 0.28 + 0.003
            if s < 0.87:
                return width * (0.28 + 0.72 * (s - 0.84) / 0.03)
            return width * (1 - s) / 0.13 + 0.003
        return width
    specs = []
    if socket:
        specs.append(dict(lathe=[(z - socket, hr * 1.05), (z - socket + 0.02, hr * 1.12), (z - socket * 0.3, hr * 0.92), (z, hr * 0.62), (z + 0.03, hr * 0.5)], m=m, role="fitting", n=8))
        if ferrule:
            specs.append(dict(lathe=[(z - socket - 0.004, hr * 1.18), (z - socket + 0.025, hr * 1.18), (z - socket + 0.03, hr * 1.05)], m=m, role="fitting", n=8))
    specs.append(dict(sweep=[(0, z), (0, z + length)], wl=w, wr=w, t=lambda s: t * (1 - 0.6 * s) + 0.003, sharp=(True, True), m=m, role="edge", per=10, bevel=0.5))
    if wings:
        specs.append(dict(sweep=[(-wings, z - socket * 0.55), (0, z - socket * 0.62), (wings, z - socket * 0.55)], wl=0.012, wr=0.012, t=0.012, sharp=(False, False),
                          tip=None, m=m, role="edge", per=3))
    for k in range(barbs):
        zz = z + length * (0.06 + 0.1 * k)
        for sx in (1, -1):
            ww = w(0.06 + 0.1 * k)
            specs.append(dict(sweep=[(sx * ww * 0.6, zz + 0.04), (sx * (ww + 0.05), zz - 0.03)], wl=0.012, wr=0.012, t=0.008, sharp=(True, True), m=m, role="edge", per=1))
    return specs


def glaive(z, length=0.75, width=0.10, curve=0.10, back_spike=0.0, hook=0.0, side=-1, t=0.018, m="Steel", hr=0.042, socket=0.22, point=0.25, flare=0.3, size=1.0):
    """A single-edged polearm blade: up from a socket, its edge to `side`, its back curving
    away (`curve`), with a spike or a hook on the back if asked."""
    length, width, curve, back_spike, hook = length * size, width * size, curve * size, back_spike * size, hook * size
    def w(s):
        return width * (1 + flare * math.sin(math.pi * s * 0.8)) * (1 - smoothstep(1 - point, 1.0, s) * 0.85)
    path = [(0, z), (side * curve * 0.1, z + length * 0.4), (-side * curve * 0.2, z + length * 0.75), (-side * curve, z + length)]
    specs = [dict(lathe=[(z - socket, hr * 1.1), (z - socket * 0.2, hr * 0.95), (z + 0.02, hr * 0.7)], m=m, role="fitting", n=8)]
    if side < 0:
        specs.append(dict(sweep=path, wl=w, wr=0.012, t=t, sharp=(True, False), m=m, role="edge", per=6, tip_offset=width * 0.3))
    else:
        specs.append(dict(sweep=path, wl=0.012, wr=w, t=t, sharp=(False, True), m=m, role="edge", per=6, tip_offset=-width * 0.3))
    if back_spike:
        specs.append(dict(sweep=[(-side * 0.01, z + length * 0.25), (-side * (back_spike * 0.6), z + length * 0.24), (-side * back_spike, z + length * 0.18)],
                          wl=lambda s: 0.02 * (1 - s) + 0.002, wr=lambda s: 0.02 * (1 - s) + 0.002, t=lambda s: 0.016 * (1 - s) + 0.002, sharp=(False, False), m=m, role="edge", per=3))
    if hook:
        specs.append(dict(sweep=[(-side * 0.01, z + length * 0.30), (-side * hook * 0.7, z + length * 0.32), (-side * hook, z + length * 0.42), (-side * hook * 0.8, z + length * 0.50)],
                          wl=lambda s: 0.03 * (1 - s) + 0.003, wr=lambda s: 0.016 * (1 - s) + 0.002, t=0.012, sharp=(True, False), m=m, role="edge", per=4))
    return specs


def hook(z, reach=0.20, rise=0.16, side=1, width=0.035, t=0.014, m="Steel", root=0.03, down=False, size=1.0):
    """A hook out of the haft at z: out to `side`, then curling up (or down) and back."""
    reach, rise, width = reach * size, rise * size, width * size
    sz = -1 if down else 1
    path = [(side * root, z), (side * reach * 0.7, z + sz * rise * 0.15), (side * reach, z + sz * rise * 0.6), (side * reach * 0.75, z + sz * rise)]
    if side > 0:
        return [dict(sweep=path, wl=lambda s: width * (1 - 0.7 * s), wr=lambda s: width * 0.4 * (1 - 0.5 * s), t=t, sharp=(True, False), m=m, role="edge", per=5)]
    return [dict(sweep=path, wl=lambda s: width * 0.4 * (1 - 0.5 * s), wr=lambda s: width * (1 - 0.7 * s), t=t, sharp=(False, True), m=m, role="edge", per=5)]


def prongs(z, n=3, spread=0.12, length=0.35, curve=0.0, centre=1.0, barbs=True, r=0.016, m="Steel", hr=0.042, bar=True, size=1.0):
    """Prongs up from a crossbar: a trident's three, a fork's two, a ranseur's spike and wings.
    `centre` is the middle prong's length against the others'; `curve` bends the outer ones out."""
    spread, length, curve, r = spread * size, length * size, curve * size, r * size ** 0.8
    specs = []
    xs = [lerp(-spread, spread, k / (n - 1)) for k in range(n)] if n > 1 else [0.0]
    if bar and n > 1:
        specs.append(dict(tube=[(-spread - r * 0.5, 0, z), (spread + r * 0.5, 0, z)], r=r * 1.2, m=m, role="head", n=6))
        specs.append(dict(lathe=[(z - 0.16, hr * 1.1), (z - 0.04, hr * 0.95), (z + r, r * 1.5)], m=m, role="fitting", n=8))
    for x in xs:
        L = length * (centre if abs(x) < 1e-6 else 1.0)
        out = curve * (1 if x > 0 else -1 if x < 0 else 0)
        path = [(x, z), (x + out * 0.4, z + L * 0.5), (x + out, z + L)]
        specs.append(dict(sweep=path, wl=lambda s, r=r: r * (1 - 0.8 * s ** 1.5), wr=lambda s, r=r: r * (1 - 0.8 * s ** 1.5), t=lambda s, r=r: r * 0.9 * (1 - 0.8 * s ** 1.5),
                          sharp=(False, False), m=m, role="edge", per=3))
        if barbs:
            for sx in ((1, -1) if abs(x) < 1e-6 else ((1,) if x > 0 else (-1,))):
                bx = x + out * 0.85
                specs.append(dict(sweep=[(bx + sx * r * 0.4, z + L * 0.9), (bx + sx * r * 2.6, z + L * 0.78)], wl=r * 0.55, wr=r * 0.55, t=r * 0.4, sharp=(True, True), m=m, role="edge", per=1))
    return specs


def flanged(z0, z1, r=0.17, n=6, core=0.09, m="Steel", cap=True, low=False):
    """A flanged mace's head: flanges standing out of an iron core, deepest in the middle."""
    h = z1 - z0
    out = [(core * 0.5, z0), (r * (0.75 if low else 0.9), z0 + h * 0.18), (r, z0 + h * 0.48), (r * (0.75 if low else 0.9), z0 + h * 0.8), (core * 0.5, z1)]
    specs = [dict(lathe=[(z0 - 0.03, core * 0.55), (z0, core * 0.7), (z0 + h * 0.3, core), (z1 - h * 0.25, core), (z1, core * 0.6)], m=m, role="head", n=n * 2)]
    for k in range(n):
        a = k * 2 * math.pi / n
        c, s_ = math.cos(a), math.sin(a)
        rings = [ring([(x * c - t * s_, x * s_ + t * c, zz) for x, zz in out]) for t in (-0.016, 0.016)]
        specs.append(dict(_rings=rings, m=m, role="head"))
    if cap:
        specs.append(dict(lathe=[(z1 - 0.01, core * 0.6), (z1 + 0.03, core * 0.45), (z1 + 0.07, 0.0)], m=m, role="head", n=8))
    return specs


def collar(z, r, h=0.06, m="Steel"):
    return [dict(lathe=[(z - h, r * 0.85), (z - h * 0.7, r), (z, r * 1.1), (z + 0.01, r * 0.9)], m=m, role="fitting", n=10)]


def butt(z, kind="cap", r=0.045, m="DarkIron"):
    """The foot of a haft: a cap, a spike, a knob, a ring."""
    if kind == "cap":
        return [dict(lathe=[(z - 0.012, 0.0), (z - 0.01, r * 0.8), (z, r * 1.1), (z + 0.09, r * 1.05), (z + 0.1, r * 0.95)], m=m, role="fitting", n=8)]
    if kind == "spike":
        return [dict(lathe=[(z - 0.22, 0.0), (z - 0.12, r * 0.45), (z, r * 1.05), (z + 0.08, r * 1.08), (z + 0.09, r * 0.95)], m=m, role="fitting", n=8)]
    if kind == "knob":
        return [dict(lathe=[(z - 0.07, 0.0), (z - 0.06, r * 0.9), (z - 0.03, r * 1.3), (z, r * 1.1), (z + 0.02, r * 0.95)], m=m, role="fitting", n=10)]
    if kind == "flare":
        return [dict(lathe=[(z - 0.02, 0.0), (z - 0.015, r * 1.35), (z + 0.03, r * 1.25), (z + 0.09, r * 1.0)], m=m, role="fitting", n=10)]
    return []


HEADS = {"axe": axe, "hammer": hammer, "pick": pick, "spear": spear, "glaive": glaive, "hook": hook, "prongs": prongs,
         "flanged": flanged, "collar": collar, "butt": butt}


def heads(specs_or_heads):
    """Expand `head` entries — dicts with a "kind" from HEADS — into part specs."""
    out = []
    for h in specs_or_heads:
        if "kind" in h:
            args = {k: v for k, v in h.items() if k != "kind"}
            out += HEADS[h["kind"]](**args)
        else:
            out.append(h)
    return out


def realise(specs):
    """Part specs to objects, including the raw rings some heads hand over."""
    parts = []
    for s in specs:
        if "_rings" in s:
            o = gg.loft(f"Flange_{len(parts)}", s["_rings"], material(s.get("m", "Steel")), smooth_shading=False)
            o["role"] = s.get("role", "head")
            parts.append(o)
        else:
            parts += make(s)
    return parts


# --- swords and knives -------------------------------------------------------------------------

def _hilt(r, metal, fittings, held, blade_root=0.0, blade_halfwidth=0.05, thick=0.02):
    """Guard, grip and pommel, shared by the straight and curved builders.

    guard.kind: cross | disc | tsuba | swept | knuckle | shell | parry | ring | crescent | bar | none
    pommel.kind: scent | ovoid | wheel | pear | ball | knob | cap | crook | ring | spike | beak | none
    """
    g = r.get("guard", {"kind": "none"})
    gr = r["grip"]
    pm = r.get("pommel", {"kind": "none"})
    parts = []
    kind = g.get("kind", "cross")
    gz = g.get("z", gr["top"])
    bend = gr.get("bend", 0.0)
    if kind in ("cross", "ring", "knuckle", "swept", "parry", "bar"):
        span = g.get("span", 0.2)
        droop = g.get("droop", 0.0)
        if kind == "swept":
            droop = (lambda t, d=g.get("droop", 0.07): d * t ** 3)          # S-curved quillons
        if kind == "parry":
            droop = g.get("droop", 0.09)
        parts.append(tag(cross_guard("Guard", gz, span, g.get("height", 0.015), g.get("depth", 0.02), fittings, g.get("ends", "knob"),
                                     droop, g.get("centre", 0.4), g.get("taper", 0.3), g.get("knob", 0.75)), "fitting"))
    elif kind == "disc":
        z0, z1, rx, ry = g["bottom"], g["top"], g["rx"], g["ry"]
        prof = [(z0, 0.72), (z0 + 0.008, 0.90), (z0 + 0.25 * (z1 - z0), 1.0), (z0 + 0.7 * (z1 - z0), 0.98), (z1 - 0.01, 0.86), (z1, 0.62)]
        sections = [ellipse(0, 0, z, rx * k, ry * k, 20) for z, k in prof]
        parts.append(tag(crisp(gg.loft("Guard", sections, fittings), 40), "fitting"))
    elif kind == "tsuba":
        rx, ry, h = g.get("rx", 0.08), g.get("ry", 0.07), g.get("height", 0.012)
        parts.append(tag(prism("Guard", [(gz - h, rx * 0.97, ry * 0.97), (gz - h * 0.7, rx, ry), (gz + h * 0.7, rx, ry), (gz + h, rx * 0.97, ry * 0.97)], fittings, 20, 1.0, 40), "fitting"))
        # The habaki, a collar that wedges the blade in its scabbard.
        parts.append(tag(prism("Habaki", [(gz + h, blade_halfwidth * 1.15 + 0.004, thick * 1.6 + 0.004, (blade_root or 0.0)), (gz + h + 0.05, blade_halfwidth * 1.05 + 0.002, thick * 1.3 + 0.003, (blade_root or 0.0))], material("Gold" if g.get("gold", True) else "Steel"), 12, 0.6), "fitting"))
    elif kind == "crescent":
        # A crescent of steel in front of the knuckles, sharp on its outside: the hook sword's.
        w = g.get("span", 0.16)
        drop = g.get("drop", 0.12)
        pts = [(-w, gz - drop * 0.2), (-w * 0.7, gz - drop * 0.75), (0.0, gz - drop), (w * 0.7, gz - drop * 0.75), (w, gz - drop * 0.2)]
        parts.append(tag(sweep("Guard", pts, lambda s: 0.03 * math.sin(math.pi * s) + 0.004, 0.012, 0.009, fittings, (True, False), "point", 6), "edge"))
        parts.append(tag(prism("Guard_Block", [(gz - 0.03, 0.03, 0.022), (gz + 0.012, 0.03, 0.022)], fittings), "fitting"))
    elif kind == "shell":
        # A cutlass's shell: a dished plate across the hand, under a knuckle bow.
        rr = g.get("radius", 0.13)
        parts.append(tag(lathe("Guard", [(gz + 0.012, 0.03), (gz + 0.010, rr * 0.6), (gz - 0.006, rr * 0.95), (gz - 0.03, rr), (gz - 0.036, rr * 0.96),
                                         (gz - 0.014, rr * 0.88), (gz - 0.002, rr * 0.55), (gz - 0.004, 0.03)], fittings, 18, g.get("oval", 0.72)), "fitting"))
    if kind in ("ring", "parry") or g.get("side_ring"):
        rr = g.get("ring", 0.05)
        parts.append(tag(torus("Guard_Ring", (0, -0.02, gz - 0.005), rr, 0.009, fittings, plane="XY", n=16), "fitting"))
    if kind == "swept":
        # A swept hilt: the knuckle bow down to the pommel, a ring on the outside, and the
        # branches that join them.
        rr = g.get("ring", 0.06)
        parts.append(tag(torus("Guard_Ring", (-0.01, -0.035, gz + 0.01), rr, 0.009, fittings, tilt=0.0, plane="XY", n=16), "fitting"))
        parts.append(tag(tube("Guard_Branch", spline([(-0.03, -0.01, gz + 0.005), (-0.05, -0.07, gz + 0.04), (-0.02, -0.09, gz + 0.06)], 4), 0.008, fittings, 6), "fitting"))
    if kind in ("knuckle", "swept", "shell") or g.get("knuckle"):
        x0 = -g.get("radius", 0.13) * 0.75 if kind == "shell" else -g.get("span", 0.12) * 0.95
        z0 = gz - 0.01
        x1, z1 = bend - 0.035, gr["bottom"] - 0.025
        pts = []
        for k in range(13):
            u = k / 12
            pts.append((lerp(x0, x1, u) - g.get("bow", 0.075) * math.sin(math.pi * u) ** 0.8, 0.0, lerp(z0, z1, u)))
        parts.append(tag(tube("Knuckle_Bow", pts, lambda u: 0.010 + 0.004 * math.sin(math.pi * u), fittings), "fitting"))
    if g.get("rings"):
        rr, rz, rx, tilt = g["rings"]
        for sx in (-1, 1):
            parts.append(tag(torus(f"Guard_Ring_{sx}", (sx * rx, 0, rz), rr, 0.011, fittings, tilt), "fitting"))
    if g.get("langets"):
        up, down = g["langets"]
        for side in (-1, 1):
            y = side * (thick + 0.006)
            for nm, z_a, z_b in (("Up", gz, gz + up), ("Down", gz, gz - down)):
                secs = []
                for k in range(6):
                    u = k / 5
                    zz = lerp(z_a, z_b, u)
                    hw = 0.022 * (1 - u ** 1.5) + 0.002
                    secs.append(ring([(-hw, y, zz), (0, y + side * 0.006, zz), (hw, y, zz), (0, y - side * 0.002, zz)]))
                parts.append(tag(crisp(gg.loft(f"Langet_{nm}_{side}", secs, fittings), 30), "fitting"))

    parts.append(grip("Grip", gr["bottom"], gr["top"], gr["radius"], held, gr.get("style", "spiral"), gr.get("turns", 8),
                      gr.get("profile", "straight"), bend=bend, oval=gr.get("oval", 0.86)))
    parts += _pommel(pm, fittings, gr, bend)
    return parts


def _pommel(p, fittings, gr, bend=0.0):
    kind = p.get("kind", "none")
    if kind == "none":
        return []
    top = p.get("top", gr["bottom"])
    r = p.get("radius", 0.04)
    if kind == "scent":
        h = p["height"]
        prof = [(top + 0.004, r * 0.55), (top - 0.10 * h, r * 0.66), (top - 0.32 * h, r * 0.95), (top - 0.48 * h, r),
                (top - 0.70 * h, r * 0.74), (top - 0.88 * h, r * 0.36), (top - h, 0.0)]
        return [tag(lathe("Pommel", prof, fittings, n=8, oval=0.78, faceted=True, phase=math.pi / 8, x=bend), "fitting")]
    if kind == "ovoid":
        h = p["height"]
        prof = [(top + 0.004, r * 0.45), (top - 0.05 * h, r * 0.72), (top - 0.25 * h, r * 0.96), (top - 0.50 * h, r),
                (top - 0.78 * h, r * 0.82), (top - 0.94 * h, r * 0.45), (top - h, r * 0.38)]
        parts = [tag(lathe("Pommel", prof, fittings, n=16, oval=p.get("oval", 0.8)), "fitting")]
        parts.append(tag(lathe("Pommel_Nut", [(top - h + 0.004, r * 0.30), (top - h - 0.012, r * 0.30), (top - h - 0.024, r * 0.18), (top - h - 0.028, 0.0)],
                               material(p.get("nut", "Bronze")), n=10), "fitting"))
        return parts
    if kind == "wheel":
        zc = top - r * 0.92
        parts = [tag(wheel("Pommel", zc, r, p.get("half", 0.024), fittings), "fitting")]
        b = zc - r * 0.97
        parts.append(tag(lathe("Pommel_Button", [(b + 0.01, 0.018), (b - 0.006, 0.022), (b - 0.02, 0.014), (b - 0.026, 0.0)], fittings, n=8, faceted=True), "fitting"))
        return parts
    if kind == "pear":
        h = p["height"]
        prof = [(top + 0.004, r * 0.48), (top - 0.07 * h, r * 0.62), (top - 0.30 * h, r * 0.92), (top - 0.52 * h, r),
                (top - 0.72 * h, r * 0.86), (top - 0.86 * h, r * 0.55), (top - 0.94 * h, r * 0.38), (top - h, 0.0)]
        return [tag(lathe("Pommel", prof, fittings, n=8, oval=0.86, faceted=True, phase=math.pi / 8), "fitting")]
    if kind in ("ball", "knob"):
        h = p.get("height", 2 * r)
        prof = [(top + 0.004, r * 0.5), (top - 0.15 * h, r * 0.86), (top - 0.5 * h, r), (top - 0.85 * h, r * 0.86), (top - h, 0.0)]
        return [tag(lathe("Pommel", prof, fittings, n=12, x=bend), "fitting")]
    if kind == "cap":
        h = p.get("height", 0.03)
        prof = [(top + 0.004, r * 0.9), (top - h * 0.3, r * 1.05), (top - h * 0.8, r), (top - h, r * 0.5), (top - h - 0.004, 0.0)]
        return [tag(lathe("Pommel", prof, fittings, n=12, oval=p.get("oval", 0.85), x=bend), "fitting")]
    if kind == "ring":
        rr = p.get("ring", 0.04)
        parts = [tag(lathe("Pommel", [(top + 0.004, r * 0.8), (top - 0.02, r), (top - 0.03, r * 0.6)], fittings, n=10, x=bend), "fitting")]
        parts.append(tag(torus("Pommel_Ring", (bend, 0, top - 0.03 - rr), rr, p.get("wire", 0.009), fittings, n=16), "fitting"))
        return parts
    if kind == "spike":
        return [tag(spike("Pommel", (bend, 0, top + 0.01), (bend, 0, top - p.get("length", 0.12)), r, fittings, 6), "edge")]
    if kind == "crook":
        # A walking stick's handle: the grip turns over into a crook at the top.
        pts = [(0, 0, top + 0.01), (0, 0, top - 0.04), (-0.02, 0, top - 0.10), (-0.08, 0, top - 0.13), (-0.14, 0, top - 0.10), (-0.15, 0, top - 0.04)]
        return [tag(tube("Pommel", spline(pts, 4), r, material(p.get("m", "Wood")), 8), "grip"),
                tag(lathe("Pommel_Cap", [(top - 0.04, 0.0), (top - 0.035, r * 1.15), (top - 0.005, r * 1.15), (top, 0.0)], fittings, 10, x=-0.15), "fitting")]
    if kind == "beak":
        h = p.get("height", 0.085)
        secs = []
        prof = [(0.0, 0.92), (0.15, 1.05), (0.45, 1.18), (0.75, 1.05), (0.92, 0.72), (1.0, 0.0)]
        for u, k in prof:
            z = top - h * u + 0.004
            x = bend + (-p.get("beak", 0.04)) * u ** 2
            secs.append(ellipse(x, 0, z, r * k, r * k * 0.82, 12) if k > 0 else [Vector((x - 0.01, 0, z))] * 12)
        return [tag(crisp(gg.loft("Pommel", secs, fittings), 40), "fitting")]
    raise ValueError(kind)


def _straight(r):
    """A straight double-edged blade with its hilt (metal names the blade's material).

    blade: base, tip, width, thick, section (diamond|lens|hex|square), flat, taper, point,
    curve, distal, fuller (s0, s1, half-width, depth), leaf, waist, wave (amplitude, count).
    ricasso (z0, z1, hx, hy) and lugs (z, length, height): a zweihander's."""
    metal = material(r["metal"])
    fittings = material(r["fittings"])
    held = material(r.get("grip_material", "Wrap"))
    b = r["blade"]
    parts = []
    info = {}
    if b.get("split"):
        # Two half-blades side by side, parting toward the tip: a sword that splits in two.
        gap, angle = b["split"]
        half = dict(b, width=b["width"] * 0.5, split=None)
        floor = []
        for sx in (-1, 1):
            bl, _ = straight_blade(f"Blade_{sx}", half, metal)
            place(bl, (0, 0, -b["base"]))
            place(bl, (0, 0, 0), (0, sx * angle, 0))
            place(bl, (sx * (b["width"] * 0.5 + gap) * 0.5, 0, b["base"]))
            parts.append(bl)
    else:
        blade, floor = straight_blade("Blade", b, metal)
        parts.append(blade)
    info["fuller_floor"] = floor
    info["fx"] = (Vector((0, 0, b["base"])), Vector((0, 0, b["tip"])))
    if r.get("ricasso"):
        z0, z1, hx, hy = r["ricasso"]
        steps = 14
        sections = []
        for i in range(steps + 1):
            t = i / steps
            swell = 1 + 0.05 * math.sin(t * steps * math.pi)
            sections.append(box_ring_z(lerp(z0, z1, t), hx * swell, hy * swell))
        parts.append(tag(gg.loft("Ricasso", sections, held), "grip"))
        lz, ll, lh = r["lugs"]
        for side in (-1, 1):
            secs = []
            for k in range(7):
                u = k / 6
                x = side * (hx * 0.85 + ll * u)
                zc = lz - 0.35 * lh * u ** 1.6
                hh = lh * (1 - 0.82 * u) * (1.15 if k == 0 else 1.0)
                dd = 0.026 * (1 - 0.6 * u)
                secs.append(ring([(x, 0, zc - hh), (x, dd, zc), (x, 0, zc + hh * 0.8), (x, -dd, zc)]))
            secs.append(ring([(side * (hx * 0.85 + ll * 1.08), 0, lz - 0.40 * lh)] * 4))
            parts.append(tag(crisp(gg.loft(f"Lug_{side}", secs, metal), 30), "edge"))
        info["fx"] = (Vector((0, 0, z0)), Vector((0, 0, b["tip"])))
    parts += _hilt(r, metal, fittings, held, 0.0, b["width"], b["thick"])
    parts += realise(heads(r.get("parts", []) + r.get("extra", [])))
    return parts, info


def _curved(r):
    """A single-edged blade (see `curved_blade`) with its hilt."""
    metal = material(r["metal"])
    fittings = material(r["fittings"])
    held = material(r.get("grip_material", "Wrap"))
    b = r["blade"]
    blade, tip, binfo = curved_blade("Blade", b, metal)
    parts = [blade]
    parts += _hilt(r, metal, fittings, held, 0.0, b["width"] / 2, b["thick"])
    parts += realise(heads(r.get("parts", []) + r.get("extra", [])))
    root = Vector((0, 0, b["base"]))
    return parts, {"tip": tip, "fx": (root, tip), "blade": binfo}


def _hafted(r):
    """Anything on a haft or a handle, or nothing at all: axes, maces, hammers, picks, spears,
    polearms, flails, staves, thrown and worn things.

    haft (z0, z1, r0[, r1]) with haft_material (Wood) and haft_sides; wraps [(z0, z1, r[, material,
    style])]; butt dict for `butt()`; head: a list of HEADS entries or raw part specs; parts and
    extra: raw part specs."""
    parts = []
    h = r.get("haft")
    if h:
        parts.append(haft("Haft", h[0], h[1], h[2], material(r.get("haft_material", "Wood")), h[3] if len(h) > 3 else None,
                          r.get("haft_sides", 8), r.get("haft_swell", 0.06), r.get("haft_steps", 6), r.get("haft_oval", 1.0)))
    for w in r.get("wraps", []):
        parts.append(grip("Wrap", w[0], w[1], w[2], material(w[3] if len(w) > 3 else r.get("grip_material", "Wrap")),
                          w[4] if len(w) > 4 else "spiral", max(3, int((w[1] - w[0]) / 0.045)), n=8))
    if r.get("butt"):
        parts += realise(butt(**r["butt"]))
    parts += realise(heads(r.get("head", [])))
    if r.get("lower"):
        # A double weapon's other end: built as if at the top, then turned end over end.
        lower = realise(heads(r["lower"]))
        place(lower, (0, 0, 0), (180, 0, 0))
        parts += lower
    parts += realise(heads(r.get("parts", []) + r.get("extra", [])))
    if r.get("worn"):
        # Authored the readable way — knuckles along X, striking up +Z, the forearm down -Z,
        # the back of the hand toward +Y — and turned into the hand's convention: the knuckle
        # line along Z (the grip axis, as for anything held), striking forward along +Y, the
        # forearm back along -Y, the back of the hand toward +X.
        m = WORN.to_4x4()
        for o in parts:
            o.matrix_world = m @ o.matrix_world
    return parts, {}


# Worn-on-the-hand authoring frame to the hand's convention: X to Z, Y to X, Z to Y.
WORN = Matrix(((0.0, 1.0, 0.0), (0.0, 0.0, 1.0), (1.0, 0.0, 0.0)))


# --- bows, crossbows, firearms, shields --------------------------------------------------------

def _bow(r):
    """A bow standing up the Z axis, held at its middle (the origin), its back toward -Y and the
    string behind it at +Y — the legacy shortbow's convention, which the bow grip was measured on.

    reach (half-length), belly (how far behind the grip the string lies), thick, recurve (how far
    the ears beyond the string's contact turn forward again, as a fraction of belly; 0 for a
    plain longbow's D), contact (where along the limb the string touches), material,
    belly_material (a composite's horn lamination), ears (the tips' material), thorns,
    grip_material, plus parts."""
    reach, belly, thick = r["reach"], r["belly"], r["thick"]
    rec = r.get("recurve", 0.0)
    c = r.get("contact", 0.80)
    wood = material(r.get("material", "Wood"))

    def centre(t):
        a = abs(t)
        if rec <= 0:
            return belly * a * a
        if a <= c:
            return belly * (a / c) ** 2
        u = (a - c) / (1 - c)
        return belly - belly * rec * u ** 1.4

    steps = 32
    sections = []
    for i in range(steps + 1):
        t = i / steps * 2 - 1
        a = abs(t)
        taper = 1.0 - 0.55 * min(a, c) ** 1.5 / max(c, 1e-3) ** 1.5 * (0.85 if rec > 0 else 1.0)
        if rec > 0 and a > c:
            taper = max(0.42, taper * (1 - 0.3 * (a - c) / (1 - c)))
        sections.append(ellipse(0.0, centre(t), reach * t, thick * 1.25 * taper, thick * 0.78 * taper + 0.004, 8))
    parts = [tag(gg.loft("Bow_Limb", sections, wood), "limb")]
    if r.get("belly_material"):
        for sgn in (-1, 1):
            secs = []
            for i in range(11):
                t = sgn * (0.12 + (c - 0.14) * i / 10)
                taper = 1.0 - 0.5 * abs(t) ** 1.5
                secs.append(ellipse(0.0, centre(t) + thick * 0.55 * taper, reach * t, thick * 1.18 * taper, thick * 0.34 * taper + 0.002, 8))
            parts.append(tag(gg.loft(f"Bow_Belly_{sgn}", secs, material(r["belly_material"])), "limb"))
    ears = material(r.get("ears", "Bone"))
    for sgn in (-1, 1):
        z = reach * sgn
        o = lathe(f"Nock_{sgn}", [(0, thick * 0.7), (0.05, thick * 0.62), (0.09, thick * 0.4), (0.11, 0.0)], ears, 8)
        place(o, (0, 0, 0), (0 if sgn > 0 else 180, 0, 0))
        place(o, (0, centre(1.0), z - 0.06 * sgn))
        parts.append(tag(o, "fitting"))
    ys = belly + thick * 0.25
    if rec > 0:
        pts = [(0, centre(1.0) + thick * 0.2, -reach + 0.03), (0, ys, -reach * c), (0, ys, reach * c), (0, centre(1.0) + thick * 0.2, reach - 0.03)]
    else:
        pts = [(0, ys, -reach + 0.03), (0, ys, reach - 0.03)]
    parts.append(tag(tube("Bow_String", pts, 0.0075, material("Cloth"), 5), "string"))
    parts.append(grip("Bow_Grip", -0.15, 0.15, thick * 1.45, material(r.get("grip_material", "Wrap")), "cord", 6, n=8))
    if r.get("thorns"):
        count = r["thorns"]
        for k in range(count):
            t = lerp(-0.85, 0.85, k / max(1, count - 1))
            if abs(t) < 0.2:
                continue
            z = reach * t
            y = centre(t) - thick * 0.6
            parts.append(tag(spike(f"Thorn_{k}", (0.0, y, z), (0.0, y - 0.06, z + 0.03 * (1 if t > 0 else -1)), 0.013, wood, 5), "edge"))
    parts += realise(heads(r.get("parts", []) + r.get("extra", [])))
    return parts, {"fx": (Vector((0, centre(1.0), -reach)), Vector((0, centre(1.0), reach)))}


def _crossbow(r):
    """A crossbow built standing up, stock along +Z, its top (where the bolt lies) toward -Y,
    and laid forward by the exporter like the legacy light crossbow.

    stock (z0, z1, depth, width), butt (kind: straight | belly | pistol | none), prod (z, span,
    curve, thick, material), stirrup, bolt (True or its length), nut, trigger, plus parts."""
    s0, s1, depth, width = r["stock"]
    wood = material(r.get("stock_material", "Wood"))
    iron = material(r.get("fittings", "DarkIron"))
    parts = []
    sections = []
    for i in range(11):
        t = i / 10
        z = lerp(s0, s1, t)
        d = depth * (1 - 0.35 * t + 0.25 * math.sin(t * math.pi))
        w = width * (1 - 0.2 * t)
        sections.append(box_ring_z(z, w, d, 0.0, 0.35 * depth * (1 - t) ** 2 - 0.2 * depth * t, 12, 0.6))
    parts.append(tag(gg.loft("Stock", sections, wood), "haft"))
    bk = r.get("butt", "straight")
    if bk == "straight":
        parts.append(tag(prism("Butt", [(s0 - 0.06, width * 1.1, depth * 1.4, 0, depth * 0.55), (s0 + 0.15, width * 1.05, depth * 1.25, 0, depth * 0.5)], wood, 12, 0.6), "haft"))
    elif bk == "belly":
        # A gastraphetes's rest: a crescent the archer leans his belly into to span it.
        pts = arc((0.0, s0 - 0.02), 0.16, 200, 340, 10)
        parts.append(tag(tube("Belly_Rest", [(x, 0.0, z + 0.14) for x, z in pts], 0.035, wood, 8, oval=0.6), "haft"))
    elif bk == "pistol":
        pts = [(0, depth * 0.3, s0 + 0.05), (0, depth * 1.4, s0 - 0.04), (0, depth * 2.6, s0 - 0.10)]
        parts.append(tag(tube("Pistol_Grip", spline(pts, 4), lambda u: width * (1.0 - 0.1 * u), wood, 10, oval=0.75), "grip"))
    pz, span, curve, pth = r["prod"]
    prod_m = material(r.get("prod_material", "Wood"))
    for k, off in enumerate(r.get("prods", [0.0])):
        secs = []
        for i in range(13):
            t = i / 12 * 2 - 1
            x = span * t
            z = pz + off - curve * t * t
            taper = 1.0 - 0.55 * abs(t)
            secs.append(ring([(x, -pth * taper - 0.03, z - pth * 0.6 * taper), (x, pth * taper - 0.03, z - pth * 0.6 * taper),
                              (x, pth * taper - 0.03, z + pth * 0.6 * taper), (x, -pth * taper - 0.03, z + pth * 0.6 * taper)]))
        parts.append(tag(crisp(gg.loft(f"Prod_{k}", secs, prod_m), 40), "limb"))
        sz = pz + off - curve + 0.0
        draw = r.get("draw", 0.0)
        parts.append(tag(tube(f"Prod_String_{k}", [(-span, -0.03, sz + 0.01), (0, -0.03, sz - draw), (span, -0.03, sz + 0.01)], 0.008, material("Cloth"), 5), "string"))
        parts.append(tag(prism(f"Prod_Binding_{k}", [(pz + off - pth * 1.4, width * 1.25, depth * 0.9, 0, -0.01), (pz + off + pth * 1.4, width * 1.25, depth * 0.9, 0, -0.01)], iron, 12, 0.5), "fitting"))
    if r.get("stirrup", True):
        rr = r.get("stirrup_r", 0.07)
        parts.append(tag(torus("Stirrup", (0, 0.0, s1 + rr * 0.8), rr, 0.011, iron, plane="XZ", n=14), "fitting"))
    trig = r.get("trigger", True)
    if trig:
        parts.append(tag(prism("Trigger", [(s0 + 0.32, 0.01, 0.012, 0, depth + 0.07), (s0 + 0.52, 0.01, 0.012, 0, depth + 0.02)], iron, 8, 0.5), "fitting"))
        parts.append(tag(lathe("Nut", [(-0.025, 0.0), (-0.024, 0.026), (0.024, 0.026), (0.025, 0.0)], iron, 10), "fitting"))
        place(parts[-1], (0, -depth - 0.005, s0 + 0.6), (0, 90, 0))
    bolt = r.get("bolt", True)
    if bolt:
        L = bolt if isinstance(bolt, float) else (pz - (s0 + 0.6)) + 0.25
        b0 = s0 + 0.6
        for k, off in enumerate(r.get("prods", [0.0]) if r.get("bolts_each") else [0.0]):
            yb = -depth - 0.03 - (0.0 if k == 0 else 0.05)
            parts.append(tag(lathe(f"Bolt_{k}", [(b0 + off, 0.0), (b0 + off + 0.005, 0.012), (b0 + off + L, 0.012), (b0 + off + L + 0.01, 0.022), (b0 + off + L + 0.09, 0.0)], material("Wood"), 6), "bolt"))
            place(parts[-1], (0, yb, 0))
            parts.append(tag(lathe(f"Bolt_Head_{k}", [(b0 + off + L, 0.016), (b0 + off + L + 0.02, 0.022), (b0 + off + L + 0.1, 0.0)], material("Steel"), 6), "edge"))
            place(parts[-1], (0, yb, 0))
            for fk in range(2):
                parts.append(tag(sweep(f"Bolt_Fletch_{k}_{fk}", [(0, b0 + off + 0.02), (0, b0 + off + 0.12)], lambda s: 0.03 * (1 - s * 0.6), 0.0, 0.002, material("Cloth"), (True, True), None, 1), "bolt"))
                place(parts[-1], (0, yb, 0), (0, 0, 90 * fk))
    parts += realise(heads(r.get("parts", []) + r.get("extra", [])))
    tipz = s0 + 0.6 + (bolt if isinstance(bolt, float) else (pz - (s0 + 0.6)) + 0.25) + 0.1 if bolt else pz
    return parts, {"fx": (Vector((0, -depth - 0.03, s0 + 0.6)), Vector((0, -depth - 0.03, tipz))), "pointed": True}


def _firearm(r):
    """A firearm built standing up, muzzle up +Z, its top toward -Y and its trigger and grip
    toward +Y; the exporter lays it forward like a crossbow. The origin is where the firing
    hand closes: on a pistol's grip, on a long gun's wrist.

    barrel (z0, z1, radius[, bore]); barrels (count, spread, arrangement: side | over | ring);
    octagonal; flare (muzzle bell radius); bands; stock: pistol | long | none; stock_len; lock:
    flint | percussion | wheel | none; cylinder (radius, length); guard; plus parts."""
    wood = material(r.get("stock_material", "Wood"))
    iron = material(r.get("metal", "DarkIron"))
    brass = material(r.get("fittings", "Bronze"))
    z0, z1, br = r["barrel"][:3]
    count, spread, arrangement = r.get("barrels", (1, 0.0, "side"))
    yb = -br - 0.02                                       # the barrel's axis, above the stock
    parts = []
    offsets = []
    if count == 1:
        offsets = [(0.0, 0.0)]
    elif arrangement == "side":
        offsets = [(lerp(-spread, spread, k / (count - 1)), 0.0) for k in range(count)]
    elif arrangement == "over":
        offsets = [(0.0, lerp(-spread, spread, k / (count - 1))) for k in range(count)]
    elif arrangement == "fan":
        offsets = [(0.0, 0.0)] * count
    else:
        offsets = [(spread * math.cos(2 * math.pi * k / count), spread * math.sin(2 * math.pi * k / count)) for k in range(count)]
    flare = r.get("flare", 0.0)
    for k, (ox, oy) in enumerate(offsets):
        n = 8 if r.get("octagonal") else 12
        prof = [(z0, br * 1.25), (z0 + 0.06, br * 1.2), (z0 + 0.10, br * 1.08), (lerp(z0, z1, 0.5), br * 1.0)]
        if flare:
            prof += [(z1 - (z1 - z0) * 0.25, br * 0.95), (z1 - 0.05, flare * 0.8), (z1, flare)]
        else:
            prof += [(z1 - 0.04, br * 0.95), (z1 - 0.03, br * 1.12), (z1, br * 1.1)]
        prof += [(z1 + 0.001, br * (0.5 if not flare else flare / br * 0.85)), (z1 - 0.06, 0.0)]
        o = lathe(f"Barrel_{k}", prof, material(r.get("barrel_material", r.get("metal", "DarkIron"))), n, faceted=r.get("octagonal", False), phase=math.pi / 8 if n == 8 else 0.0)
        if arrangement == "fan":
            # A duck's foot: barrels splayed out from one breech.
            a = lerp(-spread, spread, k / max(1, count - 1))
            place(o, (0, 0, -z0))
            place(o, (0, 0, 0), (0, a, 0))
            place(o, (0, yb, z0))
        else:
            place(o, (ox, yb + oy, 0))
        parts.append(tag(o, "head"))
    for bz in r.get("bands", []):
        span_x = max(abs(o[0]) for o in offsets) + br * 1.3
        parts.append(tag(prism(f"Band_{bz}", [(bz - 0.012, span_x, br * 1.3, 0, yb), (bz + 0.012, span_x, br * 1.3, 0, yb)], brass, 12, 0.9), "fitting"))
    stock = r.get("stock", "long")
    if stock == "pistol":
        # The grip drops back and down from the breech, flaring to a capped butt.
        g = r.get("grip_len", 0.42)
        pts = [(0, yb * 0.2, z0 + 0.10), (0, 0.06, z0 - 0.02), (0, 0.18, z0 - g * 0.55), (0, 0.27, z0 - g)]
        parts.append(tag(tube("Grip", spline(pts, 5), lambda u: 0.034 + 0.016 * u ** 2, wood, 10, oval=0.62), "grip"))
        parts.append(tag(lathe("Butt_Cap", [(-0.015, 0.0), (-0.012, 0.044), (0.02, 0.052), (0.03, 0.0)], brass, 12, oval=0.62), "fitting"))
        orient(parts[-1], (0, 0.27, z0 - g), (0, 0.30, -0.25))
        # The stock under the barrel, forward from the breech.
        fe = r.get("forend", 0.45)
        parts.append(tag(prism("Forend", [(z0 - 0.04, 0.034, 0.028, 0, 0.012), (z0 + (z1 - z0) * fe, 0.028, 0.022, 0, 0.0)], wood, 10, 0.8), "haft"))
    elif stock == "long":
        L = r.get("stock_len", 0.95)
        fe = r.get("forend", 0.8)
        secs = [(z0 - L, 0.05, 0.13, 0, 0.13), (z0 - L * 0.7, 0.045, 0.10, 0, 0.09), (z0 - L * 0.35, 0.034, 0.05, 0, 0.04), (z0 - 0.02, 0.036, 0.045, 0, 0.02),
                (z0 + (z1 - z0) * fe * 0.5, 0.036, 0.034, 0, 0.0), (z0 + (z1 - z0) * fe, 0.032, 0.03, 0, -0.005)]
        parts.append(tag(prism("Stock", secs, wood, 12, 0.75), "haft"))
        parts.append(tag(prism("Butt_Plate", [(z0 - L - 0.012, 0.052, 0.135, 0, 0.13), (z0 - L + 0.01, 0.052, 0.135, 0, 0.13)], brass, 12, 0.75), "fitting"))
    lock = r.get("lock", "flint")
    if lock in ("flint", "percussion", "wheel"):
        lz = z0 + 0.02
        parts.append(tag(prism("Lock_Plate", [(lz - 0.08, 0.006, 0.022, 0.03, yb + br * 0.6), (lz + 0.06, 0.006, 0.022, 0.03, yb + br * 0.6)], iron, 8, 0.5), "fitting"))
        cock = [(0.045, yb + 0.03, lz - 0.08), (0.05, yb - 0.01, lz - 0.10), (0.05, yb - 0.05, lz - 0.07), (0.05, yb - 0.06, lz - 0.03)]
        parts.append(tag(tube("Hammer", spline(cock, 3), 0.009, iron, 6), "fitting"))
        if lock == "flint":
            parts.append(tag(prism("Frizzen", [(lz - 0.01, 0.006, 0.012, 0.05, yb - 0.02), (lz + 0.04, 0.006, 0.012, 0.05, yb - 0.04)], iron, 8, 0.5), "fitting"))
        elif lock == "wheel":
            parts.append(tag(lathe("Wheel", [(-0.006, 0.0), (-0.005, 0.03), (0.005, 0.03), (0.006, 0.0)], iron, 12), "fitting"))
            place(parts[-1], (0.045, yb + 0.01, lz - 0.02), (0, 90, 0))
    if r.get("cylinder"):
        cr, cl = r["cylinder"]
        o = lathe("Cylinder", [(z0 + 0.01, 0.0), (z0 + 0.012, cr * 0.9), (z0 + 0.02, cr), (z0 + cl - 0.01, cr), (z0 + cl, cr * 0.9), (z0 + cl + 0.002, 0.0)], iron, 6, faceted=True)
        place(o, (0, yb + br * 0.4, 0))
        parts.append(tag(o, "head"))
    if r.get("guard", True):
        gz = z0 - 0.05
        pts = [(0, 0.0, gz + 0.07), (0, 0.08, gz + 0.06), (0, 0.10, gz - 0.02), (0, 0.05, gz - 0.08), (0, 0.0, gz - 0.10)]
        parts.append(tag(tube("Trigger_Guard", spline(pts, 3), 0.007, brass, 6), "fitting"))
        parts.append(tag(tube("Trigger", spline([(0, 0.0, gz), (0, 0.05, gz - 0.01), (0, 0.065, gz - 0.03)], 3), 0.006, iron, 5), "fitting"))
    parts += realise(heads(r.get("parts", []) + r.get("extra", [])))
    # The origin is where the firing hand closes: the middle of a pistol's grip, a long gun's
    # wrist behind the trigger. Everything moves so that point is at the origin.
    if "hold" in r:
        hold = Vector(r["hold"])
    elif stock == "pistol":
        g = r.get("grip_len", 0.42)
        hold = Vector((0, 0.13, z0 - g * 0.5))
    else:
        hold = Vector((0, 0.05, z0 - 0.16))
    place(parts, tuple(-hold))
    a, b = Vector((0, yb, z0)) - hold, Vector((0, yb, z1)) - hold
    return parts, {"fx": (a, b), "pointed": True}


def _shield(r):
    """A shield, its strap at the origin and its face looking down -Y.

    kind: round (planks, rim and boss) | disc (a plain metal disc) | oval | skull; radius,
    boards, boss (dome | spike | none), spikes (round the face), rim_blades, plus parts."""
    kind = r.get("kind", "round")
    R = r["radius"]
    wood = material(r.get("face_material", "Wood"))
    iron = material(r.get("fittings", "DarkIron"))
    face = (90, 0, 0)
    parts = []
    if kind == "round":
        boards = r.get("boards", 5)
        gap = 0.012
        width = (2 * R - gap * (boards - 1)) / boards
        for i in range(boards):
            x0 = -R + i * (width + gap)
            parts.append(_plank(f"Board_{i}", x0, x0 + width, R * 0.985, 0.07, 0.08, wood))
        parts.append(tag(torus("Rim", (0, 0.0, 0), R, 0.04, iron, plane="XZ", n=28, m=8), "fitting"))
    elif kind in ("disc", "oval"):
        k = r.get("aspect", 1.0)
        dome = r.get("dome", 0.1)
        prof = [(dome, 0.0), (dome * 0.95, R * 0.3), (dome * 0.6, R * 0.72), (0.0, R * 0.97), (-0.012, R), (-0.03, R * 0.96), (-0.035, R * 0.5), (-0.03, 0.0)]
        o = lathe("Face", prof, material(r.get("face_material", "Steel")), 32, k)
        place(o, (0, -0.06, 0), (90, 0, 0))
        parts.append(tag(o, "head" if r.get("sharp_rim") else "fitting"))
    boss = r.get("boss", "dome")
    if boss in ("dome", "spike"):
        b = R * r.get("boss_size", 0.27)
        prof = [(0.0, b), (0.02, b * 0.98), (b * 0.45, b * 0.75), (b * 0.75, b * 0.35), (b * 0.82, 0.0)]
        if boss == "spike":
            prof = [(0.0, b), (0.02, b * 0.98), (b * 0.35, b * 0.62), (b * 0.6, b * 0.32), (r.get("spike", 0.35), 0.0)]
        o = lathe("Boss", prof, iron, 16)
        place(o, (0, -0.07, 0), (90, 0, 0))
        parts.append(tag(o, "edge" if boss == "spike" else "fitting"))
    for k in range(r.get("spikes", 0)):
        a = k * 2 * math.pi / r["spikes"] + 0.3
        base = (R * 0.68 * math.cos(a), -0.08, R * 0.68 * math.sin(a))
        parts.append(tag(spike(f"Spike_{k}", base, (base[0] * 1.08, -0.08 - r.get("spike_len", 0.16), base[2] * 1.08), 0.03, iron, 6), "edge"))
    rivets = r.get("rivets", 8 if kind == "round" else 0)
    for j in range(rivets):
        a = j * 2 * math.pi / rivets + 0.2
        parts.append(tag(lathe(f"Rivet_{j}", [(0, 0.024), (0.01, 0.022), (0.02, 0.0)], iron, 8), "fitting"))
        place(parts[-1], (R * 0.86 * math.cos(a), -0.075, R * 0.86 * math.sin(a)), (90, 0, 0))
    strap = material("Wrap")
    parts.append(tag(prism("Strap", [(-R * 0.5, 0.05, 0.012, 0, 0.085), (R * 0.5, 0.05, 0.012, 0, 0.085)], strap, 8, 0.5), "grip"))
    place(parts[-1], (0, 0, 0), (0, 90, 0))
    parts.append(tag(tube("Handle", [(0, 0.08, -R * 0.25), (0, 0.08, R * 0.25)], 0.022, strap, 8), "grip"))
    parts += realise(heads(r.get("parts", []) + r.get("extra", [])))
    return parts, {}


def _plank(name, x0, x1, radius, thick, dome, m):
    """One board of a round shield: a strip between two chords, bowed forward in the middle."""
    def bow(x):
        return -dome * (1 - (x / radius) ** 2)
    n = 6
    xs = [x0 + (x1 - x0) * i / n for i in range(n + 1)]
    front = [(x, bow(x) - thick / 2, math.sqrt(max(0.0, radius * radius - x * x))) for x in xs]
    front += [(x, bow(x) - thick / 2, -math.sqrt(max(0.0, radius * radius - x * x))) for x in reversed(xs)]
    back = [(x, y + thick, z) for (x, y, z) in front]
    return tag(gg.loft(name, [ring(front), ring(back)], m, smooth_shading=False), "fitting")


BUILDERS = {
    "straight": _straight,
    "curved": _curved,
    "hafted": _hafted,
    "bow": _bow,
    "crossbow": _crossbow,
    "firearm": _firearm,
    "shield": _shield,
}


# --- variants ----------------------------------------------------------------------------------

SPECIAL = {
    "cold-iron": "ColdIron",
    "silver": "Silver",
    "adamantine": "Adamantine",
    "mithral": "Mithral",
}
METALS = ("Steel", "DarkIron")


def rune_line(name, floor, bonus):
    """The mark of an enhanced weapon: a line of small glyphs down the fuller's floor on both
    faces, glowing faintly — brighter for a greater bonus.

    `floor` is [(z, y of the fuller floor, fuller half-width)] from the blade builder. Each
    glyph is a stave down the line and, every other one, a pair of twigs: the shape of a rune
    without being any particular one."""
    mat = material(f"Rune{bonus}")
    parts = []
    if len(floor) < 4:
        return parts
    z_lo, z_hi = floor[0][0] + 0.06, floor[-1][0] - 0.05

    def at(z):
        for (za, ya, fa), (zb, yb, fb) in zip(floor, floor[1:]):
            if za <= z <= zb:
                k = (z - za) / max(zb - za, 1e-6)
                return lerp(ya, yb, k), lerp(fa, fb, k)
        return floor[-1][1], floor[-1][2]

    w = 0.0058
    strokes = []
    glyph = 0.075
    z = z_lo
    i = 0
    while z + glyph < z_hi:
        strokes.append(((0.0, z), (0.0, z + glyph * 0.8)))
        if i % 3 == 0:
            strokes.append(((0.0, z + glyph * 0.55), (0.6, z + glyph * 0.25)))
            strokes.append(((0.0, z + glyph * 0.55), (-0.6, z + glyph * 0.25)))
        elif i % 3 == 1:
            strokes.append(((0.0, z + glyph * 0.15), (0.6, z + glyph * 0.45)))
        else:
            strokes.append(((-0.5, z + glyph * 0.4), (0.5, z + glyph * 0.4)))
        z += glyph
        i += 1
    for side in (1, -1):
        for j, ((xa, za), (xb, zb)) in enumerate(strokes):
            ya, fa = at(za)
            yb, fb = at(zb)
            pa = Vector((xa * fa * 0.8, side * (ya + 0.0025), za))
            pb = Vector((xb * fb * 0.8, side * (yb + 0.0025), zb))
            d = (pb - pa).normalized()
            across = Vector((-d.z, 0.0, d.x)) * w
            sections = [ring([pa - across, pa + across, pa + across + Y * side * 0.002, pa - across + Y * side * 0.002]),
                        ring([pb - across, pb + across, pb + across + Y * side * 0.002, pb - across + Y * side * 0.002])]
            parts.append(tag(gg.loft(f"{name}_{side}_{j}", sections, mat, smooth_shading=False), "rune"))
    return parts


def rune_lines_along(name, fx, bonus):
    """For a weapon with no fuller: the rune line laid along its effects line instead."""
    a, b = fx
    floor = []
    for k in range(12):
        p = a.lerp(b, k / 11)
        floor.append((p.z, 0.02, 0.02))
    return rune_line(name, floor, bonus)


def variant(parts, info, kind):
    """A weapon in a special material, or an enhanced one, made from its plain parts.

    A special material replaces every plain metal on the business end and the fittings; what is
    held (wrap, wood, bone) is left alone. An enhancement (+1 to +5) adds a rune line down the
    fuller, glowing brighter for a greater bonus."""
    if not kind:
        return parts
    if kind.startswith("+"):
        return parts + rune_line("Rune", info.get("fuller_floor", []), int(kind[1:]))
    new = material(SPECIAL[kind])
    for o in parts:
        if o.get("role") in ("edge", "fitting", "head", "chain"):
            for i, m in enumerate(o.data.materials):
                if m is not None and m.name.split(".")[0] in METALS:
                    o.data.materials[i] = new
    return parts


# --- the table ---------------------------------------------------------------------------------

from weapon_recipes import HANDS, LEGACY_FAMILY, RECIPES  # noqa: E402


def _merge(base, over):
    out = copy.deepcopy(base)
    for k, v in over.items():
        if isinstance(v, dict) and isinstance(out.get(k), dict):
            out[k] = _merge(out[k], v)
        else:
            out[k] = copy.deepcopy(v)
    return out


def recipe(weapon):
    """A row with its `like` chain resolved: the base, with this row merged over it all the way
    down, and this row's `extra` parts added to the base's."""
    r = RECIPES[weapon]
    if "like" not in r:
        return copy.deepcopy(r)
    base = recipe(r["like"])
    base.pop("variant", None)
    extra = base.get("extra", []) + r.get("extra", [])
    merged = _merge(base, {k: v for k, v in r.items() if k not in ("like", "extra")})
    merged["extra"] = extra
    return merged


def hands(weapon):
    if weapon in RECIPES:
        return recipe(weapon)["hands"]
    return HANDS.get(weapon, "one")


def family(weapon):
    if weapon in RECIPES:
        return recipe(weapon).get("family", "other")
    return LEGACY_FAMILY.get(weapon, "legacy")


def is_pointed(weapon):
    if weapon not in RECIPES:
        return False
    r = recipe(weapon)
    return r.get("pointed", r.get("build") in ("crossbow", "firearm"))


def is_worn(weapon):
    return weapon in RECIPES and bool(recipe(weapon).get("worn"))


def fx_line(parts, info):
    """Where the effects run: the builder's say, or the business parts' longest extent, from
    the end nearer the hand to the end further from it."""
    if info.get("fx"):
        return info["fx"]
    pts = []
    for o in parts:
        if o.get("role") in FX_ROLES:
            pts += [o.matrix_world @ v.co for v in o.data.vertices]
    if not pts:
        return None
    lo = Vector((min(p[i] for p in pts) for i in range(3)))
    hi = Vector((max(p[i] for p in pts) for i in range(3)))
    size = hi - lo
    axis = max(range(3), key=lambda i: size[i])
    c = (lo + hi) / 2
    a, b = c.copy(), c.copy()
    a[axis], b[axis] = lo[axis], hi[axis]
    return (a, b) if a.length <= b.length else (b, a)


def build(weapon):
    """The parts of one weapon, standing up, grip at the origin, and what its builder knows
    about it: the effects line, the fuller's floor for a rune line, whether it is laid forward."""
    r = recipe(weapon)
    r["id"] = weapon
    parts, info = BUILDERS[r.get("build", "hafted")](r)
    bpy.context.view_layer.update()
    if "fx" in r:
        info["fx"] = (Vector(r["fx"][0]), Vector(r["fx"][1])) if r["fx"] else None
    elif r.get("family") == "shields" and not any(o.get("role") == "edge" for o in parts):
        info["fx"] = None
    else:
        info["fx"] = fx_line(parts, info)
    kind = r.get("variant")
    if kind and kind.startswith("+"):
        if info.get("fuller_floor"):
            parts = parts + rune_line("Rune", info["fuller_floor"], int(kind[1:]))
        elif info["fx"]:
            parts = parts + rune_lines_along("Rune", info["fx"], int(kind[1:]))
    elif kind:
        variant(parts, info, kind)
    info["pointed"] = is_pointed(weapon)
    return parts, info

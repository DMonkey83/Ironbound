"""Named weapons: the top of the loot ladder, built to a higher standard than the catalogue.

Imported by `generate_weapons.py`, which registers the builder and bakes the result; not run
on its own. Each named weapon is a row in `weapon_recipes.py` (family "named", build "named",
`base` the weapon it is in the hand) and a function here.

The owner asked for "way more creative, like epic named weapons", and then said what that means:
"shades, glows, colours, cracks, widths, handle designs". So a named weapon is held to this:

- a bold shape no mundane weapon has (wings, flames, ice, a skull, swans), so that at 64 px it
  is not a longsword;
- three to five materials chosen to contrast, never one grey;
- glow placed where the eye should go — a fuller of scripture, crack veins at an edge, a gem —
  baked into an emission atlas the game reads as KHR_materials_emissive_strength;
- ornament as real geometry: feathers, claws round a faceted gem, wire wound on a grip,
  knotwork standing proud of a face, vertebrae, carved heads.

It is still the base weapon in the hand: about its size, the same grip origin and axes, so the
existing hand bones hold it.

Sculpted pieces (heads, a skull, wing arms) are metaballs converted to a mesh, the technique the
goblins are made with; every metaball gets a name of its own, because Blender fuses metaballs
whose names share a stem. Gradients that run across a blade — temper colours from spine to edge,
a white-gold bevel, lava cracks near the edge — are driven by per-vertex attributes the baked
materials read: `Edge` (0 on the blade's middle line, 1 at its edge), `Along` (0 at the root of
a part, 1 at its end) and `Vein` (1 where blood veins gather).
"""
import math

import bpy
from mathutils import Euler, Matrix, Vector

import generate_goblin as gg
import surface
import weapon_families as wf
import weapon_parts as wp
from weapon_parts import Y, lerp, smoothstep, spline


# --- materials --------------------------------------------------------------------------------
# Each is a flat placeholder named for the bake; its procedural recipe is registered with
# surface.py, and what it emits (Emission Color at strength 1) is baked into the emission atlas.

def _attr(nt, name):
    n = nt.nodes.new("ShaderNodeAttribute")
    n.attribute_name = name
    return n.outputs["Fac"]


def _emit(nt, bsdf, colour):
    surface._plug(nt, bsdf.inputs["Emission Color"], surface._rgba(colour))
    bsdf.inputs["Emission Strength"].default_value = 1.0


def holy_blade(c):
    """Blackened cold iron, its edge bevel white gold: Edge picks the bevel out."""
    m, nt, bsdf = surface._tree("Named_HolyBlade")
    co = surface._coords(nt)
    edge = _attr(nt, "Edge")
    bevel = surface._ramp(nt, edge, [(0.60, 0.0), (0.66, 1.0)])
    iron = surface._shade(nt, (0.040, 0.042, 0.052), surface._span(nt, surface._noise(nt, co, 9, 4, 0.6), 0.75, 1.3))
    gold = surface._shade(nt, (0.98, 0.86, 0.58), surface._span(nt, surface._noise(nt, co, 30, 2), 0.92, 1.05))
    tone = surface._mix(nt, iron, gold, bevel)
    tone = surface._mix(nt, tone, (1.0, 0.97, 0.9), surface._ramp(nt, edge, [(0.93, 0.0), (1.0, 0.6)]))
    rough = surface._mix(nt, surface._span(nt, surface._noise(nt, co, 40, 2), 0.32, 0.5), 0.14, bevel)
    metal = surface._mix(nt, 0.55, 0.72, bevel)
    n = surface._bump(nt, surface._voronoi(nt, co, 18, "F1"), 0.25, 0.01)
    surface._finish(nt, bsdf, tone, rough, n, metal)
    return m


def flame_blade(c):
    """Steel tempered by its own fire: blue-black at the spine, straw, then orange-red at the
    edge, with lava cracks glowing in the hot band near the edge."""
    m, nt, bsdf = surface._tree("Named_FlameBlade")
    co = surface._coords(nt)
    edge = _attr(nt, "Edge")
    temper = surface._ramp(nt, edge, [(0.0, (0.020, 0.028, 0.06)), (0.32, (0.06, 0.06, 0.08)), (0.58, (0.30, 0.20, 0.10)),
                                      (0.78, (0.62, 0.24, 0.05)), (0.93, (0.85, 0.14, 0.03)), (1.0, (1.0, 0.45, 0.08))])
    tone = surface._shade(nt, temper, surface._span(nt, surface._noise(nt, co, 14, 3), 0.8, 1.15))
    cracks = surface._ramp(nt, surface._voronoi(nt, co, 26), [(0.0, 1.0), (0.05, 0.0)])
    near = surface._ramp(nt, edge, [(0.55, 0.0), (0.78, 1.0)])
    patchy = surface._ramp(nt, surface._noise(nt, co, 6, 3), [(0.40, 0.0), (0.55, 1.0)])
    veins = surface._math(nt, "MULTIPLY", surface._math(nt, "MULTIPLY", cracks, near), patchy)
    hot = surface._ramp(nt, edge, [(0.90, 0.0), (1.0, 0.45)])
    glow = surface._math(nt, "ADD", veins, hot)
    tone = surface._mix(nt, tone, (0.05, 0.01, 0.0), surface._math(nt, "MULTIPLY", veins, 0.8))
    _emit(nt, bsdf, surface._mix(nt, (0, 0, 0), (0.45, 0.12, 0.015), glow))
    rough = surface._span(nt, surface._noise(nt, co, 40, 2), 0.25, 0.42)
    n = surface._bump(nt, cracks, 0.35, 0.004)
    surface._finish(nt, bsdf, tone, rough, n, 0.55)
    return m


def ember_iron(c):
    """Blackened iron whose tips (Along near 1) are glowing like a coal."""
    m, nt, bsdf = surface._tree("Named_EmberIron")
    co = surface._coords(nt)
    along = _attr(nt, "Along")
    tone = surface._shade(nt, (0.045, 0.035, 0.032), surface._span(nt, surface._noise(nt, co, 12, 3), 0.8, 1.25))
    hot = surface._ramp(nt, along, [(0.30, 0.0), (0.70, 0.55), (1.0, 1.0)])
    tone = surface._mix(nt, tone, (0.55, 0.10, 0.01), hot)
    _emit(nt, bsdf, surface._mix(nt, (0, 0, 0), (0.42, 0.10, 0.01), hot))
    surface._finish(nt, bsdf, tone, surface._span(nt, surface._noise(nt, co, 30, 2), 0.35, 0.6),
                    surface._bump(nt, surface._voronoi(nt, co, 20, "F1"), 0.3, 0.01), 0.5)
    return m


def frost_steel(c):
    """Pale blue-white steel, rimed with frost toward the guard and along its edges."""
    m, nt, bsdf = surface._tree("Named_FrostSteel")
    co = surface._coords(nt)
    edge = _attr(nt, "Edge")
    along = _attr(nt, "Along")
    tone = surface._shade(nt, (0.50, 0.66, 0.86), surface._span(nt, surface._noise(nt, co, 12, 3), 0.9, 1.06))
    crystals = surface._ramp(nt, surface._voronoi(nt, co, 55, "DISTANCE_TO_EDGE"), [(0.0, 1.0), (0.08, 0.0)])
    frost = surface._math(nt, "MAXIMUM", surface._ramp(nt, along, [(0.0, 1.0), (0.4, 0.0)]), surface._ramp(nt, edge, [(0.8, 0.0), (1.0, 0.8)]))
    frost = surface._math(nt, "MULTIPLY", frost, surface._ramp(nt, surface._noise(nt, co, 9, 3), [(0.35, 0.3), (0.6, 1.0)]))
    rime = surface._math(nt, "MULTIPLY", frost, surface._math(nt, "ADD", crystals, 0.35), clamp=True)
    tone = surface._mix(nt, tone, (0.95, 0.98, 1.0), rime)
    rough = surface._mix(nt, 0.18, 0.7, rime)
    _emit(nt, bsdf, surface._mix(nt, (0, 0, 0), (0.06, 0.32, 0.5), surface._ramp(nt, edge, [(0.94, 0.0), (1.0, 0.6)])))
    n = surface._bump(nt, crystals, 0.3, 0.004)
    surface._finish(nt, bsdf, tone, rough, n, 0.5)
    return m


def ice(c):
    """Ice that glows from within: pale cyan, glassy, brightest toward each crystal's tip."""
    m, nt, bsdf = surface._tree("Named_Ice")
    co = surface._coords(nt)
    along = _attr(nt, "Along")
    tone = surface._shade(nt, (0.30, 0.72, 0.95), surface._span(nt, surface._noise(nt, co, 20, 3), 0.85, 1.1))
    _emit(nt, bsdf, surface._mix(nt, (0.01, 0.07, 0.12), (0.08, 0.40, 0.62), surface._ramp(nt, along, [(0.0, 0.0), (1.0, 1.0)])))
    surface._finish(nt, bsdf, tone, 0.06, surface._bump(nt, surface._voronoi(nt, co, 30, "F1"), 0.2, 0.006), 0.0)
    return m


def black_iron(c):
    """The Life-Drinker's iron: near black, pitted, a dull red in it, veined with blood where the
    veins gather (Vein)."""
    m, nt, bsdf = surface._tree("Named_BlackIron")
    co = surface._coords(nt)
    vein = _attr(nt, "Vein")
    tone = surface._shade(nt, (0.035, 0.028, 0.028), surface._span(nt, surface._noise(nt, co, 10, 4, 0.6), 0.7, 1.35))
    pits = surface._ramp(nt, surface._voronoi(nt, co, 60, "F1"), [(0.0, 1.0), (0.06, 0.0)])
    cracks = surface._ramp(nt, surface._voronoi(nt, co, 14), [(0.0, 1.0), (0.018, 0.0)])
    veins = surface._math(nt, "MULTIPLY", cracks, surface._ramp(nt, vein, [(0.35, 0.0), (0.9, 1.0)]))
    tone = surface._mix(nt, tone, (0.20, 0.0, 0.0), veins)
    edge = surface._edges(nt, 3.0)
    tone = surface._mix(nt, tone, (0.22, 0.20, 0.20), surface._math(nt, "MULTIPLY", edge, 0.5))
    _emit(nt, bsdf, surface._mix(nt, (0, 0, 0), (0.30, 0.0, 0.0), veins))
    rough = surface._span(nt, surface._noise(nt, co, 30, 2), 0.32, 0.6)
    n = surface._bump(nt, pits, 0.4, 0.004)
    n = surface._bump(nt, cracks, 0.3, 0.006, n)
    surface._finish(nt, bsdf, tone, rough, n, 0.55)
    return m


def glowing(colour, glow, rough=0.3, metal=0.0, name="Glow"):
    def recipe(c):
        m, nt, bsdf = surface._tree(f"Named_{name}")
        co = surface._coords(nt)
        tone = surface._shade(nt, colour, surface._span(nt, surface._noise(nt, co, 25, 2), 0.85, 1.08))
        _emit(nt, bsdf, glow)
        surface._finish(nt, bsdf, tone, rough, None, metal)
        return m
    return recipe


def gem(colour, glow, name="Gem"):
    """A cut stone: glassy, its colour deepening into the facets, a light inside."""
    def recipe(c):
        m, nt, bsdf = surface._tree(f"Named_{name}")
        co = surface._coords(nt)
        depth = surface._ramp(nt, surface._edges(nt, 4.0), [(0.0, 0.6), (1.0, 1.35)])
        tone = surface._shade(nt, colour, depth)
        _emit(nt, bsdf, surface._mix(nt, (0, 0, 0), glow, surface._span(nt, surface._noise(nt, co, 40, 2), 0.7, 1.0)))
        surface._finish(nt, bsdf, tone, 0.04, None, 0.0)
        return m
    return recipe


def gilt(colour):
    """Gilding, bright, a little worn back to bronze on its edges."""
    m, nt, bsdf = surface._tree("Named_Gilt")
    co = surface._coords(nt)
    tone = surface._shade(nt, colour, surface._span(nt, surface._noise(nt, co, 18, 3), 0.85, 1.08))
    worn = surface._math(nt, "MULTIPLY", surface._edges(nt, 3.0), surface._ramp(nt, surface._noise(nt, co, 22, 3), [(0.45, 0.0), (0.6, 1.0)]))
    tone = surface._mix(nt, tone, (0.45, 0.25, 0.10), surface._math(nt, "MULTIPLY", worn, 0.7))
    n = surface._bump(nt, surface._noise(nt, co, 60, 3), 0.12, 0.003)
    surface._finish(nt, bsdf, tone, surface._span(nt, worn, 0.22, 0.45), n, 0.7)
    return m


NAMED_MATERIALS = {
    # name: (flat colour, metallic, roughness, recipe)
    "HolyBlade": ((0.05, 0.05, 0.06), 0.6, 0.35, holy_blade),
    "HolyRune": ((0.95, 0.75, 0.35), 0.6, 0.3, glowing((0.95, 0.72, 0.30), (0.55, 0.36, 0.10), 0.25, 0.6, "HolyRune")),
    "Gilt": ((0.95, 0.70, 0.28), 0.7, 0.3, gilt),
    "WhiteLeather": ((0.80, 0.76, 0.68), 0.0, 0.7, lambda c: surface.leather(c)),
    "ClearCrystal": ((0.86, 0.93, 1.0), 0.0, 0.04, gem((0.86, 0.93, 1.0), (0.32, 0.28, 0.20), "ClearCrystal")),
    "FlameBlade": ((0.4, 0.2, 0.1), 0.55, 0.35, flame_blade),
    "EmberIron": ((0.05, 0.04, 0.04), 0.5, 0.5, ember_iron),
    "CharredLeather": ((0.045, 0.032, 0.028), 0.0, 0.85, lambda c: surface.leather(c)),
    "Ruby": ((0.55, 0.02, 0.04), 0.0, 0.04, gem((0.60, 0.02, 0.04), (0.40, 0.02, 0.01), "Ruby")),
    "FrostSteel": ((0.7, 0.8, 0.92), 0.5, 0.25, frost_steel),
    "Ice": ((0.62, 0.88, 1.0), 0.0, 0.06, ice),
    "FrostRune": ((0.2, 0.6, 0.85), 0.2, 0.3, glowing((0.20, 0.55, 0.85), (0.06, 0.38, 0.55), 0.2, 0.2, "FrostRune")),
    "FrostWrap": ((0.30, 0.40, 0.52), 0.0, 0.8, lambda c: surface.leather(c)),
    "WhiteCord": ((0.85, 0.88, 0.90), 0.0, 0.9, lambda c: surface.cloth(c)),
    "BrightSilver": ((0.88, 0.92, 0.98), 0.4, 0.1, wf._silver),
    "BlackIron": ((0.04, 0.03, 0.03), 0.55, 0.45, black_iron),
    "BloodGroove": ((0.12, 0.0, 0.0), 0.3, 0.3, glowing((0.12, 0.0, 0.0), (0.16, 0.0, 0.0), 0.3, 0.3, "BloodGroove")),
    "OldBone": ((0.66, 0.58, 0.42), 0.0, 0.55, lambda c: surface.bone(c)),
    "BloodLeather": ((0.20, 0.025, 0.025), 0.0, 0.8, lambda c: surface.leather(c)),
    "DarkWood": ((0.17, 0.09, 0.045), 0.0, 0.7, lambda c: surface.wood(c)),
    "DwarfSteel": ((0.62, 0.63, 0.66), 0.5, 0.3, lambda c: surface.steel(c, 0.6)),
    "EtchedIron": ((0.07, 0.065, 0.06), 0.45, 0.55, lambda c: surface.iron(c, 0.05)),
    "BlueRune": ((0.85, 0.65, 0.25), 0.6, 0.3, glowing((0.85, 0.62, 0.22), (0.16, 0.34, 0.62), 0.25, 0.6, "BlueRune")),
    "NamedGold": ((0.90, 0.66, 0.22), 0.7, 0.3, gilt),
    "BraidLeather": ((0.42, 0.20, 0.06), 0.0, 0.8, lambda c: surface.leather(c)),
    "PaleAsh": ((0.80, 0.70, 0.53), 0.0, 0.6, lambda c: surface.wood(c, (0.58, 0.47, 0.33))),
    "GreenLeather": ((0.05, 0.20, 0.09), 0.0, 0.8, lambda c: surface.leather(c)),
    "Emerald": ((0.02, 0.40, 0.15), 0.0, 0.04, gem((0.03, 0.42, 0.16), (0.03, 0.42, 0.12), "Emerald")),
    "Oathstring": ((0.92, 0.92, 0.82), 0.0, 0.5, glowing((0.92, 0.92, 0.82), (0.36, 0.48, 0.30), 0.5, 0.0, "Oathstring")),
}

for _n, (_c, _m, _r, _rec) in NAMED_MATERIALS.items():
    surface.RECIPES[_n] = (lambda rec: lambda c, mm, rr: rec(c))(_rec)


def M(name):
    if name in NAMED_MATERIALS:
        c, metal, rough, _ = NAMED_MATERIALS[name]
        return gg.mat(name, c, metal, rough)
    return wf.material(name)


# --- small tools --------------------------------------------------------------------------------

_count = [0]


def uniq(stem):
    """A name with no dot in it: metaballs whose names share a stem melt into one another."""
    _count[0] += 1
    return f"{stem}{_count[0]}"


def tag(objs, role):
    for o in (objs if isinstance(objs, list) else [objs]):
        o["role"] = role
    return objs


def set_attr(obj, name, fn):
    """A per-vertex value the baked materials read, from each vertex's position."""
    me = obj.data
    a = me.attributes.get(name) or me.attributes.new(name, "FLOAT", "POINT")
    mw = obj.matrix_world
    vals = [fn(mw @ v.co) for v in me.vertices]
    a.data.foreach_set("value", vals)
    return obj


def along_attr(obj, a, b):
    """Along: 0 at point a, 1 at point b, by projection."""
    a, b = Vector(a), Vector(b)
    d = b - a
    L2 = max(d.length_squared, 1e-9)
    return set_attr(obj, "Along", lambda p: max(0.0, min(1.0, (p - a).dot(d) / L2)))


def blade_attrs(obj, b):
    """Edge (0 down the middle, 1 at the edges) and Along for a straight blade."""
    z0, z1 = b["base"], b["tip"]

    def edge(p):
        s = max(0.0, min(0.999, (p.z - z0) / (z1 - z0)))
        w = max(wp.blade_width(s, b), 1e-4)
        return min(1.0, abs(p.x) / w)
    set_attr(obj, "Edge", edge)
    set_attr(obj, "Along", lambda p: max(0.0, min(1.0, (p.z - z0) / (z1 - z0))))
    return obj


def at_angle(obj, root, degrees):
    """A part built up +Z in the XZ plane, swung about Y to `degrees` (from +Z toward +X) and set
    at `root`: the flat stays facing Y."""
    wp.place(obj, (0, 0, 0), (0, degrees, 0))
    wp.place(obj, tuple(wp.V(root)))
    return obj


def sculpt(stem, elements, material, res=0.006, decimate=0.45):
    """Metaballs to a mesh: elements are dicts of co, r (a radius or three), neg, rot (degrees)."""
    name = uniq(stem)
    mb = bpy.data.metaballs.new(name)
    mb.resolution = res
    mb.render_resolution = res
    mb.threshold = 0.6
    ob = bpy.data.objects.new(name, mb)
    bpy.context.collection.objects.link(ob)
    for el in elements:
        e = mb.elements.new(type="ELLIPSOID")
        e.co = Vector(el["co"])
        r = el["r"]
        r = (r, r, r) if isinstance(r, (int, float)) else r
        e.size_x, e.size_y, e.size_z = (v * 1.55 for v in r)
        e.radius = 1.0
        e.stiffness = el.get("stiff", 2.0)
        e.use_negative = el.get("neg", False)
        if el.get("rot"):
            e.rotation = Euler([math.radians(a) for a in el["rot"]]).to_quaternion()
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.convert(target="MESH")
    obj = bpy.context.view_layer.objects.active
    obj.name = name + "Mesh"
    for p in obj.data.polygons:
        p.use_smooth = True
    obj.data.materials.clear()
    obj.data.materials.append(material)
    if decimate and len(obj.data.polygons) > 400:
        mod = obj.modifiers.new("Thin", "DECIMATE")
        mod.ratio = decimate
        bpy.ops.object.modifier_apply(modifier="Thin")
    obj["role"] = "fitting"
    return obj


def chain_balls(points, radii, per=3):
    pts = spline(points, per)
    out = []
    for i, p in enumerate(pts):
        u = i / max(1, len(pts) - 1)
        out.append(dict(co=p, r=lerp(radii[0], radii[1], u)))
    return out


# --- ornament -------------------------------------------------------------------------------------

def brilliant(name, centre, r, h, material, axis=(0, 0, 1), n=8, roll=0.0):
    """A brilliant-cut stone: a table, a crown down to the girdle, a pavilion to a point."""
    prof = [(h * 0.40, 0.0), (h * 0.40, r * 0.56), (h * 0.14, r), (0.0, r), (-h * 0.62, 0.0)]
    o = wp.lathe(name, prof, material, n, faceted=True, phase=math.pi / n)
    wp.orient(o, centre, axis, roll)
    o["role"] = "fitting"
    return o


def cabochon(name, centre, r, h, material, axis=(0, 0, 1), n=16):
    prof = [(-0.004, 0.0), (-0.002, r), (h * 0.35, r * 0.88), (h * 0.7, r * 0.55), (h, 0.0)]
    o = wp.lathe(name, prof, material, n)
    wp.orient(o, centre, axis)
    o["role"] = "fitting"
    return o


def crystal(name, base, tip, r, material, n=6, flat_tip=False):
    """A long hexagonal crystal from base to tip, pointed; Along runs base to tip."""
    base, tip = Vector(base), Vector(tip)
    L = (tip - base).length
    prof = [(-L * 0.06, 0.0), (0.0, r * 0.85), (L * 0.1, r), (L * 0.72, r * 0.92), (L, 0.0 if not flat_tip else r * 0.4)]
    o = wp.lathe(name, prof, material, n, faceted=True, phase=math.pi / n)
    wp.orient(o, base, tip - base)
    along_attr(o, base, tip)
    o["role"] = "edge"
    return o


def claws(name, top, centre, r, count, material, reach=1.15, thick=0.009):
    """Claws curling down from a cap over a stone, each a tapering hooked tube."""
    out = []
    for k in range(count):
        a = 2 * math.pi * k / count + math.pi / count
        d = Vector((math.cos(a), math.sin(a), 0.0))
        c = Vector(centre)
        pts = [Vector(top) + d * r * 0.55, c + d * r * reach + Vector((0, 0, r * 0.55)), c + d * r * reach * 1.02, c + d * r * 0.85 - Vector((0, 0, r * 0.55))]
        out.append(wp.tube(f"{name}_{k}", spline(pts, 4), lambda u: thick * (1.3 - 0.9 * u), material, 6))
    return tag(out, "fitting")


def wire_wrap(name, z0, z1, r, turns, material, wire=0.0045, both=True, oval=0.86):
    """Wire wound on a grip, and if `both`, wound back over itself into a lattice."""
    out = []
    for sgn in ((1, -1) if both else (1,)):
        pts = []
        steps = int(turns * 16)
        for k in range(steps + 1):
            t = k / steps
            a = sgn * 2 * math.pi * turns * t
            pts.append((r * math.cos(a), r * oval * math.sin(a), lerp(z0, z1, t)))
        out.append(wp.tube(f"{name}_{sgn}", pts, wire, material, 4))
    return tag(out, "grip")


def feather(name, root, angle, length, width, material, curl=0.15, thick=0.007):
    """One feather in the blade's plane: a vane each side of a raised quill, a fine point, a
    gentle curl; Along runs root to tip."""
    path = [(0.0, 0.0), (curl * length * 0.2, length * 0.45), (curl * length, length)]
    w = lambda s: width * math.sin(math.pi * min(1.0, 0.12 + s * 0.95)) ** 0.55 * (1 - 0.25 * s) + 0.002
    o = wp.sweep(name, path, w, w, lambda s: thick * (1 - 0.6 * s) + 0.0015, material, (True, True), "point", 4, 0.62)
    at_angle(o, root, angle)
    tip = wp.V(root) + Vector((math.sin(math.radians(angle)), 0, math.cos(math.radians(angle)))) * length
    along_attr(o, wp.V(root), tip)
    o["role"] = "fitting"
    return o


def wing(side, root, material, span=0.34, rise=0.16, layer_y=0.012, scale=1.0):
    """A spread angel's wing in the blade's plane: a sculpted arm sweeping out and up, primaries
    fanning from it, a row of shorter coverts over their roots."""
    sx = side
    arm = [Vector((root[0], 0, root[1])), Vector((root[0] + sx * span * 0.35, 0, root[1] + rise * 0.30)),
           Vector((root[0] + sx * span * 0.72, 0, root[1] + rise * 0.72)), Vector((root[0] + sx * span, 0, root[1] + rise))]
    parts = [sculpt("WingArm", chain_balls(arm, (0.026 * scale, 0.012 * scale), 4), material, 0.005, 0.5)]
    pts = spline(arm, 6)
    n = 13
    for k in range(n):
        u = 0.14 + 0.86 * k / (n - 1)
        p = pts[int(u * (len(pts) - 1))]
        ang = lerp(152, 40, u) if sx > 0 else lerp(-152, -40, u)       # from hanging to swept out and up
        L = lerp(0.11, 0.27, u ** 1.1) * scale
        parts.append(feather(uniq("Primary"), (p.x, layer_y * (k % 2) - layer_y / 2, p.z), ang, L, 0.030 * scale, material, curl=-0.14 * sx))
    for k in range(9):
        u = 0.08 + 0.84 * k / 8
        p = pts[int(u * (len(pts) - 1))]
        ang = lerp(160, 95, u) if sx > 0 else lerp(-160, -95, u)
        parts.append(feather(uniq("Covert"), (p.x, layer_y * 1.6, p.z + 0.005), ang, lerp(0.06, 0.10, u) * scale, 0.02 * scale, material, curl=-0.1 * sx))
        parts.append(feather(uniq("Covert"), (p.x, -layer_y * 1.6, p.z + 0.005), ang, lerp(0.06, 0.10, u) * scale, 0.02 * scale, material, curl=-0.1 * sx))
    return parts


def flame(name, path, w0, material, thick=0.012, per=5):
    """A tongue of flame: a tapering S-curve, sharp both sides; Along runs root to tip."""
    o = wp.sweep(name, path, lambda s: w0 * (1 - s) ** 0.8 + 0.002, lambda s: w0 * (1 - s) ** 0.8 + 0.002, lambda s: thick * (1 - 0.7 * s) + 0.002,
                 material, (True, True), "point", per, 0.5)
    pts = spline(path, per)
    lengths = [0.0]
    for a, b in zip(pts, pts[1:]):
        lengths.append(lengths[-1] + (b - a).length)

    def along(p):
        best = min(range(len(pts)), key=lambda i: (pts[i] - p).length_squared)
        return lengths[best] / lengths[-1]
    set_attr(o, "Along", along)
    o["role"] = "fitting"
    return o


def glyphs(name, origin, along, up, normal, length, height, material, width=0.0045, raise_=0.0022, glyph=None, seed=0):
    """A line of glyphs standing proud of a face: staves with twigs and bars, like script."""
    origin, along, up, normal = Vector(origin), Vector(along).normalized(), Vector(up).normalized(), Vector(normal).normalized()
    glyph = glyph or height * 0.75
    shapes = [[((0.5, 0), (0.5, 1))], [((0.5, 0), (0.5, 1)), ((0.5, 0.65), (1.0, 0.35))], [((0, 0), (0.5, 1)), ((0.5, 1), (1.0, 0))],
              [((0.5, 0), (0.5, 1)), ((0.0, 0.5), (1.0, 0.5))], [((0.2, 0), (0.2, 1)), ((0.2, 1), (0.9, 0.6)), ((0.9, 0.6), (0.2, 0.3))],
              [((0.5, 0), (0.5, 1)), ((0.5, 0.3), (0.0, 0.0)), ((0.5, 0.3), (1.0, 0.0))], [((0, 1), (1, 0)), ((0, 0), (1, 1))],
              [((0.3, 0), (0.3, 1)), ((0.7, 0), (0.7, 1)), ((0.3, 0.5), (0.7, 0.5))]]
    out = []
    count = int(length / (glyph * 1.25))
    for k in range(count):
        shape = shapes[(k * 5 + seed * 3 + k // 3) % len(shapes)]
        x0 = k * glyph * 1.25
        for j, ((ua, va), (ub, vb)) in enumerate(shape):
            pa = origin + along * (x0 + ua * glyph * 0.8) + up * ((va - 0.5) * height)
            pb = origin + along * (x0 + ub * glyph * 0.8) + up * ((vb - 0.5) * height)
            d = (pb - pa)
            if d.length < 1e-6:
                continue
            side = d.normalized().cross(normal) * width
            secs = [[pa - side, pa + side, pa + side + normal * raise_, pa - side + normal * raise_],
                    [pb - side, pb + side, pb + side + normal * raise_, pb - side + normal * raise_]]
            o = gg.loft(f"{name}_{k}_{j}", secs, material, smooth_shading=False)
            o["role"] = "rune"
            out.append(o)
    return out


def fuller_glyphs(name, floor, material, glyph=0.05, width=0.0042, seed=0):
    """Glyphs down a blade's fuller on both faces, following the floor's depth."""
    out = []
    if len(floor) < 3:
        return out
    z_lo, z_hi = floor[0][0] + 0.04, floor[-1][0] - 0.04

    def at(z):
        for (za, ya, fa), (zb, yb, fb) in zip(floor, floor[1:]):
            if za <= z <= zb:
                k = (z - za) / max(zb - za, 1e-6)
                return lerp(ya, yb, k), lerp(fa, fb, k)
        return floor[-1][1], floor[-1][2]
    shapes = [[((0, -0.6), (0, 0.6))], [((0, -0.6), (0, 0.6)), ((0, 0.2), (0.7, -0.2))], [((0, -0.6), (0, 0.6)), ((0, 0.1), (-0.7, -0.3)), ((0, 0.1), (0.7, -0.3))],
              [((-0.7, 0.5), (0.7, -0.5)), ((-0.7, -0.5), (0.7, 0.5))], [((0, -0.6), (0, 0.6)), ((-0.6, 0), (0.6, 0))], [((-0.5, -0.6), (-0.5, 0.6)), ((-0.5, 0.6), (0.6, 0.1)), ((0.6, 0.1), (-0.5, -0.1))]]
    z = z_lo
    k = 0
    while z + glyph < z_hi:
        shape = shapes[(k * 7 + seed) % len(shapes)]
        zc = z + glyph * 0.5
        for sgn in (1, -1):
            for j, ((xa, va), (xb, vb)) in enumerate(shape):
                za_, zb_ = zc + va * glyph * 0.42, zc + vb * glyph * 0.42
                ya, fa = at(za_)
                yb, fb = at(zb_)
                pa = Vector((xa * fa * 0.75, sgn * (ya + 0.0018), za_))
                pb = Vector((xb * fb * 0.75, sgn * (yb + 0.0018), zb_))
                d = pb - pa
                if d.length < 1e-6:
                    continue
                side = Vector((-d.z, 0.0, d.x)).normalized() * width
                n = Vector((0, sgn * 0.0022, 0))
                o = gg.loft(f"{name}_{k}_{sgn}_{j}", [[pa - side, pa + side, pa + side + n, pa - side + n], [pb - side, pb + side, pb + side + n, pb - side + n]],
                            material, smooth_shading=False)
                o["role"] = "rune"
                out.append(o)
        z += glyph
        k += 1
    return out


def knot(name, centre, rx, rz, face_y, material, r=0.0055, lobes=3):
    """Interlaced knotwork standing proud of a face (the face at y = face_y, facing its sign):
    a closed trefoil-like curve whose depth rises and falls so the strands pass over and under."""
    c = Vector(centre)
    sgn = 1 if face_y >= 0 else -1
    pts = []
    n = 90
    for k in range(n):
        t = 2 * math.pi * k / n
        x = math.sin(t) + 2 * math.sin((lobes - 1) * t)
        z = math.cos(t) - 2 * math.cos((lobes - 1) * t)
        h = 0.5 + 0.5 * math.sin(lobes * t)
        pts.append(Vector((c.x + x * rx / 3.0, face_y + sgn * (0.003 + h * 0.006), c.z + z * rz / 3.0)))
    o = wp.tube(name, pts, r, material, 5, closed=True)
    o["role"] = "fitting"
    return o


def vine(name, points, material, r=0.004, leaves=10, leaf=0.035, face=-1):
    """Leaf filigree: a wire vine along a path with small leaves sprouting alternately."""
    pts = spline(points, 6)
    out = [wp.tube(name, pts, r, material, 4)]
    for k in range(leaves):
        i = int((k + 0.5) / leaves * (len(pts) - 2))
        p, q = pts[i], pts[i + 1]
        d = (q - p).normalized()
        side = 1 if k % 2 == 0 else -1
        outv = Vector((side, 0, 0)) - d * d.x * side
        if outv.length < 1e-4:
            continue
        outv.normalize()
        tip = p + (outv * 0.8 + d * 0.6).normalized() * leaf
        lp = [p, p.lerp(tip, 0.5) + outv * leaf * 0.12, tip]
        w = lambda s: leaf * 0.32 * math.sin(math.pi * s) ** 0.7 + 0.001
        lf = wp.sweep(f"{name}_Leaf{k}", lp, w, w, 0.0025, material, (True, True), "point", 3, 0.6)
        out.append(lf)
    return tag(out, "fitting")


# --- the six -----------------------------------------------------------------------------------

def holy_avenger(r):
    """Holy Avenger: a knightly longsword of blackened cold iron with a white-gold bevel, gold
    scripture glowing down its fuller, gilt angel's wings for a guard, white leather bound with
    gold wire, a sunburst round a crystal for a pommel."""
    blade_m, rune_m, gilt_m = M("HolyBlade"), M("HolyRune"), M("Gilt")
    b = dict(base=0.235, tip=2.08, width=0.080, thick=0.020, section="hex", flat=0.56, taper=0.40, point=0.12, curve=0.75,
             fuller=(0.0, 0.74, 0.026, 0.55))
    blade, floor = wp.straight_blade("Blade", b, blade_m)
    blade_attrs(blade, b)
    parts = [blade]
    parts += fuller_glyphs("Scripture", floor, rune_m, 0.058, 0.0052)
    # Gold scrollwork on both flats at the root of the blade, either side of the fuller.
    for sgn in (1, -1):
        for sx in (1, -1):
            pts = [(sx * (0.030 + 0.012 * math.sin(k * 1.3)), sgn * 0.0215, 0.27 + k * 0.03) for k in range(7)]
            parts += vine(uniq("Scroll"), pts, gilt_m, 0.0035, 5, 0.022)
    # The guard: a gilt boss with a crystal, and the wings.
    parts.append(tag(wp.prism("GuardBoss", [(0.185, 0.045, 0.032), (0.20, 0.06, 0.04), (0.245, 0.06, 0.04), (0.27, 0.035, 0.03)], gilt_m, 12, 0.6), "fitting"))
    for sgn in (1, -1):
        parts.append(cabochon(uniq("BossStone"), (0, sgn * 0.040, 0.225), 0.018, 0.012, M("ClearCrystal"), (0, sgn, 0)))
    for side in (1, -1):
        parts += wing(side, (side * 0.045, 0.225), gilt_m, span=0.33, rise=0.17)
    # The grip: white leather and a lattice of gold wire, gilt ferrules.
    parts.append(wp.grip("Grip", -0.245, 0.185, 0.034, M("WhiteLeather"), "plain", 1, "barrel"))
    parts += wire_wrap("GoldWire", -0.235, 0.175, 0.0365, 5.5, gilt_m)
    for z in (-0.25, 0.18):
        parts.append(tag(wp.lathe(uniq("Ferrule"), [(z - 0.012, 0.036), (z - 0.006, 0.041), (z + 0.006, 0.041), (z + 0.012, 0.036)], gilt_m, 12), "fitting"))
    # The pommel: a sunburst — a disc, long and short rays — round a clear crystal on each face.
    zc = -0.32
    disc = wp.lathe("SunDisc", [(-0.026, 0.0), (-0.026, 0.035), (-0.02, 0.058), (-0.012, 0.064), (0.012, 0.064), (0.02, 0.058), (0.026, 0.035), (0.026, 0.0)], gilt_m, 24)
    wp.place(disc, (0, 0, 0), (90, 0, 0))
    wp.place(disc, (0, 0, zc))
    parts.append(tag(disc, "fitting"))
    for k in range(16):
        a = 2 * math.pi * k / 16 + math.pi / 16
        long_ = k % 2 == 0
        L = 0.075 if long_ else 0.045
        ray = wp.sweep(uniq("Ray"), [(0.0, 0.0), (0.0, L)], lambda s: 0.014 * (1 - s) + 0.001, lambda s: 0.014 * (1 - s) + 0.001, 0.010, gilt_m, (True, True), "point", 2, 0.6)
        at_angle(ray, (0.058 * math.sin(a), zc + 0.058 * math.cos(a)), math.degrees(a))
        parts.append(tag(ray, "fitting"))
    for sgn in (1, -1):
        parts.append(brilliant(uniq("SunStone"), (0, sgn * 0.024, zc), 0.036, 0.04, M("ClearCrystal"), (0, sgn, 0), 10))
        parts.append(tag(wp.torus(uniq("Bezel"), (0, sgn * 0.027, zc), 0.037, 0.005, gilt_m, plane="XZ", n=20, m=5), "fitting"))
    parts.append(tag(wp.lathe("Button", [(zc - 0.06, 0.016), (zc - 0.072, 0.02), (zc - 0.09, 0.0)], gilt_m, 8, faceted=True), "fitting"))
    return parts, {"fx": (Vector((0, 0, b["base"])), Vector((0, 0, b["tip"])))}


def flame_tongue(r):
    """Flame Tongue: a flamberge tempered from a blue-black spine to an orange-red edge, lava
    veins glowing near the edge; quillons curling up like flames; charred leather; a red stone
    held in iron claws."""
    blade_m, ember = M("FlameBlade"), M("EmberIron")
    b = dict(base=0.235, tip=2.02, width=0.082, thick=0.019, section="hex", flat=0.36, taper=0.32, point=0.10, curve=0.8, wave=(0.30, 7.5))
    blade, _ = wp.straight_blade("Blade", b, blade_m)
    blade_attrs(blade, b)
    parts = [blade]
    # The guard: a block, and flames — three rising on one side, two on the other, two licking up
    # the blade's faces.
    parts.append(tag(wp.prism("GuardBlock", [(0.19, 0.04, 0.03), (0.205, 0.05, 0.036), (0.24, 0.05, 0.036), (0.255, 0.03, 0.028)], ember, 12, 0.6), "fitting"))
    tongues = [
        ([(0.03, 0.22), (0.11, 0.19), (0.18, 0.215), (0.205, 0.29), (0.16, 0.35), (0.185, 0.43), (0.15, 0.52)], 0.046),
        ([(0.03, 0.205), (0.09, 0.165), (0.16, 0.14), (0.215, 0.165), (0.23, 0.21)], 0.030),
        ([(0.04, 0.235), (0.085, 0.275), (0.07, 0.33), (0.10, 0.38), (0.085, 0.44)], 0.026),
        ([(-0.03, 0.22), (-0.10, 0.215), (-0.165, 0.26), (-0.15, 0.33), (-0.19, 0.39), (-0.16, 0.47)], 0.042),
        ([(-0.03, 0.205), (-0.10, 0.175), (-0.16, 0.18), (-0.19, 0.225)], 0.028),
        ([(-0.04, 0.235), (-0.08, 0.28), (-0.065, 0.33), (-0.09, 0.37)], 0.022),
    ]
    for k, (path, w0) in enumerate(tongues):
        o = flame(uniq("Flame"), path, w0, ember, 0.013)
        wp.place(o, (0, (0.007 if k % 2 else -0.007), 0))
        parts.append(o)
    for sgn in (1, -1):
        for dx, h in ((0.0, 0.20), (0.02, 0.13), (-0.02, 0.11)):
            o = flame(uniq("Lick"), [(dx, 0.235), (dx + 0.014, 0.235 + h * 0.35), (dx - 0.01, 0.235 + h * 0.7), (dx + 0.006, 0.235 + h)], 0.018, ember, 0.005)
            wp.place(o, (0, sgn * 0.021, 0))
            parts.append(o)
    # Charred leather on the grip, iron ferrules.
    parts.append(wp.grip("Grip", -0.245, 0.19, 0.035, M("CharredLeather"), "spiral", 10, "barrel"))
    for z in (-0.25, 0.185):
        parts.append(tag(wp.lathe(uniq("Ferrule"), [(z - 0.012, 0.036), (z - 0.006, 0.041), (z + 0.006, 0.041), (z + 0.012, 0.036)], ember, 10), "fitting"))
    # The pommel: a cap, four claws, and a ruby held table-down between them.
    parts.append(tag(wp.lathe("ClawCap", [(-0.245, 0.034), (-0.26, 0.04), (-0.275, 0.032), (-0.28, 0.02)], ember, 12), "fitting"))
    gc = Vector((0, 0, -0.345))
    parts.append(brilliant("Ruby", gc, 0.058, 0.09, M("Ruby"), (0, 0, -1), 8))
    parts += claws("Claw", (0, 0, -0.275), gc, 0.058, 4, ember, 1.12, 0.011)
    return parts, {"fx": (Vector((0, 0, b["base"])), Vector((0, 0, b["tip"])))}


def frost_brand(r):
    """Frost Brand: a pale blue-white greatsword with ice growing out of it — crystals jutting
    from its edges where the lugs would be and up the middle of its flats — cyan runes in the
    fuller, a guard of ice shards, a pommel of one long crystal."""
    steel, ice_m, wrap = M("FrostSteel"), M("Ice"), M("FrostWrap")
    b = dict(base=0.58, tip=2.95, width=0.098, thick=0.021, section="hex", flat=0.5, taper=0.32, point=0.10, curve=0.72,
             fuller=(0.06, 0.66, 0.026, 0.55))
    blade, floor = wp.straight_blade("Blade", b, steel)
    blade_attrs(blade, b)
    parts = [blade]
    parts += fuller_glyphs("FrostRunes", floor, M("FrostRune"), 0.06, 0.0045, seed=2)
    # The ricasso, wrapped pale, bound with white cord.
    secs = [wp.box_ring_z(lerp(0.27, 0.60, t / 10), 0.068, 0.034) for t in range(11)]
    parts.append(tag(gg.loft("Ricasso", secs, wrap), "grip"))
    parts += wire_wrap("RicassoCord", 0.28, 0.59, 0.071, 4, M("WhiteCord"), 0.006, True, 0.55)
    # Ice where the lugs would be: crystals jutting out of both edges, angled up.
    for side in (1, -1):
        for (zz, ang, L, rr) in ((0.62, 62, 0.28, 0.036), (0.66, 35, 0.22, 0.030), (0.60, 88, 0.16, 0.026), (0.74, 18, 0.15, 0.022),
                                 (0.58, 115, 0.10, 0.02), (1.25, 40, 0.09, 0.016), (1.70, 30, 0.06, 0.012)):
            if zz > 1.0:
                zz += 0.07 * side
            base = Vector((side * 0.06, 0.0, zz))
            d = Vector((side * math.sin(math.radians(ang)), 0.0, math.cos(math.radians(ang))))
            parts.append(crystal(uniq("EdgeIce"), base, base + d * L, rr, ice_m))
    # Ice up the middle of both flats, smaller toward the point.
    for k in range(11):
        z = 0.64 + k * 0.10
        L = 0.16 * (1 - k / 13)
        for sgn in (1, -1):
            lean = 0.45 if k % 2 else -0.45
            base = Vector((lean * 0.03, sgn * 0.008, z))
            d = Vector((lean * 0.6, sgn * 0.7, 0.55)).normalized()
            parts.append(crystal(uniq("FlatIce"), base, base + d * L, 0.022 * (1 - k / 14), ice_m))
    # The guard: a silver bar under a burst of ice shards, longer one side than the other.
    parts.append(tag(wp.prism("GuardBar", [(0.215, 0.10, 0.032), (0.23, 0.12, 0.036), (0.26, 0.12, 0.036), (0.275, 0.09, 0.03)], M("BrightSilver"), 12, 0.5), "fitting"))
    for side, shards in ((1, ((0, 0.30, 0.034), (24, 0.20, 0.026), (-28, 0.16, 0.024), (50, 0.12, 0.02))),
                         (-1, ((0, 0.26, 0.032), (30, 0.18, 0.026), (-22, 0.20, 0.026), (-55, 0.10, 0.018)))):
        for ang, L, rr in shards:
            base = Vector((side * 0.08, 0.0, 0.245))
            d = Vector((side * math.cos(math.radians(ang)), 0.12 * math.sin(math.radians(ang * 3)), math.sin(math.radians(ang)))).normalized()
            parts.append(crystal(uniq("GuardIce"), base, base + d * L, rr, ice_m))
    # The grip: pale blue leather under a lattice of white cord.
    parts.append(wp.grip("Grip", -0.52, 0.21, 0.040, wrap, "plain", 1, "central"))
    parts += wire_wrap("Cord", -0.51, 0.20, 0.0425, 7, M("WhiteCord"), 0.0055)
    # The pommel: one long hexagonal crystal in a silver collar.
    parts.append(tag(wp.lathe("PommelCollar", [(-0.50, 0.04), (-0.53, 0.05), (-0.56, 0.046), (-0.575, 0.036)], M("BrightSilver"), 12), "fitting"))
    parts.append(crystal("PommelIce", (0, 0, -0.56), (0, 0, -0.92), 0.052, ice_m))
    for k in range(3):
        a = 2 * math.pi * k / 3 + 0.5
        parts.append(crystal(uniq("PommelShard"), (0.02 * math.cos(a), 0.02 * math.sin(a), -0.58), (0.09 * math.cos(a), 0.09 * math.sin(a), -0.74), 0.024, ice_m))
    return parts, {"fx": (Vector((0, 0, 0.27)), Vector((0, 0, b["tip"])))}


def life_drinker(r):
    """Life-Drinker: a greataxe with a huge black iron crescent each side, blood grooves running
    from the edge to red veins round the eye, a spike above; a spine of bone vertebrae up the
    haft; a small skull at its foot."""
    iron, bone, groove = M("BlackIron"), M("OldBone"), M("BloodGroove")
    parts = []
    parts.append(wp.haft("Haft", -0.62, 2.40, 0.048, M("DarkWood"), 0.044, 10))
    parts.append(wp.grip("Grip", -0.46, 0.30, 0.054, M("BloodLeather"), "spiral", 10))
    zc = 2.06
    # The eye: a tall socket block, and the two crescents.
    parts.append(tag(wp.prism("Socket", [(zc - 0.28, 0.062, 0.052), (zc - 0.25, 0.07, 0.058), (zc + 0.25, 0.07, 0.058), (zc + 0.28, 0.062, 0.052)], iron, 12, 0.5), "head"))
    for side in (1, -1):
        R = 0.62
        pts = [(side * (-0.07 + R * math.cos(math.radians(a))), zc + R * math.sin(math.radians(a))) for a in range(-52, 53, 8)]
        # outer edge sharp, the body broad toward the haft
        body = lambda s: 0.03 + 0.24 * math.sin(math.pi * s) ** 0.9
        if side > 0:
            o = wp.sweep(uniq("Crescent"), pts, body, lambda s: 0.03, 0.040, iron, (False, True), "point", 3, 0.4)
        else:  # the left crescent runs top to bottom, so its body is still on its left
            o = wp.sweep(uniq("Crescent"), list(reversed(pts)), body, lambda s: 0.03, 0.040, iron, (False, True), "point", 3, 0.4)
        o["role"] = "edge"
        set_attr(o, "Vein", lambda p: smoothstep(0.42, 0.12, abs(p.x)) * smoothstep(0.34, 0.08, abs(p.z - zc)))
        parts.append(o)
        # The neck joining the crescent to the eye.
        neck = wp.sweep(uniq("Neck"), [(side * 0.05, zc), (side * 0.30, zc)], lambda s: 0.10 + 0.08 * s, lambda s: 0.10 + 0.08 * s, 0.046, iron, (False, False), None, 2, 0.4)
        set_attr(neck, "Vein", lambda p: 1.0)
        parts.append(tag(neck, "head"))
        # Blood grooves: three channels on each face running in from the edge.
        for k, dz in enumerate((-0.17, 0.0, 0.17)):
            for sgn in (1, -1):
                gx0 = side * 0.43
                path = [(gx0, zc + dz * 1.15), (side * 0.40, zc + dz * 0.9), (side * 0.30, zc + dz * 0.55), (side * 0.22, zc + dz * 0.25)]
                g = wp.sweep(uniq("Groove"), path, lambda s: 0.010 * (1 - 0.4 * s), lambda s: 0.010 * (1 - 0.4 * s), 0.003, groove, (False, False), None, 4, 0.5)
                wp.place(g, (0, sgn * 0.0405, 0))
                parts.append(tag(g, "rune"))
    # The spike above the head, four-sided, with a barb.
    parts.append(tag(wp.lathe("Spike", [(zc + 0.27, 0.05), (zc + 0.32, 0.045), (zc + 0.62, 0.012), (zc + 0.80, 0.0)], iron, 4, faceted=True, phase=math.pi / 4), "edge"))
    # The spine: vertebrae up the haft, each a centrum round it with a spinous process raking back
    # and down and a transverse process to each side.
    for k in range(11):
        z = 0.44 + k * 0.125
        s = 1.32 - 0.22 * abs(k - 5) / 5
        el = [dict(co=(0, 0, z), r=(0.068 * s, 0.068 * s, 0.035 * s)),
              dict(co=(0.07 * s, 0, z - 0.012), r=(0.04 * s, 0.022 * s, 0.024 * s)),
              dict(co=(0.12 * s, 0, z - 0.03), r=(0.03 * s, 0.014 * s, 0.016 * s)),
              dict(co=(0.155 * s, 0, z - 0.05), r=(0.016 * s, 0.009 * s, 0.01 * s)),
              dict(co=(0, 0.075 * s, z), r=(0.016 * s, 0.03 * s, 0.014 * s)),
              dict(co=(0, -0.075 * s, z), r=(0.016 * s, 0.03 * s, 0.014 * s)),
              dict(co=(0, 0, z), r=(0.05, 0.05, 0.06), neg=True)]
        parts.append(sculpt("Vertebra", el, bone, 0.0045, 0.45))
    # The skull at the foot.
    sk = -0.70
    skull = [dict(co=(0, 0, sk), r=(0.062, 0.068, 0.058)),
             dict(co=(0, -0.045, sk - 0.05), r=(0.042, 0.03, 0.04)),
             dict(co=(0, -0.05, sk - 0.095), r=(0.034, 0.022, 0.022)),
             dict(co=(0.028, -0.058, sk - 0.04), r=(0.018, 0.016, 0.018)),
             dict(co=(-0.028, -0.058, sk - 0.04), r=(0.018, 0.016, 0.018)),
             dict(co=(0.024, -0.075, sk - 0.03), r=0.017, neg=True),
             dict(co=(-0.024, -0.075, sk - 0.03), r=0.017, neg=True),
             dict(co=(0, -0.08, sk - 0.068), r=(0.008, 0.012, 0.012), neg=True),
             dict(co=(0, 0, sk + 0.06), r=(0.03, 0.03, 0.04))]
    parts.append(sculpt("Skull", skull, bone, 0.0035, 0.5))
    for sgn in (1, -1):
        parts.append(tag(wp.lathe(uniq("Eye"), [(-0.008, 0.0), (-0.006, 0.008), (0.006, 0.008), (0.008, 0.0)], groove, 8), "rune"))
        wp.place(parts[-1], (sgn * 0.024, -0.062, sk - 0.03))
    return parts, {"fx": (Vector((0, 0, zc - 0.55)), Vector((0, 0, zc + 0.55)))}


def dwarven_thrower(r):
    """Dwarven Thrower: a warhammer with a heavy anvil for a head, deep-cut knotwork on its
    cheeks and gold runes glowing blue-white round its waist; braided leather beards and gold
    bands down the haft; a mountain peak at its foot."""
    steel, etched, gold, rune = M("DwarfSteel"), M("EtchedIron"), M("NamedGold"), M("BlueRune")
    parts = [wp.haft("Haft", -0.30, 1.30, 0.04, M("DarkWood"), 0.038, 10)]
    zc = 1.40
    head_from = None
    # The anvil: a top block with the face at one end and a horn at the other, a waist, a foot.
    secs = []
    for x, z0, z1, d in ((-0.21, 0.0, 0.10, 0.068), (-0.20, -0.004, 0.105, 0.075), (0.10, -0.004, 0.105, 0.075), (0.14, 0.01, 0.10, 0.06),
                         (0.20, 0.035, 0.095, 0.042), (0.26, 0.055, 0.088, 0.024), (0.30, 0.066, 0.078, 0.0)):
        secs.append([Vector((x, yy * max(d, 0.0005), zc + zz)) for yy, zz in ((-1, z0), (1, z0), (1, z1), (-1, z1))] if d > 0 else [Vector((x, 0, zc + 0.072))] * 4)
    top = gg.loft("AnvilTop", secs, steel, smooth_shading=False)
    head_from = len(parts)
    parts.append(tag(top, "head"))
    parts.append(tag(wp.prism("AnvilWaist", [(zc - 0.075, 0.07, 0.048), (zc - 0.03, 0.05, 0.04), (zc + 0.002, 0.075, 0.055)], steel, 12, 0.45), "head"))
    parts.append(tag(wp.prism("AnvilFoot", [(zc - 0.13, 0.13, 0.072), (zc - 0.11, 0.135, 0.075), (zc - 0.08, 0.11, 0.065), (zc - 0.07, 0.08, 0.05)], steel, 12, 0.45), "head"))
    # Knotwork on both cheeks of the top block, and on the foot.
    for sgn in (1, -1):
        parts.append(knot(uniq("Knot"), (-0.04, 0, zc + 0.052), 0.11, 0.046, sgn * 0.075, etched, 0.0055))
        parts.append(knot(uniq("Knot"), (0.0, 0, zc - 0.10), 0.07, 0.018, sgn * 0.075, etched, 0.004, 4))
        parts += glyphs(uniq("Runes"), (-0.05, sgn * 0.044, zc - 0.045), (1, 0, 0), (0, 0, 1), (0, sgn, 0), 0.10, 0.026, rune, 0.004)
        parts += glyphs(uniq("Runes"), (-0.185, sgn * 0.075, zc + 0.012), (1, 0, 0), (0, 0, 1), (0, sgn, 0), 0.30, 0.02, rune, 0.0035, seed=1)
    # Gold on the anvil: a band round the face's end and a cap on the horn's tip.
    band = wp.prism("FaceBand", [(-0.216, 0.057, 0.079), (-0.196, 0.057, 0.079)], gold, 8, 0.5)
    wp.place(band, (0, 0, 0), (0, 90, 0))
    wp.place(band, (0, 0, zc + 0.05))
    parts.append(tag(band, "fitting"))
    parts.append(tag(wp.lathe("HornCap", [(0.0, 0.026), (0.03, 0.02), (0.07, 0.0)], gold, 8), "fitting"))
    wp.orient(parts[-1], (0.25, 0, zc + 0.072), (1, 0, -0.1))
    # The whole head heavier than a warhammer's: half as big again about its middle.
    for o in parts[head_from:]:
        o.matrix_world = Matrix.Translation((0, 0, zc)) @ Matrix.Diagonal((1.45, 1.35, 1.45, 1.0)) @ Matrix.Translation((0, 0, -zc)) @ o.matrix_world
    # Gold bands down the haft, and beards of braided leather hung under the head.
    for z in (0.25, 0.62, 1.0, 1.20):
        parts.append(tag(wp.lathe(uniq("Band"), [(z - 0.02, 0.04), (z - 0.012, 0.047), (z + 0.012, 0.047), (z + 0.02, 0.04)], gold, 12), "fitting"))
    parts.append(wp.grip("Grip", -0.28, 0.22, 0.044, M("BraidLeather"), "diamond", 8))
    for k, (bx, by, L) in enumerate(((0.06, 0.035, 0.40), (-0.06, 0.035, 0.34), (0.0, -0.06, 0.46))):
        z0 = zc - 0.19
        for strand in range(2):
            pts = []
            for i in range(25):
                t = i / 24
                a = 2 * math.pi * 3.0 * t + strand * math.pi
                pts.append((bx + 0.009 * math.cos(a), by + 0.009 * math.sin(a), z0 - L * t))
            parts.append(tag(wp.tube(uniq("Braid"), pts, lambda u: 0.009 * (1 - 0.35 * u), M("BraidLeather"), 5), "fitting"))
        parts.append(tag(wp.lathe(uniq("Bead"), [(-0.016, 0.0), (-0.012, 0.016), (0.012, 0.016), (0.016, 0.0)], gold, 10), "fitting"))
        wp.place(parts[-1], (bx, by, z0 - L - 0.01))
    # The foot of the haft: a mountain, one peak and its shoulders, the snow on them gold.
    parts.append(tag(wp.lathe("Peak", [(-0.29, 0.046), (-0.33, 0.06), (-0.48, 0.0)], steel, 5, faceted=True), "fitting"))
    for k in range(3):
        a = 2 * math.pi * k / 3 + 0.4
        o = wp.lathe(uniq("Shoulder"), [(0.0, 0.035), (0.03, 0.04), (0.10, 0.0)], steel, 5, faceted=True)
        wp.orient(o, (0.035 * math.cos(a), 0.035 * math.sin(a), -0.31), (0.6 * math.cos(a), 0.6 * math.sin(a), -1.0))
        parts.append(tag(o, "fitting"))
    parts.append(tag(wp.lathe("Snow", [(-0.43, 0.012), (-0.45, 0.008), (-0.485, 0.0)], gold, 5, faceted=True), "fitting"))
    return parts, {"fx": (Vector((0, 0, zc - 0.19)), Vector((0, 0, zc + 0.15)))}


def oathbow(r):
    """Oathbow: an elegant recurve of pale ash, silver leaf filigree running along its back,
    swans' heads carved at its tips, a green stone in the riser, a string with a faint light."""
    reach, belly, thick, rec, c = 1.92, 0.50, 0.046, 0.78, 0.80
    row = dict(reach=reach, belly=belly, thick=thick, recurve=rec, contact=c, material="Wood", grip_material="Wrap", ears="Bone")
    parts, info = wf._bow(row)
    ash, silver, gold = M("PaleAsh"), M("BrightSilver"), M("NamedGold")
    keep = []
    for o in parts:
        if o.name.startswith("Nock"):
            bpy.data.objects.remove(o, do_unlink=True)
            continue
        if o.name.startswith("Bow_String"):
            o.data.materials[0] = M("Oathstring")
        elif o.name.startswith("Bow_Grip"):
            o.data.materials[0] = M("GreenLeather")
        else:
            o.data.materials[0] = ash
        keep.append(o)
    parts = keep

    def centre(t):
        a = abs(t)
        if a <= c:
            return belly * (a / c) ** 2
        u = (a - c) / (1 - c)
        return belly - belly * rec * u ** 1.4

    def back(t):
        a = abs(t)
        taper = 1.0 - 0.55 * min(a, c) ** 1.5 / c ** 1.5 * 0.85
        return centre(t) - (thick * 0.78 * taper + 0.004)
    # Silver leaf filigree down the back of each limb.
    for sgn in (1, -1):
        pts = []
        for phase in (0.0, math.pi):
            pts = []
            for k in range(25):
                t = sgn * (0.13 + 0.72 * k / 24)
                pts.append((0.026 * math.sin(k * 0.8 + phase), back(t) - 0.002, reach * t))
            parts += vine(uniq("Filigree"), pts, silver, 0.0055, 14, 0.06)
    # Swans: at each tip a neck rising out of the ear, curving forward, a head and a gilt beak.
    for sgn in (1, -1):
        tip = Vector((0, centre(1.0), sgn * reach))
        neck = [tip + Vector((0, 0.03, -sgn * 0.06)), tip + Vector((0, 0.0, sgn * 0.06)), tip + Vector((0, -0.05, sgn * 0.16)),
                tip + Vector((0, -0.14, sgn * 0.19)), tip + Vector((0, -0.21, sgn * 0.13)), tip + Vector((0, -0.23, sgn * 0.06))]
        el = chain_balls(neck, (0.034, 0.024), 5)
        head = tip + Vector((0, -0.245, sgn * 0.035))
        el.append(dict(co=head, r=(0.03, 0.046, 0.032)))
        el.append(dict(co=head + Vector((0, 0.01, sgn * 0.016)), r=(0.028, 0.03, 0.028)))
        for ex in (0.024, -0.024):
            el.append(dict(co=head + Vector((ex, -0.004, sgn * 0.018)), r=0.008, neg=True))
        parts.append(sculpt("Swan", el, ash, 0.0035, 0.5))
        beak = wp.lathe(uniq("Beak"), [(0.0, 0.017), (0.035, 0.015), (0.07, 0.006), (0.078, 0.0)], gold, 8)
        wp.orient(beak, head + Vector((0, -0.035, -sgn * 0.006)), (0, -1, -sgn * 0.5))
        parts.append(tag(beak, "fitting"))
        parts.append(tag(wp.lathe(uniq("TipCollar"), [(-0.03, 0.028), (-0.02, 0.036), (0.02, 0.036), (0.03, 0.028)], silver, 10), "fitting"))
        wp.orient(parts[-1], tip + Vector((0, 0.03, -sgn * 0.07)), (0, 0, sgn))
    # The riser: a green stone in a silver bezel on the back, above the grip.
    parts.append(tag(wp.prism("Riser", [(0.10, 0.05, 0.03, 0, -0.035), (0.16, 0.056, 0.034, 0, -0.04), (0.28, 0.05, 0.03, 0, -0.035)], ash, 12, 0.7), "fitting"))
    parts.append(brilliant("RiserStone", (0, -0.072, 0.20), 0.044, 0.05, M("Emerald"), (0, -1, 0), 10))
    parts.append(tag(wp.torus("RiserBezel", (0, -0.074, 0.20), 0.046, 0.007, silver, plane="XZ", n=20, m=5), "fitting"))
    for sx in (1, -1):
        for dz in (-1, 1):
            leaf = wp.sweep(uniq("RiserLeaf"), [(0.0, 0.0), (sx * 0.03, dz * 0.05), (sx * 0.02, dz * 0.11)], lambda s: 0.022 * math.sin(math.pi * s) + 0.002,
                            lambda s: 0.022 * math.sin(math.pi * s) + 0.002, 0.004, silver, (True, True), "point", 3, 0.6)
            wp.place(leaf, (sx * 0.035, -0.072, 0.20 + dz * 0.03))
            parts.append(tag(leaf, "fitting"))
    return parts, info


BUILD = {
    "named-holy-avenger": holy_avenger,
    "named-flame-tongue": flame_tongue,
    "named-frost-brand": frost_brand,
    "named-life-drinker": life_drinker,
    "named-dwarven-thrower": dwarven_thrower,
    "named-oathbow": oathbow,
}


def _named(r):
    return BUILD[r["id"]](r)


wf.BUILDERS["named"] = _named

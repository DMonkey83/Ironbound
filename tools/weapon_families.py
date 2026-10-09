"""Weapon families: one recipe table, one builder per family, a dozen named materials.

    import weapon_families as wf
    parts, info = wf.build("longsword")       # Blender objects, standing up, grip at the origin

Imported by `generate_weapons.py`, which owns the export, and by `render_icons.py`, which reads
the hands classes and the material table. Not run on its own.

WHY FAMILIES. The first sword was one function at four lengths, and the owner could not tell a
longsword from a greatsword: "some of those swords look way too similar, some dont look correct,
for what they are." A weapon is told apart by the parts that make it what it is — a zweihander
by its ricasso, lugs and forearm-long grip, a gladius by its parallel edges and short point —
so each family builds from named parts, and the RECIPES table says which parts, and how big,
for each id. Two ids share a silhouette only when the real weapons do.

THE RECIPE FORMAT. `RECIPES[id]` is a dict:

    family    which builder: "straight" or "curved" so far
    hands     the inventory size class: light | one | two | polearm | bow | crossbow | shield
    like      another id to start from; this row's keys are merged over it (one level deep)
    variant   a special material or an enhancement: cold-iron | silver | adamantine | mithral | +1..+5
    metal, fittings, grip_material     the named materials of the blade, the guard and pommel, the grip
    blade, guard, grip, pommel, ...     the family's own parameters, in human-model units

Every part is tagged with a `role` (edge, fitting, grip, rune) so a variant knows what to swap:
a cold-iron sword is cold iron where it is metal and still leather where it is held.

MATERIALS. Catalogue weapons are not baked: each mesh carries a few materials named exactly as
in MATERIALS, which the game swaps for its own shared tileable ones. The seven swords that
characters hold today are still baked by `surface.py` like every other model they stand next to,
and the names are kept up to the bake so a recipe here is also how they are textured. A rune's
glow is never baked: it stays its own emissive material.
"""
import math

import bpy
from mathutils import Vector

import generate_goblin as gg
import surface

Y = Vector((0.0, 1.0, 0.0))


# --- small maths -------------------------------------------------------------------------------

def lerp(a, b, t):
    return a + (b - a) * t


def smoothstep(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def tag(obj, role):
    obj["role"] = role
    return obj


def crisp(obj, degrees=28):
    """Keep smooth shading on the curves but break it on the ridges: a blade's bevel line, a
    guard's corners. A ridge smoothed over is what makes a sword look moulded from soap."""
    obj.data.set_sharp_from_angle(angle=math.radians(degrees))
    return obj


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


def _lustre(polish, metallic):
    """A dark or bright special metal: steel's brushing, its own colour, its own polish."""
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
    # The special materials. Each must be told from steel at a glance, in an icon.
    "Silver":     ((0.88, 0.93, 1.00), 0.38, 0.08, _silver),
    "ColdIron":   ((0.050, 0.058, 0.085), 0.60, 0.35, _cold_iron),
    "Adamantine": ((0.055, 0.085, 0.065), 0.62, 0.28, _lustre(0.20, 0.62)),
    "Mithral":    ((0.72, 0.82, 0.98), 0.60, 0.16, _lustre(0.10, 0.60)),
}

# The rune line of an enhanced weapon: the one emissive material, brighter with the bonus.
RUNE_COLOUR = (0.30, 0.66, 1.00)
RUNE_STRENGTH = {1: 1.4, 2: 2.2, 3: 3.2, 4: 4.4, 5: 6.0}

for _name, (_colour, _metal, _rough, _recipe) in MATERIALS.items():
    surface.RECIPES[_name] = (lambda r: lambda c, m, rough: r(c))(_recipe)


def material(name):
    """The named material, made once per Blender session."""
    if name.startswith("Rune"):
        bonus = int(name[4:])
        m = gg.mat(name, (0.05, 0.12, 0.22), 0.0, 0.3)
        bsdf = m.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Emission Color"].default_value = (*RUNE_COLOUR, 1.0)
        bsdf.inputs["Emission Strength"].default_value = RUNE_STRENGTH[bonus]
        return m
    colour, metallic, rough, _ = MATERIALS[name]
    return gg.mat(name, colour, metallic, rough)


def procedural(name):
    """The procedural stand-in for a named material, for drawing an icon of an unbaked model."""
    base = name.split(".")[0]
    if base not in MATERIALS:
        return None
    m = MATERIALS[base][3](MATERIALS[base][0])
    m.name = f"Icon_{base}"
    return m


UNBAKED = ("Rune",)


def is_unbaked(obj):
    return any(m is not None and m.name.startswith(UNBAKED) for m in obj.data.materials)


# --- building blocks ---------------------------------------------------------------------------

def ring(points):
    return [Vector(p) for p in points]


def ellipse(cx, cy, z, rx, ry, n=12, phase=0.0):
    return [Vector((cx + rx * math.cos(a), cy + ry * math.sin(a), z)) for a in (phase + i * 2 * math.pi / n for i in range(n))]


def lathe(name, profile, material, n=12, oval=1.0, faceted=False, x=0.0, phase=0.0):
    """A solid turned about Z from (z, radius) pairs; a radius of 0 closes it to a point."""
    sections = [ellipse(x, 0.0, z, r, r * oval, n, phase) if r > 0 else [Vector((x, 0.0, z))] * n for z, r in profile]
    o = gg.loft(name, sections, material, smooth_shading=True)
    return crisp(o, 20 if faceted else 50)


def tube(name, points, radius, material, n=8):
    """A round bar swept along a polyline: a knuckle bow, a ring, a chain's link."""
    points = [Vector(p) for p in points]
    sections = []
    for i, p in enumerate(points):
        a = points[max(0, i - 1)]
        b = points[min(len(points) - 1, i + 1)]
        t = (b - a).normalized()
        side = t.cross(Y) if abs(t.dot(Y)) < 0.9 else t.cross(Vector((1, 0, 0)))
        side.normalize()
        up = side.cross(t)
        r = radius(i / (len(points) - 1)) if callable(radius) else radius
        sections.append([p + (side * math.cos(k * 2 * math.pi / n) + up * math.sin(k * 2 * math.pi / n)) * r for k in range(n)])
    return gg.loft(name, sections, material)


def torus(name, centre, major, minor, material, tilt=0.0, n=18, m=6):
    """A ring standing in the blade's plane (XZ), leaned back by `tilt` degrees about X."""
    c = Vector(centre)
    pts = []
    for k in range(n + 1):
        a = k * 2 * math.pi / n
        pts.append(c + Vector((major * math.cos(a), 0.0, major * math.sin(a))))
    lean = math.radians(tilt)
    pts = [c + Vector((p.x - c.x, -(p.z - c.z) * math.sin(lean), (p.z - c.z) * math.cos(lean))) for p in pts]
    return tube(name, pts, minor, material, m)


def box_ring_yz(x, zc, hh, dd, chamfer=0.3):
    """A rounded rectangle in the plane x = const: hh half-height (Z), dd half-depth (Y)."""
    c = chamfer * min(hh, dd)
    return ring([
        (x, -dd + c, zc - hh), (x, dd - c, zc - hh), (x, dd, zc - hh + c), (x, dd, zc + hh - c),
        (x, dd - c, zc + hh), (x, -dd + c, zc + hh), (x, -dd, zc + hh - c), (x, -dd, zc - hh + c),
    ])


def cross_guard(name, zc, span, h, d, material, ends="knob", droop=0.0, centre=0.4, taper=0.3, knob=0.75):
    """A bar across the blade, lofted along X: square in section with chamfered corners, deepest
    at the middle where it is drilled for the tang, its quillons tapering, turned by `droop`
    (toward the blade when positive) and finished with a knob or a flared end."""
    ts = [-1.0, -0.99, -0.965, -0.93, -0.88, -0.8, -0.65, -0.45, -0.25, -0.1, 0.0,
          0.1, 0.25, 0.45, 0.65, 0.8, 0.88, 0.93, 0.965, 0.99, 1.0]
    sections = []
    for t in ts:
        a = abs(t)
        mid = 1.0 - smoothstep(0.0, 0.3, a)
        hh = h * (1 - taper * a) * (1 + centre * mid)
        dd = d * (1 - 0.5 * taper * a) * (1 + 0.5 * centre * mid)
        if ends == "knob":
            k = smoothstep(0.78, 0.95, a)
            hh *= 1 + knob * k
            dd *= 1 + 0.6 * knob * k
        elif ends == "flare":
            k = smoothstep(0.6, 1.0, a)
            hh *= 1 + knob * k
        if a > 0.93:                                             # round the very end off
            r = math.sqrt(max(0.0, 1 - ((a - 0.93) / 0.07) ** 2))
            hh *= max(r, 0.0)
            dd *= max(r, 0.0)
        z = zc + droop * a ** 2.2
        if hh < 1e-4:
            sections.append([Vector((span * t, 0.0, z))] * 8)
        else:
            sections.append(box_ring_yz(span * t, z, hh, dd))
    return crisp(gg.loft(name, sections, material), 35)


def grip(name, z0, z1, r, material, style="spiral", turns=8, profile="straight", bend=0.0, oval=0.86, n=10):
    """What the hand closes round, bottom z0 to top z1.

    style: spiral (a leather strip wound on, each turn a raised helix), cord (rings of cord),
    ridged (four finger grooves, a gladius's bone grip), plain.
    profile: straight, barrel (swelled for a hand and a half), central (a raised ring between
    two hands), waisted. bend: how far the bottom is drawn aside, for a curved grip.
    """
    per_turn = 5
    steps = max(8, turns * per_turn) if style in ("spiral", "cord") else 16
    sections = []
    for i in range(steps + 1):
        t = i / steps
        z = lerp(z0, z1, t)
        rr = r
        if profile == "barrel":
            rr *= 1 + 0.10 * math.sin(math.pi * t)
        elif profile == "central":
            rr *= 1 + 0.05 * math.sin(math.pi * t) + 0.16 * math.exp(-((t - 0.5) / 0.045) ** 2)
        elif profile == "waisted":
            rr *= 1 - 0.07 * math.sin(math.pi * t)
        if style == "ridged":
            phase = (t * 4) % 1.0
            rr *= 1 - 0.22 * math.sin(math.pi * phase) ** 1.5
        cx = bend * (1 - t) ** 2
        # The ends of a wrap are tucked under a little ferrule of the wrap itself.
        lip = 1 + 0.08 * (smoothstep(0.03, 0.0, t) + smoothstep(0.97, 1.0, t))
        pts = []
        for k in range(n):
            a = k * 2 * math.pi / n
            q = rr * lip
            if style == "spiral":
                q *= 1 + 0.075 * math.sin(2 * math.pi * turns * t - a)
            elif style == "cord":
                q *= 1 + 0.09 * math.sin(2 * math.pi * turns * t)
            pts.append(Vector((cx + q * math.cos(a), q * oval * math.sin(a), z)))
        sections.append(pts)
    return tag(gg.loft(name, sections, material), "grip")


def wheel(name, zc, r, half, material, boss=0.55, n=20):
    """A wheel pommel: a disc standing in the blade's plane, chamfered round its rim, with a
    raised boss on each face."""
    prof = [(-half * 1.45, r * boss * 0.7), (-half * 1.4, r * boss * 0.92), (-half * 1.1, r * boss),
            (-half * 1.0, r * 0.80), (-half * 0.92, r * 0.95), (-half * 0.55, r), (half * 0.55, r),
            (half * 0.92, r * 0.95), (half * 1.0, r * 0.80), (half * 1.1, r * boss),
            (half * 1.4, r * boss * 0.92), (half * 1.45, r * boss * 0.7)]
    sections = [[Vector((rr * math.cos(a), y, zc + rr * math.sin(a))) for a in (k * 2 * math.pi / n for k in range(n))] for y, rr in prof]
    return crisp(gg.loft(name, sections, material), 30)


# --- blades ------------------------------------------------------------------------------------

def _thickness(kind, a, flat):
    a = abs(a)
    if kind == "diamond":
        return 1.0 - a
    if kind == "lens":
        return 1.0 - a * a
    if a <= flat:                                                # "hex": flats, then bevels
        return 1.0
    return ((1.0 - a) / (1.0 - flat)) ** 0.9


def edge_section(origin, across, w, t, kind="hex", flat=0.5, fuller=0.0, fuller_w=0.0):
    """A double-edged blade's cross-section: 20 points round it, both edges single points.

    `across` is the unit vector to one edge, w the half-width and t the half-thickness. A
    fuller is a rounded groove down each face, `fuller` of the thickness deep and `fuller_w`
    (in units) to each side of the middle. The sample points sit on the fuller's shoulders and
    the bevel line, so both are crisp ridges and not smeared between rings."""
    fw = min(0.8 * flat if kind == "hex" else 0.5, fuller_w / max(w, 1e-6)) if fuller_w > 0 else 0.5 * flat
    fw = max(fw, 0.05)
    s1, s2 = 0.5 * (1 + flat), flat
    if fw >= s2:
        fw = 0.8 * s2
    top = [1.0, s1, s2, fw, 0.5 * fw, 0.0, -0.5 * fw, -fw, -s2, -s1, -1.0]
    bottom = [-s1, -s2, -fw, -0.5 * fw, 0.0, 0.5 * fw, fw, s2, s1]

    def y(a):
        v = _thickness(kind, a, flat)
        if fuller > 0 and abs(a) < fw:
            v -= fuller * (1 - (a / fw) ** 2) ** 0.8
        return t * max(v, 0.12)

    return [origin + across * (a * w) + Y * y(a) for a in top] + [origin + across * (a * w) - Y * y(a) for a in bottom]


def blade_width(s, b):
    """Half-width at s (0 at the shoulder, 1 at the tip): a straight taper by `taper` of the
    width, then the point over the last `point` of the length, straight (curve 1) or ogival."""
    w0, taper, point, curve = b["width"], b.get("taper", 0.0), b.get("point", 0.15), b.get("curve", 1.0)
    ps = 1.0 - point
    if s <= ps:
        return w0 * (1 - taper * (s / ps if ps > 0 else 0))
    u = (s - ps) / point
    return w0 * (1 - taper) * max(0.0, 1 - u) ** curve


def straight_blade(name, b, material):
    """A straight double-edged blade from z=b['base'] to b['tip'], centred on the Z axis."""
    z0, z1 = b["base"], b["tip"]
    L = z1 - z0
    t0 = b["thick"]
    ps = 1.0 - b.get("point", 0.15)
    ss = {i / 18 for i in range(18)} | {ps, ps + 0.002} | {ps + (1 - ps) * k / 7 for k in range(1, 7)}
    ss = sorted(s for s in ss if 0 <= s < 1)
    fuller = b.get("fuller")                                     # (start s, end s, half-width, depth)
    sections = [edge_section(Vector((0, 0, z0 - 0.02)), Vector((1, 0, 0)), blade_width(0, b), t0, b.get("section", "hex"), b.get("flat", 0.5))]
    floor = []
    for s in ss:
        w = blade_width(s, b)
        t = t0 * (1 - b.get("distal", 0.55) * s)
        if s > ps:
            t *= (1 - (s - ps) / (1 - ps)) ** 0.45 * 0.85 + 0.15
        depth, fwa = 0.0, 0.0
        if fuller:
            f0, f1, fwa, fd = fuller
            fade = smoothstep(f0, f0 + 0.03, s) * smoothstep(f1, f1 - 0.07, s)
            depth = fd * fade
            fwa = fwa * (0.45 + 0.55 * math.sqrt(fade))
        z = z0 + L * s
        sections.append(edge_section(Vector((0, 0, z)), Vector((1, 0, 0)), w, t, b.get("section", "hex"), b.get("flat", 0.5), depth, fwa))
        if fuller and depth > 0.5 * fuller[3]:
            floor.append((z, t * max(1.0 - depth, 0.12), fwa))
    sections.append([Vector((0, 0, z1))] * 20)
    blade = crisp(gg.loft(name, sections, material), 24)
    return tag(blade, "edge"), floor


def curved_blade(name, b, material):
    """A single-edged blade sweeping back, edge on the convex side (-X), spine (+X).

    The spine runs as a parabola from the guard, `sweep` aside at the tip. The edge stands
    `width` off it and flares by `flare` toward the tip; from `clip` on the back is cut away in
    a straight false edge down to the point, which sits `toff` of the width in from the spine
    line — a clip point, the yelman of a kilij. A narrow fuller runs near the spine."""
    z0, L, sweep = b["base"], b["length"], b["sweep"]
    W0, flare, sc, toff = b["width"], b["flare"], b["clip"], b.get("toff", 0.3)
    t0 = b["thick"]
    half = W0 / 2

    def spine(s):
        return Vector((half + sweep * s * s, 0.0, z0 + L * s))

    def normal(s):
        tz = Vector((2 * sweep * s, 0.0, L)).normalized()
        return Vector((-tz.z, 0.0, tz.x))                       # toward the edge

    Wc = W0 * (1 + flare * sc * sc)
    step = b.get("step", 0.0) * W0                              # the back stands out at the yelman
    qs_top = [0.78, 0.56, 0.42, 0.31, 0.20, 0.09, 0.0]
    fz = b.get("fuller", (0.03, 0.62, 0.20, 0.42, 0.35))           # s0, s1, q0, q1, depth

    def section(s):
        N = normal(s)
        if s <= sc:
            db, de, tb = 0.0, W0 * (1 + flare * s * s), 1.0
        else:
            u = (s - sc) / (1 - sc)
            db = -step * (1 - u) + toff * Wc * u
            de = toff * Wc + (Wc - toff * Wc) * max(0.0, 1 - u ** 2.0) ** 0.75
            tb = max(0.10, (1 - u) ** 1.4)                       # the back sharpened to a false edge
        t = t0 * (1 - 0.5 * s)
        B = spine(s) + N * db
        E = spine(s) + N * de
        fade = smoothstep(fz[0], fz[0] + 0.04, s) * smoothstep(fz[1], fz[1] - 0.08, s)

        def th(q):
            v = (1 - 0.12 * q) if q < 0.45 else 0.946 * ((1 - q) / 0.55) ** 0.9
            v *= lerp(tb, 1.0, smoothstep(0.0, 0.5, q))
            if fade > 0 and fz[2] < q < fz[3]:
                m = (q - fz[2]) / (fz[3] - fz[2])
                v -= fz[4] * fade * math.sin(math.pi * m) ** 0.8
            return t * max(v, 0.1)

        pts = [E]
        pts += [B + (E - B) * q + Y * th(q) for q in qs_top]
        pts += [B + (E - B) * q - Y * th(q) for q in reversed(qs_top)]
        return pts

    ss = sorted({i / 16 for i in range(16)} | {sc, sc + 0.004} | {sc + (1 - sc) * k / 8 for k in range(1, 8)})
    base = [p - Vector((0, 0, 0.02)) for p in section(0.0)]
    tip = spine(1.0) + normal(1.0) * toff * Wc
    sections = [base] + [section(s) for s in ss] + [[tip] * 15]
    return tag(crisp(gg.loft(name, sections, material), 26), "edge"), tip


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


# --- families ----------------------------------------------------------------------------------

def _pommel(p, fittings):
    kind, top = p["kind"], p["top"]
    r = p["radius"]
    if kind == "scent":
        # A scent-stopper: a faceted drop, widest a third of the way down, like a flask's stopper.
        h = p["height"]
        prof = [(top + 0.004, r * 0.55), (top - 0.10 * h, r * 0.66), (top - 0.32 * h, r * 0.95), (top - 0.48 * h, r),
                (top - 0.70 * h, r * 0.74), (top - 0.88 * h, r * 0.36), (top - h, 0.0)]
        return [tag(lathe("Pommel", prof, fittings, n=8, oval=0.78, faceted=True, phase=math.pi / 8), "fitting")]
    if kind == "ovoid":
        h = p["height"]
        prof = [(top + 0.004, r * 0.45), (top - 0.05 * h, r * 0.72), (top - 0.25 * h, r * 0.96), (top - 0.50 * h, r),
                (top - 0.78 * h, r * 0.82), (top - 0.94 * h, r * 0.45), (top - h, r * 0.38)]
        parts = [tag(lathe("Pommel", prof, fittings, n=16, oval=p.get("oval", 0.8)), "fitting")]
        nut = p.get("nut", "Bronze")
        parts.append(tag(lathe("Pommel_Nut", [(top - h + 0.004, r * 0.30), (top - h - 0.012, r * 0.30), (top - h - 0.024, r * 0.18), (top - h - 0.028, 0.0)],
                               material(nut), n=10), "fitting"))
        return parts
    if kind == "wheel":
        zc = top - r * 0.92
        parts = [tag(wheel("Pommel", zc, r, p.get("half", 0.024), fittings), "fitting")]
        # The tang is peened over a button below the wheel.
        b = zc - r * 0.97
        parts.append(tag(lathe("Pommel_Button", [(b + 0.01, 0.018), (b - 0.006, 0.022), (b - 0.02, 0.014), (b - 0.026, 0.0)], fittings, n=8, faceted=True), "fitting"))
        return parts
    if kind == "pear":
        h = p["height"]
        prof = [(top + 0.004, r * 0.48), (top - 0.07 * h, r * 0.62), (top - 0.30 * h, r * 0.92), (top - 0.52 * h, r),
                (top - 0.72 * h, r * 0.86), (top - 0.86 * h, r * 0.55), (top - 0.94 * h, r * 0.38), (top - h, 0.0)]
        return [tag(lathe("Pommel", prof, fittings, n=8, oval=0.86, faceted=True, phase=math.pi / 8), "fitting")]
    if kind == "ball":
        h = p.get("height", 2 * r)
        prof = [(top + 0.004, r * 0.5), (top - 0.15 * h, r * 0.86), (top - 0.5 * h, r), (top - 0.85 * h, r * 0.86), (top - h, 0.0)]
        return [tag(lathe("Pommel", prof, fittings, n=12), "fitting")]
    raise ValueError(kind)


def _straight(r):
    metal = material(r["metal"])
    fittings = material(r["fittings"])
    held = material(r.get("grip_material", "Wrap"))
    b, g, gr, pm = r["blade"], r["guard"], r["grip"], r["pommel"]
    parts = []
    info = {}

    blade, floor = straight_blade("Blade", b, metal)
    parts.append(blade)
    info["fuller_floor"] = floor

    if g["kind"] == "cross":
        parts.append(tag(cross_guard("Guard", g["z"], g["span"], g["height"], g["depth"], fittings, g.get("ends", "knob"),
                                     g.get("droop", 0.0), g.get("centre", 0.4), g.get("taper", 0.3), g.get("knob", 0.75)), "fitting"))
    elif g["kind"] == "disc":
        # A gladius's hilt-guard: an oval block, rounded over top and bottom.
        z0, z1, rx, ry = g["bottom"], g["top"], g["rx"], g["ry"]
        prof = [(z0, 0.72), (z0 + 0.008, 0.90), (z0 + 0.25 * (z1 - z0), 1.0), (z0 + 0.7 * (z1 - z0), 0.98), (z1 - 0.01, 0.86), (z1, 0.62)]
        sections = [ellipse(0, 0, z, rx * k, ry * k, 20) for z, k in prof]
        parts.append(tag(crisp(gg.loft("Guard", sections, fittings), 40), "fitting"))
    if g.get("rings"):
        # Side rings: a loop to each side of the ricasso, standing off the guard at an angle.
        rr, rz, rx, tilt = g["rings"]
        for side in (-1, 1):
            parts.append(tag(torus(f"Guard_Ring_{side}", (side * rx, 0, rz), rr, 0.011, fittings, tilt), "fitting"))
    if r.get("ricasso"):
        # The zweihander's ricasso: the unsharpened foot of the blade, wrapped in leather so a
        # hand can choke up on it, with the parrying lugs at its top.
        z0, z1, hx, hy = r["ricasso"]
        steps = 14
        sections = []
        for i in range(steps + 1):
            t = i / steps
            swell = 1 + 0.05 * math.sin(t * steps * math.pi)
            sections.append(box_ring_yz_z(lerp(z0, z1, t), hx * swell, hy * swell))
        parts.append(tag(gg.loft("Ricasso", sections, held), "grip"))
        lz, ll, lh = r["lugs"]
        for side in (-1, 1):
            secs = []
            for k in range(7):
                u = k / 6
                x = side * (hx * 0.85 + ll * u)
                zc = lz - 0.35 * lh * u ** 1.6                   # hooked back toward the guard
                hh = lh * (1 - 0.82 * u) * (1.15 if k == 0 else 1.0)
                dd = 0.026 * (1 - 0.6 * u)
                secs.append(ring([(x, 0, zc - hh), (x, dd, zc), (x, 0, zc + hh * 0.8), (x, -dd, zc)]))
            secs.append(ring([(side * (hx * 0.85 + ll * 1.08), 0, lz - 0.40 * lh)] * 4))
            parts.append(tag(crisp(gg.loft(f"Lug_{side}", secs, metal), 30), "edge"))

    parts.append(grip("Grip", gr["bottom"], gr["top"], gr["radius"], held, gr.get("style", "spiral"), gr.get("turns", 8),
                      gr.get("profile", "straight")))
    parts += _pommel(pm, fittings)
    return parts, info


def box_ring_yz_z(z, hx, hy, n=12):
    """A rounded rectangle in the plane z = const, for wrapping a flat bar."""
    pts = []
    for k in range(n):
        a = k * 2 * math.pi / n
        c, s = math.cos(a), math.sin(a)
        # a superellipse: flat sides, round corners
        x = hx * math.copysign(abs(c) ** 0.55, c)
        y = hy * math.copysign(abs(s) ** 0.55, s)
        pts.append(Vector((x, y, z)))
    return pts


def _curved(r):
    metal = material(r["metal"])
    fittings = material(r["fittings"])
    held = material(r.get("grip_material", "Wrap"))
    b, g, gr, pm = r["blade"], r["guard"], r["grip"], r["pommel"]
    parts = []
    blade, tip = curved_blade("Blade", b, metal)
    parts.append(blade)
    parts.append(tag(cross_guard("Guard", g["z"], g["span"], g["height"], g["depth"], fittings, g.get("ends", "knob"),
                                 g.get("droop", 0.0), g.get("centre", 0.4), g.get("taper", 0.2), g.get("knob", 1.0)), "fitting"))
    if g.get("langets"):
        # Langets: tongues of the guard lying along the blade's flats and down the grip.
        up, down = g["langets"]
        for side in (-1, 1):
            y = side * (b["thick"] + 0.006)
            for name, z_a, z_b in (("Up", g["z"], g["z"] + up), ("Down", g["z"], g["z"] - down)):
                secs = []
                for k in range(6):
                    u = k / 5
                    zz = lerp(z_a, z_b, u)
                    hw = 0.022 * (1 - u ** 1.5) + 0.002
                    secs.append(ring([(-hw, y, zz), (0, y + side * 0.006, zz), (hw, y, zz), (0, y - side * 0.002, zz)]))
                parts.append(tag(crisp(gg.loft(f"Langet_{name}_{side}", secs, fittings), 30), "fitting"))
    bend = gr.get("bend", 0.0)
    parts.append(grip("Grip", gr["bottom"], gr["top"], gr["radius"], held, gr.get("style", "spiral"), gr.get("turns", 7),
                      gr.get("profile", "straight"), bend=bend))
    if g.get("knuckle"):
        # A knuckle bow from the edge-side quillon down to the pommel, clear of the fingers.
        x0, z0 = -g["span"] * 0.82, g["z"] - 0.01
        x1, z1 = bend - 0.03, gr["bottom"] - 0.03
        pts = []
        for k in range(13):
            u = k / 12
            x = lerp(x0, x1, u) - 0.085 * math.sin(math.pi * u) ** 0.8
            z = lerp(z0, z1, u)
            pts.append((x, 0.0, z))
        parts.append(tag(tube("Knuckle_Bow", pts, lambda u: 0.012 + 0.004 * math.sin(math.pi * u), fittings), "fitting"))
    # A capped, beaked pommel, turned on along the grip's bend toward the edge.
    top = gr["bottom"]
    rr = pm["radius"]
    h = pm["height"]
    secs = []
    prof = [(0.0, 0.92), (0.15, 1.05), (0.45, 1.18), (0.75, 1.05), (0.92, 0.72), (1.0, 0.0)]
    for u, k in prof:
        z = top - h * u + 0.004
        x = bend + (-pm.get("beak", 0.04)) * u ** 2
        secs.append(ellipse(x, 0, z, rr * k, rr * k * 0.82, 12) if k > 0 else [Vector((x - 0.01, 0, z))] * 12)
    parts.append(tag(crisp(gg.loft("Pommel", secs, fittings), 40), "fitting"))
    return parts, {"tip": tip}


FAMILIES = {
    "straight": _straight,
    "curved": _curved,
}


# --- variants ----------------------------------------------------------------------------------

SPECIAL = {
    "cold-iron": "ColdIron",
    "silver": "Silver",
    "adamantine": "Adamantine",
    "mithral": "Mithral",
}
METALS = ("Steel", "DarkIron")


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
        if o.get("role") in ("edge", "fitting"):
            for i, m in enumerate(o.data.materials):
                if m is not None and m.name.split(".")[0] in METALS:
                    o.data.materials[i] = new
    return parts


# --- the recipe table --------------------------------------------------------------------------
# Units are the human models' (one unit is about 0.45 m). Grip at the origin, blade up +Z, flat
# of the blade facing Y. The seven swords keep the overall length of the models they replace to
# within a few per cent, so the hand still closes in the same place.

RECIPES = {
    # A dagger: a narrow blade two hands long tapering evenly to its point, diamond in section;
    # a short straight cross; a wound grip; a faceted scent-stopper pommel.
    "dagger": dict(
        family="straight", hands="light", metal="Steel", fittings="DarkIron",
        blade=dict(base=0.225, tip=0.80, width=0.050, thick=0.021, section="diamond", taper=0.0, point=1.0, curve=0.92, distal=0.3),
        guard=dict(kind="cross", z=0.218, span=0.135, height=0.015, depth=0.022, ends="knob", knob=0.9, centre=0.5, taper=0.2),
        grip=dict(bottom=-0.045, top=0.21, radius=0.032, style="spiral", turns=6),
        pommel=dict(kind="scent", top=-0.045, height=0.13, radius=0.046),
    ),
    # A gladius: a broad straight blade with parallel edges and a short angular point; an oval
    # hilt-guard; a bone grip ridged for four fingers; a big rounded pommel. Bronze fittings.
    "short-sword": dict(
        family="straight", hands="light", metal="Steel", fittings="Bronze", grip_material="Bone",
        blade=dict(base=0.245, tip=1.30, width=0.068, thick=0.020, section="lens", taper=0.0, point=0.17, curve=1.0, distal=0.25),
        guard=dict(kind="disc", bottom=0.165, top=0.255, rx=0.115, ry=0.078),
        grip=dict(bottom=-0.05, top=0.17, radius=0.037, style="ridged", profile="straight"),
        pommel=dict(kind="ovoid", top=-0.05, height=0.13, radius=0.085, oval=0.78, nut="Bronze"),
    ),
    # A longsword: a long slender blade tapering gently, a fuller two-thirds of its length; a long
    # straight cross; a grip for a hand and a half; a wheel pommel.
    "longsword": dict(
        family="straight", hands="one", metal="Steel", fittings="DarkIron",
        blade=dict(base=0.235, tip=2.00, width=0.070, thick=0.019, section="hex", flat=0.5, taper=0.48, point=0.12, curve=0.78,
                   fuller=(0.0, 0.66, 0.020, 0.55)),
        guard=dict(kind="cross", z=0.222, span=0.30, height=0.017, depth=0.024, ends="knob", knob=0.55, centre=0.6, taper=0.25),
        grip=dict(bottom=-0.245, top=0.21, radius=0.035, style="spiral", turns=10, profile="barrel"),
        pommel=dict(kind="wheel", top=-0.235, radius=0.080, half=0.022),
    ),
    # The same longsword in alchemical silver, polished to a mirror, fittings and all.
    "silvered-longsword": dict(like="longsword", variant="silver"),
    # A zweihander: a very long blade above a leather-wrapped ricasso with parrying lugs; a wide
    # cross with turned quillons and side rings; a grip as long as a forearm; a big pear pommel.
    "greatsword": dict(
        family="straight", hands="two", metal="Steel", fittings="DarkIron",
        blade=dict(base=0.58, tip=2.95, width=0.098, thick=0.021, section="hex", flat=0.5, taper=0.32, point=0.10, curve=0.72,
                   fuller=(0.0, 0.62, 0.026, 0.55)),
        ricasso=(0.27, 0.60, 0.068, 0.034),
        lugs=(0.615, 0.125, 0.034),
        guard=dict(kind="cross", z=0.245, span=0.43, height=0.024, depth=0.030, ends="knob", knob=0.7, droop=0.075, centre=0.6, taper=0.3,
                   rings=(0.058, 0.300, 0.118, 28)),
        grip=dict(bottom=-0.52, top=0.225, radius=0.040, style="spiral", turns=15, profile="central"),
        pommel=dict(kind="pear", top=-0.52, height=0.24, radius=0.085),
    ),
    # Aldric's: the same zweihander, gilt where the plain one is iron, a rune line in the fuller.
    "greatsword-plus-one": dict(like="greatsword", fittings="Gold", variant="+1"),
    # A scimitar: a single-edged blade sweeping back, widening toward the tip and ending in a clip
    # point; a short cross with ball quillons and langets; a grip curving toward the edge.
    "scimitar": dict(
        family="curved", hands="one", metal="Steel", fittings="DarkIron",
        blade=dict(base=0.225, length=1.57, sweep=0.32, width=0.112, flare=0.42, clip=0.78, toff=0.45, step=0.16, thick=0.022),
        guard=dict(z=0.212, span=0.15, height=0.016, depth=0.023, ends="knob", knob=1.1, centre=0.5, taper=0.15, langets=(0.075, 0.06)),
        grip=dict(bottom=-0.205, top=0.205, radius=0.033, style="spiral", turns=8, bend=0.045),
        pommel=dict(radius=0.040, height=0.085, beak=0.045),
    ),
}

# The models that are not built by a family yet, for the icons' size classes.
HANDS = {
    "greataxe": "two", "cold-iron-greataxe": "two", "light-mace": "light", "shortspear": "one",
    "quarterstaff": "two", "shortbow": "bow", "light-crossbow": "crossbow",
    "heavy-shield": "shield", "light-shield": "shield",
}


def recipe(weapon):
    r = RECIPES[weapon]
    if "like" in r:
        base = dict(recipe(r["like"]))
        base.pop("variant", None)
        base.update({k: v for k, v in r.items() if k != "like"})
        return base
    return dict(r)


def hands(weapon):
    if weapon in RECIPES:
        return recipe(weapon)["hands"]
    return HANDS.get(weapon, "one")


def build(weapon):
    """The parts of one weapon, standing up, grip at the origin, and what its family knows
    about it (where the fuller's floor is, for a rune line)."""
    r = recipe(weapon)
    parts, info = FAMILIES[r["family"]](r)
    parts = variant(parts, info, r.get("variant"))
    return parts, info

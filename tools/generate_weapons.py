"""What the party holds: one small textured model per hand-held item.

    blender -b --factory-startup --python tools/generate_weapons.py -- <output-directory> [name ...]

Generated for the same reason the goblins are — nothing to licence — and because the character
pack's own weapon meshes went with its archive.

ONE CONVENTION FOR EVERY WEAPON, so that the only orientation anybody has to discover is the
hand's: the grip is at the origin and the business end runs up Blender's +Z, which the exporter
turns into Godot's +Y. A blade's flat faces Blender's Y, so Godot's Z. A shield is the exception
that proves it: its strap point is at the origin and its face looks down Blender's -Y.

The other exception is anything you POINT rather than swing. A crossbow held by the sword's
convention aims at the sky, so it is turned to run down Blender's +Y, top side up: in the hand
that comes out along the forearm, and goes wherever the arm is aimed. Doing it here, in the
model, is what lets the game use one grip for everything it puts in a fist.

Sizes are in the HUMAN MODELS' native units, not the board's. Those models stand about four
units tall and Godot scales them down to fit their square; a weapon hung on a bone is scaled
with them, so it is modelled at the size of the hand that holds it. One unit is about 0.45 m.

Shapes are lofted from cross-sections rather than assembled from primitives, because a blade is
a thing with a fuller and a taper in two directions at once, and a cone is not. Every surface
is then baked — grain, rust, brushed steel — by `surface.py`, so the sword in Valeria's hand is
a textured sword and not a grey wedge.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import generate_goblin as gg  # noqa: E402
import surface  # noqa: E402

SILVER = gg.mat("Silvered_Steel", (0.84, 0.87, 0.93), 0.45, 0.25)
STEEL = gg.mat("Sword_Steel", (0.58, 0.59, 0.62), 0.45, 0.40)
COLD_IRON = gg.mat("Cold_Iron", (0.19, 0.21, 0.25), 0.30, 0.55)
ASH = gg.mat("Ash_Wood", (0.52, 0.37, 0.20), 0.0, 0.85)
WRAP = gg.mat("Grip_Wrap", (0.16, 0.08, 0.04), 0.0, 0.9)
surface.RECIPES["Grip_Wrap"] = lambda c, m, r: surface.leather(c)


def ring(points):
    return [Vector(p) for p in points]


def blade_section(z, w, t, fuller=0.45):
    """A blade's cross-section at height z: a flattened hexagon with a fuller down each face.

    w is the half-width, t the half-thickness. The middle of each face is dished to `fuller`
    of the full thickness, which is what catches the light down the length of a real blade.
    """
    return ring([
        (w, 0, z), (w * 0.55, t, z), (w * 0.18, t * fuller, z), (-w * 0.18, t * fuller, z),
        (-w * 0.55, t, z), (-w, 0, z), (-w * 0.55, -t, z), (-w * 0.18, -t * fuller, z),
        (w * 0.18, -t * fuller, z), (w * 0.55, -t, z),
    ])


def round_section(z, rx, ry, n=12, cx=0.0, cy=0.0):
    return ring([(cx + rx * math.cos(a), cy + ry * math.sin(a), z) for a in (i * 2 * math.pi / n for i in range(n))])


def box_section(z, hx, hy, cx=0.0, cy=0.0, chamfer=0.0):
    c = chamfer
    return ring([
        (cx + hx - c, cy - hy, z), (cx + hx, cy - hy + c, z), (cx + hx, cy + hy - c, z), (cx + hx - c, cy + hy, z),
        (cx - hx + c, cy + hy, z), (cx - hx, cy + hy - c, z), (cx - hx, cy - hy + c, z), (cx - hx + c, cy - hy, z),
    ])


def point(z, x=0.0, y=0.0, n=10):
    return ring([(x, y, z)] * n)


def wrapped_grip(name, z0, z1, radius, turns, material=WRAP):
    """A grip wound with cord: a cylinder whose radius swells and dips once per turn."""
    steps = turns * 6
    sections = []
    for i in range(steps + 1):
        t = i / steps
        swell = radius * (1.0 + 0.09 * math.sin(t * turns * 2 * math.pi))
        sections.append(round_section(z0 + (z1 - z0) * t, swell, swell, 12))
    return gg.loft(name, sections, material)


def sword(steel, length=2.0, width=0.13, curved=False, grip=0.21, guard=None, fitting=None):
    """A sword: wound grip, pommel, a guard, and a blade with a fuller.

    `grip` is how far the grip runs up from the hand — a two-handed sword's is long enough for
    both, and its pommel hangs below the lower one. Everything above the grip moves up with it.
    """
    grip_r = 0.047
    fitting = fitting or (gg.GOLD if not curved else gg.IRON)
    below = grip - 0.21                                  # how much longer than a longsword's
    parts = [
        wrapped_grip("Grip", -0.02 - below, 0.21, grip_r, max(7, int(7 * grip / 0.21))),
        gg.loft("Pommel", [point(-0.27 - below), round_section(-0.24 - below, 0.05, 0.05, 12), round_section(-0.19 - below, 0.082, 0.082, 12),
                           round_section(-0.13 - below, 0.082, 0.082, 12), round_section(-0.10 - below, 0.052, 0.052, 12), round_section(-0.02 - below, 0.05, 0.05, 12)],
                fitting),
    ]

    # The guard: a bar that thickens at the middle and droops at the ends toward the blade.
    guard_w = guard if guard is not None else (0.30 if not curved else 0.19)
    sections = []
    for i in range(9):
        t = i / 8 * 2 - 1
        droop = 0.045 * t * t * (1 if curved else -1)
        half = 0.028 * (1.0 - 0.45 * abs(t)) + 0.01
        sections.append(ring([(guard_w / 2 * t, y, 0.24 + droop + z) for y, z in ((-0.045, -half), (0.045, -half), (0.045, half), (-0.045, half))]))
    parts.append(gg.loft("Guard", sections, fitting, smooth_shading=False))

    # The blade: wide and thick at the shoulder, running to a point in both dimensions, with
    # the fuller fading out before the tip. A scimitar's is swept back and single-edged.
    sections = [blade_section(0.22, width * 0.9, 0.026, 0.5)]
    base = 0.26
    steps = 14
    for i in range(steps + 1):
        t = i / steps
        z = base + (length - base) * t
        w = width * (1.0 - 0.72 * t ** 2.2) if not curved else width * (1.0 + 0.35 * math.sin(t * math.pi) - 0.9 * t ** 3)
        th = 0.030 * (1.0 - 0.70 * t)
        sec = blade_section(z, w, th, 0.45 + 0.5 * t ** 2)
        if curved:
            sweep = 0.34 * t * t
            sec = ring([(p.x + sweep + w * 0.35, p.y, p.z) for p in sec])        # the edge carries forward
        sections.append(sec)
    tip = Vector((0.34 + width * 0.35, 0, length)) if curved else Vector((0, 0, length))
    sections.append(point(tip.z + 0.06, tip.x, 0))
    parts.append(gg.loft("Blade", sections, steel))
    return parts


def greataxe(steel):
    parts = [
        gg.loft("Haft", [round_section(-0.62, 0.046, 0.046, 10), round_section(0.0, 0.05, 0.05, 10),
                         round_section(1.6, 0.052, 0.052, 10), round_section(2.52, 0.046, 0.046, 10)], ASH),
        wrapped_grip("Haft_Wrap", -0.25, 0.28, 0.056, 10),
        gg.loft("Spike", [round_section(2.50, 0.055, 0.055, 8), round_section(2.56, 0.04, 0.04, 8), point(2.86, n=8)], steel),
        gg.loft("Langet", [box_section(1.35, 0.018, 0.064, 0, 0, 0.004), box_section(2.46, 0.018, 0.064, 0, 0, 0.004)], steel, smooth_shading=False),
        gg.loft("Socket", [box_section(1.86, 0.10, 0.075, 0, 0, 0.02), box_section(2.30, 0.10, 0.075, 0, 0, 0.02)], steel, smooth_shading=False),
    ]
    # Two bearded blades, each lofted outward from the socket: thick where it is seated on the
    # haft, hollow-ground on the way, and thinning to an edge. The outline is the beard shape;
    # each loft ring is that outline shrunk toward the socket, at one thickness.
    outline = [(0.07, 1.84), (0.26, 1.66), (0.56, 1.52), (0.78, 1.74), (0.84, 2.06), (0.78, 2.40), (0.56, 2.62), (0.26, 2.50), (0.07, 2.30)]
    cx, cz = 0.07, 2.07
    for side in (-1, 1):
        sections = []
        for frac, thick in ((0.0, 0.055), (0.45, 0.034), (0.80, 0.012), (1.0, 0.0025)):
            front = [(side * (cx + (x - cx) * frac + 0.001), thick, cz + (z - cz) * frac) for (x, z) in outline]
            back = [(x, -y, z) for (x, y, z) in front]
            sections.append(ring(front + list(reversed(back))))
        parts.append(gg.loft(f"AxeBlade_{'L' if side < 0 else 'R'}", sections, steel))
    for j in range(3):
        for y in (0.075, -0.075):
            parts.append(gg.uv(f"Rivet_{j}_{y > 0}", (0, y, 1.95 + 0.14 * j), (0.02,) * 3, steel, 8, 5))
    return parts


def quarterstaff():
    # Held at the middle, so the origin is there too and it reaches as far down as up.
    parts = [
        gg.loft("Staff", [round_section(-1.55, 0.040, 0.040, 10), round_section(-0.6, 0.048, 0.048, 10),
                          round_section(0.0, 0.050, 0.050, 10), round_section(0.9, 0.047, 0.047, 10),
                          round_section(2.25, 0.038, 0.038, 10)], ASH),
        wrapped_grip("Staff_Wrap", -0.36, 0.36, 0.056, 14),
        gg.loft("Shoe_Top", [round_section(2.10, 0.052, 0.052, 10), round_section(2.26, 0.046, 0.046, 10), round_section(2.30, 0.03, 0.03, 10), point(2.31, n=10)], gg.IRON),
        gg.loft("Shoe_Foot", [point(-1.61, n=10), round_section(-1.60, 0.03, 0.03, 10), round_section(-1.56, 0.052, 0.052, 10), round_section(-1.40, 0.054, 0.054, 10)], gg.IRON),
    ]
    for z in (0.62, -0.85, 1.4):
        parts.append(gg.loft(f"Knot_{z}", [round_section(z - 0.06, 0.047, 0.047, 10), round_section(z, 0.058, 0.055, 10), round_section(z + 0.07, 0.047, 0.047, 10)], ASH))
    return parts


def bow_limbs(reach, belly, thick):
    """A bow standing up the Z axis, belly forward (-Y), held at its middle, its limbs
    tapering to recurved tips. The grip is where the hand is, so it is where the origin is."""
    sections = []
    steps = 24
    for i in range(steps + 1):
        t = i / steps * 2 - 1
        z = reach * t
        y = -belly * (1 - t * t) + belly * 0.22 * t ** 6 + belly         # the tips curl back forward
        taper = 1.0 - 0.6 * abs(t) ** 1.5
        sections.append(round_section(z, thick * 1.2 * taper, thick * 0.75 * taper + 0.004, 10, 0.0, y))
    parts = [gg.loft("Bow_Limb", sections, gg.BOW_WOOD)]
    for t in (-1, 1):
        z = reach * t
        parts.append(gg.loft(f"Nock_{t}", [round_section(z - 0.03 * t, 0.02, 0.02, 8, 0, belly * 1.22), round_section(z + 0.04 * t, 0.014, 0.014, 8, 0, belly * 1.22), point(z + 0.05 * t, 0, belly * 1.22, 8)], gg.CLAW))
    top, foot = Vector((0, belly * 1.22, reach)), Vector((0, belly * 1.22, -reach))
    parts.append(gg.limb("Bow_String", foot, top, 0.009, gg.BOW_STRING))
    parts.append(wrapped_grip("Bow_Grip", -0.17, 0.17, thick * 1.45, 6))
    return parts


def crossbow():
    """Built standing up like everything else — stock up Z — and laid forward by build()."""
    parts = []
    # The tiller: deep at the butt, slimmer forward, its underside swept in a curve.
    sections = []
    for i in range(11):
        t = i / 10
        z = -0.55 + 2.0 * t
        depth = 0.085 - 0.03 * t + 0.03 * math.sin(t * math.pi)
        width = 0.062 - 0.012 * t
        sections.append(box_section(z, width, depth, 0.0, 0.03 * (1 - t) ** 2 - 0.02 * t, 0.012))
    parts.append(gg.loft("Stock", sections, ASH, smooth_shading=False))
    parts.append(gg.loft("Butt", [box_section(-0.60, 0.07, 0.12, 0, 0.05, 0.02), box_section(-0.40, 0.066, 0.11, 0, 0.045, 0.02)], ASH, smooth_shading=False))
    # Trigger lever under the stock, and the nut it releases on top.
    parts.append(gg.loft("Trigger", [box_section(0.02, 0.01, 0.07, 0, 0.12, 0.003), box_section(0.22, 0.01, 0.05, 0, 0.14, 0.003)], gg.IRON, smooth_shading=False))
    parts.append(gg.cyl("Nut", (0, -0.085, 0.34), 0.03, 0.14, gg.IRON, (0, math.pi / 2, 0), 10))
    # The bolt in the groove, and a stirrup at the nose for spanning.
    parts.append(gg.loft("Bolt", [round_section(0.30, 0.013, 0.013, 6, 0, -0.10), round_section(1.30, 0.013, 0.013, 6, 0, -0.10),
                                  round_section(1.36, 0.024, 0.024, 6, 0, -0.10), point(1.50, 0, -0.10, 6)], gg.BOW_WOOD))
    stirrup = gg.ring("Stirrup", (0, 0.06, 1.52), 0.075, 0.012, gg.IRON)
    stirrup.scale = (1.0, 0.5, 1.4)
    parts.append(stirrup)
    # The prod: a short stiff bow across the nose, tapering to its tips, with a string.
    sections = []
    for i in range(13):
        t = i / 12 * 2 - 1
        x = 0.74 * t
        z = 1.30 - 0.20 * t * t
        taper = 1.0 - 0.55 * abs(t)
        sections.append(ring([(x, -0.03 * taper - 0.03, z - 0.018 * taper), (x, 0.03 * taper - 0.03, z - 0.018 * taper),
                              (x, 0.03 * taper - 0.03, z + 0.018 * taper), (x, -0.03 * taper - 0.03, z + 0.018 * taper)]))
    parts.append(gg.loft("Prod", sections, gg.IRON))
    parts.append(gg.limb("Prod_String", Vector((-0.74, -0.03, 1.10)), Vector((0.74, -0.03, 1.10)), 0.009, gg.BOW_STRING))
    parts.append(gg.loft("Prod_Binding", [box_section(1.20, 0.08, 0.12, 0, -0.01, 0.01), box_section(1.40, 0.08, 0.12, 0, -0.01, 0.01)], WRAP, smooth_shading=False))
    return parts


def plank(name, x0, x1, radius, thick, dome, material):
    """One board of a round shield: a strip between two chords, bowed forward in the middle."""
    def bow(x):
        return -dome * (1 - (x / radius) ** 2)

    n = 6
    xs = [x0 + (x1 - x0) * i / n for i in range(n + 1)]
    front = [(x, bow(x) - thick / 2, math.sqrt(max(0.0, radius * radius - x * x))) for x in xs]
    front += [(x, bow(x) - thick / 2, -math.sqrt(max(0.0, radius * radius - x * x))) for x in reversed(xs)]
    back = [(x, y + thick, z) for (x, y, z) in front]
    return gg.loft(name, [ring(front), ring(back)], material, smooth_shading=False)


def shield(radius, heavy):
    face = (math.pi / 2, 0, 0)
    iron = STEEL if heavy else gg.IRON
    parts = []
    boards = 5 if heavy else 4
    gap = 0.012
    width = (2 * radius - gap * (boards - 1)) / boards
    for i in range(boards):
        x0 = -radius + i * (width + gap)
        parts.append(plank(f"Board_{i}", x0, x0 + width, radius * 0.985, 0.075, 0.09, gg.WOOD))
    parts.append(gg.ring("Rim", (0, 0.0, 0), radius, 0.045, iron, face))
    boss = gg.dome("Boss", (0, -0.085, 0), (radius * 0.27, radius * 0.27, radius * 0.22), iron, 0.0, 16, 9)
    boss.rotation_euler = (math.pi / 2, 0, 0)
    parts.append(boss)
    parts.append(gg.ring("Boss_Rim", (0, -0.10, 0), radius * 0.29, 0.016, iron, face))
    # Strapping across the back: the arm goes through one, the hand takes the other.
    parts.append(gg.loft("Strap", [box_section(-radius * 0.55, 0.055, 0.012, 0, 0.09), box_section(radius * 0.55, 0.055, 0.012, 0, 0.09)], gg.LEATHER, smooth_shading=False))
    parts.append(gg.cyl("Handle", (0, 0.08, 0), 0.024, radius * 0.5, gg.LEATHER, (0, 0, 0), 8))
    if heavy:
        parts.append(gg.ring("Band", (0, -0.112, 0), radius * 0.62, 0.02, iron, face))
        band = gg.cube("Colours", (0, -0.118, 0), (radius * 0.90, 0.004, 0.11), gg.CLOTH_RED)
        band.rotation_euler = (0, math.radians(40), 0)
        parts.append(band)
    rivets = 10 if heavy else 8
    for j in range(rivets):
        a = j * 2 * math.pi / rivets + 0.2
        parts.append(gg.uv(f"Rivet_{j}", (radius * 0.86 * math.cos(a), -0.11, radius * 0.86 * math.sin(a)), (0.03,) * 3, iron, 8, 5))
    for j in range(6 if heavy else 0):
        a = j * 2 * math.pi / 6 + 0.5
        parts.append(gg.uv(f"Rivet_In_{j}", (radius * 0.62 * math.cos(a), -0.125, radius * 0.62 * math.sin(a)), (0.026,) * 3, iron, 8, 5))
    return parts


def mace(steel):
    """A light mace: a short haft and a head of six flanges round an iron core."""
    parts = [
        gg.loft("Haft", [round_section(-0.30, 0.040, 0.040, 10), round_section(0.0, 0.044, 0.044, 10), round_section(0.95, 0.040, 0.040, 10)], ASH),
        wrapped_grip("Haft_Wrap", -0.22, 0.20, 0.050, 8),
        gg.loft("Pommel", [point(-0.36, n=10), round_section(-0.34, 0.05, 0.05, 10), round_section(-0.29, 0.058, 0.058, 10), round_section(-0.25, 0.045, 0.045, 10)], gg.IRON),
        gg.loft("Collar", [round_section(0.86, 0.055, 0.055, 10), round_section(0.95, 0.066, 0.066, 10)], steel, smooth_shading=False),
        gg.uv("Core", (0, 0, 1.08), (0.10, 0.10, 0.13), steel, 14, 10),
        gg.loft("Cap", [round_section(1.20, 0.06, 0.06, 10), point(1.29, n=10)], steel),
    ]
    for i in range(6):
        a = i * math.pi / 3
        c, s_ = math.cos(a), math.sin(a)
        # A flange: a blade of iron standing out from the core, deepest in the middle.
        outline = [(0.05, 0.92), (0.16, 0.98), (0.19, 1.08), (0.16, 1.18), (0.05, 1.24)]
        rings = []
        for t in (-0.018, 0.018):
            rings.append(ring([(x * c - t * s_, x * s_ + t * c, z) for x, z in outline]))
        parts.append(gg.loft(f"Flange_{i}", rings, steel, smooth_shading=False))
    return parts


def spear(steel):
    """A short spear: an ash shaft, a leaf blade with a midrib, an iron butt cap."""
    parts = [
        gg.loft("Shaft", [round_section(-1.05, 0.036, 0.036, 10), round_section(0.0, 0.042, 0.042, 10), round_section(1.85, 0.036, 0.036, 10)], ASH),
        wrapped_grip("Shaft_Wrap", -0.20, 0.25, 0.046, 8),
        gg.loft("Butt", [point(-1.14, n=8), round_section(-1.10, 0.03, 0.03, 8), round_section(-0.98, 0.042, 0.042, 8)], gg.IRON),
        gg.loft("Socket", [round_section(1.78, 0.042, 0.042, 8), round_section(1.98, 0.034, 0.034, 8)], steel),
    ]
    sections = []
    for i in range(9):
        t = i / 8
        z = 1.98 + 0.55 * t
        w = 0.11 * math.sin(math.pi * min(1.0, t * 1.15) ** 0.8) * (1 - 0.15 * t) + 0.005
        sections.append(blade_section(z, w, 0.022 * (1 - 0.6 * t) + 0.004, 0.9))
    sections.append(point(2.6, n=10))
    parts.append(gg.loft("Head", sections, steel))
    return parts


RUNE_STEEL = gg.mat("Rune_Steel", (0.60, 0.66, 0.78), 0.45, 0.30)
surface.RECIPES["Rune_Steel"] = lambda c, m, r: surface.steel(c, 0.75)


WEAPONS = {
    "greatsword": lambda: sword(STEEL, length=2.75, width=0.16, grip=0.62, guard=0.40),
    # The merchant's sword: the same blade forged better, bright steel with a cold blue to it,
    # and gold where the plain one has iron. Which of the two Aldric holds should be visible.
    "greatsword-plus-one": lambda: sword(RUNE_STEEL, length=2.75, width=0.16, grip=0.62, guard=0.44),
    "short-sword": lambda: sword(STEEL, length=1.25, width=0.12, guard=0.24, fitting=gg.IRON),
    "dagger": lambda: sword(STEEL, length=0.70, width=0.10, guard=0.18, fitting=gg.IRON),
    "light-mace": lambda: mace(STEEL),
    "shortspear": lambda: spear(STEEL),
    "longsword": lambda: sword(STEEL),
    "silvered-longsword": lambda: sword(SILVER),
    "scimitar": lambda: sword(STEEL, length=1.75, width=0.15, curved=True),
    "greataxe": lambda: greataxe(STEEL),
    "cold-iron-greataxe": lambda: greataxe(COLD_IRON),
    "quarterstaff": quarterstaff,
    "shortbow": lambda: bow_limbs(1.25, 0.42, 0.034),
    "light-crossbow": crossbow,
    "heavy-shield": lambda: shield(0.80, heavy=True),
    "light-shield": lambda: shield(0.60, heavy=False),
}

POINTED = {"light-crossbow"}

# Modelled at human size, about 0.45 m to the unit against the goblins' 0.6, so the surface
# noise is told to run a third coarser to keep the same grain per hand.
surface.SCALE = 1.33


def build(name, directory, keep=None):
    gg.clear_scene()
    parts = WEAPONS[name]()
    bpy.context.view_layer.update()

    surface.finish([(name, parts, 1024, True)], keep=keep)

    # One mesh: a sword is one thing, and should be one draw call.
    bpy.ops.object.select_all(action="DESELECT")
    for o in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    whole = bpy.context.view_layer.objects.active
    whole.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    if name in POINTED:
        # Built standing up like everything else, then laid forward: length to +Y, top to +Z.
        for v in whole.data.vertices:
            v.co = Vector((v.co.x, v.co.z, -v.co.y))

    lo = Vector((min(v.co[i] for v in whole.data.vertices) for i in range(3)))
    hi = Vector((max(v.co[i] for v in whole.data.vertices) for i in range(3)))
    tris = sum(len(p.vertices) - 2 for p in whole.data.polygons)

    path = os.path.join(directory, f"{name}.glb")
    gg.export_glb(path)
    print(f"WEAPON {name:20} {tris:5} tris   z[{lo.z:+.2f},{hi.z:+.2f}]  x[{lo.x:+.2f},{hi.x:+.2f}]  y[{lo.y:+.2f},{hi.y:+.2f}]")


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        raise RuntimeError("Usage: blender -b --factory-startup --python generate_weapons.py -- OUTPUT_DIRECTORY")
    os.makedirs(args[0], exist_ok=True)
    for weapon in (args[1:] or WEAPONS):
        build(weapon, args[0])

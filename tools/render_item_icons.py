"""Inventory icons for everything that is not a weapon: armour, rings, coins, valuables, the
party's bag, and the containers loot is taken from.

    blender -b --factory-startup --python tools/render_item_icons.py -- <output-directory> [id ...] [--props <glb-directory>]

One 256-pixel square transparent PNG per id, drawn exactly as `render_icons.py` draws the
weapons — its lights, its sky, its camera, its margin, imported from it rather than copied, so
the two sets stay one set. The weapons' icons are drawn from the models the game holds; most of
these things have no model in the game, so each is modelled here, in the same scale as the
props (one unit a five-foot square), and drawn with the same procedural surfaces.

ICONS KEEP RELATIVE SIZE WITHIN A FAMILY. Every icon in a family is drawn at one scale, set so
the largest of them fills its share of the slot (`FAMILIES`): armour fills the slot and a ring
does not, a chain shirt hangs longer than a breastplate, a die is smaller than a tankard.

Container icons (`container-<name>`) are the prop models from `art/props/`, drawn with the lid
open (turned as the game turns it, or lifted off), for the loot window's title.
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import render_icons as ri  # noqa: E402
import surface  # noqa: E402
import props_surface as ps  # noqa: E402
import props_parts as pp  # noqa: E402
import props_cloth as pc  # noqa: E402

PROPS = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "src", "Ironbound.Game", "art", "props")

# Share of the slot the largest member of each family fills.
FAMILIES = {
    "armour": 1.0,
    "coins": 0.72,
    "trinkets": 0.62,
    "valuables": 0.86,
    "bag": 0.94,
    "containers": 0.95,
}


def mat(key, recipe, *args, **kw):
    """A procedural material at the props' scale, named so a rebuild reuses it."""
    m = bpy.data.materials.get(key)
    if m is not None:
        return m
    saved = surface.SCALE
    surface.SCALE = ps.SCALE
    try:
        m = recipe(*args, **kw)
    finally:
        surface.SCALE = saved
    m.name = key
    return m


def assign(obj, material):
    obj.data.materials.clear()
    obj.data.materials.append(material)
    return obj


def shade_smooth(obj, angle=40.0):
    for p in obj.data.polygons:
        p.use_smooth = True
    if angle:
        obj.data.set_sharp_from_angle(angle=math.radians(angle))
    return obj


def new_object(name, bm, material, angle=40.0):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    o = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(o)
    o.data.materials.append(material)
    return shade_smooth(o, angle)


# --- metals for icons ------------------------------------------------------------------------
# The icon world has a sky to reflect (render_icons.light), so metal here can be metal.

def metal(colour, rough=0.32, metallic=0.9, wear=0.6, name="Icon_Metal"):
    m, nt, bsdf = surface._tree(name)
    co = surface._coords(nt)
    edges = surface._edges(nt, 6.0)
    tone = surface._shade(nt, colour, surface._span(nt, ps._n(nt, co, 30.0, 3), 0.82, 1.08))
    tone = surface._mix(nt, tone, tuple(min(1.0, c * 1.25 + 0.05) for c in colour), surface._math(nt, "MULTIPLY", edges, wear))
    grime = ps._grey(nt, surface._ramp(nt, ps._n(nt, co, 60.0, 3), [(0.55, 0.0), (0.75, 1.0)]))
    tone = surface._shade(nt, tone, surface._span(nt, grime, 1.0, 0.7))
    r = surface._math(nt, "ADD", rough, surface._math(nt, "MULTIPLY", grime, 0.25))
    n = ps._bump(nt, ps._n(nt, co, 90.0, 2), 0.12, 0.001)
    surface._finish(nt, bsdf, tone, r, n, metallic)
    return m


def icon_leather(colour, rub=0.22):
    """Leather for icons: fine grain, a mottle, small creases, edges a little rubbed. The
    props' leather goes pale wherever the mesh is curved, and on a garment that is everywhere."""
    m, nt, bsdf = surface._tree("Surface_IconLeather")
    co = surface._coords(nt)
    grain = ps._n(nt, co, 400.0, 4, 0.6)
    mottle = ps._n(nt, co, 14.0, 3, 0.6)
    creases = ps._grey(nt, surface._ramp(nt, ps._v(nt, co, 90.0), [(0.0, 0.0), (0.04, 1.0)]))
    tone = surface._shade(nt, colour, surface._span(nt, mottle, 0.72, 1.2))
    tone = surface._shade(nt, tone, surface._span(nt, grain, 0.85, 1.08))
    tone = surface._shade(nt, tone, surface._span(nt, creases, 0.7, 1.0))
    tone = surface._mix(nt, tone, tuple(min(1.0, c * 1.6 + 0.03) for c in colour), surface._math(nt, "MULTIPLY", surface._edges(nt, 4.0), rub))
    n = ps._bump(nt, grain, 0.25, 0.0006)
    n = ps._bump(nt, creases, 0.35, 0.0015, n)
    surface._finish(nt, bsdf, tone, surface._span(nt, mottle, 0.5, 0.75), n)
    return m


def gem(colour, name="Icon_Gem", rough=0.05, glow=0.0):
    m, nt, bsdf = surface._tree(name)
    surface._finish(nt, bsdf, colour, rough, None, 0.0)
    bsdf.inputs["Coat Weight"].default_value = 1.0
    if glow:
        bsdf.inputs["Emission Color"].default_value = (*colour, 1.0)
        bsdf.inputs["Emission Strength"].default_value = glow
    return m


def flat(colour, rough=0.6, name="Icon_Flat"):
    m, nt, bsdf = surface._tree(name)
    surface._finish(nt, bsdf, colour, rough)
    return m


# --- the body armour hangs on ------------------------------------------------------------------
# Heights from the hem (0) to the top of the shoulders; half-widths across (x) and front to back
# (y) of a man's trunk at that height, in board units (a man is 1.4 tall).

TRUNK = [(0.00, 0.172, 0.112), (0.10, 0.160, 0.104), (0.20, 0.150, 0.100), (0.30, 0.160, 0.108),
         (0.40, 0.178, 0.120), (0.48, 0.186, 0.124), (0.54, 0.186, 0.116), (0.585, 0.165, 0.098),
         (0.62, 0.120, 0.072), (0.64, 0.070, 0.055)]


def trunk(z):
    z = max(TRUNK[0][0], min(TRUNK[-1][0], z))
    for (z0, x0, y0), (z1, x1, y1) in zip(TRUNK, TRUNK[1:]):
        if z0 <= z <= z1:
            t = (z - z0) / (z1 - z0)
            t = t * t * (3 - 2 * t)
            return x0 + (x1 - x0) * t, y0 + (y1 - y0) * t
    return TRUNK[-1][1], TRUNK[-1][2]


def on_trunk(theta, z, offset=0.0, power=2.6):
    """A point on the trunk at angle theta (0 the front middle, +pi/2 the wearer's left side,
    which is the viewer's right) and height z, pushed out `offset` along the surface normal."""
    hx, hy = trunk(z)
    s, c = math.sin(theta), math.cos(theta)
    e = 2.0 / power
    x = hx * math.copysign(abs(s) ** e, s)
    y = -hy * math.copysign(abs(c) ** e, c)
    n = Vector((x / (hx * hx), y / (hy * hy), 0.0))
    # Chest: the trunk swells forward over the ribs.
    chest = math.exp(-((z - 0.44) / 0.10) ** 2) * max(0.0, c) ** 2 * 0.018
    p = Vector((x, y - chest, z))
    return p + n.normalized() * offset


def panel(name, material, zb, zt, offset, cols=40, rows=30, back=False, keel=0.0, flare=0.0, folds=0.0, seed=0.0, uv_scale=1.0):
    """A garment panel wrapped on the trunk: across from side to side (u in -1..1), and at each
    u from zb(u) up to zt(u). `keel` pushes the middle forward (a breastplate's ridge), `flare`
    pushes the lower edge out, `folds` hangs vertical folds in it (cloth and mail). UVs are
    (arc across, height), in board units times `uv_scale`."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    grid = []
    for i in range(cols + 1):
        u = -1 + 2 * i / cols
        lo, hi = zb(u), zt(u)
        col = []
        for j in range(rows + 1):
            s = j / rows
            z = lo + (hi - lo) * s
            theta = u * math.pi / 2
            if back:
                theta = math.pi - theta
            off = offset + keel * max(0.0, 1 - abs(u) / 0.25) ** 2 * math.exp(-((z - 0.42) / 0.2) ** 2)
            off += flare * max(0.0, 1 - (z - lo) / 0.08) ** 2
            if folds:
                hang = max(0.0, 1 - (z - lo) / 0.35)
                off += folds * hang * (0.5 + 0.5 * math.sin(u * 9 + 1.7 * noise.noise(Vector((u * 2, z * 3, seed)))))
            p = on_trunk(theta, z, off)
            col.append((bm.verts.new(p), (u * 0.35 * uv_scale, z * uv_scale)))
        grid.append(col)
    for i in range(cols):
        for j in range(rows):
            quad = (grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1])
            f = bm.faces.new([q[0] for q in quad])
            for loop, (_, co) in zip(f.loops, quad):
                loop[uv].uv = co
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    coords = [[v.co.copy() for v, _ in col] for col in grid]
    o = new_object(name, bm, material, 0)
    return o, coords


def thicken(obj, t, offset=-1.0):
    m = obj.modifiers.new("Thick", "SOLIDIFY")
    m.thickness = t
    m.offset = offset
    return obj


def edge_tube(name, material, points, radius, sides=6):
    bm = pp.sweep([Vector(p) for p in points], pp.round_profile(radius, sides), None)
    o = new_object(name, bm, material, 0)
    return o


def column_edge(grid, j_from_top=False, which=None):
    """The points along a panel's top or bottom edge, left to right."""
    return [col[-1 if j_from_top else 0].copy() for col in grid]


def studs_on(name, material, zb, zt, offset, du, dz, radius, umax=0.9, back=False, jitter=0.0, rng=None):
    bm = bmesh.new()
    rng = rng or random.Random(1)
    u = -umax
    spots = []
    while u <= umax + 1e-6:
        z = zb(u) + dz * 0.6
        while z < zt(u) - dz * 0.4:
            th = u * math.pi / 2
            if back:
                th = math.pi - th
            p = on_trunk(th, z, offset)
            n = (on_trunk(th, z, offset + 0.01) - p).normalized()
            spots.append((p, n))
            z += dz
        u += du
    for p, n in spots:
        pp.nail_head(bm, p, n, radius, rng, height=radius * 0.7, segments=8)
    return new_object(name, bm, material, 70)


# --- armour -------------------------------------------------------------------------------------

def neckline(depth, shoulder=0.30, top=0.605, arm=0.44):
    """The top edge of a front panel: a scoop at the neck, up over the collarbones, and down
    round the armholes to the side."""
    def zt(u):
        a = abs(u)
        if a < shoulder:
            return top - depth * math.cos(a / shoulder * math.pi / 2)
        t = (a - shoulder) / (1 - shoulder)
        return top - (top - arm) * (t ** 1.4)
    return zt


def breastplate(rng):
    steel = mat("Icon_Steel", metal, (0.62, 0.63, 0.66), 0.22, 0.95, 0.5, name="Icon_Steel")
    leather = mat("Icon_Leather", icon_leather, (0.20, 0.10, 0.045))
    brass = mat("Icon_Brass", metal, (0.70, 0.52, 0.22), 0.3, 0.9, 0.4, name="Icon_Brass")
    parts = []
    zt = neckline(0.07, 0.30, 0.60, 0.43)
    zb = lambda u: 0.10 + 0.02 * u * u
    front, grid = panel("Plate", steel, zb, zt, 0.012, keel=0.018, flare=0.016)
    parts.append(thicken(front, 0.006))
    back, _ = panel("Back", steel, lambda u: 0.11, neckline(0.02, 0.3, 0.61, 0.43), 0.012, back=True)
    parts.append(thicken(back, 0.006))
    # Rolled edges round the neck and the arms, and the hem turned out.
    top = column_edge(grid, True)
    parts.append(edge_tube("NeckRoll", steel, top, 0.0055))
    parts.append(edge_tube("HemRoll", steel, column_edge(grid, False), 0.004))
    # Two lames of a fauld below.
    for k in range(2):
        z0 = 0.10 - 0.045 * (k + 1)
        lame, g2 = panel(f"Lame_{k}", steel, lambda u, z0=z0: z0 + 0.02 * u * u, lambda u, z0=z0: z0 + 0.055 + 0.02 * u * u, 0.020 + 0.006 * (k + 1), cols=30, rows=4, flare=0.006)
        parts.append(thicken(lame, 0.005))
        parts.append(edge_tube(f"LameRoll_{k}", steel, column_edge(g2, False), 0.003))
        for u in (-0.85, 0.0, 0.85):
            p = on_trunk(u * math.pi / 2, z0 + 0.04, 0.03 + 0.006 * (k + 1))
            parts.append(rivet(f"LameRivet_{k}_{u}", brass, p, (p - on_trunk(u * math.pi / 2, z0 + 0.04, 0.0)).normalized(), 0.0055, rng))
    # Shoulder straps over to the back, buckled at the front.
    for side in (-1, 1):
        u = side * 0.36
        a = on_trunk(u * math.pi / 2, zt(u) - 0.01, 0.022)
        path = [a, a + Vector((0, 0.03, 0.035)), Vector((a.x * 0.95, 0.0, 0.66)), Vector((a.x * 0.92, 0.07, 0.64)), Vector((a.x * 0.9, 0.12, 0.6))]
        parts.append(new_object(f"Strap_{side}", pp.sweep(path, pp.band_profile(0.03, 0.004), None), leather, 60))
        parts.append(buckle(f"Buckle_{side}", brass, a + Vector((0, -0.006, -0.02)), rng))
    # Rivets round the plate's edge.
    for i, p in enumerate(top[3:-3:3]):
        parts.append(rivet(f"EdgeRivet_{i}", brass, p + Vector((0, -0.006, -0.012)), Vector((0, -1, 0.2)), 0.0045, rng))
    return parts


def rivet(name, material, at, normal, radius, rng):
    bm = bmesh.new()
    pp.nail_head(bm, at, normal, radius, rng, height=radius * 0.75, segments=8)
    return new_object(name, bm, material, 70)


def buckle(name, material, at, rng, size=0.02):
    """A square frame buckle: a four-sided ring turned square to the strap."""
    o = pp.torus(name, material, (0, 0, 0), size * 0.6, size * 0.12, normal=(0, 1, 0), segs=4, minor_segs=4)
    o.rotation_euler = (0, math.radians(45), 0)
    o.location = Vector(at)
    return o


def leather_cuirass(rng, studded=False):
    colour = (0.075, 0.04, 0.02) if studded else (0.13, 0.065, 0.03)
    leather = mat("Icon_Leather_S" if studded else "Icon_Leather_L", icon_leather, colour)
    dark = mat("Icon_Leather_Dark", icon_leather, (0.06, 0.032, 0.016))
    iron = mat("Icon_Rivet", metal, (0.55, 0.55, 0.56), 0.35, 0.9, 0.7, name="Icon_Rivet")
    brass = mat("Icon_Brass", metal, (0.70, 0.52, 0.22), 0.3, 0.9, 0.4, name="Icon_Brass")
    parts = []
    zt = neckline(0.05, 0.32, 0.61, 0.45)
    zb = lambda u: 0.13 + 0.015 * u * u
    front, grid = panel("Cuirass", leather, zb, zt, 0.012, keel=0.006, folds=0.0)
    parts.append(thicken(front, 0.008))
    back, _ = panel("Back", leather, lambda u: 0.13, neckline(0.02, 0.32, 0.62, 0.45), 0.012, back=True)
    parts.append(thicken(back, 0.008))
    parts.append(edge_tube("Collar", dark, column_edge(grid, True), 0.007))
    parts.append(edge_tube("Hem", dark, column_edge(grid, False), 0.006))
    # Shoulder guards: two overlapping shells over each shoulder.
    for side in (-1, 1):
        for k in range(2):
            c = Vector((side * (0.165 + 0.012 * k), 0.0, 0.585 - 0.04 * k))
            o = pp.heap(f"Pauldron_{side}_{k}", leather, (0, 0, 0), (0.075 - 0.006 * k, 0.085, 0.05), rng, res=14, bumps=0.02, freq=4)
            bmesh_open_bottom(o, 0.0)
            o.matrix_world = Matrix.Translation(c) @ Matrix.Rotation(side * math.radians(-38 - 10 * k), 4, "Y")
            thicken(o, 0.006, 1.0)
            assign(o, leather)
            parts.append(o)
            if studded:
                for j in range(5):
                    a = math.radians(-60 + 30 * j)
                    p = Matrix.Translation(c) @ Matrix.Rotation(side * math.radians(-38 - 10 * k), 4, "Y") @ Vector((0.0, 0.07 * math.sin(a), 0.045 * math.cos(a)))
                    parts.append(rivet(f"PStud_{side}_{k}_{j}", iron, p, (p - c).normalized(), 0.0055, rng))
    # Strips hanging from the hem.
    for i in range(7):
        u = -0.84 + i * 0.28
        th = u * math.pi / 2
        top_p = on_trunk(th, zb(u) + 0.01, 0.022)
        nrm = (on_trunk(th, zb(u), 0.03) - on_trunk(th, zb(u), 0.0)).normalized()
        part = pp.slab(f"Strip_{i}", leather, rng, top_p + Vector((0, 0, -0.06)) + nrm * 0.006, (0, 0, 1), nrm, 0.12, 0.068, 0.007, seg=0.05, bevel=0.003, wear=0.6, chips=1)
        assign(part, leather)
        parts.append(part)
        if studded:
            for k in range(3):
                p = top_p + Vector((0, 0, -0.03 - 0.035 * k)) + nrm * 0.011
                parts.append(rivet(f"SStud_{i}_{k}", iron, p, nrm, 0.0055, rng))
    if studded:
        parts.append(studs_on("Studs", iron, lambda u: zb(u) + 0.01, lambda u: zt(u) - 0.02, 0.021, 0.115, 0.038, 0.0058, 0.88, rng=rng))
    else:
        # Stitched seams down the front.
        for side in (-1, 1):
            pts = [on_trunk(side * 0.3 * math.pi / 2, z, 0.021) for z in [0.15 + 0.03 * k for k in range(15)]]
            parts.append(edge_tube(f"Seam_{side}", dark, pts, 0.0025, 4))
    # A belt.
    belt = [on_trunk(-math.pi / 2 + math.pi * k / 30, 0.2, 0.024) for k in range(31)]
    parts.append(new_object("Belt", pp.sweep(belt, pp.band_profile(0.032, 0.005), None), dark, 60))
    parts.append(buckle("BeltBuckle", brass, on_trunk(0.0, 0.2, 0.03), rng, 0.03))
    return parts


def bmesh_open_bottom(obj, z):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < z - 1e-5], context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()


def sleeve(name, material, side, length, top_r, bot_r, droop=0.6, rng=None, ragged=0.0, uv_scale=1.0):
    """A short sleeve from the shoulder, hanging down and out."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    start = Vector((side * 0.17, 0.0, 0.53))
    direction = Vector((side * math.sin(droop), 0.0, -math.cos(droop)))
    rings = []
    n = 14
    steps = 8
    for k in range(steps + 1):
        t = k / steps
        c = start + direction * length * t
        r = top_r + (bot_r - top_r) * t
        ring = []
        for i in range(n):
            a = i * math.tau / n
            local = Vector((math.cos(a) * r * math.cos(droop), math.sin(a) * r, math.cos(a) * r * math.sin(droop) * side))
            p = c + local
            if k == steps and ragged and rng:
                p += direction * rng.uniform(-ragged, ragged)
            ring.append(bm.verts.new(p))
        rings.append(ring)
    for k in range(steps):
        for i in range(n):
            j = (i + 1) % n
            f = bm.faces.new((rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i]))
            for loop, (ii, kk) in zip(f.loops, ((i, k), (i + 1, k), (i + 1, k + 1), (i, k + 1))):
                loop[uv].uv = (ii / n * 0.6 * uv_scale, -kk / steps * length * uv_scale)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return thicken(new_object(name, bm, material, 0), 0.006)


def mail_material(colour, rings=110.0, name="Icon_Mail"):
    """Riveted mail: rows of rings, each row offset half a ring, dark between, the rings' tops
    catching light. Drawn on the garment's UVs, so it follows the drape."""
    m, nt, bsdf = surface._tree(name)
    uvn = nt.nodes.new("ShaderNodeUVMap")
    uvn.uv_map = "UVMap"
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(uvn.outputs[0], sep.inputs[0])
    u = surface._math(nt, "MULTIPLY", sep.outputs[0], rings)
    v = surface._math(nt, "MULTIPLY", sep.outputs[1], rings * 1.15)
    row = surface._math(nt, "FLOOR", v)
    shift = surface._math(nt, "MULTIPLY", surface._math(nt, "MODULO", row, 2.0), 0.5)
    fu = surface._math(nt, "SUBTRACT", surface._math(nt, "FRACT", surface._math(nt, "ADD", u, shift)), 0.5)
    fv = surface._math(nt, "SUBTRACT", surface._math(nt, "FRACT", v), 0.5)
    d = surface._math(nt, "SQRT", surface._math(nt, "ADD", surface._math(nt, "MULTIPLY", fu, fu), surface._math(nt, "MULTIPLY", fv, fv)))
    ring = ps._grey(nt, surface._ramp(nt, d, [(0.18, 0.0), (0.26, 1.0), (0.40, 1.0), (0.50, 0.0)]))
    co = surface._coords(nt)
    tone = surface._shade(nt, colour, surface._span(nt, ring, 0.18, 1.1))
    tone = surface._shade(nt, tone, surface._span(nt, ps._n(nt, co, 20.0, 3), 0.75, 1.1))
    rust = ps._grey(nt, surface._ramp(nt, ps._n(nt, co, 14.0, 3), [(0.6, 0.0), (0.75, 0.6)]))
    tone = surface._mix(nt, tone, (0.25, 0.12, 0.05), rust)
    n = ps._bump(nt, ring, 0.9, 0.003)
    surface._finish(nt, bsdf, tone, surface._span(nt, ring, 0.7, 0.35), n, surface._span(nt, ring, 0.2, 0.85))
    return m


def chain_shirt(rng):
    mail = mail_material((0.50, 0.51, 0.54))
    leather = mat("Icon_Leather_Dark", icon_leather, (0.06, 0.032, 0.016))
    parts = []
    zt = neckline(0.06, 0.34, 0.625, 0.50)
    hem = lambda u: -0.05 + 0.012 * math.sin(u * 7 + 1) + 0.01 * u * u
    front, grid = panel("Shirt", mail, hem, zt, 0.008, folds=0.010, seed=1, uv_scale=1.0)
    parts.append(thicken(front, 0.006))
    back, _ = panel("Back", mail, hem, neckline(0.02, 0.34, 0.63, 0.5), 0.008, back=True, folds=0.008, seed=2)
    parts.append(thicken(back, 0.006))
    for side in (-1, 1):
        parts.append(sleeve(f"Sleeve_{side}", mail, side, 0.20, 0.07, 0.065, 0.62, rng, 0.006))
    parts.append(edge_tube("Collar", leather, column_edge(grid, True), 0.006))
    return parts


def hide_armour(rng):
    hide = mat("Icon_Hide", hide_material, (0.095, 0.062, 0.038))
    fur = mat("Icon_Fur", surface.fur, (0.20, 0.155, 0.11))
    dark = mat("Icon_Leather_Dark", icon_leather, (0.06, 0.032, 0.016))
    bone = mat("Icon_Bone", surface.bone, (0.72, 0.66, 0.52))
    parts = []
    zt = neckline(0.05, 0.34, 0.63, 0.50)
    hem = lambda u: -0.07 + 0.03 * noise.noise(Vector((u * 3.0, 0.5, 2.0))) + 0.012 * u * u
    front, grid = panel("Coat", hide, hem, zt, 0.014, folds=0.008, seed=3)
    parts.append(thicken(front, 0.008))
    back, _ = panel("Back", hide, hem, neckline(0.02, 0.34, 0.63, 0.50), 0.014, back=True, folds=0.006, seed=4)
    parts.append(thicken(back, 0.008))
    for side in (-1, 1):
        parts.append(sleeve(f"Sleeve_{side}", hide, side, 0.17, 0.075, 0.075, 0.6, rng, 0.012))
    # A shaggy collar of fur round the neck and over the shoulders.
    pts = column_edge(grid, True)
    collar = []
    for p in pts:
        collar.append(p)
    bm = pp.sweep(collar, pp.round_profile(0.048, 14, 1.0, 0.75), None)
    seed = rng.uniform(0, 9)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=1, use_grid_fill=True)
    for v in bm.verts:
        tuft = abs(noise.noise(v.co * 90 + Vector((seed, 0, 0))))
        v.co += Vector((noise.noise(v.co * 40 + Vector((seed, 0, 0))), noise.noise(v.co * 40 + Vector((0, seed, 0))), noise.noise(v.co * 40) - 0.6)) * 0.014
        v.co.z -= tuft * 0.012
    parts.append(new_object("Collar", bm, fur, 0))
    # Fur along the hem.
    bm = pp.sweep(column_edge(grid, False), pp.round_profile(0.022, 8, 1.0, 0.8), None)
    for v in bm.verts:
        v.co += Vector((noise.noise(v.co * 70), noise.noise(v.co * 70 + Vector((3, 0, 0))), noise.noise(v.co * 70 + Vector((0, 5, 0))) - 0.5)) * 0.01
    parts.append(new_object("HemFur", bm, fur, 0))
    # Thong lacing down the front, toggled with bone.
    for k in range(4):
        z = 0.48 - 0.09 * k
        a = on_trunk(-0.06, z, 0.026)
        b = on_trunk(0.06, z, 0.026)
        parts.append(edge_tube(f"Lace_{k}", dark, [a, (a + b) / 2 + Vector((0, -0.006, -0.004)), b], 0.003, 5))
        parts.append(pp.lathe(f"Toggle_{k}", bone, [(0.0, -0.016), (0.005, -0.014), (0.005, 0.014), (0.0, 0.016)], 6,
                              (a + b) / 2 + Vector((0, -0.008, 0)), Matrix.Rotation(math.radians(90), 3, "Y") @ Matrix.Rotation(0.3, 3, "X")))
        parts[-1].data.materials.clear()
        parts[-1].data.materials.append(bone)
    belt = [on_trunk(-math.pi / 2 + math.pi * k / 30, 0.17, 0.028) for k in range(31)]
    parts.append(new_object("Belt", pp.sweep(belt, pp.band_profile(0.03, 0.006), None), dark, 60))
    return parts


def hide_material(colour):
    """Hide with the hair left on, sewn from pieces: each piece its own shade, the hair lying
    down the coat in streaks, the joins a little darker."""
    m, nt, bsdf = surface._tree("Surface_Hide")
    co = surface._coords(nt)
    cell = nt.nodes.new("ShaderNodeTexVoronoi")
    cell.feature = "F1"
    nt.links.new(co, cell.inputs[0])
    cell.inputs["Scale"].default_value = 7.0 * surface.SCALE
    shade = ps._grey(nt, cell.outputs["Color"])
    tone = surface._shade(nt, colour, surface._span(nt, shade, 0.70, 1.30))
    streak = ps._n(nt, surface._stretch(nt, co, (1.0, 1.0, 0.08)), 70.0, 3, 0.65)
    tone = surface._shade(nt, tone, surface._span(nt, streak, 0.62, 1.25))
    seams = ps._grey(nt, surface._ramp(nt, ps._v(nt, co, 7.0), [(0.0, 1.0), (0.02, 0.0)]))
    tone = surface._mix(nt, tone, (0.04, 0.025, 0.012), surface._math(nt, "MULTIPLY", seams, 0.5))
    n = ps._bump(nt, streak, 0.6, 0.003)
    n = ps._bump(nt, seams, -0.4, 0.003, n)
    surface._finish(nt, bsdf, tone, 0.9, n)
    return m


def scale_mail(rng):
    leather = mat("Icon_Leather_Dark", icon_leather, (0.06, 0.032, 0.016))
    backing = mat("Icon_Leather_B", icon_leather, (0.11, 0.06, 0.03))
    scale_m = mat("Icon_Scale", metal, (0.40, 0.40, 0.40), 0.34, 0.85, 0.75, name="Icon_Scale")
    brass = mat("Icon_Brass", metal, (0.70, 0.52, 0.22), 0.3, 0.9, 0.4, name="Icon_Brass")
    parts = []
    zt = neckline(0.05, 0.33, 0.62, 0.48)
    hem = lambda u: -0.04 + 0.01 * u * u
    front, grid = panel("Coat", backing, hem, zt, 0.008)
    parts.append(thicken(front, 0.006))
    back, _ = panel("Back", backing, hem, neckline(0.02, 0.33, 0.62, 0.48), 0.008, back=True)
    parts.append(thicken(back, 0.006))
    for side in (-1, 1):
        parts.append(sleeve(f"Sleeve_{side}", backing, side, 0.16, 0.072, 0.07, 0.6))
    # The scales: rows from the hem up, each overlapping the row below, every other row offset
    # by half a scale. Each is a small leaf of steel, domed, its foot tipped out a little.
    bm = bmesh.new()
    dz = 0.030
    row = 0
    z = -0.03
    while z < 0.60:
        width = 0.034
        u_step = width / 0.30
        u = -1.0 + (u_step / 2 if row % 2 else 0.0)
        while u <= 1.0:
            if z < zt(u) - 0.035:
                scale_leaf(bm, u, z, 0.044, width * 1.08, rng)
            u += u_step
        z += dz
        row += 1
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    parts.append(new_object("Scales", bm, scale_m, 50))
    parts.append(edge_tube("Collar", leather, column_edge(grid, True), 0.008))
    belt = [on_trunk(-math.pi / 2 + math.pi * k / 30, 0.17, 0.04) for k in range(31)]
    parts.append(new_object("Belt", pp.sweep(belt, pp.band_profile(0.034, 0.006), None), leather, 60))
    parts.append(buckle("Buckle", brass, on_trunk(0.0, 0.17, 0.046), rng, 0.032))
    return parts


def scale_leaf(bm, u, z, length, width, rng):
    th = u * math.pi / 2
    base = on_trunk(th, z, 0.012)
    up = (on_trunk(th, z + 0.01, 0.012) - base).normalized()
    out = (on_trunk(th, z, 0.03) - base).normalized()
    leaf_at(bm, base, out, -up, length, width, rng)


def leaf_at(bm, base, out, down, length, width, rng):
    """One scale: hung from its top edge at `base`, hanging along `down`, lying on `out`. A
    rounded foot, a raised midrib, tipped out at the foot so it overlaps the row below."""
    down = (down - out * down.dot(out)).normalized()
    side = down.cross(out).normalized()
    tip = 0.22 + rng.uniform(-0.05, 0.05)
    outline = []
    n = 10
    for i in range(n + 1):
        a = math.pi * i / n
        x = math.cos(a) * width / 2
        y = math.sin(a) * length * 0.55 + length * 0.45
        outline.append((x, y))
    top = [(width / 2, 0.0), (-width / 2, 0.0)]
    ring = [(width / 2, 0.0)] + outline[1:-1] + [(-width / 2, 0.0)]
    verts_f, verts_b = [], []
    for (x, y) in ring:
        lift = (y / length) ** 2 * tip * length + 0.004 * (1 - (2 * x / width) ** 2)
        p = base + side * x + down * y + out * lift
        verts_f.append(bm.verts.new(p + out * 0.0025))
        verts_b.append(bm.verts.new(p))
    centre = bm.verts.new(base + down * length * 0.5 + out * (0.25 * tip * length + 0.007))
    for i in range(len(ring)):
        j = (i + 1) % len(ring)
        bm.faces.new((verts_f[i], verts_f[j], centre))
        bm.faces.new((verts_b[j], verts_b[i], verts_f[i], verts_f[j]))


# --- small things ---------------------------------------------------------------------------------

def coin_material(colour, name):
    """A struck coin: a raised rim, a raised device in the middle, worn bright on both."""
    m, nt, bsdf = surface._tree(name)
    uvn = nt.nodes.new("ShaderNodeUVMap")
    uvn.uv_map = "UVMap"
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(uvn.outputs[0], sep.inputs[0])
    x = surface._math(nt, "SUBTRACT", sep.outputs[0], 0.5)
    y = surface._math(nt, "SUBTRACT", sep.outputs[1], 0.5)
    r = surface._math(nt, "MULTIPLY", surface._math(nt, "SQRT", surface._math(nt, "ADD", surface._math(nt, "MULTIPLY", x, x), surface._math(nt, "MULTIPLY", y, y))), 2.0)
    ang = surface._math(nt, "ARCTAN2", y, x)
    rim = ps._grey(nt, surface._ramp(nt, r, [(0.78, 0.0), (0.84, 1.0), (0.98, 1.0), (1.0, 0.6)]))
    star = surface._math(nt, "ADD", 0.30, surface._math(nt, "MULTIPLY", surface._math(nt, "COSINE", surface._math(nt, "MULTIPLY", ang, 6.0)), 0.12))
    device = ps._grey(nt, surface._ramp(nt, surface._math(nt, "SUBTRACT", star, r), [(-0.03, 0.0), (0.0, 1.0)]))
    dots = ps._grey(nt, surface._ramp(nt, surface._math(nt, "ABSOLUTE", surface._math(nt, "SUBTRACT", r, 0.66)), [(0.0, 1.0), (0.03, 0.0)]))
    dots = surface._math(nt, "MULTIPLY", dots, ps._grey(nt, surface._ramp(nt, surface._math(nt, "COSINE", surface._math(nt, "MULTIPLY", ang, 24.0)), [(0.5, 0.0), (0.8, 1.0)])))
    height = surface._math(nt, "MAXIMUM", surface._math(nt, "MAXIMUM", rim, device), dots)
    co = surface._coords(nt)
    tone = surface._shade(nt, colour, surface._span(nt, height, 0.62, 1.12))
    tone = surface._shade(nt, tone, surface._span(nt, ps._n(nt, co, 120.0, 2), 0.85, 1.05))
    n = ps._bump(nt, height, 0.9, 0.0015)
    surface._finish(nt, bsdf, tone, surface._span(nt, height, 0.42, 0.22), n, 0.9)
    return m


def coin_obj(bm, uv, at, normal, radius, thick, rng, segs=24):
    normal = Vector(normal).normalized()
    side = normal.orthogonal().normalized()
    other = normal.cross(side)
    a0 = rng.uniform(0, math.tau)
    rings = []
    for h, rr in ((-thick / 2, radius * 0.97), (-thick / 2 * 0.5, radius), (thick / 2 * 0.5, radius), (thick / 2, radius * 0.97)):
        ring = []
        for i in range(segs):
            a = a0 + i * math.tau / segs
            ring.append(bm.verts.new(Vector(at) + (side * math.cos(a) + other * math.sin(a)) * rr + normal * h))
        rings.append(ring)
    for r0, r1 in zip(rings, rings[1:]):
        for i in range(segs):
            j = (i + 1) % segs
            f = bm.faces.new((r0[i], r0[j], r1[j], r1[i]))
            for loop in f.loops:
                loop[uv].uv = (0.5, 0.995)
    for ring, flip in ((rings[0], True), (rings[-1], False)):
        verts = list(reversed(ring)) if flip else ring
        f = bm.faces.new(verts)
        for loop in f.loops:
            k = ring.index(loop.vert)
            a = a0 + k * math.tau / segs
            loop[uv].uv = (0.5 + 0.5 * math.cos(a) * (-1 if flip else 1), 0.5 + 0.5 * math.sin(a))


def coins(rng, colour, name, layout):
    material = mat(f"Icon_{name}", coin_material, colour, f"Icon_{name}")
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    R, T = 0.016, 0.0035
    for (x, y, z, n) in layout(R, T):
        coin_obj(bm, uv, (x, y, z), n, R * rng.uniform(0.97, 1.03), T, rng)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return [new_object(name, bm, material, 50)]


def stack_layout(stacks, loose, rng):
    def layout(R, T):
        out = []
        for (sx, sy, count) in stacks:
            for k in range(count):
                out.append((sx + rng.uniform(-0.002, 0.002), sy + rng.uniform(-0.002, 0.002), T / 2 + k * T * 1.02,
                            Vector((rng.uniform(-0.04, 0.04), rng.uniform(-0.04, 0.04), 1.0))))
        for (lx, ly, tilt) in loose:
            n = Vector((math.sin(tilt) * 0.8, -math.sin(tilt) * 0.6, math.cos(tilt)))
            out.append((lx, ly, T / 2 + R * math.sin(abs(tilt)) * 0.9, n))
        return out
    return layout


def ring_item(rng, band_colour, stone_colour, stone="cabochon", name="Ring"):
    band = mat(f"Icon_{name}_Band", metal, band_colour, 0.18, 1.0, 0.6, name=f"Icon_{name}_Band")
    parts = []
    R, w, t = 0.011, 0.0035, 0.0016
    # The band: a flattened D-section swept round, thicker under the stone.
    path, nrm = [], []
    for k in range(48):
        a = k * math.tau / 48
        path.append(Vector((math.cos(a) * R, 0.0, math.sin(a) * R)))
        nrm.append(Vector((math.cos(a), 0.0, math.sin(a))))
    bm = pp.sweep(path, [(-w / 2, -t * 0.4), (w / 2, -t * 0.4), (w / 2, t * 0.5), (w * 0.3, t), (-w * 0.3, t), (-w / 2, t * 0.5)], nrm, closed_path=True)
    parts.append(new_object(f"{name}_Band", bm, band, 0))
    top = Vector((0, 0, R + t))
    if stone == "cabochon":
        bez = pp.lathe(f"{name}_Bezel", band, [(0.0, 0.0), (0.0045, 0.0), (0.0048, 0.0018), (0.0042, 0.0022), (0.0, 0.0022)], 20, top)
        assign(bez, band)
        parts.append(bez)
        st = pp.heap(f"{name}_Stone", gem(stone_colour, f"Icon_{name}_Gem", 0.08, 0.6), top + Vector((0, 0, 0.0018)), (0.0037, 0.0037, 0.0028), rng, res=14, bumps=0.0, freq=1)
        parts.append(st)
    else:
        # A faceted garnet in four claws.
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.0042, radius2=0.0028, depth=0.0018)
        bmesh.ops.translate(bm, vec=(0, 0, 0.0009), verts=bm.verts)
        pav = bmesh.new()
        bmesh.ops.create_cone(pav, cap_ends=True, segments=8, radius1=0.0, radius2=0.0042, depth=0.003)
        bmesh.ops.translate(pav, vec=(0, 0, -0.0015), verts=pav.verts)
        mesh = bpy.data.meshes.new("tmp")
        pav.to_mesh(mesh)
        pav.free()
        bm.from_mesh(mesh)
        bpy.data.meshes.remove(mesh)
        pp.place(bm, Matrix.Translation(top + Vector((0, 0, 0.0035))))
        parts.append(new_object(f"{name}_Stone", bm, gem(stone_colour, f"Icon_{name}_Gem", 0.02, 0.15), 10))
        for k in range(4):
            a = math.pi / 4 + k * math.pi / 2
            p0 = top + Vector((math.cos(a) * 0.0025, math.sin(a) * 0.0025, 0.0))
            p1 = top + Vector((math.cos(a) * 0.0046, math.sin(a) * 0.0046, 0.0052))
            parts.append(edge_tube(f"{name}_Claw_{k}", band, [p0, p1], 0.0007, 5))
        parts.append(pp.lathe(f"{name}_Collet", band, [(0.0, 0.0), (0.0026, 0.0), (0.003, 0.0022), (0.0, 0.0022)], 12, top))
        assign(parts[-1], band)
    return parts


def holy_symbol(rng):
    """A sunburst in silver on a leather thong: a domed disc with a face struck in it, a rim,
    and twelve rays, straight and flame-waved by turns, cut from the same plate."""
    silver = mat("Icon_Silver", metal, (0.90, 0.90, 0.92), 0.22, 0.7, 0.4, name="Icon_Silver")
    thong = mat("Icon_Thong", icon_leather, (0.16, 0.08, 0.04))
    parts = []
    face = Matrix.Rotation(math.radians(-90), 3, "X")
    disc = pp.lathe("Sun", silver, [(0.0, 0.0), (0.017, 0.0), (0.017, 0.0022), (0.0145, 0.0034), (0.013, 0.0028), (0.0, 0.0050)], 32, (0, 0, 0), face)
    parts.append(assign(disc, silver))
    bm = bmesh.new()
    t = 0.0024
    for k in range(12):
        a = k * math.tau / 12 + math.tau / 24
        d = Vector((math.cos(a), 0, math.sin(a)))
        sd = Vector((-math.sin(a), 0, math.cos(a)))
        length = 0.034 if k % 2 == 0 else 0.028
        wave = 0.0 if k % 2 == 0 else 0.0028
        outline = []
        n = 6
        for i in range(n + 1):
            f = i / n
            w = 0.0055 * (1 - f) ** 0.9
            off = wave * math.sin(f * math.pi * 2)
            outline.append((0.0155 + (length - 0.0155) * f, w + off))
        pts = [(r, w) for r, w in outline] + [(r, w - 2 * 0.0055 * (1 - i / n) ** 0.9) for i, (r, w) in reversed(list(enumerate(outline)))]
        front = [bm.verts.new(d * r + sd * w + Vector((0, -t, 0))) for r, w in pts]
        back = [bm.verts.new(d * r + sd * w) for r, w in pts]
        bm.faces.new(front)
        bm.faces.new(list(reversed(back)))
        for i in range(len(pts)):
            j = (i + 1) % len(pts)
            bm.faces.new((front[i], back[i], back[j], front[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    parts.append(new_object("Rays", bm, silver, 35))
    # A face struck in the sun: brows, eyes and a smile in low relief.
    for x in (-0.0045, 0.0045):
        parts.append(assign(pp.heap(f"Eye_{x}", silver, (x, -0.0048, 0.0025), (0.0018, 0.0009, 0.0012), rng, res=6, bumps=0.0, freq=1), silver))
    path = [Vector((0.006 * math.cos(a), -0.0048, -0.002 + 0.004 * math.sin(a))) for a in [math.pi + k * math.pi / 8 for k in range(9)]]
    parts.append(edge_tube("Smile", silver, path, 0.0007, 5))
    bail = pp.torus("Bail", silver, (0, 0, 0), 0.0035, 0.0011, normal=(1, 0, 0), segs=12, minor_segs=5)
    bail.location = (0, -0.0012, 0.0365)
    parts.append(assign(bail, silver))
    # The thong through the bail, its loop lying slack behind.
    path = []
    for k in range(25):
        a = k / 24 * math.tau
        path.append(Vector((0.024 * math.sin(a), 0.006 * (1 - math.cos(a)), 0.0365 + 0.022 * (1 - math.cos(a)))))
    parts.append(edge_tube("Thong", thong, path, 0.0014, 6))
    return parts


def bloodstones(rng):
    leather = mat("Icon_Pouch", icon_leather, (0.20, 0.11, 0.05))
    cord = mat("Icon_Cord", icon_leather, (0.12, 0.06, 0.03))
    stone = mat("Icon_Bloodstone", bloodstone_material)
    parts = []
    prof = [(0.0, 0.0), (0.02, 0.001), (0.032, 0.012), (0.034, 0.028), (0.025, 0.042), (0.014, 0.05), (0.012, 0.054), (0.02, 0.062), (0.024, 0.07), (0.016, 0.074), (0.0, 0.075)]
    pouch = pc.sculpt_sack("Pouch", rng, [(r, z) for r, z in _resample(prof, 22)], 28, neck=0.052, tie_spread=0.025,
                           flop=(0.9, math.radians(150)), pleats=7, lumps=0.003, creases=0.003)
    assign(pouch, leather)
    shade_smooth(pouch, 0)
    pouch.location = (0.012, 0.012, 0.0)
    parts.append(pouch)
    parts.append(pp.torus("Tie", cord, (0.012, 0.012, 0.052), 0.0125, 0.0016, segs=16, minor_segs=5))
    assign(parts[-1], cord)
    for k, (x, y, s) in enumerate(((-0.024, -0.03, 1.0), (0.008, -0.036, 0.85), (-0.04, -0.008, 0.9))):
        st = pp.heap(f"Stone_{k}", stone, (x, y, 0.0), (0.0115 * s, 0.009 * s, 0.0085 * s), rng, res=12, bumps=0.06, freq=50)
        bmesh_open_bottom(st, 0.0)
        st.data.transform(Matrix.Translation((0, 0, 0.0005)))
        parts.append(st)
    return parts


def bloodstone_material():
    m, nt, bsdf = surface._tree("Surface_Bloodstone")
    co = surface._coords(nt)
    base = surface._shade(nt, (0.05, 0.16, 0.08), surface._span(nt, ps._n(nt, co, 80.0, 3), 0.6, 1.3))
    flecks = ps._grey(nt, surface._ramp(nt, ps._n(nt, co, 260.0, 3, 0.7), [(0.60, 0.0), (0.64, 1.0)]))
    tone = surface._mix(nt, base, (0.55, 0.04, 0.03), flecks)
    surface._finish(nt, bsdf, tone, 0.12, None, 0.0)
    bsdf.inputs["Coat Weight"].default_value = 0.6
    return m


def bone_dice(rng):
    bone = mat("Icon_Dice", dice_material)
    parts = []
    for k, (x, y, turn) in enumerate(((-0.0115, 0.004, 0.4), (0.0105, -0.007, -0.3), (0.001, 0.017, 1.0))):
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=0.017)
        pp._bevel_sharp(bm, 0.0028, 3, profile=0.5)
        for v in bm.verts:
            v.co += Vector((noise.noise(v.co * 300 + Vector((k, 0, 0))), noise.noise(v.co * 300 + Vector((0, k, 0))), 0)) * 0.0004
        o = new_object(f"Die_{k}", bm, bone, 25)
        o.location = (x, y, 0.0085)
        o.rotation_euler = (0.0, 0.0, turn)
        parts.append(o)
    return parts


def dice_material():
    """Yellowed bone; on every face a little creature carved where a pip would be, which at
    icon size is a dark, irregular mark in the middle of each face."""
    m, nt, bsdf = surface._tree("Surface_Dice")
    co = surface._coords(nt)
    obj = nt.nodes.new("ShaderNodeTexCoord").outputs["Object"]
    x, y, z = ps._xyz(nt, obj)
    ax, ay, az = (surface._math(nt, "ABSOLUTE", c) for c in (x, y, z))
    big = surface._math(nt, "MAXIMUM", surface._math(nt, "MAXIMUM", ax, ay), az)
    sq = surface._math(nt, "ADD", surface._math(nt, "ADD", surface._math(nt, "MULTIPLY", x, x), surface._math(nt, "MULTIPLY", y, y)), surface._math(nt, "MULTIPLY", z, z))
    off = surface._math(nt, "SQRT", surface._math(nt, "MAXIMUM", surface._math(nt, "SUBTRACT", sq, surface._math(nt, "MULTIPLY", big, big)), 0.0))
    # The creature: a body and a head, its outline wobbled into legs by a fine noise; cut crisp.
    wobble = surface._math(nt, "MULTIPLY", surface._math(nt, "SUBTRACT", ps._n(nt, obj, 700.0, 2, 0.5), 0.5), 0.0035)
    cut = ps._grey(nt, surface._ramp(nt, surface._math(nt, "ADD", off, wobble), [(0.0034, 1.0), (0.0038, 0.0)]))
    tone = surface._shade(nt, (0.82, 0.74, 0.57), surface._span(nt, ps._n(nt, co, 300.0, 3), 0.85, 1.05))
    tone = surface._mix(nt, tone, (0.48, 0.36, 0.20), ps._grey(nt, surface._ramp(nt, ps._n(nt, co, 90.0, 3), [(0.6, 0.0), (0.8, 0.7)])))
    tone = surface._mix(nt, tone, (0.10, 0.06, 0.03), cut)
    surface._finish(nt, bsdf, tone, 0.45, ps._bump(nt, cut, -0.8, 0.0006), 0.0)
    return m


def silk_scarf(rng):
    """Blue silk, gold thread worked along both ends, folded small: a folded square with an
    end let fall loose across it."""
    silk = mat("Icon_Silk", silk_material)
    parts = []
    W, L = 0.07, 0.30
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    # A ribbon laid along a path: folded on itself twice, then the tail across the top.
    path = []
    for k in range(80):
        t = k / 79
        if t < 0.33:
            p = Vector((-0.045 + 0.09 * t / 0.33, 0.0, 0.002))
        elif t < 0.38:
            a = (t - 0.33) / 0.05 * math.pi
            p = Vector((0.045 + 0.004 * math.sin(a), 0.0, 0.002 + 0.004 * (1 - math.cos(a))))
        elif t < 0.71:
            p = Vector((0.045 - 0.09 * (t - 0.38) / 0.33, 0.0, 0.010))
        elif t < 0.76:
            a = (t - 0.71) / 0.05 * math.pi
            p = Vector((-0.045 - 0.004 * math.sin(a), 0.0, 0.010 + 0.004 * (1 - math.cos(a))))
        else:
            q = (t - 0.76) / 0.24
            p = Vector((-0.045 + 0.075 * q, -0.012 * q + 0.03 * q * q, 0.018 + 0.006 * math.sin(q * 3)))
        path.append(p)
    cols = 14
    rows = []
    for k, p in enumerate(path):
        t = (path[min(k + 1, len(path) - 1)] - path[max(k - 1, 0)]).normalized()
        side = Vector((0, 1, 0)) if k < 60 else Vector((-0.3, 1, 0)).normalized()
        side = (side - t * side.dot(t)).normalized()
        row = []
        for i in range(cols + 1):
            s = i / cols - 0.5
            ripple = 0.0012 * math.sin(k * 0.9 + i * 0.7) + 0.002 * noise.noise(Vector((k * 0.2, i * 0.3, 1.0)))
            row.append((bm.verts.new(p + side * s * W + Vector((0, 0, ripple))), (i / cols, k / 79)))
        rows.append(row)
    for k in range(len(rows) - 1):
        for i in range(cols):
            quad = (rows[k][i], rows[k][i + 1], rows[k + 1][i + 1], rows[k + 1][i])
            f = bm.faces.new([q[0] for q in quad])
            for loop, (_, c) in zip(f.loops, quad):
                loop[uv].uv = c
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    end = [rows[-1][i][0].co.copy() for i in range(cols + 1)]
    o = new_object("Scarf", bm, silk, 0)
    thicken(o, 0.0012)
    parts.append(o)
    # Fringe at the loose end.
    for i in range(0, cols + 1, 1):
        p = end[i]
        parts.append(edge_tube(f"Fringe_{i}", mat("Icon_Gold_Thread", metal, (0.85, 0.62, 0.22), 0.3, 0.9, 0.3, name="Icon_Gold_Thread"),
                               [p, p + Vector((0.008, -0.002 + 0.001 * i, -0.004))], 0.0007, 4))
    return parts


def silk_material():
    m, nt, bsdf = surface._tree("Surface_Silk")
    uvn = nt.nodes.new("ShaderNodeUVMap")
    uvn.uv_map = "UVMap"
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(uvn.outputs[0], sep.inputs[0])
    v = sep.outputs[1]
    # Gold worked along both ends: bands near v=0 and v=1, a border down both edges.
    ends = surface._math(nt, "MINIMUM", v, surface._math(nt, "SUBTRACT", 1.0, v))
    band = ps._grey(nt, surface._ramp(nt, ends, [(0.02, 1.0), (0.03, 0.0), (0.05, 0.0), (0.055, 1.0), (0.065, 1.0), (0.07, 0.0)]))
    u = sep.outputs[0]
    edge = surface._math(nt, "MINIMUM", u, surface._math(nt, "SUBTRACT", 1.0, u))
    border = ps._grey(nt, surface._ramp(nt, edge, [(0.04, 1.0), (0.06, 0.0)]))
    gold = surface._math(nt, "MAXIMUM", band, border)
    blue = (0.06, 0.16, 0.55)
    tone = surface._mix(nt, blue, (0.85, 0.62, 0.22), gold)
    surface._finish(nt, bsdf, tone, surface._span(nt, gold, 0.25, 0.3), None, surface._span(nt, gold, 0.0, 0.8))
    bsdf.inputs["Sheen Weight"].default_value = 0.8
    bsdf.inputs["Sheen Tint"].default_value = (0.7, 0.8, 1.0, 1.0)
    return m


def tankard(rng):
    pewter = mat("Icon_Pewter", pewter_material)
    parts = []
    prof = [(0.0, 0.0), (0.033, 0.0), (0.036, 0.004), (0.034, 0.008), (0.031, 0.012), (0.029, 0.082), (0.032, 0.086), (0.032, 0.092), (0.028, 0.094), (0.026, 0.01), (0.0, 0.012)]
    body = pp.lathe("Body", pewter, prof, 32, (0, 0, 0))
    assign(body, pewter)
    parts.append(body)
    for z in (0.022, 0.07):
        parts.append(pp.torus(f"Band_{z}", pewter, (0, 0, z), 0.0305, 0.0018, segs=32, minor_segs=6))
        assign(parts[-1], pewter)
    # The hinged lid, open a little, and its thumb-piece.
    lid = pp.lathe("Lid", pewter, [(0.0, 0.0), (0.034, 0.0), (0.035, 0.002), (0.03, 0.006), (0.012, 0.012), (0.004, 0.016), (0.0, 0.016)], 32, (0, 0, 0))
    assign(lid, pewter)
    lid.location = (0, 0, 0.093)
    parts.append(lid)
    thumb = pp.heap("Thumb", pewter, (0.038, 0.0, 0.098), (0.007, 0.010, 0.008), rng, res=8, bumps=0.1, freq=40)
    assign(thumb, pewter)
    parts.append(thumb)
    # The handle: a C from rim to foot.
    path = []
    for k in range(17):
        a = -math.pi / 2 + math.pi * k / 16
        path.append(Vector((0.031 + 0.026 * math.cos(a), 0.0, 0.05 - 0.032 * math.sin(a) * 1.05)))
    parts.append(new_object("Handle", pp.sweep(path, pp.band_profile(0.010, 0.006), [Vector((math.cos(-math.pi / 2 + math.pi * k / 16), 0, -math.sin(-math.pi / 2 + math.pi * k / 16))) for k in range(17)]), pewter, 50))
    return parts


def pewter_material():
    """Pewter: soft grey with a little blue, dull, worked round its middle with a hunt — at icon
    size a frieze of darker figures between the bands."""
    m, nt, bsdf = surface._tree("Surface_Pewter")
    co = surface._coords(nt)
    obj = nt.nodes.new("ShaderNodeTexCoord").outputs["Object"]
    x, y, z = ps._xyz(nt, obj)
    ang = surface._math(nt, "ARCTAN2", y, x)
    frieze = ps._grey(nt, surface._ramp(nt, z, [(0.026, 0.0), (0.028, 1.0), (0.064, 1.0), (0.066, 0.0)]))
    figures = ps._grey(nt, surface._ramp(nt, ps._n(nt, ps._combine(nt, surface._math(nt, "MULTIPLY", ang, 0.08), z, 0.0), 260.0, 3, 0.5), [(0.5, 0.0), (0.56, 1.0)]))
    relief = surface._math(nt, "MULTIPLY", frieze, figures)
    tone = surface._shade(nt, (0.56, 0.57, 0.60), surface._span(nt, ps._n(nt, co, 50.0, 3), 0.85, 1.05))
    tone = surface._mix(nt, tone, (0.24, 0.24, 0.26), surface._math(nt, "MULTIPLY", frieze, surface._math(nt, "SUBTRACT", 1.0, figures)))
    n = ps._bump(nt, relief, 0.8, 0.0012)
    surface._finish(nt, bsdf, tone, 0.42, n, 0.8)
    return m


def amber(rng):
    """Amber the size of a hen's egg with a beetle in its heart, capped and hung on a chain fit
    for an ogre's neck."""
    m, nt, bsdf = surface._tree("Icon_Amber")
    surface._finish(nt, bsdf, (0.95, 0.48, 0.06), 0.05, None, 0.0)
    bsdf.inputs["Transmission Weight"].default_value = 0.85
    bsdf.inputs["IOR"].default_value = 1.54
    bsdf.inputs["Emission Color"].default_value = (0.9, 0.35, 0.02, 1)
    bsdf.inputs["Emission Strength"].default_value = 0.15
    resin = m
    iron = mat("Icon_Chain", metal, (0.40, 0.39, 0.38), 0.4, 0.8, 0.6, name="Icon_Chain")
    beetle = flat((0.03, 0.018, 0.008), 0.5, "Icon_Beetle")
    parts = []
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=28, v_segments=18, radius=1.0)
    for v in bm.verts:
        p = v.co.copy()
        k = 1.0 + 0.05 * noise.noise(p * 2.5)
        egg = 1.0 + 0.18 * p.z
        v.co = Vector((p.x * 0.021 * k * egg, p.y * 0.019 * k * egg, p.z * 0.028 * k))
    parts.append(new_object("Egg", bm, resin, 0))
    body = pp.heap("Beetle", beetle, (0.0, 0.0, -0.004), (0.006, 0.004, 0.009), rng, res=8, bumps=0.1, freq=60)
    parts.append(assign(body, beetle))
    for side in (-1, 1):
        for k in range(3):
            parts.append(edge_tube(f"Leg_{side}_{k}", beetle, [Vector((side * 0.003, 0, -0.008 + 0.005 * k)), Vector((side * 0.009, 0, -0.011 + 0.006 * k))], 0.0006, 4))
    cap = pp.lathe("Cap", iron, [(0.0, 0.0), (0.008, 0.0), (0.006, 0.006), (0.0, 0.007)], 14, (0, 0, 0.026))
    parts.append(assign(cap, iron))
    # The chain: a loop from the cap up and round, links alternating flat and edge on.
    count = 22
    for k in range(count):
        t = (k + 0.5) / count * math.tau
        c = Vector((0.040 * math.sin(t), 0.004 * math.sin(t * 2), 0.036 + 0.042 * (1 - math.cos(t))))
        tangent = Vector((0.040 * math.cos(t), 0.0, 0.042 * math.sin(t))).normalized()
        n = Vector((0, 1, 0)) if k % 2 else tangent.cross(Vector((0, 1, 0))).normalized()
        link = pp.torus(f"Link_{k}", iron, (0, 0, 0), 0.0050, 0.0016, normal=n, segs=12, minor_segs=4)
        link.location = c
        parts.append(assign(link, iron))
    return parts


def backpack(rng):
    leather = mat("Icon_Pack", icon_leather, (0.17, 0.09, 0.04))
    dark = mat("Icon_Leather_Dark", icon_leather, (0.06, 0.032, 0.016))
    blanket = mat("Icon_Roll", surface.cloth, (0.32, 0.20, 0.12))
    brass = mat("Icon_Brass", metal, (0.70, 0.52, 0.22), 0.3, 0.9, 0.4, name="Icon_Brass")
    parts = []
    # The sack of the pack: a soft box, bulging, front toward the viewer.
    W, D, H = 0.30, 0.15, 0.36
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=10, use_grid_fill=True)
    for v in bm.verts:
        q = v.co * 2
        n4 = (abs(q.x) ** 3 + abs(q.y) ** 3 + abs(q.z) ** 3) ** (1 / 3)
        q = q / max(n4, 1e-6)
        p = Vector((q.x * W / 2, q.y * D / 2, q.z * H / 2 + H / 2))
        p.y -= 0.02 * max(0.0, 1 - q.x * q.x) * max(0.0, 1 - q.z * q.z) * (1 if q.y < 0 else 0)
        p += Vector((noise.noise(p * 14), noise.noise(p * 14 + Vector((2, 0, 0))), noise.noise(p * 14 + Vector((0, 4, 0))))) * 0.006
        v.co = p
    parts.append(new_object("Pack", bm, leather, 0))
    # The flap over the top and down the front.
    bm = bmesh.new()
    rows = []
    for j in range(13):
        t = j / 12
        row = []
        for i in range(11):
            s = i / 10 - 0.5
            if t < 0.45:
                # Across the top, back to front.
                a = t / 0.45 * math.pi / 2
                p = Vector((s * W * 0.95, (D / 2 + 0.006) * (1 - 2 * math.sin(a)), H + 0.012))
            else:
                # Down the front, narrowing a little.
                k = (t - 0.45) / 0.55
                p = Vector((s * W * 0.95 * (1 - 0.15 * k), -D / 2 - 0.012, H + 0.006 - k * H * 0.48))
            row.append(bm.verts.new(p))
        rows.append(row)
    for j in range(12):
        for i in range(10):
            bm.faces.new((rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    flap = thicken(new_object("Flap", bm, leather, 30), 0.006)
    parts.append(flap)
    # Two straps down the flap to buckles on the front.
    for x in (-0.08, 0.08):
        path = [Vector((x, D / 2 + 0.012, H + 0.02)), Vector((x, 0.0, H + 0.022)), Vector((x, -D / 2 - 0.02, H + 0.01)), Vector((x, -D / 2 - 0.022, H * 0.42))]
        parts.append(new_object(f"Strap_{x}", pp.sweep(path, pp.band_profile(0.028, 0.004), [Vector((0, 0, 1)), Vector((0, 0, 1)), Vector((0, -1, 0)), Vector((0, -1, 0))]), dark, 50))
        parts.append(buckle(f"Buckle_{x}", brass, Vector((x, -D / 2 - 0.028, H * 0.42)), rng, 0.034))
    # A pocket on the front below the flap.
    pocket = pp.slab("Pocket", leather, rng, (0, -D / 2 - 0.016, H * 0.2), (0, 0, 1), (0, -1, 0), 0.12, 0.17, 0.03, seg=0.04, bevel=0.012, wear=0.3, chips=0)
    assign(pocket, leather)
    parts.append(pocket)
    # A blanket rolled and strapped on top.
    roll = pp.lathe("Roll", blanket, [(0.0, -0.17), (0.038, -0.17), (0.045, -0.15), (0.046, 0.15), (0.038, 0.17), (0.0, 0.17)], 18,
                    (0, 0.01, H + 0.065), Matrix.Rotation(math.radians(90), 3, "Y"), jitter=0.05, rng=rng)
    assign(roll, blanket)
    parts.append(roll)
    for x in (-0.11, 0.11):
        parts.append(pp.torus(f"RollStrap_{x}", dark, (x, 0.01, H + 0.065), 0.048, 0.004, normal=(1, 0, 0), segs=20, minor_segs=4))
        assign(parts[-1], dark)
    return parts


def _resample(profile, rows):
    import generate_props as gp
    return gp._resample(profile, rows)


# --- containers -------------------------------------------------------------------------------

def container(name):
    def build(rng):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=os.path.join(PROPS, f"{name}.glb"))
        objs = [o for o in bpy.data.objects if o not in before]
        lid = next((o for o in objs if o.name.split(".")[0] == "Lid"), None)
        lock = next((o for o in objs if o.name.split(".")[0] == "Lock"), None)
        if lock is not None:
            bpy.data.objects.remove(lock, do_unlink=True)
            objs.remove(lock)
        if lid is not None:
            opening = lid.get("open", -100)
            if lid.get("hinge", "x") == "none" or opening == "lift":
                lo, hi = ri.bounds([lid])
                lid.location += Vector((-(hi.x - lo.x) * 0.15, (hi.y - lo.y) * 0.8 + 0.04, 0.0))
                lid.rotation_mode = "XYZ"
                lid.rotation_euler = (math.radians(-65), 0, 0)
            else:
                lid.rotation_mode = "XYZ"
                lid.rotation_euler = (math.radians(float(opening)), 0, 0)
        for o in objs:
            if o.type != "MESH":
                bpy.data.objects.remove(o, do_unlink=True)
        return [o for o in bpy.data.objects if o.type == "MESH"]
    return build


CONTAINERS = ["crate", "crate-b", "chest", "strongbox", "barrel", "sack", "sack-open", "cart", "cart-overturned", "weapon-rack", "pile", "niche"]

# id -> (family, builder, pose: (turn about Z, tilt toward the camera) in degrees)
ITEMS = {
    "breastplate": ("armour", breastplate, (-18, 6)),
    "chain-shirt": ("armour", chain_shirt, (-18, 6)),
    "hide-armour": ("armour", hide_armour, (-18, 6)),
    "leather-armour": ("armour", lambda rng: leather_cuirass(rng, False), (-18, 6)),
    "studded-leather": ("armour", lambda rng: leather_cuirass(rng, True), (-18, 6)),
    "scale-mail": ("armour", scale_mail, (-18, 6)),
    "ring-of-protection": ("trinkets", lambda rng: ring_item(rng, (0.82, 0.83, 0.86), (0.18, 0.40, 0.95), "cabochon", "Protection"), (-25, 40)),
    "gold-garnet-ring": ("trinkets", lambda rng: ring_item(rng, (0.95, 0.70, 0.30), (0.45, 0.02, 0.05), "faceted", "Garnet"), (-25, 40)),
    "coins-gp": ("coins", lambda rng: coins(rng, (0.98, 0.72, 0.26), "Gold",
                                             stack_layout([(-0.012, 0.006, 6), (0.016, 0.012, 4)], [(-0.026, -0.024, 0.15), (0.006, -0.028, 0.3), (0.032, -0.012, 0.1)], rng)), (-20, 55)),
    "coins-sp": ("coins", lambda rng: coins(rng, (0.86, 0.87, 0.90), "Silver",
                                             stack_layout([(0.0, 0.008, 4)], [(-0.026, -0.016, 0.2), (0.008, -0.026, 0.1), (0.03, 0.0, 0.35), (-0.03, 0.02, 0.05)], rng)), (-20, 55)),
    "coins-cp": ("coins", lambda rng: coins(rng, (0.80, 0.42, 0.22), "Copper",
                                             stack_layout([(-0.008, 0.01, 3), (0.018, 0.004, 2)], [(-0.03, -0.018, 0.25), (-0.004, -0.028, 0.1), (0.024, -0.024, 0.4), (0.036, 0.02, 0.15), (-0.036, 0.018, 0.3)], rng)), (-20, 55)),
    "silver-holy-symbol": ("valuables", holy_symbol, (-15, 28)),
    "bloodstone": ("valuables", bloodstones, (-20, 35)),
    "bone-dice": ("trinkets", bone_dice, (-20, 35)),
    "silk-scarf": ("valuables", silk_scarf, (-20, 50)),
    "pewter-tankard": ("valuables", tankard, (-30, 12)),
    "gorrums-amber": ("valuables", amber, (-15, 10)),
    "party-bag": ("bag", backpack, (-25, 10)),
}
for _c in CONTAINERS:
    ITEMS[f"container-{_c}"] = ("containers", container(_c), (-35, 30))


def posed(item):
    """Build an item and turn it into its pose, centred on the origin. Returns the meshes and
    its extent across the picture (x by z after the turn)."""
    ri.clear()
    for block in (bpy.data.materials,):
        for m in list(block):
            if m.users == 0:
                block.remove(m)
    family, builder, (turn, tilt) = ITEMS[item]
    rng = random.Random(sum(map(ord, item)))
    objs = [o for o in builder(rng) if o is not None and o.type == "MESH"]
    bpy.context.view_layer.update()
    m = Matrix.Rotation(math.radians(tilt), 4, "X") @ Matrix.Rotation(math.radians(turn), 4, "Z")
    for o in objs:
        o.matrix_world = m @ o.matrix_world
    bpy.context.view_layer.update()
    lo, hi = ri.bounds(objs)
    centre = (lo + hi) / 2
    for o in objs:
        o.matrix_world = Matrix.Translation(-centre) @ o.matrix_world
    bpy.context.view_layer.update()
    lo, hi = ri.bounds(objs)
    return objs, max(hi.x - lo.x, hi.z - lo.z)


def render(item, out, extent):
    scene = bpy.context.scene
    ri.light(scene)
    ri.camera(scene, extent, 1.0)
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    scene.render.film_transparent = True
    scene.render.resolution_x = ri.SIZE
    scene.render.resolution_y = ri.SIZE
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "AgX"
    scene.render.filepath = out
    bpy.ops.render.render(write_still=True)
    print(f"ICON {item:24} -> {out}")


def family_extents(items):
    """Every family is drawn at one scale: its largest member's extent over its share."""
    sizes = {}
    for item in ITEMS:
        family = ITEMS[item][0]
        if family not in {ITEMS[i][0] for i in items}:
            continue
        if family == "containers":
            continue
        _, extent = posed(item)
        sizes[family] = max(sizes.get(family, 0.0), extent)
    return {f: e / FAMILIES[f] for f, e in sizes.items()}


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        raise RuntimeError("Usage: blender -b --factory-startup --python render_item_icons.py -- OUTPUT_DIRECTORY [id ...] [--props DIR]")
    if "--props" in args:
        k = args.index("--props")
        PROPS = args[k + 1]
        del args[k:k + 2]
    target = args[0]
    os.makedirs(target, exist_ok=True)
    wanted = args[1:] or list(ITEMS)
    extents = family_extents(wanted)
    for item in wanted:
        objs, extent = posed(item)
        family = ITEMS[item][0]
        if family == "containers":
            # The loot window's title: each container nearly fills its slot, the small ones a
            # little less, so a sack is not drawn the size of a cart.
            frame = extent / (0.78 + 0.17 * min(1.0, extent / 1.2))
        else:
            frame = extents[family]
        render(item, os.path.join(target, f"{item}.png"), frame)

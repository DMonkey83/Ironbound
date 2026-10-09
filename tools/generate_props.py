"""Props: the containers loot is taken from, and the furniture the levels are furnished with.

    blender -b --factory-startup --python tools/generate_props.py -- src/Ironbound.Game/art/props [id ...]

One .glb per prop, baked by `surface.py` like everything it stands next to: one atlas per prop,
colour + ORM + normal, 1024 square. Built from boards (`props_parts.board_mesh`) that are cut out
of true, bevelled, worn and chipped as geometry, with nails and rivets as geometry, iron bands
swept round the wood they bind, rope laid from three strands; the textures add grain, rust,
mould and paint (`props_surface`), and rust is run down the wood from every nail after the bake.

SCALE. One board unit is one square, five feet; a man stands 1.4 of them (`Main.CapsuleHeight`),
so a prop is built at about 0.8 units to the metre: a crate comes to a man's hip. Blender's Z is
up (Godot's Y), the front of every prop faces -Y (Godot's +Z, the side the default camera sees),
and the origin is on the floor in the middle of the footprint.

NODES. A container is a few nodes in one file, so the game shows its state without a second
model:

- the body, named after the prop;
- `Lid`, its origin exactly on the hinge line, so the game turns it about its local X to open
  it. A hinged lid opens with a negative turn (the front rises, as with the chest's -100°); a
  lid with no hinge (crate, barrel) has its origin at its own centre and is lifted off. The
  hinge axis and opening angle are also written on the node as glTF extras (`hinge`, `open`).
- `Contents`: what shows inside when it is open. Hidden once the container is empty.
- `Lock`: the padlock or hasp. Hidden once picked or broken.
- `FX_Glint`: an empty at the top centre, where a lootable sparkle goes.

The bake is done with the lid lifted clear, so the inside of a box is lit as an open box and not
baked black, and with a floor under it, so the foot is darker where it meets the ground.
"""
import json
import math
import os
import random
import struct
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import surface  # noqa: E402
import props_surface as ps  # noqa: E402
import props_parts as pp  # noqa: E402

ps.register()


def M(name):
    return ps.material(name)


# --- the scene ---------------------------------------------------------------------------------

def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.curves):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


class Build:
    """What a builder hands back: parts per node, the lid's pivot, rust sources."""

    def __init__(self, name):
        self.name = name
        self.nodes = {}
        self.pivots = {}
        self.extras = {}
        self.rust = []
        self.glint = None
        self.atlas = 1024
        self.borrow = []
        self.unbaked_uvs = []

    def add(self, node, *parts):
        self.nodes.setdefault(node, []).extend(p for p in parts if p is not None)

    def nails(self, node, name, material, rng, spots, radius=0.0065, reach=0.08, strength=0.8, flat=False):
        part, heads = pp.nails(name, material, rng, spots, radius, flat=flat)
        self.add(node, part)
        for h in heads:
            if rng.random() < 0.85:
                self.rust.append((tuple(h), reach * rng.uniform(0.5, 1.3), strength * rng.uniform(0.5, 1.0)))
        return part


# --- crates -----------------------------------------------------------------------------------

def crate(rng, variant="a", lite=False):
    """A stackable plank crate: boards on a frame of cleats, skids under it, rope handles in the
    ends, a lid nailed down on two battens."""
    a = variant == "a"
    b = Build("crate" if a else "crate-b")
    W, D, H = (0.78, 0.58, 0.64) if a else (0.62, 0.48, 0.47)
    t = 0.022                         # board
    tc = 0.021                        # cleat
    cw = 0.058 if a else 0.05         # cleat width
    skid = 0.038
    lid_t, bat_t = 0.02, 0.022
    Hb = H - lid_t - bat_t            # top of the walls
    z0 = skid + t                     # walls stand on the bottom boards
    wood = M("Prop_Pine") if a else M("Prop_Pine_Worn")
    iron = M("Prop_Iron_Rusty")
    wear = 1.0 if a else 1.8
    chips = 2 if a else 4
    side_boards = 4 if a else 3
    end_boards = 3 if a else 3
    lid_boards = 4 if a else 3
    nail_spots = []

    def wb(name, centre, along, out, length, width, thick, node="body", **kw):
        kw.setdefault("wear", wear)
        kw.setdefault("chips", chips)
        if lite:
            # Thrown out of the wreck and small: fewer cuts along each board.
            kw["seg"] = 0.25
        part = pp.slab(name, wood, rng, centre, along, out, length, width, thick, **kw)
        b.add(node, part)
        return part

    # Skids and bottom.
    for s in (-1, 1):
        wb(f"Skid_{s}", (0, s * (D / 2 - 0.085), skid / 2), (1, 0, 0), (0, 0, -1), W - 0.03, 0.055, skid, chips=1, seg=0.2)
    for i, (at, w) in enumerate(pp.split(D, 4 if a else 3, 0.004, rng)):
        wb(f"Bottom_{i}", (0, -D / 2 + at, skid + t / 2), (1, 0, 0), (0, 0, -1), W, w, t, chips=1, seg=0.2)

    # Front and back: boards along X the full width; the ends sit between them.
    wall = Hb - z0
    front_rows = pp.split(wall, side_boards, 0.006 if a else 0.009, rng, 0.2)
    for s, label in ((-1, "Front"), (1, "Back")):
        for i, (at, w) in enumerate(front_rows):
            kw = {}
            if not a and s == -1 and i == side_boards - 1:
                # Crate B's top front board has had a corner split off.
                kw["notch"] = ((W / 2 - 0.02, -t / 2, w / 2 - 0.01), 0.07)
            part = wb(f"{label}_{i}", (0, s * (D / 2 - t / 2), z0 + at), (1, 0, 0), (0, s, 0), W, w, t,
                      bow=0.003 * s, **kw)
    for s in (-1, 1):
        for i, (at, w) in enumerate(pp.split(wall, end_boards, 0.006, rng, 0.2)):
            wb(f"End_{s}_{i}", (s * (W / 2 - t / 2), 0, z0 + at), (0, 1, 0), (s, 0, 0), D - 2 * t, w, t)

    # Cleats. The ends get a full frame; the front and back get uprights at each corner, and
    # crate A a frame and a diagonal brace as well.
    def cleat(name, a_, b_, out, width=cw, node="body"):
        part = pp.plank_run(name, wood, rng, a_, b_, width, tc, out, wear=wear, chips=1, bevel=0.006)
        b.add(node, part)
        return part

    for s in (-1, 1):
        x = s * (W / 2 + tc / 2)
        for e in (-1, 1):
            y = e * (D / 2 - cw / 2)
            cleat(f"EndPost_{s}_{e}", (x, y, z0 - 0.004), (x, y, Hb + 0.002), (s, 0, 0))
            for row_at, _ in front_rows:
                nail_spots.append(((x + s * tc / 2, y + rng.uniform(-0.01, 0.01), z0 + row_at), (s, 0, 0)))
        for zz, lab in ((z0 + cw / 2, "Lo"), (Hb - cw / 2, "Hi")):
            cleat(f"EndRail_{s}_{lab}", (x, -D / 2 + cw, zz), (x, D / 2 - cw, zz), (s, 0, 0))
            for yy in (-D / 2 + cw + 0.04, 0.0, D / 2 - cw - 0.04):
                nail_spots.append(((x + s * tc / 2, yy, zz), (s, 0, 0)))

    for s in (-1, 1):
        y = s * (D / 2 + tc / 2)
        for e in (-1, 1):
            xx = e * (W / 2 - cw / 2)
            cleat(f"Post_{s}_{e}", (xx, y, z0 - 0.004), (xx, y, Hb + 0.002), (0, s, 0))
            for row_at, _ in front_rows:
                nail_spots.append(((xx + rng.uniform(-0.008, 0.008), y + s * tc / 2, z0 + row_at), (0, s, 0)))
        if a:
            for zz, lab in ((z0 + cw / 2, "Lo"), (Hb - cw / 2, "Hi")):
                cleat(f"Rail_{s}_{lab}", (-W / 2 + cw, y, zz), (W / 2 - cw, y, zz), (0, s, 0))
                for xx in (-W / 4, W / 4):
                    nail_spots.append(((xx, y + s * tc / 2, zz), (0, s, 0)))
            # The brace runs from a bottom corner to the opposite top corner inside the frame.
            p0 = Vector((-s * (W / 2 - cw - 0.02), y, z0 + cw + 0.02))
            p1 = Vector((s * (W / 2 - cw - 0.02), y, Hb - cw - 0.02))
            cleat(f"Brace_{s}", p0, p1, (0, s, 0), width=cw * 0.95)
            for row_at, _ in front_rows:
                zz = z0 + row_at
                k = (zz - p0.z) / (p1.z - p0.z)
                if 0.08 < k < 0.92:
                    nail_spots.append(((p0.x + (p1.x - p0.x) * k, y + s * tc / 2, zz), (0, s, 0)))

    b.nails("body", "Nails", iron, rng, nail_spots, 0.0068 if a else 0.0062, reach=0.09)

    # Rope handles: through two blocks on each end, sagging between, knotted outside.
    zb = Hb - (0.16 if a else 0.13)
    for s in (() if lite else (-1, 1)):
        x = s * (W / 2 + tc)
        for e in (-1, 1):
            y = e * (0.10 if a else 0.08)
            blk = pp.slab(f"Block_{s}_{e}", wood, rng, (x + s * 0.016, y, zb), (0, 1, 0), (s, 0, 0), 0.07, 0.05, 0.032, wear=wear, chips=1, bevel=0.006)
            b.add("body", blk)
            b.nails("body", f"BlockNails_{s}_{e}", iron, rng, [((x + s * 0.032, y + q * 0.022, zb), (s, 0, 0)) for q in (-1, 1)], 0.0055, reach=0.06)
        r = 0.0085 if a else 0.0075
        span = 0.10 if a else 0.08
        out = s * 0.034
        path = [(x + out, -span - 0.04, zb), (x + out + s * 0.012, -span - 0.02, zb - 0.03)]
        path += [(x + out + s * 0.02, span * k, zb - 0.07 - 0.012 * (1 - k * k)) for k in (-0.6, -0.2, 0.2, 0.6)]
        path += [(x + out + s * 0.012, span + 0.02, zb - 0.03), (x + out, span + 0.04, zb)]
        b.add("body", pp.rope(f"Handle_{s}", M("Prop_Rope"), path, r, rng))
        for e in (-1, 1):
            b.add("body", pp.heap(f"Knot_{s}_{e}", M("Prop_Rope"), (x + out, e * (span + 0.052), zb), (0.016, 0.014, 0.016), rng, res=6, bumps=0.4, freq=40))

    # The lid: boards across, two battens on top, nailed down into the walls' top edges.
    for i, (at, w) in enumerate(pp.split(D + 0.006, lid_boards, 0.005, rng, 0.25)):
        wb(f"Lid_{i}", (0, -D / 2 - 0.003 + at, Hb + lid_t / 2), (1, 0, 0), (0, 0, 1), W + 0.01, w, lid_t, node="Lid", bow=-0.002)
    lid_nails = []
    bx = W / 2 - (0.075 if a else 0.065)
    for e in ((-1, 1) if a else (-1, 1)):
        x = e * bx
        part = pp.plank_run(f"Batten_{e}", wood, rng, (x, -D / 2 + 0.01, Hb + lid_t + bat_t / 2), (x, D / 2 - 0.01, Hb + lid_t + bat_t / 2),
                            0.06, bat_t, (0, 0, 1), wear=wear, chips=2, bevel=0.006)
        b.add("Lid", part)
        for (at, w) in pp.split(D + 0.006, lid_boards, 0.005, rng, 0.0):
            lid_nails.append(((x + rng.uniform(-0.012, 0.012), -D / 2 + at, Hb + lid_t + bat_t), (0, 0, 1)))
    for e in (-1, 1):
        for k in range(4 if a else 3):
            xx = -W / 2 + 0.16 + (W - 0.32) * k / (3 if a else 2)
            lid_nails.append(((xx + rng.uniform(-0.02, 0.02), e * (D / 2 - t / 2), Hb + lid_t), (0, 0, 1)))
    b.nails("Lid", "LidNails", iron, rng, lid_nails, 0.0068, reach=0.05)
    b.pivots["Lid"] = (0.0, 0.0, Hb + (lid_t + bat_t) / 2)
    b.extras["Lid"] = {"hinge": "none", "open": "lift"}

    # Inside: a little straw left on the floor whatever happens; the packed load is Contents.
    floor_z = z0
    inner = ((-W / 2 + t + 0.01, -D / 2 + t + 0.01), (W / 2 - t - 0.01, D / 2 - t - 0.01))
    bm = pp.bmesh.new()
    pp.straw_strands(bm, rng, (0, 0, floor_z + 0.004), (W / 2 - t - 0.03, D / 2 - t - 0.03), 70, (0.05, 0.10), 0.006, 0.1, 0.05, inner)
    floor_straw = pp.to_object("FloorStraw", bm, M("Prop_Straw"), sharp_angle=0)
    b.add("body", floor_straw)

    top = Hb - (0.075 if a else 0.065)
    bed, stalks, height = pp.packing("Packing", M("Prop_Straw"), rng, inner[0], inner[1], floor_z, top,
                                     strands=260 if a else 200, over_rim=30 if a else 18, rim_z=Hb)
    b.add("Contents", bed, stalks)
    b.borrow.append((stalks, bed, 9))
    b.borrow.append((floor_straw, bed, 6))
    if a:
        # Tobin's draughts: stoneware and glass necks standing out of the straw.
        spots = [(-0.22, -0.10), (-0.08, 0.07), (0.10, -0.07), (0.24, 0.09), (0.0, -0.16), (-0.25, 0.12)]
        for i, (x, y) in enumerate(spots):
            glass = i % 2 == 0
            mat = M("Prop_Glass") if glass else M("Prop_Bone")
            base = height(x, y) - 0.065
            prof = [(0.0, base), (0.036, base + 0.002), (0.038, base + 0.07), (0.030, base + 0.095), (0.014, base + 0.115),
                    (0.012, base + 0.15), (0.016, base + 0.155), (0.016, base + 0.165), (0.011, base + 0.168)]
            tilt = Matrix.Rotation(rng.uniform(-0.25, 0.25), 3, "X") @ Matrix.Rotation(rng.uniform(-0.25, 0.25), 3, "Y")
            b.add("Contents", pp.lathe(f"Bottle_{i}", mat, prof, 12, (x, y, 0), tilt, sharp_angle=60))
            cork_base = base + 0.163
            cork = pp.lathe(f"Cork_{i}", M("Prop_Bread"), [(0.0, cork_base), (0.010, cork_base), (0.011, cork_base + 0.02), (0.0, cork_base + 0.022)], 8, (x, y, 0), tilt)
            b.add("Contents", cork)
    else:
        # Rope, torches and bread: what the storeroom said.
        coil = []
        cx, cy = -0.10, 0.03
        base = height(cx, cy) - 0.012
        for k in range(int(3.5 * 22)):
            ang = k / 22 * math.tau
            rad = 0.085 - 0.004 * (k / 22)
            coil.append((cx + rad * math.cos(ang), cy + rad * math.sin(ang), base + 0.013 * k / 22))
        b.add("Contents", pp.rope("Coil", M("Prop_Rope"), coil, 0.0105, rng, segs_per_unit=70))
        for i in range(3):
            y = -0.13 + i * 0.045
            x0 = 0.02 + rng.uniform(-0.01, 0.01)
            z = height(x0 + 0.14, y) + 0.008
            stick = pp.lathe(f"Torch_{i}", M("Prop_Oak_Clean"), [(0.0, 0.0), (0.011, 0.0), (0.011, 0.24), (0.0, 0.24)], 7, (x0, y, z),
                             Matrix.Rotation(math.radians(88), 3, "Y") @ Matrix.Rotation(rng.uniform(-0.1, 0.1), 3, "X"))
            head = pp.heap(f"TorchHead_{i}", M("Prop_Cloth"), (x0 + 0.23, y, z), (0.045, 0.02, 0.02), rng, res=8, bumps=0.25, freq=30)
            b.add("Contents", stick, head)
        b.add("Contents", pp.heap("Bread", M("Prop_Bread"), (-0.13, -0.13, height(-0.13, -0.13) + 0.01), (0.07, 0.045, 0.035), rng, res=10, bumps=0.12, freq=14))

    b.glint = (0.0, 0.0, H + 0.06)
    return b


def perimeter_band(b, name, node, W, D, z, height, thick, material, rng, corner=0.006):
    """An iron band right round a box at height z, lying on its faces, its edges knocked off."""
    path = pp.rounded_rect_path(0, 0, W / 2 + 0.0005, D / 2 + 0.0005, corner, 0.0, 2)
    path = [(p[0], p[2], z) for p in path]
    nrm = []
    for p in path:
        on_x = abs(abs(p[0]) - (W / 2 + 0.0005)) < corner * 1.1
        on_y = abs(abs(p[1]) - (D / 2 + 0.0005)) < corner * 1.1
        if on_x and on_y:
            nrm.append(Vector((p[0], p[1], 0)).normalized())
        elif on_x:
            nrm.append(Vector((math.copysign(1, p[0]), 0, 0)))
        else:
            nrm.append(Vector((0, math.copysign(1, p[1]), 0)))
    c = min(0.0015, thick * 0.35)
    up = Vector((0, 0, 1))
    sections = []
    for p, n in zip(path, nrm):
        p = Vector(p)
        sections.append([p + up * (-height / 2), p + n * thick + up * (-height / 2 + c), p + n * thick + up * (height / 2 - c), p + up * (height / 2)])
    sections.append(sections[0])
    bm = pp.loft_mesh(sections, closed=True, cap=False)
    pp.hammered(bm, rng, thick * 0.1, 50)
    b.add(node, pp.to_object(name, bm, material, sharp_angle=50))


def plate(b, node, name, material, rng, centre, across, out, length, width, thick, bevel=0.0018, taper=0.0):
    """A flat piece of iron or brass on a face: a lock plate, a corner leaf, a hinge strap."""
    bm = pp.board_mesh(length, width, thick, rng, seg=0.05, bevel=bevel, wear=0.25, chips=0, bow=0, rough_ends=0.0008, taper=taper)
    z = Vector(across).normalized()
    y = -Vector(out).normalized()
    x = y.cross(z)
    pp.place(bm, pp.frame(centre, x, y, z))
    pp.hammered(bm, rng, thick * 0.08, 50)
    part = pp.to_object(name, bm, material, wood=False, sharp_angle=50)
    b.add(node, part)
    return part


# --- the chest -------------------------------------------------------------------------------

def chest(rng):
    """A travel chest: a painted board box with a barrel-vaulted lid, iron corners and straps
    studded with brass, a brass hasp and padlock, and leather handles at the ends."""
    b = Build("chest")
    W, D = 0.80, 0.47
    Hb = 0.34                         # body, to the hinge line
    rise = 0.12                       # the lid's vault
    rim = 0.045                       # the lid's straight walls under the vault
    t = 0.02
    wood = M("Prop_Chest_Wood")
    iron = M("Prop_Iron")
    brass = M("Prop_Brass")
    z0 = 0.025                        # on two skids
    studs = []

    def wb(name, centre, along, out, length, width, thick, node="body", **kw):
        kw.setdefault("wear", 0.8)
        kw.setdefault("chips", 1)
        kw.setdefault("bevel", 0.004)
        part = pp.slab(name, wood, rng, centre, along, out, length, width, thick, **kw)
        b.add(node, part)
        return part

    for s in (-1, 1):
        wb(f"Skid_{s}", (0, s * (D / 2 - 0.07), z0 / 2), (1, 0, 0), (0, 0, -1), W - 0.04, 0.05, z0, chips=1)
    for i, (at, w) in enumerate(pp.split(D, 3, 0.002, rng, 0.1)):
        wb(f"Bottom_{i}", (0, -D / 2 + at, z0 + t / 2), (1, 0, 0), (0, 0, -1), W, w, t)
    wall_lo = z0 + t
    # Tight joints: a chest is joinery, not slats, and a gap here showed the lit inside as a
    # white line.
    rows = pp.split(Hb - wall_lo, 3, -0.0005, rng, 0.15)
    for s, label in ((-1, "Front"), (1, "Back")):
        for i, (at, w) in enumerate(rows):
            wb(f"{label}_{i}", (0, s * (D / 2 - t / 2), wall_lo + at), (1, 0, 0), (0, s, 0), W, w, t)
    for s in (-1, 1):
        for i, (at, w) in enumerate(pp.split(Hb - wall_lo, 2, -0.0005, rng, 0.1)):
            wb(f"End_{s}_{i}", (s * (W / 2 - t / 2), 0, wall_lo + at), (0, 1, 0), (s, 0, 0), D - 2 * t, w, t)

    # The lid: straight walls front and back, arched ends, and laths over the vault.
    lz = Hb
    for s, label in ((-1, "LidFront"), (1, "LidBack")):
        wb(f"{label}", (0, s * (D / 2 - t / 2), lz + rim / 2), (1, 0, 0), (0, s, 0), W, rim, t, node="Lid", bow=0.0)
    # The vault is a circular arc from front to back through `rise`.
    half = D / 2
    R = (half ** 2 + rise ** 2) / (2 * rise)
    cz = lz + rim + rise - R

    def arc(u):
        """u in [-1, 1] front to back -> (y, z, outward normal y, z)."""
        a = math.asin(u * half / R)
        return R * math.sin(a), cz + R * math.cos(a), math.sin(a), math.cos(a)

    # End boards: arch-topped slabs, built as a polygon and given thickness.
    for s in (-1, 1):
        bm = pp.bmesh.new()
        outline = [(-half, lz), (half, lz)]
        for k in range(12, -1, -1):
            y, z, _, _ = arc(-1 + 2 * k / 12)
            outline.append((y * 0.995, z - 0.002))
        verts = [bm.verts.new((s * (W / 2 - t / 2) - t / 2, y, z)) for (y, z) in outline]
        face = bm.faces.new(verts)
        ext = pp.bmesh.ops.extrude_face_region(bm, geom=[face])
        moved = [v for v in ext["geom"] if isinstance(v, pp.bmesh.types.BMVert)]
        pp.bmesh.ops.translate(bm, vec=(t, 0, 0), verts=moved)
        pp.bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        pp._bevel_sharp(bm, 0.003, 2)
        pp._grain(bm, rng)
        b.add("Lid", pp.to_object(f"LidEnd_{s}", bm, wood))
    laths = 7
    for i in range(laths):
        u0, u1 = -1 + 2 * i / laths, -1 + 2 * (i + 1) / laths
        y0, z0_, _, _ = arc(u0)
        y1, z1_, _, _ = arc(u1)
        ym, zm, ny, nz = arc((u0 + u1) / 2)
        width = math.hypot(y1 - y0, z1_ - z0_) - 0.003
        centre = (0, ym - ny * t / 2, zm - nz * t / 2)
        wb(f"Lath_{i}", centre, (1, 0, 0), (0, ny, nz), W + 0.004, width, t, node="Lid", bow=0.0, chips=1, wear=0.6)

    # Iron. Two straps run down the front, over the vault and down the back; they are cut at
    # the lid line, the lid's halves doubling as the hinge leaves at the back.
    sw, st = 0.05, 0.005
    for xs in (-0.23, 0.23):
        body_front = [(xs, -D / 2 - 0.0005, wall_lo - 0.005 + (Hb - 0.004 - wall_lo + 0.005) * k / 4) for k in range(5)]
        n_front = [(0, -1, 0)] * 5
        b.add("body", pp.strap_around(f"StrapF_{xs}", iron, rng, body_front, sw, st, n_front))
        body_back = [(xs, D / 2 + 0.0005, wall_lo - 0.005 + (Hb - 0.004 - wall_lo + 0.005) * k / 4) for k in range(5)]
        b.add("body", pp.strap_around(f"StrapB_{xs}", iron, rng, body_back, sw, st, [(0, 1, 0)] * 5))
        lid_path, lid_n = [], []
        lid_path.append((xs, -D / 2 - 0.0005, lz + 0.004))
        lid_n.append((0, -1, 0))
        lid_path.append((xs, -D / 2 - 0.0005, lz + rim))
        lid_n.append((0, -1, 0))
        for k in range(17):
            y, z, ny, nz = arc(-1 + 2 * k / 16)
            lid_path.append((xs, y + ny * 0.0005, z + nz * 0.0005))
            lid_n.append((0, ny, nz))
        lid_path.append((xs, D / 2 + 0.0005, lz + rim))
        lid_n.append((0, 1, 0))
        lid_path.append((xs, D / 2 + 0.0005, lz + 0.004))
        lid_n.append((0, 1, 0))
        b.add("Lid", pp.strap_around(f"StrapLid_{xs}", iron, rng, lid_path, sw, st, lid_n))
        # Studs: brass heads down each strap.
        for k in range(4):
            z = wall_lo + 0.03 + (Hb - wall_lo - 0.06) * k / 3
            studs.append(("body", (xs, -D / 2 - st, z), (0, -1, 0)))
            studs.append(("body", (xs, D / 2 + st, z), (0, 1, 0)))
        for k in range(1, 8):
            y, z, ny, nz = arc(-1 + 2 * k / 8)
            studs.append(("Lid", (xs, y + ny * st, z + nz * st), (0, ny, nz)))
        studs.append(("Lid", (xs, -D / 2 - st, lz + rim * 0.5), (0, -1, 0)))
        # Hinge knuckles on the back, on the hinge line.
        for k, dx in enumerate((-0.03, 0.0, 0.03)):
            node = "Lid" if k == 1 else "body"
            kn = pp.lathe(f"Knuckle_{xs}_{k}", iron, [(0.0, -0.014), (0.0085, -0.014), (0.0085, 0.014), (0.0, 0.014)], 10,
                          (xs + dx, D / 2 + 0.006, Hb), Matrix.Rotation(math.radians(90), 3, "Y"))
            b.add(node, kn)
        b.add("body", pp.lathe(f"Pin_{xs}", iron, [(0.0, -0.05), (0.004, -0.05), (0.004, 0.05), (0.0, 0.05)], 6,
                               (xs, D / 2 + 0.006, Hb), Matrix.Rotation(math.radians(90), 3, "Y")))

    # Bands round the body's foot and the lid's rim.
    perimeter_band(b, "FootBand", "body", W, D, wall_lo + 0.022, 0.04, 0.0045, iron, rng)
    perimeter_band(b, "LidBand", "Lid", W, D, lz + 0.021, 0.036, 0.0045, iron, rng)
    perimeter_band(b, "TopBand", "body", W, D, Hb - 0.02, 0.032, 0.0045, iron, rng)

    # Corner irons: angle plates up every vertical corner of the body.
    for sx in (-1, 1):
        for sy in (-1, 1):
            for (z_a, z_b) in ((wall_lo - 0.004, wall_lo + 0.11), (Hb - 0.10, Hb - 0.002)):
                for face in ("x", "y"):
                    if face == "x":
                        c = (sx * (W / 2 + 0.003), sy * (D / 2 - 0.024), (z_a + z_b) / 2)
                        bm = pp.board_mesh(z_b - z_a, 0.05, 0.005, rng, seg=0.05, bevel=0.0015, wear=0.3, chips=0, bow=0, rough_ends=0.001)
                        pp.place(bm, pp.frame(c, (0, 1, 0), (-sx, 0, 0), (0, 0, 1)))
                    else:
                        c = (sx * (W / 2 - 0.024), sy * (D / 2 + 0.003), (z_a + z_b) / 2)
                        bm = pp.board_mesh(z_b - z_a, 0.05, 0.005, rng, seg=0.05, bevel=0.0015, wear=0.3, chips=0, bow=0, rough_ends=0.001)
                        pp.place(bm, pp.frame(c, (1, 0, 0), (0, -sy, 0), (0, 0, 1)))
                    pp.hammered(bm, rng, 0.0006, 50)
                    b.add("body", pp.to_object(f"Corner_{sx}_{sy}_{z_a:.2f}_{face}", bm, iron, wood=False, sharp_angle=50))
                    zz = (z_a + z_b) / 2
                    if face == "x":
                        studs.append(("body", (sx * (W / 2 + 0.0055), sy * (D / 2 - 0.024), zz), (sx, 0, 0)))
                    else:
                        studs.append(("body", (sx * (W / 2 - 0.024), sy * (D / 2 + 0.0055), zz), (0, sy, 0)))
            # The lid's corners: a cap where the wall meets the arched end.
            for face in ("x", "y"):
                zc = lz + rim / 2 + 0.006
                if face == "x":
                    c = (sx * (W / 2 + 0.003), sy * (D / 2 - 0.026), zc)
                    bm = pp.board_mesh(rim + 0.012, 0.052, 0.005, rng, seg=0.05, bevel=0.0015, wear=0.3, chips=0, bow=0, rough_ends=0.001)
                    pp.place(bm, pp.frame(c, (0, 1, 0), (-sx, 0, 0), (0, 0, 1)))
                else:
                    c = (sx * (W / 2 - 0.026), sy * (D / 2 + 0.003), zc)
                    bm = pp.board_mesh(rim + 0.012, 0.052, 0.005, rng, seg=0.05, bevel=0.0015, wear=0.3, chips=0, bow=0, rough_ends=0.001)
                    pp.place(bm, pp.frame(c, (1, 0, 0), (0, -sy, 0), (0, 0, 1)))
                pp.hammered(bm, rng, 0.0006, 50)
                b.add("Lid", pp.to_object(f"LidCorner_{sx}_{sy}_{face}", bm, iron, wood=False, sharp_angle=50))
    # The arched ends' edges are bound with a band following the vault.
    for s in (-1, 1):
        path, nrm = [], []
        for k in range(21):
            y, z, ny, nz = arc(-1 + 2 * k / 20)
            path.append((s * (W / 2 + 0.0005) - s * 0.016, y + ny * 0.0005, z + nz * 0.0005))
            nrm.append((0, ny, nz))
        b.add("Lid", pp.strap_around(f"VaultEdge_{s}", iron, rng, path, 0.034, 0.0045, nrm))

    # Brass studs everywhere the iron is fixed.
    for node in ("body", "Lid"):
        spots = [(p, n) for (nd, p, n) in studs if nd == node]
        b.nails(node, f"Studs_{node}", brass, rng, spots, 0.0075, reach=0.06, strength=0.5)

    # The lock: a brass plate on the front, a hasp hanging from the lid over a staple, and the
    # padlock through the staple.
    plate_c = (0.0, -D / 2 - 0.002, Hb - 0.045)
    bm = pp.board_mesh(0.085, 0.075, 0.004, rng, seg=0.04, bevel=0.0015, wear=0.2, chips=0, bow=0, rough_ends=0.001)
    pp.place(bm, pp.frame(plate_c, (1, 0, 0), (0, 1, 0), (0, 0, 1)))
    b.add("body", pp.to_object("LockPlate", bm, brass, wood=False, sharp_angle=50))
    b.nails("body", "PlateNails", brass, rng, [((sx * 0.03, -D / 2 - 0.004, plate_c[2] + sz * 0.034), (0, -1, 0)) for sx in (-1, 1) for sz in (-1, 1)], 0.005, reach=0.05, strength=0.4)
    # The staple: a loop standing out of the plate.
    staple = pp.torus("Staple", iron, (0, -D / 2 - 0.016, Hb - 0.055), 0.013, 0.004, normal=(1, 0, 0), segs=14, minor_segs=6)
    b.add("body", staple)
    # The hasp: a brass tongue hinged on the lid's front, hanging down over the staple.
    hz_top = lz + rim * 0.75
    bm = pp.board_mesh(0.105, 0.05, 0.006, rng, seg=0.03, bevel=0.002, wear=0.2, chips=0, bow=0, rough_ends=0.001, taper=0.15)
    pp.place(bm, pp.frame((0, -D / 2 - 0.010, hz_top - 0.05), (1, 0, 0), (0, 1, 0), (0, 0, -1)))
    b.add("Lid", pp.to_object("Hasp", bm, brass, wood=False, sharp_angle=50))
    b.add("Lid", pp.lathe("HaspHinge", brass, [(0.0, -0.022), (0.006, -0.022), (0.006, 0.022), (0.0, 0.022)], 10,
                          (0, -D / 2 - 0.009, hz_top), Matrix.Rotation(math.radians(90), 3, "Y")))
    # Padlock: a heavy brass body, the shackle through the staple.
    # The shackle's crown passes through the middle of the staple's eye.
    lock_c = Vector((0, -D / 2 - 0.020, Hb - 0.055 - 0.022 - 0.043))
    body = pp.lathe("PadlockBody", brass, [(0.0, -0.015), (0.031, -0.015), (0.037, -0.008), (0.037, 0.008), (0.031, 0.015), (0.0, 0.015)], 16,
                    lock_c, Matrix.Rotation(math.radians(90), 3, "X"))
    b.add("Lock", body)
    b.add("Lock", pp.torus("Shackle", iron, lock_c + Vector((0, 0.0, 0.043)), 0.022, 0.0052, normal=(0, 1, 0), segs=16, minor_segs=6, open_angle=math.radians(110)))
    b.add("Lock", pp.lathe("Keyhole", iron, [(0.0, 0.0), (0.0075, 0.0), (0.0075, 0.0025), (0.0, 0.0025)], 8, lock_c + Vector((0, -0.015, -0.004)), Matrix.Rotation(math.radians(90), 3, "X")))
    b.add("Lock", pp.lathe("KeySlot", iron, [(0.0, 0.0), (0.003, 0.0), (0.003, 0.0025), (0.0, 0.0025)], 6, lock_c + Vector((0, -0.015, -0.014)), Matrix.Rotation(math.radians(90), 3, "X")))

    # Leather handles: a strap between two iron loops on each end.
    for s in (-1, 1):
        x = s * (W / 2 + 0.004)
        hz = Hb - 0.10
        for e in (-1, 1):
            b.add("body", pp.torus(f"HandleLoop_{s}_{e}", iron, (x + s * 0.008, e * 0.075, hz), 0.012, 0.0035, normal=(0, 1, 0), segs=12, minor_segs=6))
            b.nails("body", f"LoopNails_{s}_{e}", iron, rng, [((x + s * 0.002, e * 0.075 + q * 0.017, hz), (s, 0, 0)) for q in (-1, 1)], 0.0045, reach=0.05)
        path, nrm = [], []
        for k in range(13):
            u = -1 + 2 * k / 12
            path.append((x + s * (0.016 + 0.018 * (1 - u * u)), u * 0.082, hz - 0.012 - 0.045 * (1 - u * u)))
            nrm.append((s, 0, 0.0))
        bm = pp.sweep(path, pp.band_profile(0.026, 0.006), [(s * (1 - u * u) * 0.3, 0, 1) for u in (-1 + 2 * k / 12 for k in range(13))])
        b.add("body", pp.to_object(f"Handle_{s}", bm, M("Prop_Leather"), sharp_angle=60))

    # Inside: a folded cloth, coin heaped against it, the glint of a silver cup.
    floor = wall_lo
    b.add("Contents", pp.folded_cloth("Cloth", M("Prop_Cloth"), rng, (0.16, 0.03, floor), (0.30, 0.26), 4, 0.014, 0.1))
    b.add("Contents", pp.coin_pile("Coins", M("Prop_Gold"), rng, (-0.12, -0.01, floor), 0.19, 0.12, 170, 0.012, squash_y=0.85))
    cup = pp.lathe("Cup", M("Prop_Silver"), [(0.0, 0.0), (0.03, 0.0), (0.032, 0.006), (0.008, 0.014), (0.007, 0.06), (0.03, 0.075), (0.042, 0.12), (0.038, 0.122), (0.025, 0.08), (0.0, 0.075)], 14,
                   (0.17, 0.05, floor + 0.05), Matrix.Rotation(0.3, 3, "Y"))
    b.add("Contents", cup)

    b.pivots["Lid"] = (0.0, D / 2 + 0.006, Hb)
    b.extras["Lid"] = {"hinge": "x", "open": -100}
    b.glint = (0.0, 0.0, Hb + rim + rise + 0.06)
    return b


# --- the strongbox -------------------------------------------------------------------------

def keyhole(name, material, centre, out, size):
    """A keyhole as a black inlay a hair proud of its plate: a round eye over a flared slot."""
    import bmesh
    bm = bmesh.new()
    out = Vector(out).normalized()
    up = Vector((0, 0, 1))
    side = up.cross(out).normalized()
    pts = []
    for k in range(12):
        a = math.radians(-60 + 300 * k / 11)
        pts.append((math.cos(a + math.pi / 2) * size * 0.5, size * 0.35 + math.sin(a + math.pi / 2) * size * 0.5))
    pts = [(size * 0.18, size * 0.15)] + pts[1:-1] + [(-size * 0.18, size * 0.15), (-size * 0.32, -size * 0.75), (size * 0.32, -size * 0.75)]
    verts = [bm.verts.new(Vector(centre) + side * x + up * z) for (x, z) in pts]
    bm.faces.new(verts)
    ext = bmesh.ops.extrude_face_region(bm, geom=bm.faces[:])
    bmesh.ops.translate(bm, vec=out * 0.0015, verts=[v for v in ext["geom"] if isinstance(v, bmesh.types.BMVert)])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return pp.to_object(name, bm, material, wood=False, sharp_angle=30)


def strongbox(rng):
    """Gorrum's hoard: thick dark oak gone almost entirely under iron — bands round it and
    over it, a rivet wherever iron crosses iron, heavy corner irons, a big lock plate with a
    keyhole, the hasp down over its staple, drop rings at the ends for four bearers."""
    b = Build("strongbox")
    W, D = 0.86, 0.60
    Hb = 0.44
    lid_t = 0.055
    t = 0.032
    wood = M("Prop_Oak_Dark")
    iron = M("Prop_Iron")
    foot = 0.04
    z0 = foot
    rivets = {"body": [], "Lid": []}

    def wb(name, centre, along, out, length, width, thick, node="body", **kw):
        kw.setdefault("wear", 0.8)
        kw.setdefault("chips", 2)
        kw.setdefault("bevel", 0.006)
        part = pp.slab(name, wood, rng, centre, along, out, length, width, thick, **kw)
        b.add(node, part)
        return part

    for i, (at, w) in enumerate(pp.split(D, 3, 0.002, rng, 0.1)):
        wb(f"Bottom_{i}", (0, -D / 2 + at, z0 + t / 2), (1, 0, 0), (0, 0, -1), W, w, t, seg=0.25)
    wall_lo = z0 + t
    rows = pp.split(Hb - wall_lo, 3, 0.003, rng, 0.12)
    for s_, label in ((-1, "Front"), (1, "Back")):
        for i, (at, w) in enumerate(rows):
            wb(f"{label}_{i}", (0, s_ * (D / 2 - t / 2), wall_lo + at), (1, 0, 0), (0, s_, 0), W, w, t)
    for s_ in (-1, 1):
        for i, (at, w) in enumerate(pp.split(Hb - wall_lo, 3, 0.003, rng, 0.12)):
            wb(f"End_{s_}_{i}", (s_ * (W / 2 - t / 2), 0, wall_lo + at), (0, 1, 0), (s_, 0, 0), D - 2 * t, w, t)
    for i, (at, w) in enumerate(pp.split(D + 0.014, 4, 0.003, rng, 0.15)):
        wb(f"Lid_{i}", (0, -D / 2 - 0.007 + at, Hb + lid_t / 2), (1, 0, 0), (0, 0, 1), W + 0.014, w, lid_t, node="Lid", bow=0.0)

    # Bands round the body and the lid's edge.
    bt = 0.008
    zs = [wall_lo + 0.03, (wall_lo + Hb) / 2 - 0.01, Hb - 0.032]
    for k, z in enumerate(zs):
        perimeter_band(b, f"Band_{k}", "body", W, D, z, 0.062, bt, iron, rng, 0.01)
    perimeter_band(b, "LidEdge", "Lid", W + 0.014, D + 0.014, Hb + lid_t / 2, lid_t + 0.004, bt, iron, rng, 0.008)

    # Uprights: four on the front and back, two on each end; over the lid as straps.
    xs = (-0.33, -0.18, 0.18, 0.33)
    ys = (-0.17, 0.17)
    for x in xs:
        for s_ in (-1, 1):
            plate(b, "body", f"Up_{x}_{s_}", iron, rng, (x, s_ * (D / 2 + bt + 0.0035), (wall_lo - 0.01 + Hb) / 2), (0, 0, 1), (0, s_, 0),
                  Hb - wall_lo + 0.006, 0.055, 0.008)
            for z in zs:
                rivets["body"].append(((x, s_ * (D / 2 + bt + 0.0085), z), (0, s_, 0)))
            for z in (wall_lo + 0.085, Hb - 0.10):
                rivets["body"].append(((x, s_ * (D / 2 + bt + 0.0085), z + rng.uniform(-0.01, 0.01)), (0, s_, 0)))
        plate(b, "Lid", f"LidStrap_{x}", iron, rng, (x, 0, Hb + lid_t + 0.004), (0, 1, 0), (0, 0, 1), D + 0.02, 0.055, 0.008)
        for k in range(5):
            y = -D / 2 + 0.03 + (D - 0.06) * k / 4
            rivets["Lid"].append(((x, y, Hb + lid_t + 0.0085), (0, 0, 1)))
    for y in ys:
        for s_ in (-1, 1):
            plate(b, "body", f"EndUp_{y}_{s_}", iron, rng, (s_ * (W / 2 + bt + 0.0035), y, (wall_lo - 0.01 + Hb) / 2), (0, 0, 1), (s_, 0, 0),
                  Hb - wall_lo + 0.006, 0.055, 0.008)
            for z in zs:
                rivets["body"].append(((s_ * (W / 2 + bt + 0.0085), y, z), (s_, 0, 0)))
    for y in (-D / 2 + 0.07, D / 2 - 0.07):
        plate(b, "Lid", f"LidRail_{y}", iron, rng, (0, y, Hb + lid_t + 0.0035), (1, 0, 0), (0, 0, 1), W - 0.02, 0.05, 0.007)
        for x in (-0.41, -0.255, 0.0, 0.255, 0.41):
            rivets["Lid"].append(((x * 0.98, y, Hb + lid_t + 0.0075), (0, 0, 1)))
    # Heavy angle irons up every corner.
    for sx in (-1, 1):
        for sy in (-1, 1):
            zc = (wall_lo - 0.02 + Hb) / 2
            plate(b, "body", f"Angle_{sx}_{sy}_x", iron, rng, (sx * (W / 2 + bt + 0.0045), sy * (D / 2 - 0.03), zc), (0, 0, 1), (sx, 0, 0), Hb - wall_lo + 0.03, 0.075, 0.008)
            plate(b, "body", f"Angle_{sx}_{sy}_y", iron, rng, (sx * (W / 2 - 0.03), sy * (D / 2 + bt + 0.0045), zc), (0, 0, 1), (0, sy, 0), Hb - wall_lo + 0.03, 0.075, 0.008)
            for z in (wall_lo + 0.06, zs[1], Hb - 0.07):
                rivets["body"].append(((sx * (W / 2 + bt + 0.0085), sy * (D / 2 - 0.035), z), (sx, 0, 0)))
                rivets["body"].append(((sx * (W / 2 - 0.035), sy * (D / 2 + bt + 0.0085), z), (0, sy, 0)))
            # Iron-shod feet.
            fb = pp.board_mesh(foot + 0.01, 0.09, 0.09, rng, seg=0.05, bevel=0.008, wear=0.3, chips=0, bow=0, rough_ends=0.001)
            pp.place(fb, pp.frame((sx * (W / 2 - 0.035), sy * (D / 2 - 0.035), (foot + 0.01) / 2), (1, 0, 0), (0, 1, 0), (0, 0, 1)))
            pp.hammered(fb, rng, 0.001, 40)
            b.add("body", pp.to_object(f"Foot_{sx}_{sy}", fb, iron, wood=False, sharp_angle=50))
            for zf in ((foot + 0.01) / 2,):
                rivets["body"].append(((sx * (W / 2 - 0.035), sy * (D / 2 + 0.01), zf), (0, sy, 0)))
            # The lid's corners.
            for face in ("x", "y"):
                if face == "x":
                    plate(b, "Lid", f"LidCorner_{sx}_{sy}_x", iron, rng, (sx * (W / 2 + 0.007 + bt + 0.004), sy * (D / 2 - 0.02), Hb + lid_t / 2), (0, 0, 1), (sx, 0, 0), lid_t + 0.008, 0.07, 0.007)
                else:
                    plate(b, "Lid", f"LidCorner_{sx}_{sy}_y", iron, rng, (sx * (W / 2 - 0.02), sy * (D / 2 + 0.007 + bt + 0.004), Hb + lid_t / 2), (0, 0, 1), (0, sy, 0), lid_t + 0.008, 0.07, 0.007)
    # Rivets where crossings were not already counted: along each band between uprights.
    for z in zs:
        for x in (-0.255, 0.0, 0.255):
            for s_ in (-1, 1):
                rivets["body"].append(((x + rng.uniform(-0.02, 0.02), s_ * (D / 2 + bt + 0.0005), z), (0, s_, 0)))
        for s_ in (-1, 1):
            rivets["body"].append(((s_ * (W / 2 + bt + 0.0005), 0.0, z), (s_, 0, 0)))

    # The lock plate, the keyhole, the staple; the hasp is the Lock.
    pz = Hb - 0.13
    front = -D / 2 - bt
    plate(b, "body", "LockPlate", iron, rng, (0, front - 0.006, pz), (0, 0, 1), (0, -1, 0), 0.23, 0.24, 0.01, bevel=0.004)
    plate(b, "body", "LockBoss", iron, rng, (0, front - 0.0145, pz - 0.025), (0, 0, 1), (0, -1, 0), 0.12, 0.13, 0.007, bevel=0.003)
    b.add("body", keyhole("Keyhole", M("Prop_Void"), (0, front - 0.0182, pz - 0.035), (0, -1, 0), 0.04))
    for sx in (-1, 1):
        for sz in (-1, 1):
            rivets["body"].append(((sx * 0.095, front - 0.012, pz + sz * 0.09), (0, -1, 0)))
    b.add("body", pp.torus("Staple", iron, (0, front - 0.024, pz + 0.07), 0.018, 0.006, normal=(1, 0, 0), segs=14, minor_segs=6))
    hz = Hb + lid_t * 0.55
    b.add("Lock", pp.lathe("HaspPin", iron, [(0.0, -0.045), (0.009, -0.045), (0.009, 0.045), (0.0, 0.045)], 10,
                           (0, -D / 2 - 0.007 - bt - 0.008, hz), Matrix.Rotation(math.radians(90), 3, "Y")))
    plate(b, "Lock", "Hasp", iron, rng, (0, front - 0.022, hz - 0.08), (0, 0, -1), (0, -1, 0), 0.16, 0.085, 0.01, bevel=0.003, taper=0.2)
    b.add("Lock", pp.torus("HaspRing", iron, (0, front - 0.034, pz + 0.055), 0.014, 0.005, normal=(1, 0, 0), segs=12, minor_segs=6))

    # Drop rings at the ends for the bearers.
    for s_ in (-1, 1):
        x = s_ * (W / 2 + bt)
        for y in (-0.10, 0.10):
            plate(b, "body", f"RingPlate_{s_}_{y}", iron, rng, (x + s_ * 0.007, y, Hb - 0.10), (0, 0, 1), (s_, 0, 0), 0.07, 0.06, 0.007)
            b.add("body", pp.torus(f"RingEye_{s_}_{y}", iron, (x + s_ * 0.018, y, Hb - 0.10), 0.011, 0.005, normal=(0, 1, 0), segs=12, minor_segs=6))
            ring = pp.torus(f"DropRing_{s_}_{y}", iron, (x + s_ * 0.026, y, Hb - 0.10 - 0.05), 0.05, 0.0085, normal=(0, 1, 0), segs=20, minor_segs=7)
            ring.rotation_euler = (0, s_ * math.radians(-12), 0)
            b.add("body", ring)
            rivets["body"].append(((x + s_ * 0.0115, y, Hb - 0.10 + 0.024), (s_, 0, 0)))
            rivets["body"].append(((x + s_ * 0.0115, y, Hb - 0.10 - 0.024), (s_, 0, 0)))

    # Hinges: three heavy straps up the back and over the lid.
    for x in (-0.28, 0.0, 0.28):
        plate(b, "body", f"HingeLeaf_{x}", iron, rng, (x, D / 2 + bt + 0.0035, Hb - 0.07), (0, 0, 1), (0, 1, 0), 0.14, 0.05, 0.007)
        plate(b, "Lid", f"HingeStrap_{x}", iron, rng, (x, D / 2 - 0.08, Hb + lid_t + 0.004), (0, 1, 0), (0, 0, 1), 0.20, 0.05, 0.007, taper=0.25)
        for k, dx in enumerate((-0.04, 0.0, 0.04)):
            node = "Lid" if k == 1 else "body"
            b.add(node, pp.lathe(f"Knuckle_{x}_{k}", iron, [(0.0, -0.019), (0.011, -0.019), (0.011, 0.019), (0.0, 0.019)], 10,
                                 (x + dx, D / 2 + 0.014, Hb), Matrix.Rotation(math.radians(90), 3, "Y")))
        rivets["body"].append(((x, D / 2 + bt + 0.007, Hb - 0.11), (0, 1, 0)))
        rivets["Lid"].append(((x, D / 2 - 0.13, Hb + lid_t + 0.0075), (0, 0, 1)))

    for node, spots in rivets.items():
        b.nails(node, f"Rivets_{node}", iron, rng, spots, 0.0105, reach=0.08, strength=0.7)

    # The hoard.
    floor = wall_lo
    b.add("Contents", pp.coin_pile("Hoard", M("Prop_Gold"), rng, (0.0, 0.0, floor), 0.36, 0.27, 300, 0.013, squash_y=0.72))
    gems = [("Prop_Gem_Red", (-0.15, -0.08)), ("Prop_Gem_Green", (0.12, 0.05)), ("Prop_Gem_Blue", (0.05, -0.12)), ("Prop_Gem_Red", (0.22, -0.04)), ("Prop_Gem_Green", (-0.25, 0.08))]
    import bmesh
    for i, (mname, (x, y)) in enumerate(gems):
        bm = bmesh.new()
        bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.022)
        bmesh.ops.scale(bm, vec=(1.0, 0.8, 0.65), verts=bm.verts)
        d = math.sqrt((x / 0.36) ** 2 + (y / (0.36 * 0.72)) ** 2)
        z = floor + 0.27 * max(0.0, 1 - d * d) ** 0.8 + 0.008
        pp.place(bm, Matrix.Translation((x, y, z)) @ Matrix.Rotation(rng.uniform(0, 3), 4, "Z") @ Matrix.Rotation(rng.uniform(-0.4, 0.4), 4, "X"))
        b.add("Contents", pp.to_object(f"Gem_{i}", bm, M(mname), wood=False, sharp_angle=10))
    for i, (x, y, tilt) in enumerate(((0.26, 0.12, 0.5), (-0.30, -0.10, -0.7))):
        d = math.sqrt((x / 0.36) ** 2 + (y / (0.36 * 0.72)) ** 2)
        z = floor + 0.27 * max(0.0, 1 - d * d) ** 0.8 - 0.02
        b.add("Contents", pp.lathe(f"Goblet_{i}", M("Prop_Gold"), [(0.0, 0.0), (0.035, 0.0), (0.036, 0.008), (0.009, 0.018), (0.008, 0.07), (0.035, 0.085), (0.046, 0.14), (0.042, 0.142), (0.03, 0.09), (0.0, 0.085)], 14,
                                   (x, y, z), Matrix.Rotation(tilt, 3, "Y") @ Matrix.Rotation(rng.uniform(-0.3, 0.3), 3, "X")))

    b.pivots["Lid"] = (0.0, D / 2 + 0.014, Hb)
    b.extras["Lid"] = {"hinge": "x", "open": -100}
    b.glint = (0.0, 0.0, Hb + lid_t + 0.07)
    return b


# --- the barrel ----------------------------------------------------------------------------

def barrel(rng):
    """A coopered barrel: staves bulging to the middle, three iron hoops, the chime standing
    proud of a head that lifts off. Water inside, and a dipper floating on it."""
    import bmesh
    b = Build("barrel")
    H, Rb, Re, t = 0.72, 0.255, 0.212, 0.022
    wood = M("Prop_Oak")
    iron = M("Prop_Iron_Rusty")

    def radius(z):
        u = z / H * 2 - 1
        return Re + (Rb - Re) * (1 - u * u)

    def slope(z):
        u = z / H * 2 - 1
        return -(Rb - Re) * 2 * u * (2 / H)

    # Staves: unequal widths, as staves are.
    n = 17
    widths = [1.0 + rng.uniform(-0.25, 0.25) for _ in range(n)]
    total = sum(widths)
    a = rng.uniform(0, math.tau)
    levels = 12
    for i, w in enumerate(widths):
        span = w / total * math.tau
        a0, a1 = a + 0.0035, a + span - 0.0035
        a += span
        amid = (a0 + a1) / 2
        sections = []
        for k in range(levels + 1):
            z = H * k / levels
            r = radius(z) + rng.uniform(-0.0006, 0.0006)
            c = 0.004
            dc = c / r
            ring = []
            for j in range(4):
                aa = a0 + dc + (a1 - a0 - 2 * dc) * j / 3
                ring.append((r * math.cos(aa), r * math.sin(aa), z))
            ring.append(((r - c) * math.cos(a1), (r - c) * math.sin(a1), z))
            ring.append(((r - t) * math.cos(a1), (r - t) * math.sin(a1), z))
            ring.append(((r - t) * math.cos(a0), (r - t) * math.sin(a0), z))
            ring.append(((r - c) * math.cos(a0), (r - c) * math.sin(a0), z))
            sections.append(ring)
        bm = pp.loft_mesh(sections)
        # The ends of the staves: worn uneven, some a little lower than others.
        drop = rng.uniform(0, 0.006)
        for v in bm.verts:
            if v.co.z > H - 1e-4:
                v.co.z -= drop + rng.uniform(0, 0.002)
        g = bm.verts.layers.float_vector.new("grain")
        tl = bm.verts.layers.float.new("board")
        tone = rng.random()
        px, py, pz = rng.uniform(-0.05, 0.05), rng.uniform(0.08, 0.25), rng.uniform(-5, 5)
        for v in bm.verts:
            ang = math.atan2(v.co.y, v.co.x)
            da = (ang - amid + math.pi) % math.tau - math.pi
            rr = math.hypot(v.co.x, v.co.y)
            v[g] = (da * Rb - px, (rr - radius(v.co.z)) + py, v.co.z + pz)
            v[tl] = tone
        b.add("body", pp.to_object(f"Stave_{i}", bm, wood))

    # The heads: the bottom one fixed inside the chime, the top one loose.
    def head(name, z, node, thick=0.022, boards=3):
        rr = radius(z) - t - 0.001
        parts = []
        for j, (at, w) in enumerate(pp.split(2 * rr, boards, 0.003, rng, 0.2)):
            y0 = -rr + at - w / 2
            y1 = y0 + w
            pts = []
            for k in range(9):
                y = y0 + (y1 - y0) * k / 8
                pts.append((math.sqrt(max(rr * rr - y * y, 1e-6)), y))
            for k in range(9):
                y = y1 - (y1 - y0) * k / 8
                pts.append((-math.sqrt(max(rr * rr - y * y, 1e-6)), y))
            bm = bmesh.new()
            verts = [bm.verts.new((x, y, z - thick / 2)) for (x, y) in pts]
            face = bm.faces.new(verts)
            ext = bmesh.ops.extrude_face_region(bm, geom=[face])
            bmesh.ops.translate(bm, vec=(0, 0, thick), verts=[v for v in ext["geom"] if isinstance(v, bmesh.types.BMVert)])
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
            pp._bevel_sharp(bm, 0.003, 2)
            g = bm.verts.layers.float_vector.new("grain")
            tl = bm.verts.layers.float.new("board")
            tone = rng.random()
            px, py = rng.uniform(-0.1, 0.1), rng.uniform(0.1, 0.3)
            for v in bm.verts:
                v[g] = (v.co.y - px, v.co.z - z + py, v.co.x)
                v[tl] = tone
            parts.append(pp.to_object(f"{name}_{j}", bm, wood))
        b.add(node, *parts)
        return rr

    head("Bottom", 0.045, "body")
    lid_z = H - 0.034
    rr = head("Head", lid_z, "Lid")
    batten = pp.slab("LidBatten", wood, rng, (0, 0, lid_z + 0.011 + 0.0115), (0, 1, 0), (0, 0, 1), rr * 1.5, 0.05, 0.023, wear=0.8, chips=1, bevel=0.005)
    b.add("Lid", batten)
    b.nails("Lid", "LidNails", iron, rng, [((rng.uniform(-0.01, 0.01), y, lid_z + 0.035), (0, 0, 1)) for y in (-0.12, -0.04, 0.04, 0.12)], 0.006, reach=0.04)
    b.pivots["Lid"] = (0.0, 0.0, lid_z + 0.012)
    b.extras["Lid"] = {"hinge": "none", "open": "lift"}

    # Hoops: flat iron bands following the bulge, each riveted where it laps.
    for k, (zc, hw) in enumerate(((0.075, 0.042), (H * 0.62, 0.036), (H - 0.07, 0.042))):
        path, nrm = [], []
        steps = 40
        for j in range(steps):
            ang = j * math.tau / steps
            r = radius(zc) + 0.0005
            path.append((r * math.cos(ang), r * math.sin(ang), zc))
            nv = Vector((math.cos(ang), math.sin(ang), -slope(zc))).normalized()
            nrm.append(nv)
        bm = pp.sweep(path, pp.band_profile(hw, 0.005), nrm, closed_path=True)
        pp.hammered(bm, rng, 0.0006, 40)
        b.add("body", pp.to_object(f"Hoop_{k}", bm, iron, sharp_angle=50))
        ang = rng.uniform(0, math.tau)
        r = radius(zc) + 0.005
        b.nails("body", f"HoopRivets_{k}", iron, rng, [((r * math.cos(ang + d), r * math.sin(ang + d), zc + dz), (math.cos(ang + d), math.sin(ang + d), 0)) for d in (-0.03, 0.03) for dz in (-hw * 0.25, hw * 0.25)], 0.0055, reach=0.05)
        # Rust runs from the hoop's lower edge all round.
        for j in range(28):
            aa = j * math.tau / 28 + rng.uniform(-0.05, 0.05)
            rr_ = radius(zc - hw / 2) + 0.002
            if rng.random() < 0.7:
                b.rust.append(((rr_ * math.cos(aa), rr_ * math.sin(aa), zc - hw / 2), rng.uniform(0.05, 0.16), rng.uniform(0.3, 0.7)))

    # Water, and a dipper on it.
    wz = H - 0.13
    wr = radius(wz) - t - 0.001
    bm = bmesh.new()
    bmesh.ops.create_circle(bm, cap_ends=True, segments=24, radius=wr)
    pp.place(bm, Matrix.Translation((0, 0, wz)))
    b.add("Contents", pp.to_object("Water", bm, M("Prop_Water"), wood=False, sharp_angle=0))
    dip = pp.lathe("DipperBowl", M("Prop_Oak_Clean"), [(0.0, -0.025), (0.03, -0.022), (0.045, -0.01), (0.05, 0.012), (0.046, 0.013), (0.04, -0.004), (0.0, -0.012)], 12, (0.05, -0.04, wz + 0.004))
    b.add("Contents", dip)
    handle = pp.lathe("DipperHandle", M("Prop_Oak_Clean"), [(0.0, 0.0), (0.008, 0.0), (0.007, 0.21), (0.0, 0.21)], 6, (0.05 + 0.045, -0.04, wz + 0.01),
                      Matrix.Rotation(math.radians(70), 3, "Y") @ Matrix.Rotation(math.radians(-25), 3, "X"))
    b.add("Contents", handle)
    b.glint = (0.0, 0.0, H + 0.06)
    return b


# --- sacks -----------------------------------------------------------------------------------

def _resample(profile, rows):
    """A profile [(r, z)] resampled to `rows` points evenly along its length."""
    import bisect
    lengths = [0.0]
    for (r0, z0), (r1, z1) in zip(profile, profile[1:]):
        lengths.append(lengths[-1] + math.hypot(r1 - r0, z1 - z0))
    out = []
    for i in range(rows):
        d = lengths[-1] * i / (rows - 1)
        k = min(max(bisect.bisect_right(lengths, d) - 1, 0), len(profile) - 2)
        t = (d - lengths[k]) / max(lengths[k + 1] - lengths[k], 1e-9)
        (r0, z0), (r1, z1) = profile[k], profile[k + 1]
        out.append((r0 + (r1 - r0) * t, z0 + (z1 - z0) * t))
    out[0] = profile[0]
    out[-1] = profile[-1]
    return out


def _neck_rope(b, rng, sack, z_lo, z_hi, rope_r=0.0065, at=None):
    """The drawstring: two turns round the gathered neck, a knot, two loose ends."""
    # The cloth just under the tie: the cord is pulled tight, so whatever cloth stands out past
    # it at its height is drawn in under it.
    pts = [sack.matrix_world @ v.co for v in sack.data.vertices if z_lo <= v.co.z <= z_hi and math.hypot(v.co.x, v.co.y) > 1e-4]
    c = sum(pts, Vector()) / len(pts)
    radii = sorted(math.hypot(p.x - c.x, p.y - c.y) for p in pts)
    rad = radii[int(len(radii) * 0.35)]
    c.z = at if at is not None else c.z
    for v in sack.data.vertices:
        dz = abs(v.co.z - c.z)
        if dz > 0.022:
            continue
        d = Vector((v.co.x - c.x, v.co.y - c.y, 0))
        limit = rad - rope_r * 0.6 + (dz / 0.022) ** 2 * 0.02
        if d.length > limit:
            d = d.normalized() * limit
            v.co.x, v.co.y = c.x + d.x, c.y + d.y
    sack.data.update()
    path = []
    for k in range(2 * 24 + 1):
        a = k / 24 * math.tau
        rr = rad + rope_r * 0.9 + 0.002 * math.sin(a * 3)
        path.append((c.x + rr * math.cos(a), c.y + rr * math.sin(a), c.z - 0.004 + 0.009 * k / 48))
    b.add("body", pp.rope("Drawstring", M("Prop_Rope"), path, rope_r, rng, segs_per_unit=160))
    knot_at = Vector((c.x + rad + rope_r * 2, c.y - 0.01, c.z + 0.002))
    b.add("body", pp.heap("Knot", M("Prop_Rope"), knot_at, (0.014, 0.012, 0.012), rng, res=6, bumps=0.4, freq=50))
    for e, (dx, dy) in enumerate(((0.02, -0.03), (0.035, 0.01))):
        end = knot_at + Vector((dx, dy, -0.10 - 0.03 * e))
        mid = knot_at.lerp(end, 0.5) + Vector((0.015, 0, 0.01))
        b.add("body", pp.rope(f"End_{e}", M("Prop_Rope"), [knot_at, mid, end], rope_r * 0.9, rng, segs_per_unit=160))
    return c, rad


def sack(rng, open_=False):
    """A full burlap sack: slumped, leaning, bulging at the foot, gathered into deep pleats
    under its drawstring with the tuft above flopped over. The open one has its mouth rolled
    back and coin spilling over the rim and across the floor."""
    import props_cloth as pc
    import bmesh
    b = Build("sack-open" if open_ else "sack")
    if not open_:
        profile = [(0.0, 0.0), (0.12, 0.002), (0.175, 0.02), (0.195, 0.06), (0.19, 0.13), (0.175, 0.21), (0.15, 0.28),
                   (0.11, 0.335), (0.07, 0.37), (0.045, 0.39), (0.05, 0.405), (0.08, 0.44), (0.10, 0.475), (0.095, 0.50),
                   (0.06, 0.505), (0.0, 0.50)]
        profile = _resample(profile, 44)
        bag = pc.sculpt_sack("Sack", rng, profile, 56, neck=0.39, tie_spread=0.20, lean=(-0.03, 0.03),
                             flop=(1.05, math.radians(160)), pleats=9, lumps=0.02, creases=0.018)
        bag.data.materials.append(M("Prop_Burlap"))
        for poly in bag.data.polygons:
            poly.use_smooth = True
        _flag(bag, False)
        b.add("body", bag)
        _neck_rope(b, rng, bag, 0.365, 0.385, at=0.385)
        top = max(v.co.z for v in bag.data.vertices)
        b.glint = (0.0, 0.0, top + 0.06)
        return b

    # Open: the mouth rolled back on itself, coin heaped in it and spilling down the front.
    profile = [(0.0, 0.0), (0.12, 0.002), (0.18, 0.02), (0.20, 0.06), (0.198, 0.14), (0.188, 0.22), (0.178, 0.28),
               (0.172, 0.31), (0.176, 0.33), (0.19, 0.342), (0.207, 0.338), (0.214, 0.322), (0.21, 0.30), (0.20, 0.29)]
    profile = _resample(profile, 40)
    bag = pc.sculpt_sack("Sack", rng, profile, 56, neck=None, lean=(0.0, 0.0), pleats=9, rim=0.30, creases=0.02, lumps=0.016)
    # bag_mesh closes both ends: open the mouth by taking away the top's fan.
    bm = bmesh.new()
    bm.from_mesh(bag.data)
    tip = [v for v in bm.verts if math.hypot(v.co.x, v.co.y) < 1e-4 and v.co.z > 0.2]
    bmesh.ops.delete(bm, geom=tip, context="VERTS")
    bm.to_mesh(bag.data)
    bm.free()
    mod = bag.modifiers.new("Thick", "SOLIDIFY")
    mod.thickness = 0.005
    mod.offset = -1.0
    bag.data.materials.append(M("Prop_Burlap"))
    for poly in bag.data.polygons:
        poly.use_smooth = True
    _flag(bag, False)
    b.add("body", bag)

    mz = 0.31
    heap = pp.coin_pile("Coins", M("Prop_Gold"), rng, (0.0, 0.0, mz - 0.02), 0.165, 0.07, 230, 0.012)
    b.add("Contents", heap)
    # Down the front: coins lying on the sack's own surface, found by casting at it.
    from mathutils.bvhtree import BVHTree
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    tree = BVHTree.FromObject(bag, dg)
    spill = bmesh.new()
    for i in range(80):
        ang = math.radians(-90 + rng.uniform(-28, 28) * (1.2 - 0.6 * rng.random()))
        z = 0.34 * (1 - rng.random() ** 1.6)
        origin = Vector((0.6 * math.cos(ang), 0.6 * math.sin(ang), z))
        hit, nrm, _, _ = tree.ray_cast(origin, Vector((-math.cos(ang), -math.sin(ang), 0)))
        if hit is None:
            continue
        nrm = (nrm + Vector((rng.uniform(-0.3, 0.3), rng.uniform(-0.3, 0.3), 0.5))).normalized()
        pp.coin(spill, hit + nrm * 0.003, nrm, 0.012 * rng.uniform(0.9, 1.08), 0.0022, rng)
    for i in range(120):
        t = rng.random() ** 0.8
        ang = math.radians(-90 + rng.uniform(-30, 30) * (0.5 + t))
        d = 0.215 + 0.30 * t
        p = Vector((d * math.cos(ang), d * math.sin(ang), 0.0013))
        nrm = Vector((rng.uniform(-0.15, 0.15), rng.uniform(-0.15, 0.15), 1)).normalized()
        if rng.random() < 0.08:
            nrm = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), 0.5)).normalized()
            p.z = 0.009
        pp.coin(spill, p, nrm, 0.012 * rng.uniform(0.9, 1.08), 0.0022, rng)
    bmesh.ops.recalc_face_normals(spill, faces=spill.faces)
    b.add("Contents", pp.to_object("Spill", spill, M("Prop_Gold"), wood=False, sharp_angle=50))
    b.add("Contents", pp.coin_pile("Drift", M("Prop_Gold"), rng, (0.0, -0.235, 0.0), 0.075, 0.03, 45, 0.012))
    b.glint = (0.0, 0.0, 0.42)
    return b


def _flag(obj, wood):
    a = obj.data.attributes.get("is_wood") or obj.data.attributes.new("is_wood", "FLOAT", "FACE")
    a.data.foreach_set("value", [1.0 if wood else 0.0] * len(obj.data.polygons))


# --- the cart ----------------------------------------------------------------------------------

def wheel(b, node, rng, centre, R=0.25, spokes=10, hub_len=0.13, prefix="Wheel", side=1):
    """A cartwheel round X at `centre`: a turned hub, tapered spokes dished outward, five
    felloes, an iron tyre nailed on. Returns its parts."""
    wood = M("Prop_Cart_Wood")
    iron = M("Prop_Iron_Rusty")
    parts = []
    c = Vector(centre)
    hub = pp.lathe(f"{prefix}_Hub", wood, [(0.0, -hub_len / 2), (0.028, -hub_len / 2), (0.040, -hub_len * 0.3), (0.050, 0.0),
                                           (0.044, hub_len * 0.3), (0.030, hub_len / 2), (0.0, hub_len / 2)], 14,
                   c, Matrix.Rotation(math.radians(90), 3, "Y"), jitter=0.02, rng=rng)
    parts.append(hub)
    for e in (-1, 1):
        parts.append(pp.torus(f"{prefix}_Band_{e}", iron, c + Vector((e * hub_len * 0.36, 0, 0)), 0.043, 0.006, normal=(1, 0, 0), segs=16, minor_segs=5))
    felloe_depth = 0.045
    dish = 0.022 * side
    for i in range(spokes):
        a = i * math.tau / spokes + rng.uniform(-0.02, 0.02)
        d = Vector((0, math.cos(a), math.sin(a)))
        p0 = c + d * 0.045
        p1 = c + d * (R - felloe_depth + 0.004) + Vector((dish, 0, 0))
        length = (p1 - p0).length
        part = pp.slab(f"{prefix}_Spoke_{i}", wood, rng, (p0 + p1) / 2, p1 - p0, (1, 0, 0), length, 0.024, 0.018,
                       seg=0.06, bevel=0.004, wear=0.8, chips=1, taper=0.25)
        parts.append(part)
    a0 = rng.uniform(0, 1)
    for i in range(5):
        aa, ab = a0 + i * math.tau / 5 + 0.006, a0 + (i + 1) * math.tau / 5 - 0.006
        parts.append(pp.arc_board(f"{prefix}_Felloe_{i}", wood, rng, R - 0.006, aa, ab, felloe_depth, 0.05, centre=c + Vector((dish, 0, 0))))
    path, nrm = [], []
    for k in range(48):
        a = k * math.tau / 48
        path.append(c + Vector((dish, math.cos(a), math.sin(a))) * 1.0 + Vector((0, (R - 0.006) * math.cos(a) - math.cos(a), (R - 0.006) * math.sin(a) - math.sin(a))))
        nrm.append(Vector((0, math.cos(a), math.sin(a))))
    bm = pp.sweep(path, pp.band_profile(0.052, 0.006), nrm, closed_path=True)
    pp.hammered(bm, rng, 0.0008, 40)
    parts.append(pp.to_object(f"{prefix}_Tyre", bm, iron, sharp_angle=50))
    spots = []
    for k in range(10):
        a = k * math.tau / 10 + 0.3
        dd = Vector((0, math.cos(a), math.sin(a)))
        spots.append((c + Vector((dish, 0, 0)) + dd * (R + 0.0), dd))
    tyre_nails, heads = pp.nails(f"{prefix}_TyreNails", iron, rng, spots, 0.007)
    parts.append(tyre_nails)
    b.add(node, *parts)
    return parts


def cart_parts(b, rng, with_load=True):
    """Tobin's handcart: a plank bed with side boards on stakes, two shafts under it running
    forward into handles, an axle and two spoked wheels, a stand under the shafts. Returns the
    parts by kind so the overturned one can take a wheel off."""
    wood = M("Prop_Cart_Wood")
    iron = M("Prop_Iron_Rusty")
    BW, BL = 0.54, 0.62           # bed across x, along y
    fz = 0.31                     # bed floor underside
    t = 0.02
    cy = 0.04                     # bed centre along y (behind the axle a little)
    ax_y, ax_z = 0.03, 0.25
    out = {"body": [], "wheels": []}

    def add(part):
        out["body"].append(part)
        b.add("body", part)
        return part

    def wb(name, centre, along, out_, length, width, thick, **kw):
        kw.setdefault("wear", 1.2)
        kw.setdefault("chips", 2)
        return add(pp.slab(name, wood, rng, centre, along, out_, length, width, thick, **kw))

    for i, (at, w) in enumerate(pp.split(BW, 4, 0.004, rng, 0.15)):
        wb(f"Floor_{i}", (-BW / 2 + at, cy, fz + t / 2), (0, 1, 0), (0, 0, 1), BL, w, t)
    side_h = 0.15
    for sx in (-1, 1):
        for i, (at, w) in enumerate(pp.split(side_h, 2, 0.008, rng, 0.1)):
            wb(f"Side_{sx}_{i}", (sx * (BW / 2 + t / 2), cy, fz + t + at), (0, 1, 0), (sx, 0, 0), BL + 0.02, w, t)
    for sy, label in ((-1, "Front"), (1, "Tail")):
        for i, (at, w) in enumerate(pp.split(side_h * 0.9, 2, 0.008, rng, 0.1)):
            wb(f"{label}_{i}", (0, cy + sy * (BL / 2 - t / 2), fz + t + at), (1, 0, 0), (0, sy, 0), BW, w, t)
    nail_spots = []
    for sx in (-1, 1):
        for yy in (-BL / 2 + 0.03, 0.0, BL / 2 - 0.03):
            x = sx * (BW / 2 + t + 0.016)
            wb(f"Stake_{sx}_{yy:.2f}", (x, cy + yy, fz + 0.09), (0, 0, 1), (sx, 0, 0), 0.25, 0.034, 0.03, chips=1, seg=0.08)
            for zz in (fz + t + 0.04, fz + t + 0.11):
                nail_spots.append(((x + sx * 0.015, cy + yy, zz), (sx, 0, 0)))
    # Shafts: under the bed, forward into handles.
    for sx in (-1, 1):
        x = sx * 0.20
        wb(f"Shaft_{sx}", (x, cy - 0.17, fz - 0.024), (0, 1, 0), (0, 0, -1), BL + 0.38, 0.05, 0.045, chips=2, seg=0.12, bevel=0.008)
    wb("Crossbar", (0, -0.42, fz - 0.024), (1, 0, 0), (0, -1, 0), 0.47, 0.04, 0.035, bevel=0.007)
    # Axle: a squared beam with iron caps; the wheels turn on its arms.
    wb("Axle", (0, ax_y, ax_z + 0.012), (1, 0, 0), (0, 0, -1), 0.60, 0.06, 0.05, chips=1, bevel=0.008)
    for sx in (-1, 1):
        add(pp.lathe(f"Arm_{sx}", iron, [(0.0, 0.0), (0.016, 0.0), (0.016, 0.12), (0.0, 0.12)], 8, (sx * 0.28, ax_y, ax_z),
                     Matrix.Rotation(math.radians(90 * sx), 3, "Y")))
        add(pp.lathe(f"Linchpin_{sx}", iron, [(0.0, 0.0), (0.004, 0.0), (0.004, 0.05), (0.008, 0.05), (0.008, 0.058), (0.0, 0.058)], 6,
                     (sx * 0.405, ax_y, ax_z - 0.03), None))
        # Blocks between axle and bed.
        wb(f"Bolster_{sx}", (sx * 0.20, ax_y, ax_z + 0.045), (0, 1, 0), (0, 0, 1), 0.10, 0.05, 0.03, chips=1)
        # The stand that keeps her level with the handles down.
        wb(f"Stand_{sx}", (sx * 0.20, -0.33, (fz - 0.045) / 2), (0, 0, 1), (0, -1, 0), fz - 0.045, 0.034, 0.03, chips=1, seg=0.1)
    b.nails("body", "CartNails", iron, rng, nail_spots, 0.0065, reach=0.08)
    out["body"].append(b.nodes["body"][-1])
    for sx in (-1, 1):
        out["wheels"].append(wheel(b, "body", rng, (sx * 0.335, ax_y, ax_z), side=sx, prefix=f"Wheel_{sx}"))
        out["body"].extend(out["wheels"][-1])
    # In the bed: straw, glass from the draughts, a dark sticky stain.
    bm = pp.bmesh.new()
    pp.straw_strands(bm, rng, (0, cy, fz + t + 0.003), (BW / 2 - 0.04, BL / 2 - 0.04), 90, (0.05, 0.10), 0.006, 0.1, 0.05)
    straw = pp.to_object("BedStraw", bm, M("Prop_Straw"), sharp_angle=0)
    add(straw)
    bm = pp.bmesh.new()
    for i in range(14):
        p = Vector((rng.uniform(-0.2, 0.2), cy + rng.uniform(-0.25, 0.25), fz + t + 0.002))
        s_ = rng.uniform(0.008, 0.02)
        a = rng.uniform(0, math.tau)
        v = [bm.verts.new(p + Vector((math.cos(a + k * 2.1 + rng.uniform(-0.4, 0.4)) * s_, math.sin(a + k * 2.1) * s_, rng.uniform(0, 0.006)))) for k in range(3)]
        bm.faces.new(v)
    shards = pp.to_object("Shards", bm, M("Prop_Glass"), wood=False, sharp_angle=0)
    add(shards)
    if with_load:
        # The greatsword in its sacking, lashed under the bed between the shafts.
        sections = []
        for k in range(15):
            yy = -0.36 + 0.66 * k / 14
            rr = 0.042 + 0.006 * math.sin(k * 1.7) + (0.012 if k > 11 else 0.0)
            ring = []
            for j in range(10):
                a = j * math.tau / 10
                w = 1.0 + 0.12 * pp.noise.noise(Vector((math.cos(a) * 2, yy * 8, math.sin(a) * 2)))
                ring.append((rr * w * math.cos(a) * 1.25, yy, fz - 0.07 + rr * w * math.sin(a) * 0.8))
            sections.append(ring)
        bm = pp.loft_mesh(sections)
        bundle = pp.to_object("Bundle", bm, M("Prop_Burlap"), wood=False, sharp_angle=0)
        w = bundle.data.attributes.new("weave", "FLOAT_VECTOR", "CORNER")
        vals = []
        for poly in bundle.data.polygons:
            for li in poly.loop_indices:
                co = bundle.data.vertices[bundle.data.loops[li].vertex_index].co
                vals.extend((math.atan2(co.z - fz + 0.07, co.x) * 0.05, co.y, 0.0))
        w.data.foreach_set("vector", vals)
        b.add("Contents", bundle)
        for k, yy in enumerate((-0.22, 0.12)):
            path = []
            for j in range(13):
                a = j * math.tau / 12
                path.append((0.25 * math.cos(a) * 0.95, yy + 0.004 * math.sin(a * 2), fz - 0.06 + 0.055 * math.sin(a)))
            b.add("Contents", pp.rope(f"Lashing_{k}", M("Prop_Rope"), path, 0.0065, rng, segs_per_unit=110))
    if with_load:
        litter = pp.heap("BedLitter", M("Prop_Straw"), (0.12, cy + 0.15, fz + t), (0.09, 0.11, 0.012), rng, res=10, bumps=0.5, freq=18)
        add(litter)
        b.borrow.append((straw, litter, 6))
    else:
        b.borrow.append((straw, None, 6))
    return out


def shift(b, offset):
    """Move everything a builder made, and everything it remembered about where things are."""
    offset = Vector(offset)
    for parts in b.nodes.values():
        for p in parts:
            p.location += offset
    b.rust = [(tuple(Vector(p) + offset), r, k) for (p, r, k) in b.rust]
    b.pivots = {k: tuple(Vector(v) + offset) for k, v in b.pivots.items()}
    if b.glint:
        b.glint = tuple(Vector(b.glint) + offset)


def cart(rng):
    b = Build("cart")
    cart_parts(b, rng)
    b.glint = (0.0, 0.04, 0.55)
    # Built round its axle; the handles run forward, so the middle of what it covers is not
    # the axle. Put the origin in the middle of the footprint.
    shift(b, (0.0, 0.13, 0.0))
    return b


def cart_overturned(rng):
    """The wreck at the cave mouth: the cart on its side, one wheel off and lying in the grass,
    two crates thrown out of it, one burst, straw and bottles everywhere."""
    b = Build("cart-overturned")
    parts = cart_parts(b, rng, with_load=False)
    lost = parts["wheels"][1]                     # the right-hand wheel comes off
    keep = [p for p in b.nodes["body"] if p not in lost]
    # On its side with the open bed turned toward the camera, so what spilled out of it shows.
    tilt = Matrix.Rotation(math.radians(-38), 4, "Z") @ Matrix.Rotation(math.radians(98), 4, "Y")
    for p in keep:
        p.matrix_world = tilt @ p.matrix_world
    bpy.context.view_layer.update()
    lo = min((p.matrix_world @ v.co).z for p in keep for v in p.data.vertices)
    for p in keep:
        p.matrix_world = Matrix.Translation((-0.08, 0.10, -lo)) @ p.matrix_world
    # The lost wheel lies flat, propped on its hub.
    for p in lost:
        p.matrix_world = Matrix.Translation((-0.50, -0.30, 0.065)) @ Matrix.Rotation(math.radians(84), 4, "Y") @ Matrix.Rotation(math.radians(6), 4, "X") @ Matrix.Translation((-0.335, -0.03, -0.25)) @ p.matrix_world
    # Rust sources moved with what they were on: rebuild from the cart's own nails is not worth
    # it; the wreck keeps the rust of nothing but its crates.
    b.rust = []
    # Two crates thrown out on the open side.
    for k, (where, turn, roll) in enumerate((((-0.42, 0.42, 0.0), 0.4, 0.0), ((0.56, 0.36, 0.0), -0.9, 90.0))):
        sub = crate(rng, "b", lite=True)
        mats = Matrix.Translation(where) @ Matrix.Rotation(turn, 4, "Z") @ Matrix.Rotation(math.radians(roll), 4, "X") @ Matrix.Scale(0.62, 4)
        for node, ps_ in sub.nodes.items():
            for p in ps_:
                if node == "Contents":
                    bpy.data.objects.remove(p, do_unlink=True)
                    continue
                p.matrix_world = mats @ p.matrix_world
                if node == "Lid" and k == 1:
                    p.matrix_world = Matrix.Translation((0.30, 0.10, 0.0)) @ Matrix.Rotation(math.radians(-80), 4, "X") @ p.matrix_world
                b.add("body", p)
        bpy.context.view_layer.update()
        if roll:
            parts_k = [p for node, ps_ in sub.nodes.items() if node != "Contents" for p in ps_]
            lo = min((p.matrix_world @ v.co).z for p in parts_k for v in p.data.vertices)
            for p in parts_k:
                p.matrix_world = Matrix.Translation((0, 0, -lo)) @ p.matrix_world
        floor_straw = [t_ for (t_, s_, n_) in sub.borrow if n_ == 6]
    # Straw and bottles out of the burst crate: a drift of it, and stalks blown about.
    drift = pp.heap("SpiltDrift", M("Prop_Straw"), (0.30, -0.30, 0.0), (0.20, 0.16, 0.03), rng, res=14, bumps=0.35, freq=14)
    b.add("body", drift)
    bm = pp.bmesh.new()
    pp.straw_strands(bm, rng, (0.30, -0.28, 0.003), (0.30, 0.24), 160, (0.05, 0.12), 0.03, 0.1, 0.05)
    loose = pp.to_object("Spilt", bm, M("Prop_Straw"), sharp_angle=0)
    b.add("body", loose)
    b.borrow = [(loose, drift, 6), (b.borrow[0][0], drift, 6)] + [(t_, drift, 6) for t_ in floor_straw]
    for i in range(4):
        x, y = 0.30 + rng.uniform(-0.15, 0.2), -0.32 + rng.uniform(-0.12, 0.12)
        glass = i % 2 == 0
        prof = [(0.0, 0.0), (0.036, 0.002), (0.038, 0.07), (0.030, 0.095), (0.014, 0.115), (0.012, 0.15), (0.016, 0.155), (0.016, 0.165), (0.011, 0.168)]
        rot = Matrix.Rotation(rng.uniform(0, math.tau), 3, "Z") @ Matrix.Rotation(math.radians(90), 3, "X")
        b.add("Contents", pp.lathe(f"Bottle_{i}", M("Prop_Glass") if glass else M("Prop_Bone"), prof, 12, (x, y, 0.037), rot, sharp_angle=60))
    b.glint = (0.30, -0.30, 0.25)
    return b


# --- the weapon rack ---------------------------------------------------------------------------

WEAPONS = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "src", "Ironbound.Game", "art", "weapons")
# The weapons are modelled at the human models' size; a man is 4.3 of those units and 1.4 of
# the board's, so this is the scale Main.Model gives them in a hand.
WEAPON_SCALE = 1.4 / 4.3


def import_weapon(name, matrix):
    """One of the game's own weapon models, unchanged, placed by `matrix` (in the weapon's
    frame: grip at the origin, length up +Z). Its baked textures are read through its own UVs
    while the rack's bake writes them into the rack's atlas through new ones."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(WEAPONS, f"{name}.glb"))
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == "MESH"]
    for o in meshes:
        world = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = matrix @ Matrix.Scale(WEAPON_SCALE, 4) @ world
    for o in new:
        if o.type != "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    for o in meshes:
        mesh = o.data
        old = mesh.uv_layers[0]
        old.name = "Orig"
        fresh = mesh.uv_layers.new(name="UVMap")
        mesh.uv_layers.active = fresh
        fresh.active_render = True
        for slot in o.material_slots:
            m = slot.material
            if m is None or not m.node_tree:
                continue
            nt = m.node_tree
            for n in list(nt.nodes):
                if n.type == "TEX_IMAGE" and not n.inputs["Vector"].is_linked:
                    uvn = nt.nodes.new("ShaderNodeUVMap")
                    uvn.uv_map = "Orig"
                    nt.links.new(uvn.outputs[0], n.inputs["Vector"])
                elif n.type == "NORMAL_MAP":
                    n.uv_map = "Orig"
            m.name = f"Rack_{name}_{m.name}"
        _flag(o, False)
        o.name = f"Weapon_{name}"
    return meshes


def drop_orig_uvs(objects):
    for o in objects:
        layer = o.data.uv_layers.get("Orig")
        if layer is not None:
            o.data.uv_layers.remove(layer)


def weapon_rack(rng):
    """A standing rack: two posts on splayed feet, a top rail with pegs to hold the hafts and a
    bottom rail with cups for the butts. Three of the game's own weapons stand in it."""
    b = Build("weapon-rack")
    wood = M("Prop_Oak")
    iron = M("Prop_Iron")
    Wd, Hh = 0.80, 0.92
    nail_spots = []

    def wb(name, centre, along, out, length, width, thick, **kw):
        kw.setdefault("wear", 1.0)
        kw.setdefault("chips", 2)
        part = pp.slab(name, wood, rng, centre, along, out, length, width, thick, **kw)
        b.add("body", part)
        return part

    for sx in (-1, 1):
        x = sx * Wd / 2
        wb(f"Post_{sx}", (x, 0.02, Hh / 2), (0, 0, 1), (sx, 0, 0), Hh, 0.07, 0.065, bevel=0.009)
        wb(f"Foot_{sx}", (x, 0.0, 0.03), (0, 1, 0), (0, 0, 1), 0.38, 0.075, 0.06, bevel=0.009, chips=2)
        wb(f"Cap_{sx}", (x, 0.02, Hh + 0.012), (0, 1, 0), (0, 0, 1), 0.10, 0.09, 0.025, bevel=0.006, chips=1)
        for sy in (-1, 1):
            # Braces from foot to post.
            a = Vector((x, sy * 0.15, 0.05))
            c = Vector((x, sy * 0.03, 0.20))
            part = pp.plank_run(f"Brace_{sx}_{sy}", wood, rng, a, c, 0.035, 0.025, (sx, 0, 0), wear=1.0, chips=1, bevel=0.005)
            b.add("body", part)
            nail_spots.append(((x + sx * 0.014, sy * 0.13, 0.065), (sx, 0, 0)))
    top = wb("TopRail", (0, -0.01, Hh - 0.05), (1, 0, 0), (0, -1, 0), Wd + 0.12, 0.085, 0.045, bevel=0.008)
    low = wb("LowRail", (0, -0.07, 0.10), (1, 0, 0), (0, 0, 1), Wd - 0.05, 0.12, 0.03, bevel=0.006)
    wb("BackRail", (0, 0.05, 0.13), (1, 0, 0), (0, 1, 0), Wd - 0.05, 0.05, 0.03, bevel=0.006)
    for sx in (-1, 1):
        for zz in (Hh - 0.065, Hh - 0.035):
            nail_spots.append(((sx * Wd / 2, -0.027, zz), (0, -1, 0)))
    slots = [-0.27, -0.09, 0.09, 0.27]
    for i, x in enumerate(slots):
        # Pegs either side of each slot in the top rail; a cup in the bottom rail.
        for e in (-1, 1):
            b.add("body", pp.lathe(f"Peg_{i}_{e}", wood, [(0.0, 0.0), (0.011, 0.0), (0.010, 0.075), (0.008, 0.085), (0.0, 0.085)], 8,
                                   (x + e * 0.045, -0.03, Hh - 0.05), Matrix.Rotation(math.radians(78), 3, "X")))
        b.add("body", pp.torus(f"Cup_{i}", iron, (x, -0.085, 0.118), 0.026, 0.006, normal=(0, 0, 1), segs=14, minor_segs=5))
    b.nails("body", "RackNails", iron, rng, nail_spots, 0.006, reach=0.07)

    # The weapons: hafted ones butt-down in the cups, the sword point-down, all leaning back
    # against the pegs.
    def stand(name, x, bottom, lean=7.0, turn=0.0, flip=False):
        flipm = Matrix.Rotation(math.pi, 4, "X") if flip else Matrix.Identity(4)
        base = Matrix.Translation((0, 0, -bottom * WEAPON_SCALE))
        m = (Matrix.Translation((x, -0.085, 0.105)) @ Matrix.Rotation(math.radians(-lean), 4, "X")
             @ Matrix.Rotation(math.radians(turn), 4, "Z") @ base @ flipm)
        return import_weapon(name, m)

    weapons = []
    # Leaning just enough to rest against the front of the top rail, between its pegs.
    weapons += stand("shortspear", slots[0], -1.14, 1.8, 80)
    weapons += stand("greataxe", slots[1], -0.62, 1.8, 0)
    weapons += stand("longsword", slots[3], -2.0, 1.5, 10, flip=True)
    b.add("Contents", *weapons)
    b.unbaked_uvs = weapons
    b.glint = (0.0, 0.0, Hh + 0.10)
    return b


# --- the pile ----------------------------------------------------------------------------------

def pile(rng):
    """Things dropped on the ground: a bundle knotted up in a cloth, a strap round it, a sword's
    hilt poking out of the knot, all lying on the corner of a second cloth. Taking the bundle
    leaves the cloth."""
    import props_cloth as pc
    import bmesh
    b = Build("pile")
    # The cloth underneath, rumpled.
    bm = bmesh.new()
    nx, ny = 26, 22
    sx, sy = 0.62, 0.52
    seed = rng.uniform(0, 50)
    grid = []
    for j in range(ny + 1):
        row = []
        for i in range(nx + 1):
            x = -sx / 2 + sx * i / nx
            y = -sy / 2 + sy * j / ny
            fold = pc._ridged(Vector((x * 1.3, y * 1.3, 0.2)), 3.0, seed) ** 5 * 0.035
            z = 0.002 + fold + 0.006 * pp.noise.noise(Vector((x * 9, y * 9, seed)))
            edge = max(abs(x) / (sx / 2), abs(y) / (sy / 2))
            z += 0.012 * max(0.0, edge - 0.85) / 0.15 * (0.5 + 0.5 * pp.noise.noise(Vector((x * 5, y * 5, seed + 1))))
            row.append(bm.verts.new((x + 0.04 * pp.noise.noise(Vector((y * 3, seed, 0))), y, max(0.002, z))))
        grid.append(row)
    for j in range(ny):
        for i in range(nx):
            bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
    rot = Matrix.Rotation(0.4, 4, "Z")
    pp.place(bm, rot)
    under = pp.to_object("Wrapper", bm, M("Prop_Cloth"), wood=False, sharp_angle=0)
    mod = under.modifiers.new("Thick", "SOLIDIFY")
    mod.thickness = 0.004
    mod.offset = -1.0
    b.add("body", under)

    # The bundle: a long roll of cloth with a sword in it, gathered and tied at one end, the
    # hilt out of the other, a strap round its middle.
    profile = [(0.0, 0.0), (0.05, 0.004), (0.08, 0.025), (0.092, 0.06), (0.095, 0.15), (0.09, 0.26), (0.08, 0.31),
               (0.055, 0.345), (0.032, 0.365), (0.036, 0.378), (0.055, 0.40), (0.062, 0.425), (0.045, 0.44), (0.0, 0.445)]
    profile = _resample(profile, 36)
    bundle = pc.sculpt_sack("Bundle", rng, profile, 40, neck=0.365, tie_spread=0.09, lean=(0.0, 0.0),
                            flop=(0.7, math.radians(90)), pleats=7, lumps=0.018, creases=0.012)
    lay = Matrix.Rotation(math.radians(90), 4, "Y")
    bundle.data.transform(lay)
    for v in bundle.data.vertices:
        v.co.z *= 0.72
        # It lies on the ground: what would be under the floor is flattened onto it.
    lo = min(v.co.z for v in bundle.data.vertices)
    for v in bundle.data.vertices:
        v.co.z -= lo
        v.co.x -= 0.22
        v.co.z = max(v.co.z, 0.0) + 0.006 * max(0.0, 0.02 - v.co.z) / 0.02
    bundle.data.update()
    bundle.data.materials.append(M("Prop_Cloth"))
    for poly in bundle.data.polygons:
        poly.use_smooth = True
    _flag(bundle, False)
    b.add("Contents", bundle)
    # The strap round its middle, fitted to the cloth there.
    mesh = bundle.data
    band = [v.co for v in mesh.vertices if abs(v.co.x - 0.0) < 0.02]
    cy = sum(v.y for v in band) / len(band)
    cz = sum(v.z for v in band) / len(band)
    path, nrm = [], []
    for k in range(36):
        a_ = k * math.tau / 36
        d = Vector((0, math.cos(a_), math.sin(a_)))
        best = max((p.y - cy) * d.y + (p.z - cz) * d.z for p in band if ((p.y - cy) * d.y + (p.z - cz) * d.z) > 0.8 * math.hypot(p.y - cy, p.z - cz))
        r = best + 0.002
        path.append(Vector((0.0, cy + d.y * r, max(cz + d.z * r, 0.004))))
        nrm.append(d)
    bm = pp.sweep(path, pp.band_profile(0.034, 0.005), nrm, closed_path=True)
    b.add("Contents", pp.to_object("Strap", bm, M("Prop_Leather"), wood=False, sharp_angle=60))
    top_pt = max(path, key=lambda p: p.z)
    b.add("Contents", pp.torus("Buckle", M("Prop_Iron"), top_pt + Vector((0.0, 0.0, 0.004)), 0.02, 0.0042, normal=(0, 0, 1), segs=4, minor_segs=4))
    # The hilt: out of the rounded end, rising a little; the crossguard just clear of the cloth.
    end_x = min(v.co.x for v in mesh.vertices)
    hilt_m = (Matrix.Translation((end_x + 0.02, cy, cz + 0.015)) @ Matrix.Rotation(math.radians(-58), 4, "Y")
              @ Matrix.Rotation(math.radians(12), 4, "X"))
    grip = pp.lathe("Grip", M("Prop_Leather"), [(0.0, 0.0), (0.014, 0.0), (0.015, 0.06), (0.014, 0.12), (0.0, 0.12)], 8, (0, 0, 0.014))
    guard = pp.slab("Guard", M("Prop_Iron"), rng, (0, 0, 0.004), (0, 1, 0), (0, 0, 1), 0.17, 0.022, 0.018, seg=0.05, bevel=0.003, wear=0.3, chips=0)
    pommel = pp.heap("Pommel", M("Prop_Iron"), (0, 0, 0.146), (0.024, 0.024, 0.02), rng, res=8, bumps=0.05, freq=10)
    for part in (grip, guard, pommel):
        part.matrix_world = hilt_m @ part.matrix_world
        _flag(part, False)
        b.add("Contents", part)
    b.glint = (0.0, 0.0, 0.26)
    return b


# --- the niche ---------------------------------------------------------------------------------

def rock_block(name, size, rng, res=0.03, amp=0.025, centre=(0, 0, 0), flat_back=False, strata=0.0, power=4.0):
    """A lump of rock: a box subdivided, rounded toward a superellipsoid so it has no corners,
    then pushed about by layered noise along its normals (and, with `strata`, by the beds of the
    rock). `flat_back` keeps the +Y face flat, to stand against a wall."""
    import bmesh
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    cuts = max(2, int(max(size) / res))
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=cuts, use_grid_fill=True)
    seed = rng.uniform(0, 100)
    half = Vector(size) / 2
    for v in bm.verts:
        q = v.co * 2.0                                   # on the unit cube
        n4 = (abs(q.x) ** power + abs(q.y) ** power + abs(q.z) ** power) ** (1.0 / power)
        q = q / max(n4, 1e-6)
        p = Vector((q.x * half.x, q.y * half.y, q.z * half.z))
        nrm = Vector((math.copysign(abs(q.x) ** (power - 1), q.x) / half.x, math.copysign(abs(q.y) ** (power - 1), q.y) / half.y,
                      math.copysign(abs(q.z) ** (power - 1), q.z) / half.z)).normalized()
        d = amp * (pp.noise.noise(p * 3 + Vector((seed, 0, 0))) * 0.8 + pp.noise.noise(p * 9 + Vector((0, seed, 0))) * 0.35
                   + 0.35 * abs(pp.noise.noise(p * 22 + Vector((0, 0, seed)))))

        if strata:
            d += strata * math.sin(p.z * 38 + 3 * pp.noise.noise(p * 4 + Vector((seed, seed, 0))))
        if flat_back and q.y > 0.8:
            d *= max(0.0, (1.0 - q.y) / 0.2)
        v.co = p + nrm * d
    pp.place(bm, Matrix.Translation(centre))
    return bm


def niche(rng):
    """Loose stones in a cave wall pulled out to show a cavity: the hidden cache. The face of
    the wall is a slab of rock; the `Lid` is the stone that stopped the hole, pivoting out on
    its bottom front edge; two more stones lie where they were dropped."""
    import bmesh
    b = Build("niche")
    rock = M("Prop_Rock")
    W, D, H = 0.90, 0.30, 0.95
    wall_bm = rock_block("Wall", (W, D, H + 0.06), rng, 0.03, 0.06, (0, 0, H / 2 - 0.03), flat_back=True, strata=0.008)
    for v in wall_bm.verts:
        v.co.z = max(v.co.z, 0.0)
    wall = pp.to_object("Wall", wall_bm, rock, wood=False, sharp_angle=22)
    # Cut the cavity with a boolean against a lumpy egg.
    hole_c = Vector((0.02, -D / 2 + 0.02, 0.44))
    cav_bm = bmesh.new()
    bmesh.ops.create_uvsphere(cav_bm, u_segments=20, v_segments=12, radius=1.0)
    seed = rng.uniform(0, 50)
    for v in cav_bm.verts:
        p = v.co.copy()
        k = 1.0 + 0.12 * pp.noise.noise(p * 2.5 + Vector((seed, 0, 0)))
        v.co = Vector((p.x * 0.17 * k, p.y * 0.17 * k, p.z * 0.14 * k)) + hole_c
    cutter = pp.to_object("Cutter", cav_bm, rock, wood=False)
    mod = wall.modifiers.new("Cavity", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.solver = "EXACT"
    mod.object = cutter
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT")
    wall.select_set(True)
    bpy.context.view_layer.objects.active = wall
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(cutter, do_unlink=True)
    _flag(wall, False)
    b.add("body", wall)
    # The plug: a stone that fills the mouth of the hole, its face part of the wall's face.
    plug_bm = rock_block("Plug", (0.26, 0.12, 0.22), rng, 0.025, 0.02, (0, 0, 0), power=3.0)
    front = -D / 2 - 0.01
    pp.place(plug_bm, Matrix.Translation((hole_c.x, front + 0.05, hole_c.z)))
    plug = pp.to_object("Plug", plug_bm, rock, wood=False, sharp_angle=22)
    b.add("Lid", plug)
    lo_z = min(v.co.z for v in plug.data.vertices)
    lo_y = min(v.co.y for v in plug.data.vertices)
    b.pivots["Lid"] = (hole_c.x, lo_y, lo_z)
    b.extras["Lid"] = {"hinge": "x", "open": 80}
    # Stones already pulled out, lying at the foot.
    stones = ((-0.27, -0.30, (0.15, 0.11, 0.09)), (0.30, -0.34, (0.12, 0.10, 0.08)), (0.12, -0.27, (0.06, 0.05, 0.045)),
              (-0.08, -0.36, (0.05, 0.04, 0.035)), (0.40, -0.22, (0.05, 0.05, 0.04)))
    for k, (x, y, sz) in enumerate(stones):
        st = rock_block(f"Stone_{k}", sz, rng, 0.02, min(sz) * 0.25, (0, 0, 0), power=2.4)
        pp.place(st, Matrix.Translation((x, y, sz[2] / 2 - 0.006)) @ Matrix.Rotation(rng.uniform(0, 3), 4, "Z") @ Matrix.Rotation(rng.uniform(-0.3, 0.3), 4, "X"))
        for v in st.verts:
            v.co.z = max(v.co.z, 0.0)
        b.add("body", pp.to_object(f"Stone_{k}", st, rock, wood=False, sharp_angle=22))
    # In the hole: a leather purse, a scroll case, a few coins.
    import props_cloth as pc
    floor_z = hole_c.z - 0.11
    purse_profile = _resample([(0.0, 0.0), (0.045, 0.002), (0.06, 0.02), (0.055, 0.05), (0.028, 0.065), (0.02, 0.074), (0.03, 0.088), (0.0, 0.092)], 16)
    purse = pc.sculpt_sack("Purse", rng, purse_profile, 24, neck=0.072, tie_spread=0.03, flop=(0.9, 1.0), pleats=6, lumps=0.006, creases=0.004)
    purse.data.materials.append(M("Prop_Leather"))
    for poly in purse.data.polygons:
        poly.use_smooth = True
    purse.location = (hole_c.x - 0.06, hole_c.y + 0.05, floor_z - 0.01)
    _flag(purse, False)
    b.add("Contents", purse)
    case = pp.lathe("ScrollCase", M("Prop_Leather"), [(0.0, -0.09), (0.022, -0.09), (0.022, 0.09), (0.0, 0.09)], 10,
                    (hole_c.x + 0.06, hole_c.y + 0.07, floor_z + 0.025), Matrix.Rotation(math.radians(90), 3, "Y") @ Matrix.Rotation(0.4, 3, "X"))
    b.add("Contents", case)
    for e in (-1, 1):
        cap = pp.lathe(f"CaseCap_{e}", M("Prop_Brass"), [(0.0, 0.0), (0.025, 0.0), (0.025, 0.02), (0.0, 0.022)], 10,
                       (hole_c.x + 0.06 + e * 0.085, hole_c.y + 0.07 + e * 0.035, floor_z + 0.025),
                       Matrix.Rotation(math.radians(90 * e), 3, "Y") @ Matrix.Rotation(0.4, 3, "X"))
        b.add("Contents", cap)
    b.add("Contents", pp.coin_pile("Coins", M("Prop_Gold"), rng, (hole_c.x + 0.01, hole_c.y + 0.0, floor_z - 0.025), 0.05, 0.015, 22, 0.011, base=False))
    b.glint = (0.0, 0.0, H + 0.06)
    # The footprint runs from the stones on the floor to the back of the wall: centre it.
    shift(b, (0.0, 0.10, 0.0))
    return b


# --- furniture -------------------------------------------------------------------------------

def bed(rng):
    """A straw pallet on a low plank frame, a rough blanket thrown over it."""
    import bmesh
    b = Build("bed")
    wood = M("Prop_Oak")
    iron = M("Prop_Iron_Rusty")
    W, L = 0.70, 0.96
    leg = 0.10
    nail_spots = []

    def wb(name, centre, along, out, length, width, thick, **kw):
        kw.setdefault("wear", 1.2)
        kw.setdefault("chips", 2)
        part = pp.slab(name, wood, rng, centre, along, out, length, width, thick, **kw)
        b.add("body", part)
        return part

    for sx in (-1, 1):
        for sy in (-1, 1):
            wb(f"Leg_{sx}_{sy}", (sx * (W / 2 - 0.03), sy * (L / 2 - 0.03), leg / 2), (0, 0, 1), (sx, 0, 0), leg, 0.06, 0.06, bevel=0.008, seg=0.1)
        wb(f"Rail_{sx}", (sx * (W / 2 + 0.01), 0, leg - 0.03), (0, 1, 0), (sx, 0, 0), L + 0.03, 0.07, 0.025, bevel=0.006)
        for sy in (-1, 1):
            nail_spots.append(((sx * (W / 2 + 0.023), sy * (L / 2 - 0.03), leg - 0.04), (sx, 0, 0)))
    for sy in (-1, 1):
        wb(f"End_{sy}", (0, sy * (L / 2 + 0.01), leg - 0.03), (1, 0, 0), (0, sy, 0), W + 0.03, 0.07, 0.025, bevel=0.006)
    for i, (at, w) in enumerate(pp.split(L - 0.04, 6, 0.03, rng, 0.2)):
        y = -L / 2 + 0.02 + at
        wb(f"Slat_{i}", (0, y, leg + 0.012), (1, 0, 0), (0, 0, 1), W + 0.02, w, 0.022, seg=0.12)
        for sx in (-1, 1):
            nail_spots.append(((sx * (W / 2 + 0.005), y, leg + 0.024), (0, 0, 1)))
    b.nails("body", "BedNails", iron, rng, nail_spots, 0.006, reach=0.06)
    # The pallet: a ticking bag of straw, lumpy, sagging where it is lain on, piped at its seams.
    top = leg + 0.024
    pw, pl, ph = W - 0.06, L - 0.06, 0.085
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=(pw, pl, ph), verts=bm.verts)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=14, use_grid_fill=True)
    seed = rng.uniform(0, 50)
    for v in bm.verts:
        p = v.co.copy()
        ex, ey, ez = abs(p.x) / (pw / 2), abs(p.y) / (pl / 2), p.z / (ph / 2)
        # Round off the box into a pillow: pull the edges in as they near the corners.
        squeeze = max(0.0, max(ex, ey) - 0.7) / 0.3
        p.z *= 1.0 - 0.55 * squeeze ** 2
        lump = 0.012 * pp.noise.noise(Vector((p.x * 8, p.y * 8, seed))) + 0.006 * pp.noise.noise(Vector((p.x * 20, p.y * 20, seed + 3)))
        sag = -0.02 * math.exp(-((p.y + 0.05) / 0.25) ** 2) * (1 - ex ** 2)
        if p.z > 0:
            p.z += (lump + sag) * (1 - squeeze)
        v.co = p + Vector((0, 0, top + ph / 2))
    pallet = pp.to_object("Pallet", bm, M("Prop_Burlap"), wood=False, sharp_angle=0)
    _flat_weave(pallet)
    b.add("body", pallet)
    # Straw out of a split seam at the foot.
    bm = bmesh.new()
    pp.straw_strands(bm, rng, (0.0, pl / 2 - 0.01, top + 0.04), (pw / 2 - 0.05, 0.02), 70, (0.04, 0.09), 0.02, 0.4, 0.2)
    stalks = pp.to_object("Stalks", bm, M("Prop_Straw"), sharp_angle=0)
    b.add("body", stalks)
    tuft = pp.heap("Tuft", M("Prop_Straw"), (0.08, pl / 2 + 0.0, top + 0.035), (0.07, 0.025, 0.03), rng, res=8, bumps=0.4, freq=20)
    b.add("body", tuft)
    b.borrow.append((stalks, tuft, 6))
    # The blanket: thrown on, rumpled, half off the foot and over one side.
    bm = bmesh.new()
    nx, ny = 30, 34
    bwid, blen = W + 0.10, 0.70
    rows = []
    surface_z = top + ph
    for j in range(ny + 1):
        row = []
        for i in range(nx + 1):
            u = -bwid / 2 + bwid * i / nx
            v = -0.06 + blen * j / ny
            x, y = u, v
            z = surface_z + 0.012 + 0.018 * pp.noise.noise(Vector((u * 6, v * 6, seed + 9)))
            z += 0.03 * (1.0 - abs(pp.noise.noise(Vector((u * 2.5 + v * 1.5, v * 3.0, seed + 5))))) ** 6
            # Over the side: past the pallet's edge it falls down the side.
            over = abs(x) - (pw / 2 - 0.02)
            if over > 0:
                x = math.copysign(pw / 2 - 0.02 + 0.03 * math.sin(min(over / 0.06, 1.0) * math.pi / 2), x)
                z -= over * 1.6
            over_y = y - (pl / 2 - 0.02)
            if over_y > 0:
                y = pl / 2 - 0.02 + 0.02 * math.sin(min(over_y / 0.05, 1.0) * math.pi / 2)
                z -= over_y * 1.4
            row.append(bm.verts.new((x, y - 0.12, max(z, leg + 0.03))))
        rows.append(row)
    for j in range(ny):
        for i in range(nx):
            bm.faces.new((rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i]))
    pp.place(bm, Matrix.Rotation(0.06, 4, "Z"))
    blanket = pp.to_object("Blanket", bm, M("Prop_Blanket"), wood=False, sharp_angle=0)
    mod = blanket.modifiers.new("Thick", "SOLIDIFY")
    mod.thickness = 0.008
    mod.offset = 1.0
    b.add("body", blanket)
    # A rolled cloth for a pillow at the head.
    pillow = pp.heap("Pillow", M("Prop_Cloth"), (0.0, -pl / 2 + 0.11, surface_z + 0.005), (0.20, 0.07, 0.045), rng, res=12, bumps=0.15, freq=10)
    b.add("body", pillow)
    return b


def _flat_weave(obj):
    """The burlap weave laid flat across a part, for things that are not lathed bags."""
    w = obj.data.attributes.new("weave", "FLOAT_VECTOR", "CORNER")
    vals = []
    for poly in obj.data.polygons:
        n = poly.normal
        for li in poly.loop_indices:
            co = obj.data.vertices[obj.data.loops[li].vertex_index].co
            if abs(n.z) > 0.6:
                vals.extend((co.x, co.y, 0.0))
            elif abs(n.x) > abs(n.y):
                vals.extend((co.y, co.z, 0.0))
            else:
                vals.extend((co.x, co.z, 0.0))
    w.data.foreach_set("vector", vals)


def table(rng):
    """A heavy trestle table: a top of three thick boards on two splayed trestles, a stretcher
    between them wedged through, and on it an iron plate with the end of a meal, a cup and a
    candle stub guttered down to its dish."""
    b = Build("table")
    wood = M("Prop_Oak")
    iron = M("Prop_Iron")
    L, Wd, H = 0.98, 0.60, 0.58
    tt = 0.045
    nail_spots = []

    def wb(name, centre, along, out, length, width, thick, **kw):
        kw.setdefault("wear", 1.3)
        kw.setdefault("chips", 3)
        part = pp.slab(name, wood, rng, centre, along, out, length, width, thick, **kw)
        b.add("body", part)
        return part

    for i, (at, w) in enumerate(pp.split(Wd, 3, 0.006, rng, 0.15)):
        wb(f"Top_{i}", (0, -Wd / 2 + at, H - tt / 2), (1, 0, 0), (0, 0, 1), L, w, tt, bevel=0.008, bow=-0.003)
    for sx in (-1, 1):
        x = sx * (L / 2 - 0.16)
        wb(f"Cleat_{sx}", (x, 0, H - tt - 0.02), (0, 1, 0), (0, 0, -1), Wd - 0.04, 0.07, 0.04, bevel=0.007)
        wb(f"FootBoard_{sx}", (x, 0, 0.025), (0, 1, 0), (0, 0, 1), Wd - 0.02, 0.08, 0.05, bevel=0.008)
        for sy in (-1, 1):
            a = Vector((x, sy * 0.21, 0.05))
            c = Vector((x, sy * 0.08, H - tt - 0.04))
            part = pp.plank_run(f"Leg_{sx}_{sy}", wood, rng, a, c, 0.07, 0.05, (sx, 0, 0), wear=1.2, chips=2, bevel=0.008)
            b.add("body", part)
            nail_spots.append(((x + sx * 0.026, sy * 0.09, H - tt - 0.05), (sx, 0, 0)))
            nail_spots.append(((x + sx * 0.026, sy * 0.20, 0.03), (sx, 0, 0)))
    wb("Stretcher", (0, 0, 0.26), (1, 0, 0), (0, -1, 0), L - 0.16, 0.07, 0.05, bevel=0.008)
    for sx in (-1, 1):
        wb(f"Wedge_{sx}", (sx * (L / 2 - 0.10), 0, 0.26), (0, 0, 1), (sx, 0, 0), 0.11, 0.03, 0.02, bevel=0.004, chips=0, taper=0.4)
    for i, (at, w) in enumerate(pp.split(Wd, 3, 0.006, rng, 0.0)):
        for sx in (-1, 1):
            nail_spots.append(((sx * (L / 2 - 0.16), -Wd / 2 + at, H), (0, 0, 1)))
    b.nails("body", "TableNails", iron, rng, nail_spots, 0.0075, reach=0.06)
    # The end of a meal.
    plate = pp.lathe("Plate", iron, [(0.0, 0.0), (0.075, 0.0), (0.085, 0.012), (0.09, 0.015), (0.087, 0.017), (0.075, 0.006), (0.0, 0.004)], 18,
                     (0.18, 0.05, H))
    b.add("body", plate)
    bone = pp.lathe("Bone", M("Prop_Bone"), [(0.0, -0.05), (0.014, -0.048), (0.008, -0.03), (0.007, 0.03), (0.013, 0.045), (0.0, 0.05)], 8,
                    (0.19, 0.04, H + 0.016), Matrix.Rotation(math.radians(88), 3, "Y") @ Matrix.Rotation(0.6, 3, "X"))
    b.add("body", bone)
    crust = pp.heap("Crust", M("Prop_Bread"), (0.14, 0.09, H + 0.008), (0.035, 0.025, 0.018), rng, res=8, bumps=0.25, freq=20)
    b.add("body", crust)
    cup = pp.lathe("Cup", M("Prop_Oak_Clean"), [(0.0, 0.0), (0.03, 0.0), (0.033, 0.005), (0.035, 0.07), (0.031, 0.072), (0.029, 0.01), (0.0, 0.01)], 14,
                   (-0.14, -0.10, H))
    b.add("body", cup)
    for e in (0.022, 0.05):
        b.add("body", pp.torus(f"CupHoop_{e}", iron, (-0.14, -0.10, H + e), 0.0345, 0.0025, segs=14, minor_segs=4))
    dish = pp.lathe("Dish", M("Prop_Brass"), [(0.0, 0.0), (0.045, 0.0), (0.05, 0.01), (0.046, 0.011), (0.04, 0.004), (0.0, 0.004)], 14, (-0.30, 0.13, H))
    b.add("body", dish)
    wax = pp.lathe("Candle", M("Prop_Wax"), [(0.0, 0.004), (0.03, 0.004), (0.026, 0.008), (0.016, 0.012), (0.016, 0.04), (0.012, 0.045), (0.004, 0.044), (0.0, 0.046)], 12,
                   (-0.30, 0.13, H), jitter=0.12, rng=rng)
    b.add("body", wax)
    wick = pp.lathe("Wick", M("Prop_Void"), [(0.0, 0.044), (0.0015, 0.044), (0.0012, 0.055), (0.0, 0.056)], 5, (-0.30, 0.13, H))
    b.add("body", wick)
    for k in range(4):
        a = rng.uniform(0, math.tau)
        drip = pp.heap(f"Drip_{k}", M("Prop_Wax"), (-0.30 + 0.016 * math.cos(a), 0.13 + 0.016 * math.sin(a), H + 0.02 + rng.uniform(-0.01, 0.01)),
                       (0.004, 0.004, 0.012), rng, res=6, bumps=0.1, freq=10)
        b.add("body", drip)
    return b


PROPS = {
    "crate": lambda rng: crate(rng, "a"),
    "crate-b": lambda rng: crate(rng, "b"),
    "chest": chest,
    "strongbox": strongbox,
    "barrel": barrel,
    "sack": lambda rng: sack(rng, False),
    "sack-open": lambda rng: sack(rng, True),
    "cart": cart,
    "cart-overturned": cart_overturned,
    "weapon-rack": weapon_rack,
    "pile": pile,
    "niche": niche,
    "bed": bed,
    "table": table,
}
SEEDS = {"crate": 11, "crate-b": 23, "chest": 5, "strongbox": 7, "barrel": 3, "sack": 4, "sack-open": 9, "cart": 13, "cart-overturned": 17, "weapon-rack": 19, "pile": 29, "niche": 31, "bed": 37, "table": 41}


# --- bake, assemble, export -------------------------------------------------------------------

def bake(build, keep=None):
    """Bake every node's parts into one atlas. Each node is moved apart from the others while
    the bake runs, so its occlusion is its own: the inside of a box is not baked black under
    its lid, and what lies in it is not darkened by walls the game will light round anyway.
    Only the body gets a floor, so its foot darkens where it meets the ground."""
    parts = [p for ps_ in build.nodes.values() for p in ps_]
    bpy.ops.mesh.primitive_plane_add(size=6.0, location=(0, 0, 0))
    floor = bpy.context.active_object
    floor.name = "BakeFloor"
    moved = []
    for k, (node, node_parts) in enumerate(build.nodes.items()):
        if node == "body":
            continue
        offset = Vector((0.0, 0.0, 3.0 * (k + 1)))
        for p in node_parts:
            p.location += offset
            moved.append((p, offset))
    bpy.context.view_layer.update()
    surface.finish([(build.name, parts, build.atlas, True)], keep=keep)
    for p, offset in moved:
        p.location -= offset
    bpy.data.objects.remove(floor, do_unlink=True)
    bpy.context.view_layer.update()
    if not os.environ.get("IRONBOUND_FAST"):
        drop_orig_uvs(build.unbaked_uvs)
        borrow_uvs(build)
        ps.weather(parts, build.name, build.rust, keep)
        ps.jpeg_colour(build.name)


def borrow_uvs(build):
    """Point every loose stalk at a spot of the straw bed it lies on. A stalk is too thin to
    get a texel of its own in the atlas, and baked alone it came out as a black hair."""
    rng = random.Random(7)
    for thin, source, per in build.borrow:
        if source is None:
            # No bed to borrow from: a straw stalk keeps its own colour by pointing at the
            # middle of its own first face, which is at least straw.
            continue
        src = source.data
        layer = src.uv_layers.active
        if layer is None or thin.data.uv_layers.active is None:
            continue
        centres = []
        for poly in src.polygons:
            if poly.normal.z < 0.5:
                continue
            u = sum(layer.data[i].uv[0] for i in poly.loop_indices) / poly.loop_total
            v = sum(layer.data[i].uv[1] for i in poly.loop_indices) / poly.loop_total
            centres.append((u, v))
        if not centres:
            continue
        uv = thin.data.uv_layers.active.data
        # One spot per stalk: each stalk is `per` faces in a row.
        for poly in thin.data.polygons:
            if poly.index % per == 0:
                spot = rng.choice(centres)
            for i in poly.loop_indices:
                uv[i].uv = spot


def assemble(build):
    made = {}
    for node, parts in build.nodes.items():
        if not parts:
            continue
        name = build.name if node == "body" else node
        obj = pp.join(name, parts, build.pivots.get(node, (0.0, 0.0, 0.0)))
        for k, v in build.extras.get(node, {}).items():
            obj[k] = v
        made[node] = obj
    if build.glint:
        pp.empty("FX_Glint", build.glint)
    return made


def export(path):
    path = os.path.abspath(path)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", use_selection=False, export_apply=True, export_yup=True,
        export_animations=False, export_skins=False, export_morph=False, export_materials="EXPORT",
        export_extras=True, export_cameras=False, export_lights=False)


def check(path):
    """Read the GLB back: nodes, triangles, the box in Godot's frame, the size on disk."""
    with open(path, "rb") as f:
        data = f.read()
    jlen = struct.unpack("<I", data[12:16])[0]
    doc = json.loads(data[20:20 + jlen])
    nodes = doc["nodes"]
    tris = {}
    lo = [1e9] * 3
    hi = [-1e9] * 3
    for n in nodes:
        if "mesh" not in n:
            continue
        t = n.get("translation", [0, 0, 0])
        count = 0
        for prim in doc["meshes"][n["mesh"]]["primitives"]:
            count += doc["accessors"][prim["indices"]]["count"] // 3
            acc = doc["accessors"][prim["attributes"]["POSITION"]]
            for i in range(3):
                lo[i] = min(lo[i], acc["min"][i] + t[i])
                hi[i] = max(hi[i], acc["max"][i] + t[i])
        tris[n["name"]] = count
    names = [n.get("name") for n in nodes]
    size = os.path.getsize(path)
    print(f"PROP {os.path.basename(path)}  {size / 1e6:.2f} MB  nodes {names}")
    print(f"     triangles {tris}  total {sum(tris.values())}")
    print(f"     box (Godot) x[{lo[0]:+.3f},{hi[0]:+.3f}] y[{lo[1]:+.3f},{hi[1]:+.3f}] z[{lo[2]:+.3f},{hi[2]:+.3f}]  "
          f"footprint {hi[0] - lo[0]:.3f} x {hi[2] - lo[2]:.3f}  height {hi[1]:.3f}")
    for n in nodes:
        if n.get("name") in ("Lid", "FX_Glint"):
            print(f"     {n['name']:8} at {[round(v, 4) for v in n.get('translation', [0, 0, 0])]}  extras {n.get('extras', {})}")
    ok = lo[1] > -0.02 and size < 3.4e6
    print(f"     {'PASS' if ok else 'FAIL'}")
    return ok


def build(name, directory, keep=None):
    clear_scene()
    rng = random.Random(SEEDS.get(name, 1))
    made = PROPS[name](rng)
    bpy.context.view_layer.update()
    bake(made, keep)
    assemble(made)
    path = os.path.join(directory, f"{name}.glb")
    export(path)
    check(path)


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        raise RuntimeError("Usage: blender -b --factory-startup --python generate_props.py -- OUTPUT_DIRECTORY [id ...]")
    keep = os.environ.get("IRONBOUND_KEEP")
    os.makedirs(args[0], exist_ok=True)
    for prop in (args[1:] or PROPS):
        build(prop, args[0], keep)

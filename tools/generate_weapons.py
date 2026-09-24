"""What the party holds: one small static model per hand-held item.

    blender -b --factory-startup --python tools/generate_weapons.py -- <output-directory>

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
"""
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import generate_goblin as gg  # noqa: E402

SILVER = gg.mat("Silvered_Steel", (0.82, 0.86, 0.92), 0.35, 0.30)
STEEL = gg.mat("Sword_Steel", (0.56, 0.57, 0.60), 0.30, 0.45)
COLD_IRON = gg.mat("Cold_Iron", (0.17, 0.19, 0.23), 0.30, 0.55)
ASH = gg.mat("Ash_Wood", (0.50, 0.36, 0.20), 0.0, 0.85)


def sword(steel, length=2.0, width=0.13, curved=False):
    parts = [
        gg.cyl("Grip", (0, 0, 0), 0.045, 0.42, gg.LEATHER, vertices=10),
        gg.uv("Pommel", (0, 0, -0.25), (0.075,) * 3, gg.GOLD, 10, 6),
        gg.cube("Guard", (0, 0, 0.23), (0.26 if not curved else 0.16, 0.05, 0.035), gg.GOLD if not curved else gg.IRON, 0.012),
    ]
    edge = gg.blade("Blade", (0, 0, 0.25), length, width, steel, 0.16)
    if curved:
        # A scimitar: the same blade, swept back further the further up it goes.
        for v in edge.data.vertices:
            # The blade's vertices are about its own middle, not the grip.
            t = max(0.0, (v.co.z + length / 2) / length)
            v.co.x += 0.34 * t * t
    parts.append(edge)
    return parts


def greataxe(steel):
    parts = [
        gg.cyl("Haft", (0, 0, 0.95), 0.05, 2.9, ASH, vertices=10),
        gg.cyl("Haft_Wrap", (0, 0, 0.0), 0.058, 0.5, gg.LEATHER, vertices=10),
        gg.cone("Spike", (0, 0, 2.52), 0.05, 0.004, 0.26, steel, vertices=8),
        gg.cube("Socket", (0, 0, 2.05), (0.085, 0.085, 0.24), steel, 0.02),
    ]
    # Two bearded blades, cut from an outline: a wedge would be a hammer.
    import bmesh
    for side in (-1, 1):
        outline = [(0.07, 1.86), (0.30, 1.62), (0.62, 1.55), (0.74, 2.05), (0.62, 2.55), (0.30, 2.48), (0.07, 2.24)]
        bm = bmesh.new()
        near = [bm.verts.new((side * x, -0.02 if i in (0, 6) else -0.004, z)) for i, (x, z) in enumerate(outline)]
        far = [bm.verts.new((side * x, 0.02 if i in (0, 6) else 0.004, z)) for i, (x, z) in enumerate(outline)]
        bm.faces.new(near if side > 0 else list(reversed(near)))
        bm.faces.new(list(reversed(far)) if side > 0 else far)
        for i in range(len(outline)):
            j = (i + 1) % len(outline)
            quad = (near[j], near[i], far[i], far[j])
            bm.faces.new(quad if side > 0 else tuple(reversed(quad)))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        mesh = bpy.data.meshes.new(f"AxeBlade_{side}")
        bm.to_mesh(mesh)
        bm.free()
        head = bpy.data.objects.new(f"AxeBlade_{side}", mesh)
        bpy.context.collection.objects.link(head)
        gg.apply_mat(head, steel)
        parts.append(head)
    return parts


def quarterstaff():
    # Held at the middle, so the origin is there too and it reaches as far down as up.
    return [
        gg.cyl("Staff", (0, 0, 0.35), 0.048, 3.7, ASH, vertices=10),
        gg.cyl("Staff_Wrap", (0, 0, 0.0), 0.056, 0.7, gg.LEATHER, vertices=10),
        gg.cyl("Staff_Cap_Top", (0, 0, 2.17), 0.062, 0.14, gg.IRON, vertices=10),
        gg.cyl("Staff_Cap_Foot", (0, 0, -1.47), 0.062, 0.14, gg.IRON, vertices=10),
    ]


def bow_limbs(reach, belly, thick):
    """A bow standing up the Z axis, belly forward (-Y), held at its middle."""
    parts, points = [], []
    for i in range(9):
        t = (i / 8) * 2 - 1
        points.append(Vector((0, -belly * (1 - t * t), reach * t)))
    for a, b in zip(points, points[1:]):
        parts.append(gg.limb("Bow_Limb", a, b, thick, gg.BOW_WOOD))
    parts.append(gg.limb("Bow_String", points[0], points[-1], 0.010, gg.BOW_STRING))
    parts.append(gg.cyl("Bow_Grip", tuple(points[4]), thick * 1.5, 0.30, gg.LEATHER, vertices=8))
    # The grip is where the hand is, so it is where the origin is.
    for o in parts:
        o.location.y += belly
    return parts


def crossbow():
    parts = [
        gg.cube("Stock", (0, 0, 0.55), (0.06, 0.075, 0.85), ASH, 0.02),
        gg.cube("Stock_Butt", (0, 0.03, -0.35), (0.065, 0.11, 0.16), ASH, 0.03),
        gg.cyl("Trigger", (0, 0.10, 0.10), 0.014, 0.16, gg.IRON, vertices=6),
        gg.cyl("Bolt", (0, -0.085, 0.95), 0.013, 0.95, gg.BOW_WOOD, vertices=6),
        gg.cone("Bolt_Head", (0, -0.085, 1.48), 0.03, 0.002, 0.12, STEEL, vertices=6),
    ]
    # The prod: a short stiff bow lying across the front of the stock.
    points = [Vector((0.72 * t, 0, 1.30 - 0.22 * t * t)) for t in (-1, -0.66, -0.33, 0, 0.33, 0.66, 1)]
    for a, b in zip(points, points[1:]):
        parts.append(gg.limb("Prod", a, b, 0.034, gg.IRON))
    parts.append(gg.limb("Prod_String", points[0], points[-1], 0.010, gg.BOW_STRING))
    return parts


def shield(radius, heavy):
    face = (math.pi / 2, 0, 0)
    parts = [
        gg.cyl("Board", (0, -0.06, 0), radius, 0.09, gg.WOOD, face, 28),
        gg.ring("Rim", (0, -0.06, 0), radius, 0.05, STEEL if heavy else gg.IRON, face),
        gg.dome("Boss", (0, -0.09, 0), (radius * 0.28,) * 3, STEEL if heavy else gg.IRON, 0.0, 14, 8),
        gg.cube("Strap", (0, 0.02, 0), (0.05, 0.02, radius * 0.55), gg.LEATHER, 0.01),
    ]
    parts[2].rotation_euler = (math.pi / 2, 0, 0)

    for j in range(3 if heavy else 2):
        seam = gg.cube(f"Seam_{j}", (0, -0.108, 0), (radius * 0.94, 0.004, 0.012), gg.SEAM)
        seam.rotation_euler = (0, j * math.pi / (3 if heavy else 2) + 0.3, 0)
        parts.append(seam)

    if heavy:
        parts.append(gg.ring("Band", (0, -0.10, 0), radius * 0.60, 0.024, STEEL, face))
        band = gg.cube("Colours", (0, -0.112, 0), (radius * 0.92, 0.004, 0.10), gg.CLOTH_RED)
        band.rotation_euler = (0, math.radians(40), 0)
        parts.append(band)

    for j in range(8 if heavy else 6):
        a = j * 2 * math.pi / (8 if heavy else 6)
        parts.append(gg.uv(f"Rivet_{j}", (radius * 0.82 * math.cos(a), -0.115, radius * 0.82 * math.sin(a)), (0.035,) * 3, STEEL, 8, 5))
    return parts


WEAPONS = {
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


def build(name, directory):
    gg.clear_scene()
    parts = WEAPONS[name]()

    # One mesh: a sword is one thing, and should be one draw call.
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT")
    for o in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.convert(target="MESH")          # applies the bevels before the join
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

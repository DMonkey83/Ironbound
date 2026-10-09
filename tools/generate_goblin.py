import bpy
import math
import os
import struct
import json
import sys
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import surface  # noqa: E402
import cards  # noqa: E402

RIGHT_HAND = (0.48, -0.05, 0.78)
LEFT_HAND = (-0.48, -0.05, 0.78)

MATERIALS = {}

def mat(name, color, metallic=0.0, roughness=0.7):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1.0)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    MATERIALS[name] = m
    return m

SKIN = mat("Goblin_Skin", (0.24, 0.38, 0.12), 0.0, 0.85)
SKIN_DARK = mat("Goblin_Skin_Dark", (0.12, 0.20, 0.06), 0.0, 0.9)
EYE = mat("Goblin_Eye", (0.55, 0.42, 0.08), 0.0, 0.35)
PUPIL = mat("Goblin_Pupil", (0.01, 0.006, 0.002), 0.0, 0.25)
TOOTH = mat("Goblin_Teeth", (0.72, 0.62, 0.38), 0.0, 0.65)
LEATHER = mat("Leather", (0.12, 0.055, 0.025), 0.0, 0.9)
LEATHER_LIGHT = mat("Leather_Light", (0.28, 0.13, 0.055), 0.0, 0.85)
CLOTH = mat("Cloth", (0.16, 0.12, 0.09), 0.0, 1.0)
# Barely metallic on purpose. The game lights with a flat ambient colour and has no sky to
# reflect, and a properly metallic surface with nothing to reflect renders as a dark hole.
METAL = mat("Worn_Metal", (0.46, 0.47, 0.50), 0.30, 0.50)
CHAIN = mat("Chain_Shirt", (0.25, 0.26, 0.29), 0.25, 0.75)
WOOD = mat("Shield_Wood", (0.36, 0.17, 0.07), 0.0, 0.9)
BOW_WOOD = mat("Bow_Wood", (0.62, 0.38, 0.14), 0.0, 0.8)
BOW_STRING = mat("Bow_String", (0.88, 0.82, 0.62), 0.0, 0.7)
CLOTH_RED = mat("Cloth_Red", (0.50, 0.07, 0.05), 0.0, 1.0)
GOLD = mat("Gold", (0.85, 0.62, 0.15), 0.30, 0.45)
FLETCH = mat("Fletching", (0.85, 0.82, 0.75), 0.0, 0.9)
CLAW = mat("Claw", (0.10, 0.09, 0.06), 0.0, 0.6)
HAIR = mat("Hair", (0.07, 0.045, 0.03), 0.0, 0.9)
IRON = mat("Rusty_Iron", (0.31, 0.265, 0.235), 0.25, 0.65)
IRON_EDGE = mat("Ground_Edge", (0.56, 0.55, 0.53), 0.30, 0.45)
SEAM = mat("Plank_Seam", (0.10, 0.05, 0.02), 0.0, 1.0)

def smooth(obj):
    if obj.type == "MESH":
        for p in obj.data.polygons:
            p.use_smooth = True

def apply_mat(obj, material):
    obj.data.materials.append(material)

def uv(name, loc, scale, material, segments=20, rings=12):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, location=loc)
    o = bpy.context.object
    o.name = name
    o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    smooth(o)
    apply_mat(o, material)
    return o

def cube(name, loc, scale, material, bevel=0.0):
    bpy.ops.mesh.primitive_cube_add(location=loc)
    o = bpy.context.object
    o.name = name
    o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod = o.modifiers.new("EdgeBevel", "BEVEL")
        mod.width = bevel
        mod.segments = 2
    apply_mat(o, material)
    return o

def cyl(name, loc, radius, depth, material, rotation=(0, 0, 0), vertices=16):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices,
        radius=radius,
        depth=depth,
        location=loc,
        rotation=rotation,
    )
    o = bpy.context.object
    o.name = name
    smooth(o)
    apply_mat(o, material)
    return o

def cone(name, loc, r1, r2, depth, material, rotation=(0, 0, 0), vertices=16):
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices,
        radius1=r1,
        radius2=r2,
        depth=depth,
        location=loc,
        rotation=rotation,
    )
    o = bpy.context.object
    o.name = name
    smooth(o)
    apply_mat(o, material)
    return o

def orient_between(obj, a, b):
    a = Vector(a)
    b = Vector(b)
    direction = b - a
    obj.location = (a + b) / 2
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = direction.to_track_quat("Z", "Y")
    obj.dimensions.z = direction.length
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

def limb(name, a, b, radius, material):
    o = cyl(name, (0, 0, 0), radius, 1.0, material)
    orient_between(o, a, b)
    return o

def frame(variant):
    """Every landmark on the body, written down once.

    The flesh, the skeleton and the kit are all placed from this, so a bone runs down the middle
    of the limb it moves and a bracer sits on the forearm it was made for. Right-hand side only;
    mirror() gives the left. The hobgoblin is the same creature stood up straighter: taller,
    broader, less of a stoop, and built heavier.
    """
    hob = variant == "hobgoblin"
    sx, sy, sz, sr = (1.15, 0.70, 1.18, 1.15) if hob else (1.0, 1.0, 1.0, 1.0)

    def P(x, y, z):
        return Vector((x * sx, y * sy, z * sz))

    L = {
        "pelvis": P(0, 0.06, 0.80), "belly": P(0, 0.00, 0.98), "ribs": P(0, -0.07, 1.20),
        "back": P(0, 0.10, 1.33), "neck_a": P(0, -0.06, 1.38), "neck_b": P(0, -0.20, 1.50),
        "head": P(0, -0.30, 1.60),
        "shoulder": P(0.33, -0.02, 1.35), "elbow": P(0.50, -0.10, 1.01),
        "wrist": P(0.54, -0.31, 0.72), "palm": P(0.54, -0.36, 0.64), "fingertip": P(0.54, -0.46, 0.50),
        "hip": P(0.15, 0.03, 0.76), "knee": P(0.33, -0.16, 0.43), "ankle": P(0.32, 0.02, 0.11),
        "foot": P(0.33, -0.09, 0.058), "toe": P(0.34, -0.30, 0.03),
    }
    L["r"] = sr

    # The head is big for the body — a goblin's is nearly a quarter of his height — and that,
    # with the ears, is most of what the silhouette says before anything else does.
    L["hs"] = sr * (1.08 if hob else 1.17)

    # How thick the flesh is laid on. The hobgoblin is not a goblin scaled up: he is a soldier,
    # deep in the chest and heavy in the limb, and that is a different number from his height.
    L["bulk"] = 1.14 if hob else 1.0
    L["hob"] = hob
    L["lean"] = math.atan2(-(L["ribs"].y - L["pelvis"].y) * 1.3, L["ribs"].z - L["pelvis"].z)
    return L


def mirror(p, side):
    return Vector((p.x * side, p.y, p.z))


def create_body(variant):
    """One continuous skin, grown from metaballs.

    Primitives cannot do this however many of them there are: a sphere for a chest and a
    cylinder for an arm meet at a seam, and a body made of seams reads as a toy. Metaballs merge,
    so the deltoid runs into the arm and the brow into the skull, and the stance — stooped,
    long-armed, bow-legged — is most of what makes it a goblin at forty pixels.
    """
    L = frame(variant)
    r, hob = L["r"], L["hob"]
    L["grip"] = grips(variant, L)

    mb = bpy.data.metaballs.new("GoblinMeta")
    mb.resolution = 0.018 * r
    mb.threshold = 0.6
    meta = bpy.data.objects.new("GoblinMeta", mb)
    bpy.context.collection.objects.link(meta)

    def ball(co, size, negative=False):
        e = mb.elements.new(type="ELLIPSOID")
        e.co = co
        if isinstance(size, (int, float)):
            size = (size,) * 3
        # The surface sits well inside an element's nominal size at this threshold.
        e.size_x, e.size_y, e.size_z = (v * 1.55 * r * L["bulk"] for v in size)
        e.radius = 1.0
        e.stiffness = 1.6 if negative else 2.0
        e.use_negative = negative

    def chain(a, b, ra, rb, n=4):
        for i in range(n + 1):
            t = i / n
            ball(a.lerp(b, t), ra + (rb - ra) * t)

    def off(base, x, y, z):
        return base + Vector((x, y, z)) * r

    # Torso: stooped and wiry. Broad across the shoulders, narrow at the waist, with the hump
    # of trapezius behind the neck that a life spent crouching gives you.
    ball(L["pelvis"], (0.20, 0.155, 0.15))
    ball(L["belly"], (0.172, 0.150, 0.165))
    ball(L["ribs"], (0.255, 0.185, 0.215))
    ball(L["back"] + Vector((0, -0.04, 0)) * r, (0.24, 0.12, 0.14))
    for side in (-1, 1):
        ball(off(L["ribs"], side * 0.115, -0.145, 0.075), (0.125, 0.072, 0.095))   # pectoral
        # Lats: flat against the ribs, not a second pair of shoulders behind them.
        ball(off(L["ribs"], side * 0.20, 0.08, 0.0), (0.095, 0.06, 0.14))
        ball(off(L["back"], side * 0.13, -0.02, 0.07), (0.10, 0.07, 0.07))         # trapezius
        for row in range(3):                                                        # abdominals
            ball(off(L["belly"], side * 0.05, -0.135 + row * 0.006, 0.10 - row * 0.085), 0.046)
    chain(L["neck_a"], L["neck_b"], 0.10, 0.085, 2)

    # Head: long low skull, a shelf of brow, a hooked nose, cheekbones, and a jaw that juts.
    H = L["head"]
    hs = L["hs"] / (r * L["bulk"])         # the head keeps its own scale; bulk is for the body
    jaw = 1.22 if hob else 1.0

    def face(x, y, z):
        return H + Vector((x, y, z)) * L["hs"]

    L["mouth"] = face(0, -0.245, -0.145)

    ball(H, (0.195 * hs, 0.235 * hs, 0.185 * hs))
    ball(face(0, 0.10, 0.03), (0.17 * hs, 0.16 * hs, 0.16 * hs))                   # back of skull
    ball(face(0, -0.13, -0.10), (0.130 * hs * jaw, 0.125 * hs, 0.082 * hs * jaw))  # jaw
    ball(face(0, -0.235, -0.145), (0.062 * hs * jaw, 0.05 * hs, 0.05 * hs))        # chin
    ball(face(0, -0.215, 0.062), (0.180 * hs, 0.070 * hs, 0.046 * hs))             # brow, overhanging
    for side in (-1, 1):
        ball(face(side * 0.10, -0.235, 0.052), (0.075 * hs, 0.05 * hs, 0.036 * hs))   # and scowling
    ball(face(0, -0.26, 0.0), (0.034 * hs, 0.085 * hs, 0.060 * hs))                # nose bridge
    ball(face(0, -0.345, -0.040), (0.044 * hs, 0.070 * hs, 0.050 * hs))            # nose tip
    ball(face(0, -0.378, -0.072), (0.027 * hs, 0.036 * hs, 0.028 * hs))            # the hook
    ball(face(0, -0.228, -0.112), (0.118 * hs, 0.05 * hs, 0.024 * hs))             # upper lip
    ball(face(0, -0.208, -0.178), (0.118 * hs * jaw, 0.06 * hs, 0.034 * hs))       # lower lip
    # The mouth: open in a snarl, wide, and deep enough to have an inside, which is painted
    # dark and has the teeth set in it. A slit could only ever show teeth stuck on outside.
    ball(L["mouth"], (0.125 * hs, 0.080 * hs, 0.034 * hs), negative=True)
    for side in (-1, 1):
        ball(face(side * 0.130, -0.14, -0.02), 0.066 * hs)                         # cheekbone
        ball(face(side * 0.048, -0.30, -0.065), 0.032 * hs)                        # nostril
        ball(face(side * 0.088, -0.225, 0.004), (0.044 * hs, 0.05 * hs, 0.026 * hs), negative=True)
        ball(face(side * 0.10, -0.175, -0.085), (0.03 * hs, 0.03 * hs, 0.06 * hs), negative=True)  # hollow cheek

    # Arms: long and ropey, thickest at the forearm, ending in hands too big for them.
    for side in (-1, 1):
        sh, el, wr, pa = (mirror(L[k], side) for k in ("shoulder", "elbow", "wrist", "palm"))
        ball(sh, 0.128)
        chain(off(sh, side * 0.03, -0.01, -0.06), el, 0.082, 0.070)
        ball(sh.lerp(el, 0.50) + Vector((-side * 0.02, -0.01, 0.02)) * r, (0.082, 0.092, 0.115))   # biceps
        ball(el, 0.072)
        chain(el, wr, 0.092, 0.056)
        ball(el.lerp(wr, 0.28) + Vector((side * 0.015, 0.0, 0.0)) * r, (0.082, 0.085, 0.105))      # forearm
        fist(ball, chain, L["grip"][side], wr, r)

    # Legs: short, crouched and bowed, on feet as outsized as the hands.
    for side in (-1, 1):
        hp, kn, an, ft = (mirror(L[k], side) for k in ("hip", "knee", "ankle", "foot"))
        chain(hp, kn, 0.125, 0.082)
        ball(hp.lerp(kn, 0.35) + Vector((side * 0.02, -0.03, 0)) * r, (0.105, 0.11, 0.13))         # quadriceps
        ball(off(kn, 0, -0.025, -0.01), 0.078)
        chain(kn, an, 0.078, 0.050)
        ball(kn.lerp(an, 0.38) + Vector((0, 0.075, 0)) * r, (0.075, 0.088, 0.115))                 # calf
        ball(ft, (0.098, 0.185, 0.055))
        for dx in (-0.06, 0.0, 0.06):
            ball(off(ft, dx, -0.195, -0.018), (0.034, 0.055, 0.034))

    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = meta
    meta.select_set(True)
    bpy.ops.object.convert(target="MESH")
    body = bpy.context.view_layer.objects.active
    body.name = "Body"
    smooth(body)
    thin = body.modifiers.new("Thin", "DECIMATE")
    thin.ratio = 0.24
    bpy.ops.object.modifier_apply(modifier="Thin")

    # Stood on the floor: the lowest toe is zero, whatever the proportions did to it.
    drop = min(v.co.z for v in body.data.vertices)
    for v in body.data.vertices:
        v.co.z -= drop
    for key, value in L.items():
        if isinstance(value, Vector):
            value.z -= drop
    for g in L["grip"].values():
        g["c"].z -= drop

    parts = [body]
    parts += create_head_parts(L, body)
    return parts, L


EAR_INNER = {}


def create_ear(name, root, sweep, length, side):
    """A goblin's ear: a long cupped leaf, thick at the root, notched, thin at the tip.

    Built vertex by vertex because nothing in the primitive box is this shape — the cone it
    replaces read as a horn. The front face is dished in behind the rim, which is what catches
    the light and makes it an ear; which loops belong to that face is remembered so the painter
    can make the inside run red.
    """
    import bmesh

    S = Vector(sweep).normalized()
    up = Vector((0, 0, 1))
    W = (up - S * up.dot(S)).normalized()          # across the ear, roughly upward
    N = S.cross(W) * side                          # out of its front face
    if N.y > 0:
        N = -N                                     # the dished side faces the way he does

    # (along, lower edge, upper edge, thickness) as fractions of the length.
    stations = [
        (0.00, -0.13, 0.12, 0.060), (0.10, -0.17, 0.17, 0.050), (0.24, -0.19, 0.21, 0.040),
        (0.34, -0.18, 0.15, 0.034), (0.40, -0.17, 0.20, 0.030), (0.55, -0.14, 0.20, 0.024),
        (0.72, -0.09, 0.16, 0.018), (0.88, -0.035, 0.09, 0.012), (1.00, 0.02, 0.03, 0.006),
    ]

    bm = bmesh.new()
    rows = []
    for u, lo, hi, thick in stations:
        mid = (lo + hi) / 2
        at = Vector(root) + S * (u * length)
        rows.append((
            bm.verts.new(at + W * (lo * length)),                                      # lower rim
            bm.verts.new(at + W * (hi * length)),                                      # upper rim
            bm.verts.new(at + W * (mid * length) - N * (thick * length * 0.9)),        # back, bowed out
            bm.verts.new(at + W * (mid * length) - N * (thick * length * 0.15)),       # front, dished in
        ))

    front = []
    for (l0, h0, b0, f0), (l1, h1, b1, f1) in zip(rows, rows[1:]):
        quads = [(l0, l1, b1, b0), (b0, b1, h1, h0)]
        dish = [(h0, h1, f1, f0), (f0, f1, l1, l0)]
        for q in quads + dish:
            face = bm.faces.new(q if side > 0 else tuple(reversed(q)))
            if q in dish:
                front.append(face)
    bm.faces.new((rows[0][0], rows[0][2], rows[0][1], rows[0][3]) if side > 0 else (rows[0][3], rows[0][1], rows[0][2], rows[0][0]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    mesh = bpy.data.meshes.new(name)
    front_indices = {f.index for f in front}
    bm.faces.ensure_lookup_table()
    bm.to_mesh(mesh)
    bm.free()

    ear = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(ear)
    smooth(ear)
    EAR_INNER[name] = {li for poly in mesh.polygons if poly.index in front_indices for li in poly.loop_indices}
    return ear


def create_head_parts(L, body):
    """What metaballs cannot hold: anything thin, sharp or bright."""
    r, hob, H, hs = L["r"], L["hob"], L["head"], L["hs"]
    parts = []

    def off(x, y, z):
        return tuple(H + Vector((x, y, z)) * hs)

    for side in (-1, 1):
        suffix = "L" if side < 0 else "R"

        # Out, back and up. The sweep is what separates a goblin's ears from a pair of horns.
        sweep = (side * 0.66, 0.42, 0.62) if not hob else (side * 0.68, 0.50, 0.50)
        parts.append(create_ear(f"Ear_{suffix}", off(side * 0.165, 0.01, -0.01), sweep, (0.36 if hob else 0.54) * hs, side))

        parts.append(ring(f"Earring_{suffix}", off(side * 0.27, 0.08, 0.02), 0.034 * hs, 0.010 * hs, GOLD, rotation=(math.pi / 2, 0, 0)))
        # Small, and set back under the brow. Big round eyes are what made him look friendly.
        parts.append(uv(f"Eye_{suffix}", off(side * 0.088, -0.188, 0.002), (0.025 * hs,) * 3, EYE, 12, 8))
        parts.append(uv(f"Pupil_{suffix}", off(side * 0.085, -0.209, 0.001), (0.010 * hs, 0.006 * hs, 0.013 * hs), PUPIL, 8, 6))

    parts += create_teeth(L, body)
    return parts


def tooth(name, root, direction, length, width, material, point=0.25, curl=0.15, flat=0.7):
    """One tooth: wide and flattened at the root, tapering to a blunt tip or, for a fang, a
    point, and curving a little. `point` is how much of the root width is left at the tip."""
    sections = []
    n = 5
    for i in range(n + 1):
        s = i / n
        w = width * (1.0 - (1.0 - point) * s ** 1.3)
        y = curl * length * s * s
        sections.append([Vector((w * math.cos(a), y + w * flat * math.sin(a), s * length)) for a in (k * 2 * math.pi / 8 for k in range(8))])
    sections.append([Vector((0, curl * length * 1.05, length * 1.03))] * 8)
    o = loft(name, sections, material)
    o.matrix_world = aim(root, direction, wide=(1, 0, 0))
    return o


def create_teeth(L, body):
    """Teeth set in the mouth, found by looking for the gums.

    Rays go up and down from inside the open mouth; where they meet flesh is the gum, and each
    tooth grows from just inside it toward the gap. So the teeth are always inside the lips,
    whatever the metaballs did to the face, and none floats in front of it. The two lower
    fangs stand at the corners and rise past the upper lip, as a goblin's do.
    """
    import random

    hs, H = L["hs"], L["head"]
    rnd = random.Random(4 if L["hob"] else 2)
    m = L["mouth"]
    parts = []

    def gum(x, y, upward):
        origin = Vector((x, y, m.z))
        hit, loc, _n, _i = body.ray_cast(origin, Vector((0, 0, 1 if upward else -1)), distance=0.2 * hs)
        return loc if hit else None

    # The front of the upper lip, from a ray coming in from in front of the face.
    hit, lip, _n, _i = body.ray_cast(Vector((0, m.y - 0.5, m.z + 0.03 * hs)), Vector((0, 1, 0)), distance=1.0)
    front = (lip.y if hit else m.y - 0.06 * hs) + 0.016 * hs

    # Uneven: a goblin's teeth are crooked, chipped and not all there.
    lower = []
    for row, upward, count, spacing, size in (("Upper", True, 7, 0.019, 1.0), ("Lower", False, 6, 0.018, 0.75)):
        for i in range(count):
            if rnd.random() < 0.12:
                continue                                     # one knocked out
            x = (i - (count - 1) / 2) * spacing * hs + rnd.uniform(-0.002, 0.002) * hs
            y = front + 1.6 * x * x / hs + rnd.uniform(0.0, 0.004) * hs
            g = gum(x, y, upward)
            if g is None:
                continue
            if not upward:
                lower.append(g)
            canine = upward and i in (1, count - 2)
            length = (0.032 if canine else rnd.uniform(0.014, 0.024)) * hs * size
            root = g + Vector((0, 0, 0.006 * hs if upward else -0.006 * hs))
            tilt = Vector((rnd.uniform(-0.25, 0.25), rnd.uniform(-0.30, 0.0), -1.0 if upward else 1.0))
            parts.append(tooth(f"Tusk_{row}_{i}", root, tilt, length, rnd.uniform(0.0075, 0.0095) * hs, TOOTH,
                               point=0.15 if canine else rnd.uniform(0.35, 0.6)))

    # The underbite: a fang at each corner, rooted in the lower gum, up past the upper lip.
    fang = 1.35 if L["hob"] else 1.0
    # Where the outermost lower teeth found gum is the corner of the mouth, near enough.
    for side in (-1, 1):
        if not lower:
            break
        g = max(lower, key=lambda p: p.x * side) + Vector((side * 0.006 * hs, 0.004 * hs, 0))
        parts.append(tooth(f"Tusk_{'L' if side < 0 else 'R'}", g - Vector((0, 0, 0.012 * hs)), (side * 0.22, -0.30, 1.0),
                           0.085 * hs * fang, 0.016 * hs * fang, TOOTH, point=0.2, curl=0.25))
    return parts


def create_crest(L, body, arm):
    """A goblin's crest of coarse hair down the middle of the skull, as cards."""
    H, hs = L["head"], L["hs"]
    info = cards.atlas("Goblin_Hair", {"hair": ((0.025, 0.018, 0.012), (0.16, 0.11, 0.07))}, size=512)

    def plan(p, n):
        q = p - H
        if abs(q.x) > 0.055 * hs or n.z < 0.15 or q.z < 0.05 * hs or q.y < -0.20 * hs or q.y > 0.26 * hs:
            return None
        t = (q.y + 0.20 * hs) / (0.46 * hs)                  # 0 at the brow, 1 at the nape
        return {"density": 1.0, "length": (0.07 + 0.08 * math.sin(t * math.pi)) * hs, "flow": (0, 1.0, 0.35),
                "palette": "hair", "lift": 55, "width": 0.45}

    crest = cards.grow(body, "Hair_Crest", info, plan, 140, seed=3, segments=3)
    cards.skin_like(crest, body, arm)
    return crest


def skin_material():
    m = bpy.data.materials.get("Goblin_Hide")
    if m:
        return m
    m = bpy.data.materials.new("Goblin_Hide")
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    col = m.node_tree.nodes.new("ShaderNodeVertexColor")
    col.layer_name = "Col"
    m.node_tree.links.new(col.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.78
    bsdf.inputs["Metallic"].default_value = 0.0
    return m


def paint_skin(parts, L):
    """Shade the hide: occlusion baked per vertex, times a tone that changes over the body.

    It stands in for the texture a sculpted model would have. The bake runs with the kit already
    on, so a strap casts its own grime onto the chest beneath it, and the whole result rides in
    the vertex colours — no image, no UVs, nothing for the importer to lose.
    """
    body = next(o for o in parts if o.name == "Body")
    ears = [o for o in parts if o.name.startswith("Ear_")]
    hide = skin_material()

    scene = bpy.context.scene
    engine = scene.render.engine
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 48
    scene.render.bake.target = "VERTEX_COLORS"

    # Olive going to yellow where the skin is thin, and weathered red-brown where it is worked:
    # shoulders, elbows, knees, knuckles. Sampled from the reference sheet rather than guessed.
    olive = Vector((0.345, 0.385, 0.125))
    pale = Vector((0.520, 0.500, 0.235))
    ruddy = Vector((0.430, 0.235, 0.120))

    if L["hob"]:
        # Grey going to slate, the colour of something that lives in a barracks rather than a
        # burrow. One look should say this is not another goblin.
        olive = Vector((0.300, 0.315, 0.250))
        pale = Vector((0.455, 0.450, 0.360))
        ruddy = Vector((0.330, 0.200, 0.160))
    flush = Vector((0.560, 0.235, 0.150))
    grime = Vector((0.150, 0.150, 0.070))

    hs = L["hs"]
    maw = Vector((0.13, 0.035, 0.03))
    mouth = L["mouth"]
    reach = Vector((0.125, 0.080, 0.034)) * hs * 1.35
    nose = L["head"] + Vector((0, -0.335, -0.05)) * hs
    face = L["head"] + Vector((0, -0.20, -0.04)) * hs
    chest = L["ribs"].lerp(L["belly"], 0.45)
    worn = [mirror(L[k], s) for k in ("shoulder", "elbow", "knee", "palm") for s in (-1, 1)]

    def near(v, point, radius):
        d = (v - point).length / radius
        return max(0.0, 1.0 - d * d)

    def mottle(v):
        # Smooth and low-frequency. Per-vertex randomness reads as sandpaper; hide is blotchy.
        a = math.sin(v.x * 9.1 + 1.3) * math.sin(v.y * 7.7 + 0.4) * math.sin(v.z * 8.3 + 2.2)
        b = math.sin(v.x * 21.0 + v.z * 17.0) * math.sin(v.y * 19.0 - v.z * 13.0)
        return a * 0.65 + b * 0.35

    for obj in [body] + ears:
        mesh = obj.data
        attr = mesh.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
        mesh.color_attributes.active_color = attr
        mesh.color_attributes.render_color_index = 0
        mesh.materials.clear()
        mesh.materials.append(hide)

        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        try:
            bpy.ops.object.bake(type="AO")
            baked = True
        except RuntimeError as exc:
            print(f"AO bake failed for {obj.name}: {exc}")
            baked = False

        inner = EAR_INNER.get(obj.name)
        world = obj.matrix_world
        root = L["head"]

        for poly in mesh.polygons:
            for li in poly.loop_indices:
                v = world @ mesh.vertices[mesh.loops[li].vertex_index].co
                ao = attr.data[li].color[0] if baked else 1.0
                tone = olive.copy()

                if inner is not None:
                    along = min(1.0, (v - root).length / (0.75 * hs))
                    tone = tone.lerp(flush, 0.80 if li in inner else 0.18 + 0.30 * along)
                else:
                    front = max(0.0, min(1.0, (chest.y - v.y) * 5.0)) * max(0.0, 1 - abs(v.z - chest.z) * 2.2)
                    tone = tone.lerp(pale, front * 0.80 + near(v, face, 0.26 * hs) * 0.45)
                    tone = tone.lerp(ruddy, max(near(v, p, 0.19 * L["r"]) for p in worn) * 0.55)
                    tone = tone.lerp(flush, near(v, nose, 0.10 * hs) * 0.55)
                    tone = tone.lerp(ruddy, max(0.0, mottle(v)) * 0.30)
                    tone = tone.lerp(grime, max(0.0, 1.0 - v.z / (0.10 * L["r"])) * 0.55)
                    # Inside the mouth: dark and wet-red, fading out at the lips.
                    d = Vector(((v.x - mouth.x) / reach.x, (v.y - mouth.y) / reach.y, (v.z - mouth.z) / reach.z)).length
                    if d < 1.0:
                        tone = maw.lerp(tone, max(0.0, d - 0.70) / 0.30)

                tone *= 1.0 + mottle(v * 1.7) * 0.07
                shade = 0.10 + 0.90 * (ao ** 2.4)
                attr.data[li].color = (tone.x * shade, tone.y * shade, tone.z * shade, 1.0)

    scene.render.engine = engine


def dome(name, loc, scale, material, keep_above=0.0, segments=20, rings=12):
    """The top of a sphere: helmets, pauldrons, caps.

    keep_above is a fraction of the dome's own half-height, -1 for the whole sphere and 0 for
    the upper half. Left open underneath, which costs nothing: whatever wears it fills the hole
    and back faces are culled anyway.
    """
    import bmesh

    o = uv(name, loc, scale, material, segments, rings)
    bm = bmesh.new()
    bm.from_mesh(o.data)
    cut = keep_above * scale[2]
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < cut - 1e-5], context="VERTS")
    bm.to_mesh(o.data)
    bm.free()
    return o


def ring(name, loc, major, minor, material, rotation=(0, 0, 0), scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_torus_add(
        location=loc, rotation=rotation, major_radius=major, minor_radius=minor,
        major_segments=28, minor_segments=8,
    )
    o = bpy.context.object
    o.name = name
    o.scale = scale
    smooth(o)
    apply_mat(o, material)
    return o


def blade(name, base, length, width, material, thickness=0.2):
    """A tapering diamond-section blade rising from base, rather than a plank."""
    bpy.ops.mesh.primitive_cone_add(
        vertices=4, radius1=width, radius2=width * 0.12, depth=length,
        location=(base[0], base[1], base[2] + length / 2),
    )
    o = bpy.context.object
    o.name = name
    o.scale = (1.0, thickness, 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    apply_mat(o, material)
    return o


def loft(name, sections, material, cap_start=True, cap_end=True, smooth_shading=True):
    """A skin over a run of cross-sections: a blade, a limb, a stock, a plank with an edge.

    sections is a list of rings, each a list of points in order round the ring, every ring
    the same length. Consecutive rings are joined with quads; a ring may collapse to a point
    (all entries equal) for a tip, and the duplicate vertices are merged away.
    """
    import bmesh

    bm = bmesh.new()
    rings = [[bm.verts.new(tuple(p)) for p in ring] for ring in sections]
    count = len(sections[0])
    for a, b in zip(rings, rings[1:]):
        for i in range(count):
            j = (i + 1) % count
            quad = (a[i], a[j], b[j], b[i])
            if len({v.co[:] for v in quad}) >= 3:
                bm.faces.new(quad)
    if cap_start and len({v.co[:] for v in rings[0]}) >= 3:
        bm.faces.new(list(reversed(rings[0])))
    if cap_end and len({v.co[:] for v in rings[-1]}) >= 3:
        bm.faces.new(rings[-1])
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    o = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(o)
    if smooth_shading:
        smooth(o)
    apply_mat(o, material)
    return o


def tuft(name, base, direction, length, width, material, curl=0.45, wide=(1, 0, 0)):
    """One lock of hair: a flat leaf, broad at the root, curling as it goes, ending in a point.

    Lofted rather than a cone, because a cone is a spike and a head of spikes is a hedgehog.
    The leaf bends in its own Y, which `aim` sets perpendicular to both the direction and `wide`.
    """
    sections = []
    n = 4
    for i in range(n + 1):
        s = i / n
        w = width * (1.0 - s) ** 0.75
        t = width * 0.20 * (1.0 - s) + 0.002
        y = curl * length * s * s
        z = s * length
        sections.append([Vector((w, y - t, z)), Vector((w, y + t, z)), Vector((-w, y + t, z)), Vector((-w, y - t, z))])
    sections.append([Vector((0, curl * length * 1.1, length * 1.08))] * 4)
    o = loft(name, sections, material)
    o.matrix_world = aim(base, direction, wide=wide)
    return o


def tusk(name, root, direction, length, radius, material, curl=0.35):
    """A tooth: round at the root, curving, blunt at the tip. A cone was a thorn."""
    sections = []
    n = 5
    for i in range(n + 1):
        s = i / n
        rr = radius * (1.0 - 0.85 * s ** 1.4)
        y = curl * length * s * s
        sections.append([Vector((rr * math.cos(a), y + rr * 0.8 * math.sin(a), s * length)) for a in (k * 2 * math.pi / 8 for k in range(8))])
    sections.append([Vector((0, curl * length * 1.05, length * 1.02))] * 8)
    o = loft(name, sections, material)
    o.matrix_world = aim(root, direction, wide=(1, 0, 0))
    return o


def kit(L):
    """The torso as the kit sees it: a leaning ellipsoid, fitted to the body's own landmarks."""
    r = L["r"]
    tall = (L["neck_a"].z - L["pelvis"].z) / 0.58
    return {
        "c": L["pelvis"].lerp(L["neck_a"], 0.5) + Vector((0, -0.012, 0.02)),
        "s": Vector((0.285 * r * L["bulk"], 0.25 * r * L["bulk"], 0.37 * tall)),
        "lean": L["lean"],
        "r": r,
        "bulk": L["bulk"],
        "L": L,
    }


TORSO_BONES = ("pelvis", "spine", "chest")


def torso_piece(L, threshold=0.5, waist=0.0):
    """The body's own skin over the torso, as a bmesh: the faces its torso bones carry, cut clean
    at the waist and below the neck, the largest piece kept, its ragged armholes smoothed.

    Armour cut from this fits by construction. The shells it replaces were ellipsoids placed
    by landmark, and however they were scaled, the shoulder blades and traps came through them
    in one place while they stood off the ribs in another.
    """
    import bmesh

    body = L["body"]
    bm = bmesh.new()
    bm.from_mesh(body.data)

    # Which bone each bit of skin is nearest, by distance to the bones as segments, with the
    # collarbones counted as torso: the armhole then falls round the shoulder joint, where a
    # vest's does. Bone weights were tried first and gave the shoulder blades to the arms.
    M = mirror
    torso = [(L["pelvis"], L["belly"]), (L["belly"], L["ribs"]), (L["ribs"], L["neck_a"]), (L["back"], L["neck_a"])]
    torso += [(L["neck_a"], M(L["shoulder"], s).lerp(L["neck_a"], 0.15)) for s in (-1, 1)]
    limbs = [(M(L[a], s), M(L[b], s)) for s in (-1, 1) for a, b in (("shoulder", "elbow"), ("elbow", "wrist"), ("hip", "knee"), ("knee", "ankle"))]
    limbs += [(L["neck_b"], L["head"])]

    def reach(p, segments):
        best = 1e9
        for a, b in segments:
            ab = b - a
            t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
            best = min(best, (p - (a + ab * t)).length)
        return best

    bias = 0.75 + threshold          # under 1: the torso wins ties, and a little more
    drop = [f for f in bm.faces if reach(f.calc_center_median(), torso) * bias > reach(f.calc_center_median(), limbs)]
    bmesh.ops.delete(bm, geom=drop, context="FACES_ONLY")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")

    spine = (L["neck_a"] - L["pelvis"]).normalized()
    neck = (L["neck_b"] - L["neck_a"]).normalized()
    for co, no in ((L["belly"].lerp(L["pelvis"], waist), spine), (L["neck_a"] - neck * (0.02 * L["r"]), -neck)):
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no, clear_inner=True)

    # The largest connected piece: crumbs of skin the weights left behind are not armour.
    bm.faces.ensure_lookup_table()
    seen, best = set(), []
    for f in bm.faces:
        if f in seen:
            continue
        island, todo = [], [f]
        seen.add(f)
        while todo:
            g = todo.pop()
            island.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h not in seen:
                        seen.add(h)
                        todo.append(h)
        if len(island) > len(best):
            best = island
    keep = set(best)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f not in keep], context="FACES_ONLY")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")

    # Ease the armholes the weights cut: the boundary pulled toward its neighbours a few times.
    for _ in range(4):
        moves = {}
        for v in bm.verts:
            rim = [e.other_vert(v) for e in v.link_edges if e.is_boundary]
            if len(rim) == 2:
                moves[v] = v.co * 0.5 + (rim[0].co + rim[1].co) * 0.25
        for v, co in moves.items():
            v.co = co
    bm.normal_update()
    return bm


def armour_lift(k):
    """How far armour stands off the skin, and how thick it is."""
    r = k["r"] * k["bulk"]
    return 0.010 * r, 0.022 * r


def fitted_shell(name, piece, offset, thickness, material):
    """A copy of a piece of skin pushed out along its normals and given thickness."""
    import bmesh

    bm = piece.copy()
    bm.normal_update()
    for v in bm.verts:
        v.co += v.normal * offset
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    smooth(obj)
    apply_mat(obj, material)
    mod = obj.modifiers.new("Thick", "SOLIDIFY")
    mod.thickness = thickness
    mod.offset = 1.0
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier="Thick")
    return obj


def ribbon(name, points, normals, width, thickness, material, closed=True):
    """A flat band lying on a surface along a path: a strap, a belt, a binding."""
    import bmesh

    bm = bmesh.new()
    n = len(points)
    rows = []
    for i, (p, nor) in enumerate(zip(points, normals)):
        a, b = points[(i - 1) % n] if closed or i > 0 else p, points[(i + 1) % n] if closed or i < n - 1 else p
        t = (b - a).normalized()
        side = t.cross(nor).normalized()
        rows.append((bm.verts.new(p - side * width / 2), bm.verts.new(p + side * width / 2)))
    last = n if closed else n - 1
    for i in range(last):
        a0, b0 = rows[i]
        a1, b1 = rows[(i + 1) % n]
        bm.faces.new((a0, b0, b1, a1))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    apply_mat(obj, material)
    mod = obj.modifiers.new("Thick", "SOLIDIFY")
    mod.thickness = thickness
    mod.offset = 1.0
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier="Thick")
    return obj


def onto(bvh, p, lift):
    """The point on the skin nearest p, lifted off it, and the skin's normal there."""
    loc, nor, _i, _d = bvh.find_nearest(p)
    return loc + nor * lift, nor


def skin_from_body(obj, body, arm):
    """Weight every vertex like the body vertex nearest it, so the piece bends with the skin."""
    from mathutils import kdtree

    tree = kdtree.KDTree(len(body.data.vertices))
    for v in body.data.vertices:
        tree.insert(v.co, v.index)
    tree.balance()
    bones = {b.name for b in arm.data.bones}
    names = {g.index: g.name for g in body.vertex_groups if g.name in bones}
    world = obj.matrix_world
    for v in obj.data.vertices:
        _co, idx, _d = tree.find(world @ v.co)
        for g in body.data.vertices[idx].groups:
            if g.group in names and g.weight > 0:
                vg = obj.vertex_groups.get(names[g.group]) or obj.vertex_groups.new(name=names[g.group])
                vg.add([v.index], g.weight, "REPLACE")
    mod = obj.modifiers.get("Armature") or obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    obj.parent = arm


def lean_matrix(k):
    return Matrix.Translation(k["c"]) @ Matrix.Rotation(k["lean"], 4, "X")


def torso_pt(k, x, y, z):
    return lean_matrix(k) @ Vector((x, y, z))


def on_torso(k, angle, dz, lift=1.0, grow=1.0):
    """A point on the torso shell, angle measured round the body from the front."""
    sc = k["s"] * grow
    squeeze = math.sqrt(max(0.0, 1.0 - (dz / sc.z) ** 2))
    return torso_pt(k, sc.x * math.sin(angle) * squeeze * lift, -sc.y * math.cos(angle) * squeeze * lift, dz)


def place(objs, matrix):
    """Move a group built at the origin to where it belongs, as one rigid piece."""
    bpy.context.view_layer.update()
    for o in objs:
        o.matrix_world = matrix @ o.matrix_world
    return objs


def aim(origin, direction, wide=Vector((0, 1, 0))):
    """A matrix whose Z runs along direction and whose X lies as near to `wide` as it can."""
    z = Vector(direction).normalized()
    x = (Vector(wide) - z * Vector(wide).dot(z)).normalized()
    y = z.cross(x)
    m = Matrix((x, y, z)).transposed().to_4x4()
    m.translation = Vector(origin)
    return m


def at_idle(direction, swing):
    """Where to point something at rest so that it points `direction` once the guard is up.

    Nobody ever sees the rest pose: the game plays the idle from the first frame. So held things
    are aimed for the idle and wound back by however far the arm swings to get there.
    """
    # Swinging an arm forward is a *negative* turn about world X (down becomes -Y), so undoing
    # it is a positive one. Getting this backwards lays the cleaver across his belt.
    return Matrix.Rotation(swing, 3, "X") @ Vector(direction)


RIGHT_SWING = 1.05
LEFT_SWING = 1.20


def grips(variant, L):
    """Where each hand closes, and round what: one place for the fist and the thing in it.

    For each side: the middle of the grip `c`, its axis `a` (toward the blade, the thumb end),
    the knuckles' direction `k`, the back of the hand `j`, and the grip's radius `rg`. Worked
    out in the guard pose, where the forearm is level and pointing at the enemy, and wound back
    to the rest pose by `at_idle`, because the guard is the only pose anybody sees.

    The axis is kept square to the forearm. A blade laid along the hand is a knife on a plate,
    which is what the open-palmed version looked like; a fist holds a grip across it.
    """
    r = L["r"]
    out = {}
    for side in (1, -1):
        swing = RIGHT_SWING if side > 0 else LEFT_SWING
        wr, pa = mirror(L["wrist"], side), mirror(L["palm"], side)
        k = (pa - wr).normalized()
        k_idle = Matrix.Rotation(-swing, 3, "X") @ k
        if variant == "goblin-archer" and side < 0:
            want, rg = Vector((0.0, 0.0, 1.0)), 0.046 * r            # the bow, upright
        elif variant != "goblin-archer" and side > 0:
            want, rg = Vector((0.30, -0.20, 0.93)), 0.034 * r         # a blade, up and out
        else:
            want, rg = Vector((side * 0.25, 0.0, 1.0)), 0.020 * r     # closed on a strap, or nothing
        want = (want - k_idle * want.dot(k_idle)).normalized()
        a = at_idle(want, swing).normalized()
        a = (a - k * a.dot(k)).normalized()
        j = (a.cross(k) * side).normalized()                        # right-handed for the right hand
        # The grip lies in the palm: below the back of the hand, a little short of the knuckles.
        c = pa + k * (0.012 * r) - j * (0.030 * r)
        out[side] = {"c": c, "a": a, "k": k, "j": j, "rg": rg}
    return out


def fist(ball, chain, g, wrist, r):
    """A hand closed round a grip, in metaballs, so it melts into the wrist like the rest.

    In the plane square to the grip: the back of the hand lies over it, the four fingers leave
    the knuckle row, go down in front of it and curl back underneath to the heel of the palm,
    and the thumb comes round the other way over the index finger. A grip through the middle
    is then inside flesh on every side, which is what reads as *held*.
    """
    c, a, k, j, rg = g["c"], g["a"], g["k"], g["j"], g["rg"]
    finger = 0.031 * r
    ring = rg + finger * 0.95
    step = 0.036 * r

    def at(along, degrees, radius=ring):
        t = math.radians(degrees)
        return c + a * along + (k * math.cos(t) + j * math.sin(t)) * radius

    # The back of the hand: wrist to knuckles, broad across the fingers, over the grip.
    top = c + j * (rg + 0.028 * r)
    for along in (-1.1 * step, 0.0, 1.1 * step):
        chain(wrist + a * (along * 0.6), top + a * along + k * (0.020 * r), 0.050 * r, 0.040 * r, 3)
    ball(c - k * (0.030 * r) - j * (0.005 * r) + a * (0.6 * step), 0.045 * r)       # heel of the thumb
    ball(c - k * (0.035 * r) - j * (0.010 * r) - a * (1.0 * step), 0.040 * r)       # heel of the palm

    # Four fingers, index nearest the blade, each a little smaller than the last.
    for i in range(4):
        along = (1.5 - i) * step
        shrink = 1.0 - 0.07 * i
        path = [at(along, 70), at(along, 15), at(along, -45), at(along, -105), at(along, -150, ring * 0.92)]
        for p, q in zip(path, path[1:]):
            chain(p, q, finger * shrink, finger * shrink * 0.94, 2)
        ball(at(along, 62, ring + 0.010 * r), finger * shrink * 1.15)             # the knuckle

    # The thumb: from its heel, round the far side of the grip, and over the index finger.
    along = 2.3 * step
    path = [c - k * (0.030 * r) + a * (1.2 * step), at(along, 200), at(along, 250), at(along, 300, ring * 1.05), at(along * 0.85, 335, ring * 1.12)]
    for p, q in zip(path, path[1:]):
        chain(p, q, finger * 1.10, finger * 1.0, 2)


def create_cuirass(k, material, grow=1.06, waist=0.15):
    """A vest cut from the body's own skin over the torso, stood off it and given thickness.

    `grow` is kept for the callers' sake and means only "armour, not hide": the fit comes from
    the skin, not from scaling a shape until it stops poking through.
    """
    from mathutils.bvhtree import BVHTree

    piece = torso_piece(k["L"], 0.25, waist)
    offset, thick = armour_lift(k)
    k["armour"] = BVHTree.FromBMesh(piece)
    k["armour_top"] = offset + thick
    shell = fitted_shell("Cuirass", piece, offset, thick, material)
    piece.free()
    return [shell]


def skin_bvh(k):
    """The torso's skin to lay things on: the armour if there is any, the hide if not."""
    from mathutils.bvhtree import BVHTree

    if "armour" in k:
        return k["armour"], k["armour_top"]
    piece = torso_piece(k["L"], 0.35, 0.3)
    bvh = BVHTree.FromBMesh(piece)
    piece.free()
    return bvh, 0.0


def create_studs(k, grow=1.10):
    bvh, top = skin_bvh(k)
    studs = []
    for row, dz in enumerate((-0.19, 0.0, 0.19)):
        for i in range(10):
            angle = (i + 0.5 * (row % 2)) * (2 * math.pi / 10)
            at, _n = onto(bvh, on_torso(k, angle, dz * k["s"].z / 0.37, 1.01, grow), top + 0.004 * k["r"])
            studs.append(uv(f"Stud_{row}_{i}", tuple(at), (0.026 * k["r"],) * 3, METAL, 8, 5))
    return studs


def create_straps(k, material=LEATHER, crossed=True, grow=1.0):
    """Crossed belts over the chest and back — on bare hide for the goblin, over armour for the
    others. Each is a band laid along the skin, so it lies flat on the shoulder blades and dips
    into the small of the back instead of cutting through one and floating over the other."""
    bvh, top = skin_bvh(k)
    sc = k["s"] * grow
    r = k["r"]
    lift = top + 0.006 * r
    parts = []
    for i, tilt in enumerate((50, -50) if crossed else (50,)):
        turn = lean_matrix(k) @ Matrix.Rotation(math.radians(tilt), 4, "Y")
        points, normals = [], []
        for j in range(72):
            t = j / 72 * 2 * math.pi
            guess = turn @ Vector((sc.x * 1.04 * math.cos(t), sc.y * 1.07 * math.sin(t), 0))
            at, nor = onto(bvh, guess, lift)
            points.append(at)
            normals.append(nor)
        parts.append(ribbon(f"Strap_{i}", points, normals, 0.060 * r, 0.014 * r, material))
    if crossed:
        at, nor = onto(bvh, on_torso(k, 0.0, 0.0, 1.05, grow), lift + 0.008 * r)
        buckle = ring("Strap_Ring", (0, 0, 0), 0.05 * r, 0.014 * r, IRON)
        buckle.matrix_world = Matrix.Translation(at) @ nor.to_track_quat("Z", "Y").to_matrix().to_4x4()
        parts.append(buckle)
    return parts


def create_belt(L, k, heavy=False):
    r = k["r"]
    at = L["pelvis"] + Vector((0, 0, 0.07 * r))
    belt = cyl("Belt", (0, 0, 0), 0.262 * r * k["bulk"], (0.135 if heavy else 0.10) * r, LEATHER, vertices=24)
    belt.scale = (1.0, 0.80, 1.0)
    parts = [belt,
             cube("Buckle", (0, -0.215 * r * k["bulk"], 0), (0.075 * r, 0.02, 0.07 * r), IRON if heavy else GOLD, 0.01),
             cube("Buckle_Tongue", (0, -0.232 * r * k["bulk"], 0), (0.045 * r, 0.012, 0.042 * r), LEATHER, 0.006),
             cube("Pouch", (0.17 * r, 0.155 * r, -0.075 * r), (0.075 * r, 0.05 * r, 0.085 * r), LEATHER_LIGHT, 0.025),
             cube("Pouch_Flap", (0.17 * r, 0.20 * r, -0.04 * r), (0.072 * r, 0.014, 0.04 * r), LEATHER, 0.01),
             cube("Pouch_Small", (-0.17 * r, 0.16 * r, -0.065 * r), (0.055 * r, 0.045 * r, 0.065 * r), LEATHER_LIGHT, 0.02)]
    if heavy:
        second = cyl("Belt_Second", (0, 0, -0.085 * r), 0.258 * r * k["bulk"], 0.045 * r, LEATHER_LIGHT, vertices=24)
        second.scale = (1.0, 0.82, 1.0)
        second.rotation_euler = (0, math.radians(7), 0)
        parts.append(second)
    return place(parts, Matrix.Translation(at) @ Matrix.Rotation(k["lean"] * 0.5, 4, "X"))


def rag(name, top, length, width, material, swing=0.0):
    """A strip of torn hide hanging from its top edge, ending in a point."""
    bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=width * 0.10, radius2=width, depth=length)
    o = bpy.context.object
    o.name = name
    o.scale = (1.0, 0.10, 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    for v in o.data.vertices:
        v.co.z -= length / 2
    o.location = top
    o.rotation_euler = (swing, 0, 0)
    apply_mat(o, material)
    return o


def create_loincloth(L, k, material=CLOTH):
    """Ragged, in strips of different lengths, swung clear of the crouching thighs."""
    r = k["r"]
    hang = L["pelvis"] + Vector((0, 0, 0.03 * r))
    parts = []
    for i, (dx, length) in enumerate(((-0.10, 0.30), (0.0, 0.40), (0.10, 0.27), (-0.05, 0.22), (0.055, 0.34))):
        parts.append(rag(f"Loincloth_F{i}", hang + Vector((dx * r, -0.20 * r - 0.004 * i, 0)), length * r, 0.080 * r, LEATHER_LIGHT if i < 3 else LEATHER, -0.42))
    for i, (dx, length) in enumerate(((-0.11, 0.36), (0.0, 0.46), (0.11, 0.33), (0.05, 0.25))):
        parts.append(rag(f"Loincloth_B{i}", hang + Vector((dx * r, 0.17 * r + 0.004 * i, 0)), length * r, 0.085 * r, LEATHER_LIGHT if i < 3 else LEATHER, 0.10))
    return parts


def create_wraps(L, k, arm_material, leg_material, knees=None, greaves=False, banded=False):
    """Forearms and shins: plate for the sergeant, and for the rest strips of hide wound round
    in separate bands, which is what the reference wears and reads far better than a tube."""
    r = k["r"] * k["bulk"]
    parts = []
    spans = ((0.26, 0.44), (0.48, 0.66), (0.70, 0.90)) if banded else ((0.30, 0.92),)

    for side in (-1, 1):
        suffix = "L" if side < 0 else "R"
        el, wr, kn, an = (mirror(L[key], side) for key in ("elbow", "wrist", "knee", "ankle"))

        for i, (a, b) in enumerate(spans):
            material = arm_material if i % 2 == 0 else LEATHER_LIGHT if banded else arm_material
            parts.append(limb(f"Bracer_{suffix}_{i}", el.lerp(wr, a), el.lerp(wr, b), (0.094 - 0.009 * i) * r, material))

        if greaves:
            parts.append(limb(f"Boot_{suffix}", kn.lerp(an, 0.12), kn.lerp(an, 0.97), 0.084 * r, leg_material))
        else:
            for i, (a, b) in enumerate(spans):
                material = leg_material if i % 2 == 0 else LEATHER if banded else leg_material
                parts.append(limb(f"Boot_{suffix}_{i}", kn.lerp(an, a + 0.04), kn.lerp(an, min(0.97, b + 0.06)), (0.082 - 0.008 * i) * r, material))

        if knees:
            parts.append(dome(f"KneePad_{suffix}", tuple(kn + Vector((0, -0.075 * r, 0.0))), (0.085 * r, 0.06 * r, 0.085 * r), knees, -0.5, 12, 8))
    return parts


def create_pauldrons(L, k, material, sides=(1,), spikes=True, size=1.0, layered=False):
    r = k["r"]
    parts = []
    for side in sides:
        suffix = "L" if side < 0 else "R"
        at = mirror(L["shoulder"], side) + Vector((side * 0.025, 0.0, 0.04)) * r
        w = 0.175 * r * size
        parts.append(dome(f"Pauldron_{suffix}", tuple(at), (w, w * 1.08, 0.135 * r * size), material, -0.30, 16, 10))
        parts.append(ring(f"Pauldron_{suffix}_Rim", tuple(at + Vector((0, 0, -0.04 * r * size))), w * 0.96, 0.017 * r, LEATHER, scale=(1.0, 1.08, 1.0)))

        if layered:
            # A second plate lapped over the first, and the rivets that hold the pair together.
            parts.append(dome(f"Pauldron_{suffix}_Cap", tuple(at + Vector((side * 0.01, 0, 0.045)) * r * size), (w * 0.74, w * 0.80, 0.105 * r * size), material, -0.10, 14, 8))
            for j in range(7):
                a = j * 2 * math.pi / 7
                parts.append(uv(f"Pauldron_{suffix}_Rivet_{j}", tuple(at + Vector((math.cos(a) * w * 0.86, math.sin(a) * w * 0.93, 0.015 * r))), (0.017 * r,) * 3, IRON_EDGE, 6, 4))

        if spikes:
            for j, (dx, dy) in enumerate(((0.0, 0.0), (0.075, -0.075), (0.075, 0.085))):
                lean_out = side * math.radians(16 + 24 * (j > 0))
                parts.append(cone(f"Spike_{suffix}_{j}", tuple(at + Vector((side * dx, dy, 0.17 - 0.045 * (j > 0))) * r * size), 0.036 * r, 0.003, 0.14 * r, IRON_EDGE if layered else material, (0, lean_out, 0), 8))
    return parts


def create_necklace(L, k):
    r = k["r"]
    at = L["neck_a"].lerp(L["neck_b"], 0.15) + Vector((0, -0.02 * r, -0.04 * r))
    cord = ring("Strap_Necklace", (0, 0, 0), 0.155 * r, 0.011 * r, LEATHER, scale=(1.0, 1.15, 1.0))
    parts = [cord]
    for i, a in enumerate((-0.7, -0.35, 0.0, 0.35, 0.7)):
        fang = cone(f"Strap_Fang_{i}", (0.155 * r * math.sin(a), -0.178 * r * math.cos(a), -0.04 * r), 0.016 * r, 0.002, (0.085 - 0.02 * abs(a)) * r, TOOTH, (math.pi, 0, 0), 6)
        parts.append(fang)
    return place(parts, Matrix.Translation(at) @ Matrix.Rotation(k["lean"] + 0.55, 4, "X"))


def create_blade(prefix, L, k, length, width, cleaver=False):
    """Built upright at the origin, then put in the right hand aimed for the guard pose."""
    r = k["r"]
    parts = [
        cyl(f"{prefix}_Grip", (0, 0, 0), 0.032 * r, 0.20 * r, LEATHER, vertices=10),
        uv(f"{prefix}_Pommel", (0, 0, -0.115 * r), (0.042 * r,) * 3, GOLD if not cleaver else METAL, 10, 6),
        cube(f"{prefix}_Guard", (0, 0, 0.105 * r), (0.11 * r, 0.028 * r, 0.018 * r), METAL, 0.006),
        blade(f"{prefix}_Blade", (0, 0, 0.115 * r), length * r, width * r, METAL, 0.16),
    ]
    if cleaver:
        # A chopper's heavy belly, and bites out of the edge where it has met other blades.
        parts.append(blade(f"{prefix}_Belly", (0.035 * r, 0, 0.30 * r), length * 0.55 * r, width * 0.95 * r, METAL, 0.15))
        for j in range(3):
            parts.append(cone(f"{prefix}_Notch_{j}", (width * 0.80 * r, 0, (0.26 + 0.12 * j) * r), 0.035 * r, 0.002, 0.07 * r, METAL, (0, math.radians(90), 0), 4))
    g = L["grip"][1]
    return place(parts, aim(g["c"], g["a"], wide=g["k"]))


def create_cleaver(L, k):
    """A broad single-edged chopper, widest near the tip, its edge bitten by other men's armour.

    Cut from an outline rather than assembled, because the shape *is* the weapon: a symmetric
    blade is a sword whatever you call it, and the reference carries a butcher's tool.
    """
    import bmesh

    r = k["r"]
    # (toward the edge, up the blade). Spine first, over the clipped tip, back down the edge.
    outline = [
        (-0.030, 0.12), (-0.036, 0.50), (-0.022, 0.76), (0.040, 0.83),
        (0.112, 0.72), (0.092, 0.685), (0.126, 0.62), (0.104, 0.585), (0.132, 0.52),
        (0.110, 0.475), (0.122, 0.41), (0.096, 0.34), (0.080, 0.26), (0.054, 0.18), (0.036, 0.12),
    ]
    spine = 4          # how many of those points are the blunt back

    bm = bmesh.new()
    near, far = [], []
    for i, (x, z) in enumerate(outline):
        thick = (0.011 if i < spine else 0.003) * r
        near.append(bm.verts.new((x * r, -thick, z * r)))
        far.append(bm.verts.new((x * r, thick, z * r)))
    bm.faces.new(near)
    bm.faces.new(list(reversed(far)))
    count = len(outline)
    for i in range(count):
        j = (i + 1) % count
        bm.faces.new((near[j], near[i], far[i], far[j]))
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new("Sword_Blade")
    bm.to_mesh(mesh)
    bm.free()
    chopper = bpy.data.objects.new("Sword_Blade", mesh)
    bpy.context.collection.objects.link(chopper)
    apply_mat(chopper, IRON_EDGE)

    parts = [
        chopper,
        cyl("Sword_Grip", (0, 0, 0), 0.034 * r, 0.22 * r, LEATHER, vertices=10),
        ring("Sword_Grip_Wrap", (0, 0, 0.03 * r), 0.036 * r, 0.010 * r, LEATHER_LIGHT),
        uv("Sword_Pommel", (0, 0, -0.125 * r), (0.04 * r,) * 3, IRON, 10, 6),
        cube("Sword_Guard", (0.01 * r, 0, 0.115 * r), (0.075 * r, 0.026 * r, 0.016 * r), IRON, 0.006),
    ]

    # Through the fist, edge toward the knuckles, which is how a chopper is held.
    g = L["grip"][1]
    return place(parts, aim(g["c"], g["a"], wide=g["k"]))


def create_shield(name, L, k, radius, spiked=False, material=WOOD):
    """Built facing forward at the origin, then strapped to the outside of the left forearm."""
    r = k["r"]
    face = (math.pi / 2, 0, 0)
    iron = IRON if spiked else METAL
    parts = [
        cyl(name, (0, 0, 0), radius, 0.055 * r, material, face, 24),
        ring(f"{name}_Rim", (0, 0, 0), radius, 0.032 * r, iron, face),
        ring(f"{name}_Band", (0, -0.022 * r, 0), radius * 0.58, 0.015 * r, iron, face),
        dome(f"{name}_Boss", (0, -0.015 * r, 0), (radius * 0.30,) * 3, iron, 0.0, 14, 8),
    ]
    parts[-1].rotation_euler = (math.pi / 2, 0, 0)

    # The seams between the planks. A disc of plain brown is a lid; these make it a shield.
    for j in range(4):
        seam = cube(f"{name}_Seam_{j}", (0, -0.029 * r, 0), (radius * 0.93, 0.004, 0.007 * r), SEAM)
        seam.rotation_euler = (0, j * math.pi / 4 + 0.2, 0)
        parts.append(seam)

    if not spiked:
        band = cube(f"{name}_Paint", (0, -0.0305 * r, 0), (radius * 0.90, 0.003, 0.055 * r), CLOTH_RED)
        band.rotation_euler = (0, math.radians(38), 0)
        parts.append(band)

    studs = 10 if spiked else 8
    for j in range(studs):
        a = j * 2 * math.pi / studs
        parts.append(uv(f"{name}_Rivet_{j}", (radius * 0.80 * math.cos(a), -0.035 * r, radius * 0.80 * math.sin(a)), (0.021 * r,) * 3, IRON_EDGE if spiked else METAL, 8, 5))
        if spiked:
            tip = Vector((math.cos(a), 0, math.sin(a)))
            spike = cone(f"{name}_Spike_{j}", (0, 0, 0), 0.036 * r, 0.003, 0.10 * r, IRON_EDGE, vertices=6)
            spike.matrix_world = aim(tip * (radius + 0.035 * r), tip)
            parts.append(spike)

    # Held by its strap in the left fist, board in front of the knuckles: the fist is behind the
    # boss, where a hand on a shield is, and not through the middle of the planks. Built facing
    # -Y; aim() makes Y = Z x X, so X = a x k turns that face the way the knuckles point, which
    # in the guard pose is at the enemy.
    g = L["grip"][-1]
    stand_off = g["rg"] + 0.10 * r
    return place(parts, aim(g["c"] + g["k"] * stand_off, g["a"], wide=g["a"].cross(g["k"])))


def create_bow(L, k):
    """A shortbow a person can see: shin to shoulder, belly forward, in the left hand, and
    aimed so that it stands upright once the guard is up."""
    r = k["r"]
    reach, belly = 0.62 * r, 0.24 * r

    curve = bpy.data.curves.new("ShortbowCurve", "CURVE")
    curve.dimensions = "3D"
    curve.bevel_depth = 0.034 * r
    curve.bevel_resolution = 2
    spline = curve.splines.new("BEZIER")
    spline.bezier_points.add(4)
    pts = [(0, belly - 0.05, -reach), (0, belly * 0.45, -reach * 0.55), (0, 0, 0),
           (0, belly * 0.45, reach * 0.55), (0, belly - 0.05, reach)]
    for bp, co in zip(spline.bezier_points, pts):
        bp.co = co
        bp.handle_left_type = "AUTO"
        bp.handle_right_type = "AUTO"
    bow = bpy.data.objects.new("Shortbow", curve)
    bpy.context.collection.objects.link(bow)
    apply_mat(bow, BOW_WOOD)

    # The object, not the datablock: a bpy.types.Curve has no .data to assign to.
    string = bow.copy()
    string.data = bow.data.copy()
    string.name = "Shortbow_String"
    string.data.materials.clear()
    string.data.bevel_depth = 0.009 * r
    string.data.splines.clear()
    sp = string.data.splines.new("POLY")
    sp.points.add(1)
    sp.points[0].co = (*pts[0], 1)
    sp.points[1].co = (*pts[-1], 1)
    bpy.context.collection.objects.link(string)
    apply_mat(string, BOW_STRING)

    wrap = cyl("Shortbow_Grip", (0, 0, 0), 0.046 * r, 0.16 * r, LEATHER, vertices=10)
    g = L["grip"][-1]
    # The string is on the bow's +Y side and the archer is behind the string, so its -Y faces
    # the way the knuckles do: aim() makes Y = Z x X, and X = a x k gives Y = -k.
    return place([bow, string, wrap], aim(g["c"], g["a"], wide=g["a"].cross(g["k"])))


def create_quiver(k):
    r, sc = k["r"], k["s"]
    lean = math.radians(24)
    parts = [cyl("Quiver", (0, 0, 0), 0.08 * r, 0.56 * r, LEATHER_LIGHT, vertices=12)]
    for i, (dx, dy) in enumerate(((-0.03, -0.02), (0.03, -0.02), (0.0, 0.03), (0.035, 0.035))):
        parts.append(cyl(f"Arrow_{i}", (dx * r, dy * r, 0.38 * r), 0.011 * r, 0.26 * r, BOW_WOOD, vertices=6))
        parts.append(cone(f"Fletch_{i}", (dx * r, dy * r, 0.50 * r), 0.038 * r, 0.011 * r, 0.12 * r, CLOTH_RED if i % 2 else FLETCH, vertices=6))
    seat = lean_matrix(k) @ Matrix.Translation((0.08 * r, sc.y * 1.10 + 0.085 * r, 0.06)) @ Matrix.Rotation(lean, 4, "Y")
    return place(parts, seat)


def create_scabbard(L, k):
    """The archer's scimitar, sheathed on the left hip where a camera behind him can see it."""
    r = k["r"]
    parts = [
        cyl("Scabbard", (0, 0, -0.25 * r), 0.042 * r, 0.52 * r, LEATHER, vertices=10),
        cyl("Scimitar_Grip", (0, 0, 0.11 * r), 0.030 * r, 0.16 * r, LEATHER_LIGHT, vertices=10),
        cube("Scimitar_Guard", (0, 0, 0.025 * r), (0.07 * r, 0.03 * r, 0.016 * r), GOLD, 0.006),
    ]
    hip = L["pelvis"] + Vector((-0.25 * r, 0.06 * r, 0.05 * r))
    return place(parts, Matrix.Translation(hip) @ Matrix.Rotation(math.radians(38), 4, "X"))


def create_pack(k):
    r, sc = k["r"], k["s"]
    parts = [
        cube("Pack", (0, 0, 0), (0.19 * r, 0.09 * r, 0.17 * r), LEATHER_LIGHT, 0.04),
        cube("Pack_Flap", (0, 0.075 * r, 0.06 * r), (0.18 * r, 0.025, 0.09 * r), LEATHER, 0.02),
        cyl("Bedroll", (0, 0.0, 0.235 * r), 0.075 * r, 0.46 * r, CLOTH, (0, math.pi / 2, 0), 12),
    ]
    return place(parts, lean_matrix(k) @ Matrix.Translation((0, sc.y * 1.0 + 0.10 * r, -0.02)))


def flap(name, top, width, length, material, swing=0.0, yaw=0.0, thick=0.011):
    """A flat panel hanging from its top edge: mail skirts, tabards, the strips of a cloak."""
    o = cube(name, (0, 0, 0), (width / 2, thick, length / 2), material, 0.006)
    for v in o.data.vertices:
        v.co.z -= length / 2
    o.location = top
    o.rotation_euler = (swing, 0, yaw)
    return o


def create_mail_skirt(L, k):
    """Mail hanging in panels from the belt instead of a bell. A cone hid his legs entirely and
    turned him into a chess pawn; panels let them show, and part where he strides."""
    r, b = k["r"], k["bulk"]
    hang = L["pelvis"] + Vector((0, 0, 0.02 * r))
    parts = []
    for i in range(8):
        a = i * math.pi / 4
        at = hang + Vector((0.262 * r * b * math.sin(a), -0.215 * r * b * math.cos(a), 0))
        parts.append(flap(f"Skirt_Flap_{i}", at, 0.19 * r * b, (0.40 - 0.05 * (i % 2)) * r, CHAIN, -0.20, a))
    return parts


def create_tabard(L, k):
    r, b = k["r"], k["bulk"]
    top = L["pelvis"] + Vector((0, -0.235 * r * b, 0.05 * r))
    return [
        flap("Tabard", top, 0.19 * r, 0.52 * r, CLOTH_RED, -0.30),
        flap("Tabard_Trim", top + Vector((0, -0.150 * r, -0.475 * r)), 0.19 * r, 0.045 * r, GOLD, -0.30, thick=0.014),
    ]


def create_gorget(L, k):
    r = k["r"]
    at = L["neck_a"] + Vector((0, -0.01 * r, -0.02 * r))
    collar = ring("Gorget", (0, 0, 0), 0.165 * r, 0.045 * r, METAL, scale=(1.0, 1.1, 0.8))
    return place([collar], Matrix.Translation(at) @ Matrix.Rotation(k["lean"] + 0.35, 4, "X"))


def create_elbow_cops(L, k):
    r = k["r"] * k["bulk"]
    parts = []
    for side in (-1, 1):
        suffix = "L" if side < 0 else "R"
        cop = dome(f"ElbowCop_{suffix}", (0, 0, 0), (0.088 * r, 0.088 * r, 0.075 * r), METAL, -0.2, 12, 8)
        cop.rotation_euler = (math.radians(-100), 0, 0)
        cop.location = mirror(L["elbow"], side) + Vector((side * 0.01, 0.045, 0.0)) * r
        parts.append(cop)
    return parts


def create_cape(L, k):
    """A soldier's cloak in strips of uneven length, fanned a little and hanging away from the
    back. The single board it replaces looked like he was carrying a door."""
    r, sc = k["r"], k["s"]
    seat = lean_matrix(k) @ Matrix.Translation((0, sc.y * 1.12 + 0.05 * r, sc.z * 0.78))
    parts = []
    widths = 5
    for i in range(widths):
        t = (i - (widths - 1) / 2) / ((widths - 1) / 2)             # -1 .. 1 across the back
        strip = flap(f"Cape_{i}", (t * sc.x * 0.82, abs(t) * -0.03 * r, 0),
                     sc.x * 0.46, (1.12 - 0.10 * (i % 2) - 0.06 * abs(t)) * r, CLOTH_RED,
                     0.20 - k["lean"], t * 0.20, thick=0.010)
        parts.append(strip)
    parts.append(ring("Cape_Collar", (0, -sc.y * 1.05, 0.10 * r), sc.x * 0.72, 0.038 * r, CLOTH_RED, scale=(1.0, 0.9, 1.0)))
    parts.append(uv("Cape_Clasp", (0, -sc.y * 1.72, 0.08 * r), (0.04 * r,) * 3, GOLD, 10, 6))
    return place(parts, seat)


def create_helmet(L, k):
    hs, H = L["hs"], L["head"]
    shell = (0.228 * hs, 0.268 * hs, 0.222 * hs)
    parts = [
        dome("Helmet", (0, 0, 0), shell, METAL, 0.20, 24, 14),
        ring("Helmet_Rim", (0, 0, shell[2] * 0.20), shell[0] * 0.98, 0.022 * hs, GOLD, scale=(1.0, shell[1] / shell[0], 1.0)),
        cube("NoseGuard", (0, -shell[1] * 0.98, -0.02 * hs), (0.022 * hs, 0.016, 0.085 * hs), METAL, 0.008),
        cube("Helmet_Ridge", (0, 0.01 * hs, shell[2] * 0.97), (0.02 * hs, shell[1] * 0.62, 0.022 * hs), GOLD, 0.006),
    ]
    for side in (-1, 1):
        suffix = "L" if side < 0 else "R"
        parts.append(cube(f"Helmet_Cheek_{suffix}", (side * shell[0] * 0.93, -0.085 * hs, -0.075 * hs), (0.012, 0.062 * hs, 0.095 * hs), METAL, 0.008))

    # Horsehair, standing along the ridge and swept back: tallest in the middle.
    for i in range(9):
        t = i / 8
        y = (-0.55 + 1.15 * t) * shell[1] * 0.62
        tall = (0.10 + 0.10 * math.sin(t * math.pi)) * hs
        for dx in (-0.012, 0.012):
            parts.append(cone(f"Crest_{i}_{dx > 0}", (dx * hs, y, shell[2] * 0.97 + tall * 0.45), 0.026 * hs, 0.004, tall, CLOTH_RED, (math.radians(-14 - 22 * t), 0, 0), 5))
    return place(parts, Matrix.Translation(H + Vector((0, -0.005, 0.035)) * hs))


def create_headband(L, k):
    r, H = k["r"], L["head"]
    parts = [
        ring("Headband", (0, 0, 0), 0.20 * r, 0.028 * r, CLOTH_RED, scale=(1.0, 1.13, 1.0)),
        rag("Headband_Tail", Vector((0.06 * r, 0.225 * r, 0.0)), 0.26 * r, 0.04 * r, CLOTH_RED, 0.25),
    ]
    return place(parts, Matrix.Translation(H + Vector((0, 0.005, 0.085)) * r) @ Matrix.Rotation(math.radians(-8), 4, "X"))


def create_equipment(variant, L):
    k = kit(L)
    parts = []

    if variant == "goblin":
        # Dressed like a raider: hide, straps and scavenged iron, with the body left to show.
        parts += create_straps(k, LEATHER, crossed=True)
        parts += create_necklace(L, k)
        parts += create_pauldrons(L, k, IRON, sides=(1,), spikes=True, size=1.12, layered=True)
        parts += create_wraps(L, k, LEATHER, LEATHER_LIGHT, banded=True)
        parts += create_belt(L, k, heavy=True)
        parts += create_loincloth(L, k)
        parts += create_cleaver(L, k)
        parts += create_shield("Shield", L, k, 0.31 * k["r"], spiked=True)
    elif variant == "goblin-archer":
        parts += create_cuirass(k, LEATHER_LIGHT)
        parts += create_studs(k)
        parts += create_straps(k, LEATHER, crossed=False, grow=1.10)
        parts += create_wraps(L, k, LEATHER, LEATHER, banded=True)
        parts += create_belt(L, k)
        parts += create_loincloth(L, k)
        parts += create_headband(L, k)
        parts += create_quiver(k)
        parts += create_scabbard(L, k)
        parts += create_bow(L, k)
    else:
        # A soldier, and a sergeant of them: mail under plate at the joints, his rank in red.
        parts += create_cuirass(k, CHAIN, grow=1.12, waist=0.75)
        parts += create_mail_skirt(L, k)
        parts += create_tabard(L, k)
        parts += create_gorget(L, k)
        parts += create_pauldrons(L, k, METAL, sides=(-1, 1), spikes=True, size=1.12, layered=True)
        parts += create_elbow_cops(L, k)
        parts += create_wraps(L, k, METAL, METAL, knees=METAL, greaves=True)
        parts += create_belt(L, k, heavy=True)
        parts += create_helmet(L, k)
        parts += create_cape(L, k)
        parts += create_blade("Longsword", L, k, 0.92, 0.066)
        parts += create_shield("HeavyShield", L, k, 0.39 * k["r"], spiked=False)

    return parts


def create_armature(L):
    """A skeleton laid down the middle of the flesh it moves, from the same landmarks."""
    bpy.ops.object.armature_add(enter_editmode=True, location=(0, 0, 0))
    arm = bpy.context.object
    arm.name = "Goblin_Armature"
    arm.data.name = "Goblin_ArmatureData"
    eb = arm.data.edit_bones
    eb.remove(eb[0])

    def bone(name, head, tail, parent=None):
        b = eb.new(name)
        b.head = head
        b.tail = tail
        if parent:
            b.parent = eb.get(parent)
        return b

    waist = L["pelvis"].lerp(L["belly"], 0.55)
    sternum = L["belly"].lerp(L["ribs"], 0.6)
    crown = L["head"] + Vector((0, -0.04, 0.20)) * L["r"]

    bone("root", (0, 0, 0), (0, 0, 0.15))
    bone("pelvis", L["pelvis"] - Vector((0, 0, 0.10)), waist, "root")
    bone("spine", waist, sternum, "pelvis")
    bone("chest", sternum, L["neck_a"], "spine")
    bone("neck", L["neck_a"], L["neck_b"], "chest")
    bone("head", L["neck_b"], crown, "neck")

    for side, sgn in (("L", -1), ("R", 1)):
        sh, el, wr, tip = (mirror(L[k], sgn) for k in ("shoulder", "elbow", "wrist", "fingertip"))
        hp, kn, an, toe = (mirror(L[k], sgn) for k in ("hip", "knee", "ankle", "toe"))
        bone(f"upper_arm_{side}", sh, el, "chest")
        bone(f"forearm_{side}", el, wr, f"upper_arm_{side}")
        bone(f"hand_{side}", wr, tip, f"forearm_{side}")
        bone(f"thigh_{side}", hp, kn, "pelvis")
        bone(f"shin_{side}", kn, an, f"thigh_{side}")
        bone(f"foot_{side}", an, toe, f"shin_{side}")

    # Roll every bone so its Z faces the way the goblin does. Local X is then the same
    # side-to-side hinge on both arms and both legs, and a positive X rotation always means
    # "swing forward" — which is what lets the animation tables below be read by a person.
    for b in eb:
        b.align_roll(Vector((0, -1, 0)))

    bpy.ops.object.mode_set(mode="POSE")
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm


def rigid_skin(obj, arm, bone_name):
    if obj.type != "MESH":
        return
    vg = obj.vertex_groups.get(bone_name) or obj.vertex_groups.new(name=bone_name)
    vg.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")
    mod = obj.modifiers.get("Armature") or obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    mod.use_deform_preserve_volume = False
    obj.parent = arm

def auto_skin(obj, arm):
    if obj.type != "MESH":
        return
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    try:
        bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    except RuntimeError as exc:
        raise RuntimeError(f"Automatic skinning failed for {obj.name}: {exc}") from exc

def fill_unweighted(obj, arm):
    """Give any vertex automatic weighting missed to the bone nearest it.

    Heat weighting gives up on flesh far from every bone — a tail, a tuft — and leaves it
    weighted to nothing. The exporter then invents a 'neutral_bone' to hold it, the skin no
    longer matches the skeleton, and the self-check fails, rightly.
    """
    bones = {b.name: b for b in arm.data.bones}
    groups = {g.index: g.name for g in obj.vertex_groups if g.name in bones}
    for v in obj.data.vertices:
        if any(g.group in groups and g.weight > 1e-4 for g in v.groups):
            continue
        p = v.co

        def distance(b):
            a, c = b.head_local, b.tail_local
            ac = c - a
            t = max(0.0, min(1.0, (p - a).dot(ac) / max(ac.length_squared, 1e-9)))
            return (p - (a + ac * t)).length

        name = min((b for b in bones.values() if b.name != "root"), key=distance).name
        vg = obj.vertex_groups.get(name) or obj.vertex_groups.new(name=name)
        vg.add([v.index], 1.0, "REPLACE")


def bind_all(body, equipment, arm):
    skin = next(o for o in body if o.name == "Body")
    for obj in body:
        if obj is skin:
            continue                                   # skinned before the armour was cut from it
        if obj.name.startswith(("Eye_", "Pupil_", "Tusk_", "Ear_", "Earring_", "Hair_")):
            rigid_skin(obj, arm, "head")
        else:
            auto_skin(obj, arm)

    # What lies on the skin bends with the skin: its weights are the body's under it.
    fitted = [o for o in equipment if o.type == "MESH" and o.name.startswith(("Cuirass", "Strap_", "Stud_"))]
    for obj in fitted:
        skin_from_body(obj, skin, arm)
    equipment = [o for o in equipment if o not in fitted]

    # By prefix, first match wins, so the specific entries sit above the general ones. The
    # exact-name table this replaces needed a new row for every stud and arrow, and threw on
    # the .001 suffix Blender gives a repeated name.
    rigid_map = [
        ("Sword_", "hand_R"), ("Longsword_", "hand_R"),
        ("Shortbow", "hand_L"),
        ("Scimitar", "pelvis"), ("Scabbard", "pelvis"),
        ("HeavyShield", "hand_L"), ("Shield", "hand_L"),
        ("Bracer_L", "forearm_L"), ("Bracer_R", "forearm_R"),
        ("Pauldron_L", "upper_arm_L"), ("Pauldron_R", "upper_arm_R"),
        ("Spike_L", "upper_arm_L"), ("Spike_R", "upper_arm_R"),
        ("Boot_L", "shin_L"), ("Boot_R", "shin_R"),
        ("KneePad_L", "shin_L"), ("KneePad_R", "shin_R"),
        ("Cuirass", "chest"), ("Stud_", "chest"), ("Strap_", "chest"),
        ("Pack", "chest"), ("Bedroll", "chest"),
        ("Quiver", "chest"), ("Arrow_", "chest"), ("Fletch_", "chest"),
        ("Cape", "chest"), ("Gorget", "chest"), ("Tabard", "pelvis"),
        ("ElbowCop_L", "forearm_L"), ("ElbowCop_R", "forearm_R"),
        ("Helmet", "head"), ("Crest", "head"), ("NoseGuard", "head"), ("Headband", "head"),
        ("Belt", "pelvis"), ("Buckle", "pelvis"), ("Pouch", "pelvis"),
        ("Loincloth", "pelvis"), ("Skirt", "pelvis"),
    ]

    for obj in equipment:
        if obj.type == "CURVE":
            # Deselect first. Without it the armature is still selected from an earlier step,
            # Blender refuses the whole batch with "1 object type(s) which do not support
            # conversion", and the bow reaches the exporter as a curve with no vertices to
            # weight — two primitives arriving without JOINTS_0 and nothing else to say so.
            bpy.ops.object.select_all(action="DESELECT")
            bpy.context.view_layer.objects.active = obj
            obj.select_set(True)
            bpy.ops.object.convert(target="MESH")
            obj = bpy.context.view_layer.objects.active

        bone = next((b for prefix, b in rigid_map if obj.name.startswith(prefix)), None)
        if bone is None:
            raise RuntimeError(f"No rigid skin bone mapping for equipment: {obj.name}")
        rigid_skin(obj, arm, bone)

def pose_reset(arm):
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.location = (0, 0, 0)
        pb.rotation_euler = (0, 0, 0)
        pb.scale = (1, 1, 1)

def key_pose(arm, frame, rotations=None, locations=None):
    rotations = rotations or {}
    locations = locations or {}
    for name, rot in rotations.items():
        pb = arm.pose.bones[name]
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = rot
        pb.keyframe_insert("rotation_euler", frame=frame, group=name)
    for name, loc in locations.items():
        pb = arm.pose.bones[name]
        pb.location = loc
        pb.keyframe_insert("location", frame=frame, group=name)

def action_fcurves(action):
    """Every F-Curve in an action, on either side of the slotted-action change.

    Blender 4.4 moved curves from Action.fcurves into layers -> strips -> channelbags, and 5.x
    removed the old attribute outright, so touching it throws rather than warning.
    """
    if hasattr(action, "fcurves"):
        return list(action.fcurves)

    curves = []
    for layer in action.layers:
        for strip in layer.strips:
            for bag in getattr(strip, "channelbags", []):
                curves.extend(bag.fcurves)
    return curves


def add_action(arm, name, end, frames):
    action = bpy.data.actions.new(name)
    arm.animation_data_create()
    arm.animation_data.action = action
    pose_reset(arm)
    for entry in frames:
        # The tables below are written both ways — some rows carry locations and most do not —
        # so the arity is read rather than assumed.
        frame, rotations = entry[0], entry[1]
        locations = entry[2] if len(entry) > 2 else {}
        bpy.context.scene.frame_set(frame)
        key_pose(arm, frame, rotations, locations)
    if hasattr(action, "use_frame_range"):
        action.use_frame_range = True
    if hasattr(action, "frame_start"):
        action.frame_start = 1
        action.frame_end = end

    for fc in action_fcurves(action):
        for kp in fc.keyframe_points:
            kp.interpolation = "BEZIER"
    return action

def create_animations(arm):
    """Four clips, written in one convention: X swings a bone forward, negative X swings it back.

    Rows are (frame, rotations) or (frame, rotations, locations). A bone left out of a row is
    not keyed there, so every clip opens with a full row to pin the bones it uses.
    """
    actions = []

    guard = {
        "upper_arm_R": (0.12, 0.0, 0.0), "forearm_R": (0.93, 0.0, 0.0),
        "upper_arm_L": (0.30, 0.0, 0.0), "forearm_L": (0.90, 0.0, 0.0),
        "chest": (0.0, 0.0, 0.0), "head": (0.0, 0.0, 0.0), "pelvis": (0.0, 0.0, 0.0),
    }

    def varied(**changes):
        row = dict(guard)
        row.update(changes)
        return row

    # Weight shifting from foot to foot, weapon up, breathing through the chest.
    actions.append(add_action(arm, "goblin_idle_combat", 20, [
        (1, varied(), {"root": (0, 0, 0)}),
        (6, varied(chest=(0.05, 0.0, 0.03), head=(-0.04, 0.06, 0.0), pelvis=(0.0, 0.0, -0.03),
                   forearm_R=(0.85, 0.0, 0.0), forearm_L=(0.78, 0.0, 0.0)), {"root": (0, -0.012, 0)}),
        (11, varied(), {"root": (0, 0, 0)}),
        (16, varied(chest=(0.05, 0.0, -0.03), head=(-0.04, -0.06, 0.0), pelvis=(0.0, 0.0, 0.03),
                    forearm_R=(0.68, 0.0, 0.0), forearm_L=(0.90, 0.0, 0.0)), {"root": (0, -0.012, 0)}),
        (20, varied(), {"root": (0, 0, 0)}),
    ]))

    # Overhead chop: rear back with the arm high, then the whole torso goes into the blow.
    actions.append(add_action(arm, "goblin_attack_melee", 12, [
        (1, varied()),
        (4, varied(chest=(-0.28, 0.0, 0.0), head=(-0.10, 0.0, 0.0),
                   upper_arm_R=(2.25, 0.0, 0.0), forearm_R=(1.05, 0.0, 0.0),
                   upper_arm_L=(0.55, 0.0, 0.0))),
        (7, varied(chest=(0.42, 0.0, 0.0), head=(0.10, 0.0, 0.0), pelvis=(0.12, 0.0, 0.0),
                   upper_arm_R=(0.55, 0.0, 0.0), forearm_R=(0.12, 0.0, 0.0),
                   upper_arm_L=(-0.20, 0.0, 0.0))),
        (10, varied(chest=(0.15, 0.0, 0.0), upper_arm_R=(0.40, 0.0, 0.0), forearm_R=(0.50, 0.0, 0.0))),
        (12, varied()),
    ]))

    def stride(phase):
        a, b = (1, -1) if phase else (-1, 1)
        return {
            "chest": (0.22, 0.0, 0.0), "head": (-0.12, 0.0, 0.0),
            "thigh_R": (0.75 * a, 0.0, 0.0), "shin_R": (-0.95 if a < 0 else -0.25, 0.0, 0.0),
            "thigh_L": (0.75 * b, 0.0, 0.0), "shin_L": (-0.95 if b < 0 else -0.25, 0.0, 0.0),
            "upper_arm_R": (0.70 * b, 0.0, 0.0), "forearm_R": (0.85, 0.0, 0.0),
            "upper_arm_L": (0.70 * a, 0.0, 0.0), "forearm_L": (0.85, 0.0, 0.0),
        }

    actions.append(add_action(arm, "goblin_run", 20, [
        (1, stride(True), {"root": (0, 0, 0)}),
        (6, stride(False), {"root": (0, 0.03, 0)}),
        (11, stride(False), {"root": (0, 0, 0)}),
        (16, stride(True), {"root": (0, 0.03, 0)}),
        (20, stride(True), {"root": (0, 0, 0)}),
    ]))

    # Struck, staggers, goes over backwards and stays there. The root tips the whole skeleton
    # about the floor, and is lifted as it goes so the back lands on the ground, not under it.
    limp = {"upper_arm_R": (0.0, 0.0, 0.0), "forearm_R": (0.2, 0.0, 0.0),
            "upper_arm_L": (0.0, 0.0, 0.0), "forearm_L": (0.2, 0.0, 0.0)}

    # Tipping the skeleton over backwards about its feet lays the body out *behind* where it
    # stood — a whole square behind, on the board. So the root walks forward as it goes over, by
    # as far as the hips would otherwise travel back, and the body comes to rest on the square
    # it died in. The root's local Z faces the way the creature does; align_roll saw to that.
    hips = arm.data.bones["pelvis"].head_local.z

    def fall(tip, lift):
        return {"root": (0, lift, hips * math.sin(-tip) * 1.15)}

    actions.append(add_action(arm, "goblin_death", 20, [
        (1, varied(root=(0.0, 0.0, 0.0), thigh_R=(0.0, 0.0, 0.0), thigh_L=(0.0, 0.0, 0.0)), {"root": (0, 0, 0)}),
        (5, varied(root=(-0.22, 0.0, 0.0), chest=(-0.35, 0.0, 0.0), head=(-0.30, 0.0, 0.0),
                   upper_arm_R=(1.1, 0.0, 0.0), upper_arm_L=(1.1, 0.0, 0.0)), fall(-0.22, 0.0)),
        (12, dict(limp, root=(-1.05, 0.0, 0.0), chest=(-0.20, 0.0, 0.0), head=(0.15, 0.0, 0.0),
                  thigh_R=(0.45, 0.0, 0.0), thigh_L=(0.25, 0.0, 0.0)), fall(-1.05, 0.14)),
        (17, dict(limp, root=(-1.52, 0.0, 0.0), chest=(-0.10, 0.0, 0.0), head=(-0.20, 0.0, 0.0),
                  upper_arm_R=(-0.35, 0.0, -0.55), upper_arm_L=(-0.35, 0.0, 0.55),
                  thigh_R=(0.30, 0.0, 0.0), thigh_L=(0.10, 0.0, 0.0)), fall(-1.52, 0.24)),
        (20, dict(limp, root=(-1.50, 0.0, 0.0), chest=(-0.10, 0.0, 0.0), head=(-0.20, 0.0, 0.0),
                  upper_arm_R=(-0.35, 0.0, -0.55), upper_arm_L=(-0.35, 0.0, 0.55),
                  thigh_R=(0.30, 0.0, 0.0), thigh_L=(0.10, 0.0, 0.0)), fall(-1.50, 0.24)),
    ]))

    # A trip: drop, and sweep the weapon arm low across their shins.
    actions.append(add_action(arm, "goblin_trip", 12, [
        (1, varied(), {"root": (0, 0, 0)}),
        (4, varied(pelvis=(0.30, 0.0, 0.0), chest=(0.40, 0.35, 0.0), head=(-0.30, 0.0, 0.0),
                   upper_arm_R=(-0.55, 0.0, 0.0), forearm_R=(0.35, 0.0, 0.0),
                   thigh_R=(0.55, 0.0, 0.0), shin_R=(-0.80, 0.0, 0.0), thigh_L=(0.40, 0.0, 0.0), shin_L=(-0.60, 0.0, 0.0)),
         {"root": (0, -0.13, 0)}),
        (7, varied(pelvis=(0.35, 0.0, 0.0), chest=(0.60, -0.55, 0.0), head=(-0.35, 0.0, 0.0),
                   upper_arm_R=(0.95, 0.0, 0.0), forearm_R=(0.15, 0.0, 0.0),
                   thigh_R=(0.60, 0.0, 0.0), shin_R=(-0.85, 0.0, 0.0), thigh_L=(0.40, 0.0, 0.0), shin_L=(-0.60, 0.0, 0.0)),
         {"root": (0, -0.15, 0.10)}),
        (10, varied(chest=(0.20, 0.0, 0.0)), {"root": (0, -0.04, 0.03)}),
        (12, varied(), {"root": (0, 0, 0)}),
    ]))

    # A shove: coil behind the shield arm, then the whole body goes into them.
    actions.append(add_action(arm, "goblin_shove", 12, [
        (1, varied(), {"root": (0, 0, 0)}),
        (4, varied(chest=(-0.18, 0.0, 0.0), upper_arm_L=(0.25, 0.0, 0.0), forearm_L=(1.35, 0.0, 0.0),
                   thigh_R=(0.30, 0.0, 0.0), shin_R=(-0.45, 0.0, 0.0)), {"root": (0, -0.05, -0.05)}),
        (7, varied(pelvis=(0.22, 0.0, 0.0), chest=(0.50, 0.0, 0.0), head=(-0.25, 0.0, 0.0),
                   upper_arm_L=(0.95, 0.0, 0.0), forearm_L=(0.95, 0.0, 0.0), upper_arm_R=(-0.30, 0.0, 0.0),
                   thigh_L=(0.55, 0.0, 0.0), shin_L=(-0.35, 0.0, 0.0), thigh_R=(-0.40, 0.0, 0.0)),
         {"root": (0, -0.05, 0.20)}),
        (10, varied(chest=(0.18, 0.0, 0.0)), {"root": (0, 0, 0.06)}),
        (12, varied(), {"root": (0, 0, 0)}),
    ]))

    pose_reset(arm)
    arm.animation_data.action = None
    return actions

def configure_animation_export():
    settings = bpy.ops.export_scene.gltf.get_rna_type().properties
    return

def export_glb(path):
    path = os.path.abspath(path)
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    kwargs = {
        "filepath": path,
        "export_format": "GLB",
        "use_selection": False,
        "export_apply": True,
        "export_yup": True,
        "export_animations": True,
        "export_skins": True,
        "export_morph": False,
        "export_materials": "EXPORT",
    }
    props = bpy.ops.export_scene.gltf.get_rna_type().properties
    if "export_animation_mode" in props:
        enum_values = {item.identifier for item in props["export_animation_mode"].enum_items}
        if "ACTIONS" in enum_values:
            kwargs["export_animation_mode"] = "ACTIONS"
    if "export_nla_strips" in props:
        kwargs["export_nla_strips"] = False
    bpy.ops.export_scene.gltf(**kwargs)

def read_glb_json(path):
    with open(path, "rb") as f:
        magic, version, length = struct.unpack("<4sII", f.read(12))
        if magic != b"glTF":
            raise RuntimeError("Not a GLB file")
        json_chunk_length, chunk_type = struct.unpack("<II", f.read(8))
        if chunk_type != 0x4E4F534A:
            raise RuntimeError("GLB first chunk is not JSON")
        raw = f.read(json_chunk_length)
        return json.loads(raw.rstrip(b" \t\r\n\x00").decode("utf-8"))

def validate_glb(path, arm, creature="goblin"):
    data = read_glb_json(path)
    meshes = data.get("meshes", [])
    skins = data.get("skins", [])
    animations = [a.get("name", "") for a in data.get("animations", [])]
    primitives = [p for m in meshes for p in m.get("primitives", [])]
    missing_joints = [i for i, p in enumerate(primitives) if "JOINTS_0" not in p.get("attributes", {})]
    joint_count = len(skins[0].get("joints", [])) if skins else 0

    tri_count = 0
    for primitive in primitives:
        mode = primitive.get("mode", 4)
        accessor_index = primitive.get("indices")
        if accessor_index is not None:
            acc = data["accessors"][accessor_index]
            count = acc["count"]
            if mode == 4:
                tri_count += count // 3
            elif mode == 5:
                tri_count += max(0, count - 2)
            elif mode == 6:
                tri_count += max(0, count - 2)
        else:
            pos = primitive.get("attributes", {}).get("POSITION")
            if pos is not None:
                count = data["accessors"][pos]["count"]
                if mode == 4:
                    tri_count += count // 3

    exported_mesh_nodes = [n for n in data.get("nodes", []) if "mesh" in n]
    skinned_nodes = [n for n in exported_mesh_nodes if "skin" in n]
    bbox_min = Vector((float("inf"), float("inf"), float("inf")))
    bbox_max = Vector((float("-inf"), float("-inf"), float("-inf")))
    for obj in bpy.context.scene.objects:
        if obj.type == "MESH":
            for v in obj.bound_box:
                world = obj.matrix_world @ Vector(v)
                bbox_min.x = min(bbox_min.x, world.x)
                bbox_min.y = min(bbox_min.y, world.y)
                bbox_min.z = min(bbox_min.z, world.z)
                bbox_max.x = max(bbox_max.x, world.x)
                bbox_max.y = max(bbox_max.y, world.y)
                bbox_max.z = max(bbox_max.z, world.z)

    # The four the game knows how to ask for, under whatever creature's name they were made.
    expected_names = [f"{creature}_{clip}" for clip in ("idle_combat", "attack_melee", "run", "death")]
    animation_ok = all(any(expected in name for name in animations) for expected in expected_names)
    print(f"Meshes:             {len(exported_mesh_nodes)}")
    print(f"Skinned meshes:     {len(skinned_nodes)}/{len(exported_mesh_nodes)}")
    print(f"Triangles:           {tri_count}")
    print(f"Joint count:         {joint_count}")
    print("Animations:")
    for name in animations:
        print(f"  {name}")
    print(f"Bounding box min:    ({bbox_min.x:.4f}, {bbox_min.y:.4f}, {bbox_min.z:.4f})")
    print(f"Bounding box max:    ({bbox_max.x:.4f}, {bbox_max.y:.4f}, {bbox_max.z:.4f})")
    print(f"Lowest Z:            {bbox_min.z:.4f}")
    print(f"JOINTS_0 missing:    {len(missing_joints)}")
    if missing_joints:
        print(f"Missing primitive indices: {missing_joints}")
    checks = [
        len(exported_mesh_nodes) > 0,
        len(skinned_nodes) == len(exported_mesh_nodes),
        joint_count == len(arm.data.bones),
        not missing_joints,
        animation_ok,
        bbox_min.z >= -0.01,
    ]
    print(f"Validation:          {'PASS' if all(checks) else 'FAIL'}")
    if not all(checks):
        raise RuntimeError("GLB self-check failed")

def setup_scene():
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    engines = scene.render.bl_rna.properties["engine"].enum_items.keys()
    for wanted in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
        if wanted in engines:
            scene.render.engine = wanted
            break
    scene["ironbound_asset"] = True
    scene["godot_target"] = "Godot 4.x"
    scene["export_format"] = "glTF 2.0 / GLB"
    scene["coordinate_convention"] = "Blender -Y forward; glTF +Z forward"

def clear_scene():
    bpy.ops.object.mode_set(mode="OBJECT") if bpy.context.object and bpy.context.object.mode != "OBJECT" else None
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (
        bpy.data.meshes,
        bpy.data.curves,
        bpy.data.armatures,
        bpy.data.cameras,
        bpy.data.lights,
    ):
        for block in list(datablocks):
            if block.users == 0:
                datablocks.remove(block)

def parse_args():
    args = sys.argv
    if "--" not in args:
        raise RuntimeError("Usage: blender -b --factory-startup --python generate_goblin.py -- VARIANT OUTPUT_GLB")
    user = args[args.index("--") + 1:]
    if len(user) == 1:
        variant = "goblin"
        output = user[0]
    elif len(user) >= 2:
        variant = user[0]
        output = user[1]
    else:
        raise RuntimeError("Usage: ... -- goblin|goblin-archer|hobgoblin OUTPUT_GLB")
    if variant not in {"goblin", "goblin-archer", "hobgoblin"}:
        raise RuntimeError(f"Unknown variant: {variant}")
    return variant, output

def build(variant, output):
    clear_scene()
    setup_scene()
    body, L = create_body(variant)
    # Skeleton and skin first: the armour is cut from the body by which bones carry it.
    arm = create_armature(L)
    skin = next(o for o in body if o.name == "Body")
    auto_skin(skin, arm)
    fill_unweighted(skin, arm)
    L["body"] = skin
    equipment = create_equipment(variant, L)
    paint_skin(body, L)
    bind_all(body, equipment, arm)
    if variant != "hobgoblin":                     # his is under a helmet
        create_crest(L, next(o for o in body if o.name == "Body"), arm)
    dress(body, equipment)
    create_animations(arm)

    bpy.ops.wm.save_as_mainfile(filepath=os.path.splitext(os.path.abspath(output))[0] + ".blend")
    export_glb(output)

    print("")
    print(f"=== {variant} ===")
    validate_glb(output, arm)

def dress(body, equipment):
    """Bake the surfaces: the hide to one atlas, everything worn or carried to another.

    After the skinning, so the bow is a mesh by now, and before the animations, which do not
    care. The hide keeps the occlusion it was painted with; the kit gets its own baked in.
    """
    hide = [o for o in body if o.name == "Body" or o.name.startswith("Ear_")]
    rest = [o for o in body if o not in hide] + list(equipment)
    surface.finish([("hide", hide, 1024, False), ("kit", rest, 1024, True)])


def main():
    variant, output = parse_args()
    build(variant, output)

if __name__ == "__main__":
    main()

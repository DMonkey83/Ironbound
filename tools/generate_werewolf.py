"""A werewolf, grown the same way the goblins are.

    blender -b --factory-startup --python tools/generate_werewolf.py -- <out.glb>

Everything that is not specifically a wolf is borrowed from generate_goblin.py: the skeleton and
its one rotation convention, rigid and automatic skinning, the animation tables' format, the
whole-scene export, and the check that parses the exported GLB. What is written here is the
creature: a digitigrade body on its toes, a long open muzzle, and a pelt.

Shape, proportion and palette follow a concept sheet the project owner supplied (the unarmoured
werewolf in its lower half). It is reference only; nothing was extracted from it.
"""
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import generate_goblin as gg  # noqa: E402
import surface  # noqa: E402
import cards  # noqa: E402

FUR = gg.mat("Wolf_Fur", (0.105, 0.080, 0.064), 0.0, 0.95)
FUR_PALE = gg.mat("Wolf_Fur_Pale", (0.36, 0.26, 0.19), 0.0, 0.95)
surface.RECIPES["Wolf_Fur_Pale"] = lambda c, m, r: surface.fur(c)
FANG = gg.mat("Wolf_Fang", (0.86, 0.80, 0.66), 0.0, 0.5)
TALON = gg.mat("Wolf_Talon", (0.13, 0.11, 0.09), 0.0, 0.45)
MAW = gg.mat("Wolf_Maw", (0.30, 0.05, 0.05), 0.0, 0.6)
NOSE = gg.mat("Wolf_Nose", (0.03, 0.025, 0.025), 0.0, 0.4)
AMBER = gg.mat("Wolf_Eye", (1.0, 0.55, 0.08), 0.0, 0.3)


def frame():
    """Landmarks, right-hand side. Same names as the goblin's, so the same skeleton fits.

    The legs are the difference. A wolf stands on its toes: the thigh runs forward to the knee,
    the shin back to a hock held well off the ground, and the long foot down to the toes. With
    the goblin's bone names that is thigh, shin and foot, and nothing downstream needs to know.
    """
    V = Vector
    L = {
        "pelvis": V((0, 0.12, 1.02)), "belly": V((0, 0.03, 1.25)), "ribs": V((0, -0.10, 1.55)),
        "back": V((0, 0.10, 1.72)), "neck_a": V((0, -0.14, 1.78)), "neck_b": V((0, -0.32, 1.88)),
        "head": V((0, -0.43, 1.94)),
        "shoulder": V((0.43, -0.04, 1.72)), "elbow": V((0.64, -0.10, 1.29)),
        "wrist": V((0.68, -0.34, 0.92)), "palm": V((0.68, -0.41, 0.82)), "fingertip": V((0.68, -0.54, 0.62)),
        "hip": V((0.19, 0.10, 1.00)), "knee": V((0.31, -0.24, 0.68)), "ankle": V((0.31, 0.13, 0.36)),
        "foot": V((0.32, -0.07, 0.075)), "toe": V((0.33, -0.34, 0.03)),
    }
    L["r"] = 1.0
    L["hs"] = 1.0
    L["bulk"] = 1.0
    L["hob"] = False
    L["lean"] = math.atan2(-(L["ribs"].y - L["pelvis"].y) * 1.3, L["ribs"].z - L["pelvis"].z)
    return L


def grow_body(L):
    mb = bpy.data.metaballs.new("WolfMeta")
    mb.resolution = 0.021
    mb.threshold = 0.6
    meta = bpy.data.objects.new("WolfMeta", mb)
    bpy.context.collection.objects.link(meta)
    M = gg.mirror

    def ball(co, size, negative=False):
        e = mb.elements.new(type="ELLIPSOID")
        e.co = co
        if isinstance(size, (int, float)):
            size = (size,) * 3
        e.size_x, e.size_y, e.size_z = (v * 1.55 for v in size)
        e.radius = 1.0
        e.stiffness = 1.6 if negative else 2.0
        e.use_negative = negative

    def chain(a, b, ra, rb, n=4):
        for i in range(n + 1):
            t = i / n
            ball(a.lerp(b, t), ra + (rb - ra) * t)

    def off(base, x, y, z):
        return base + Vector((x, y, z))

    # Torso: a wedge. Enormous across the shoulders and through the chest, drawn in hard at
    # the waist, carried well forward of the hips.
    ball(L["pelvis"], (0.22, 0.18, 0.17))
    ball(L["belly"], (0.20, 0.17, 0.19))
    ball(L["ribs"], (0.34, 0.25, 0.27))
    ball(L["back"], (0.34, 0.21, 0.20))
    for side in (-1, 1):
        ball(off(L["ribs"], side * 0.15, -0.20, 0.09), (0.165, 0.09, 0.125))     # pectoral
        ball(off(L["ribs"], side * 0.27, 0.20, 0.0), (0.12, 0.09, 0.19))         # lat
        ball(off(L["back"], side * 0.19, 0.0, 0.10), (0.15, 0.12, 0.10))         # trapezius
        for row in range(3):
            ball(off(L["belly"], side * 0.06, -0.155, 0.13 - row * 0.10), 0.055) # abdominals
    chain(L["neck_a"], L["neck_b"], 0.17, 0.14, 2)

    # Head: a wolf's. Broad flat skull, heavy brow, and a long muzzle split into an upper jaw
    # and a lower one hanging open, so the teeth have somewhere to be.
    H = L["head"]
    ball(H, (0.17, 0.19, 0.15))
    ball(off(H, 0, -0.125, 0.070), (0.155, 0.07, 0.042))                         # brow
    for side in (-1, 1):
        ball(off(H, side * 0.085, -0.150, 0.062), (0.070, 0.05, 0.030))          # drawn down over each eye
    chain(off(H, 0, -0.15, -0.015), off(H, 0, -0.43, -0.075), 0.085, 0.052, 4)   # upper jaw
    ball(off(H, 0, -0.30, 0.0), (0.05, 0.10, 0.035))                             # bridge of the nose
    # The lower jaw, hanging open: as wide at the tooth line as the upper, which is what makes
    # it a wolf's and not a rat's. Two rami meeting at the chin, filled between.
    chain(off(H, 0, -0.10, -0.115), off(H, 0, -0.385, -0.190), 0.056, 0.030, 7)
    for side in (-1, 1):
        chain(off(H, side * 0.042, -0.10, -0.112), off(H, side * 0.026, -0.37, -0.182), 0.046, 0.026, 7)
    for side in (-1, 1):
        ball(off(H, side * 0.125, -0.06, -0.04), (0.075, 0.09, 0.085))           # cheek and jaw muscle
        ball(off(H, side * 0.075, -0.180, 0.022), (0.036, 0.045, 0.022), negative=True)   # eye socket

    # Arms: long enough to reach the knee, thickest at the forearm, ending in a hand like a rake.
    for side in (-1, 1):
        sh, el, wr, pa = (M(L[k], side) for k in ("shoulder", "elbow", "wrist", "palm"))
        ball(sh, 0.185)
        chain(off(sh, side * 0.04, -0.01, -0.09), el, 0.118, 0.092)
        ball(sh.lerp(el, 0.5) + Vector((-side * 0.02, -0.02, 0.02)), (0.115, 0.125, 0.15))
        ball(el, 0.092)
        chain(el, wr, 0.115, 0.066)
        ball(el.lerp(wr, 0.28), (0.105, 0.105, 0.13))
        ball(pa, (0.095, 0.105, 0.065))
        for dx in (-0.066, -0.022, 0.022, 0.066):
            chain(off(pa, dx, -0.04, 0.0), off(pa, dx * 1.25, -0.115, -0.15), 0.036, 0.026, 3)
        chain(off(pa, -side * 0.08, 0.01, 0.015), off(pa, -side * 0.145, -0.08, -0.05), 0.04, 0.03, 2)

    # Legs: a wolf's, on its toes. Huge thigh, a shin running *back* to the hock, a long foot.
    for side in (-1, 1):
        hp, kn, hk, ft = (M(L[k], side) for k in ("hip", "knee", "ankle", "foot"))
        chain(hp, kn, 0.175, 0.105)
        ball(hp.lerp(kn, 0.4) + Vector((side * 0.03, -0.02, 0.0)), (0.15, 0.17, 0.17))   # haunch
        ball(kn, 0.095)
        chain(kn, hk, 0.100, 0.064, 8)
        ball(kn.lerp(hk, 0.3) + Vector((0, 0.05, 0)), (0.085, 0.10, 0.12))       # the muscle behind the shin
        ball(hk, 0.068)                                                          # the hock
        chain(hk, ft, 0.060, 0.064, 8)
        ball(ft, (0.088, 0.13, 0.055))
        for dx in (-0.066, -0.022, 0.022, 0.066):
            ball(off(ft, dx, -0.16, -0.02), (0.030, 0.06, 0.034))

    # A tail: a thick root tapering away behind, for the brush of fur to grow on. Without
    # flesh under it a tail of fur is a cloud with nothing in it.
    root = L["pelvis"] + Vector((0, 0.17, -0.02))
    for i, p in enumerate((root, root + Vector((0, 0.15, -0.12)), root + Vector((0, 0.25, -0.32)), root + Vector((0, 0.29, -0.54)))):
        L[f"tail{i}"] = p
    for i in range(3):
        chain(L[f"tail{i}"], L[f"tail{i + 1}"], (0.075, 0.062, 0.048)[i], (0.062, 0.048, 0.030)[i], 3)

    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = meta
    meta.select_set(True)
    bpy.ops.object.convert(target="MESH")
    body = bpy.context.view_layer.objects.active
    body.name = "Body"
    gg.smooth(body)
    shag(body, L)
    thin = body.modifiers.new("Thin", "DECIMATE")
    thin.ratio = 0.26
    bpy.ops.object.modifier_apply(modifier="Thin")

    drop = min(v.co.z for v in body.data.vertices)
    for v in body.data.vertices:
        v.co.z -= drop
    for value in L.values():
        if isinstance(value, Vector):
            value.z -= drop
    return body


def shag(body, L):
    """Push the hide out in clumps before it is thinned, so the silhouette is a coat and not
    a wetsuit. Kept off the muzzle, the palms and the feet, which are skin and pad."""
    H = L["head"]
    muzzle = H + Vector((0, -0.30, -0.08))
    weight = body.vertex_groups.new(name="Shag")
    for v in body.data.vertices:
        p = v.co
        w = 1.0 - max(0.0, 1.0 - (p - muzzle).length / 0.32)
        w *= min(1.0, max(0.0, (p.z - 0.22) / 0.22))
        for side in (-1, 1):
            w *= 1.0 - max(0.0, 1.0 - (p - gg.mirror(L["palm"], side)).length / 0.24)
        weight.add([v.index], w, "REPLACE")

    clumps = bpy.data.textures.new("Shag", "CLOUDS")
    clumps.noise_scale = 0.10
    clumps.noise_depth = 3
    mod = body.modifiers.new("Shag", "DISPLACE")
    mod.texture = clumps
    mod.texture_coords = "GLOBAL"
    mod.strength = 0.055
    mod.mid_level = 0.5
    mod.vertex_group = "Shag"
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.modifier_apply(modifier="Shag")


CARD = None


def strand_texture():
    """A card's worth of fur: a few dozen strands, dark at the root, paler at the tips, and
    nothing at all between them. Painted here rather than loaded, so nothing is licensed."""
    import random
    import numpy as np

    size = 256
    rnd = random.Random(3)
    rgba = np.zeros((size, size, 4), dtype=np.float32)
    v = np.linspace(0.0, 1.0, size)[:, None]                 # row 0 is the root
    u = np.linspace(0.0, 1.0, size)[None, :]
    for _ in range(90):
        x0 = rnd.uniform(0.04, 0.96)
        x1 = x0 + rnd.uniform(-0.10, 0.10)
        top = rnd.uniform(0.55, 1.0)
        width = rnd.uniform(0.010, 0.026)
        shade = rnd.uniform(0.55, 1.35)
        centre = x0 + (x1 - x0) * v
        half = width * np.clip(1.0 - v / top, 0.0, 1.0)
        hit = (np.abs(u - centre) < half) & (v < top)
        tone = np.array((0.19, 0.145, 0.11)) * shade
        tip = np.clip(v / top, 0, 1)
        colour = tone[None, None, :] * (0.55 + 0.75 * tip)
        rgba[..., :3] = np.where(hit[..., None], colour, rgba[..., :3])
        rgba[..., 3] = np.where(hit, 1.0, rgba[..., 3])
    img = bpy.data.images.new("Fur_Strands", size, size, alpha=True)
    img.pixels.foreach_set(rgba.ravel())
    img.pack()
    return img


def card_material():
    """Alpha-cut, double-sided, so the glTF says MASK and Godot draws it with alpha scissor:
    no sorting, no halo, and the shadow has strands in it."""
    global CARD
    if CARD is not None:
        return CARD
    m = bpy.data.materials.new("Fur_Card")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = strand_texture()
    cut = nt.nodes.new("ShaderNodeMath")
    cut.operation = "ROUND"                     # the exporter's cue for alphaMode MASK
    nt.links.new(tex.outputs[0], bsdf.inputs["Base Color"])
    nt.links.new(tex.outputs[1], cut.inputs[0])
    nt.links.new(cut.outputs[0], bsdf.inputs["Alpha"])
    bsdf.inputs["Roughness"].default_value = 0.9
    m.blend_method = "CLIP"
    m.use_backface_culling = False
    CARD = m
    return m


def tuft(name, base, direction, length, width, material=None, curl=0.45, wide=(1, 0, 0)):
    """One lock of fur as a hair card: a strip of three quads bending in its own Y, wearing
    the strand texture, root at the base. Solid locks, however many, read as plates; a card
    shows a dozen strands and the hide between them."""
    import bmesh

    bm = bmesh.new()
    uv_layer = bm.loops.layers.uv.new("UVMap")
    rows = []
    n = 3
    for i in range(n + 1):
        t = i / n
        y = curl * length * t * t
        w = width * (1.0 - 0.25 * t)
        rows.append((bm.verts.new((-w, y, t * length)), bm.verts.new((w, y, t * length)), t))
    for (a0, b0, t0), (a1, b1, t1) in zip(rows, rows[1:]):
        f = bm.faces.new((a0, b0, b1, a1))
        for loop, (u, v) in zip(f.loops, ((0, t0), (1, t0), (1, t1), (0, t1))):
            loop[uv_layer].uv = (u, v)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    o = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(o)
    o.data.materials.append(card_material())
    o.matrix_world = gg.aim(base, direction, wide=wide)
    return o


# Root and tip colours of the strands. The hide under them is painted darker still, so a gap
# between cards reads as deep fur and not as skin.
PALETTES = {
    "coat": ((0.035, 0.026, 0.020), (0.30, 0.215, 0.145)),
    "dark": ((0.025, 0.019, 0.015), (0.17, 0.125, 0.090)),
    "pale": ((0.09, 0.065, 0.048), (0.50, 0.385, 0.270)),
}


def coat_plan(L):
    """Where the fur grows, how long, which way it lies, and what colour: the rules the cards
    are scattered by. A mane over the neck, shoulders and back; a coat everywhere else; shorter
    and paler down the chest; a brush of a tail; bare muzzle, palms and pads."""
    H = L["head"]
    M = gg.mirror
    limbs = []
    for side in (-1, 1):
        for a, b, kind, reach in (("shoulder", "elbow", "upper", 0.20), ("elbow", "wrist", "fore", 0.15),
                                  ("hip", "knee", "thigh", 0.22), ("knee", "ankle", "shin", 0.13), ("ankle", "foot", "foot", 0.10)):
            limbs.append((M(L[a], side), M(L[b], side), kind, reach, side))
    for i in range(3):
        limbs.append((L[f"tail{i}"], L[f"tail{i + 1}"], "tail", 0.11, 0))
    palms = [M(L["palm"], s) for s in (-1, 1)]
    ribs = L["ribs"]

    def closest(p):
        best = None
        for a, b, kind, reach, side in limbs:
            ab = b - a
            t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
            d = (p - (a + ab * t)).length
            if d < reach and (best is None or d / reach < best[0]):
                best = (d / reach, kind, ab.normalized(), side)
        return best

    def plan(p, n):
        q = p - H
        if p.z < 0.17 or min((p - c).length for c in palms) < 0.12:
            return None                                                   # pads and palms
        if q.length < 0.36 and q.y < -0.09:                               # the face and muzzle
            if q.y < -0.40 or (q.z < -0.06 and abs(q.x) < 0.11):
                return None                                               # nose, lips and the mouth
            # Short and lying flat, swept back off the nose: a face with fur on it, not bald.
            return {"density": 9.0, "length": 0.075, "flow": (q.x * 2.0, 1.0, 0.25), "palette": "coat", "lift": 8, "shape": 0, "width": 0.35}
        if q.length < 0.30:                                               # skull and cheek ruffs
            if abs(q.x) > 0.10 and q.z < 0.03:
                side = 1 if q.x > 0 else -1
                return {"density": 4.0, "length": 0.17, "flow": (side * 0.7, 0.7, -0.3), "palette": "coat", "lift": 35}
            return {"density": 3.0, "length": 0.11, "flow": (0, 1, 0.15), "palette": "coat", "lift": 18}
        if q.length < 0.52 and q.z < -0.08 and q.y > -0.10 and n.y < 0.25 and n.z < 0.3:  # throat: a pale bib
            return {"density": 3.0, "length": 0.11, "flow": (0, 0.15, -1), "palette": "pale", "lift": 15}
        limb = closest(p)
        if limb is not None:
            _d, kind, axis, side = limb
            if kind == "foot":
                return None
            outer = max(0.0, n.x * side) + max(0.0, n.y)                  # the outside and back of it
            length = {"upper": 0.16, "fore": 0.13, "thigh": 0.19, "shin": 0.12, "foot": 0.06, "tail": 0.20}[kind]
            if kind == "tail":
                outer = 0.0
            density = {"upper": 2.0, "fore": 1.8, "thigh": 2.2, "shin": 1.4, "foot": 0.8, "tail": 6.0}[kind]
            return {"density": density, "length": length * (1.0 + 0.6 * outer), "flow": tuple(axis + Vector((0, 0.2, -0.3))),
                    "palette": "dark" if kind == "tail" else "coat", "lift": 12 if kind != "tail" else 30}
        if n.y < -0.30:                                                   # chest and belly
            return {"density": 1.6, "length": 0.08 if p.z < ribs.z else 0.11, "flow": (0, -0.15, -1), "palette": "pale", "lift": 10}
        if p.z > ribs.z - 0.10:                                           # the mane, behind
            return {"density": 3.6, "length": 0.30, "flow": (n.x * 0.4, 0.5, -1), "palette": "dark", "lift": 28}
        return {"density": 2.2, "length": 0.17, "flow": (n.x * 0.3, 0.35, -1), "palette": "coat", "lift": 14}

    return plan


def head_parts(L, body):
    """Teeth, eyes, nose, ears, tongue: whatever must be sharp, bright or wet."""
    V = Vector
    H = L["head"]
    parts = []

    for side in (-1, 1):
        suffix = "L" if side < 0 else "R"
        parts.append(gg.create_ear(f"Ear_{suffix}", tuple(H + V((side * 0.105, 0.06, 0.10))), (side * 0.42, 0.22, 0.88), 0.30, side))
        parts.append(gg.uv(f"Eye_{suffix}", tuple(H + V((side * 0.075, -0.150, 0.020))), (0.0165,) * 3, AMBER, 10, 6))
        parts.append(gg.uv(f"Pupil_{suffix}", tuple(H + V((side * 0.074, -0.164, 0.019))), (0.0055, 0.004, 0.010), gg.PUPIL, 8, 5))

    parts += teeth(L, body)
    parts.append(gg.uv("Tusk_Nose", tuple(H + V((0, -0.462, -0.058))), (0.040, 0.030, 0.030), NOSE, 12, 8))
    tongue = gg.uv("Tusk_Tongue", tuple(H + V((0, -0.27, -0.140))), (0.036, 0.110, 0.012), MAW, 10, 6)
    tongue.rotation_euler = (math.radians(-12), 0, 0)
    parts.append(tongue)
    return parts


def crossings(body, x, y, top, bottom):
    """Every point where a vertical line through (x, y) crosses the hide, top down."""
    out = []
    origin = Vector((x, y, top))
    down = Vector((0, 0, -1))
    while origin.z > bottom:
        hit, loc, _n, _i = body.ray_cast(origin, down, distance=origin.z - bottom)
        if not hit:
            break
        out.append(loc)
        origin = loc + down * 1e-4
    return out


def teeth(L, body):
    """A wolf's teeth, set on its gums: found by dropping a line through the open jaws.

    Top down, a line through the muzzle crosses the hide four times — into the upper jaw, out
    of its gum, into the lower gum, out under the chin — so the second and third crossings are
    where the upper and lower teeth grow from. Wherever the jaws have closed there are only two
    and that tooth is left out, so nothing is ever stuck on the outside of the face.
    """
    H = L["head"]
    parts = []
    # (y, x, upper length, upper width, lower length, lower width, how pointed)
    stations = [
        (-0.425, 0.010, 0.036, 0.010, 0.030, 0.009, 0.45),       # incisors
        (-0.418, 0.024, 0.040, 0.010, 0.032, 0.009, 0.45),
        (-0.392, 0.036, 0.100, 0.016, 0.065, 0.013, 0.10),       # the fangs
        (-0.350, 0.042, 0.040, 0.011, 0.036, 0.010, 0.20),
        (-0.312, 0.046, 0.046, 0.012, 0.040, 0.011, 0.20),
        (-0.272, 0.049, 0.050, 0.013, 0.036, 0.011, 0.25),       # the carnassial
        (-0.232, 0.051, 0.032, 0.012, 0.030, 0.010, 0.35),
    ]
    for side in (-1, 1):
        suffix = "L" if side < 0 else "R"
        for k, (y, x, ul, uw, ll, lw, point) in enumerate(stations):
            upper = lower = None
            for shrink in (1.0, 0.8, 0.6, 0.4):
                c = crossings(body, H.x + side * x * shrink, H.y + y, H.z + 0.40, H.z - 0.45)
                # The second crossing is the upper gum if it is still up in the muzzle and not
                # the underside of a closed jaw; the third, if there is one, is the lower gum.
                if upper is None and len(c) >= 2 and c[1].z > H.z - 0.15:
                    upper = c[1]
                if lower is None and len(c) >= 4 and c[1].z - c[2].z > 0.008:
                    lower = c[2]
                if upper is not None and lower is not None:
                    break
            if upper is not None:
                parts.append(gg_tooth(f"Tusk_U_{suffix}_{k}", upper + Vector((0, 0, 0.010)), (side * -0.10, 0.10, -1.0), ul, uw, FANG, point))
            if lower is not None:
                parts.append(gg_tooth(f"Tusk_D_{suffix}_{k}", lower - Vector((0, 0, 0.010)), (side * -0.10, -0.12, 1.0), ll, lw, FANG, point))
            if upper is not None and lower is not None:
                L.setdefault("gums", []).append((upper.copy(), lower.copy()))
    return parts


def gg_tooth(name, root, direction, length, width, material, point):
    return gg.tooth(name, root, direction, length, width, material, point=point, curl=0.12)


def talons(L):
    """Claws, grouped by the bone that swings them."""
    V = Vector
    M = gg.mirror
    groups = {}
    for side in (-1, 1):
        suffix = "L" if side < 0 else "R"
        pa, ft = M(L["palm"], side), M(L["foot"], side)

        hand = []
        for i, dx in enumerate((-0.066, -0.022, 0.022, 0.066)):
            tip = pa + V((dx * 1.25, -0.125, -0.165))
            claw = gg.cone(f"Talon_H{suffix}_{i}", (0, 0, 0), 0.020, 0.002, 0.15, TALON, vertices=6)
            claw.matrix_world = gg.aim(tip, (dx * 0.4, -0.55, -0.83))
            hand.append(claw)
        groups[f"hand_{suffix}"] = hand

        foot = []
        for i, dx in enumerate((-0.066, -0.022, 0.022, 0.066)):
            claw = gg.cone(f"Talon_F{suffix}_{i}", (0, 0, 0), 0.018, 0.002, 0.11, TALON, vertices=6)
            claw.matrix_world = gg.aim(ft + V((dx, -0.235, -0.02)), (dx * 0.3, -0.92, -0.38))
            foot.append(claw)
        groups[f"foot_{suffix}"] = foot
    return groups


def join(objs, name):
    """Many small meshes on one bone become one mesh: a hundred locks of fur should not be a
    hundred draw calls."""
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    merged = bpy.context.view_layer.objects.active
    merged.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return merged


def paint(body, ears, L):
    """The coat's colour, with occlusion baked under it: grey-brown, dark along the back and
    down the limbs, paler and warmer where the chest and belly show through."""
    hide = gg.skin_material()
    scene = bpy.context.scene
    engine = scene.render.engine
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 48
    scene.render.bake.target = "VERTEX_COLORS"

    # Dark: this is the undercoat seen between the cards, not the coat itself.
    coat = Vector((0.085, 0.063, 0.047))
    dark = Vector((0.040, 0.031, 0.025))
    chest = Vector((0.260, 0.180, 0.125))
    pink = Vector((0.300, 0.150, 0.130))
    lips = Vector((0.035, 0.022, 0.020))
    maw = Vector((0.24, 0.06, 0.06))
    gums = L.get("gums", [])

    def mouth(v):
        """1 inside the mouth, 0 outside it, by the gums the teeth found."""
        near = [g for g in gums if abs(g[0].y - v.y) < 0.05]
        if not near:
            return 0.0
        up, lo = min(near, key=lambda g: abs(g[0].y - v.y))
        if abs(v.x) > abs(up.x) + 0.045:
            return 0.0
        return 1.0 if lo.z - 0.015 < v.z < up.z + 0.015 else 0.0
    middle = L["ribs"].lerp(L["belly"], 0.5)
    muzzle = L["head"] + Vector((0, -0.40, -0.08))

    def mottle(v):
        return math.sin(v.x * 11.0 + 0.7) * math.sin(v.y * 9.0 + 1.9) * math.sin(v.z * 8.0 + 0.3)

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

        inner = gg.EAR_INNER.get(obj.name)
        world = obj.matrix_world
        for poly in mesh.polygons:
            facing_back = (world.to_3x3() @ poly.normal).y
            for li in poly.loop_indices:
                v = world @ mesh.vertices[mesh.loops[li].vertex_index].co
                ao = attr.data[li].color[0] if baked else 1.0

                if inner is not None:
                    tone = pink.lerp(dark, 0.35) if li in inner else dark
                else:
                    tone = coat.copy()
                    front = max(0.0, min(1.0, (middle.y - v.y) * 4.0)) * max(0.0, 1 - abs(v.z - middle.z) * 1.7) * max(0.0, 1 - abs(v.x) * 2.2)
                    tone = tone.lerp(chest, min(1.0, front * 1.5))
                    tone = tone.lerp(dark, max(0.0, facing_back) * 0.75)                  # along the back
                    tone = tone.lerp(dark, max(0.0, min(1.0, (abs(v.x) - 0.42) * 3.5)) * 0.6)   # down the arms
                    tone = tone.lerp(dark, max(0.0, 1 - v.z / 0.5) * 0.55)                # and the shins
                    tone = tone.lerp(dark, max(0.0, 1 - (v - muzzle).length / 0.13) * 0.6)
                    tone = tone.lerp(dark, max(0.0, mottle(v)) * 0.35)
                    inside = mouth(v)
                    if inside:
                        # Black at the lips, red where the inside shows.
                        tone = maw if abs(v.x) < 0.035 or (v - L["head"]).y > -0.30 else lips

                shade = 0.10 + 0.90 * (ao ** 2.2)
                attr.data[li].color = (tone.x * shade, tone.y * shade, tone.z * shade, 1.0)

    scene.render.engine = engine


def animate(arm):
    """Four clips in the goblin's convention: +X swings a bone forward."""
    stance = {
        "upper_arm_R": (0.10, 0.0, 0.45), "forearm_R": (0.85, 0.0, 0.0),
        "upper_arm_L": (0.10, 0.0, -0.45), "forearm_L": (0.85, 0.0, 0.0),
        "chest": (0.10, 0.0, 0.0), "head": (-0.10, 0.0, 0.0), "pelvis": (0.0, 0.0, 0.0), "neck": (0.0, 0.0, 0.0),
    }

    def held(**changes):
        row = dict(stance)
        row.update(changes)
        return row

    # Heaving: the whole ribcage works, the head swings looking for an opening.
    gg.add_action(arm, "werewolf_idle_combat", 24, [
        (1, held(), {"root": (0, 0, 0)}),
        (7, held(chest=(0.17, 0.0, 0.04), head=(-0.16, 0.10, 0.0), forearm_R=(0.80, 0.0, 0.0), forearm_L=(0.62, 0.0, 0.0)), {"root": (0, -0.025, 0)}),
        (13, held(), {"root": (0, 0, 0)}),
        (19, held(chest=(0.17, 0.0, -0.04), head=(-0.16, -0.10, 0.0), forearm_R=(0.62, 0.0, 0.0), forearm_L=(0.80, 0.0, 0.0)), {"root": (0, -0.025, 0)}),
        (24, held(), {"root": (0, 0, 0)}),
    ])

    # It bites. Rears back with both arms wide, then the whole body goes in behind the jaws.
    gg.add_action(arm, "werewolf_attack_melee", 14, [
        (1, held()),
        (5, held(chest=(-0.30, 0.0, 0.0), neck=(-0.20, 0.0, 0.0), head=(-0.30, 0.0, 0.0),
                 upper_arm_R=(1.30, 0.0, 0.0), upper_arm_L=(1.30, 0.0, 0.0), forearm_R=(0.90, 0.0, 0.0), forearm_L=(0.90, 0.0, 0.0)), {"root": (0, 0, 0)}),
        (8, held(pelvis=(0.25, 0.0, 0.0), chest=(0.55, 0.0, 0.0), neck=(0.30, 0.0, 0.0), head=(0.20, 0.0, 0.0),
                 upper_arm_R=(0.75, 0.0, 0.0), upper_arm_L=(0.75, 0.0, 0.0), forearm_R=(0.25, 0.0, 0.0), forearm_L=(0.25, 0.0, 0.0)), {"root": (0, 0, -0.10)}),
        (11, held(chest=(0.30, 0.0, 0.0)), {"root": (0, 0, -0.03)}),
        (14, held(), {"root": (0, 0, 0)}),
    ])

    def lope(phase):
        a, b = (1, -1) if phase else (-1, 1)
        return {
            "chest": (0.42, 0.0, 0.0), "head": (-0.35, 0.0, 0.0),
            "thigh_R": (0.80 * a, 0.0, 0.0), "shin_R": (-0.60 if a < 0 else 0.25, 0.0, 0.0),
            "thigh_L": (0.80 * b, 0.0, 0.0), "shin_L": (-0.60 if b < 0 else 0.25, 0.0, 0.0),
            "upper_arm_R": (0.85 * b, 0.0, 0.0), "forearm_R": (0.70, 0.0, 0.0),
            "upper_arm_L": (0.85 * a, 0.0, 0.0), "forearm_L": (0.70, 0.0, 0.0),
        }

    gg.add_action(arm, "werewolf_run", 20, [
        (1, lope(True), {"root": (0, 0, 0)}),
        (6, lope(False), {"root": (0, 0.05, 0)}),
        (11, lope(False), {"root": (0, 0, 0)}),
        (16, lope(True), {"root": (0, 0.05, 0)}),
        (20, lope(True), {"root": (0, 0, 0)}),
    ])

    limp = {"upper_arm_R": (0.0, 0.0, 0.0), "forearm_R": (0.2, 0.0, 0.0),
            "upper_arm_L": (0.0, 0.0, 0.0), "forearm_L": (0.2, 0.0, 0.0)}

    # As the goblins': the root walks forward as it tips back, so he dies on his own square
    # instead of laid out across the one behind it.
    hips = arm.data.bones["pelvis"].head_local.z

    def fall(tip, lift):
        return {"root": (0, lift, hips * math.sin(-tip) * 1.15)}

    end = dict(limp, root=(-1.50, 0.0, 0.0), chest=(-0.15, 0.0, 0.0), head=(-0.25, 0.0, 0.0), neck=(0.0, 0.0, 0.0),
               upper_arm_R=(-0.30, 0.0, 0.60), upper_arm_L=(-0.30, 0.0, -0.60), thigh_R=(0.35, 0.0, 0.0), thigh_L=(0.10, 0.0, 0.0))
    gg.add_action(arm, "werewolf_death", 22, [
        (1, held(root=(0.0, 0.0, 0.0), thigh_R=(0.0, 0.0, 0.0), thigh_L=(0.0, 0.0, 0.0)), {"root": (0, 0, 0)}),
        (6, held(root=(-0.25, 0.0, 0.0), chest=(-0.40, 0.0, 0.0), head=(-0.45, 0.0, 0.0), upper_arm_R=(1.2, 0.0, 0.0), upper_arm_L=(1.2, 0.0, 0.0)), fall(-0.25, 0.0)),
        (13, dict(limp, root=(-1.05, 0.0, 0.0), chest=(-0.20, 0.0, 0.0), head=(0.10, 0.0, 0.0), neck=(0.0, 0.0, 0.0), thigh_R=(0.45, 0.0, 0.0), thigh_L=(0.25, 0.0, 0.0)), fall(-1.05, 0.18)),
        (18, end, fall(-1.50, 0.30)),
        (22, end, fall(-1.50, 0.30)),
    ])

    # He trips with his jaws, as wolves do: down on the forelimbs and a snap at the legs.
    gg.add_action(arm, "werewolf_trip", 14, [
        (1, held(), {"root": (0, 0, 0)}),
        (5, held(pelvis=(0.35, 0.0, 0.0), chest=(0.55, 0.0, 0.0), neck=(-0.35, 0.0, 0.0), head=(-0.30, 0.0, 0.0),
                 upper_arm_R=(0.70, 0.0, 0.30), upper_arm_L=(0.70, 0.0, -0.30), forearm_R=(0.30, 0.0, 0.0), forearm_L=(0.30, 0.0, 0.0),
                 thigh_R=(0.55, 0.0, 0.0), thigh_L=(0.55, 0.0, 0.0)), {"root": (0, -0.20, 0)}),
        (8, held(pelvis=(0.40, 0.0, 0.0), chest=(0.75, 0.0, 0.0), neck=(0.30, 0.0, 0.0), head=(0.35, 0.0, 0.0),
                 upper_arm_R=(0.85, 0.0, 0.20), upper_arm_L=(0.85, 0.0, -0.20), forearm_R=(0.20, 0.0, 0.0), forearm_L=(0.20, 0.0, 0.0),
                 thigh_R=(0.60, 0.0, 0.0), thigh_L=(0.60, 0.0, 0.0)), {"root": (0, -0.24, 0.16)}),
        (11, held(chest=(0.30, 0.0, 0.0)), {"root": (0, -0.06, 0.04)}),
        (14, held(), {"root": (0, 0, 0)}),
    ])

    # And shoves with everything: both arms and a shoulder, off the back legs.
    gg.add_action(arm, "werewolf_shove", 14, [
        (1, held(), {"root": (0, 0, 0)}),
        (5, held(chest=(-0.20, 0.0, 0.0), upper_arm_R=(0.20, 0.0, 0.30), upper_arm_L=(0.20, 0.0, -0.30),
                 forearm_R=(1.40, 0.0, 0.0), forearm_L=(1.40, 0.0, 0.0), thigh_R=(0.35, 0.0, 0.0), thigh_L=(0.35, 0.0, 0.0)), {"root": (0, -0.08, -0.06)}),
        (8, held(pelvis=(0.20, 0.0, 0.0), chest=(0.50, 0.0, 0.0), head=(-0.30, 0.0, 0.0),
                 upper_arm_R=(1.25, 0.0, 0.15), upper_arm_L=(1.25, 0.0, -0.15), forearm_R=(0.35, 0.0, 0.0), forearm_L=(0.35, 0.0, 0.0),
                 thigh_R=(-0.35, 0.0, 0.0), thigh_L=(0.50, 0.0, 0.0)), {"root": (0, -0.04, 0.24)}),
        (11, held(chest=(0.20, 0.0, 0.0)), {"root": (0, 0, 0.07)}),
        (14, held(), {"root": (0, 0, 0)}),
    ])

    gg.pose_reset(arm)
    arm.animation_data.action = None


def build(output):
    gg.clear_scene()
    gg.setup_scene()

    L = frame()
    body = grow_body(L)
    face = head_parts(L, body)
    claws = talons(L)
    ears = [o for o in face if o.name.startswith("Ear_")]

    paint(body, ears, L)
    arm = gg.create_armature(L)

    gg.auto_skin(body, arm)
    gg.fill_unweighted(body, arm)
    face_mesh = join([o for o in face if o not in ears], "Face")
    gg.rigid_skin(face_mesh, arm, "head")
    for ear in ears:
        gg.rigid_skin(ear, arm, "head")
    coat = cards.grow(body, "Fur_Coat", cards.atlas("Wolf", PALETTES), coat_plan(L), 5200, seed=9, segments=3)
    cards.skin_like(coat, body, arm)

    # Fur on the backs of the ears, and a tuft at the root: an ear that is bare outside is a horn.
    def ear_plan(p, n):
        return {"density": 1.0, "length": 0.07, "flow": (0, 0.2, 1.0), "palette": "dark", "lift": 10, "shape": 0} if n.y > -0.1 else None

    for ear in ears:
        tufts = cards.grow(ear, f"Fur_{ear.name}", cards.atlas(f"WolfEar_{ear.name}", {"dark": PALETTES["dark"]}, size=256),
                           ear_plan, 90, seed=13, segments=2)
        cards.ROOTS.pop(tufts.name, None)
        gg.rigid_skin(tufts, arm, "head")
    talon_meshes = []
    for bone, set_ in claws.items():
        talon_meshes.append(join(set_, f"Talons_{bone}"))
        gg.rigid_skin(talon_meshes[-1], arm, bone)

    # The hide is painted, so its recipe reads the vertex colours; but it is fur, not skin.
    surface.RECIPES["Goblin_Hide"] = lambda c, m, r: surface.fur(None)
    surface.finish([("hide", [body] + ears, 1024, False), ("kit", [face_mesh] + talon_meshes, 1024, True)])

    animate(arm)

    bpy.ops.wm.save_as_mainfile(filepath=os.path.splitext(os.path.abspath(output))[0] + ".blend")
    gg.export_glb(output)
    print("\n=== werewolf ===")
    gg.validate_glb(output, arm, creature="werewolf")


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        raise RuntimeError("Usage: blender -b --factory-startup --python generate_werewolf.py -- OUTPUT_GLB")
    build(args[-1])

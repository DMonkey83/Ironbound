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

FUR = gg.mat("Wolf_Fur", (0.105, 0.080, 0.064), 0.0, 0.95)
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
    chain(off(H, 0, -0.10, -0.115), off(H, 0, -0.385, -0.190), 0.066, 0.026, 7)  # lower jaw, open
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

    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = meta
    meta.select_set(True)
    bpy.ops.object.convert(target="MESH")
    body = bpy.context.view_layer.objects.active
    body.name = "Body"
    gg.smooth(body)
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


def tuft(name, base, direction, length, width, material=FUR):
    """One lock of fur: a flattened spike lying along the body."""
    bpy.ops.mesh.primitive_cone_add(vertices=5, radius1=width, radius2=width * 0.04, depth=length)
    o = bpy.context.object
    o.name = name
    o.scale = (1.0, 0.42, 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    for v in o.data.vertices:
        v.co.z += length / 2
    o.matrix_world = gg.aim(base, direction, wide=(1, 0, 0))
    gg.apply_mat(o, material)
    return o


def pelt(L):
    """The coat, as locks grouped by the bone that carries them.

    Metaballs give a smooth hide, and a smooth wolf is a rubber toy. The mane is what makes the
    silhouette: up over the skull, thick round the neck and shoulders, down the spine, with
    ruffs at the cheeks, elbows and haunches, and a brush of a tail.
    """
    V = Vector
    M = gg.mirror
    groups = {"head": [], "chest": [], "spine": [], "pelvis": [],
              "forearm_L": [], "forearm_R": [], "thigh_L": [], "thigh_R": []}
    count = [0]

    def lock(bone, base, direction, length, width):
        count[0] += 1
        groups[bone].append(tuft(f"Fur_{bone}_{count[0]}", base, direction, length, width))

    H = L["head"]
    # Crown and nape: swept back from the brow.
    for i in range(5):
        t = i / 4
        for dx in (-0.09, -0.03, 0.03, 0.09):
            lock("head", H + V((dx, -0.02 + 0.20 * t, 0.12 - 0.05 * t)), (dx * 1.2, 0.75, 0.55 - 0.5 * t), 0.24 + 0.05 * t, 0.060)
    # Cheek ruffs, flaring out and back.
    for side in (-1, 1):
        for j in range(4):
            lock("head", H + V((side * 0.15, 0.0 + 0.03 * j, -0.06 - 0.035 * j)), (side * 0.85, 0.45, -0.35 - 0.1 * j), 0.20, 0.055)

    # The mane over the shoulders and down the spine, in rows across the back.
    spine = [L["neck_b"], L["neck_a"], L["back"], L["ribs"] + V((0, 0.30, -0.02)), L["belly"] + V((0, 0.19, 0.0)), L["pelvis"] + V((0, 0.17, 0.02))]
    for i in range(len(spine) - 1):
        a, b = spine[i], spine[i + 1]
        bone = "chest" if i < 3 else "spine" if i < 4 else "pelvis"
        across = (0.30, 0.34, 0.30, 0.20, 0.15)[i]
        for step in range(3):
            at = a.lerp(b, step / 3)
            for k in range(-2, 3):
                dx = k / 2 * across
                lift = 0.10 * (1 - abs(k) / 2.5) + 0.05
                lock(bone, at + V((dx, 0.08 + 0.02 * abs(k), lift)), (dx * 0.5, 0.42, -0.80), 0.30 - 0.025 * i, 0.058)

    # Shoulders: a ruff standing off each one.
    for side in (-1, 1):
        sh = M(L["shoulder"], side)
        for j in range(5):
            a = j / 4 * math.pi
            lock("chest", sh + V((side * 0.08 * math.sin(a), 0.13 * math.cos(a), 0.14)), (side * 0.55, 0.45 * math.cos(a) + 0.2, -0.55), 0.24, 0.058)

        # Elbows and the backs of the forearms.
        el, wr = M(L["elbow"], side), M(L["wrist"], side)
        bone = "forearm_L" if side < 0 else "forearm_R"
        for j in range(5):
            at = el.lerp(wr, j / 6) + V((side * 0.05, 0.085, 0.0))
            lock(bone, at, (side * 0.35, 0.70, -0.45), 0.21 - 0.015 * j, 0.052)

        # Haunches.
        hp, kn = M(L["hip"], side), M(L["knee"], side)
        bone = "thigh_L" if side < 0 else "thigh_R"
        for j in range(5):
            at = hp.lerp(kn, j / 5) + V((side * 0.10, 0.14, 0.0))
            lock(bone, at, (side * 0.30, 0.65, -0.60), 0.23, 0.060)

    # Tail: a brush hanging behind, in overlapping locks round a core.
    root = L["pelvis"] + V((0, 0.19, -0.04))
    down = V((0, 0.55, -0.83)).normalized()
    for i in range(6):
        at = root + down * (0.10 * i)
        for a in range(5):
            ang = a * 2 * math.pi / 5 + i * 0.6
            out = V((math.cos(ang) * 0.06, math.sin(ang) * 0.03, 0))
            lock("pelvis", at + out, down + out * 3.0, 0.30 - 0.02 * i, 0.058)

    return groups


def head_parts(L):
    """Teeth, eyes, nose, ears, tongue: whatever must be sharp, bright or wet."""
    V = Vector
    H = L["head"]
    parts = []

    for side in (-1, 1):
        suffix = "L" if side < 0 else "R"
        parts.append(gg.create_ear(f"Ear_{suffix}", tuple(H + V((side * 0.105, 0.06, 0.10))), (side * 0.42, 0.22, 0.88), 0.30, side))
        parts.append(gg.uv(f"Eye_{suffix}", tuple(H + V((side * 0.075, -0.150, 0.020))), (0.0165,) * 3, AMBER, 10, 6))
        parts.append(gg.uv(f"Pupil_{suffix}", tuple(H + V((side * 0.074, -0.164, 0.019))), (0.0055, 0.004, 0.010), gg.PUPIL, 8, 5))

        # Canines: long ones down from the upper jaw, shorter ones up from the lower.
        parts.append(gg.cone(f"Tusk_Upper_{suffix}", tuple(H + V((side * 0.042, -0.405, -0.125))), 0.016, 0.002, 0.085, FANG, (math.pi, 0, 0), 8))
        parts.append(gg.cone(f"Tusk_Lower_{suffix}", tuple(H + V((side * 0.030, -0.355, -0.150))), 0.012, 0.002, 0.060, FANG, (math.radians(-12), 0, 0), 8))
        for j in range(5):                                                       # the rest, along each side
            y = -0.20 - 0.038 * j
            parts.append(gg.cone(f"Tusk_U_{suffix}_{j}", tuple(H + V((side * (0.060 - 0.004 * j), y, -0.098 - 0.009 * j))), 0.010, 0.002, 0.034, FANG, (math.pi, 0, 0), 6))
            parts.append(gg.cone(f"Tusk_D_{suffix}_{j}", tuple(H + V((side * (0.046 - 0.004 * j), y + 0.02, -0.128 - 0.011 * j))), 0.009, 0.002, 0.028, FANG, vertices=6))

    parts.append(gg.uv("Tusk_Nose", tuple(H + V((0, -0.468, -0.062))), (0.030, 0.020, 0.022), NOSE, 10, 6))
    tongue = gg.uv("Tusk_Tongue", tuple(H + V((0, -0.27, -0.132))), (0.038, 0.120, 0.016), MAW, 10, 6)
    tongue.rotation_euler = (math.radians(-15), 0, 0)
    parts.append(tongue)
    return parts


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

    coat = Vector((0.175, 0.132, 0.105))
    dark = Vector((0.075, 0.058, 0.048))
    chest = Vector((0.520, 0.335, 0.225))
    pink = Vector((0.300, 0.150, 0.130))
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
                    tone = pink if li in inner else dark
                else:
                    tone = coat.copy()
                    front = max(0.0, min(1.0, (middle.y - v.y) * 4.0)) * max(0.0, 1 - abs(v.z - middle.z) * 1.7) * max(0.0, 1 - abs(v.x) * 2.2)
                    tone = tone.lerp(chest, min(1.0, front * 1.5))
                    tone = tone.lerp(dark, max(0.0, facing_back) * 0.75)                  # along the back
                    tone = tone.lerp(dark, max(0.0, min(1.0, (abs(v.x) - 0.42) * 3.5)) * 0.6)   # down the arms
                    tone = tone.lerp(dark, max(0.0, 1 - v.z / 0.5) * 0.55)                # and the shins
                    tone = tone.lerp(dark, max(0.0, 1 - (v - muzzle).length / 0.13) * 0.6)
                    tone = tone.lerp(dark, max(0.0, mottle(v)) * 0.35)

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
    face = head_parts(L)
    fur = pelt(L)
    claws = talons(L)
    ears = [o for o in face if o.name.startswith("Ear_")]

    paint(body, ears, L)
    arm = gg.create_armature(L)

    gg.auto_skin(body, arm)
    gg.rigid_skin(join([o for o in face if o not in ears], "Face"), arm, "head")
    for ear in ears:
        gg.rigid_skin(ear, arm, "head")
    for bone, locks in fur.items():
        if locks:
            gg.rigid_skin(join(locks, f"Fur_{bone}"), arm, bone)
    for bone, set_ in claws.items():
        gg.rigid_skin(join(set_, f"Talons_{bone}"), arm, bone)

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

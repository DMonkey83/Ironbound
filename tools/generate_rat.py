"""A dire rat: the orc guard's pet, the size of a large dog.

    blender -b --factory-startup --python tools/generate_rat.py -- <out.glb>

Everything that is not specifically a rat is borrowed from generate_goblin.py and the modules
beside it: metaball flesh, the export and its self-check, the texture bake (`surface.py`) and
hair cards (`cards.py`). What is written here is the animal.

It is a four-legged thing on a skeleton with a biped's bone names, so that nothing in the game
has to know: the forelegs are `upper_arm/forearm/hand`, the hind legs `thigh/shin/foot`, and the
spine runs forward instead of up. The four clips the game asks for are written for that shape.
It stands with its head and shoulders raised, hunched like a rat on alert, rather than flat
along the ground: the game sizes a creature by its height, and a long low animal would be
stretched across three squares.
"""
import math
import os
import random
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import generate_goblin as gg  # noqa: E402
import surface  # noqa: E402
import cards  # noqa: E402

TOOTH = gg.mat("Rat_Teeth", (0.78, 0.58, 0.20), 0.0, 0.45)
EYE = gg.mat("Rat_Eye", (0.02, 0.012, 0.012), 0.0, 0.12)
surface.RECIPES["Rat_Teeth"] = lambda c, m, r: surface.bone(c)
surface.RECIPES["Rat_Eye"] = lambda c, m, r: surface.wet(c, 0.12)

# Root and tip colours of the coat's strands: dun and dirty, with a paler belly.
PALETTES = {
    "coat": ((0.075, 0.062, 0.050), (0.32, 0.27, 0.21)),
    "pale": ((0.14, 0.12, 0.10), (0.50, 0.44, 0.36)),
}


def frame():
    """Landmarks, right-hand side, with the goblin's names. Forward is -Y."""
    V = Vector
    L = {
        "pelvis": V((0, 0.32, 0.31)), "belly": V((0, 0.10, 0.34)), "ribs": V((0, -0.10, 0.41)),
        "back": V((0, 0.04, 0.50)), "neck_a": V((0, -0.24, 0.52)), "neck_b": V((0, -0.34, 0.56)),
        "head": V((0, -0.46, 0.57)),
        "shoulder": V((0.12, -0.17, 0.38)), "elbow": V((0.14, -0.20, 0.21)),
        "wrist": V((0.13, -0.25, 0.07)), "palm": V((0.13, -0.29, 0.03)), "fingertip": V((0.13, -0.36, 0.01)),
        "hip": V((0.14, 0.30, 0.29)), "knee": V((0.17, 0.16, 0.19)), "ankle": V((0.15, 0.32, 0.08)),
        "foot": V((0.15, 0.23, 0.03)), "toe": V((0.15, 0.12, 0.01)),
    }
    L["r"] = 1.0
    L["hs"] = 1.0
    L["bulk"] = 1.0
    L["hob"] = False
    L["variant"] = "dire-rat"
    L["lean"] = 0.0
    return L


def grow_body(L):
    mb = bpy.data.metaballs.new("RatMeta")
    mb.resolution = 0.012
    mb.threshold = 0.6
    meta = bpy.data.objects.new("RatMeta", mb)
    bpy.context.collection.objects.link(meta)
    M = gg.mirror
    V = Vector

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

    # The body: a pear, heavy at the haunches, rising to a hunched back over the shoulders.
    ball(L["pelvis"], (0.15, 0.17, 0.15))
    ball(L["belly"], (0.14, 0.15, 0.14))
    ball(L["ribs"], (0.13, 0.13, 0.14))
    ball(L["back"], (0.11, 0.14, 0.09))
    chain(L["neck_a"], L["neck_b"], 0.085, 0.07, 3)

    # The head: a wedge, broad behind the eyes and running to a blunt, whiskered snout.
    H = L["head"]
    ball(H, (0.075, 0.08, 0.065))
    chain(H + V((0, -0.04, -0.01)), H + V((0, -0.19, -0.035)), 0.06, 0.028, 5)
    ball(H + V((0, -0.205, -0.04)), (0.024, 0.02, 0.02))                          # the nose pad
    for side in (-1, 1):
        ball(H + V((side * 0.05, -0.02, -0.025)), (0.045, 0.05, 0.04))            # cheek
    ball(H + V((0, -0.10, -0.07)), (0.04, 0.07, 0.022))                           # the lower jaw

    # Legs: forelegs thin and close under the chest, hind legs heavy-thighed and folded.
    for side in (-1, 1):
        sh, el, wr, pa = (M(L[k], side) for k in ("shoulder", "elbow", "wrist", "palm"))
        ball(sh, 0.075)
        chain(sh, el, 0.065, 0.048, 6)
        chain(el, wr, 0.046, 0.032, 6)
        ball(pa, (0.03, 0.045, 0.018))
        for dx in (-0.022, 0.0, 0.022):
            chain(V((pa.x + dx, pa.y - 0.02, pa.z)), V((pa.x + dx * 1.3, pa.y - 0.06, 0.012)), 0.012, 0.008, 2)

        hp, kn, an, ft = (M(L[k], side) for k in ("hip", "knee", "ankle", "foot"))
        ball(hp.lerp(kn, 0.45) + V((side * 0.02, 0.02, 0.0)), (0.09, 0.12, 0.11))   # the haunch
        chain(kn, an, 0.055, 0.036, 6)
        chain(an, ft, 0.034, 0.030, 4)
        ball(ft, (0.032, 0.07, 0.018))
        for dx in (-0.025, 0.0, 0.025):
            chain(V((ft.x + dx, ft.y - 0.05, ft.z)), V((ft.x + dx * 1.3, ft.y - 0.10, 0.012)), 0.012, 0.008, 2)

    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = meta
    meta.select_set(True)
    bpy.ops.object.convert(target="MESH")
    body = bpy.context.view_layer.objects.active
    body.name = "Body"
    gg.smooth(body)
    thin = body.modifiers.new("Thin", "DECIMATE")
    thin.ratio = 0.35
    bpy.ops.object.modifier_apply(modifier="Thin")

    drop = min(v.co.z for v in body.data.vertices)
    for v in body.data.vertices:
        v.co.z -= drop
    for value in L.values():
        if isinstance(value, Vector):
            value.z -= drop
    return body


def tail(L):
    """Long, bare and ringed: a lofted tube from the rump, curving down to the ground and out."""
    V = Vector
    start = L["pelvis"] + V((0, 0.14, -0.02))
    sections = []
    n = 24
    for i in range(n + 1):
        t = i / n
        p = start + V((0.12 * math.sin(t * 2.6), 0.62 * t, -0.40 * math.sin(t * math.pi * 0.55) + 0.08 * t * t))
        p.z = max(p.z, 0.025)
        r = 0.035 * (1 - t) ** 0.9 + 0.004
        r *= 1.0 + 0.08 * math.sin(t * 90)                                        # the rings
        sections.append([p + V((r * math.cos(a), 0, r * math.sin(a))) for a in (k * 2 * math.pi / 8 for k in range(8))])
    sections.append([sections[-1][0].lerp(sections[-1][4], 0.5)] * 8)
    return gg.loft("Tail", sections, gg.SKIN)


def head_parts(L, body):
    """Eyes, ears, the yellow front teeth, the nose and whiskers."""
    V = Vector
    H = L["head"]
    parts = []
    for side in (-1, 1):
        suffix = "L" if side < 0 else "R"
        parts.append(gg.uv(f"Eye_{suffix}", tuple(H + V((side * 0.048, -0.075, 0.025))), (0.016, 0.016, 0.016), EYE, 10, 6))
        # A rat's ear: a round cup, thin, standing up and back behind the eye.
        ear = gg.dome(f"Ear_{suffix}", (0, 0, 0), (0.045, 0.012, 0.05), gg.SKIN, -0.6, 14, 8)
        ear.rotation_euler = (math.radians(80), 0, side * math.radians(-30))
        ear.location = H + V((side * 0.06, 0.02, 0.07))
        parts.append(ear)
        for j in range(4):                                                         # whiskers
            root = H + V((side * 0.03, -0.17, -0.02 + 0.008 * j))
            tip = root + V((side * 0.16, 0.02 + 0.02 * j, -0.01 + 0.025 * (j - 1.5)))
            parts.append(gg.limb(f"Tusk_Whisker_{suffix}_{j}", root, tip, 0.0018, gg.HAIR))
    # The incisors: two long and two short, yellow, at the very front of the jaws.
    for side in (-1, 1):
        parts.append(gg.tooth(f"Tusk_Upper_{side}", H + V((side * 0.008, -0.185, -0.045)), (0, -0.25, -1.0), 0.045, 0.008, TOOTH, point=0.6, curl=0.2))
        parts.append(gg.tooth(f"Tusk_Lower_{side}", H + V((side * 0.007, -0.165, -0.085)), (0, -0.35, 1.0), 0.035, 0.007, TOOTH, point=0.6, curl=0.2))
    return parts


def armature(L):
    """The goblin's bones over a rat's body. The spine runs forward, so its bones are rolled
    with Z up rather than Z forward: X is still the side-to-side hinge, so a positive X turn
    nods a spine bone *up*: the tables below use negative turns to bring the head down."""
    bpy.ops.object.armature_add(enter_editmode=True, location=(0, 0, 0))
    arm = bpy.context.object
    arm.name = "Rat_Armature"
    arm.data.name = "Rat_ArmatureData"
    eb = arm.data.edit_bones
    eb.remove(eb[0])
    M = gg.mirror

    def bone(name, head, tail, parent=None, roll=Vector((0, -1, 0))):
        b = eb.new(name)
        b.head = head
        b.tail = tail
        if parent:
            b.parent = eb.get(parent)
        b.align_roll(roll)
        return b

    up = Vector((0, 0, 1))
    waist = L["pelvis"].lerp(L["belly"], 0.55)
    sternum = L["belly"].lerp(L["ribs"], 0.6)
    bone("root", (0, 0, 0), (0, 0, 0.15))
    bone("pelvis", L["pelvis"] + Vector((0, 0.08, 0)), waist, "root", up)
    bone("spine", waist, sternum, "pelvis", up)
    bone("chest", sternum, L["neck_a"], "spine", up)
    bone("neck", L["neck_a"], L["neck_b"], "chest", up)
    bone("head", L["neck_b"], L["head"] + Vector((0, -0.20, -0.04)), "neck", up)
    for side, sgn in (("L", -1), ("R", 1)):
        sh, el, wr, tip = (M(L[k], sgn) for k in ("shoulder", "elbow", "wrist", "fingertip"))
        hp, kn, an, toe = (M(L[k], sgn) for k in ("hip", "knee", "ankle", "toe"))
        bone(f"upper_arm_{side}", sh, el, "chest")
        bone(f"forearm_{side}", el, wr, f"upper_arm_{side}")
        bone(f"hand_{side}", wr, tip, f"forearm_{side}")
        bone(f"thigh_{side}", hp, kn, "pelvis")
        bone(f"shin_{side}", kn, an, f"thigh_{side}")
        bone(f"foot_{side}", an, toe, f"shin_{side}")
    bpy.ops.object.mode_set(mode="POSE")
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm


def paint(body, ears, tail_obj, L):
    """Vertex colours with occlusion under them: a dun coat, a paler belly, pink where it is bare."""
    hide = gg.skin_material()
    scene = bpy.context.scene
    engine = scene.render.engine
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 32
    scene.render.bake.target = "VERTEX_COLORS"

    coat = Vector((0.11, 0.09, 0.07))
    belly = Vector((0.30, 0.26, 0.21))
    pink = Vector((0.55, 0.36, 0.33))
    H = L["head"]
    paws = [gg.mirror(L[k], s) for k in ("palm", "foot") for s in (-1, 1)]

    for obj in [body] + ears + [tail_obj]:
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
        except RuntimeError:
            baked = False
        world = obj.matrix_world
        for poly in mesh.polygons:
            n = (world.to_3x3() @ poly.normal).normalized()
            for li in poly.loop_indices:
                v = world @ mesh.vertices[mesh.loops[li].vertex_index].co
                ao = attr.data[li].color[0] if baked else 1.0
                if obj is not body:
                    tone = pink * (0.85 if obj is tail_obj else 1.0)
                else:
                    tone = coat.lerp(belly, max(0.0, -n.z) * 0.8)
                    snout = max(0.0, 1.0 - (v - (H + Vector((0, -0.20, -0.04)))).length / 0.06)
                    tone = tone.lerp(pink, snout)
                    tone = tone.lerp(pink, max(max(0.0, 1.0 - (v - p).length / 0.07) for p in paws))
                shade = 0.15 + 0.85 * ao ** 2.0
                attr.data[li].color = (tone.x * shade, tone.y * shade, tone.z * shade, 1.0)
    scene.render.engine = engine


def coat_plan(L):
    """Short and bristling, lying back along the body; paler and sparse underneath; none on the
    snout, the paws or the ears."""
    H = L["head"]
    paws = [gg.mirror(L[k], s) for k in ("palm", "foot") for s in (-1, 1)]

    def plan(p, n):
        if p.z < 0.06 or min((p - c).length for c in paws) < 0.06:
            return None
        q = p - H
        if q.y < -0.13:
            return None                                                    # the snout is bare
        if n.z < -0.4:
            return {"density": 1.0, "length": 0.035, "flow": (0, 1, -0.3), "palette": "pale", "lift": 10}
        hump = max(0.0, p.z - 0.45) * 2.5
        return {"density": 3.0, "length": 0.06 + 0.04 * hump, "flow": (n.x * 0.2, 1.0, -0.35), "palette": "coat", "lift": 22}

    return plan


def animate(arm):
    """Four clips and the two the game falls back from, on a body that is all spine."""
    stance = {"chest": (0.0, 0.0, 0.0), "neck": (0.0, 0.0, 0.0), "head": (0.0, 0.0, 0.0), "pelvis": (0.0, 0.0, 0.0),
              "upper_arm_R": (0.0, 0.0, 0.0), "upper_arm_L": (0.0, 0.0, 0.0), "thigh_R": (0.0, 0.0, 0.0), "thigh_L": (0.0, 0.0, 0.0)}

    def held(**changes):
        row = dict(stance)
        row.update(changes)
        return row

    # Sniffing: the head works up and down and side to side, the flanks heave.
    gg.add_action(arm, "rat_idle_combat", 20, [
        (1, held(), {"root": (0, 0, 0)}),
        (5, held(head=(-0.15, 0.0, 0.10), neck=(0.08, 0.0, 0.0)), {"root": (0, -0.008, 0)}),
        (10, held(head=(0.10, 0.0, 0.0)), {"root": (0, 0, 0)}),
        (15, held(head=(-0.15, 0.0, -0.10), neck=(0.08, 0.0, 0.0)), {"root": (0, -0.008, 0)}),
        (20, held(), {"root": (0, 0, 0)}),
    ])

    # The bite: gathers on its haunches, then the whole body lunges behind the teeth.
    gg.add_action(arm, "rat_attack_melee", 12, [
        (1, held(), {"root": (0, 0, 0)}),
        (4, held(chest=(0.25, 0.0, 0.0), head=(0.25, 0.0, 0.0), upper_arm_R=(-0.4, 0.0, 0.0), upper_arm_L=(-0.4, 0.0, 0.0),
                 thigh_R=(0.3, 0.0, 0.0), thigh_L=(0.3, 0.0, 0.0)), {"root": (0, -0.04, 0.05)}),
        (7, held(chest=(-0.30, 0.0, 0.0), neck=(-0.25, 0.0, 0.0), head=(-0.35, 0.0, 0.0), upper_arm_R=(0.5, 0.0, 0.0), upper_arm_L=(0.5, 0.0, 0.0),
                 thigh_R=(-0.35, 0.0, 0.0), thigh_L=(-0.35, 0.0, 0.0)), {"root": (0, -0.03, -0.12)}),
        (12, held(), {"root": (0, 0, 0)}),
    ])

    # Scurrying: diagonal pairs of legs together, the back flexing with them.
    def stride(a):
        return held(upper_arm_R=(0.55 * a, 0, 0), thigh_L=(0.55 * a, 0, 0), upper_arm_L=(-0.55 * a, 0, 0), thigh_R=(-0.55 * a, 0, 0),
                    chest=(-0.08 * a, 0, 0), head=(0.08 * a, 0, 0))

    gg.add_action(arm, "rat_run", 10, [
        (1, stride(1), {"root": (0, 0, 0)}),
        (3, stride(0), {"root": (0, 0.03, 0)}),
        (6, stride(-1), {"root": (0, 0, 0)}),
        (8, stride(0), {"root": (0, 0.03, 0)}),
        (10, stride(1), {"root": (0, 0, 0)}),
    ])

    # Dying: it rolls onto its side, legs drawn up.
    curl = dict(upper_arm_R=(0.7, 0, 0), upper_arm_L=(0.7, 0, 0), thigh_R=(-0.6, 0, 0), thigh_L=(-0.6, 0, 0), head=(-0.4, 0, 0))
    gg.add_action(arm, "rat_death", 18, [
        (1, held(root=(0.0, 0.0, 0.0)), {"root": (0, 0, 0)}),
        (6, held(root=(0.0, 0.0, 0.6), head=(0.3, 0, 0)), {"root": (0, 0, 0)}),
        (12, held(root=(0.0, 0.0, 1.45), **curl), {"root": (0, -0.05, 0)}),
        (18, held(root=(0.0, 0.0, 1.5), **curl), {"root": (0, -0.05, 0)}),
    ])

    # A trip and a shove are a lunge at the legs and a shoulder-barge: the bite, lower and harder.
    gg.add_action(arm, "rat_trip", 12, [
        (1, held(), {"root": (0, 0, 0)}),
        (5, held(chest=(-0.45, 0, 0), neck=(-0.30, 0, 0), head=(-0.30, 0, 0)), {"root": (0, -0.06, -0.10)}),
        (12, held(), {"root": (0, 0, 0)}),
    ])
    gg.add_action(arm, "rat_shove", 12, [
        (1, held(), {"root": (0, 0, 0)}),
        (5, held(chest=(-0.20, 0, 0.25), head=(-0.10, 0, 0)), {"root": (0, -0.02, -0.12)}),
        (12, held(), {"root": (0, 0, 0)}),
    ])
    gg.pose_reset(arm)
    arm.animation_data.action = None


def build(output):
    gg.clear_scene()
    gg.setup_scene()

    L = frame()
    body = grow_body(L)
    face = head_parts(L, body)
    tail_obj = tail(L)
    ears = [o for o in face if o.name.startswith("Ear_")]

    paint(body, ears, tail_obj, L)
    arm = armature(L)

    gg.auto_skin(body, arm)
    gg.fill_unweighted(body, arm)
    gg.rigid_skin(tail_obj, arm, "pelvis")
    rigid = [o for o in face if o not in ears]
    for o in rigid + ears:
        gg.rigid_skin(o, arm, "head")

    coat = cards.grow(body, "Fur_Coat", cards.atlas("Rat", PALETTES, size=512), coat_plan(L), 2600, seed=21, segments=2)
    cards.skin_like(coat, body, arm)

    surface.RECIPES["Goblin_Hide"] = lambda c, m, r: surface.fur(None)
    surface.SCALE = 0.45                    # a small animal: fine grain, as on a hand
    surface.finish([("hide", [body] + ears + [tail_obj], 1024, False), ("kit", rigid, 512, True)])

    animate(arm)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.splitext(os.path.abspath(output))[0] + ".blend")
    gg.export_glb(output)
    print("\n=== dire rat ===")
    gg.validate_glb(output, arm, creature="rat")


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        raise RuntimeError("Usage: blender -b --factory-startup --python generate_rat.py -- OUTPUT_GLB")
    build(args[-1])

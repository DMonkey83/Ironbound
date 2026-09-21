import bpy
import math
import os
import struct
import json
import sys
from mathutils import Vector, Matrix

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
CHAIN = mat("Chain_Shirt", (0.38, 0.39, 0.43), 0.25, 0.65)
WOOD = mat("Shield_Wood", (0.36, 0.17, 0.07), 0.0, 0.9)
BOW_WOOD = mat("Bow_Wood", (0.62, 0.38, 0.14), 0.0, 0.8)
BOW_STRING = mat("Bow_String", (0.88, 0.82, 0.62), 0.0, 0.7)
CLOTH_RED = mat("Cloth_Red", (0.50, 0.07, 0.05), 0.0, 1.0)
GOLD = mat("Gold", (0.85, 0.62, 0.15), 0.30, 0.45)
FLETCH = mat("Fletching", (0.85, 0.82, 0.75), 0.0, 0.9)
CLAW = mat("Claw", (0.10, 0.09, 0.06), 0.0, 0.6)
HAIR = mat("Hair", (0.07, 0.045, 0.03), 0.0, 0.9)

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
        "shoulder": P(0.31, -0.02, 1.34), "elbow": P(0.47, -0.10, 1.00),
        "wrist": P(0.50, -0.30, 0.72), "palm": P(0.50, -0.35, 0.65), "fingertip": P(0.50, -0.44, 0.53),
        "hip": P(0.14, 0.03, 0.76), "knee": P(0.29, -0.15, 0.43), "ankle": P(0.27, 0.02, 0.11),
        "foot": P(0.28, -0.08, 0.055), "toe": P(0.28, -0.26, 0.03),
    }
    L["r"] = sr
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
        e.size_x, e.size_y, e.size_z = (v * 1.55 * r for v in size)
        e.radius = 1.0
        e.stiffness = 1.6 if negative else 2.0
        e.use_negative = negative

    def chain(a, b, ra, rb, n=4):
        for i in range(n + 1):
            t = i / n
            ball(a.lerp(b, t), ra + (rb - ra) * t)

    def off(base, x, y, z):
        return base + Vector((x, y, z)) * r

    # Torso: stooped, belly slack, a hump of trapezius behind the neck.
    ball(L["pelvis"], (0.21, 0.16, 0.15))
    ball(L["belly"], (0.20, 0.17, 0.17))
    ball(L["ribs"], (0.25, 0.19, 0.21))
    ball(L["back"], (0.24, 0.15, 0.15))
    for side in (-1, 1):
        ball(off(L["ribs"], side * 0.11, -0.14, 0.07), (0.115, 0.07, 0.09))     # pectoral
        ball(off(L["ribs"], side * 0.20, 0.19, 0.02), (0.10, 0.07, 0.14))       # lat
    chain(L["neck_a"], L["neck_b"], 0.10, 0.09, 2)

    # Head: low skull, heavy brow, hooked nose, underbite.
    H = L["head"]
    jaw = 1.18 if hob else 1.0
    ball(H, (0.20, 0.23, 0.185))
    ball(off(H, 0, -0.13, -0.10), (0.135 * jaw, 0.12, 0.085 * jaw))
    ball(off(H, 0, -0.22, -0.13), (0.075 * jaw, 0.05, 0.05))
    ball(off(H, 0, -0.19, 0.065), (0.165, 0.055, 0.045))
    ball(off(H, 0, -0.245, 0.0), (0.04, 0.07, 0.06))
    ball(off(H, 0, -0.315, -0.055), (0.05, 0.06, 0.05))
    ball(off(H, 0, -0.20, -0.16), (0.11 * jaw, 0.06, 0.035))
    ball(off(H, 0, -0.245, -0.11), (0.085, 0.05, 0.016), negative=True)          # the mouth
    for side in (-1, 1):
        ball(off(H, side * 0.125, -0.14, -0.025), 0.062)                          # cheekbone
        ball(off(H, side * 0.045, -0.285, -0.065), 0.03)                          # nostril
        ball(off(H, side * 0.085, -0.215, 0.015), (0.05, 0.05, 0.032), negative=True)  # socket

    # Arms: long, hanging forward of the body, hands near the knees.
    for side in (-1, 1):
        sh, el, wr, pa = (mirror(L[k], side) for k in ("shoulder", "elbow", "wrist", "palm"))
        ball(sh, 0.125)
        chain(off(sh, side * 0.03, -0.01, -0.06), el, 0.085, 0.075)
        ball(sh.lerp(el, 0.55) + Vector((-side * 0.02, 0.0, 0.02)) * r, (0.085, 0.09, 0.11))
        chain(el, wr, 0.085, 0.055)
        ball(pa, (0.065, 0.075, 0.05))
        for dx in (-0.04, 0.0, 0.04):
            chain(off(pa, dx, -0.03, 0.0), off(pa, dx * 1.2, -0.09, -0.13), 0.034, 0.026, 3)
        chain(off(pa, -side * 0.055, 0.01, 0.01), off(pa, -side * 0.10, -0.08, -0.04), 0.036, 0.028, 2)

    # Legs: crouched and bowed.
    for side in (-1, 1):
        hp, kn, an, ft = (mirror(L[k], side) for k in ("hip", "knee", "ankle", "foot"))
        chain(hp, kn, 0.125, 0.085)
        ball(off(kn, 0, -0.02, -0.01), 0.075)
        chain(kn, an, 0.08, 0.05)
        ball(kn.lerp(an, 0.4) + Vector((0, 0.07, 0)) * r, (0.075, 0.085, 0.11))   # calf
        ball(ft, (0.085, 0.16, 0.05))
        for dx in (-0.05, 0.0, 0.05):
            ball(off(ft, dx, -0.165, -0.015), (0.028, 0.045, 0.03))

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

    parts = [body]
    parts += create_head_parts(L)
    return parts, L


def create_head_parts(L):
    """What metaballs cannot hold: anything thin, sharp or bright."""
    r, hob, H = L["r"], L["hob"], L["head"]
    parts = []

    def off(x, y, z):
        return tuple(H + Vector((x, y, z)) * r)

    for side in (-1, 1):
        suffix = "L" if side < 0 else "R"

        length = (0.30 if hob else 0.42) * r
        bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.15 * r, radius2=0.004, depth=length)
        ear = bpy.context.object
        ear.name = f"Ear_{suffix}"
        ear.scale = (1.0, 0.26, 1.0)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

        # Out, back and up: the sweep is what separates a goblin's ears from a pair of horns.
        sweep = Vector((side * 0.72, 0.52, 0.46)).normalized()
        ear.rotation_mode = "QUATERNION"
        ear.rotation_quaternion = sweep.to_track_quat("Z", "X")
        root = H + Vector((side * 0.155, 0.03, 0.02)) * r
        ear.location = root + sweep * (length * 0.5)
        smooth(ear)
        parts.append(ear)

        parts.append(ring(f"Earring_{suffix}", off(side * 0.235, 0.04, -0.07), 0.04 * r, 0.011 * r, GOLD, rotation=(math.pi / 2, 0, 0)))
        parts.append(uv(f"Eye_{suffix}", off(side * 0.085, -0.187, 0.013), (0.034 * r,) * 3, EYE, 12, 8))
        parts.append(uv(f"Pupil_{suffix}", off(side * 0.085, -0.214, 0.013), (0.014 * r, 0.008 * r, 0.02 * r), PUPIL, 8, 6))
        parts.append(cone(f"Tusk_{suffix}", off(side * 0.062, -0.245, -0.10), 0.02 * r, 0.003, 0.085 * r, TOOTH, (math.radians(-8), 0, side * math.radians(-10)), 8))

    if not hob:
        for i in range(6):
            y = -0.13 + i * 0.07
            z = 0.185 * math.sqrt(max(0.0, 1 - (y / 0.24) ** 2)) * 0.92
            parts.append(cone(f"Hair_{i}", off(0, y, z + 0.05), 0.04 * r, 0.004, (0.17 - abs(i - 2) * 0.015) * r, HAIR, (math.radians(-18 + i * 9), 0, 0), 6))
    return parts


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
    import random

    body = next(o for o in parts if o.name == "Body")
    ears = [o for o in parts if o.name.startswith("Ear_")]
    hide = skin_material()

    scene = bpy.context.scene
    engine = scene.render.engine
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 48
    scene.render.bake.target = "VERTEX_COLORS"

    skin = Vector((0.30, 0.40, 0.13))
    pale = Vector((0.43, 0.47, 0.21))
    flush = Vector((0.47, 0.22, 0.14))
    nose = L["head"] + Vector((0, -0.315, -0.055)) * L["r"]
    chest = L["ribs"].lerp(L["belly"], 0.5)
    rng = random.Random(7)

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

        is_ear = obj in ears
        world = obj.matrix_world
        span = max(v.co.z for v in mesh.vertices) - min(v.co.z for v in mesh.vertices) if is_ear else 1.0
        base = min(v.co.z for v in mesh.vertices) if is_ear else 0.0

        for poly in mesh.polygons:
            for li in poly.loop_indices:
                local = mesh.vertices[mesh.loops[li].vertex_index].co
                v = world @ local
                ao = attr.data[li].color[0] if baked else 1.0
                tone = skin.copy()
                if is_ear:
                    tone = tone.lerp(flush, 0.15 + 0.6 * (local.z - base) / span)
                else:
                    front = max(0.0, min(1.0, (chest.y - v.y) * 4.5)) * max(0.0, 1 - abs(v.z - chest.z) * 2.4)
                    tone = tone.lerp(pale, front * 0.85)
                    if (v - nose).length < 0.085 * L["r"]:
                        tone = tone.lerp(flush, 0.45)
                    if v.z < 0.09 or (v - mirror(L["palm"], 1 if v.x > 0 else -1)).length < 0.17 * L["r"]:
                        tone = tone * 0.70
                tone *= 0.93 + rng.random() * 0.14
                shade = 0.14 + 0.86 * (ao ** 2.2)
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


def kit(L):
    """The torso as the kit sees it: a leaning ellipsoid, fitted to the body's own landmarks."""
    r = L["r"]
    tall = (L["neck_a"].z - L["pelvis"].z) / 0.58
    return {
        "c": L["pelvis"].lerp(L["neck_a"], 0.5) + Vector((0, -0.012, 0.02)),
        "s": Vector((0.285 * r, 0.25 * r, 0.37 * tall)),
        "lean": L["lean"],
        "r": r,
    }


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


def create_cuirass(k, material, grow=1.10):
    shell = uv("Cuirass", (0, 0, 0), tuple(k["s"] * grow), material, 24, 14)
    return place([shell], lean_matrix(k))


def create_studs(k, grow=1.10):
    studs = []
    for row, dz in enumerate((-0.19, 0.0, 0.19)):
        for i in range(10):
            angle = (i + 0.5 * (row % 2)) * (2 * math.pi / 10)
            studs.append(uv(f"Stud_{row}_{i}", on_torso(k, angle, dz * k["s"].z / 0.37, 1.01, grow), (0.026 * k["r"],) * 3, METAL, 8, 5))
    return studs


def create_straps(k, material=LEATHER, crossed=True, grow=1.0):
    """Crossed belts over the chest and back — on bare hide for the goblin, over armour for
    the others. Round the back is where the camera mostly sees them."""
    sc = k["s"] * grow
    parts = []
    for i, tilt in enumerate((50, -50) if crossed else (50,)):
        strap = ring(f"Strap_{i}", (0, 0, 0), sc.x * 1.04, 0.030 * k["r"], material, scale=(1.0, sc.y / sc.x * 1.03, 1.0))
        strap.rotation_euler = (0, math.radians(tilt), 0)
        parts.append(strap)
    place(parts, lean_matrix(k))
    parts.append(uv("Strap_Boss", on_torso(k, 0.0, 0.02, 1.04, grow), (0.05 * k["r"], 0.03 * k["r"], 0.05 * k["r"]), METAL, 10, 6))
    return parts


def create_belt(L, k):
    r = k["r"]
    at = L["pelvis"] + Vector((0, 0, 0.07 * r))
    belt = cyl("Belt", (0, 0, 0), 0.262 * r, 0.10 * r, LEATHER, vertices=24)
    belt.scale = (1.0, 0.80, 1.0)
    parts = [belt,
             cube("Buckle", (0, -0.215 * r, 0), (0.055 * r, 0.02, 0.055 * r), GOLD, 0.01),
             cube("Pouch", (0.19 * r, 0.13 * r, -0.07 * r), (0.07 * r, 0.05 * r, 0.08 * r), LEATHER_LIGHT, 0.025),
             cube("Pouch_Small", (-0.20 * r, -0.10 * r, -0.06 * r), (0.05 * r, 0.04 * r, 0.06 * r), LEATHER_LIGHT, 0.02)]
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
        parts.append(rag(f"Loincloth_F{i}", hang + Vector((dx * r, -0.20 * r - 0.004 * i, 0)), length * r, 0.075 * r, material if i < 3 else LEATHER_LIGHT, -0.42))
    for i, (dx, length) in enumerate(((-0.11, 0.36), (0.0, 0.46), (0.11, 0.33), (0.05, 0.25))):
        parts.append(rag(f"Loincloth_B{i}", hang + Vector((dx * r, 0.17 * r + 0.004 * i, 0)), length * r, 0.08 * r, material if i < 3 else LEATHER_LIGHT, 0.10))
    return parts


def create_wraps(L, k, arm_material, leg_material, knees=None, greaves=False):
    r = k["r"]
    parts = []
    for side in (-1, 1):
        suffix = "L" if side < 0 else "R"
        el, wr, kn, an = (mirror(L[key], side) for key in ("elbow", "wrist", "knee", "ankle"))
        parts.append(limb(f"Bracer_{suffix}", el.lerp(wr, 0.30), el.lerp(wr, 0.92), 0.083 * r, arm_material))
        parts.append(limb(f"Boot_{suffix}", kn.lerp(an, 0.30 if not greaves else 0.12), kn.lerp(an, 0.97), (0.082 if greaves else 0.074) * r, leg_material))
        if knees:
            parts.append(dome(f"KneePad_{suffix}", tuple(kn + Vector((0, -0.075 * r, 0.0))), (0.085 * r, 0.06 * r, 0.085 * r), knees, -0.5, 12, 8))
    return parts


def create_pauldrons(L, k, material, sides=(1,), spikes=True, size=1.0):
    r = k["r"]
    parts = []
    for side in sides:
        suffix = "L" if side < 0 else "R"
        at = mirror(L["shoulder"], side) + Vector((side * 0.02, 0.0, 0.035)) * r
        parts.append(dome(f"Pauldron_{suffix}", tuple(at), (0.165 * r * size, 0.175 * r * size, 0.13 * r * size), material, -0.30, 16, 10))
        parts.append(ring(f"Pauldron_{suffix}_Rim", tuple(at + Vector((0, 0, -0.035 * r * size))), 0.158 * r * size, 0.016 * r, LEATHER, scale=(1.0, 1.06, 1.0)))
        if spikes:
            for j, (dx, dy) in enumerate(((0.0, 0.0), (0.07, -0.07), (0.07, 0.08))):
                lean_out = side * math.radians(18 + 22 * (j > 0))
                parts.append(cone(f"Spike_{suffix}_{j}", tuple(at + Vector((side * dx, dy, 0.13 - 0.03 * (j > 0))) * r * size), 0.035 * r, 0.003, 0.13 * r, material, (0, lean_out, 0), 8))
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
    palm = mirror(L["palm"], 1) + Vector((0, -0.02, -0.035)) * r
    return place(parts, aim(palm, at_idle((0.10, -0.42, 0.90), RIGHT_SWING), wide=at_idle((0, -0.90, -0.42), RIGHT_SWING)))


def create_shield(name, L, k, radius, spiked=False, material=WOOD):
    """Built facing forward at the origin, then strapped to the outside of the left forearm."""
    r = k["r"]
    face = (math.pi / 2, 0, 0)
    parts = [
        cyl(name, (0, 0, 0), radius, 0.055 * r, material, face, 24),
        ring(f"{name}_Rim", (0, 0, 0), radius, 0.030 * r, METAL, face),
        ring(f"{name}_Band", (0, -0.02 * r, 0), radius * 0.60, 0.016 * r, METAL, face),
        dome(f"{name}_Boss", (0, -0.015 * r, 0), (radius * 0.30,) * 3, METAL, 0.0, 14, 8),
    ]
    parts[-1].rotation_euler = (math.pi / 2, 0, 0)
    for j in range(8):
        a = j * math.pi / 4
        parts.append(uv(f"{name}_Rivet_{j}", (radius * 0.80 * math.cos(a), -0.035 * r, radius * 0.80 * math.sin(a)), (0.022 * r,) * 3, METAL, 8, 5))
        if spiked:
            tip = Vector((math.cos(a), 0, math.sin(a)))
            spike = cone(f"{name}_Spike_{j}", (0, 0, 0), 0.04 * r, 0.003, 0.13 * r, METAL, vertices=6)
            spike.matrix_world = aim(tip * (radius + 0.05 * r), tip)
            parts.append(spike)
    fore = mirror(L["elbow"], -1).lerp(mirror(L["wrist"], -1), 0.55)
    # Face mostly forward with the guard up, so it is presented to whoever he is fighting.
    turn = Matrix.Rotation(LEFT_SWING * 0.55, 4, "X") @ Matrix.Rotation(math.radians(-38), 4, "Z")
    return place(parts, Matrix.Translation(fore + Vector((-0.10 * r, -0.07 * r, 0.0))) @ turn)


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
    palm = mirror(L["palm"], -1) + Vector((0, -0.03, -0.03)) * r
    return place([bow, string, wrap], aim(palm, at_idle((0, -0.12, 0.99), LEFT_SWING), wide=(1, 0, 0)))


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


def create_cape(L, k):
    r, sc = k["r"], k["s"]
    cape = cube("Cape", (0, 0, -0.42 * r), (sc.x * 0.95, 0.02, 0.62 * r), CLOTH_RED, 0.02)
    collar = ring("Cape_Collar", (0, -sc.y * 1.05, 0.12 * r), sc.x * 0.72, 0.035 * r, CLOTH_RED, scale=(1.0, 0.9, 1.0))
    seat = lean_matrix(k) @ Matrix.Translation((0, sc.y * 1.14 + 0.05 * r, sc.z * 0.80)) @ Matrix.Rotation(-k["lean"] - math.radians(9), 4, "X")
    return place([cape, collar], seat)


def create_helmet(L, k):
    r, H = k["r"], L["head"]
    shell = (0.222 * r, 0.255 * r, 0.215 * r)
    parts = [
        dome("Helmet", (0, 0, 0), shell, METAL, 0.22, 24, 14),
        ring("Helmet_Rim", (0, 0, shell[2] * 0.22), shell[0] * 0.975, 0.024 * r, GOLD, scale=(1.0, shell[1] / shell[0], 1.0)),
        cube("NoseGuard", (0, -shell[1] * 0.97, -0.01 * r), (0.024 * r, 0.018, 0.085 * r), METAL, 0.008),
        cyl("Crest", (0, 0.03 * r, shell[2] * 0.93), 0.155 * r, 0.035 * r, CLOTH_RED, (0, math.pi / 2, 0), 20),
    ]
    return place(parts, Matrix.Translation(H + Vector((0, -0.005, 0.035)) * r))


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
        parts += create_pauldrons(L, k, METAL, sides=(1,), spikes=True)
        parts += create_wraps(L, k, LEATHER, LEATHER_LIGHT)
        parts += create_belt(L, k)
        parts += create_loincloth(L, k)
        parts += create_blade("Sword", L, k, 0.62, 0.085, cleaver=True)
        parts += create_shield("Shield", L, k, 0.30 * k["r"], spiked=True)
    elif variant == "goblin-archer":
        parts += create_cuirass(k, LEATHER_LIGHT)
        parts += create_studs(k)
        parts += create_straps(k, LEATHER, crossed=False, grow=1.10)
        parts += create_wraps(L, k, LEATHER, LEATHER)
        parts += create_belt(L, k)
        parts += create_loincloth(L, k)
        parts += create_headband(L, k)
        parts += create_quiver(k)
        parts += create_scabbard(L, k)
        parts += create_bow(L, k)
    else:
        parts += create_cuirass(k, CHAIN, grow=1.12)
        skirt = cone("Skirt_Chain", (0, 0, 0), 0.40 * k["r"], 0.30 * k["r"], 0.44 * k["r"], CHAIN, vertices=24)
        skirt.scale = (1.0, 0.86, 1.0)
        parts += place([skirt], Matrix.Translation(L["pelvis"] + Vector((0, -0.03, -0.12)) * k["r"]) @ Matrix.Rotation(k["lean"] * 0.4, 4, "X"))
        parts += create_pauldrons(L, k, METAL, sides=(-1, 1), spikes=True, size=1.1)
        parts += create_wraps(L, k, METAL, METAL, knees=METAL, greaves=True)
        parts += create_belt(L, k)
        parts += create_helmet(L, k)
        parts += create_cape(L, k)
        parts += create_blade("Longsword", L, k, 0.90, 0.062)
        parts += create_shield("HeavyShield", L, k, 0.37 * k["r"], spiked=False)

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

def bind_all(body, equipment, arm):
    for obj in body:
        if obj.name.startswith(("Eye_", "Pupil_", "Tusk_", "Ear_", "Earring_", "Hair_")):
            rigid_skin(obj, arm, "head")
        else:
            auto_skin(obj, arm)

    # By prefix, first match wins, so the specific entries sit above the general ones. The
    # exact-name table this replaces needed a new row for every stud and arrow, and threw on
    # the .001 suffix Blender gives a repeated name.
    rigid_map = [
        ("Sword_", "hand_R"), ("Longsword_", "hand_R"),
        ("Shortbow", "hand_L"),
        ("Scimitar", "pelvis"), ("Scabbard", "pelvis"),
        ("HeavyShield", "forearm_L"), ("Shield", "forearm_L"),
        ("Bracer_L", "forearm_L"), ("Bracer_R", "forearm_R"),
        ("Pauldron_L", "upper_arm_L"), ("Pauldron_R", "upper_arm_R"),
        ("Spike_L", "upper_arm_L"), ("Spike_R", "upper_arm_R"),
        ("Boot_L", "shin_L"), ("Boot_R", "shin_R"),
        ("KneePad_L", "shin_L"), ("KneePad_R", "shin_R"),
        ("Cuirass", "chest"), ("Stud_", "chest"), ("Strap_", "chest"),
        ("Pack", "chest"), ("Bedroll", "chest"),
        ("Quiver", "chest"), ("Arrow_", "chest"), ("Fletch_", "chest"),
        ("Cape", "chest"),
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
        "upper_arm_R": (0.30, 0.0, 0.0), "forearm_R": (0.75, 0.0, 0.0),
        "upper_arm_L": (0.35, 0.0, 0.0), "forearm_L": (0.85, 0.0, 0.0),
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
    actions.append(add_action(arm, "goblin_death", 20, [
        (1, varied(root=(0.0, 0.0, 0.0), thigh_R=(0.0, 0.0, 0.0), thigh_L=(0.0, 0.0, 0.0)), {"root": (0, 0, 0)}),
        (5, varied(root=(-0.22, 0.0, 0.0), chest=(-0.35, 0.0, 0.0), head=(-0.30, 0.0, 0.0),
                   upper_arm_R=(1.1, 0.0, 0.0), upper_arm_L=(1.1, 0.0, 0.0)), {"root": (0, 0, 0)}),
        (12, dict(limp, root=(-1.05, 0.0, 0.0), chest=(-0.20, 0.0, 0.0), head=(0.15, 0.0, 0.0),
                  thigh_R=(0.45, 0.0, 0.0), thigh_L=(0.25, 0.0, 0.0)), {"root": (0, 0.14, 0)}),
        (17, dict(limp, root=(-1.52, 0.0, 0.0), chest=(-0.10, 0.0, 0.0), head=(-0.20, 0.0, 0.0),
                  upper_arm_R=(-0.35, 0.0, -0.55), upper_arm_L=(-0.35, 0.0, 0.55),
                  thigh_R=(0.30, 0.0, 0.0), thigh_L=(0.10, 0.0, 0.0)), {"root": (0, 0.24, 0)}),
        (20, dict(limp, root=(-1.50, 0.0, 0.0), chest=(-0.10, 0.0, 0.0), head=(-0.20, 0.0, 0.0),
                  upper_arm_R=(-0.35, 0.0, -0.55), upper_arm_L=(-0.35, 0.0, 0.55),
                  thigh_R=(0.30, 0.0, 0.0), thigh_L=(0.10, 0.0, 0.0)), {"root": (0, 0.24, 0)}),
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

def validate_glb(path, arm):
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

    expected_names = [
        "goblin_idle_combat",
        "goblin_attack_melee",
        "goblin_run",
        "goblin_death",
    ]
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
    equipment = create_equipment(variant, L)
    paint_skin(body, L)
    arm = create_armature(L)
    bind_all(body, equipment, arm)
    create_animations(arm)

    bpy.ops.wm.save_as_mainfile(filepath=os.path.splitext(os.path.abspath(output))[0] + ".blend")
    export_glb(output)

    print("")
    print(f"=== {variant} ===")
    validate_glb(output, arm)

def main():
    variant, output = parse_args()
    build(variant, output)

if __name__ == "__main__":
    main()

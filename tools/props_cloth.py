"""Cloth: sacks and bundles, sculpted as cloth behaves rather than as a squashed sphere.

A full sack slumps under its own weight, bulges at the foot where it meets the floor, leans,
and gathers into deep pleats where there is more cloth than shape — at the neck, under the
tie, in the tuft above it, which flops over. `sculpt_sack` builds exactly those: a lathed bag
whose every vertex is moved by named rules (slump, lean, pleats converging on the tie, sagging
creases, lumps of what is inside) rather than by one noise.

Blender's cloth simulation with internal pressure was tried first (`drape`, kept for loose
cloth): the bag either collapsed flat or held its shape and skated off across the floor, at
every mass, pressure and friction tried, so the sacks are sculpted.

The weave is laid on before the drape, as a corner attribute `weave` (`props_surface.burlap`
reads it), so the threads follow every fold the simulation makes. Round the bag the thread
count is a whole number, so the weave closes on itself without a seam.
"""
import math

import bmesh
import bpy
from mathutils import Vector, noise

import props_parts as pp

THREADS = 120.0          # threads per board unit; must match props_surface.burlap


def bag_mesh(name, profile, segments, rng, wobble=0.0):
    """A closed bag lathed from profile [(radius, z)], bottom to top, both ends closed to a
    point. Returns the object and the indices of each ring's vertices."""
    bm = bmesh.new()
    rings = []
    seed = rng.uniform(0, 100)
    for k, (r, z) in enumerate(profile):
        if r <= 1e-6:
            rings.append([bm.verts.new((0, 0, z))])
            continue
        ring = []
        for i in range(segments):
            a = i * math.tau / segments
            w = 1.0 + wobble * noise.noise(Vector((math.cos(a) * 2, math.sin(a) * 2, z * 6 + seed)))
            ring.append(bm.verts.new((r * w * math.cos(a), r * w * math.sin(a), z)))
        rings.append(ring)
    for a_, b_ in zip(rings, rings[1:]):
        if len(a_) == 1:
            for i in range(segments):
                bm.faces.new((a_[0], b_[(i + 1) % segments], b_[i]))
        elif len(b_) == 1:
            for i in range(segments):
                bm.faces.new((a_[i], a_[(i + 1) % segments], b_[0]))
        else:
            for i in range(segments):
                j = (i + 1) % segments
                bm.faces.new((a_[i], a_[j], b_[j], b_[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    _weave(obj, profile, segments)
    return obj


def _weave(obj, profile, segments):
    """(around, along) in board units per face corner; around wraps to a whole thread count."""
    mesh = obj.data
    lengths = [0.0]
    for (r0, z0), (r1, z1) in zip(profile, profile[1:]):
        lengths.append(lengths[-1] + math.hypot(r1 - r0, z1 - z0))
    girth = max(r for r, _ in profile) * math.tau
    threads_round = round(girth * THREADS)
    span = threads_round / THREADS
    attr = mesh.attributes.new("weave", "FLOAT_VECTOR", "CORNER")
    values = []
    for poly in mesh.polygons:
        angles = []
        for li in poly.loop_indices:
            v = mesh.vertices[mesh.loops[li].vertex_index].co
            angles.append(math.atan2(v.y, v.x) % math.tau if (abs(v.x) + abs(v.y)) > 1e-7 else None)
        known = [a for a in angles if a is not None]
        # A face across the wrap takes its small angles round past a full turn.
        wrap = known and max(known) - min(known) > math.pi
        for li, a in zip(poly.loop_indices, angles):
            v = mesh.vertices[mesh.loops[li].vertex_index].co
            if a is None:
                a = sum(known) / len(known) if known else 0.0
            if wrap and a < math.pi:
                a += math.tau
            # Height along the profile: nearest profile row by z.
            k = min(range(len(profile)), key=lambda i: abs(profile[i][1] - v.z))
            values.extend((a / math.tau * span, lengths[k], 0.0))
    attr.data.foreach_set("vector", values)


def drape(obj, frames=40, pressure=6.0, mass=0.6, stiffness=8.0, floor=0.0, quality=6, pin=None, shrink=0.0, colliders=()):
    """Run the cloth simulation on obj for `frames`, standing on a floor at z=floor, and keep
    the last frame as its shape."""
    scene = bpy.context.scene
    bpy.ops.mesh.primitive_plane_add(size=8.0, location=(0, 0, floor))
    ground = bpy.context.active_object
    ground.name = "DrapeFloor"
    ground.modifiers.new("Collision", "COLLISION")
    ground.collision.thickness_outer = 0.003
    ground.collision.cloth_friction = 15.0
    for c in colliders:
        c.modifiers.new("Collision", "COLLISION")
        c.collision.thickness_outer = 0.004
    mod = obj.modifiers.new("Cloth", "CLOTH")
    st = mod.settings
    st.quality = quality
    st.mass = mass
    st.tension_stiffness = stiffness
    st.compression_stiffness = stiffness
    st.shear_stiffness = stiffness * 0.6
    st.bending_stiffness = 0.15
    st.air_damping = 2.0
    st.shrink_min = shrink
    if pressure:
        st.use_pressure = True
        st.uniform_pressure_force = pressure
    if pin:
        group = obj.vertex_groups.new(name="Pin")
        group.add(pin, 1.0, "REPLACE")
        st.vertex_group_mass = "Pin"
    cs = mod.collision_settings
    cs.distance_min = 0.003
    cs.use_self_collision = True
    cs.self_distance_min = 0.002
    mod.point_cache.frame_start = 1
    mod.point_cache.frame_end = frames
    scene.frame_start = 1
    scene.frame_end = frames
    for f in range(1, frames + 1):
        scene.frame_set(f)
    pp_select(obj)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    scene.frame_set(1)
    bpy.data.objects.remove(ground, do_unlink=True)
    for c in colliders:
        for m in list(c.modifiers):
            if m.type == "COLLISION":
                c.modifiers.remove(m)
    return obj


def pp_select(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def ring_points(obj, z_lo, z_hi):
    """World positions of the vertices that started between z_lo and z_hi (by the `start_z`
    attribute written before the drape), in order round the axis."""
    mesh = obj.data
    sz = mesh.attributes.get("start_z")
    pts = []
    for v in mesh.vertices:
        z0 = sz.data[v.index].value if sz else v.co.z
        if z_lo <= z0 <= z_hi:
            pts.append(obj.matrix_world @ v.co)
    return pts


def remember_start(obj):
    mesh = obj.data
    a = mesh.attributes.new("start_z", "FLOAT", "POINT")
    a.data.foreach_set("value", [v.co.z for v in mesh.vertices])
    b = mesh.attributes.new("start_angle", "FLOAT", "POINT")
    b.data.foreach_set("value", [math.atan2(v.co.y, v.co.x) for v in mesh.vertices])


def _ridged(p, freq, seed):
    """A fold: 1 along a sharp crease line, falling away either side."""
    return 1.0 - abs(noise.noise(p * freq + Vector((seed, seed * 0.7, seed * 0.3))))


def sculpt_sack(name, rng, profile, segments=56, neck=None, tie_spread=0.16, lean=(0.05, -0.03),
                flop=(0.6, 0.0), pleats=11, lumps=0.012, creases=0.010, rim=None):
    """A sack sculpted from a lathed profile [(r, z)].

    neck: z of the tie (gathered pleats converge on it from `tie_spread` below and flare in the
    tuft above, which bends over by `flop` = (radians, direction angle)); rim: z above which the
    cloth is a rolled-back mouth (pleated, bunched). lean moves the top sideways, growing with
    height; lumps press out what is inside; creases are the sag folds of slack cloth."""
    obj = bag_mesh(name, profile, segments, rng)
    mesh = obj.data
    seed = rng.uniform(0, 50)
    top = max(z for _, z in profile)
    phase = [rng.uniform(0, math.tau) for _ in range(3)]
    for v in mesh.vertices:
        x, y, z = v.co
        r = math.hypot(x, y)
        if r < 1e-6:
            if neck is not None and z > neck:
                v.co = _flop(Vector((0, 0, z)), neck, flop)
            v.co.x += lean[0] * (z / top) ** 2
            v.co.y += lean[1] * (z / top) ** 2
            continue
        a = math.atan2(y, x)
        p = Vector((math.cos(a), math.sin(a), z * 2.5))
        d = 0.0
        # Pleats: deep folds round the axis, irregular in spacing and depth.
        if neck is not None:
            below = max(0.0, 1.0 - (neck - z) / tie_spread) if z <= neck else 0.0
            above = 1.0 if z > neck else 0.0
            k = below ** 1.6 * 0.30 + above * 0.38
            if k > 0:
                wave = math.sin(pleats * a + 0.8 * math.sin(3 * a + phase[0]) + 2.0 * noise.noise(p * 1.5 + Vector((seed, 0, 0))))
                # Each pleat its own depth, so the gathers do not come out as a rosette.
                depth = 0.55 + 0.9 * (0.5 + 0.5 * noise.noise(Vector((math.cos(a) * 2.5, math.sin(a) * 2.5, seed))))
                d += k * r * (abs(wave) ** 0.6 - 0.45) * depth
        if rim is not None and z > rim:
            wave = math.sin(pleats * a + 1.5 * noise.noise(p * 2 + Vector((seed, 0, 0))))
            d += 0.012 * (abs(wave) ** 0.6 - 0.4)
        # Sag creases: slack cloth folds in long diagonal valleys, more toward the top, and
        # crumples in short horizontal ones where the weight squats on the foot.
        slack = min(1.0, z / (neck or top)) ** 1.2
        d -= creases * slack * _ridged(Vector((math.cos(a) * 1.4, math.sin(a) * 1.4, z * 3.0 + a * 0.35)), 2.2, seed) ** 6 * 2.5
        d -= creases * 0.9 * _ridged(Vector((math.cos(a) * 0.9, math.sin(a) * 0.9, z * 2.2 + seed * 0.1)), 2.6, seed + 3) ** 8 * 2.0
        squat = math.exp(-((z - 0.06) / 0.05) ** 2)
        d -= creases * squat * _ridged(Vector((math.cos(a) * 3.0, math.sin(a) * 3.0, z * 14.0)), 1.0, seed + 7) ** 5 * 1.6
        # Slumped: one side pushed out low, the other fallen in high.
        d += 0.018 * math.cos(a - phase[1]) * (1.0 - min(1.0, z / 0.3)) - 0.012 * math.cos(a - phase[1]) * min(1.0, z / (neck or top))
        # Lumps of what is inside pressing through.
        d += lumps * noise.noise(p * 3.2 + Vector((0, 0, seed))) * min(1.0, z * 8)
        # The foot spreads where the weight sits on the floor.
        foot = math.exp(-z / 0.035) * 0.02
        rr = max(0.002, r + d + foot)
        q = Vector((rr * math.cos(a), rr * math.sin(a), max(0.0, z - 0.0)))
        if neck is not None and z > neck:
            q = _flop(q, neck, flop)
        h = q.z / top
        q.x += lean[0] * h * h
        q.y += lean[1] * h * h
        v.co = q
    mesh.update()
    return obj


def _flop(q, neck, flop):
    """Bend whatever is above the tie over, as a tuft of cloth falls when the tie holds it."""
    angle, direction = flop
    dz = q.z - neck
    t = min(1.0, dz / 0.10)
    bend = angle * t
    axis = Vector((-math.sin(direction), math.cos(direction), 0.0))
    from mathutils import Matrix
    rot = Matrix.Rotation(bend, 3, axis)
    pivot = Vector((0, 0, neck))
    return pivot + rot @ (q - pivot)

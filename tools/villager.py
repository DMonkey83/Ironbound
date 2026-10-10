"""Villagers: people the party meets and trades with, grown the way the goblins are.

    blender -b --factory-startup --python tools/generate_goblin.py -- tobin src/Ironbound.Game/art/tobin.glb
    blender -b --factory-startup --python tools/generate_goblin.py -- pedlar src/Ironbound.Game/art/pedlar.glb

The party came from a modular human pack that can no longer be rebuilt (art/PROVENANCE.md), so
the merchants are grown instead: a metaball body from a table of landmarks (`frame`), the same
skeleton, bone names and roll convention as the goblins (so the game plays their idle clip the
same way), and clothes cut from the body's own skin and lofted from it, baked by `surface.py`.

Everything that is not specifically a person is borrowed from generate_goblin.py.
"""
import math
import os
import random
import sys

import bpy
import bmesh
from mathutils import Matrix, Vector, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import generate_goblin as gg  # noqa: E402
import surface  # noqa: E402
import cards  # noqa: E402

VARIANTS = ("tobin", "pedlar")

EYE_WHITE = gg.mat("Villager_Eye", (0.62, 0.58, 0.52), 0.0, 0.3)
IRIS = gg.mat("Villager_Iris", (0.10, 0.06, 0.035), 0.0, 0.2)
surface.RECIPES["Villager_Eye"] = lambda c, m, r: surface.wet(c, 0.3)
surface.RECIPES["Villager_Iris"] = lambda c, m, r: surface.wet(c, 0.2)


def skin_recipe():
    """Human skin from the painted vertex colours: a faint blotch and fine pores. The goblins'
    hide recipe adds warts and creases, which on a person's face read as disease."""
    m, nt, bsdf = surface._tree("Surface_VillagerSkin")
    col = nt.nodes.new("ShaderNodeVertexColor")
    col.layer_name = "Col"
    co = surface._coords(nt)
    blotch = surface._noise(nt, co, 9.0, 3, 0.5)
    pores = surface._noise(nt, co, 260.0, 2, 0.6)
    tone = surface._shade(nt, col.outputs["Color"], surface._span(nt, blotch, 0.92, 1.06))
    tone = surface._shade(nt, tone, surface._span(nt, pores, 0.94, 1.03))
    n = surface._bump(nt, pores, 0.12, 0.0008)
    surface._finish(nt, bsdf, tone, surface._span(nt, blotch, 0.55, 0.7), n)
    return m


surface.RECIPES["Villager_Hide"] = lambda c, m, r: skin_recipe()


def frame(variant):
    """Landmarks, right-hand side, in the goblins' names so the same skeleton fits. A person is
    about six and a half heads tall, the party's proportions (Karn, measured: hips at 0.51 of
    his height, knees 0.27, shoulders 0.78, the base of the skull 0.84)."""
    V = Vector
    L = {
        "pelvis": V((0, 0.010, 0.745)), "belly": V((0, -0.004, 0.865)), "ribs": V((0, -0.012, 1.000)),
        "back": V((0, 0.030, 1.100)), "neck_a": V((0, 0.006, 1.178)), "neck_b": V((0, 0.012, 1.236)),
        "head": V((0, -0.012, 1.335)),
        "shoulder": V((0.148, 0.012, 1.110)), "elbow": V((0.176, 0.032, 0.868)),
        "wrist": V((0.192, 0.004, 0.665)), "palm": V((0.196, -0.002, 0.618)), "fingertip": V((0.200, -0.012, 0.540)),
        "hip": V((0.086, 0.004, 0.725)), "knee": V((0.094, -0.012, 0.405)), "ankle": V((0.094, 0.020, 0.075)),
        "foot": V((0.096, -0.035, 0.034)), "toe": V((0.100, -0.135, 0.018)),
    }
    woman = variant == "pedlar"
    if woman:
        # Narrower through the shoulders and the ribs, wider at the hips, a little shorter.
        for k in L:
            L[k] = V((L[k].x, L[k].y, L[k].z * 0.965))
        for k in ("shoulder", "elbow", "wrist", "palm", "fingertip"):
            L[k].x *= 0.90
        L["hip"].x *= 1.08
    if variant == "tobin":
        # Old and stooped: the back rounded forward from the waist, the head carried out in
        # front of the shoulders, the knees never quite straight.
        def stoop(p, k):
            dz = p.z - 0.80
            if dz <= 0:
                return p
            a = math.radians(k) * min(1.0, dz / 0.35)
            return V((p.x, p.y - math.sin(a) * dz, 0.80 + math.cos(a) * dz))
        for key in ("ribs", "back", "neck_a", "neck_b", "head", "shoulder", "elbow", "wrist", "palm", "fingertip"):
            L[key] = stoop(L[key], 16 if key not in ("neck_a", "neck_b", "head") else 24)
        # The arms hang plumb from the shoulders he has leant forward.
        for key, drop in (("elbow", 0.25), ("wrist", 0.47), ("palm", 0.517), ("fingertip", 0.595)):
            L[key] = V((L[key].x, L["shoulder"].y + {"elbow": 0.02, "wrist": -0.005, "palm": -0.01, "fingertip": -0.02}[key], L["shoulder"].z - drop))
        L["head"] += V((0, -0.025, -0.005))
        L["knee"] += V((0, -0.03, -0.006))
    L["r"] = 1.0
    L["hs"] = 1.0
    L["bulk"] = 1.0
    L["hob"] = False
    L["variant"] = variant
    L["woman"] = woman
    L["lean"] = math.atan2(-(L["ribs"].y - L["pelvis"].y) * 1.3, L["ribs"].z - L["pelvis"].z)
    return L


def grow_body(L):
    """One skin from metaballs: torso, limbs, hands, feet and a head with a face."""
    woman = L["woman"]
    mb = bpy.data.metaballs.new("VillagerMeta")
    mb.resolution = float(os.environ.get("VILLAGER_RES", "0.0062"))
    mb.threshold = 0.6
    meta = bpy.data.objects.new("VillagerMeta", mb)
    bpy.context.collection.objects.link(meta)
    M = gg.mirror

    def ball(co, size, negative=False, stiff=2.0):
        e = mb.elements.new(type="ELLIPSOID")
        e.co = co
        if isinstance(size, (int, float)):
            size = (size,) * 3
        e.size_x, e.size_y, e.size_z = (v * 1.55 for v in size)
        e.radius = 1.0
        e.stiffness = 1.6 if negative else stiff
        e.use_negative = negative
        return e

    def chain(a, b, ra, rb, n=None):
        # Close enough together that they melt into a tube: spaced wider than about half
        # their radius, metaballs make a string of beads.
        if n is None:
            n = max(3, int(math.ceil((b - a).length / (0.45 * min(ra, rb)))))
        for i in range(n + 1):
            t = i / n
            ball(a.lerp(b, t), ra + (rb - ra) * t)

    def off(base, x, y, z):
        return base + Vector((x, y, z))

    # Torso.
    hipw = 1.12 if woman else 1.0
    ball(L["pelvis"], (0.128 * hipw, 0.092, 0.088))
    ball(L["belly"], (0.112, 0.084, 0.092))
    ball(L["ribs"], (0.132 if not woman else 0.118, 0.090, 0.108))
    ball(L["back"], (0.140 if not woman else 0.122, 0.072, 0.068))
    for side in (-1, 1):
        if woman:
            ball(off(L["ribs"], side * 0.050, -0.060, 0.018), (0.042, 0.032, 0.040))      # breast
        else:
            ball(off(L["ribs"], side * 0.058, -0.060, 0.050), (0.064, 0.036, 0.048))      # pectoral
        ball(off(L["back"], side * 0.060, 0.008, 0.036), (0.050, 0.040, 0.028))           # trapezius
        ball(off(L["pelvis"], side * 0.060, 0.060, -0.030), (0.068 * hipw, 0.058, 0.068)) # buttock
    chain(L["neck_a"], L["neck_b"], 0.037 if not woman else 0.033, 0.031 if not woman else 0.029)

    # Head: an egg, not a bulb, with the features kept small. Big features are what made the
    # first one a caricature; at the size the game draws a face, a nose, a brow and the dark of
    # the eyes is all that reads.
    H = L["head"]
    hs = 0.95 if woman else 1.0

    def face(x, y, z):
        return H + Vector((x, y, z)) * hs

    ball(face(0, 0.010, 0.010), (0.068 * hs, 0.084 * hs, 0.076 * hs))                    # cranium
    ball(face(0, 0.030, -0.012), (0.060 * hs, 0.056 * hs, 0.064 * hs))                   # back of the skull
    ball(face(0, -0.030, -0.030), (0.050 * hs, 0.044 * hs, 0.050 * hs))                  # the face
    ball(face(0, -0.032, -0.056), (0.036 * hs, 0.032 * hs, 0.022 * hs))                  # jaw
    ball(face(0, -0.051, -0.073), (0.015 * hs, 0.012 * hs, 0.013 * hs))                  # chin
    ball(face(0, -0.058, 0.021), (0.042 * hs, 0.008 * hs, 0.008 * hs), stiff=1.6)        # brow
    for side in (-1, 1):
        ball(face(side * 0.038, -0.018, -0.050), (0.013 * hs, 0.020 * hs, 0.016 * hs))   # angle of the jaw
        ball(face(side * 0.040, -0.040, -0.014), 0.015 * hs)                              # cheekbone
        ball(face(side * 0.027, -0.066, 0.004), (0.012 * hs, 0.010 * hs, 0.0075 * hs), negative=True)  # eye socket
        ball(face(side * 0.066, 0.006, -0.014), (0.008 * hs, 0.016 * hs, 0.024 * hs))    # ear
        ball(face(side * 0.0085, -0.074, -0.032), 0.0058 * hs)                            # nostril
    ball(face(0, -0.068, -0.006), (0.0068 * hs, 0.0095 * hs, 0.017 * hs))                # bridge of the nose
    ball(face(0, -0.079, -0.028), (0.0088 * hs, 0.0085 * hs, 0.0082 * hs))               # tip of the nose
    ball(face(0, -0.069, -0.047), (0.014 * hs, 0.004 * hs, 0.0012 * hs), negative=True)  # the line of the mouth
    L["mouth"] = face(0, -0.066, -0.047)
    L["eye"] = face(0.027, -0.054, 0.004)
    L["face_scale"] = hs

    # Arms, hanging, hands open and turned in to the thigh.
    for side in (-1, 1):
        sh, el, wr, pa, tip = (M(L[k], side) for k in ("shoulder", "elbow", "wrist", "palm", "fingertip"))
        thin = 0.88 if woman else 1.0
        ball(sh, 0.052 * thin)                                                            # deltoid
        chain(off(sh, 0, 0, -0.03), el, 0.042 * thin, 0.034 * thin)
        ball(sh.lerp(el, 0.45) + Vector((0, -0.010, 0)), (0.036 * thin, 0.040 * thin, 0.062 * thin))   # biceps
        ball(el, 0.032 * thin)
        chain(el, wr, 0.036 * thin, 0.026 * thin)
        ball(el.lerp(wr, 0.28), (0.038 * thin, 0.036 * thin, 0.058 * thin))              # forearm
        ball(pa, (0.014 * thin, 0.028 * thin, 0.036 * thin))                              # palm
        for dy in (-0.013, -0.004, 0.005, 0.013):
            chain(off(pa, 0, dy * thin, -0.024), off(tip, -side * 0.008, dy * thin - 0.012, 0.004), 0.0092 * thin, 0.0074 * thin)
        chain(off(pa, -side * 0.004, -0.022 * thin, 0.010), off(pa, -side * 0.010, -0.040 * thin, -0.030), 0.0085 * thin, 0.007 * thin, 3)  # thumb

    # Legs.
    for side in (-1, 1):
        hp, kn, an, ft, toe = (M(L[k], side) for k in ("hip", "knee", "ankle", "foot", "toe"))
        thin = 0.92 if woman else 1.0
        chain(hp, kn, 0.072 * thin, 0.048 * thin)
        ball(hp.lerp(kn, 0.35) + Vector((side * 0.008, -0.012, 0)), (0.060 * thin, 0.062 * thin, 0.10 * thin))   # thigh
        ball(off(kn, 0, -0.008, 0), 0.044 * thin)
        chain(kn, an, 0.042 * thin, 0.028 * thin)
        ball(kn.lerp(an, 0.30) + Vector((0, 0.022, 0)), (0.040 * thin, 0.046 * thin, 0.080 * thin))            # calf
        ball(an, 0.030 * thin)
        ball(ft + Vector((0, -0.010, 0.004)), (0.036 * thin, 0.090 * thin, 0.030 * thin))                     # foot

    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = meta
    meta.select_set(True)
    bpy.ops.object.convert(target="MESH")
    body = bpy.context.view_layer.objects.active
    body.name = "Body"
    gg.smooth(body)
    faces = len(body.data.polygons)
    thin = body.modifiers.new("Thin", "DECIMATE")
    thin.ratio = min(1.0, 9000 / max(1, faces))
    bpy.ops.object.modifier_apply(modifier="Thin")
    print(f"VILLAGER body {faces} faces from the metaballs, {len(body.data.polygons)} kept")

    drop = min(v.co.z for v in body.data.vertices)
    for v in body.data.vertices:
        v.co.z -= drop
    for key, value in L.items():
        if isinstance(value, Vector):
            value.z -= drop
    return body


def eyes(L):
    parts = []
    hs = L["face_scale"]
    for side in (-1, 1):
        suffix = "L" if side < 0 else "R"
        c = gg.mirror(L["eye"], side)
        # Set back in the socket: a dark wet eye with a glint, no white to stare with. At the
        # size a face is drawn, white eyeballs are what turn a face into a doll's.
        parts.append(gg.uv(f"Eye_{suffix}", tuple(c), (0.0092 * hs, 0.0080 * hs, 0.0068 * hs), IRIS, 14, 8))
    return parts


SKIN = {
    "tobin": ((0.46, 0.29, 0.20), (0.56, 0.38, 0.28), (0.56, 0.25, 0.18)),     # weathered: base, pale, flush
    "pedlar": ((0.60, 0.42, 0.31), (0.70, 0.52, 0.40), (0.66, 0.34, 0.28)),
}


def paint(body, L):
    """Vertex colours: occlusion baked with the clothes on, times a skin tone that is ruddier at
    the nose, cheeks and knuckles and darker at the lips."""
    scene = bpy.context.scene
    engine = scene.render.engine
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 32
    scene.render.bake.target = "VERTEX_COLORS"
    base, pale, flush = (Vector(c) for c in SKIN[L["variant"]])
    lips = Vector((0.45, 0.22, 0.20))
    hs = L["face_scale"]
    nose = L["head"] + Vector((0, -0.10, -0.03)) * hs
    cheeks = [L["head"] + Vector((s * 0.045, -0.07, -0.03)) * hs for s in (-1, 1)]
    mouth = L["mouth"]
    hide = bpy.data.materials.get("Villager_Hide") or bpy.data.materials.new("Villager_Hide")
    hide.use_nodes = True

    mesh = body.data
    attr = mesh.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    mesh.color_attributes.active_color = attr
    mesh.color_attributes.render_color_index = 0
    mesh.materials.clear()
    mesh.materials.append(hide)
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    try:
        bpy.ops.object.bake(type="AO")
        baked = True
    except RuntimeError as exc:
        print(f"AO bake failed: {exc}")
        baked = False

    def near(v, p, r):
        d = (v - p).length / r
        return max(0.0, 1.0 - d * d)

    for poly in mesh.polygons:
        for li in poly.loop_indices:
            v = mesh.vertices[mesh.loops[li].vertex_index].co
            ao = attr.data[li].color[0] if baked else 1.0
            tone = base.lerp(pale, 0.25 + 0.25 * noise.noise(v * 6.0))
            tone = tone.lerp(flush, near(v, nose, 0.035) * 0.6 + max(near(v, c, 0.035) for c in cheeks) * 0.35)
            d = Vector(((v.x - mouth.x) / 0.024, (v.y - mouth.y) / 0.03, (v.z - mouth.z) / 0.012)).length
            if d < 1.0:
                tone = tone.lerp(lips, (1.0 - d) * 0.8)
            if L["variant"] == "pedlar":
                q = (v - L["head"]) / L["face_scale"]
                # Hair, painted: parted and drawn back under the hood.
                if q.z > 0.030 - 0.25 * max(0.0, -q.y - 0.02) or (q.y > 0.0 and q.z > -0.03):
                    tone = tone.lerp(Vector((0.12, 0.07, 0.04)), 0.92)
            if L["variant"] == "tobin":
                # Under the beard and the hair the skin is painted the hair's grey, so a gap
                # between cards shows more beard and not a bare chin.
                q = (v - L["head"]) / L["face_scale"]
                if (q.z < -0.020 and q.y < 0.012 and q.z > -0.10) or (-0.045 < q.z < 0.02 and q.y > -0.02):
                    grey = Vector((0.42, 0.41, 0.39))
                    tone = tone.lerp(grey, 0.9)
            shade = 0.25 + 0.75 * (ao ** 1.6)
            attr.data[li].color = (tone.x * shade, tone.y * shade, tone.z * shade, 1.0)
    # The material reads the colours (and the bake swaps it for skin_recipe).
    nt = hide.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    col = nt.nodes.new("ShaderNodeVertexColor")
    col.layer_name = "Col"
    nt.links.new(col.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.6
    scene.render.engine = engine


# --- clothes -----------------------------------------------------------------------------------
# Clothes are cut from the body's own skin, the way the goblins' armour is (gg.torso_piece):
# the faces nearest the bones a garment covers, pushed out and given thickness, so a sleeve fits
# the arm it is on whatever the metaballs did. What hangs free of the body (a coat's skirt, a
# dress, a cloak) is lofted round the body's outline instead, so it does not split between the
# legs.

def wool(colour):
    """Homespun wool for a coat or a cloak. `surface.cloth` weaves at a frequency a 1024 atlas
    cannot hold over a whole garment, and it comes out as moire swirls like wood grain; this
    keeps the weave to the bump and the colour to a mottle, a faint twill and rubbed edges."""
    m, nt, bsdf = surface._tree("Surface_Wool")
    co = surface._coords(nt)
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(co, sep.inputs[0])
    x, y, z = sep.outputs[0], sep.outputs[1], sep.outputs[2]
    mottle = surface._noise(nt, co, 7.0, 3, 0.55)
    fibre = surface._noise(nt, co, 140.0, 2, 0.6)
    diag = nt.nodes.new("ShaderNodeCombineXYZ")
    nt.links.new(surface._math(nt, "ADD", x, z), diag.inputs[0])
    nt.links.new(surface._math(nt, "SUBTRACT", y, z), diag.inputs[1])
    twill = surface._wave(nt, diag.outputs[0], 55.0, 1.5, 1.0, "X")
    tone = surface._shade(nt, colour, surface._span(nt, mottle, 0.80, 1.12))
    tone = surface._shade(nt, tone, surface._span(nt, twill, 0.95, 1.03))
    tone = surface._shade(nt, tone, surface._span(nt, fibre, 0.92, 1.04))
    tone = surface._mix(nt, tone, tuple(min(1.0, c * 1.45 + 0.04) for c in colour), surface._math(nt, "MULTIPLY", surface._edges(nt, 5.0), 0.35))
    n = surface._bump(nt, fibre, 0.25, 0.002)
    n = surface._bump(nt, mottle, 0.2, 0.02, n)
    surface._finish(nt, bsdf, tone, surface._span(nt, mottle, 0.85, 0.97), n)
    return m


def cloth_material(name, colour, recipe="wool"):
    m = gg.mat(name, colour, 0.0, 0.95)
    if recipe == "leather":
        surface.RECIPES[name] = lambda c, mm, r: surface.leather(c)
    else:
        surface.RECIPES[name] = lambda c, mm, r: wool(c)
    return m


def segments(L):
    """The bones as segments, grouped by what covers them."""
    M = gg.mirror
    g = {"torso": [(L["pelvis"], L["belly"]), (L["belly"], L["ribs"]), (L["ribs"], L["neck_a"]), (L["back"], L["neck_a"])]
         + [(L["neck_a"], M(L["shoulder"], s).lerp(L["neck_a"], 0.2)) for s in (-1, 1)],
         "neck": [(L["neck_a"], L["neck_b"])], "head": [(L["neck_b"], L["head"] + Vector((0, 0, 0.05)))]}
    for name, a, b in (("upper_arm", "shoulder", "elbow"), ("forearm", "elbow", "wrist"), ("hand", "wrist", "fingertip"),
                       ("thigh", "hip", "knee"), ("shin", "knee", "ankle"), ("foot", "ankle", "toe")):
        g[name] = [(M(L[a], s), M(L[b], s)) for s in (-1, 1)]
    return g


def _reach(p, segs):
    best = 1e9
    for a, b in segs:
        ab = b - a
        t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
        best = min(best, (p - (a + ab * t)).length)
    return best


def nearest_group(p, groups):
    return min(groups, key=lambda k: _reach(p, groups[k]))


def piece(body, L, keep, test=None, smooth_rim=4, relax=0):
    """The body's skin where `keep` (group names) is the nearest bone, and `test(p)` allows,
    as a bmesh: the largest piece of it, its ragged rim eased."""
    groups = segments(L)
    bm = bmesh.new()
    bm.from_mesh(body.data)
    drop = []
    for f in bm.faces:
        c = f.calc_center_median()
        g = nearest_group(c, groups)
        if g not in keep or (test and not test(c, g)):
            drop.append(f)
    bmesh.ops.delete(bm, geom=drop, context="FACES_ONLY")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    for _ in range(smooth_rim):
        moves = {}
        for v in bm.verts:
            rim = [e.other_vert(v) for e in v.link_edges if e.is_boundary]
            if len(rim) == 2:
                moves[v] = v.co * 0.5 + (rim[0].co + rim[1].co) * 0.25
        for v, co in moves.items():
            v.co = co
    bm.normal_update()
    if relax:
        # Cloth bridges the hollows of the body under it: smooth the piece so the muscles do
        # not show through, while the rim stays where it was cut.
        inner = [v for v in bm.verts if not v.is_boundary]
        for _ in range(relax):
            bmesh.ops.smooth_vert(bm, verts=inner, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
        bm.normal_update()
    return bm


def shell(name, bm, offset, thickness, material, puff=None, keep=0.5):
    """A piece of skin pushed out along its normals (more where `puff(p)` says), thinned to
    `keep` of its faces (cloth needs fewer than skin) and thickened."""
    for v in bm.verts:
        v.co += v.normal * (offset + (puff(v.co) if puff else 0.0))
    if keep < 1.0:
        bmesh.ops.triangulate(bm, faces=bm.faces)
        mesh = bpy.data.meshes.new("tmp")
        bm.to_mesh(mesh)
        tmp = bpy.data.objects.new("tmp", mesh)
        bpy.context.collection.objects.link(tmp)
        mod = tmp.modifiers.new("Thin", "DECIMATE")
        mod.ratio = keep
        mod.use_collapse_triangulate = True
        bpy.ops.object.select_all(action="DESELECT")
        bpy.context.view_layer.objects.active = tmp
        tmp.select_set(True)
        bpy.ops.object.modifier_apply(modifier="Thin")
        bm = bmesh.new()
        bm.from_mesh(tmp.data)
        bpy.data.objects.remove(tmp, do_unlink=True)
        bpy.data.meshes.remove(mesh)
        bm.normal_update()
    return gg.fitted_shell(name, bm, 0.0, thickness, material)


TRUNK = {}


def trunk_points(body, L):
    """Body vertices on the torso and legs (not the arms or hands hanging beside them)."""
    if body.name not in TRUNK:
        groups = segments(L)
        TRUNK[body.name] = [v.co.copy() for v in body.data.vertices
                            if nearest_group(v.co, groups) in ("torso", "thigh", "shin", "foot")]
    return TRUNK[body.name]


def outline(body, z, centre, count=48, margin=0.0):
    """The body's outline round a horizontal slice at z: for each direction, the farthest point
    of the slice that way (the convex hull's support), so a loop round both legs is one loop.
    The arms are left out: a skirt that takes in the hands is a box."""
    pts = [p for p in TRUNK.get(body.name, [v.co for v in body.data.vertices]) if abs(p.z - z) < 0.012]
    ring = []
    for k in range(count):
        a = k * math.tau / count
        d = Vector((math.sin(a), -math.cos(a), 0.0))
        far = max(((p - centre).dot(d) for p in pts), default=0.08)
        ring.append(centre + d * (far + margin) + Vector((0, 0, z - centre.z)))
    return ring


def skirt(name, body, top_z, bottom_z, centre, material, flare=0.03, folds=7, rows=14, margin=0.010, seed=0.0, hem=0.0):
    """Cloth hanging from a waist to a hem, round the body's outline, flaring and folding."""
    rings = []
    for j in range(rows + 1):
        t = j / rows
        z = top_z + (bottom_z - top_z) * t
        base = outline(body, max(z, 0.05), Vector((centre.x, centre.y, z)), 48, margin)
        ring = []
        for k, p in enumerate(base):
            a = k * math.tau / 48
            d = (p - Vector((centre.x, centre.y, z))).normalized()
            fold = (0.004 + 0.010 * t) * math.sin(a * folds + 2.0 * noise.noise(Vector((a, seed, 0.0)))) * t
            out = flare * t ** 1.4 + fold
            q = p + d * out
            q.z += hem * t * t * noise.noise(Vector((a * 2.0, seed, 1.0)))
            ring.append(q)
        rings.append(ring)
    bm = bmesh.new()
    vs = [[bm.verts.new(p) for p in ring] for ring in rings]
    for a, b in zip(vs, vs[1:]):
        for i in range(48):
            j = (i + 1) % 48
            bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    gg.smooth(obj)
    gg.apply_mat(obj, material)
    mod = obj.modifiers.new("Thick", "SOLIDIFY")
    mod.thickness = 0.006
    mod.offset = 1.0
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier="Thick")
    return obj


def patch(name, target, centre, normal, w, h, material, turn=0.0):
    """A patch sewn on: a small square laid on the garment's surface."""
    from mathutils.bvhtree import BVHTree
    bvh = BVHTree.FromObject(target, bpy.context.evaluated_depsgraph_get())
    n = Vector(normal).normalized()
    u = Vector((0, 0, 1)).cross(n).normalized() if abs(n.z) < 0.9 else Vector((1, 0, 0))
    v = n.cross(u)
    u, v = u * math.cos(turn) + v * math.sin(turn), v * math.cos(turn) - u * math.sin(turn)
    bm = bmesh.new()
    grid = []
    for j in range(7):
        row = []
        for i in range(7):
            p = Vector(centre) + u * (w * (i / 6 - 0.5)) + v * (h * (j / 6 - 0.5)) + n * 0.05
            loc, nor, _i, _d = bvh.ray_cast(p, -n, 0.2)
            if loc is None:
                loc, nor = p - n * 0.05, n
            row.append(bm.verts.new(loc + nor * 0.0025))
        grid.append(row)
    for j in range(6):
        for i in range(6):
            bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    gg.smooth(obj)
    gg.apply_mat(obj, material)
    mod = obj.modifiers.new("Thick", "SOLIDIFY")
    mod.thickness = 0.0025
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier="Thick")
    return obj


def tube_ring(name, points, radius, material, closed=True, sides=8):
    import props_parts as pp
    bm = pp.sweep(points, pp.round_profile(radius, sides), None, closed_path=closed)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    gg.smooth(obj)
    gg.apply_mat(obj, material)
    return obj


def waist_ring(body, z, centre, out, count=36):
    return outline(body, z, Vector((centre.x, centre.y, z)), count, out)


def dress_tobin(body, L):
    """An old carter: a patched brown coat to the knee over a shirt, a rope for a belt, a
    neckerchief, trousers into boots, a flat cap."""
    coat = cloth_material("Villager_Coat", (0.26, 0.17, 0.10))
    patch_a = cloth_material("Villager_Patch_A", (0.36, 0.28, 0.17))
    patch_b = cloth_material("Villager_Patch_B", (0.20, 0.17, 0.13))
    trousers = cloth_material("Villager_Trousers", (0.22, 0.20, 0.16))
    scarf = cloth_material("Villager_Scarf", (0.42, 0.14, 0.08))
    cap = cloth_material("Villager_Cap", (0.17, 0.15, 0.13))
    boots = cloth_material("Villager_Boots", (0.13, 0.08, 0.05), "leather")
    rope = gg.mat("Villager_Rope", (0.48, 0.38, 0.24), 0.0, 0.9)
    surface.RECIPES["Villager_Rope"] = lambda c, m, r: wool(c)
    parts = []
    waist = L["pelvis"].z + 0.075
    knee = L["knee"].z

    # The coat above the waist: the torso and both arms to the wrist.
    trunk_points(body, L)
    upper = piece(body, L, {"torso", "upper_arm", "forearm"}, lambda p, g: g != "torso" or waist - 0.01 < p.z < L["neck_a"].z + 0.005, relax=24)
    parts.append(shell("Coat", upper, 0.012, 0.008, coat))
    # Its skirt, from the waist to just below the knee, open a little and flaring.
    centre = L["pelvis"].lerp(L["belly"], 0.5)
    parts.append(skirt("Coat_Skirt", body, waist + 0.005, knee - 0.035, centre, coat, flare=0.035, folds=7, rows=16, margin=0.011, seed=3.0, hem=0.012))
    # Trousers into boots.
    legs = piece(body, L, {"thigh", "shin"}, lambda p, g: L["ankle"].z + 0.05 < p.z < waist, relax=6)
    parts.append(shell("Trousers", legs, 0.006, 0.006, trousers, keep=0.3))
    feet = piece(body, L, {"shin", "foot"}, lambda p, g: p.z < knee - 0.10, relax=4)
    boot = shell("Boot", feet, 0.012, 0.006, boots)
    for v in boot.data.vertices:
        v.co.z = max(v.co.z, 0.0)
    parts.append(boot)
    # Cuffs at the wrists, a rope round the waist, a neckerchief.
    for s in (-1, 1):
        el, wr = gg.mirror(L["elbow"], s), gg.mirror(L["wrist"], s)
        axis = (wr - el).normalized()
        at = wr - axis * 0.025
        side = axis.cross(Vector((0, 1, 0))).normalized()
        up = axis.cross(side).normalized()
        ring = [at + (side * math.cos(k * math.tau / 20) + up * math.sin(k * math.tau / 20)) * 0.037 for k in range(20)]
        parts.append(tube_ring(f"Coat_Cuff_{'L' if s < 0 else 'R'}", ring, 0.009, coat))
    belt = waist_ring(body, waist, centre, 0.020)
    parts.append(tube_ring("Belt_Rope", belt, 0.008, rope))
    nk = L["neck_a"] + Vector((0, -0.008, 0.004))
    scarf_ring = [nk + Vector((math.sin(k * math.tau / 24) * 0.050, -math.cos(k * math.tau / 24) * 0.046, -0.010 * math.cos(k * math.tau / 24))) for k in range(24)]
    parts.append(tube_ring("Scarf", scarf_ring, 0.014, scarf))
    knot = nk + Vector((0.012, -0.052, -0.022))
    parts.append(gg.uv("Scarf_Knot", tuple(knot), (0.016, 0.012, 0.018), scarf, 12, 8))
    parts.append(gg.flap("Scarf_End", knot + Vector((0, -0.004, -0.006)), 0.022, 0.05, scarf, -0.25, 0.15, thick=0.005))
    # Patches: one on the chest, one on the skirt, one on the left elbow.
    coat_obj = parts[0]
    skirt_obj = parts[1]
    parts.append(patch("Patch_Chest", coat_obj, L["ribs"] + Vector((0.055, -0.12, -0.02)), (0.15, -1, 0.1), 0.05, 0.055, patch_a, 0.12))
    parts.append(patch("Patch_Skirt", skirt_obj, Vector((-0.07, L["pelvis"].y - 0.15, (waist + knee) / 2 - 0.03)), (-0.3, -1, 0), 0.06, 0.05, patch_b, -0.2))
    el = gg.mirror(L["elbow"], -1)
    parts.append(patch("Patch_Elbow", coat_obj, el + Vector((-0.02, 0.06, 0.0)), (-0.4, 1, 0), 0.045, 0.05, patch_a, 0.3))
    # The cap: a soft flat cap, crown puffed and pulled forward over a short peak.
    H = L["head"]
    hs = L["face_scale"]
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=40, v_segments=20, radius=1.0)
    for v in bm.verts:
        q = v.co.copy()
        # Flat on top, full at the sides, drawn in underneath to the band round the head.
        z = q.z * (0.42 if q.z > 0 else 0.30)
        r = 1.0 if q.z > -0.2 else 0.86
        v.co = Vector((q.x * 0.082 * hs * r, q.y * 0.094 * hs * r, z * 0.11 * hs))
        v.co.y -= 0.010 * hs * max(0.0, q.z)
        v.co += Vector((noise.noise(q * 3.0) * 0.003, noise.noise(q * 3.0 + Vector((5, 0, 0))) * 0.003, 0.0))
    tilt = Matrix.Translation(H + Vector((0, -0.006, 0.050)) * hs) @ Matrix.Rotation(math.radians(-11), 4, "X")
    bm.transform(tilt)
    mesh = bpy.data.meshes.new("Cap")
    bm.to_mesh(mesh)
    bm.free()
    crown = bpy.data.objects.new("Cap", mesh)
    bpy.context.collection.objects.link(crown)
    gg.smooth(crown)
    gg.apply_mat(crown, cap)
    parts.append(crown)
    peak = []
    for k in range(13):
        a = math.radians(-62 + 124 * k / 12)
        r = 0.074 * hs
        peak.append(H + Vector((math.sin(a) * r * 0.95, -math.cos(a) * r - 0.012, 0.030 * hs)))
    bm = bmesh.new()
    inner = [bm.verts.new(p) for p in peak]
    outer = [bm.verts.new(p + (p - H - Vector((0, 0, p.z - H.z))).normalized() * 0.032 * (0.4 + 0.6 * math.cos(math.radians(-62 + 124 * k / 12)) ** 2) + Vector((0, 0, -0.012))) for k, p in enumerate(peak)]
    for i in range(12):
        bm.faces.new((inner[i], inner[i + 1], outer[i + 1], outer[i]))
    mesh = bpy.data.meshes.new("Cap_Peak")
    bm.to_mesh(mesh)
    bm.free()
    pk = bpy.data.objects.new("Cap_Peak", mesh)
    bpy.context.collection.objects.link(pk)
    gg.smooth(pk)
    gg.apply_mat(pk, cap)
    m = pk.modifiers.new("Thick", "SOLIDIFY")
    m.thickness = 0.005
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = pk
    pk.select_set(True)
    bpy.ops.object.modifier_apply(modifier="Thick")
    parts.append(pk)
    return parts


def hair_tobin(body, L, arm):
    """Grey hair under the cap, a full grey beard and moustache, and bushy brows: cards, the way
    the goblins' crests are grown."""
    H = L["head"]
    hs = L["face_scale"]
    info = cards.atlas("Villager_Hair", {"grey": ((0.50, 0.49, 0.46), (0.82, 0.80, 0.76)),
                                         "white": ((0.62, 0.61, 0.58), (0.90, 0.89, 0.86))}, size=512)

    def plan(p, n):
        q = (p - H) / hs
        # Beard: the jaw, chin and cheeks below the cheekbones, in front of the ears.
        if q.z < -0.022 and q.y < 0.010 and q.z > -0.10 and n.y < 0.45:
            if abs(q.x) < 0.012 and q.z > -0.06 and q.y < -0.06:
                return None                                   # leave the lips' line clear
            long_ = 0.050 + 0.035 * max(0.0, (-q.z - 0.04) / 0.05)
            return {"density": 1.0, "length": long_ * hs, "flow": (q.x * 0.5, -0.10, -1.0), "palette": "grey", "lift": 3, "width": 0.9, "shape": 0}
        # Moustache: over the upper lip, falling down and out.
        if -0.045 < q.z < -0.030 and q.y < -0.055 and abs(q.x) < 0.030:
            return {"density": 1.0, "length": 0.030 * hs, "flow": (math.copysign(0.8, q.x), -0.3, -1.0), "palette": "white", "lift": 10, "width": 0.6, "shape": 0}
        # Brows.
        if 0.010 < q.z < 0.030 and q.y < -0.045 and 0.008 < abs(q.x) < 0.048:
            return {"density": 1.0, "length": 0.016 * hs, "flow": (math.copysign(1.0, q.x), -0.4, 0.15), "palette": "white", "lift": 25, "width": 0.5}
        # Hair: round the back and sides under the cap.
        if -0.045 < q.z < 0.018 and q.y > -0.030 and n.z < 0.6:
            return {"density": 0.9, "length": 0.040 * hs, "flow": (q.x * 0.5, 0.35, -1.0), "palette": "grey", "lift": 15, "width": 0.4}
        return None

    hair = cards.grow(body, "Hair_Beard", info, plan, 1000, seed=7, segments=3)
    cards.skin_like(hair, body, arm)
    return hair


def drape(name, body, L, top_z, bottom_z, material, open_front=0.0, rows=18, count=56, margin=0.016, flare=0.05, folds=8, seed=0.0):
    """Cloth hung from the shoulders: each ring as wide as the body at its height or as the ring
    above it, whichever is wider, so it falls from the shoulders and does not cling to the waist.
    `open_front` is the half-angle left open at the front (radians), for a cloak."""
    centre = L["pelvis"].lerp(L["ribs"], 0.5)
    pts_all = [v.co for v in body.data.vertices]
    angles = [open_front + (math.tau - 2 * open_front) * k / (count - 1) for k in range(count)] if open_front else \
        [k * math.tau / count for k in range(count)]
    prev = None
    rings = []
    for j in range(rows + 1):
        t = j / rows
        z = top_z + (bottom_z - top_z) * t
        sl = [p for p in pts_all if abs(p.z - z) < 0.014]
        ring_r = []
        for a in angles:
            d = Vector((math.sin(a), -math.cos(a), 0.0))
            far = max(((p - centre).dot(d) for p in sl), default=0.0)
            r = far + margin
            if prev is not None:
                r = max(r, prev[len(ring_r)] - 0.002)
            ring_r.append(r)
        prev = ring_r
        ring = []
        for k, (a, r) in enumerate(zip(angles, ring_r)):
            d = Vector((math.sin(a), -math.cos(a), 0.0))
            fold = (0.004 + 0.020 * t) * math.sin(a * folds + 2.5 * noise.noise(Vector((a, seed, 0.0)))) * min(1.0, t * 1.6)
            ring.append(Vector((centre.x, centre.y, z)) + d * (r + flare * t ** 1.3 + fold))
        rings.append(ring)
    bm = bmesh.new()
    vs = [[bm.verts.new(p) for p in ring] for ring in rings]
    n = len(angles)
    for a, b in zip(vs, vs[1:]):
        for i in range(n if not open_front else n - 1):
            j = (i + 1) % n
            bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    gg.smooth(obj)
    gg.apply_mat(obj, material)
    mod = obj.modifiers.new("Thick", "SOLIDIFY")
    mod.thickness = 0.007
    mod.offset = 1.0
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier="Thick")
    return obj


def lathe(name, material, profile, centre, rotation=None, segments=20):
    import props_parts as pp
    o = pp.lathe(name, material, profile, segments, (0, 0, 0))
    o.data.materials.clear()
    o.data.materials.append(material)
    m = Matrix.Translation(Vector(centre)) @ (rotation.to_4x4() if rotation is not None else Matrix.Identity(4))
    o.matrix_world = m
    return o


def pack(L, canvas, leather, iron, copper, cord):
    """A pedlar's pack: a canvas bundle strapped high on the back, a rolled blanket on top, and
    her wares hung off it: a frying pan, a pot, a ladle, a kettle."""
    import props_parts as pp
    parts = []
    back = L["back"]
    c = Vector((0.0, back.y + 0.150, back.z - 0.060))
    W, D, H = 0.30, 0.16, 0.34
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=8, use_grid_fill=True)
    for v in bm.verts:
        q = v.co * 2
        n4 = (abs(q.x) ** 3 + abs(q.y) ** 3 + abs(q.z) ** 3) ** (1 / 3)
        q = q / max(n4, 1e-6)
        p = Vector((q.x * W / 2, q.y * D / 2, q.z * H / 2))
        p += Vector((noise.noise(p * 12), noise.noise(p * 12 + Vector((2, 0, 0))), noise.noise(p * 12 + Vector((0, 4, 0))))) * 0.010
        # Sagging: fuller at the bottom.
        p.x *= 1.0 + 0.10 * max(0.0, -q.z)
        p.y *= 1.0 + 0.12 * max(0.0, -q.z)
        v.co = p + c
    mesh = bpy.data.meshes.new("Pack")
    bm.to_mesh(mesh)
    bm.free()
    o = bpy.data.objects.new("Pack", mesh)
    bpy.context.collection.objects.link(o)
    gg.smooth(o)
    gg.apply_mat(o, canvas)
    parts.append(o)
    roll = lathe("Pack_Roll", canvas, [(0.0, -0.17), (0.040, -0.17), (0.047, -0.15), (0.048, 0.15), (0.040, 0.17), (0.0, 0.17)],
                 c + Vector((0, 0.01, H / 2 + 0.040)), Matrix.Rotation(math.radians(90), 3, "Y"), 18)
    parts.append(roll)
    for x in (-0.10, 0.10):
        ring = [c + Vector((x, 0.01, H / 2 + 0.040)) + Vector((0, math.cos(k * math.tau / 16) * 0.051, math.sin(k * math.tau / 16) * 0.051)) for k in range(16)]
        parts.append(tube_ring(f"Strap_Roll_{x:+.2f}", ring, 0.0045, leather))
    # Shoulder straps: from the top of the pack over each shoulder and down to its bottom.
    for s in (-1, 1):
        sh = gg.mirror(L["shoulder"], s)
        path = [c + Vector((s * 0.08, -D / 2 + 0.01, H / 2 - 0.03)), sh + Vector((-s * 0.045, 0.02, 0.055)), sh + Vector((-s * 0.05, -0.07, 0.01)),
                sh + Vector((-s * 0.04, -0.06, -0.12)), L["ribs"] + Vector((s * 0.11, 0.0, -0.10)), c + Vector((s * 0.10, -D / 2 + 0.01, -H / 2 + 0.03))]
        parts.append(gg.ribbon(f"Strap_{'L' if s < 0 else 'R'}", path, [Vector((0, -1, 0.3)).normalized()] * len(path), 0.030, 0.005, leather) if False else
                     tube_ring(f"Strap_{'L' if s < 0 else 'R'}", path, 0.008, leather, closed=False))
    # The frying pan, hung by its handle off the left side.
    pan_c = c + Vector((W / 2 + 0.020, 0.0, -0.02))
    pan = lathe("Pan", iron, [(0.0, 0.0), (0.072, 0.0), (0.080, 0.012), (0.084, 0.026), (0.080, 0.026), (0.074, 0.014), (0.068, 0.006), (0.0, 0.006)],
                pan_c, Matrix.Rotation(math.radians(-90), 3, "Y") @ Matrix.Rotation(math.radians(8), 3, "X"), 28)
    parts.append(pan)
    handle = [pan_c + Vector((0.012, 0.0, 0.080)), pan_c + Vector((0.012, 0.0, 0.20))]
    parts.append(tube_ring("Pan_Handle", handle, 0.008, iron, closed=False, sides=6))
    # The pot, hung under the pack on the right by its bail.
    pot_c = c + Vector((W / 2 - 0.03, 0.02, -H / 2 - 0.085))
    parts.append(lathe("Pot", copper, [(0.0, 0.0), (0.050, 0.0), (0.058, 0.010), (0.060, 0.045), (0.056, 0.064), (0.060, 0.068), (0.056, 0.070),
                                        (0.052, 0.066), (0.050, 0.012), (0.0, 0.006)], pot_c, None, 28))
    bail = [pot_c + Vector((-0.058 * math.cos(a), 0.0, 0.060 + 0.055 * math.sin(a))) for a in [k * math.pi / 10 for k in range(11)]]
    parts.append(tube_ring("Pot_Bail", bail, 0.003, iron, closed=False, sides=6))
    parts.append(tube_ring("Cord_Pot", [pot_c + Vector((0, 0, 0.115)), c + Vector((W / 2 - 0.03, 0.02, -H / 2 + 0.01))], 0.003, cord, closed=False, sides=5))
    # A ladle and a kettle on the right side.
    lad_c = c + Vector((-W / 2 - 0.012, -0.02, 0.02))
    parts.append(tube_ring("Ladle_Handle", [lad_c + Vector((0, 0, 0.11)), lad_c + Vector((0, 0, -0.08))], 0.004, iron, closed=False, sides=6))
    parts.append(lathe("Ladle_Bowl", iron, [(0.0, -0.032), (0.022, -0.026), (0.030, -0.012), (0.032, 0.0), (0.028, 0.0), (0.0, -0.026)],
                       lad_c + Vector((0, -0.005, -0.09)), Matrix.Rotation(math.radians(-80), 3, "X"), 16))
    ket_c = c + Vector((-0.02, 0.0, H / 2 + 0.09))
    parts.append(lathe("Kettle", copper, [(0.0, 0.0), (0.040, 0.0), (0.050, 0.020), (0.048, 0.040), (0.030, 0.056), (0.020, 0.060), (0.020, 0.066), (0.0, 0.068)],
                       ket_c + Vector((0.11, 0.02, 0.0)), None, 24))
    spout = [ket_c + Vector((0.11, 0.02, 0.0)) + Vector((0.045 + 0.02 * t, 0.0, 0.018 + 0.03 * t)) for t in (0.0, 0.5, 1.0)]
    parts.append(tube_ring("Kettle_Spout", spout, 0.006, copper, closed=False, sides=6))
    return parts


def dress_pedlar(body, L):
    """A travelling pedlar: a long dark dress and an apron, a hooded cloak, short boots, and her
    pack with the pots and pans hung off it."""
    dress = cloth_material("Villager_Dress", (0.10, 0.14, 0.16))
    apron = cloth_material("Villager_Apron", (0.42, 0.37, 0.28))
    cloak = cloth_material("Villager_Cloak", (0.26, 0.11, 0.07))
    canvas = cloth_material("Villager_Canvas", (0.40, 0.33, 0.22))
    boots = cloth_material("Villager_Boots", (0.13, 0.08, 0.05), "leather")
    leather = cloth_material("Villager_Strap", (0.16, 0.09, 0.05), "leather")
    iron = gg.mat("Villager_Iron", (0.20, 0.19, 0.18), 0.6, 0.5)
    surface.RECIPES["Villager_Iron"] = lambda c, m, r: surface.iron(c, 0.3)
    copper = gg.mat("Villager_Copper", (0.60, 0.30, 0.16), 0.7, 0.4)
    surface.RECIPES["Villager_Copper"] = lambda c, m, r: surface.iron(c, 0.15)
    cord = gg.mat("Villager_Rope", (0.48, 0.38, 0.24), 0.0, 0.9)
    surface.RECIPES["Villager_Rope"] = lambda c, m, r: wool(c)
    parts = []
    trunk_points(body, L)
    waist = L["pelvis"].z + 0.10
    # A high collar: the dress comes up the neck, so the neck is not a stalk between it and the
    # chin.
    bodice = piece(body, L, {"torso", "upper_arm", "forearm", "neck"},
                   lambda p, g: g in ("upper_arm", "forearm") or (g == "neck" and p.z < L["neck_b"].z - 0.012) or (g == "torso" and p.z > waist - 0.01),
                   relax=24)
    parts.append(shell("Bodice", bodice, 0.009, 0.007, dress))
    centre = L["pelvis"].lerp(L["belly"], 0.5)
    parts.append(skirt("Skirt", body, waist + 0.005, 0.035, centre, dress, flare=0.08, folds=9, rows=22, margin=0.016, seed=5.0, hem=0.008))
    # The apron: the front of a skirt, a little shorter, over the dress.
    ap = skirt("Skirt_Apron", body, waist + 0.003, 0.20, centre, apron, flare=0.09, folds=5, rows=14, margin=0.030, seed=8.0)
    bm = bmesh.new()
    bm.from_mesh(ap.data)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.calc_center_median().y > centre.y - 0.03 or abs(f.calc_center_median().x) > 0.12], context="FACES")
    bm.to_mesh(ap.data)
    bm.free()
    parts.append(ap)
    feet = piece(body, L, {"shin", "foot"}, lambda p, g: p.z < L["knee"].z - 0.16, relax=4)
    boot = shell("Boot", feet, 0.011, 0.006, boots)
    for v in boot.data.vertices:
        v.co.z = max(v.co.z, 0.0)
    parts.append(boot)
    belt = waist_ring(body, waist, centre, 0.018)
    parts.append(tube_ring("Belt_Sash", belt, 0.009, apron))
    # The cloak: from the shoulders to the calf, open at the front.
    parts.append(drape("Cloak", body, L, L["neck_a"].z - 0.080, 0.24, cloak, open_front=math.radians(48), rows=24, margin=0.016, flare=0.08, folds=11, seed=2.0))
    # The hood, up. Lofted from arches, front to back: a rounded arch round the face, the
    # cloth standing off the head over the crown, the peak dropping behind, and the sides
    # falling and widening onto the shoulders. Cut from the head's own shape it fitted like a
    # swimming cap; grown from a sphere it was a pod.
    H = L["head"]
    hs = L["face_scale"]
    base = L["neck_a"].z - 0.095
    shoulder = L["shoulder"].x * 1.68
    stations = [  # (y from the head's centre, half-width, top above the head's centre)
        (-0.082, 0.080, 0.092), (-0.060, 0.094, 0.110), (-0.020, 0.104, 0.118), (0.030, 0.108, 0.114),
        (0.075, 0.106, 0.096), (0.110, 0.098, 0.064), (0.138, 0.080, 0.020), (0.158, 0.050, -0.030), (0.170, 0.012, -0.060)]
    count = 33
    rings = []
    for k, (dy, w, top) in enumerate(stations):
        ring = []
        for i in range(count):
            phi = -math.pi / 2 + math.pi * i / (count - 1)
            zt = H.z + top * hs
            z = base + (zt - base) * math.cos(phi) ** 0.55
            # Wider where it falls onto the shoulders, below the jaw.
            low = max(0.0, min(1.0, (H.z - 0.06 * hs - z) / (H.z - 0.06 * hs - base)))
            ww = w * hs + (shoulder - w * hs) * low ** 1.3 * (0.55 + 0.45 * min(1.0, k / 3))
            x = ww * math.sin(phi)
            y = H.y + dy * hs + 0.020 * low * (1.0 if dy > 0 else 0.4)
            p = Vector((x, y, z))
            p += Vector((noise.noise(p * 30.0), noise.noise(p * 30.0 + Vector((4, 0, 0))), 0.0)) * 0.003 * min(1.0, k)
            ring.append(p)
        rings.append(ring)
    bm = bmesh.new()
    vs = [[bm.verts.new(p) for p in ring] for ring in rings]
    for a, b in zip(vs, vs[1:]):
        for i in range(count - 1):
            bm.faces.new((a[i], a[i + 1], b[i + 1], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # Normals out, away from the head.
    for f in bm.faces:
        if f.normal.dot(f.calc_center_median() - H) < 0:
            f.normal_flip()
    mesh = bpy.data.meshes.new("Hood")
    bm.to_mesh(mesh)
    bm.free()
    hood = bpy.data.objects.new("Hood", mesh)
    bpy.context.collection.objects.link(hood)
    gg.smooth(hood)
    gg.apply_mat(hood, cloak)
    mod = hood.modifiers.new("Thick", "SOLIDIFY")
    mod.thickness = 0.008
    mod.offset = -1.0
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = hood
    hood.select_set(True)
    bpy.ops.object.modifier_apply(modifier="Thick")
    parts.append(hood)
    # The turned-back edge round the face.
    parts.append(tube_ring("Hood_Edge", [p + Vector((0, -0.004, 0)) for p in rings[0]], 0.010, cloak, closed=False))
    parts += pack(L, canvas, leather, iron, copper, cord)
    return parts


def hair_pedlar(body, L, arm):
    """Brown hair framing the face inside the hood."""
    H = L["head"]
    hs = L["face_scale"]
    info = cards.atlas("Villager_Hair_Brown", {"brown": ((0.10, 0.06, 0.035), (0.30, 0.19, 0.11))}, size=512)

    def plan(p, n):
        q = (p - H) / hs
        # Locks down either side of the face, inside the hood.
        if -0.05 < q.z < 0.035 and abs(q.x) > 0.042 and q.y < 0.01:
            return {"density": 1.0, "length": 0.06 * hs, "flow": (q.x * 0.6, -0.05, -1.0), "palette": "brown", "lift": 6, "width": 0.75}
        return None

    hair = cards.grow(body, "Hair_Fringe", info, plan, 220, seed=11, segments=3)
    cards.skin_like(hair, body, arm)
    return hair


# --- the idle ------------------------------------------------------------------------------------

def animate(arm, variant):
    """A standing idle: breathing through the chest, the weight easing from foot to foot, the
    head turning a little. Arms hang. One clip, `<variant>_idle`, which the game finds by name."""
    rest = {"chest": (0.0, 0.0, 0.0), "spine": (0.0, 0.0, 0.0), "head": (0.0, 0.0, 0.0), "neck": (0.0, 0.0, 0.0),
            "pelvis": (0.0, 0.0, 0.0), "upper_arm_L": (0.0, 0.0, 0.0), "upper_arm_R": (0.0, 0.0, 0.0),
            "forearm_L": (0.0, 0.0, 0.0), "forearm_R": (0.0, 0.0, 0.0)}

    def row(**k):
        r = dict(rest)
        r.update(k)
        return r

    gg.add_action(arm, f"{variant}_idle", 72, [
        (1, row(), {"root": (0, 0, 0)}),
        (19, row(chest=(-0.025, 0.0, 0.010), spine=(-0.010, 0.0, 0.0), head=(0.02, 0.05, 0.0), pelvis=(0.0, 0.0, -0.02),
                 upper_arm_L=(0.02, 0.0, 0.0), upper_arm_R=(-0.02, 0.0, 0.0)), {"root": (0, -0.004, 0)}),
        (37, row(chest=(0.0, 0.0, 0.0), head=(0.0, 0.0, 0.0)), {"root": (0, 0, 0)}),
        (55, row(chest=(-0.025, 0.0, -0.010), spine=(-0.010, 0.0, 0.0), head=(0.03, -0.06, 0.0), pelvis=(0.0, 0.0, 0.02),
                 upper_arm_L=(-0.02, 0.0, 0.0), upper_arm_R=(0.02, 0.0, 0.0)), {"root": (0, -0.004, 0)}),
        (72, row(), {"root": (0, 0, 0)}),
    ])
    gg.pose_reset(arm)
    arm.animation_data.action = None


# --- build ----------------------------------------------------------------------------------------

FITTED = ("Coat", "Trousers", "Boot", "Patch_", "Belt_", "Scarf", "Dress", "Bodice", "Sleeve", "Skirt", "Cloak", "Shawl", "Hood")
RIGID = [("Cap", "head"), ("Eye_", "head"), ("Pack", "chest"), ("Pan", "chest"), ("Pot", "chest"),
         ("Ladle", "chest"), ("Cord", "chest"), ("Strap", "chest"), ("Kettle", "chest")]


def bind(body, clothes, arm):
    for obj in clothes:
        if obj.type != "MESH":
            continue
        bone = next((b for prefix, b in RIGID if obj.name.startswith(prefix)), None)
        if bone:
            gg.rigid_skin(obj, arm, bone)
        else:
            gg.skin_from_body(obj, body, arm)


def strip_hidden(body, L):
    """Delete the skin the clothes cover for good: the torso, the arms to just short of the
    wrist, the legs and feet. Only the head, the neck and the hands are ever seen; the rest is
    triangles under cloth, and skin that could poke through it."""
    groups = segments(L)
    bm = bmesh.new()
    bm.from_mesh(body.data)
    wr = [gg.mirror(L["wrist"], s) for s in (-1, 1)]
    gone = []
    for f in bm.faces:
        c = f.calc_center_median()
        g = nearest_group(c, groups)
        if g in ("head", "neck", "hand"):
            continue
        if g == "torso" and c.z > L["neck_a"].z - 0.045:
            continue
        if g == "forearm" and min((c - w).length for w in wr) < 0.06:
            continue
        gone.append(f)
    bmesh.ops.delete(bm, geom=gone, context="FACES")
    bm.to_mesh(body.data)
    bm.free()
    body.data.update()


def build(variant, output):
    gg.clear_scene()
    gg.setup_scene()
    L = frame(variant)
    body = grow_body(L)
    arm = gg.create_armature(L)
    gg.auto_skin(body, arm)
    gg.fill_unweighted(body, arm)
    L["body"] = body
    clothes = (dress_tobin if variant == "tobin" else dress_pedlar)(body, L)
    clothes += eyes(L)
    paint(body, L)
    bind(body, clothes, arm)
    hair = (hair_tobin if variant == "tobin" else hair_pedlar)(body, L, arm)
    strip_hidden(body, L)
    surface.finish([("hide", [body], 1024, False), ("kit", [o for o in clothes if o.type == "MESH"], 1024, True)])
    for o in [body, hair] + [c for c in clothes if c.type == "MESH"]:
        o.data.calc_loop_triangles()
        print(f"VILLAGER tris {o.name:22} {len(o.data.loop_triangles)}")
    animate(arm, variant)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.splitext(os.path.abspath(output))[0] + ".blend")
    gg.export_glb(output)
    print(f"\n=== {variant} ===")
    gg.validate_glb(output, arm, creature=variant, clips=("idle",))

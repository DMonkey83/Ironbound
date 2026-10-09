"""Hair and fur as cards: thin strips wearing a texture of strands, the way games draw them.

A solid lock of fur, however it is shaped, is a plate: it has an edge and a face and no
strands, and a coat of them reads as a hedgehog or a pile of leaves. A card is a ribbon of a
few quads with a painted tuft on it — strands where the texture is opaque, nothing where it is
not — so what you see is hair, and the hide between the cards is the dark undercoat.

    import cards
    atlas = cards.atlas("Wolf", {"coat": ((0.05, 0.04, 0.03), (0.30, 0.22, 0.15)), ...})
    fur = cards.grow(body, atlas, plan, count=2400)
    cards.skin_like(fur, body, arm)

`plan(p, n)` says, for a point on the body and its normal, how dense the fur is there, how long,
which way it lies, which palette it is, and how far it stands off the skin; zero density is
bare skin. Each card takes its skinning from the body vertex nearest its root, so it rides on
the skin it grows from through every animation without stretching.

The material is alpha-tested (glTF alphaMode MASK, double-sided). Godot draws that with alpha
scissor: no sorting, and the cards cast strand-shaped shadows. Strands are dense at the root
and thin out toward the tip, so when the texture is mipmapped down for a distant creature the
roots stay opaque and it does not go bald.
"""
import math
import random

import bpy
import bmesh
import numpy as np
from mathutils import Vector, kdtree

SHAPES = 4      # tuft shapes per palette: dense, medium, wispy, a pointed clump
ROOTS = {}      # card mesh name -> [(root, vertex count)], for skin_like


def _tuft(alpha, colour, x0, x1, height, root, tip, rnd, shape):
    """Paint one column of the atlas: a tuft of strands converging toward a point."""
    w = x1 - x0
    # Sparse enough that there is air between the strands: a full card is a leaf.
    strands = (46, 32, 16, 38)[shape]
    clump = x0 + w * rnd.uniform(0.4, 0.6)
    converge = (0.70, 0.55, 0.30, 0.90)[shape]
    for _ in range(strands):
        bx = rnd.uniform(x0 + w * 0.06, x1 - w * 0.06)
        top = int(height * rnd.uniform(0.55, 0.98) if shape != 2 else height * rnd.uniform(0.35, 0.95))
        if top < 8:
            continue
        rows = np.arange(top)
        s = rows / top
        tx = bx + (clump - bx) * converge
        wave = math.sin(rnd.uniform(0, 6.28)) * w * 0.03
        xc = bx + (tx - bx) * s ** 1.2 + wave * np.sin(s * math.pi * rnd.uniform(1, 3))
        half = rnd.uniform(0.8, 1.5) * (1.0 - s) ** 0.6 + 0.30
        tint = rnd.uniform(0.75, 1.25)
        tone = (np.array(root)[None, :] * (1 - s[:, None] ** 0.8) + np.array(tip)[None, :] * s[:, None] ** 0.8) * tint
        for dx in range(-3, 4):
            px = np.floor(xc).astype(int) + dx
            ok = (px >= x0) & (px < x1)
            cov = np.clip(half + 0.5 - np.abs(px + 0.5 - xc), 0.0, 1.0)
            r, p, c = rows[ok], px[ok], cov[ok]
            old = alpha[r, p]
            win = c > old
            alpha[r[win], p[win]] = c[win]
            colour[r[win], p[win]] = tone[ok][win]
    # Dense at the root: fill the bottom of the tuft solid, so a mipmapped card keeps its base.
    base = int(height * 0.05)
    centre = np.arange(int(x0 + w * 0.1), int(x1 - w * 0.1))
    for row in range(base):
        a = alpha[row, centre]
        empty = centre[a < 0.05]
        colour[row, empty] = np.array(root) * 0.85
        alpha[row, centre] = np.maximum(a, 0.6 + 0.4 * (1.0 - row / base))


def atlas(name, palettes, size=1024, seed=5):
    """One image holding SHAPES tufts for every palette, side by side. palettes maps a name to
    (root colour, tip colour). Returns (image, {palette: first column}, total columns)."""
    rnd = random.Random(seed)
    columns = SHAPES * len(palettes)
    width = size
    col_w = width // columns
    alpha = np.zeros((size, width), dtype=np.float32)
    colour = np.zeros((size, width, 3), dtype=np.float32)
    index = {}
    for p, (pal, (root, tip)) in enumerate(palettes.items()):
        index[pal] = p * SHAPES
        for s in range(SHAPES):
            c = p * SHAPES + s
            _tuft(alpha, colour, c * col_w, (c + 1) * col_w, size, root, tip, rnd, s)
    rgba = np.concatenate([colour, alpha[..., None]], axis=2)
    img = bpy.data.images.new(f"{name}_strands", width, size, alpha=True)
    img.pixels.foreach_set(rgba.ravel())
    img.pack()
    return img, index, columns


def material(name, image):
    """Alpha-tested and double-sided. The Round node is the glTF exporter's cue for MASK."""
    m = bpy.data.materials.new(f"{name}_Cards")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = image
    cut = nt.nodes.new("ShaderNodeMath")
    cut.operation = "ROUND"
    nt.links.new(tex.outputs[0], bsdf.inputs["Base Color"])
    nt.links.new(tex.outputs[1], cut.inputs[0])
    nt.links.new(cut.outputs[0], bsdf.inputs["Alpha"])
    bsdf.inputs["Roughness"].default_value = 0.85
    m.use_backface_culling = False
    return m


def _samples(body, plan, count, rnd):
    """Points on the body, area-weighted and density-weighted, with their normals."""
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    tris = [(f.verts[0].co.copy(), f.verts[1].co.copy(), f.verts[2].co.copy(), f.normal.copy(), f.calc_area()) for f in bm.faces]
    bm.free()
    weights = []
    plans = []
    for a, b, c, n, area in tris:
        centre = (a + b + c) / 3
        q = plan(centre, n)
        plans.append(q)
        weights.append(area * (q["density"] if q else 0.0))
    total = sum(weights)
    if total <= 0:
        return []
    cum = np.cumsum(weights) / total
    out = []
    for _ in range(count):
        i = int(np.searchsorted(cum, rnd.random()))
        i = min(i, len(tris) - 1)
        a, b, c, n, _area = tris[i]
        u, v = rnd.random(), rnd.random()
        if u + v > 1:
            u, v = 1 - u, 1 - v
        p = a + (b - a) * u + (c - a) * v
        q = plan(p, n)
        if q and q["density"] > 0:
            out.append((p, n, q))
    return out


def grow(body, name, image_info, plan, count, seed=11, segments=4):
    """Cards over `body` wherever `plan` says fur grows. One mesh, one material."""
    image, index, columns = image_info
    rnd = random.Random(seed)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    roots = []
    for p, n, q in _samples(body, plan, count, rnd):
        flow = Vector(q["flow"])
        t = (flow - n * flow.dot(n))
        if t.length < 1e-4:
            t = n.orthogonal()
        t.normalize()
        length = q["length"] * rnd.uniform(0.75, 1.25)
        half = q.get("width", 0.20) * length * rnd.uniform(0.8, 1.2) * 0.5
        lift = math.radians(q.get("lift", 30) * rnd.uniform(0.7, 1.3))
        d0 = (t * math.cos(lift) + n * math.sin(lift)).normalized()
        d1 = (t - n * 0.25).normalized()
        twist = math.radians(rnd.uniform(-35, 35))
        col = index[q["palette"]] + rnd.randrange(SHAPES) if q.get("shape") is None else index[q["palette"]] + q["shape"]
        u0, u1 = col / columns, (col + 1) / columns

        at = p - n * (0.015 * length)
        pts = [at]
        for i in range(segments):
            s = (i + 0.5) / segments
            d = d0.lerp(d1, s ** 0.8).normalized()
            pts.append(pts[-1] + d * (length / segments))
        rows = []
        for i, c in enumerate(pts):
            s = i / segments
            d = (pts[min(i + 1, segments)] - pts[max(i - 1, 0)]).normalized()
            side = d.cross(n)
            if side.length < 1e-4:
                side = d.orthogonal()
            side.normalize()
            side = side * math.cos(twist) + n * math.sin(twist)
            w = half * (1.0 - 0.35 * s)
            rows.append((bm.verts.new(c - side * w), bm.verts.new(c + side * w), s))
        for (a0, b0, s0), (a1, b1, s1) in zip(rows, rows[1:]):
            f = bm.faces.new((a0, b0, b1, a1))
            for loop, (u, v) in zip(f.loops, ((u0, s0), (u1, s0), (u1, s1), (u0, s1))):
                loop[uv].uv = (u, v * 0.985 + 0.005)
        roots.append((p, len(rows) * 2))

    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    mesh.materials.append(material(name, image))
    ROOTS[obj.name] = roots
    print(f"CARDS {name}: {len(roots)} cards, {len(mesh.polygons) * 2} tris")
    return obj


def skin_like(cards, body, arm):
    """Every vertex of a card takes the skinning of the body vertex nearest the card's root."""
    tree = kdtree.KDTree(len(body.data.vertices))
    for v in body.data.vertices:
        tree.insert(v.co, v.index)
    tree.balance()
    bones = {b.name for b in arm.data.bones}
    names = {g.index: g.name for g in body.vertex_groups if g.name in bones}
    groups = {}
    start = 0
    for p, k in ROOTS.pop(cards.name):
        _co, idx, _d = tree.find(p)
        for g in body.data.vertices[idx].groups:
            if g.weight <= 0.0 or g.group not in names:
                continue
            name = names[g.group]
            vg = groups.get(name) or cards.vertex_groups.get(name) or cards.vertex_groups.new(name=name)
            groups[name] = vg
            vg.add(list(range(start, start + k)), g.weight, "REPLACE")
        start += k
    mod = cards.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    cards.parent = arm

"""The parts props are made of: boards, iron bands, nails, rope, straw, coins.

Every part is built in its own frame as a bmesh, worn and chipped there, and then placed. A
board is never a box: it is cut a little out of true, its edges are bevelled and then rubbed
down unevenly, chips are scooped out of its corners, it bows along its length and its ends are
not square. All of that is geometry, because at the game's distance a flat-shaded slab with a
texture on it reads as a slab, whatever the texture says.

Conventions: Blender Z is up (the exporter makes it Godot's Y), the front of a prop faces -Y
(Godot's +Z, toward the default camera), units are board units (one square, five feet).

A board carries two attributes for `props_surface.plank`: `grain`, its own coordinates from a
pith outside it, and `board`, a tone of its own. Every face carries `is_wood`, which says where
`props_surface.weather` may paint rust.
"""
import math
import random

import bmesh
import bpy
from mathutils import Matrix, Vector, noise

import props_surface as ps


# --- objects ---------------------------------------------------------------------------------

class Part:
    """A finished piece of mesh with its placement, waiting to be joined into a node."""

    def __init__(self, obj):
        self.obj = obj


def to_object(name, bm, material, smooth=True, sharp_angle=40.0, wood=None):
    """A bmesh as a linked object: one material, smooth-shaded with hard edges past
    `sharp_angle`, so a bevel reads round and a face reads flat."""
    is_wood = material.name in ps.WOODS if wood is None else wood
    layer = bm.faces.layers.float.get("is_wood") or bm.faces.layers.float.new("is_wood")
    for f in bm.faces:
        f[layer] = 1.0 if is_wood else 0.0
        f.smooth = smooth
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    if smooth and sharp_angle:
        mesh.set_sharp_from_angle(angle=math.radians(sharp_angle))
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(material)
    return obj


def place(bm, matrix):
    bmesh.ops.transform(bm, matrix=matrix, verts=bm.verts)


def frame(origin, x, y, z):
    """A matrix whose columns are the given axes, at origin: a board's local X (across), Y
    (thickness), Z (length) laid wherever they are wanted."""
    m = Matrix((Vector(x).normalized().to_4d(), Vector(y).normalized().to_4d(), Vector(z).normalized().to_4d(), Vector(origin).to_4d()))
    m = m.transposed()
    m[3][3] = 1.0
    for i in range(3):
        m[3][i] = 0.0
    return m


def _grain(bm, rng, pith_r=(0.08, 0.30), tone=None):
    """Write the board's growth-ring frame: its local coordinates from a pith somewhere off
    to one side and below, the way a plank sits in a log."""
    g = bm.verts.layers.float_vector.get("grain") or bm.verts.layers.float_vector.new("grain")
    t = bm.verts.layers.float.get("board") or bm.verts.layers.float.new("board")
    a = rng.uniform(0, math.tau)
    r = rng.uniform(*pith_r)
    px, py, pz = r * math.cos(a) * 0.5, r * math.sin(a), rng.uniform(-5, 5)
    tone = rng.random() if tone is None else tone
    for v in bm.verts:
        v[g] = (v.co.x - px, v.co.y - py, v.co.z + pz)
        v[t] = tone


def _bisect(bm, axis, positions):
    for p in positions:
        co = [0.0, 0.0, 0.0]
        no = [0.0, 0.0, 0.0]
        co[axis] = p
        no[axis] = 1.0
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no)


def _bevel_sharp(bm, offset, segments=2, angle=1.0, profile=0.5):
    edges = [e for e in bm.edges if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > angle]
    if not edges or offset <= 0:
        return
    verts = list({v for e in edges for v in e.verts})
    bmesh.ops.bevel(bm, geom=edges + verts, offset=offset, offset_type="OFFSET", segments=segments,
                    profile=profile, affect="EDGES", clamp_overlap=True)


def board_mesh(length, width, thick, rng, *, seg=0.1, bevel=0.005, wear=1.0, chips=2,
               bow=0.004, twist=0.0, rough_ends=0.006, taper=0.0, notch=None):
    """One plank in its own frame: across X, thickness Y (front face at -Y), length Z, centred.

    `wear` scales how far its edges are rubbed in; `chips` is how many bites are taken out of
    its edges and corners; `bow` bends it along its length; `rough_ends` puts the saw cuts out
    of square; `taper` narrows one end (as a riven board does)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=(width, thick, length), verts=bm.verts)
    n = max(1, int(round(length / seg)))
    _bisect(bm, 2, [-length / 2 + length * i / n for i in range(1, n)])
    m = max(1, int(round(width / (seg * 0.8))))
    _bisect(bm, 0, [-width / 2 + width * i / m for i in range(1, m)])
    _bevel_sharp(bm, bevel, 2)

    seed = rng.uniform(0, 100)
    # Out of true: width wanders along the length, a bow, a twist, a taper.
    for v in bm.verts:
        x, y, z = v.co
        t = z / length + 0.5
        w = 1.0 + 0.035 * noise.noise(Vector((seed, z * 4.0, 0.3))) - taper * t
        v.co.x = x * w
        v.co.y = y + bow * math.sin(math.pi * t) + twist * x * (t - 0.5) * 2
    # The ends: saw cuts not square, and a little rough.
    for v in bm.verts:
        if abs(abs(v.co.z) - length / 2) < bevel * 2.5:
            s = 1 if v.co.z > 0 else -1
            v.co.z += s * (rough_ends * (0.5 * noise.noise(Vector((v.co.x * 25, v.co.y * 25, seed + s))) + 0.35 * v.co.x / max(width, 1e-3)))
    # Rubbed edges: vertices near an edge pulled in toward the board's core, unevenly.
    hw, ht = width / 2, thick / 2
    for v in bm.verts:
        x, y, z = v.co
        ex = max(0.0, abs(x) - (hw - bevel * 2.2)) / (bevel * 2.2)
        ey = max(0.0, abs(y) - (ht - bevel * 2.2)) / (bevel * 2.2)
        edge = min(1.0, ex * ey * 1.5 + 0.0)
        if edge <= 0:
            continue
        k = (0.5 + 0.5 * noise.noise(Vector((z * 18.0 + seed, x * 10, y * 10)))) * edge * wear * bevel * 1.2
        v.co.x -= math.copysign(k, x)
        v.co.y -= math.copysign(k * 0.6, y)
    # Chips: spherical bites out of an edge, the cut faces left as they fall.
    for _ in range(chips):
        z0 = rng.uniform(-length / 2, length / 2) if rng.random() < 0.6 else rng.choice((-1, 1)) * (length / 2 - rng.uniform(0, 0.02))
        corner = Vector((rng.choice((-1, 1)) * hw, rng.choice((-1, 1)) * ht if rng.random() < 0.7 else -ht, z0))
        r = rng.uniform(0.6, 1.0) * min(width * 0.35, 0.035) + thick * 0.2
        _carve(bm, corner, r, depth=rng.uniform(0.35, 0.7))
    if notch:
        _carve(bm, Vector(notch[0]), notch[1], depth=1.0)
    return bm


def _carve(bm, centre, radius, depth=0.6):
    """Push every vertex inside a sphere out to its surface, toward the board's inside: a chip."""
    for v in bm.verts:
        d = v.co - centre
        dist = d.length
        if dist < radius:
            inward = Vector((-centre.x, -centre.y, 0.0))
            if inward.length < 1e-6:
                inward = Vector((0, 0, -math.copysign(1, centre.z)))
            inward.normalize()
            push = (radius - dist) * depth
            v.co += inward * push


def slab(name, material, rng, centre, along, out, length, width, thick, tone=None, pith=(0.08, 0.30), **kw):
    """A board centred at `centre`, its length along `along`, its outer face looking `out`."""
    z = Vector(along).normalized()
    y = -Vector(out).normalized()
    y = (y - z * y.dot(z)).normalized()
    x = y.cross(z)
    bm = board_mesh(length, width, thick, rng, **kw)
    _grain(bm, rng, pith, tone)
    place(bm, frame(centre, x, y, z))
    return to_object(name, bm, material)


def plank_run(name, material, rng, a, b, width, thick, out, **kw):
    """A board from point a to point b, its outer face looking `out`."""
    a, b = Vector(a), Vector(b)
    return slab(name, material, rng, (a + b) / 2, b - a, out, (b - a).length, width, thick, **kw)


def split(total, count, gap, rng, spread=0.15):
    """Widths of `count` boards filling `total` with `gap` between: uneven, as boards are."""
    raw = [1.0 + rng.uniform(-spread, spread) for _ in range(count)]
    room = total - gap * (count - 1)
    s = sum(raw)
    widths = [room * r / s for r in raw]
    out, at = [], 0.0
    for w in widths:
        out.append((at + w / 2, w))
        at += w + gap
    return out


# --- lofts and bands ---------------------------------------------------------------------------

def loft_mesh(sections, closed=True, cap=True):
    """Rings of points joined into a tube; every ring the same length."""
    bm = bmesh.new()
    rings = [[bm.verts.new(tuple(p)) for p in ring] for ring in sections]
    count = len(sections[0])
    for a, b in zip(rings, rings[1:]):
        for i in range(count if closed else count - 1):
            j = (i + 1) % count
            quad = (a[i], a[j], b[j], b[i])
            if len({v.co[:] for v in quad}) >= 3:
                try:
                    bm.faces.new(quad)
                except ValueError:
                    pass
    if cap and closed:
        for ring in (rings[0], rings[-1]):
            if len({v.co[:] for v in ring}) >= 3:
                try:
                    bm.faces.new(ring if ring is rings[-1] else list(reversed(ring)))
                except ValueError:
                    pass
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def sweep(path, profile, normals=None, closed_path=False, twist=None):
    """A profile (2D points, x across and y up off the surface) carried along a path of 3D
    points. `normals` gives the 'up' at each point (the surface the band lies on); without it
    a frame is propagated along the path."""
    path = [Vector(p) for p in path]
    sections = []
    n = len(path)
    prev_up = None
    for i, p in enumerate(path):
        if closed_path:
            t = (path[(i + 1) % n] - path[(i - 1) % n])
        else:
            t = path[min(i + 1, n - 1)] - path[max(i - 1, 0)]
        t.normalize()
        if normals is not None:
            up = Vector(normals[i])
        elif prev_up is not None:
            up = prev_up
        else:
            up = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
        up = (up - t * up.dot(t)).normalized()
        prev_up = up
        side = t.cross(up)
        if twist:
            a = twist(i / max(n - 1, 1))
            up, side = up * math.cos(a) + side * math.sin(a), side * math.cos(a) - up * math.sin(a)
        sections.append([p + side * q[0] + up * q[1] for q in profile])
    if closed_path:
        sections.append(sections[0])
    return loft_mesh(sections, closed=True, cap=not closed_path)


def band_profile(width, thick, chamfer=None):
    """Cross-section of a forged strap: flat, its edges knocked off."""
    c = chamfer if chamfer is not None else min(thick * 0.45, width * 0.2)
    w, t = width / 2, thick
    return [(-w, 0.0), (w, 0.0), (w, t - c), (w - c, t), (-w + c, t), (-w, t - c)]


def round_profile(radius, n=8, sx=1.0, sy=1.0):
    return [(radius * sx * math.cos(a), radius * sy * math.sin(a)) for a in (i * math.tau / n for i in range(n))]


def rounded_rect_path(cx, cz, hx, hz, r, y, n_corner=3, plane="XZ"):
    """A rounded rectangle round (cx, cz), as a closed path, in the XZ or YZ plane at depth y."""
    pts = []
    corners = [(hx - r, hz - r, 0), (-hx + r, hz - r, 90), (-hx + r, -hz + r, 180), (hx - r, -hz + r, 270)]
    for (ox, oz, start) in corners:
        for k in range(n_corner + 1):
            a = math.radians(start + 90 * k / n_corner)
            u, w = cx + ox + r * math.cos(a), cz + oz + r * math.sin(a)
            pts.append((u, y, w) if plane == "XZ" else (y, u, w))
    return pts


def hammered(bm, rng, amount=0.0012, freq=60.0):
    """Hand-forged iron is not flat: a little dimpling everywhere."""
    seed = rng.uniform(0, 100)
    for v in bm.verts:
        v.co += Vector((noise.noise(v.co * freq + Vector((seed, 0, 0))),
                        noise.noise(v.co * freq + Vector((0, seed, 0))),
                        noise.noise(v.co * freq + Vector((0, 0, seed))))) * amount
    return bm


def strap_around(name, material, rng, path, width, thick, normals=None, closed=False):
    bm = sweep(path, band_profile(width, thick), normals, closed)
    hammered(bm, rng, thick * 0.12, 45.0)
    return to_object(name, bm, material, sharp_angle=50)


# --- small iron ------------------------------------------------------------------------------

def nail_head(bm, at, normal, radius, rng, height=None, segments=7, flat=False):
    """A forged nail head: a low irregular dome, a little tilted, sunk into the surface."""
    normal = Vector(normal).normalized()
    height = height if height is not None else radius * (0.35 if flat else 0.55)
    tilt = Vector((rng.uniform(-0.25, 0.25), rng.uniform(-0.25, 0.25), rng.uniform(-0.25, 0.25)))
    up = (normal + tilt - normal * tilt.dot(normal) * 0.0).normalized()
    side = up.orthogonal().normalized()
    other = up.cross(side)
    base = Vector(at) - normal * radius * 0.25
    ring0 = []
    ring1 = []
    for i in range(segments):
        a = i * math.tau / segments + rng.uniform(-0.2, 0.2)
        r = radius * rng.uniform(0.85, 1.1)
        d = side * math.cos(a) + other * math.sin(a)
        ring0.append(bm.verts.new(base + d * r))
        ring1.append(bm.verts.new(base + d * r * 0.62 + up * height * 0.85))
    top = bm.verts.new(base + up * height)
    for i in range(segments):
        j = (i + 1) % segments
        bm.faces.new((ring0[i], ring0[j], ring1[j], ring1[i]))
        bm.faces.new((ring1[i], ring1[j], top))
    return top.co.copy()


def nails(name, material, rng, spots, radius=0.0065, flat=False):
    """Many nail heads as one part. spots: [(point, normal)]. Returns the part and the heads,
    which `weather` bleeds rust from."""
    bm = bmesh.new()
    heads = []
    for at, normal in spots:
        heads.append(nail_head(bm, at, normal, radius * rng.uniform(0.85, 1.15), rng, flat=flat))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return to_object(name, bm, material, sharp_angle=70), heads


def rivet_row(a, b, normal, count, inset=0.0):
    a, b = Vector(a), Vector(b)
    return [(a.lerp(b, (i + 0.5) / count if inset else i / max(count - 1, 1)), normal) for i in range(count)]


def lathe(name, material, profile, segments=12, axis_origin=(0, 0, 0), rotation=None, sharp_angle=40.0, jitter=0.0, rng=None):
    """A turned shape: profile is [(radius, z)] bottom to top; spun round Z."""
    sections = []
    for (r, z) in profile:
        ring = []
        for i in range(segments):
            a = i * math.tau / segments
            rr = r * (1.0 + (rng.uniform(-jitter, jitter) if (jitter and rng) else 0.0))
            ring.append(Vector((rr * math.cos(a), rr * math.sin(a), z)))
        sections.append(ring)
    bm = loft_mesh(sections)
    m = Matrix.Translation(Vector(axis_origin))
    if rotation is not None:
        m = m @ rotation.to_4x4()
    place(bm, m)
    return to_object(name, bm, material, sharp_angle=sharp_angle)


def torus(name, material, centre, major, minor, normal=(0, 0, 1), segs=20, minor_segs=8, squash=1.0, open_angle=0.0):
    """A ring: a handle, a hoop, a drop ring, a shackle (open_angle leaves a gap)."""
    normal = Vector(normal).normalized()
    side = normal.orthogonal().normalized()
    other = normal.cross(side)
    path = []
    span = math.tau - open_angle
    count = segs if not open_angle else segs + 1
    for i in range(count):
        a = open_angle / 2 + span * i / segs if open_angle else i * math.tau / segs
        path.append(Vector(centre) + (side * math.cos(a) + other * math.sin(a)) * major)
    nrm = [normal for _ in path]
    bm = sweep(path, round_profile(minor, minor_segs, 1.0, squash), nrm, closed_path=not open_angle)
    return to_object(name, bm, material, sharp_angle=0)


# --- rope and cord ---------------------------------------------------------------------------

def rope(name, material, path, radius, rng, strands=3, twist_per_unit=None, segs_per_unit=110, sides=4):
    """A laid rope: three strands wound round each other along a path."""
    path = [Vector(p) for p in path]
    # Resample evenly.
    total = sum((b - a).length for a, b in zip(path, path[1:]))
    steps = max(6, int(total * segs_per_unit))
    pts = []
    for i in range(steps + 1):
        d = total * i / steps
        acc = 0.0
        for a, b in zip(path, path[1:]):
            seg = (b - a).length
            if acc + seg >= d - 1e-9:
                pts.append(a.lerp(b, (d - acc) / max(seg, 1e-9)))
                break
            acc += seg
    twist_per_unit = twist_per_unit or (1.0 / (radius * 7.0))
    bm = bmesh.new()
    up = Vector((0, 0, 1))
    frames = []
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        if abs(t.dot(up)) > 0.95:
            up = Vector((1, 0, 0))
        u = (up - t * up.dot(t)).normalized()
        up = u
        frames.append((p, t, u, t.cross(u)))
    for s in range(strands):
        sections = []
        for i, (p, t, u, w) in enumerate(frames):
            a = s * math.tau / strands + (total * i / steps) * twist_per_unit * math.tau
            c = p + (u * math.cos(a) + w * math.sin(a)) * radius * 0.5
            sections.append([c + (u * math.cos(b) + w * math.sin(b)) * radius * 0.58 for b in (k * math.tau / sides for k in range(sides))])
        part = loft_mesh(sections)
        mesh = bpy.data.meshes.new("tmp")
        part.to_mesh(mesh)
        part.free()
        bm.from_mesh(mesh)
        bpy.data.meshes.remove(mesh)
    return to_object(name, bm, material, sharp_angle=0)


def catenary(a, b, sag, n=12):
    a, b = Vector(a), Vector(b)
    return [a.lerp(b, i / n) + Vector((0, 0, -sag * math.sin(math.pi * i / n))) for i in range(n + 1)]


# --- loose stuff -----------------------------------------------------------------------------

def straw_strands(bm, rng, centre, spread, count, length=(0.05, 0.12), lift=0.02, droop=0.4, up_bias=0.3, bounds=None):
    """Loose stalks of straw: thin flat triangular tubes, bent once, lying every which way."""
    for _ in range(count):
        p = Vector(centre) + Vector((rng.uniform(-spread[0], spread[0]), rng.uniform(-spread[1], spread[1]), rng.uniform(0, lift)))
        if bounds:
            p.x = max(bounds[0][0], min(bounds[1][0], p.x))
            p.y = max(bounds[0][1], min(bounds[1][1], p.y))
        a = rng.uniform(0, math.tau)
        d = Vector((math.cos(a), math.sin(a), rng.uniform(-0.2, up_bias))).normalized()
        L = rng.uniform(*length)
        r = rng.uniform(0.0016, 0.0026)
        mid = p + d * L * 0.5 + Vector((0, 0, rng.uniform(-droop, droop) * L * 0.2))
        end = mid + (d + Vector((rng.uniform(-0.3, 0.3), rng.uniform(-0.3, 0.3), -droop * 0.5))).normalized() * L * 0.5
        pts = [p, mid, end]
        side = d.orthogonal().normalized()
        rings = []
        for k, q in enumerate(pts):
            other = d.cross(side).normalized()
            rr = r * (1.0 if k < 2 else 0.7)
            rings.append([bm.verts.new(q + side * rr), bm.verts.new(q + other * rr * 0.5), bm.verts.new(q - side * rr)])
        for r0, r1 in zip(rings, rings[1:]):
            for i in range(3):
                j = (i + 1) % 3
                bm.faces.new((r0[i], r0[j], r1[j], r1[i]))


def packing(name, material, rng, lo, hi, floor_z, top_z, lumps=0.025, res=0.03, strands=300, over_rim=30, rim_z=None):
    """Straw packed into a box: a lumpy bed filling the inside from wall to wall, its edge
    tucked down out of sight, and loose stalks lying across it — most flat, some arched, a
    few hanging over the rim. Returns the bed and the stalks as two parts."""
    lo, hi = Vector(lo), Vector(hi)
    nx = max(4, int((hi.x - lo.x) / res))
    ny = max(4, int((hi.y - lo.y) / res))
    seed = rng.uniform(0, 100)

    def height(x, y):
        h = top_z + lumps * (noise.noise(Vector((x * 7 + seed, y * 7, 0.5))) * 0.9 + noise.noise(Vector((x * 22, y * 22 + seed, 1.5))) * 0.35)
        # Packed up against the walls a little.
        ex = min(x - lo.x, hi.x - x) / max(hi.x - lo.x, 1e-3)
        ey = min(y - lo.y, hi.y - y) / max(hi.y - lo.y, 1e-3)
        return h + lumps * 0.4 * max(0.0, 0.12 - min(ex, ey)) / 0.12

    bm = bmesh.new()
    grid = []
    for j in range(ny + 1):
        row = []
        for i in range(nx + 1):
            x = lo.x + (hi.x - lo.x) * i / nx
            y = lo.y + (hi.y - lo.y) * j / ny
            edge = i in (0, nx) or j in (0, ny)
            z = floor_z if edge else height(x, y)
            row.append(bm.verts.new((x, y, z)))
        grid.append(row)
    for j in range(ny):
        for i in range(nx):
            bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
    bed = to_object(name, bm, material, sharp_angle=0)

    bm = bmesh.new()
    for _ in range(strands):
        x = rng.uniform(lo.x + 0.01, hi.x - 0.01)
        y = rng.uniform(lo.y + 0.01, hi.y - 0.01)
        _stalk(bm, rng, Vector((x, y, height(x, y) + 0.002)), arch=rng.random() < 0.25)
    rim_z = rim_z if rim_z is not None else top_z
    for _ in range(over_rim):
        # Over the top of a wall: start inside, lie across the rim, droop outside.
        side = rng.randrange(4)
        t = rng.uniform(0.1, 0.9)
        if side < 2:
            x = lo.x + (hi.x - lo.x) * t
            y = lo.y if side == 0 else hi.y
            out = Vector((0, -1 if side == 0 else 1, 0))
        else:
            y = lo.y + (hi.y - lo.y) * t
            x = lo.x if side == 2 else hi.x
            out = Vector((-1 if side == 2 else 1, 0, 0))
        start = Vector((x, y, rim_z + 0.004)) - out * rng.uniform(0.02, 0.05)
        _stalk(bm, rng, start, direction=(out + Vector((rng.uniform(-0.5, 0.5), rng.uniform(-0.5, 0.5), 0.05))).normalized(), droop=0.9, length=rng.uniform(0.07, 0.13))
    stalks = to_object(name + "_Stalks", bm, material, sharp_angle=0)
    return bed, stalks, height


def _stalk(bm, rng, p, direction=None, arch=False, droop=0.2, length=None):
    if direction is None:
        a = rng.uniform(0, math.tau)
        direction = Vector((math.cos(a), math.sin(a), rng.uniform(-0.05, 0.15))).normalized()
    L = length or rng.uniform(0.05, 0.12)
    r = rng.uniform(0.0016, 0.0025)
    lift = (0.03 * rng.uniform(0.3, 1.0)) if arch else 0.0
    pts = []
    for k in range(4):
        t = k / 3
        q = p + direction * L * t
        q.z += lift * math.sin(math.pi * t) - droop * 0.35 * L * t * t
        pts.append(q)
    side = direction.cross(Vector((0, 0, 1)))
    if side.length < 1e-4:
        side = Vector((1, 0, 0))
    side.normalize()
    up = side.cross(direction).normalized()
    rings = []
    for k, q in enumerate(pts):
        rr = r * (1.0 if k < 3 else 0.6)
        rings.append([bm.verts.new(q + side * rr), bm.verts.new(q + up * rr * 0.8), bm.verts.new(q - side * rr)])
    for r0, r1 in zip(rings, rings[1:]):
        for i in range(3):
            j = (i + 1) % 3
            bm.faces.new((r0[i], r0[j], r1[j], r1[i]))


def heap(name, material, centre, radii, rng, res=18, bumps=0.25, freq=9.0, floor=None, squash=1.0):
    """A low mound (of straw, coins, grain): a dome displaced by noise, flat underneath."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=res * 2, v_segments=res, radius=1.0)
    seed = rng.uniform(0, 100)
    for v in bm.verts:
        p = v.co.copy()
        n = noise.noise(p * freq * 0.25 + Vector((seed, 0, 0))) * 0.6 + noise.noise(p * freq + Vector((0, seed, 0))) * 0.4
        k = 1.0 + bumps * n
        v.co = Vector((p.x * radii[0] * k, p.y * radii[1] * k, max(p.z, -0.05) * radii[2] * k * squash))
        v.co += Vector(centre)
        if floor is not None:
            v.co.z = max(v.co.z, floor)
    return to_object(name, bm, material, sharp_angle=0)


def coin(bm, at, normal, radius, thick, rng, segments=8):
    """A struck coin: two rings and two faces, the rim a little rounded by the top ring
    being a fraction smaller."""
    normal = Vector(normal).normalized()
    side = normal.orthogonal().normalized()
    other = normal.cross(side)
    a0 = rng.uniform(0, math.tau)
    rings = []
    for h, rr in ((-thick / 2, radius), (thick / 2, radius * 0.94)):
        ring = []
        for i in range(segments):
            a = a0 + i * math.tau / segments
            ring.append(bm.verts.new(Vector(at) + (side * math.cos(a) + other * math.sin(a)) * rr + normal * h))
        rings.append(ring)
    for i in range(segments):
        j = (i + 1) % segments
        bm.faces.new((rings[0][i], rings[0][j], rings[1][j], rings[1][i]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[1])


def coin_pile(name, material, rng, centre, radius, height, count, coin_r=0.012, spill=None, floor=0.0, base=True, squash_y=1.0):
    """A heap of coins: a mound in the coins' own metal under loose coins lying on it at every
    angle, so the gaps are more coin and the edge is coins, not a dome. `spill` is an extra
    (direction, length) trail of coins across the floor."""
    bm = bmesh.new()
    centre = Vector(centre)

    def surface_z(x, y):
        d = math.sqrt((x - centre.x) ** 2 + ((y - centre.y) / squash_y) ** 2) / radius
        return centre.z + height * max(0.0, 1.0 - d * d) ** 0.8

    if base:
        rings, segs = 6, 18
        verts = [[None] * segs for _ in range(rings + 1)]
        top = bm.verts.new((centre.x, centre.y, surface_z(centre.x, centre.y) - coin_r * 0.25))
        for i in range(1, rings + 1):
            for j in range(segs):
                a = j * math.tau / segs
                d = radius * 0.97 * i / rings
                x, y = centre.x + d * math.cos(a), centre.y + d * math.sin(a) * squash_y
                verts[i][j] = bm.verts.new((x, y, surface_z(x, y) - coin_r * 0.25 + rng.uniform(-0.2, 0.2) * coin_r))
        for j in range(segs):
            k = (j + 1) % segs
            bm.faces.new((top, verts[1][j], verts[1][k]))
            for i in range(1, rings):
                bm.faces.new((verts[i][j], verts[i + 1][j], verts[i + 1][k], verts[i][k]))
    for _ in range(count):
        a = rng.uniform(0, math.tau)
        d = radius * math.sqrt(rng.random()) * 1.02
        x, y = centre.x + d * math.cos(a), centre.y + d * math.sin(a) * squash_y
        z = surface_z(x, y)
        # Coins lie along the slope, a few on edge.
        slope = Vector((math.cos(a), math.sin(a), 0)) * (height / radius) * 1.6 * (d / radius)
        nrm = (Vector((0, 0, 1)) + slope + Vector((rng.uniform(-0.35, 0.35), rng.uniform(-0.35, 0.35), 0))).normalized()
        if rng.random() < 0.08:
            nrm = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(0, 0.3))).normalized()
        coin(bm, (x, y, z + coin_r * 0.12), nrm, coin_r * rng.uniform(0.9, 1.08), coin_r * 0.18, rng)
    if spill:
        direction, length = Vector(spill[0]).normalized(), spill[1]
        for i in range(int(count * 0.35)):
            t = rng.random() ** 0.7
            p = centre + direction * (radius + length * t) + direction.orthogonal().normalized() * rng.uniform(-1, 1) * radius * (0.3 + t * 0.6)
            p.z = floor + coin_r * 0.14
            nrm = Vector((rng.uniform(-0.25, 0.25), rng.uniform(-0.25, 0.25), 1)).normalized()
            coin(bm, p, nrm, coin_r * rng.uniform(0.9, 1.08), coin_r * 0.18, rng)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return to_object(name, bm, material, sharp_angle=50)


def folded_cloth(name, material, rng, centre, size, layers=3, thick=0.012, turn=0.0):
    """A length of cloth folded in a stack: soft slabs, each a little smaller and askew, their
    folded edges rounded, the top one sagging at its corners."""
    bm = bmesh.new()
    z = 0.0
    for i in range(layers):
        w = size[0] * (1 - 0.06 * i) + rng.uniform(-0.01, 0.01)
        d = size[1] * (1 - 0.05 * i) + rng.uniform(-0.01, 0.01)
        part = board_mesh(d, w, thick, rng, seg=0.04, bevel=thick * 0.48, wear=0.0, chips=0, bow=0.0, rough_ends=0.002)
        for v in part.verts:
            v.co.y += 0.004 * noise.noise(v.co * 30)
        rot = Matrix.Rotation(turn + rng.uniform(-0.12, 0.12), 4, "Z") @ Matrix.Rotation(math.radians(90), 4, "X")
        place(part, Matrix.Translation(Vector(centre) + Vector((rng.uniform(-0.01, 0.01), rng.uniform(-0.01, 0.01), z + thick / 2))) @ rot)
        if i == layers - 1:
            c = Vector(centre)
            for v in part.verts:
                r = Vector((v.co.x - c.x, v.co.y - c.y, 0)).length / max(w, d)
                v.co.z -= max(0.0, r - 0.3) * thick * 1.2
        mesh = bpy.data.meshes.new("tmp")
        part.to_mesh(mesh)
        part.free()
        bm.from_mesh(mesh)
        bpy.data.meshes.remove(mesh)
        z += thick * 0.92
    return to_object(name, bm, material, sharp_angle=0)


# --- assembly ---------------------------------------------------------------------------------

def join(name, parts, pivot=(0.0, 0.0, 0.0)):
    """Parts into one object named `name`, its origin at `pivot` (so a lid turns about its
    hinge); the geometry stays where it was."""
    parts = [p for p in parts if p is not None]
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    if len(parts) > 1:
        bpy.ops.object.join()
    whole = bpy.context.view_layer.objects.active
    whole.name = name
    whole.data.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    pivot = Vector(pivot)
    whole.data.transform(Matrix.Translation(-pivot))
    whole.location = pivot
    return whole


def empty(name, at):
    o = bpy.data.objects.new(name, None)
    o.empty_display_size = 0.05
    o.location = Vector(at)
    bpy.context.collection.objects.link(o)
    return o


def arc_board(name, material, rng, radius, a0, a1, depth, width, axis_x=True, segments=6, chamfer=0.004, centre=(0, 0, 0)):
    """A curved board, a felloe: an arc of a ring between angles a0 and a1, `depth` thick
    radially, `width` along the axle. The ring lies in the YZ plane round X (a wheel's)."""
    sections = []
    c = chamfer
    for k in range(segments + 1):
        a = a0 + (a1 - a0) * k / segments
        ca, sa = math.cos(a), math.sin(a)

        def pt(r, x):
            return Vector(centre) + Vector((x, r * ca, r * sa))
        ro, ri, w = radius, radius - depth, width / 2
        sections.append([pt(ro, -w + c), pt(ro, w - c), pt(ro - c, w), pt(ri + c, w), pt(ri, w - c), pt(ri, -w + c), pt(ri + c, -w), pt(ro - c, -w)])
    bm = loft_mesh(sections)
    g = bm.verts.layers.float_vector.new("grain")
    t = bm.verts.layers.float.new("board")
    tone = rng.random()
    px, py = rng.uniform(-0.05, 0.05), rng.uniform(0.08, 0.2)
    am = (a0 + a1) / 2
    for v in bm.verts:
        q = v.co - Vector(centre)
        r = math.hypot(q.y, q.z)
        a = math.atan2(q.z, q.y)
        da = (a - am + math.pi) % math.tau - math.pi
        v[g] = (q.x - px, r - radius + depth / 2 + py, da * radius)
        v[t] = tone
    return to_object(name, bm, material)

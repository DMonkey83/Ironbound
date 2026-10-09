"""The parts weapons are made of: lofted blades, hafts, heads, guards, chains, cords.

Imported by `weapon_families.py`, which puts them together; not run on its own.

Everything is built in the weapon's own frame — grip at the origin, business end up +Z, the flat
of anything bladed facing Y — or built at the origin and then moved there with `place` or
`orient`. Every shape is lofted from cross-sections (`generate_goblin.loft`), because a blade
tapers in two directions at once and a haft swells and narrows, and a cone or a cylinder does
neither. A part is tagged with its `role` — edge, head, fitting, grip, haft, string, rune — so a
material variant knows what it may change and the effects line knows where the business end is.
"""
import math

from mathutils import Euler, Matrix, Vector

import generate_goblin as gg

Y = Vector((0.0, 1.0, 0.0))


# --- small maths -------------------------------------------------------------------------------

def lerp(a, b, t):
    return a + (b - a) * t


def smoothstep(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def at(value, s):
    """A parameter that may be a number, or a function of the fraction along a part."""
    return value(s) if callable(value) else value


def V(p):
    return Vector((p[0], 0.0, p[1])) if len(p) == 2 else Vector(p)


def spline(points, per=6):
    """A Catmull-Rom curve through the points, `per` samples to each span. Points are (x, z)
    pairs in the blade's plane, or 3D."""
    pts = [V(p) for p in points]
    if len(pts) < 3 or per <= 1:
        return pts
    out = []
    for i in range(len(pts) - 1):
        p0, p1, p2, p3 = pts[max(i - 1, 0)], pts[i], pts[i + 1], pts[min(i + 2, len(pts) - 1)]
        for k in range(per):
            t = k / per
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t))
    out.append(pts[-1])
    return out


def arc(centre, radius, a0, a1, n=12):
    """Points on a circle in the XZ plane, angles in degrees from +X toward +Z."""
    cx, cz = centre
    return [(cx + radius * math.cos(math.radians(lerp(a0, a1, k / n))), cz + radius * math.sin(math.radians(lerp(a0, a1, k / n)))) for k in range(n + 1)]


# --- objects -----------------------------------------------------------------------------------

def tag(obj, role):
    obj["role"] = role
    return obj


def tags(objs, role):
    for o in objs:
        o["role"] = role
    return objs


def crisp(obj, degrees=28):
    """Keep smooth shading on the curves but break it on the ridges: a blade's bevel line, a
    guard's corners. A ridge smoothed over is what makes a sword look moulded from soap."""
    obj.data.set_sharp_from_angle(angle=math.radians(degrees))
    return obj


def place(objs, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    """Move parts built at the origin into the weapon: rotation in degrees, XYZ order."""
    m = Matrix.Translation(Vector(loc)) @ Euler([math.radians(a) for a in rot]).to_matrix().to_4x4() @ Matrix.Diagonal((*scale, 1.0))
    for o in (objs if isinstance(objs, (list, tuple)) else [objs]):
        o.matrix_world = m @ o.matrix_world
    return objs


def orient(objs, origin, direction, roll=0.0):
    """Turn parts built up +Z so that +Z runs along `direction` from `origin`."""
    d = Vector(direction).normalized()
    q = d.to_track_quat("Z", "Y")
    m = Matrix.Translation(Vector(origin)) @ q.to_matrix().to_4x4() @ Matrix.Rotation(math.radians(roll), 4, "Z")
    for o in (objs if isinstance(objs, (list, tuple)) else [objs]):
        o.matrix_world = m @ o.matrix_world
    return objs


def ring(points):
    return [Vector(p) for p in points]


def ellipse(cx, cy, z, rx, ry, n=12, phase=0.0):
    return [Vector((cx + rx * math.cos(a), cy + ry * math.sin(a), z)) for a in (phase + i * 2 * math.pi / n for i in range(n))]


# --- turned and swept solids --------------------------------------------------------------------

def lathe(name, profile, material, n=12, oval=1.0, faceted=False, x=0.0, phase=0.0):
    """A solid turned about Z from (z, radius) pairs; a radius of 0 closes it to a point."""
    sections = [ellipse(x, 0.0, z, r, r * oval, n, phase) if r > 0 else [Vector((x, 0.0, z))] * n for z, r in profile]
    o = gg.loft(name, sections, material, smooth_shading=True)
    return crisp(o, 20 if faceted else 50)


def tube(name, points, radius, material, n=8, closed=False, oval=1.0):
    """A round bar swept along a path: a knuckle bow, a ring, a cord, a link. The section is
    carried along by parallel transport, so a bar that bends in three dimensions does not twist."""
    pts = [V(p) for p in points]
    if closed:
        pts = pts + [pts[0]]
    count = len(pts)
    tangents = []
    for i in range(count):
        a = pts[max(0, i - 1)] if not closed or i > 0 else pts[-2]
        b = pts[min(count - 1, i + 1)] if not closed or i < count - 1 else pts[1]
        tangents.append((b - a).normalized())
    t0 = tangents[0]
    side = t0.cross(Y) if abs(t0.dot(Y)) < 0.9 else t0.cross(Vector((1, 0, 0)))
    side.normalize()
    sections = []
    for i, p in enumerate(pts):
        if i > 0:
            rot = tangents[i - 1].rotation_difference(tangents[i])
            side = rot @ side
            side = (side - tangents[i] * side.dot(tangents[i])).normalized()
        up = side.cross(tangents[i])
        r = at(radius, i / max(1, count - 1))
        sections.append([p + (side * math.cos(k * 2 * math.pi / n) * oval + up * math.sin(k * 2 * math.pi / n)) * r for k in range(n)])
    return gg.loft(name, sections, material, cap_start=not closed, cap_end=not closed)


def torus(name, centre, major, minor, material, tilt=0.0, n=18, m=6, plane="XZ"):
    """A ring: in the blade's plane (XZ) leaned back by `tilt` degrees about X, or flat (XY)."""
    c = Vector(centre)
    pts = []
    for k in range(n):
        a = k * 2 * math.pi / n
        if plane == "XZ":
            off = Vector((major * math.cos(a), 0.0, major * math.sin(a)))
            lean = math.radians(tilt)
            off = Vector((off.x, -off.z * math.sin(lean), off.z * math.cos(lean)))
        elif plane == "YZ":
            off = Vector((0.0, major * math.cos(a), major * math.sin(a)))
        else:
            off = Vector((major * math.cos(a), major * math.sin(a), 0.0))
        pts.append(c + off)
    return tube(name, pts, minor, material, m, closed=True)


def box_ring_yz(x, zc, hh, dd, chamfer=0.3):
    """A rounded rectangle in the plane x = const: hh half-height (Z), dd half-depth (Y)."""
    c = chamfer * min(hh, dd)
    return ring([
        (x, -dd + c, zc - hh), (x, dd - c, zc - hh), (x, dd, zc - hh + c), (x, dd, zc + hh - c),
        (x, dd - c, zc + hh), (x, -dd + c, zc + hh), (x, -dd, zc + hh - c), (x, -dd, zc - hh + c),
    ])


def box_ring_z(z, hx, hy, cx=0.0, cy=0.0, n=12, square=0.55):
    """A rounded rectangle in the plane z = const: a superellipse, flat-sided, round-cornered."""
    pts = []
    for k in range(n):
        a = k * 2 * math.pi / n + math.pi / n
        c, s = math.cos(a), math.sin(a)
        pts.append(Vector((cx + hx * math.copysign(abs(c) ** square, c), cy + hy * math.copysign(abs(s) ** square, s), z)))
    return pts


def prism(name, sections, material, n=12, square=0.55, sharp=35):
    """A block lofted up Z through (z, half-x, half-y[, cx, cy]) sections: a hammer's head
    standing on its face, a tang block, a stock. Square corners rounded by `square` (1 = an
    ellipse, small = a box)."""
    rings = []
    for s in sections:
        z, hx, hy = s[0], s[1], s[2]
        cx = s[3] if len(s) > 3 else 0.0
        cy = s[4] if len(s) > 4 else 0.0
        if hx <= 0 or hy <= 0:
            rings.append([Vector((cx, cy, z))] * n)
        else:
            rings.append(box_ring_z(z, hx, hy, cx, cy, n, square))
    return crisp(gg.loft(name, rings, material), sharp)


def haft(name, z0, z1, r0, material, r1=None, n=8, swell=0.06, steps=6, oval=1.0):
    """A wooden haft from z0 to z1, r0 at the foot and r1 at the head, a little fuller in the
    middle the way a shaped stave is."""
    r1 = r0 if r1 is None else r1
    sections = []
    for i in range(steps + 1):
        t = i / steps
        r = lerp(r0, r1, t) * (1 + swell * math.sin(math.pi * t))
        sections.append(ellipse(0, 0, lerp(z0, z1, t), r, r * oval, n))
    return tag(gg.loft(name, sections, material), "haft")


def band(name, z, r, h, material, n=10, bulge=1.12):
    """A ferrule or collar round a haft: a short ring, a little proud of it."""
    return tag(lathe(name, [(z - h / 2, r * 0.98), (z - h / 2 + h * 0.2, r * bulge), (z + h / 2 - h * 0.2, r * bulge), (z + h / 2, r * 0.98)], material, n=n), "fitting")


def spike(name, base, tip, r, material, n=6, waist=0.55):
    """A tapering spike from `base` to `tip`, r at the root."""
    base, tip = V(base), V(tip)
    length = (tip - base).length
    o = lathe(name, [(0.0, r * 0.95), (length * 0.08, r), (length * 0.45, r * waist), (length, 0.0)], material, n=n, faceted=n <= 6)
    return orient(o, base, tip - base)


def spiked_ball(name, centre, r, material, spikes=12, length=0.6, spike_r=0.35, n=12):
    """A ball with spikes standing out of it all round, a morningstar's head."""
    c = V(centre)
    parts = [lathe(name, [(-r, 0.0), (-r * 0.92, r * 0.42), (-r * 0.6, r * 0.82), (0.0, r), (r * 0.6, r * 0.82), (r * 0.92, r * 0.42), (r, 0.0)], material, n=n)]
    place(parts[0], c)
    golden = math.pi * (3 - math.sqrt(5))
    for k in range(spikes):
        z = 1 - 2 * (k + 0.5) / spikes
        rr = math.sqrt(1 - z * z)
        a = golden * k
        d = Vector((rr * math.cos(a), rr * math.sin(a) * 0.9, z))
        parts.append(spike(f"{name}_Spike_{k}", c + d * r * 0.8, c + d * r * (1 + length), r * spike_r, material, 5))
    return parts


# --- flat blades -------------------------------------------------------------------------------

def _thick(q, th, sharp_l, sharp_r, bevel):
    v = 1.0
    if sharp_l:
        v *= min(1.0, q / bevel) ** 0.85
    if sharp_r:
        v *= min(1.0, (1 - q) / bevel) ** 0.85
    return th * v


SWEEP_Q = (0.0, 0.12, 0.32, 0.5, 0.68, 0.88, 1.0)


def sweep(name, path, wl, wr, t, material, sharp=(True, True), tip="point", per=6, bevel=0.34, tip_offset=0.0, crisp_deg=26):
    """A flat blade swept along a path in the XZ plane: a khopesh, a sickle, a glaive's blade,
    a hook, a prong, a kama.

    `path` runs from the root to the end, as (x, z) points (splined with `per` samples a span).
    `wl` and `wr` are the half-widths to the left of the path (the side its normal points to:
    -X for a path heading up +Z) and to the right; `t` the half-thickness. Each may be a number
    or a function of the fraction along. `sharp` says which side is an edge, thinning to
    nothing; a blunt side is a flat spine. `tip` is "point" to close at the end of the path
    (`tip_offset` across it, toward the left), or None to end square."""
    pts = spline(path, per)
    lengths = [0.0]
    for a, b in zip(pts, pts[1:]):
        lengths.append(lengths[-1] + (b - a).length)
    total = lengths[-1] or 1.0
    sl, sr = sharp
    sections = []
    for i, p in enumerate(pts):
        s = lengths[i] / total
        a, b = pts[max(i - 1, 0)], pts[min(i + 1, len(pts) - 1)]
        T = (b - a).normalized()
        N = Vector((-T.z, 0.0, T.x))
        count = len(SWEEP_Q) * 2 - (1 if sl else 0) - (1 if sr else 0)
        if tip == "point" and i == len(pts) - 1:
            sections.append([p + N * tip_offset] * count)
            continue
        A = p + N * at(wl, s)
        B = p - N * at(wr, s)
        th = at(t, s)
        top = [A.lerp(B, q) + Y * _thick(q, th, sl, sr, bevel) for q in SWEEP_Q]
        bottom = [A.lerp(B, q) - Y * _thick(q, th, sl, sr, bevel) for q in reversed(SWEEP_Q)]
        if sr:
            bottom = bottom[1:]
        if sl:
            bottom = bottom[:-1]
        sections.append(top + bottom)
    return crisp(gg.loft(name, sections, material), crisp_deg)


def plate(name, outline, centre, thick, material, edge=0.003, y0=0.0, closed=False):
    """A flat head thick at `centre` and ground thin to its outline (x, z): an axe's blade, a
    cleaver, a spade. The outline is an open curve whose two ends lie against what holds the
    head (a socket, an eye); `closed=True` makes a whole lens-shaped leaf instead."""
    cx, cz = centre
    grind = ((0.0, 1.0), (0.45, 0.62), (0.8, 0.26), (1.0, edge / max(thick, 1e-6)))
    if closed:
        rings = []
        for frac, k in grind:
            rings.append([Vector((cx + (x - cx) * frac, y0 + thick * k, cz + (z - cz) * frac)) for x, z in outline])
        back = [[Vector((p.x, y0 - (p.y - y0), p.z)) for p in r] for r in reversed(rings[:-1])]
        return crisp(gg.loft(name, rings + back, material), 30)
    sections = []
    for frac, k in grind:
        front = [(cx + (x - cx) * frac, y0 + thick * k, cz + (z - cz) * frac) for x, z in outline]
        back = [(x, y0 - (y - y0), z) for (x, y, z) in front]
        sections.append(ring(front + list(reversed(back))))
    return crisp(gg.loft(name, sections, material), 30)


# --- chains and cords --------------------------------------------------------------------------

def chain(name, points, material, link=0.07, wire=0.011, per=8):
    """A chain along a path: oval links, each turned a quarter to the last."""
    pts = spline(points, per)
    lengths = [0.0]
    for a, b in zip(pts, pts[1:]):
        lengths.append(lengths[-1] + (b - a).length)
    total = lengths[-1]
    count = max(2, int(total / (link * 0.78)))
    parts = []

    def at_len(d):
        for i in range(len(pts) - 1):
            if lengths[i + 1] >= d:
                k = (d - lengths[i]) / max(lengths[i + 1] - lengths[i], 1e-9)
                return pts[i].lerp(pts[i + 1], k), (pts[i + 1] - pts[i]).normalized()
        return pts[-1], (pts[-1] - pts[-2]).normalized()

    for j in range(count):
        c, d = at_len((j + 0.5) * total / count)
        ring_pts = []
        for k in range(8):
            a = k * 2 * math.pi / 8
            ring_pts.append(Vector((link * 0.30 * math.cos(a), 0.0, link * 0.52 * math.sin(a))))
        o = tube(f"{name}_{j}", ring_pts, wire, material, 4, closed=True)
        orient(o, c, d, roll=90.0 * (j % 2))
        parts.append(tag(o, "chain"))
    return parts


def cord(name, points, r, material, per=6, n=6, twist=0.18):
    """A rope or a thong along a path, twisted: a swollen helix round it so it reads as cord."""
    pts = spline(points, per)
    o = tube(name, pts, lambda s: r * (1 + twist * 0.5 * math.sin(s * len(pts) * 2.2)), material, n)
    return tag(o, "cord")


# --- swords: the phase-one parts ---------------------------------------------------------------

def cross_guard(name, zc, span, h, d, material, ends="knob", droop=0.0, centre=0.4, taper=0.3, knob=0.75):
    """A bar across the blade, lofted along X: square in section with chamfered corners, deepest
    at the middle where it is drilled for the tang, its quillons tapering, turned by `droop`
    (toward the blade when positive) and finished with a knob or a flared end."""
    ts = [-1.0, -0.99, -0.965, -0.93, -0.88, -0.8, -0.65, -0.45, -0.25, -0.1, 0.0,
          0.1, 0.25, 0.45, 0.65, 0.8, 0.88, 0.93, 0.965, 0.99, 1.0]
    sections = []
    for t in ts:
        a = abs(t)
        mid = 1.0 - smoothstep(0.0, 0.3, a)
        hh = h * (1 - taper * a) * (1 + centre * mid)
        dd = d * (1 - 0.5 * taper * a) * (1 + 0.5 * centre * mid)
        if ends == "knob":
            k = smoothstep(0.78, 0.95, a)
            hh *= 1 + knob * k
            dd *= 1 + 0.6 * knob * k
        elif ends == "flare":
            k = smoothstep(0.6, 1.0, a)
            hh *= 1 + knob * k
        if a > 0.93:
            r = math.sqrt(max(0.0, 1 - ((a - 0.93) / 0.07) ** 2))
            hh *= max(r, 0.0)
            dd *= max(r, 0.0)
        z = zc + (droop(t) if callable(droop) else droop * a ** 2.2)
        if hh < 1e-4:
            sections.append([Vector((span * t, 0.0, z))] * 8)
        else:
            sections.append(box_ring_yz(span * t, z, hh, dd))
    return crisp(gg.loft(name, sections, material), 35)


def grip(name, z0, z1, r, material, style="spiral", turns=8, profile="straight", bend=0.0, oval=0.86, n=10):
    """What the hand closes round, bottom z0 to top z1.

    style: spiral (a leather strip wound on, each turn a raised helix), cord (rings of cord),
    ridged (four finger grooves, a gladius's bone grip), diamond (a katana's crossed wrap),
    plain. profile: straight, barrel (swelled for a hand and a half), central (a raised ring
    between two hands), waisted. bend: how far the bottom is drawn aside, for a curved grip.
    """
    per_turn = 5
    steps = max(8, turns * per_turn) if style in ("spiral", "cord", "diamond") else 16
    sections = []
    for i in range(steps + 1):
        t = i / steps
        z = lerp(z0, z1, t)
        rr = r
        if profile == "barrel":
            rr *= 1 + 0.10 * math.sin(math.pi * t)
        elif profile == "central":
            rr *= 1 + 0.05 * math.sin(math.pi * t) + 0.16 * math.exp(-((t - 0.5) / 0.045) ** 2)
        elif profile == "waisted":
            rr *= 1 - 0.07 * math.sin(math.pi * t)
        if style == "ridged":
            phase = (t * 4) % 1.0
            rr *= 1 - 0.22 * math.sin(math.pi * phase) ** 1.5
        cx = bend * (1 - t) ** 2
        lip = 1 + 0.08 * (smoothstep(0.03, 0.0, t) + smoothstep(0.97, 1.0, t))
        pts = []
        for k in range(n):
            a = k * 2 * math.pi / n
            q = rr * lip
            if style == "spiral":
                q *= 1 + 0.075 * math.sin(2 * math.pi * turns * t - a)
            elif style == "cord":
                q *= 1 + 0.09 * math.sin(2 * math.pi * turns * t)
            elif style == "diamond":
                q *= 1 + 0.06 * max(math.sin(2 * math.pi * turns * t - a), math.sin(2 * math.pi * turns * t + a))
            pts.append(Vector((cx + q * math.cos(a), q * oval * math.sin(a), z)))
        sections.append(pts)
    return tag(gg.loft(name, sections, material), "grip")


def wheel(name, zc, r, half, material, boss=0.55, n=20):
    """A wheel pommel: a disc standing in the blade's plane, chamfered round its rim, with a
    raised boss on each face."""
    prof = [(-half * 1.45, r * boss * 0.7), (-half * 1.4, r * boss * 0.92), (-half * 1.1, r * boss),
            (-half * 1.0, r * 0.80), (-half * 0.92, r * 0.95), (-half * 0.55, r), (half * 0.55, r),
            (half * 0.92, r * 0.95), (half * 1.0, r * 0.80), (half * 1.1, r * boss),
            (half * 1.4, r * boss * 0.92), (half * 1.45, r * boss * 0.7)]
    sections = [[Vector((rr * math.cos(a), y, zc + rr * math.sin(a))) for a in (k * 2 * math.pi / n for k in range(n))] for y, rr in prof]
    return crisp(gg.loft(name, sections, material), 30)


def _thickness(kind, a, flat):
    a = abs(a)
    if kind == "diamond":
        return 1.0 - a
    if kind == "lens":
        return 1.0 - a * a
    if kind == "square":
        return 1.0 - 0.15 * a
    if a <= flat:
        return 1.0
    return ((1.0 - a) / (1.0 - flat)) ** 0.9


def edge_section(origin, across, w, t, kind="hex", flat=0.5, fuller=0.0, fuller_w=0.0):
    """A double-edged blade's cross-section: 20 points round it, both edges single points.

    `across` is the unit vector to one edge, w the half-width and t the half-thickness. A
    fuller is a rounded groove down each face, `fuller` of the thickness deep and `fuller_w`
    (in units) to each side of the middle. The sample points sit on the fuller's shoulders and
    the bevel line, so both are crisp ridges and not smeared between rings."""
    fw = min(0.8 * flat if kind == "hex" else 0.5, fuller_w / max(w, 1e-6)) if fuller_w > 0 else 0.5 * flat
    fw = max(fw, 0.05)
    s1, s2 = 0.5 * (1 + flat), flat
    if fw >= s2:
        fw = 0.8 * s2
    top = [1.0, s1, s2, fw, 0.5 * fw, 0.0, -0.5 * fw, -fw, -s2, -s1, -1.0]
    bottom = [-s1, -s2, -fw, -0.5 * fw, 0.0, 0.5 * fw, fw, s2, s1]

    def y(a):
        v = _thickness(kind, a, flat)
        if fuller > 0 and abs(a) < fw:
            v -= fuller * (1 - (a / fw) ** 2) ** 0.8
        return t * max(v, 0.12)

    return [origin + across * (a * w) + Y * y(a) for a in top] + [origin + across * (a * w) - Y * y(a) for a in bottom]


def blade_width(s, b):
    """Half-width at s (0 at the shoulder, 1 at the tip): a straight taper by `taper` of the
    width, then the point over the last `point` of the length, straight (curve 1) or ogival.
    `wave` ripples the edges, as on a flamberge."""
    w0, taper, point, curve = b["width"], b.get("taper", 0.0), b.get("point", 0.15), b.get("curve", 1.0)
    ps = 1.0 - point
    if s <= ps:
        w = w0 * (1 - taper * (s / ps if ps > 0 else 0))
    else:
        u = (s - ps) / point
        w = w0 * (1 - taper) * max(0.0, 1 - u) ** curve
    if b.get("leaf"):
        w *= 1 + b["leaf"] * math.sin(math.pi * min(1.0, s / max(ps, 1e-3))) ** 1.5
    if b.get("waist"):
        w *= 1 - b["waist"] * math.sin(math.pi * min(1.0, s / max(ps, 1e-3)))
    if b.get("wave"):
        amp, count = b["wave"]
        w *= 1 + amp * math.sin(2 * math.pi * count * s) * smoothstep(0.02, 0.1, s) * smoothstep(1.0, 0.85, s)
    return w


def straight_blade(name, b, material):
    """A straight double-edged blade from z=b['base'] to b['tip'], centred on the Z axis."""
    z0, z1 = b["base"], b["tip"]
    L = z1 - z0
    t0 = b["thick"]
    ps = 1.0 - b.get("point", 0.15)
    steps = 18 if not b.get("wave") else max(18, int(b["wave"][1] * 8))
    ss = {i / steps for i in range(steps)} | {ps, ps + 0.002} | {ps + (1 - ps) * k / 7 for k in range(1, 7)}
    ss = sorted(s for s in ss if 0 <= s < 1)
    fuller = b.get("fuller")
    sections = [edge_section(Vector((0, 0, z0 - 0.02)), Vector((1, 0, 0)), blade_width(0, b), t0, b.get("section", "hex"), b.get("flat", 0.5))]
    floor = []
    for s in ss:
        w = blade_width(s, b)
        t = t0 * (1 - b.get("distal", 0.55) * s)
        if s > ps:
            t *= (1 - (s - ps) / (1 - ps)) ** 0.45 * 0.85 + 0.15
        depth, fwa = 0.0, 0.0
        if fuller:
            f0, f1, fwa, fd = fuller
            fade = smoothstep(f0, f0 + 0.03, s) * smoothstep(f1, f1 - 0.07, s)
            depth = fd * fade
            fwa = fwa * (0.45 + 0.55 * math.sqrt(fade))
        z = z0 + L * s
        sections.append(edge_section(Vector((0, 0, z)), Vector((1, 0, 0)), w, t, b.get("section", "hex"), b.get("flat", 0.5), depth, fwa))
        if fuller and depth > 0.5 * fuller[3]:
            floor.append((z, t * max(1.0 - depth, 0.12), fwa))
    sections.append([Vector((0, 0, z1))] * 20)
    blade = crisp(gg.loft(name, sections, material), 24)
    return tag(blade, "edge"), floor


def curved_blade(name, b, material):
    """A single-edged blade sweeping back, edge on the convex side (-X), spine (+X).

    The spine runs as a parabola from the guard, `sweep` aside at the tip (negative sweeps it
    forward, over the edge, as a kukri or falcata does). The edge stands `width` off it and
    flares by `flare` toward the tip; from `clip` on the back is cut away in a straight false
    edge down to the point, which sits `toff` of the width in from the spine line — a clip
    point, the yelman of a kilij. `step` makes the back stand out where the clip starts. A
    narrow fuller runs near the spine unless `fuller` is None."""
    z0, L, sweep_ = b["base"], b["length"], b["sweep"]
    W0, flare, sc, toff = b["width"], b.get("flare", 0.0), b.get("clip", 0.85), b.get("toff", 0.3)
    t0 = b["thick"]
    half = W0 / 2
    bend = b.get("bend", 2.0)                                    # 2: a parabola; 1: a straight lean

    def spine(s):
        return Vector((half + sweep_ * s ** bend, 0.0, z0 + L * s))

    def normal(s):
        tz = Vector((bend * sweep_ * s ** (bend - 1) if s > 0 else (sweep_ if bend == 1 else 0.0), 0.0, L)).normalized()
        return Vector((-tz.z, 0.0, tz.x))

    Wc = W0 * (1 + flare * sc * sc)
    step = b.get("step", 0.0) * W0
    qs_top = [0.78, 0.56, 0.42, 0.31, 0.20, 0.09, 0.0]
    fz = b.get("fuller", (0.03, 0.62, 0.20, 0.42, 0.35))
    belly = b.get("belly", 0.0)

    def section(s):
        N = normal(s)
        if s <= sc:
            db, de, tb = 0.0, W0 * (1 + flare * s * s + belly * math.sin(math.pi * s / sc)), 1.0
        else:
            u = (s - sc) / (1 - sc)
            Wn = W0 * (1 + flare * sc * sc)
            db = -step * (1 - u) + toff * Wn * u
            de = toff * Wn + (Wn - toff * Wn) * max(0.0, 1 - u ** 2.0) ** 0.75
            tb = max(0.10, (1 - u) ** 1.4)
        t = t0 * (1 - 0.5 * s)
        B = spine(s) + N * db
        E = spine(s) + N * de
        fade = smoothstep(fz[0], fz[0] + 0.04, s) * smoothstep(fz[1], fz[1] - 0.08, s) if fz else 0.0

        def th(q):
            v = (1 - 0.12 * q) if q < 0.45 else 0.946 * ((1 - q) / 0.55) ** 0.9
            v *= lerp(tb, 1.0, smoothstep(0.0, 0.5, q))
            if fade > 0 and fz[2] < q < fz[3]:
                m = (q - fz[2]) / (fz[3] - fz[2])
                v -= fz[4] * fade * math.sin(math.pi * m) ** 0.8
            return t * max(v, 0.1)

        pts = [E]
        pts += [B + (E - B) * q + Y * th(q) for q in qs_top]
        pts += [B + (E - B) * q - Y * th(q) for q in reversed(qs_top)]
        return pts

    ss = sorted({i / 16 for i in range(16)} | {sc, sc + 0.004} | {sc + (1 - sc) * k / 8 for k in range(1, 8)})
    base = [p - Vector((0, 0, 0.02)) for p in section(0.0)]
    tip = spine(1.0) + normal(1.0) * toff * Wc
    sections = [base] + [section(s) for s in ss if s < 1] + [[tip] * 15]
    info = {"spine": spine, "normal": normal, "Wc": Wc}
    return tag(crisp(gg.loft(name, sections, material), 26), "edge"), tip, info

"""The catalogue: every weapon id with a model, its family and its parameters.

Read by `weapon_families.py`; see its docstring for the format. Ids are exactly those of
`src/Ironbound.Game/content/weapons/*.json`. Units are the human models' (one unit is about
0.45 m); grip at the origin, business end up +Z, the flat of a blade facing Y, an edge or a
head's business side toward -X.

Each section is one family, and one contact sheet. Rows are written to be read against the real
weapon: the comment says what makes it itself, and the numbers are what make that show at 64 px.
"""
import math

RECIPES = {}

# The models built before the families, kept as they were, and what an icon needs to know of them.
HANDS = {
    "greataxe": "two", "cold-iron-greataxe": "two", "light-mace": "light", "shortspear": "one",
    "quarterstaff": "two", "shortbow": "bow", "light-crossbow": "crossbow",
    "heavy-shield": "shield", "light-shield": "shield",
}
LEGACY_FAMILY = {
    "greataxe": "axes", "cold-iron-greataxe": "axes", "light-mace": "hammers", "shortspear": "spears",
    "quarterstaff": "double", "shortbow": "bows", "light-crossbow": "crossbows",
    "heavy-shield": "shields", "light-shield": "shields",
}


def S(path, wl=0.03, wr=None, t=0.012, m="Steel", **kw):
    """A swept flat blade (see weapon_parts.sweep)."""
    return dict(sweep=path, wl=wl, wr=wl if wr is None else wr, t=t, m=m, **kw)


def taper(w0, w1=0.0, k=1.0):
    return lambda s: w0 + (w1 - w0) * s ** k


def hand(m="Wrap", cuff=0.22, cuff_m=None, plated=False):
    """A closed fist in a glove or a gauntlet: the hand, four curled fingers, a thumb, a cuff."""
    out = [dict(prism=[(-0.07, 0.05, 0.032), (-0.05, 0.056, 0.036), (0.03, 0.058, 0.036), (0.05, 0.05, 0.03)], m=m, role="grip", square=0.8),
           dict(lathe=[(-0.13, 0.044), (-0.10, 0.046), (-0.07, 0.048)], m=m, n=10, oval=0.72, role="grip"),
           dict(lathe=[(-0.13 - cuff, 0.068), (-0.14 - cuff, 0.064), (-0.13, 0.05), (-0.12, 0.046)], m=cuff_m or m, n=12, oval=0.8, role="fitting")]
    for x in (-0.042, -0.014, 0.014, 0.042):
        out.append(dict(tube=[(x, -0.012, 0.04), (x, -0.004, 0.084), (x, 0.035, 0.092), (x, 0.056, 0.05), (x, 0.048, 0.01)], per=3, r=0.0155, m=m, n=6, role="grip"))
    out.append(dict(tube=[(0.055, 0.02, -0.04), (0.072, 0.04, 0.0), (0.05, 0.065, 0.04), (0.02, 0.07, 0.05)], per=3, r=0.017, m=m, n=6, role="grip"))
    if plated:
        out.append(dict(prism=[(-0.06, 0.052, 0.008, 0, -0.038), (0.035, 0.054, 0.008, 0, -0.038)], m=cuff_m or m, role="fitting", square=0.5))
        out.append(dict(tube=[(-0.058, -0.03, 0.06), (0.058, -0.03, 0.06)], r=0.014, m=cuff_m or m, n=6, role="fitting"))
    return out


# --- swords: straight double-edged blades -------------------------------------------------------

GLADIUS = dict(
    family="swords", build="straight", hands="light", metal="Steel", fittings="Bronze", grip_material="Bone",
    blade=dict(base=0.245, tip=1.30, width=0.068, thick=0.020, section="lens", taper=0.0, point=0.17, curve=1.0, distal=0.25),
    guard=dict(kind="disc", bottom=0.165, top=0.255, rx=0.115, ry=0.078),
    grip=dict(bottom=-0.05, top=0.17, radius=0.037, style="ridged", profile="straight"),
    pommel=dict(kind="ovoid", top=-0.05, height=0.13, radius=0.085, oval=0.78, nut="Bronze"),
)
LONGSWORD = dict(
    family="swords", build="straight", hands="one", metal="Steel", fittings="DarkIron",
    blade=dict(base=0.235, tip=2.00, width=0.070, thick=0.019, section="hex", flat=0.5, taper=0.48, point=0.12, curve=0.78,
               fuller=(0.0, 0.66, 0.020, 0.55)),
    guard=dict(kind="cross", z=0.222, span=0.30, height=0.017, depth=0.024, ends="knob", knob=0.55, centre=0.6, taper=0.25),
    grip=dict(bottom=-0.245, top=0.21, radius=0.035, style="spiral", turns=10, profile="barrel"),
    pommel=dict(kind="wheel", top=-0.235, radius=0.080, half=0.022),
)
GREATSWORD = dict(
    family="swords", build="straight", hands="two", metal="Steel", fittings="DarkIron",
    blade=dict(base=0.58, tip=2.95, width=0.098, thick=0.021, section="hex", flat=0.5, taper=0.32, point=0.10, curve=0.72,
               fuller=(0.0, 0.62, 0.026, 0.55)),
    ricasso=(0.27, 0.60, 0.068, 0.034),
    lugs=(0.615, 0.125, 0.034),
    guard=dict(kind="cross", z=0.245, span=0.43, height=0.024, depth=0.030, ends="knob", knob=0.7, droop=0.075, centre=0.6, taper=0.3,
               rings=(0.058, 0.300, 0.118, 28)),
    grip=dict(bottom=-0.52, top=0.225, radius=0.040, style="spiral", turns=15, profile="central"),
    pommel=dict(kind="pear", top=-0.52, height=0.24, radius=0.085),
)

RECIPES.update({
    # A dagger: a narrow blade two hands long tapering evenly to its point, diamond in section;
    # a short straight cross; a wound grip; a faceted scent-stopper pommel.
    "dagger": dict(
        family="knives", build="straight", hands="light", metal="Steel", fittings="DarkIron",
        blade=dict(base=0.225, tip=0.80, width=0.050, thick=0.021, section="diamond", taper=0.0, point=1.0, curve=0.92, distal=0.3),
        guard=dict(kind="cross", z=0.218, span=0.135, height=0.015, depth=0.022, ends="knob", knob=0.9, centre=0.5, taper=0.2),
        grip=dict(bottom=-0.045, top=0.21, radius=0.032, style="spiral", turns=6),
        pommel=dict(kind="scent", top=-0.045, height=0.13, radius=0.046),
    ),
    # A gladius: a broad straight blade with parallel edges and a short angular point; an oval
    # hilt-guard; a bone grip ridged for four fingers; a big rounded pommel. Bronze fittings.
    "gladius": GLADIUS,
    # The short sword is the gladius's shape in plain iron, with a leather-wound grip.
    "short-sword": dict(like="gladius", fittings="DarkIron", grip_material="Wrap",
                        grip=dict(style="spiral", turns=6), pommel=dict(nut="DarkIron")),
    # A longsword: a long slender blade tapering gently, a fuller two-thirds of its length; a long
    # straight cross; a grip for a hand and a half; a wheel pommel.
    "longsword": LONGSWORD,
    # The same longsword in alchemical silver, polished to a mirror, fittings and all.
    "silvered-longsword": dict(like="longsword", variant="silver"),
    # A zweihander: a very long blade above a leather-wrapped ricasso with parrying lugs; a wide
    # cross with turned quillons and side rings; a grip as long as a forearm; a big pear pommel.
    "greatsword": GREATSWORD,
    # Aldric's: the same zweihander, gilt where the plain one is iron, a rune line in the fuller.
    "greatsword-plus-one": dict(like="greatsword", fittings="Gold", variant="+1"),
})


def helix(z0, z1, radius, turns, n=40, oval=0.8):
    return [(radius * math.cos(2 * math.pi * turns * k / n), radius * oval * math.sin(2 * math.pi * turns * k / n), z0 + (z1 - z0) * k / n) for k in range(n + 1)]


def branches(z0, step, count, reach=0.13, rise=0.20, root=0.035, w=0.019, m="Steel"):
    """Side branches curving up off a blade, alternating sides: the seven-branched sword's."""
    out = []
    for k in range(count):
        sx = 1 if k % 2 == 0 else -1
        z = z0 + k * step
        path = [(sx * root, z), (sx * (root + reach * 0.45), z + rise * 0.15), (sx * (root + reach * 0.85), z + rise * 0.55), (sx * (root + reach), z + rise)]
        out.append(S(path, taper(w, 0.004), taper(w, 0.004), taper(0.012, 0.003), m=m, sharp=(True, True), per=4))
    return out


RECIPES.update({
    # A bastard sword: bigger than the longsword in every way that matters — a broader blade
    # with a short fuller, a grip long enough for two hands, a flared cross whose quillons turn
    # toward the blade, and a heavy faceted pear of a pommel.
    "bastard-sword": dict(
        like="longsword",
        blade=dict(base=0.25, tip=2.30, width=0.084, thick=0.021, taper=0.42, point=0.11, curve=0.75, fuller=(0.0, 0.45, 0.022, 0.5)),
        guard=dict(z=0.235, span=0.34, height=0.018, depth=0.026, ends="flare", knob=0.9, droop=0.05, centre=0.7, taper=0.35),
        grip=dict(bottom=-0.40, top=0.222, radius=0.036, turns=13, profile="waisted"),
        pommel=dict(kind="pear", top=-0.40, height=0.17, radius=0.07),
    ),
    # An estoc: no edge worth the name, a stiff narrow blade of diamond section for punching
    # through mail, long; a plain cross with a side ring; a long grip; a faceted pommel.
    "estoc": dict(
        family="swords", build="straight", hands="one", metal="Steel", fittings="DarkIron",
        blade=dict(base=0.235, tip=2.55, width=0.044, thick=0.028, section="diamond", taper=0.35, point=0.10, curve=0.9, distal=0.4),
        guard=dict(kind="ring", z=0.222, span=0.24, height=0.015, depth=0.02, ends="knob", knob=0.9, ring=0.05, centre=0.5),
        grip=dict(bottom=-0.36, top=0.21, radius=0.034, turns=12, profile="barrel"),
        pommel=dict(kind="scent", top=-0.36, height=0.15, radius=0.056),
    ),
    # A rapier: a long thin blade of diamond section on a swept hilt — S-curved quillons, a
    # knuckle bow down to the pommel, a ring on the outside of the hand.
    "rapier": dict(
        family="swords", build="straight", hands="one", metal="Steel", fittings="Steel",
        blade=dict(base=0.235, tip=2.40, width=0.031, thick=0.016, section="diamond", taper=0.45, point=0.08, curve=1.0, distal=0.5),
        guard=dict(kind="swept", z=0.215, span=0.27, height=0.012, depth=0.014, ends="knob", knob=1.3, droop=0.08, centre=0.4, taper=0.2, ring=0.065, bow=0.085),
        grip=dict(bottom=-0.13, top=0.205, radius=0.031, style="cord", turns=9),
        pommel=dict(kind="ball", top=-0.13, radius=0.046, height=0.09),
    ),
    # A spiral rapier: the rapier with a twisted bar wound round the hand from guard to pommel.
    "spiral-rapier": dict(
        like="rapier", fittings="Bronze",
        guard=dict(kind="cross", ring=0.0, span=0.20, droop=0.0),
        extra=[dict(tube=helix(-0.12, 0.20, 0.072, 2.2), r=0.0085, m="Bronze", role="fitting", n=6)],
    ),
    # A sword cane: a slim blade drawn out of a walking stick, the stick's crook for a grip, no guard.
    "sword-cane": dict(
        family="swords", build="straight", hands="one", metal="Steel", fittings="Gold", grip_material="Wood",
        blade=dict(base=0.20, tip=1.80, width=0.026, thick=0.015, section="diamond", taper=0.3, point=0.08, curve=1.0, distal=0.4),
        guard=dict(kind="none"),
        grip=dict(bottom=-0.20, top=0.21, radius=0.034, style="plain", oval=1.0),
        pommel=dict(kind="crook", top=-0.20, radius=0.034, m="Wood"),
        extra=[dict(lathe=[(0.17, 0.034), (0.18, 0.040), (0.225, 0.040), (0.235, 0.03)], m="Gold", role="fitting")],
    ),
    # A flambard: a two-handed sword with a flame-waved blade, ricasso and lugs, a broad cross
    # with quillons swept down, a round pommel.
    "flambard": dict(
        like="greatsword",
        blade=dict(tip=2.80, width=0.090, wave=(0.40, 6.5), fuller=None),
        guard=dict(rings=None, droop=-0.05, span=0.40, ends="knob", knob=1.0),
        pommel=dict(kind="ball", radius=0.072, height=0.15),
    ),
    # A split-blade sword: two blades side by side that part toward the tip.
    "split-blade-sword": dict(
        family="swords", build="straight", hands="one", metal="Steel", fittings="Bronze",
        blade=dict(base=0.235, tip=1.95, width=0.10, thick=0.016, section="hex", flat=0.4, taper=0.2, point=0.14, curve=0.9, split=(0.012, 2.2), steps=10),
        guard=dict(kind="cross", z=0.222, span=0.22, height=0.022, depth=0.03, ends="flare", knob=0.5, centre=0.8),
        grip=dict(bottom=-0.22, top=0.21, radius=0.035, turns=6),
        pommel=dict(kind="knob", radius=0.05, height=0.09),
    ),
    # The seven-branched sword: a straight blade with three branches up each side, alternating,
    # curving up like a tree's: six and the tip.
    "seven-branched-sword": dict(
        family="swords", build="straight", hands="two", metal="Steel", fittings="Gold",
        blade=dict(base=0.24, tip=2.30, width=0.042, thick=0.017, section="diamond", taper=0.3, point=0.08, curve=1.0),
        guard=dict(kind="bar", z=0.225, span=0.10, height=0.02, depth=0.03, ends="plain", centre=0.2),
        grip=dict(bottom=-0.40, top=0.21, radius=0.036, style="cord", turns=12),
        pommel=dict(kind="ring", radius=0.04, ring=0.05),
        extra=branches(0.75, 0.24, 6),
    ),
    # A kasatha's spinal sword: a light blade of bone, a grip of vertebrae.
    "kasatha-spinal-sword": dict(
        family="swords", build="straight", hands="light", metal="Bone", fittings="Bone", grip_material="Bone",
        blade=dict(base=0.20, tip=1.05, width=0.055, thick=0.022, section="lens", taper=0.25, point=0.25, curve=0.8, leaf=0.12),
        guard=dict(kind="cross", z=0.19, span=0.12, height=0.025, depth=0.03, ends="knob", knob=0.6, centre=0.3),
        grip=dict(bottom=-0.08, top=0.18, radius=0.034, style="ridged"),
        pommel=dict(kind="knob", radius=0.045, height=0.08),
    ),
})


# --- sabres: single-edged curved blades ---------------------------------------------------------

RECIPES.update({
    # A scimitar: a single-edged blade sweeping back, widening toward the tip and ending in a clip
    # point; a short cross with ball quillons and langets; a grip curving toward the edge.
    "scimitar": dict(
        family="sabres", build="curved", hands="one", metal="Steel", fittings="DarkIron",
        blade=dict(base=0.225, length=1.57, sweep=0.32, width=0.112, flare=0.42, clip=0.78, toff=0.45, step=0.16, thick=0.022),
        guard=dict(kind="cross", z=0.212, span=0.15, height=0.016, depth=0.023, ends="knob", knob=1.1, centre=0.5, taper=0.15, langets=(0.075, 0.06)),
        grip=dict(bottom=-0.205, top=0.205, radius=0.033, style="spiral", turns=8, bend=0.045),
        pommel=dict(kind="beak", radius=0.040, height=0.085, beak=0.045),
    ),
})


# --- sabres: single-edged and curved swords ----------------------------------------------------

def spine_at(b, s):
    """A point on a curved blade's back, and the way out of it (away from the edge)."""
    half = b["width"] / 2
    x = half + b["sweep"] * s * s
    z = b["base"] + b["length"] * s
    t = (2 * b["sweep"] * s, b["length"])
    n = math.hypot(*t)
    return (x, z), (t[1] / n, -t[0] / n), (t[0] / n, t[1] / n)


def spine_teeth(b, s0, s1, count, size=0.04, m="Steel"):
    """Saw teeth along a curved blade's back, raked toward the tip."""
    out = []
    for k in range(count):
        (x, z), (ox, oz), (tx, tz) = spine_at(b, s0 + (s1 - s0) * k / max(1, count - 1))
        out.append(S([(x - ox * 0.01, z - oz * 0.01), (x + ox * size + tx * size * 0.6, z + oz * size + tz * size * 0.6)], 0.016, 0.016, 0.006, m=m, per=1))
    return out


def spine_rings(b, s0, s1, count, radius=0.03, m="Steel"):
    """Rings hung through holes along a dao's back: the nine-ring broadsword's."""
    out = []
    for k in range(count):
        (x, z), (ox, oz), _ = spine_at(b, s0 + (s1 - s0) * k / max(1, count - 1))
        out.append(dict(torus=(x + ox * radius * 0.45, 0.0, z + oz * radius * 0.45), major=radius, minor=0.0065, m=m, n=10, sides=4, role="fitting"))
    return out


def spine_thorns(b, s0, s1, count, size=0.06, m="Steel"):
    """Thorns curving back off a blade's spine toward the hilt."""
    out = []
    for k in range(count):
        (x, z), (ox, oz), (tx, tz) = spine_at(b, s0 + (s1 - s0) * k / max(1, count - 1))
        path = [(x - ox * 0.005, z), (x + ox * size * 0.6 - tx * size * 0.1, z + oz * size * 0.6), (x + ox * size - tx * size * 0.55, z + oz * size - tz * size * 0.55)]
        out.append(S(path, taper(0.014, 0.002), taper(0.014, 0.002), 0.006, m=m, per=3))
    return out


KATANA = dict(
    family="sabres", build="curved", hands="one", metal="Steel", fittings="DarkIron",
    blade=dict(base=0.27, length=1.62, sweep=0.10, width=0.090, flare=-0.15, clip=0.93, toff=0.06, thick=0.021, fuller=None),
    guard=dict(kind="tsuba", z=0.235, rx=0.10, ry=0.088, height=0.013),
    grip=dict(bottom=-0.34, top=0.225, radius=0.034, style="diamond", turns=7, oval=0.74),
    pommel=dict(kind="cap", radius=0.036, height=0.035, oval=0.72),
)
NINE_RING = dict(base=0.25, length=1.40, sweep=0.12, width=0.13, flare=0.35, clip=0.84, toff=0.45, thick=0.02)
SAWTOOTH = dict(base=0.23, length=1.45, sweep=0.24, width=0.10, flare=0.15, clip=0.85, toff=0.4, thick=0.02)
THORN = dict(base=0.23, length=1.40, sweep=0.18, width=0.072, flare=0.15, clip=0.86, toff=0.3, thick=0.018)

RECIPES.update({
    # A katana: a long, gently curved single-edged blade with a kissaki point, a round tsuba and
    # a gilt habaki, a long grip in a crossed wrap, a small cap.
    "katana": KATANA,
    # A wakizashi: the katana's shorter companion.
    "wakizashi": dict(like="katana", hands="light", blade=dict(length=1.08, width=0.085), grip=dict(bottom=-0.17), guard=dict(rx=0.082, ry=0.072)),
    # A nodachi: a field sword, a katana grown to two hands' and a half's length.
    "nodachi": dict(like="katana", hands="two", blade=dict(base=0.29, length=2.30, sweep=0.16, width=0.098), guard=dict(z=0.255, rx=0.115, ry=0.10),
                    grip=dict(bottom=-0.62, top=0.245, turns=11)),
    # A cutlass: a short, broad, lightly curved sabre under a brass shell guard and knuckle bow.
    "cutlass": dict(
        family="sabres", build="curved", hands="one", metal="Steel", fittings="Bronze",
        blade=dict(base=0.22, length=1.30, sweep=0.14, width=0.13, flare=0.15, clip=0.84, toff=0.45, thick=0.02),
        guard=dict(kind="shell", z=0.21, radius=0.13, oval=0.72, bow=0.06),
        grip=dict(bottom=-0.19, top=0.20, radius=0.034, style="cord", turns=7, bend=0.02),
        pommel=dict(kind="cap", radius=0.04, height=0.035),
    ),
    # A falchion: a heavy single-edged blade that widens toward the tip like a cleaver, its point
    # clipped down near the back; a plain cross, a long grip, a disc pommel.
    "falchion": dict(
        family="sabres", build="curved", hands="two", metal="Steel", fittings="DarkIron",
        blade=dict(base=0.25, length=1.55, sweep=0.04, width=0.11, flare=0.95, clip=0.82, toff=0.12, thick=0.022, fuller=None),
        guard=dict(kind="cross", z=0.235, span=0.20, height=0.02, depth=0.026, ends="knob", droop=-0.03, centre=0.5, knob=0.6),
        grip=dict(bottom=-0.36, top=0.222, radius=0.036, style="spiral", turns=11),
        pommel=dict(kind="wheel", radius=0.065, half=0.02),
    ),
    # The Aldori dueling sword: long, narrow, slightly curved; a simple guard with a ring.
    "aldori-dueling-sword": dict(
        family="sabres", build="curved", hands="one", metal="Steel", fittings="Steel",
        blade=dict(base=0.23, length=1.70, sweep=0.13, width=0.064, flare=0.0, clip=0.88, toff=0.35, thick=0.017),
        guard=dict(kind="ring", z=0.215, span=0.17, height=0.013, depth=0.018, ends="knob", knob=1.0, ring=0.048, centre=0.4),
        grip=dict(bottom=-0.18, top=0.205, radius=0.031, style="cord", turns=8),
        pommel=dict(kind="ball", radius=0.036, height=0.07),
    ),
    # An elven curve blade: two-handed, long, slender and deeply curved; a gilt guard whose
    # quillons sweep up like leaves; a long grip.
    "elven-curve-blade": dict(
        family="sabres", build="curved", hands="two", metal="Steel", fittings="Gold",
        blade=dict(base=0.30, length=2.25, sweep=0.34, width=0.07, flare=0.1, clip=0.88, toff=0.2, thick=0.018),
        guard=dict(kind="cross", z=0.272, span=0.14, height=0.02, depth=0.024, ends="flare", knob=1.4, droop=0.12, centre=0.6, taper=0.1),
        grip=dict(bottom=-0.55, top=0.26, radius=0.033, style="spiral", turns=14, bend=0.03),
        pommel=dict(kind="beak", radius=0.036, height=0.07, beak=0.03),
    ),
    # A nine-ring broadsword: a broad dao, nine rings hung through its back.
    "nine-ring-broadsword": dict(
        family="sabres", build="curved", hands="one", metal="Steel", fittings="Bronze",
        blade=NINE_RING,
        guard=dict(kind="tsuba", z=0.235, rx=0.07, ry=0.05, height=0.012, gold=False),
        grip=dict(bottom=-0.20, top=0.22, radius=0.034, style="cord", turns=8, bend=0.02),
        pommel=dict(kind="ring", radius=0.035, ring=0.04),
        parts=spine_rings(NINE_RING, 0.10, 0.70, 9, m="Bronze"),
    ),
    # A butterfly sword: short and broad like a cleaver's blade, a D-guard round the knuckles.
    "butterfly-sword": dict(
        family="sabres", build="curved", hands="light", metal="Steel", fittings="Bronze",
        blade=dict(base=0.20, length=0.78, sweep=0.03, width=0.125, flare=0.15, clip=0.72, toff=0.5, thick=0.018, fuller=None),
        guard=dict(kind="knuckle", z=0.19, span=0.075, height=0.016, depth=0.02, ends="knob", knob=0.6, bow=0.05),
        grip=dict(bottom=-0.08, top=0.18, radius=0.032, style="cord", turns=6),
        pommel=dict(kind="cap", radius=0.035, height=0.025),
    ),
    # A double chicken saber: a curved saber with a hook at its tip and a crescent before the hand.
    "double-chicken-saber": dict(
        family="sabres", build="curved", hands="one", metal="Steel", fittings="Steel",
        blade=dict(base=0.24, length=1.30, sweep=0.10, width=0.085, flare=0.2, clip=0.92, toff=0.6, thick=0.018),
        guard=dict(kind="crescent", z=0.22, span=0.15, drop=0.10),
        grip=dict(bottom=-0.16, top=0.20, radius=0.032, style="cord", turns=7),
        pommel=dict(kind="spike", radius=0.02, length=0.12),
        parts=[S([(0.13, 1.40), (0.20, 1.52), (0.29, 1.53), (0.33, 1.44), (0.30, 1.36)], taper(0.026, 0.008), taper(0.016, 0.004), 0.012, sharp=(True, False), per=4)],
    ),
    # A sawtooth sabre: a curved sabre with saw teeth raked along its back.
    "sawtooth-sabre": dict(
        family="sabres", build="curved", hands="one", metal="Steel", fittings="DarkIron",
        blade=SAWTOOTH,
        guard=dict(kind="knuckle", z=0.215, span=0.12, height=0.014, depth=0.02, ends="knob", knob=0.8, bow=0.07),
        grip=dict(bottom=-0.19, top=0.20, radius=0.032, style="spiral", turns=8, bend=0.03),
        pommel=dict(kind="beak", radius=0.036, height=0.07, beak=0.03),
        parts=spine_teeth(SAWTOOTH, 0.12, 0.74, 12, 0.035),
    ),
    # A broken-back seax: a heavy straight single-edged knife-sword, its back rising to an angle
    # and then running straight down to the point; no guard, a long wooden grip.
    "broken-back-seax": dict(
        family="sabres", build="curved", hands="one", metal="Steel", fittings="DarkIron", grip_material="Wood",
        blade=dict(base=0.20, length=1.10, sweep=0.0, width=0.115, flare=0.10, clip=0.60, toff=0.12, step=0.10, thick=0.026, fuller=(0.04, 0.55, 0.15, 0.32, 0.3)),
        guard=dict(kind="bar", z=0.19, span=0.07, height=0.012, depth=0.032, ends="plain", centre=0.0),
        grip=dict(bottom=-0.24, top=0.18, radius=0.037, style="plain", oval=0.8),
        pommel=dict(kind="cap", radius=0.036, height=0.02),
    ),
    # A temple sword: a heavy blade curving forward over its edge, rings hung from the pommel.
    "temple-sword": dict(
        family="sabres", build="curved", hands="one", metal="Steel", fittings="Bronze",
        blade=dict(base=0.22, length=1.42, sweep=-0.16, bend=3.2, width=0.085, flare=0.35, clip=0.84, toff=0.5, thick=0.022, fuller=None),
        guard=dict(kind="cross", z=0.21, span=0.11, height=0.016, depth=0.022, ends="knob", knob=0.8, centre=0.5),
        grip=dict(bottom=-0.18, top=0.20, radius=0.033, style="cord", turns=7),
        pommel=dict(kind="ring", radius=0.036, ring=0.045),
        parts=[dict(torus=(0.0, 0.0, -0.33), major=0.03, minor=0.006, m="Bronze", plane="YZ", n=12, sides=5, role="fitting")],
    ),
    # A falcata: a blade curving forward over its edge, widest near the tip; a hooked bird's-head
    # hilt closing round the fingers.
    "falcata": dict(
        family="sabres", build="curved", hands="one", metal="Steel", fittings="DarkIron",
        blade=dict(base=0.22, length=1.25, sweep=-0.17, width=0.085, flare=0.9, clip=0.80, toff=0.35, thick=0.022),
        guard=dict(kind="knuckle", z=0.21, span=0.08, height=0.016, depth=0.022, ends="plain", bow=0.03),
        grip=dict(bottom=-0.16, top=0.20, radius=0.033, style="spiral", turns=6, bend=-0.03),
        pommel=dict(kind="beak", radius=0.04, height=0.10, beak=-0.07),
    ),
    # A rhoka sword: a heavy, broad, slightly curved blade whose tip forks into two points.
    "rhoka-sword": dict(
        family="sabres", build="curved", hands="one", metal="Steel", fittings="DarkIron",
        blade=dict(base=0.23, length=1.45, sweep=0.06, width=0.13, flare=0.2, clip=0.90, toff=0.55, thick=0.022),
        guard=dict(kind="cross", z=0.215, span=0.16, height=0.018, depth=0.024, ends="flare", knob=0.8, centre=0.6),
        grip=dict(bottom=-0.20, top=0.20, radius=0.034, style="spiral", turns=8),
        pommel=dict(kind="cap", radius=0.04, height=0.03),
        parts=[S([(0.11, 1.48), (0.13, 1.60), (0.115, 1.72)], taper(0.028, 0.004), taper(0.012, 0.003), 0.012, sharp=(True, False), per=3)],
    ),
    # An elven thornblade: slender, curved, thorns curling back off its spine; a gilt leaf guard.
    "elven-thornblade": dict(
        family="sabres", build="curved", hands="one", metal="Steel", fittings="Gold", grip_material="Wood",
        blade=THORN,
        guard=dict(kind="cross", z=0.215, span=0.11, height=0.018, depth=0.022, ends="flare", knob=1.3, droop=0.08, centre=0.4, taper=0.1),
        grip=dict(bottom=-0.18, top=0.20, radius=0.031, style="spiral", turns=7),
        pommel=dict(kind="knob", radius=0.033, height=0.065),
        parts=spine_thorns(THORN, 0.15, 0.70, 5, 0.07),
    ),
    # A hook sword: a narrow blade ending in a big hook like a shepherd's crook, a crescent of
    # steel before the knuckles, a spike for a pommel.
    "shang-gou": dict(
        family="sabres", build="straight", hands="light", metal="Steel", fittings="Steel",
        blade=dict(base=0.24, tip=1.38, width=0.040, thick=0.016, section="hex", flat=0.4, taper=0.0, point=0.02, curve=1.0, steps=12),
        guard=dict(kind="crescent", z=0.22, span=0.16, drop=0.12),
        grip=dict(bottom=-0.16, top=0.20, radius=0.031, style="cord", turns=7),
        pommel=dict(kind="spike", radius=0.02, length=0.13),
        parts=[S([(0.0, 1.33), (0.02, 1.47), (0.10, 1.56), (0.19, 1.53), (0.22, 1.43)], 0.032, 0.012, 0.014, sharp=(True, False), per=5)],
    ),
    # A khopesh: a bronze sickle-sword — a straight neck from the grip, then a great crescent
    # bowing out with its edge on the outside, ending in a hooked point.
    "khopesh": dict(
        family="sabres", build="hafted", hands="one", fittings="Bronze",
        parts=[dict(grip=(-0.12, 0.22, 0.034), m="Wrap", style="spiral", turns=6),
               dict(lathe=[(-0.16, 0.0), (-0.155, 0.036), (-0.12, 0.04), (-0.11, 0.032)], m="Bronze", role="fitting"),
               dict(prism=[(0.215, 0.05, 0.026), (0.25, 0.045, 0.022)], m="Bronze", role="fitting"),
               S([(0.0, 0.24), (0.0, 0.58)], 0.022, 0.022, 0.016, m="Bronze", sharp=(False, False), tip=None, per=1),
               S([(0.0, 0.56), (-0.07, 0.72), (-0.15, 0.92), (-0.13, 1.10), (-0.01, 1.20), (0.10, 1.13)],
                 lambda s: 0.016 + 0.07 * math.sin(math.pi * min(1, s * 1.1)) ** 0.8, 0.016, 0.016, m="Bronze", sharp=(True, False), per=5)],
        fx=((0, 0, 0.56), (-0.1, 0, 1.2)),
    ),
    # A shotel: a double-edged blade bent into a near half-circle, on a plain grip.
    "shotel": dict(
        family="sabres", build="hafted", hands="one",
        parts=[dict(grip=(-0.16, 0.22, 0.032), m="Horn", style="plain", oval=0.9),
               dict(lathe=[(0.20, 0.036), (0.22, 0.04), (0.25, 0.03)], m="DarkIron", role="fitting"),
               S([(0.0, 0.24)] + [(x - 0.40, z) for x, z in [(0.40 * math.cos(math.radians(a)), 0.24 + 0.40 * math.sin(math.radians(a))) for a in range(5, 175, 15)]],
                 taper(0.026, 0.008), taper(0.026, 0.008), 0.012, sharp=(True, True), per=3)],
    ),
    # A sickle-sword: a sword-length blade whose last third hooks forward like a sickle's, its
    # edge on the inside of the curve, for dragging shields aside.
    "sickle-sword": dict(
        family="sabres", build="hafted", hands="one",
        parts=[dict(grip=(-0.18, 0.20, 0.033), m="Wrap", style="spiral", turns=7),
               dict(guard=0.205, span=0.10, height=0.016, depth=0.022, ends="knob", knob=0.8, m="DarkIron", role="fitting"),
               dict(lathe=[(-0.24, 0.0), (-0.235, 0.034), (-0.19, 0.04), (-0.17, 0.03)], m="DarkIron", role="fitting"),
               S([(0.0, 0.20), (0.0, 0.62), (-0.03, 0.98), (-0.13, 1.24), (-0.29, 1.36), (-0.42, 1.33)],
                 lambda s: 0.034 + 0.012 * s, lambda s: 0.022 * (1 - 0.4 * s), 0.016, sharp=(True, False), per=5)],
    ),
})


# --- knives and daggers -------------------------------------------------------------------------

def arc_path(cx, cz, r, a0, a1, n=8):
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * k / n)), cz + r * math.sin(math.radians(a0 + (a1 - a0) * k / n))) for k in range(n + 1)]


def handle(z0, z1, r=0.03, m="Wrap", style="spiral", **kw):
    return dict(grip=(z0, z1, r), m=m, style=style, **kw)


def comb(z0, z1, count, x0=0.03, reach=0.05, m="Steel"):
    """A swordbreaker's teeth: hooked tines along one edge, slots between them."""
    out = []
    for k in range(count):
        z = z0 + (z1 - z0) * k / max(1, count - 1)
        out.append(S([(x0 - 0.01, z), (x0 + reach * 0.6, z + 0.012), (x0 + reach, z + 0.035)], taper(0.011, 0.004), taper(0.011, 0.004), 0.010, m=m, sharp=(False, False), per=3))
    return out


SICKLE_PATH = [(0.0, 0.30), (-0.05, 0.47), (-0.02, 0.62), (0.08, 0.70), (0.20, 0.70), (0.30, 0.62)]

RECIPES.update({
    # A brass knife: a cheap single-edged knife cast in brass, wooden scales riveted on.
    "brass-knife": dict(
        family="knives", build="curved", hands="light", metal="Bronze", fittings="Bronze", grip_material="Wood",
        blade=dict(base=0.16, length=0.58, sweep=0.03, width=0.07, flare=0.1, clip=0.80, toff=0.5, thick=0.016, fuller=None),
        guard=dict(kind="bar", z=0.15, span=0.045, height=0.012, depth=0.024, ends="plain", centre=0.0),
        grip=dict(bottom=-0.10, top=0.145, radius=0.030, style="plain", oval=0.7),
        pommel=dict(kind="cap", radius=0.03, height=0.02, oval=0.7),
    ),
    # A dueling dagger, the main gauche: a long narrow blade, broad quillons curving toward it,
    # and a ring to catch an opponent's blade.
    "dueling-dagger": dict(
        family="knives", build="straight", hands="light", metal="Steel", fittings="Steel",
        blade=dict(base=0.225, tip=0.98, width=0.042, thick=0.019, section="diamond", taper=0.35, point=0.15, curve=0.9),
        guard=dict(kind="parry", z=0.215, span=0.20, height=0.012, depth=0.016, ends="knob", knob=1.2, droop=0.10, centre=0.4, ring=0.05),
        grip=dict(bottom=-0.08, top=0.205, radius=0.031, style="cord", turns=7),
        pommel=dict(kind="ball", radius=0.04, height=0.08),
    ),
    # A swordbreaker dagger: a heavy back cut into a comb of hooked teeth to catch a blade.
    "swordbreaker-dagger": dict(
        family="knives", build="straight", hands="light", metal="Steel", fittings="DarkIron",
        blade=dict(base=0.225, tip=0.92, width=0.05, thick=0.022, section="hex", flat=0.4, taper=0.2, point=0.25, curve=0.9, steps=11),
        guard=dict(kind="cross", z=0.215, span=0.14, height=0.016, depth=0.02, ends="knob", knob=0.9),
        grip=dict(bottom=-0.08, top=0.205, radius=0.032, turns=6),
        pommel=dict(kind="scent", height=0.11, radius=0.042),
        parts=comb(0.33, 0.72, 7, 0.035, 0.055),
    ),
    # A drow razor: a slim, wicked, black blade hooking at its point, spidery quillons.
    "drow-razor": dict(
        family="knives", build="curved", hands="light", metal="DarkIron", fittings="DarkIron",
        blade=dict(base=0.20, length=0.80, sweep=0.13, width=0.058, flare=0.25, clip=0.70, toff=0.6, step=0.25, thick=0.015),
        guard=dict(kind="cross", z=0.19, span=0.14, height=0.010, depth=0.012, ends="plain", droop=0.08, centre=0.6, taper=0.6),
        grip=dict(bottom=-0.10, top=0.18, radius=0.028, style="spiral", turns=7),
        pommel=dict(kind="spike", radius=0.016, length=0.08),
    ),
    # A waveblade: a kris — a wavy blade swelling at its asymmetric base, on an angled grip.
    "waveblade": dict(
        family="knives", build="straight", hands="light", metal="Steel", fittings="Gold", grip_material="Wood",
        blade=dict(base=0.20, tip=0.92, width=0.042, thick=0.015, section="lens", taper=0.3, point=0.15, curve=0.9, wave=(0.42, 3.5)),
        guard=dict(kind="none"),
        grip=dict(bottom=-0.11, top=0.20, radius=0.031, style="plain", bend=-0.05, oval=0.8),
        pommel=dict(kind="cap", radius=0.034, height=0.025),
        parts=[S([(-0.02, 0.18), (0.06, 0.22), (0.10, 0.26)], 0.022, 0.012, 0.014, m="Steel", sharp=(True, False), per=2)],
    ),
    # A kunai: a leaf-shaped blade on a cord-bound handle with a ring at its end; no guard.
    "kunai": dict(
        family="knives", build="straight", hands="light", metal="DarkIron", fittings="DarkIron", grip_material="Cloth",
        blade=dict(base=0.15, tip=0.60, width=0.040, thick=0.018, section="diamond", taper=0.0, point=0.62, curve=0.9, leaf=0.45),
        guard=dict(kind="none"),
        grip=dict(bottom=-0.10, top=0.15, radius=0.022, style="cord", turns=8),
        pommel=dict(kind="ring", radius=0.022, ring=0.04),
    ),
    # A sea knife: a broad, flat-bladed knife with an up-swept tip and a lanyard ring.
    "sea-knife": dict(
        family="knives", build="curved", hands="light", metal="Steel", fittings="Bronze", grip_material="Wood",
        blade=dict(base=0.16, length=0.62, sweep=-0.03, width=0.09, flare=0.2, clip=0.82, toff=0.65, thick=0.017, fuller=None),
        guard=dict(kind="bar", z=0.15, span=0.05, height=0.014, depth=0.026, ends="plain", centre=0.0),
        grip=dict(bottom=-0.11, top=0.145, radius=0.032, style="plain", oval=0.75),
        pommel=dict(kind="ring", radius=0.03, ring=0.026),
    ),
    # A war razor: a long straight razor, its square-ended blade turned out of a bone handle.
    "war-razor": dict(
        family="knives", build="curved", hands="light", metal="Steel", fittings="Steel", grip_material="Bone",
        blade=dict(base=0.16, length=0.55, sweep=0.0, width=0.075, flare=0.08, clip=0.95, toff=0.92, thick=0.012, fuller=None),
        guard=dict(kind="bar", z=0.15, span=0.03, height=0.02, depth=0.022, ends="plain", centre=0.0),
        grip=dict(bottom=-0.18, top=0.145, radius=0.026, style="plain", oval=0.55, bend=-0.06),
        pommel=dict(kind="none"),
    ),
    # A butterfly knife: a balisong — a slim blade between two handle halves that swing apart.
    "butterfly-knife": dict(
        family="knives", build="curved", hands="light", metal="Steel", fittings="Steel", grip_material="DarkIron",
        blade=dict(base=0.15, length=0.50, sweep=0.02, width=0.055, flare=0.0, clip=0.78, toff=0.45, thick=0.013, fuller=None),
        guard=dict(kind="none"),
        grip=dict(bottom=-0.16, top=0.15, radius=0.012, style="plain"),
        parts=[dict(prism=[(-0.17, 0.010, 0.017, sx * 0.024, 0.0), (0.15, 0.010, 0.017, sx * 0.024, 0.0)], m="Steel", role="grip") for sx in (-1, 1)]
              + [dict(prism=[(-0.20, 0.008, 0.012, -0.018, 0.0), (-0.165, 0.008, 0.012, -0.018, 0.0)], m="Steel", role="fitting")],
    ),
    # A switchblade: a slim spear-pointed blade out of a slab handle with a button.
    "switchblade-knife": dict(
        family="knives", build="curved", hands="light", metal="Steel", fittings="Steel", grip_material="Horn",
        blade=dict(base=0.16, length=0.50, sweep=0.0, width=0.05, flare=0.0, clip=0.70, toff=0.5, thick=0.013, fuller=None),
        guard=dict(kind="none"),
        grip=dict(bottom=-0.17, top=0.15, radius=0.02, style="plain"),
        parts=[dict(prism=[(-0.18, 0.034, 0.018), (-0.16, 0.036, 0.02), (0.12, 0.036, 0.02), (0.165, 0.03, 0.018)], m="Horn", role="grip", square=0.6),
               dict(prism=[(0.12, 0.037, 0.021), (0.165, 0.037, 0.021)], m="Steel", role="fitting"),
               dict(lathe=[(0, 0.012), (0.01, 0.011), (0.014, 0.0)], m="Steel", dir=(0, -1, 0), at=(0, -0.02, 0.08), role="fitting")],
    ),
    # A spring blade: a blade sprung out of a sleeve worn up the forearm.
    "spring-blade": dict(
        family="knives", build="hafted", hands="light",
        parts=[dict(prism=[(-0.24, 0.042, 0.034), (-0.22, 0.046, 0.038), (0.10, 0.046, 0.038), (0.12, 0.04, 0.03)], m="DarkIron", role="fitting", square=0.7),
               dict(prism=[(-0.16, 0.05, 0.042), (-0.13, 0.05, 0.042)], m="Wrap", role="grip", square=0.7),
               dict(prism=[(0.0, 0.05, 0.042), (0.03, 0.05, 0.042)], m="Wrap", role="grip", square=0.7),
               dict(blade=dict(base=0.10, tip=0.55, width=0.034, thick=0.012, section="diamond", taper=0.2, point=0.3, curve=0.9), m="Steel")],
    ),
    # A kerambit: a small claw of a blade curving forward from a short grip with a finger ring.
    "kerambit": dict(
        family="knives", build="hafted", hands="light",
        parts=[handle(-0.04, 0.16, 0.026, "Wrap", "cord", turns=5),
               dict(torus=(0.0, 0.0, -0.085), major=0.045, minor=0.010, m="Steel", n=16),
               S([(0.0, 0.15), (-0.02, 0.28), (-0.08, 0.40), (-0.18, 0.46)], taper(0.034, 0.004), taper(0.018, 0.003), 0.012, sharp=(True, False), per=4)],
    ),
    # A machete: a long, broad, straight blade widening to a blunt-cornered tip, riveted scales.
    "machete": dict(
        family="knives", build="curved", hands="light", metal="Steel", fittings="DarkIron", grip_material="Wood",
        blade=dict(base=0.18, length=1.0, sweep=0.0, width=0.095, flare=0.45, clip=0.90, toff=0.75, thick=0.013, fuller=None),
        guard=dict(kind="none"),
        grip=dict(bottom=-0.16, top=0.18, radius=0.031, style="plain", oval=0.65, bend=-0.02),
        pommel=dict(kind="beak", radius=0.032, height=0.05, beak=-0.02),
    ),
    # A kukri: a heavy blade bent forward over its edge, broad at the belly, a ringed grip and
    # a flared butt.
    "kukri": dict(
        family="knives", build="curved", hands="light", metal="Steel", fittings="Bronze", grip_material="Horn",
        blade=dict(base=0.17, length=0.72, sweep=-0.13, width=0.075, flare=0.65, belly=0.2, clip=0.84, toff=0.45, thick=0.024, fuller=None),
        guard=dict(kind="bar", z=0.16, span=0.04, height=0.012, depth=0.03, ends="plain", centre=0.0),
        grip=dict(bottom=-0.14, top=0.155, radius=0.032, style="cord", turns=3, profile="barrel"),
        pommel=dict(kind="cap", radius=0.046, height=0.03),
    ),
    # A sica: a short dagger sharply curved forward, a small guard, a bone grip.
    "sica": dict(
        family="knives", build="curved", hands="light", metal="Steel", fittings="Bronze", grip_material="Bone",
        blade=dict(base=0.18, length=0.62, sweep=-0.17, width=0.058, flare=0.2, clip=0.78, toff=0.4, thick=0.016),
        guard=dict(kind="cross", z=0.17, span=0.07, height=0.014, depth=0.02, ends="knob", knob=0.8),
        grip=dict(bottom=-0.10, top=0.16, radius=0.03, style="ridged"),
        pommel=dict(kind="knob", radius=0.036, height=0.06),
    ),
    # A dogslicer: a goblin's crude, back-curved short blade of rough iron with notches knocked
    # out of its back, on a rag-bound grip.
    "dogslicer": dict(
        family="knives", build="curved", hands="light", metal="DarkIron", fittings="DarkIron", grip_material="Cloth",
        blade=dict(base=0.15, length=0.66, sweep=0.12, width=0.085, flare=0.45, clip=0.74, toff=0.35, step=0.18, thick=0.016, fuller=None),
        guard=dict(kind="none"),
        grip=dict(bottom=-0.13, top=0.15, radius=0.032, style="cord", turns=6),
        pommel=dict(kind="none"),
        parts=[S([(0.05, 0.30), (0.075, 0.33), (0.055, 0.36)], 0.012, 0.012, 0.012, m="DarkIron", sharp=(False, False), per=1),
               S([(0.07, 0.45), (0.10, 0.48), (0.08, 0.51)], 0.012, 0.012, 0.012, m="DarkIron", sharp=(False, False), per=1)],
    ),
    # A siangham: a thin spike for a monk's thrust, a small guard, a short grip.
    "siangham": dict(
        family="knives", build="straight", hands="light", metal="Steel", fittings="Bronze",
        blade=dict(base=0.18, tip=0.92, width=0.018, thick=0.018, section="square", taper=0.4, point=0.3, curve=0.8),
        guard=dict(kind="cross", z=0.17, span=0.05, height=0.012, depth=0.016, ends="knob", knob=1.0),
        grip=dict(bottom=-0.10, top=0.16, radius=0.026, style="cord", turns=6),
        pommel=dict(kind="knob", radius=0.03, height=0.05),
    ),
    # A quadrens: a stiletto whose blade is four-edged, a cross in section.
    "quadrens": dict(
        family="knives", build="straight", hands="light", metal="Steel", fittings="DarkIron",
        blade=dict(base=0.20, tip=0.85, width=0.03, thick=0.008, section="diamond", taper=0.0, point=1.0, curve=0.9, steps=10),
        guard=dict(kind="cross", z=0.19, span=0.07, height=0.016, depth=0.03, ends="plain", centre=0.3),
        grip=dict(bottom=-0.09, top=0.18, radius=0.03, turns=6),
        pommel=dict(kind="scent", height=0.09, radius=0.036),
        parts=[dict(blade=dict(base=0.20, tip=0.85, width=0.03, thick=0.008, section="diamond", taper=0.0, point=1.0, curve=0.9, steps=10), m="Steel", rot=(0, 0, 90))],
    ),
    # A sanpkhang: a broad leaf blade on a short haft, a monk's knife-spear.
    "sanpkhang": dict(
        family="knives", build="hafted", hands="light", haft=(-0.30, 0.34, 0.03),
        wraps=[(-0.22, 0.05, 0.034)],
        head=[dict(kind="spear", z=0.36, length=0.42, width=0.08, shape="broad", hr=0.03, socket=0.12)],
        butt=dict(z=-0.30, kind="cap", r=0.03),
    ),
    # A bichhwa, the scorpion: an S-waved double-edged blade and a loop of iron round the fingers.
    "bichhwa": dict(
        family="knives", build="hafted", hands="light",
        parts=[handle(-0.10, 0.14, 0.028, "Wrap", "spiral", turns=5),
               dict(tube=[(0.0, 0, 0.145), (-0.08, 0, 0.13), (-0.12, 0, 0.03), (-0.10, 0, -0.08), (-0.03, 0, -0.13), (0.0, 0, -0.11)], per=3, r=0.011, m="DarkIron", role="fitting"),
               S([(0.0, 0.14), (0.05, 0.28), (-0.03, 0.44), (0.03, 0.60), (0.0, 0.72)], taper(0.032, 0.006), taper(0.032, 0.006), 0.012, m="DarkIron", per=4)],
    ),
    # Deer horn knives: two crescent blades crossed, four points out, a grip in the middle of one.
    "deer-horn-knife": dict(
        family="knives", build="hafted", hands="light",
        parts=[S(arc_path(0.32, 0.0, 0.32, 235, 125), lambda s: 0.02 + 0.03 * math.sin(math.pi * s), 0.014, 0.012, sharp=(True, False), per=3),
               S(arc_path(-0.24, 0.0, 0.32, -55, 55), 0.014, lambda s: 0.02 + 0.03 * math.sin(math.pi * s), 0.012, sharp=(False, True), per=3),
               handle(-0.085, 0.085, 0.026, "Wrap", "cord", turns=5)],
        fx=((0.14, 0, -0.26), (0.14, 0, 0.26)),
    ),
    # A lungchuan tamo: a crescent-moon blade sharp on its outer edge, gripped across its middle.
    "lungchuan-tamo": dict(
        family="knives", build="hafted", hands="light",
        parts=[handle(-0.09, 0.09, 0.026, "Wrap", "cord", turns=5),
               S(arc_path(0.30, 0.0, 0.36, 128, 232, 10), lambda s: 0.012 + 0.07 * math.sin(math.pi * s) ** 0.8, 0.012, 0.012, sharp=(True, False), per=3, at=(-0.07, 0.0)),
               dict(prism=[(-0.10, 0.04, 0.012, -0.03), (0.10, 0.04, 0.012, -0.03)], m="Steel", role="fitting")],
    ),
    # A wooden stake: a length of hardwood whittled to a point.
    "wooden-stake": dict(
        family="knives", build="hafted", hands="light",
        parts=[dict(lathe=[(-0.25, 0.0), (-0.24, 0.035), (0.20, 0.038), (0.42, 0.02), (0.55, 0.0)], m="Wood", n=7, faceted=True, role="edge")],
    ),
    # An iron brush: a calligrapher's brush whose shaft is iron and whose tip hides a spike.
    "iron-brush": dict(
        family="knives", build="hafted", hands="light",
        parts=[dict(lathe=[(-0.22, 0.0), (-0.215, 0.02), (0.20, 0.018), (0.22, 0.024), (0.25, 0.024)], m="DarkIron", n=10, role="grip"),
               dict(lathe=[(0.24, 0.026), (0.30, 0.032), (0.38, 0.022), (0.44, 0.0)], m="Cloth", n=10, role="fitting"),
               dict(spike=((0, 0, 0.40), (0, 0, 0.50)), r=0.008, m="Steel")],
    ),
    # A fighting fan: a fan whose outer ribs are steel and sharpened, spread open.
    "fighting-fan": dict(
        family="knives", build="hafted", hands="light",
        parts=[dict(plate=[(0.0, 0.03)] + arc_path(0.0, 0.0, 0.46, 35, 145, 12), centre=(0.0, 0.25), thick=0.006, closed=True, m="Cloth", role="fitting")]
              + [dict(tube=[(0, 0, -0.10), (0.47 * math.cos(math.radians(a)), 0, 0.47 * math.sin(math.radians(a)))], r=0.006 if 40 < a < 140 else 0.011, m="Steel", n=5, role="edge" if not 40 < a < 140 else "fitting")
                 for a in range(35, 146, 11)]
              + [dict(lathe=[(-0.12, 0.0), (-0.11, 0.022), (-0.06, 0.022), (-0.05, 0.0)], m="Steel", role="fitting")],
    ),
    # An elven leafblade: a leaf-shaped light blade, a guard like two leaves, a gilt pommel.
    "elven-leafblade": dict(
        family="knives", build="straight", hands="light", metal="Steel", fittings="Gold", grip_material="Wood",
        blade=dict(base=0.20, tip=0.98, width=0.046, thick=0.016, section="lens", taper=0.0, point=0.35, curve=0.8, leaf=0.4),
        guard=dict(kind="cross", z=0.19, span=0.11, height=0.018, depth=0.02, ends="flare", knob=1.4, droop=0.07, centre=0.3, taper=0.1),
        grip=dict(bottom=-0.10, top=0.18, radius=0.03, style="spiral", turns=6),
        pommel=dict(kind="knob", radius=0.032, height=0.06),
    ),
    # A sickle: a crescent blade, sharp on its inside, on a wooden handle.
    "sickle": dict(
        family="knives", build="hafted", hands="light", haft=(-0.18, 0.32, 0.03, 0.026),
        parts=[dict(lathe=[(0.26, 0.03), (0.30, 0.034), (0.33, 0.026)], m="DarkIron", role="fitting"),
               S(SICKLE_PATH, lambda s: 0.010 + 0.004 * (1 - s), lambda s: 0.026 * math.sin(math.pi * min(1, 0.2 + s)) + 0.004, 0.010, sharp=(False, True), per=4)],
    ),
    # A spider-leg sickle: a sickle whose blade is jointed and angular like a spider's leg,
    # barbed at each joint.
    "spider-leg-sickle": dict(
        family="knives", build="hafted", hands="light", haft=(-0.18, 0.32, 0.03, 0.026),
        parts=[dict(lathe=[(0.26, 0.03), (0.30, 0.034), (0.33, 0.026)], m="DarkIron", role="fitting"),
               S([(0.0, 0.30), (-0.06, 0.50), (0.04, 0.68), (0.20, 0.73), (0.33, 0.64)], lambda s: 0.012, lambda s: 0.024 * (1 - s) + 0.004, 0.010, m="DarkIron", sharp=(False, True), per=1),
               S([(-0.05, 0.50), (-0.12, 0.52)], 0.008, 0.008, 0.008, m="DarkIron", per=1),
               S([(0.04, 0.68), (0.02, 0.76)], 0.008, 0.008, 0.008, m="DarkIron", per=1),
               S([(0.20, 0.73), (0.22, 0.81)], 0.008, 0.008, 0.008, m="DarkIron", per=1)],
    ),
    # A kama: a short blade set at right angles to a wooden handle, sharp on its underside.
    "kama": dict(
        family="knives", build="hafted", hands="light", haft=(-0.22, 0.40, 0.03, 0.028),
        butt=dict(z=-0.22, kind="knob", r=0.03, m="Wood"),
        parts=[dict(prism=[(0.33, 0.032, 0.034), (0.41, 0.032, 0.034)], m="DarkIron", role="fitting"),
               S([(0.0, 0.38), (-0.16, 0.42), (-0.30, 0.41), (-0.38, 0.37)], lambda s: 0.03 * (1 - 0.5 * s) + 0.004, 0.012, 0.010, sharp=(True, False), per=4)],
    ),
    # A jutte: an iron truncheon with a single hooked prong beside the grip, a ring at the butt.
    "jutte": dict(
        family="knives", build="hafted", hands="light",
        parts=[handle(-0.12, 0.10, 0.03, "Wrap", "cord", turns=6),
               dict(lathe=[(0.09, 0.028), (0.12, 0.026), (0.62, 0.018), (0.66, 0.0)], m="DarkIron", n=8, faceted=True, role="edge"),
               dict(tube=[(0.0, 0, 0.12), (-0.05, 0, 0.13), (-0.06, 0, 0.20), (-0.05, 0, 0.30)], per=3, r=0.011, m="DarkIron", role="edge"),
               dict(torus=(0.0, 0.0, -0.16), major=0.035, minor=0.008, m="DarkIron", n=14)],
    ),
    # A sai: a long round prong and two side prongs curving up from the guard; a cord grip.
    "sai": dict(
        family="knives", build="hafted", hands="light",
        parts=[handle(-0.12, 0.12, 0.028, "Wrap", "cord", turns=6),
               dict(lathe=[(-0.17, 0.0), (-0.16, 0.034), (-0.12, 0.03)], m="DarkIron", role="fitting"),
               dict(lathe=[(0.11, 0.02), (0.14, 0.024), (0.66, 0.014), (0.72, 0.0)], m="DarkIron", n=8, faceted=True, role="edge")]
              + [dict(tube=[(0.0, 0, 0.13), (sx * 0.07, 0, 0.13), (sx * 0.12, 0, 0.18), (sx * 0.11, 0, 0.30)], per=3, r=lambda u: 0.012 * (1 - 0.5 * u), m="DarkIron", role="edge") for sx in (-1, 1)],
    ),
})


# --- axes --------------------------------------------------------------------------------------

FRANCISCA = [(0.0, 0.05), (0.08, 0.07), (0.16, 0.13), (0.23, 0.23), (0.28, 0.14), (0.29, 0.02), (0.25, -0.05), (0.14, -0.05), (0.0, -0.05)]
TONGI = [(0.0, 0.05), (0.10, 0.06), (0.22, 0.12), (0.30, 0.30), (0.33, 0.20), (0.35, 0.02), (0.33, -0.16), (0.30, -0.30), (0.22, -0.12), (0.10, -0.06), (0.0, -0.05)]
HOOKED = [(0.0, 0.07), (0.12, 0.09), (0.24, 0.17), (0.32, 0.18), (0.35, 0.06), (0.35, -0.06), (0.31, -0.18), (0.24, -0.22), (0.12, -0.12), (0.0, -0.07)]

RECIPES.update({
    # A handaxe: a short haft and a compact wedge of a head, flaring to its edge.
    "handaxe": dict(
        family="axes", build="hafted", hands="light", haft=(-0.20, 0.95, 0.034, 0.030), wraps=[(-0.17, 0.18, 0.038)],
        butt=dict(z=-0.20, kind="knob", r=0.034, m="Wood"),
        head=[dict(kind="axe", z=0.80, shape="hatchet", size=1.0, thick=0.034, socket=(0.09, 0.044, 0.038), hr=0.032)],
    ),
    # A throwing axe: a slim haft and a francisca's head, its edge cocked upward.
    "throwing-axe": dict(
        family="axes", build="hafted", hands="light", haft=(-0.16, 0.86, 0.028, 0.026), wraps=[(-0.13, 0.10, 0.032)],
        head=[dict(kind="axe", z=0.74, outline=FRANCISCA, size=1.0, thick=0.03, socket=(0.07, 0.036, 0.032), hr=0.027, rivets=False)],
    ),
    # A battleaxe: an arm-long haft, a bearded head with iron langets down the haft.
    "battleaxe": dict(
        family="axes", build="hafted", hands="one", haft=(-0.30, 1.75, 0.040, 0.036), wraps=[(-0.26, 0.22, 0.044)],
        butt=dict(z=-0.30, kind="cap", r=0.040),
        head=[dict(kind="axe", z=1.52, shape="bearded", size=1.18, thick=0.045, socket=(0.12, 0.054, 0.048), langets=0.22, hr=0.038)],
    ),
    # A dwarven waraxe: a heavy, geometric, deep-bearded head with a spike behind, iron-bound haft.
    "dwarven-waraxe": dict(
        family="axes", build="hafted", hands="one", haft=(-0.30, 1.72, 0.046, 0.042), wraps=[(-0.26, 0.24, 0.05)],
        butt=dict(z=-0.30, kind="flare", r=0.046),
        head=[dict(kind="axe", z=1.46, shape="dwarven", size=1.20, thick=0.06, socket=(0.15, 0.062, 0.056), back="spike", back_len=0.18, langets=0.28, hr=0.044)],
        parts=[dict(lathe=[(z - 0.03, 0.05), (z, 0.054), (z + 0.03, 0.05)], m="DarkIron", role="fitting", n=8) for z in (0.40, 0.70, 1.00)],
    ),
    # A dwarven double waraxe: two dwarven blades back to back and a spike on top.
    "dwarven-double-waraxe": dict(
        family="axes", build="hafted", hands="one", haft=(-0.30, 1.72, 0.046, 0.042), wraps=[(-0.26, 0.24, 0.05)],
        butt=dict(z=-0.30, kind="flare", r=0.046),
        head=[dict(kind="axe", z=1.46, shape="dwarven", size=1.05, thick=0.058, socket=(0.15, 0.062, 0.056), back="blade", top=0.16, hr=0.044)],
        parts=[dict(lathe=[(z - 0.03, 0.05), (z, 0.054), (z + 0.03, 0.05)], m="DarkIron", role="fitting", n=8) for z in (0.40, 0.70, 1.00)],
    ),
    # A boarding axe: a hatchet with a hooked spike behind, for hauling on rigging and rails.
    "boarding-axe": dict(
        family="axes", build="hafted", hands="light", haft=(-0.20, 1.05, 0.034, 0.03), wraps=[(-0.17, 0.16, 0.038)],
        butt=dict(z=-0.20, kind="cap", r=0.034),
        head=[dict(kind="axe", z=0.90, shape="hatchet", size=1.05, thick=0.034, socket=(0.10, 0.045, 0.04), back="spike", back_len=0.20, hr=0.032)],
    ),
    # A hooked axe: a broad head with a hook curving down off the back of the socket.
    "hooked-axe": dict(
        family="axes", build="hafted", hands="one", haft=(-0.30, 1.75, 0.040, 0.036), wraps=[(-0.26, 0.22, 0.044)],
        butt=dict(z=-0.30, kind="cap", r=0.040),
        head=[dict(kind="axe", z=1.50, outline=HOOKED, size=1.12, thick=0.045, socket=(0.12, 0.054, 0.048), hr=0.038),
              dict(kind="hook", z=1.48, reach=0.22, rise=0.22, side=1, width=0.04, down=True)],
    ),
    # A gandasa: a long-hafted chopper with a broad cleaver of a blade and a spike on top.
    "gandasa": dict(
        family="axes", build="hafted", hands="one", haft=(-0.40, 1.95, 0.036, 0.033), wraps=[(-0.36, 0.20, 0.04)],
        butt=dict(z=-0.40, kind="spike", r=0.036),
        head=[dict(kind="axe", z=1.66, shape="gandasa", size=1.05, thick=0.035, socket=(0.13, 0.05, 0.044), top=0.20, hr=0.034)],
    ),
    # A tongi: a narrow, deep crescent on a long neck, both horns drawn out to points.
    "tongi": dict(
        family="axes", build="hafted", hands="one", haft=(-0.30, 1.62, 0.034, 0.031), wraps=[(-0.26, 0.18, 0.038)],
        butt=dict(z=-0.30, kind="cap", r=0.034),
        head=[dict(kind="axe", z=1.42, outline=TONGI, size=1.05, thick=0.03, socket=(0.12, 0.045, 0.04), root=0.10, hr=0.032),
              dict(prism=[(-0.03, 0.022, 0.016), (0.08, 0.018, 0.014)], m="Steel", rot=(0, 90, 0), at=(-0.04, 0, 1.42), role="head")],
    ),
    # A butchering axe: a massive cleaver of a head on a long two-handed haft.
    "butchering-axe": dict(
        family="axes", build="hafted", hands="two", haft=(-0.70, 2.70, 0.056, 0.05), wraps=[(-0.64, 0.30, 0.06)],
        butt=dict(z=-0.70, kind="cap", r=0.056),
        head=[dict(kind="axe", z=2.30, shape="cleaver", size=1.45, thick=0.06, socket=(0.20, 0.07, 0.062), hr=0.054)],
    ),
    # A hurlbat: an axe all of iron, spiked every way it can land.
    "hurlbat": dict(
        family="axes", build="hafted", hands="light", haft=(-0.12, 0.66, 0.024, 0.022), haft_material="DarkIron", wraps=[(-0.10, 0.16, 0.03)],
        head=[dict(kind="axe", z=0.55, shape="hatchet", size=0.85, thick=0.03, socket=(0.07, 0.035, 0.03), back="spike", back_len=0.16, top=0.16, hr=0.024, m="DarkIron")],
        parts=[dict(spike=((0, 0, -0.10), (0, 0, -0.26)), r=0.022, m="DarkIron"),
               dict(spike=((0.02, 0, 0.32), (0.13, 0, 0.42)), r=0.016, m="DarkIron")],
    ),
    # A dwarven maulaxe: an axe blade with a hammer behind it, short enough to throw.
    "dwarven-maulaxe": dict(
        family="axes", build="hafted", hands="light", haft=(-0.20, 1.05, 0.036, 0.034), wraps=[(-0.17, 0.18, 0.04)],
        butt=dict(z=-0.20, kind="flare", r=0.036),
        head=[dict(kind="axe", z=0.86, shape="dwarven", size=0.72, thick=0.045, socket=(0.11, 0.05, 0.046), back="hammer", back_len=0.15, hr=0.034)],
    ),
})


# --- hammers, maces, clubs and picks -----------------------------------------------------------

def studs(z0, z1, rings, per, radius, size=0.016, m="DarkIron", twist=0.5):
    """Iron studs set round a club in rings: a tetsubo's, a greatclub's."""
    out = []
    for k in range(rings):
        z = z0 + (z1 - z0) * k / max(1, rings - 1)
        r = radius(z) if callable(radius) else radius
        for j in range(per):
            a = 2 * math.pi * (j + twist * (k % 2)) / per
            d = (math.cos(a), math.sin(a), 0.0)
            out.append(dict(lathe=[(0.0, size), (size * 0.5, size * 0.8), (size * 0.9, 0.0)], m=m, n=6, role="head",
                            dir=d, at=(r * 0.92 * d[0], r * 0.92 * d[1], z)))
    return out


def club(profile, m="Wood", n=10, faceted=False):
    return [dict(lathe=profile, m=m, n=n, faceted=faceted, role="head")]


def teeth(path_pts, count, out_dir, size=0.05, m="Bone"):
    """Teeth set along an edge, pointing out: a terbutje's sharks' teeth."""
    out = []
    for k in range(count):
        x, z = path_pts(k / max(1, count - 1))
        out.append(dict(spike=((x, 0.0, z), (x + out_dir * size, 0.0, z + size * 0.25)), r=size * 0.32, m=m, n=4, waist=0.7))
    return out


def paddle(z0, z1, w, notch=False):
    """The outline of a flat club, a mere's or a terbutje's: widening from the grip to a rounded end."""
    pts = []
    for k in range(9):
        t = k / 8
        pts.append((w * (0.35 + 0.65 * math.sin(math.pi * 0.5 * t) ** 0.8), z0 + (z1 - z0) * t))
    tip = [(w * math.cos(math.radians(a)), z1 + w * 0.9 * math.sin(math.radians(a))) for a in range(20, 161, 20)]
    left = [(-x, z) for x, z in reversed(pts)]
    if notch:
        left = [(x * (0.55 if 0.55 < (z - z0) / (z1 - z0) < 0.72 else 1.0), z) for x, z in left]
    return pts + tip + left


RECIPES.update({
    # A club: a cudgel of hardwood, thickening to a knotted head.
    "club": dict(
        family="hammers", build="hafted", hands="one",
        head=club([(-0.26, 0.0), (-0.25, 0.036), (0.10, 0.04), (0.55, 0.055), (0.95, 0.08), (1.18, 0.088), (1.30, 0.07), (1.34, 0.0)], n=9, faceted=True)
             + [dict(lathe=[(-0.05, 0.0), (0.0, 0.03), (0.04, 0.0)], m="Wood", n=6, dir=d, at=(0.07 * d[0], 0.07 * d[1], z)) for z, d in ((0.80, (1, 0.2, 0)), (1.05, (-0.6, 0.8, 0)), (1.18, (0.3, -1, 0)))],
        wraps=[(-0.22, 0.18, 0.045, "Wrap", "cord")],
    ),
    # A greatclub: a long two-handed club with a heavy head bound and studded with iron.
    "greatclub": dict(
        family="hammers", build="hafted", hands="two",
        head=club([(-0.55, 0.0), (-0.54, 0.044), (0.0, 0.048), (0.8, 0.07), (1.6, 0.115), (2.05, 0.135), (2.22, 0.11), (2.28, 0.0)], n=10, faceted=True)
             + [dict(lathe=[(z - 0.03, r), (z, r * 1.06), (z + 0.03, r)], m="DarkIron", role="head", n=10) for z, r in ((1.30, 0.10), (2.05, 0.138))]
             + studs(1.45, 2.0, 4, 6, lambda z: 0.10 + 0.035 * (z - 1.45) / 0.55, 0.02),
        wraps=[(-0.50, 0.30, 0.054, "Wrap", "cord")],
    ),
    # A heavy mace: a long haft and a big head of eight flanges with a pointed cap.
    "heavy-mace": dict(
        family="hammers", build="hafted", hands="one", haft=(-0.36, 1.25, 0.042, 0.038), wraps=[(-0.30, 0.22, 0.048)],
        butt=dict(z=-0.36, kind="knob", r=0.042),
        head=[dict(kind="flanged", z0=1.18, z1=1.62, r=0.20, n=8, core=0.10)] + [dict(kind="collar", z=1.16, r=0.05, h=0.10, m="DarkIron")],
    ),
    # A morningstar: a haft with a spiked iron ball on a collar.
    "morningstar": dict(
        family="hammers", build="hafted", hands="one", haft=(-0.30, 1.30, 0.042, 0.04), wraps=[(-0.26, 0.20, 0.048)],
        butt=dict(z=-0.30, kind="cap", r=0.042),
        head=[dict(kind="collar", z=1.30, r=0.05, h=0.12, m="DarkIron"),
              dict(ball=(0, 0, 1.44), r=0.12, spikes=14, length=0.6, m="DarkIron", role="head")],
    ),
    # A light hammer: a short haft and a small hammer with a short spike behind.
    "light-hammer": dict(
        family="hammers", build="hafted", hands="light", haft=(-0.18, 0.95, 0.03, 0.028), wraps=[(-0.16, 0.16, 0.034)],
        butt=dict(z=-0.18, kind="knob", r=0.03, m="Wood"),
        head=[dict(kind="hammer", z=0.84, length=0.12, face=0.04, back="claw", back_len=0.12, socket=(0.06, 0.042, 0.038))],
    ),
    # A warhammer: a flat-faced hammer with a curved spike behind, a spike on top, langets down.
    "warhammer": dict(
        family="hammers", build="hafted", hands="one", haft=(-0.30, 1.50, 0.038, 0.035), wraps=[(-0.26, 0.20, 0.042)],
        butt=dict(z=-0.30, kind="cap", r=0.038),
        head=[dict(kind="hammer", z=1.34, length=0.17, face=0.058, back="spike", back_len=0.26, top=0.15, socket=(0.085, 0.06, 0.055))],
        parts=[dict(prism=[(1.05, 0.010, 0.004, 0, sy * 0.039), (1.30, 0.012, 0.004, 0, sy * 0.039)], m="DarkIron", role="fitting") for sy in (1, -1)],
    ),
    # An earth breaker: a two-handed maul with a great double-faced block of a head.
    "earth-breaker": dict(
        family="hammers", build="hafted", hands="two", haft=(-0.62, 2.35, 0.05, 0.046), wraps=[(-0.56, 0.30, 0.056)],
        butt=dict(z=-0.62, kind="cap", r=0.05),
        head=[dict(kind="hammer", z=2.14, length=0.30, face=0.13, back="face", socket=(0.18, 0.135, 0.13), m="DarkIron")],
    ),
    # A dwarven ram hammer: a heavy hammer whose head is a ram's, horns curling round its sides.
    "dwarven-ram-hammer": dict(
        family="hammers", build="hafted", hands="one", haft=(-0.30, 1.42, 0.042, 0.04), wraps=[(-0.26, 0.22, 0.046)],
        butt=dict(z=-0.30, kind="flare", r=0.042),
        head=[dict(kind="hammer", z=1.28, length=0.21, face=0.08, back="face", socket=(0.12, 0.09, 0.085), round_face=True)]
             + [dict(tube=[(sx * 0.03, sy * 0.08, 1.37), (sx * 0.11, sy * 0.12, 1.46), (sx * 0.19, sy * 0.11, 1.38), (sx * 0.18, sy * 0.095, 1.24), (sx * 0.11, sy * 0.10, 1.22)],
                     per=3, r=lambda u: 0.034 * (1 - 0.6 * u), m="Horn", role="head") for sx in (-1, 1) for sy in (1, -1)],
    ),
    # A dwarven sphinx hammer: a two-handed hammer with a carved head swept back like wings.
    "dwarven-sphinx-hammer": dict(
        family="hammers", build="hafted", hands="two", haft=(-0.50, 1.95, 0.044, 0.04), wraps=[(-0.45, 0.25, 0.05)],
        butt=dict(z=-0.50, kind="flare", r=0.044),
        head=[dict(kind="hammer", z=1.78, length=0.22, face=0.095, back=None, socket=(0.13, 0.095, 0.085))]
             + [dict(plate=[(0.08, 1.89), (0.19, 1.96), (0.36, 2.04), (0.33, 1.86), (0.24, 1.73), (0.08, 1.68)], centre=(0.08, 1.79), thick=0.035, m="Steel", cheek="DarkIron", role="head", y=sy * 0.05)
                for sy in (1, -1)],
    ),
    # A gnome piston maul: a maul whose head is a cylinder driving a piston out of its face,
    # with a pressure tank and brass pipes.
    "gnome-piston-maul": dict(
        family="hammers", build="hafted", hands="two", haft=(-0.55, 1.95, 0.045, 0.042), wraps=[(-0.50, 0.25, 0.05)],
        butt=dict(z=-0.55, kind="cap", r=0.045),
        head=[dict(lathe=[(0.0, 0.0), (0.005, 0.12), (0.40, 0.12), (0.41, 0.08)], m="DarkIron", n=12, role="head", rot=(0, -90, 0), at=(0.16, 0, 1.80)),
              dict(lathe=[(0.0, 0.065), (0.17, 0.065), (0.18, 0.10), (0.25, 0.10), (0.255, 0.0)], m="Steel", n=12, role="head", rot=(0, -90, 0), at=(-0.24, 0, 1.80)),
              dict(lathe=[(-0.08, 0.0), (-0.07, 0.065), (0.07, 0.065), (0.08, 0.0)], m="Bronze", n=10, role="fitting", at=(0.06, 0, 1.96)),
              dict(tube=[(0.06, 0, 1.96), (0.16, 0, 2.01), (0.21, 0, 1.90)], per=3, r=0.016, m="Bronze", role="fitting")],
    ),
    # A tetsubo: a long, eight-sided club of oak studded with iron rings of rivets.
    "tetsubo": dict(
        family="hammers", build="hafted", hands="two",
        head=club([(-0.62, 0.0), (-0.61, 0.045), (0.25, 0.05), (1.0, 0.075), (2.2, 0.10), (2.42, 0.09), (2.46, 0.0)], m="Wood", n=8, faceted=True)
             + studs(0.70, 2.30, 8, 8, lambda z: 0.065 + 0.035 * (z - 0.7) / 1.6, 0.017),
        wraps=[(-0.58, 0.30, 0.053, "Cloth", "diamond")],
        butt=dict(z=-0.62, kind="cap", r=0.048),
    ),
    # A knobkerrie: a thin throwing stick with a heavy round knob at its end.
    "knobkerrie": dict(
        family="hammers", build="hafted", hands="one",
        head=club([(-0.30, 0.0), (-0.29, 0.03), (0.6, 0.026), (1.12, 0.03), (1.16, 0.06), (1.24, 0.10), (1.34, 0.105), (1.44, 0.08), (1.50, 0.0)], n=12),
    ),
    # A mere: a short flat club of polished greenstone, a spatula with a lanyard at its butt.
    "mere-club": dict(
        family="hammers", build="hafted", hands="one",
        parts=[dict(plate=paddle(0.12, 0.70, 0.095), centre=(0.0, 0.42), thick=0.03, closed=True, m="Stone", role="head"),
               dict(lathe=[(-0.20, 0.03), (-0.17, 0.044), (-0.14, 0.03), (0.15, 0.036)], m="Stone", n=10, role="grip"),
               dict(torus=(0.0, 0.0, -0.25), major=0.04, minor=0.008, m="Cloth", plane="YZ", n=12)],
    ),
    # A wahaika: a short flat wooden club, a notch cut in its back edge, a carved butt.
    "wahaika": dict(
        family="hammers", build="hafted", hands="one",
        parts=[dict(plate=paddle(0.10, 0.75, 0.085, notch=True), centre=(0.0, 0.42), thick=0.028, closed=True, m="Wood", role="head"),
               dict(lathe=[(-0.20, 0.0), (-0.19, 0.04), (-0.14, 0.046), (-0.10, 0.03), (0.14, 0.034)], m="Wood", n=10, role="grip")],
    ),
    # A battle aspergillum: a mace whose head is a pierced iron ball full of holy water.
    "battle-aspergillum": dict(
        family="hammers", build="hafted", hands="light", haft=(-0.25, 0.80, 0.032, 0.03), wraps=[(-0.22, 0.14, 0.036)],
        butt=dict(z=-0.25, kind="knob", r=0.032, m="Bronze"),
        head=[dict(kind="collar", z=0.80, r=0.04, h=0.08, m="Bronze"),
              dict(lathe=[(0.80, 0.0), (0.81, 0.06), (0.86, 0.105), (0.93, 0.115), (1.00, 0.105), (1.05, 0.06), (1.07, 0.02), (1.12, 0.0)], m="Bronze", n=14, role="head")]
             + studs(0.88, 0.98, 2, 8, 0.115, 0.016, "Steel"),
    ),
    # A baston: a plain rattan fighting stick, scorched in bands.
    "baston-fighting-stick": dict(
        family="hammers", build="hafted", hands="one",
        head=club([(-0.36, 0.0), (-0.355, 0.026), (1.30, 0.024), (1.31, 0.0)], n=8),
        parts=[dict(lathe=[(z - 0.015, 0.0265), (z, 0.028), (z + 0.015, 0.0265)], m="Wrap", n=8, role="fitting") for z in (0.30, 0.65, 1.0)],
    ),
    # A sap: a leather bag of sand on a short strap, for knocking people out.
    "sap": dict(
        family="hammers", build="hafted", hands="light",
        parts=[dict(lathe=[(0.22, 0.03), (0.30, 0.055), (0.42, 0.065), (0.52, 0.055), (0.58, 0.0)], m="Wrap", n=12, role="head"),
               dict(lathe=[(0.20, 0.032), (0.22, 0.036), (0.24, 0.03)], m="Cloth", n=8, role="fitting"),
               handle(-0.14, 0.21, 0.026, "Wrap", "plain"),
               dict(torus=(0.0, 0.0, -0.19), major=0.05, minor=0.008, m="Wrap", plane="YZ", n=12)],
    ),
    # A combat scabbard: a sword's scabbard, iron-bound at throat and tip, swung like a club.
    "combat-scabbard": dict(
        family="hammers", build="hafted", hands="one",
        parts=[dict(prism=[(0.0, 0.07, 0.03), (1.30, 0.055, 0.026), (1.40, 0.03, 0.022), (1.43, 0.0, 0.0)], m="Wrap", role="head", square=0.7),
               dict(prism=[(-0.02, 0.078, 0.036), (0.12, 0.074, 0.034)], m="Bronze", role="fitting", square=0.7),
               dict(prism=[(1.24, 0.06, 0.03), (1.40, 0.034, 0.025), (1.44, 0.0, 0.0)], m="Bronze", role="fitting", square=0.7)],
    ),
    # A sharpened combat scabbard: the same, with steel edges along both sides.
    "sharpened-combat-scabbard": dict(
        like="combat-scabbard",
        extra=[S([(sx * 0.068, 0.15), (sx * 0.056, 1.25)], 0.016 if sx < 0 else 0.004, 0.004 if sx < 0 else 0.016, 0.007, sharp=(sx < 0, sx > 0), tip=None, per=1) for sx in (-1, 1)],
    ),
    # A terbutje: a flat wooden paddle edged both sides with sharks' teeth.
    "terbutje": dict(
        family="hammers", build="hafted", hands="one",
        parts=[dict(plate=paddle(0.15, 0.95, 0.07), centre=(0.0, 0.55), thick=0.022, closed=True, m="Wood", role="head"),
               handle(-0.18, 0.16, 0.03, "Wrap", "cord", turns=6)]
              + teeth(lambda t: (0.07 * (0.4 + 0.6 * math.sin(math.pi * 0.5 * t) ** 0.8), 0.22 + 0.72 * t), 9, 1, 0.045)
              + teeth(lambda t: (-0.07 * (0.4 + 0.6 * math.sin(math.pi * 0.5 * t) ** 0.8), 0.22 + 0.72 * t), 9, -1, 0.045),
    ),
    # A steel terbutje: the paddle edged with steel teeth.
    "steel-terbutje": dict(
        family="hammers", build="hafted", hands="one",
        parts=[dict(plate=paddle(0.15, 0.95, 0.07), centre=(0.0, 0.55), thick=0.022, closed=True, m="Wood", role="head"),
               handle(-0.18, 0.16, 0.03, "Wrap", "cord", turns=6)]
              + teeth(lambda t: (0.07 * (0.4 + 0.6 * math.sin(math.pi * 0.5 * t) ** 0.8), 0.22 + 0.72 * t), 9, 1, 0.05, "Steel")
              + teeth(lambda t: (-0.07 * (0.4 + 0.6 * math.sin(math.pi * 0.5 * t) ** 0.8), 0.22 + 0.72 * t), 9, -1, 0.05, "Steel"),
    ),
    # A great terbutje: a longer, broader paddle, more teeth.
    "great-terbutje": dict(
        family="hammers", build="hafted", hands="one",
        parts=[dict(plate=paddle(0.18, 1.35, 0.095), centre=(0.0, 0.75), thick=0.026, closed=True, m="Wood", role="head"),
               handle(-0.28, 0.19, 0.034, "Wrap", "cord", turns=8)]
              + teeth(lambda t: (0.095 * (0.4 + 0.6 * math.sin(math.pi * 0.5 * t) ** 0.8), 0.26 + 1.07 * t), 13, 1, 0.055)
              + teeth(lambda t: (-0.095 * (0.4 + 0.6 * math.sin(math.pi * 0.5 * t) ** 0.8), 0.26 + 1.07 * t), 13, -1, 0.055),
    ),
    # A light pick: a short haft and a slim curved beak with a small hammer behind.
    "light-pick": dict(
        family="hammers", build="hafted", hands="light", haft=(-0.20, 0.95, 0.032, 0.03), wraps=[(-0.17, 0.16, 0.036)],
        butt=dict(z=-0.20, kind="cap", r=0.032),
        head=[dict(kind="pick", z=0.86, length=0.24, drop=0.06, thick=0.016, back="hammer", back_len=0.06, face=0.026, socket=(0.06, 0.04, 0.034))],
    ),
    # A heavy pick: a long beak, curving down, a hammer face behind.
    "heavy-pick": dict(
        family="hammers", build="hafted", hands="one", haft=(-0.32, 1.50, 0.04, 0.037), wraps=[(-0.28, 0.20, 0.044)],
        butt=dict(z=-0.32, kind="cap", r=0.04),
        head=[dict(kind="pick", z=1.36, length=0.40, drop=0.12, thick=0.024, back="hammer", back_len=0.09, face=0.04, socket=(0.08, 0.052, 0.045))],
    ),
    # A mattock: a two-handed digging pick, a beak one side and a broad adze the other.
    "mattock": dict(
        family="hammers", build="hafted", hands="two", haft=(-0.60, 2.25, 0.048, 0.044), wraps=[(-0.55, 0.25, 0.054)],
        butt=dict(z=-0.60, kind="cap", r=0.048),
        head=[dict(kind="pick", z=2.08, length=0.44, drop=0.10, thick=0.026, back="adze", back_len=0.32, socket=(0.10, 0.06, 0.055))],
    ),
})


# --- spears ------------------------------------------------------------------------------------

def feathers(z, count=3, length=0.16, m="Cloth", x=0.05):
    out = []
    for k in range(count):
        a = 360 * k / count
        out.append(dict(sweep=[(0.0, z), (x * 0.5, z - length * 0.5), (x, z - length)], wl=0.012, wr=0.012, t=0.003, sharp=(True, True), m=m, per=3, role="fitting",
                        rot=(0, 0, a)))
    return out


def coil(z0, z1, radius, turns, m="Cloth", r=0.009):
    return [dict(cord=[(radius * math.cos(2 * math.pi * turns * k / 60), radius * math.sin(2 * math.pi * turns * k / 60), z0 + (z1 - z0) * k / 60) for k in range(61)],
                 r=r, m=m, per=1)]


RECIPES.update({
    # A spear: a man's height and a half of ash, a leaf-shaped head with a socket, a butt cap.
    "spear": dict(
        family="spears", build="hafted", hands="two", haft=(-1.40, 2.60, 0.042, 0.036), wraps=[(-0.20, 0.30, 0.046)],
        butt=dict(z=-1.40, kind="cap", r=0.042),
        head=[dict(kind="spear", z=2.60, length=0.60, width=0.10, shape="leaf", size=1.55)],
    ),
    # A longspear: much longer, a narrow lozenge head made to reach past a shield.
    "longspear": dict(
        family="spears", build="hafted", hands="polearm", haft=(-2.20, 4.00, 0.040, 0.033), wraps=[(-0.20, 0.30, 0.044)],
        butt=dict(z=-2.20, kind="spike", r=0.040),
        head=[dict(kind="spear", z=4.00, length=0.55, width=0.075, shape="lozenge", size=1.6)],
    ),
    # A boarding pike: a long pike with a slim needle head and iron langets.
    "boarding-pike": dict(
        family="spears", build="hafted", hands="polearm", haft=(-2.00, 3.70, 0.038, 0.032), wraps=[(-0.20, 0.25, 0.042)],
        butt=dict(z=-2.00, kind="cap", r=0.038),
        head=[dict(kind="spear", z=3.70, length=0.50, width=0.05, shape="needle", socket=0.40, size=1.5)],
    ),
    # A sarissa: a phalanx pike far longer than a man, a small head, an iron sleeve at its
    # middle where the two lengths join, a spiked butt.
    "sarissa": dict(
        family="spears", build="hafted", hands="polearm", haft=(-3.00, 5.40, 0.038, 0.030), wraps=[(-0.20, 0.25, 0.042)],
        butt=dict(z=-3.00, kind="spike", r=0.038),
        head=[dict(kind="spear", z=5.40, length=0.42, width=0.07, shape="leaf", size=1.7), dict(kind="collar", z=1.30, r=0.036, h=0.22, m="DarkIron")],
    ),
    # A lance: a long tapering pole with a cone of steel, the vamplate, to cover the hand.
    "lance": dict(
        family="spears", build="hafted", hands="polearm",
        parts=[dict(lathe=[(-0.85, 0.0), (-0.84, 0.055), (-0.55, 0.075), (-0.25, 0.045), (0.20, 0.045), (0.30, 0.085), (1.2, 0.075), (4.5, 0.045), (6.1, 0.03)], m="Wood", n=10, role="haft"),
               dict(lathe=[(0.18, 0.05), (0.20, 0.20), (0.25, 0.205), (0.42, 0.09), (0.46, 0.08)], m="Steel", n=16, role="fitting"),
               handle(-0.22, 0.17, 0.05, "Wrap", "spiral")]
              + [dict(kind="spear", z=6.10, length=0.30, width=0.045, shape="needle", hr=0.03, socket=0.15, size=1.6)],
    ),
    # A hooked lance: the lance with a hook below its head to drag a rider down.
    "hooked-lance": dict(like="lance", extra=[dict(kind="hook", z=5.80, reach=0.20, rise=0.20, side=1, width=0.04, down=True, size=1.7)]),
    # A trident: three barbed prongs on a crossbar.
    "trident": dict(
        family="spears", build="hafted", hands="one", haft=(-1.30, 2.25, 0.038, 0.034), wraps=[(-0.20, 0.25, 0.042)],
        butt=dict(z=-1.30, kind="cap", r=0.038),
        head=[dict(kind="prongs", z=2.25, n=3, spread=0.14, length=0.46, r=0.018, centre=1.1, size=1.45)],
    ),
    # A tiger fork: three prongs, the middle one longest, the outer ones splaying out.
    "tiger-fork": dict(
        family="spears", build="hafted", hands="two", haft=(-1.60, 2.80, 0.04, 0.035), wraps=[(-0.20, 0.25, 0.044)],
        butt=dict(z=-1.60, kind="cap", r=0.04),
        head=[dict(kind="prongs", z=2.80, n=3, spread=0.12, length=0.42, curve=0.10, centre=1.35, barbs=False, r=0.02, size=1.5)],
        parts=[dict(sweep=[(0, 2.68), (0.03, 2.55), (0.05, 2.42)], wl=0.03, wr=0.03, t=0.004, m="Cloth", per=2, role="fitting", rot=(0, 0, a)) for a in (0, 120, 240)],
    ),
    # A boar spear: a broad leaf head with a crossbar below it to stop a charge at arm's length.
    "boar-spear": dict(
        family="spears", build="hafted", hands="two", haft=(-1.40, 2.55, 0.046, 0.04), wraps=[(-0.20, 0.30, 0.05)],
        butt=dict(z=-1.40, kind="cap", r=0.046),
        head=[dict(kind="spear", z=2.55, length=0.62, width=0.13, shape="broad", wings=0.17, socket=0.26, size=1.45)],
    ),
    # A barbed spear: a triangular head with barbs at its base that stay in the wound.
    "barbed-spear": dict(
        family="spears", build="hafted", hands="two", haft=(-1.40, 2.55, 0.042, 0.036), wraps=[(-0.20, 0.30, 0.046)],
        butt=dict(z=-1.40, kind="cap", r=0.042),
        head=[dict(kind="spear", z=2.55, length=0.55, width=0.10, shape="triangle", barbs=3, size=1.5)],
    ),
    # A harpoon: a long barbed head on one side, a line coiled round the shaft.
    "harpoon": dict(
        family="spears", build="hafted", hands="two", haft=(-1.50, 2.55, 0.042, 0.036),
        butt=dict(z=-1.50, kind="cap", r=0.042),
        head=[dict(kind="spear", z=2.55, length=0.48, width=0.05, shape="needle", socket=0.30, size=1.4),
              S([(0.03, 2.96), (0.14, 2.80), (0.17, 2.68)], taper(0.028, 0.005), taper(0.016, 0.003), 0.012, sharp=(True, False), per=3),
              S([(-0.03, 2.74), (-0.13, 2.60), (-0.15, 2.50)], taper(0.016, 0.003), taper(0.028, 0.005), 0.012, sharp=(False, True), per=3),
              dict(torus=(0.0, 0.0, 2.20), major=0.06, minor=0.009, m="DarkIron", plane="XY", n=14)]
             + coil(-0.60, 0.20, 0.052, 9),
    ),
    # A sibat: a light spear of the islands, a narrow leaf head with one barb.
    "sibat": dict(
        family="spears", build="hafted", hands="one", haft=(-1.00, 2.20, 0.032, 0.028), wraps=[(-0.15, 0.20, 0.036, "Cloth", "cord")],
        head=[dict(kind="spear", z=2.20, length=0.44, width=0.065, shape="leaf", barbs=1, hr=0.03, size=1.4)],
    ),
    # A doru: the hoplite's spear, a leaf head forward and a long square spike, the sauroter, behind.
    "doru": dict(
        family="spears", build="hafted", hands="one", haft=(-1.55, 2.60, 0.038, 0.034), wraps=[(-0.18, 0.22, 0.042)],
        head=[dict(kind="spear", z=2.60, length=0.52, width=0.085, shape="leaf", m="Bronze", size=1.45),
              dict(lathe=[(-1.55, 0.042), (-1.60, 0.044), (-1.70, 0.032), (-1.95, 0.0)], m="Bronze", n=4, faceted=True, role="edge")],
    ),
    # A totem spear: a carved post below the head, feathers hung from it, a stone-like head.
    "totem-spear": dict(
        family="spears", build="hafted", hands="two", haft=(-1.20, 2.40, 0.04, 0.036), wraps=[(-0.18, 0.22, 0.044, "Cloth", "cord")],
        head=[dict(lathe=[(1.85, 0.04), (1.88, 0.07), (1.98, 0.075), (2.01, 0.05), (2.04, 0.08), (2.14, 0.085), (2.17, 0.05), (2.20, 0.07), (2.30, 0.065), (2.36, 0.04)],
                   m="Wood", n=6, faceted=True, role="fitting"),
              dict(kind="spear", z=2.40, length=0.45, width=0.085, shape="leaf", m="Stone", socket=0.06, ferrule=False, size=1.4)]
             + feathers(1.85, 3, 0.20),
    ),
    # An injection spear: a narrow head with an iron reservoir and plunger behind it.
    "injection-spear": dict(
        family="spears", build="hafted", hands="two", haft=(-1.30, 2.30, 0.04, 0.036), wraps=[(-0.18, 0.22, 0.044)],
        butt=dict(z=-1.30, kind="cap", r=0.04),
        head=[dict(lathe=[(1.92, 0.04), (1.95, 0.095), (2.27, 0.095), (2.31, 0.05)], m="DarkIron", n=12, role="fitting"),
              dict(tube=[(0.10, 0, 1.80), (0.10, 0, 2.18)], r=0.016, m="Steel", n=6, role="fitting"),
              dict(lathe=[(-0.015, 0.0), (-0.01, 0.035), (0.01, 0.035), (0.015, 0.0)], m="Steel", n=10, role="fitting", at=(0.10, 0, 1.79)),
              dict(kind="spear", z=2.32, length=0.42, width=0.04, shape="needle", socket=0.04, ferrule=False, size=1.4)],
    ),
    # A syringe spear: a glass reservoir in iron caps behind a narrow head.
    "syringe-spear": dict(
        family="spears", build="hafted", hands="two", haft=(-1.30, 2.20, 0.04, 0.036), wraps=[(-0.18, 0.22, 0.044)],
        butt=dict(z=-1.30, kind="cap", r=0.04),
        head=[dict(lathe=[(2.10, 0.07), (2.11, 0.085), (2.45, 0.085), (2.46, 0.07)], m="Crystal", n=12, role="fitting"),
              dict(lathe=[(2.05, 0.045), (2.07, 0.095), (2.12, 0.095), (2.13, 0.05)], m="Bronze", n=12, role="fitting"),
              dict(lathe=[(2.44, 0.05), (2.45, 0.095), (2.50, 0.095), (2.51, 0.04)], m="Bronze", n=12, role="fitting"),
              dict(kind="spear", z=2.52, length=0.40, width=0.035, shape="needle", socket=0.03, ferrule=False, size=1.4)],
    ),
    # A flask pike: a pike with an iron cage holding a flask of something nasty behind its head.
    "flask-pike": dict(
        family="spears", build="hafted", hands="polearm", haft=(-2.00, 3.70, 0.038, 0.033), wraps=[(-0.20, 0.25, 0.042)],
        butt=dict(z=-2.00, kind="cap", r=0.038),
        head=[dict(lathe=[(3.30, 0.035), (3.34, 0.07), (3.42, 0.10), (3.52, 0.09), (3.58, 0.045), (3.62, 0.03)], m="Crystal", n=12, role="fitting"),
              dict(kind="spear", z=3.70, length=0.48, width=0.055, shape="needle", socket=0.06, size=1.5)]
             + [dict(tube=[(0.105 * math.cos(math.radians(a)), 0.105 * math.sin(math.radians(a)), 3.30), (0.115 * math.cos(math.radians(a)), 0.115 * math.sin(math.radians(a)), 3.45),
                           (0.05 * math.cos(math.radians(a)), 0.05 * math.sin(math.radians(a)), 3.62)], per=3, r=0.007, m="DarkIron", n=5, role="fitting") for a in (0, 90, 180, 270)],
    ),
    # A dwarven giant-sticker: a huge broad head on a heavy shaft, a crossbar of lugs behind it.
    "dwarven-giant-sticker": dict(
        family="spears", build="hafted", hands="polearm", haft=(-1.80, 3.50, 0.052, 0.046), wraps=[(-0.22, 0.30, 0.056)],
        butt=dict(z=-1.80, kind="spike", r=0.05),
        head=[dict(kind="spear", z=3.50, length=0.95, width=0.15, shape="broad", wings=0.26, socket=0.34, hr=0.048, size=1.35)],
        parts=[dict(lathe=[(z - 0.03, 0.05), (z, 0.056), (z + 0.03, 0.05)], m="DarkIron", role="fitting", n=8) for z in (1.0, 2.0, 3.0)],
    ),
    # An elven branched spear: a long needle of a head with slender branches growing up and out
    # of its socket, like a young stag's antlers.
    "elven-branched-spear": dict(
        family="spears", build="hafted", hands="polearm", haft=(-2.00, 3.70, 0.036, 0.031), wraps=[(-0.18, 0.25, 0.04)],
        butt=dict(z=-2.00, kind="cap", r=0.036, m="Gold"),
        head=[dict(kind="spear", z=3.70, length=0.62, width=0.05, shape="needle", socket=0.22, size=1.4)]
             + [S([(sx * 0.02, 3.62 + k * 0.16), (sx * 0.16, 3.78 + k * 0.16), (sx * (0.27 - k * 0.07), 4.06 + k * 0.12)], taper(0.022, 0.004), taper(0.022, 0.004), 0.01, per=3)
                for k, sx in ((0, 1), (0, -1), (1, 1), (1, -1))],
    ),
    # A bayonet: a three-sided blade on an offset socket that slides over a musket's muzzle.
    "bayonet": dict(
        family="spears", build="hafted", hands="light",
        parts=[dict(lathe=[(-0.10, 0.032), (-0.09, 0.038), (0.10, 0.038), (0.11, 0.032)], m="Steel", n=12, role="grip", at=(0.05, 0, 0)),
               dict(prism=[(0.08, 0.012, 0.012, 0.025), (0.14, 0.012, 0.012, 0.0)], m="Steel", role="fitting"),
               dict(blade=dict(base=0.14, tip=0.92, width=0.03, thick=0.022, section="diamond", taper=0.3, point=0.2, curve=0.9), m="Steel")],
    ),
})


# --- polearms ----------------------------------------------------------------------------------

POLE = (-1.55, 3.0, 0.042, 0.036)


def socket(z0, z1, r=0.042, m="Steel"):
    return dict(lathe=[(z0, r * 1.1), (z0 + 0.03, r * 1.16), (z1 - 0.03, r * 0.95), (z1, r * 0.75)], m=m, n=8, role="fitting")


def tines(z, count=5, spread=70, length=0.30, curl=0.10, m="DarkIron", r=0.014):
    """A rake of claws fanning out of a socket and curling forward: a kumade's bear claw."""
    out = []
    for k in range(count):
        a = math.radians(-spread / 2 + spread * k / max(1, count - 1))
        dx, dz = math.sin(a), math.cos(a)
        path = [(0.0, z), (dx * length * 0.6, z + dz * length * 0.6), (dx * length - curl, z + dz * length), (dx * length - curl * 1.8, z + dz * length * 0.85)]
        out.append(dict(tube=path, per=4, r=lambda u, r=r: r * (1 - 0.7 * u), m=m, n=6, role="edge"))
    return out


SCYTHE_SNATH = [(0.0, -1.40), (0.05, -0.4), (0.0, 0.6), (-0.06, 1.6), (0.0, 2.60)]

RECIPES.update({
    # A glaive: a long single-edged blade on a pole, its back curving away toward the point.
    "glaive": dict(
        family="polearms", build="hafted", hands="polearm", haft=POLE, wraps=[(-0.20, 0.30, 0.046)],
        butt=dict(z=-1.55, kind="cap", r=0.042),
        head=[dict(kind="glaive", z=3.0, length=0.85, width=0.10, curve=0.12, size=1.3)],
    ),
    # A glaive-guisarme: the glaive with a hook rising off its back to pull riders down.
    "glaive-guisarme": dict(
        family="polearms", build="hafted", hands="polearm", haft=POLE, wraps=[(-0.20, 0.30, 0.046)],
        butt=dict(z=-1.55, kind="cap", r=0.042),
        head=[dict(kind="glaive", z=3.0, length=0.85, width=0.10, curve=0.10, hook=0.26, size=1.3)],
    ),
    # A guisarme: a pruning hook of a blade, curving forward into a hook sharp on its inside,
    # and a spike on its back.
    "guisarme": dict(
        family="polearms", build="hafted", hands="polearm", haft=POLE, wraps=[(-0.20, 0.30, 0.046)],
        butt=dict(z=-1.55, kind="cap", r=0.042),
        head=[socket(2.75, 3.02),
              S([(0.0, 3.0), (0.02, 3.55), (-0.06, 3.92), (-0.24, 4.12), (-0.40, 4.10)], lambda s: 0.05 + 0.02 * math.sin(math.pi * s), lambda s: 0.06 * (1 - 0.5 * s), 0.016, sharp=(True, False), per=5),
              dict(spike=((0.04, 0, 3.30), (0.26, 0, 3.24)), r=0.022, m="Steel")],
    ),
    # A halberd: an axe blade, a long spike above it and a fluke behind, langets down the pole.
    "halberd": dict(
        family="polearms", build="hafted", hands="polearm", haft=(-1.60, 3.05, 0.042, 0.038), wraps=[(-0.20, 0.30, 0.046)],
        butt=dict(z=-1.60, kind="spike", r=0.042),
        head=[dict(kind="axe", z=2.80, shape="halberd", size=1.55, thick=0.04, socket=(0.20, 0.05, 0.046), back="spike", back_len=0.30, top=0.70, langets=0.45, hr=0.04)],
    ),
    # A bardiche: a long crescent of a blade fixed to the pole at its top and lashed at its foot.
    "bardiche": dict(
        family="polearms", build="hafted", hands="polearm", haft=(-1.40, 2.95, 0.044, 0.04), wraps=[(-0.20, 0.30, 0.048)],
        butt=dict(z=-1.40, kind="cap", r=0.044),
        head=[dict(kind="axe", z=2.48, shape="bardiche", size=1.25, thick=0.04, socket=(0.10, 0.05, 0.046), root=0.04, hr=0.042, rivets=False),
              dict(lathe=[(1.92, 0.046), (1.95, 0.052), (2.00, 0.052), (2.03, 0.046)], m="DarkIron", n=8, role="fitting")],
    ),
    # A bill: a broad blade hooking forward at its top, a spike above, a lug behind.
    "bill": dict(
        family="polearms", build="hafted", hands="polearm", haft=POLE, wraps=[(-0.20, 0.30, 0.046)],
        butt=dict(z=-1.55, kind="cap", r=0.042),
        head=[socket(2.75, 3.02),
              S([(0.0, 2.98), (-0.03, 3.45), (-0.14, 3.78), (-0.30, 3.86)], lambda s: 0.07 + 0.03 * math.sin(math.pi * s), lambda s: 0.06 * (1 - 0.6 * s), 0.016, sharp=(True, False), per=5),
              dict(spike=((0.02, 0, 3.55), (0.04, 0, 4.05)), r=0.024, m="Steel"),
              dict(spike=((0.04, 0, 3.12), (0.20, 0, 3.16)), r=0.02, m="Steel")],
    ),
    # A bec de corbin: a crow's beak — a hammer face, a long curved beak behind it, a top spike.
    "bec-de-corbin": dict(
        family="polearms", build="hafted", hands="polearm", haft=POLE, wraps=[(-0.20, 0.30, 0.046)],
        butt=dict(z=-1.55, kind="spike", r=0.042),
        head=[dict(kind="hammer", z=2.90, length=0.24, face=0.085, back="spike", back_len=0.58, top=0.62, socket=(0.13, 0.085, 0.075), side=1)],
        parts=[dict(prism=[(2.30, 0.010, 0.004, 0, sy * 0.043), (2.82, 0.012, 0.004, 0, sy * 0.043)], m="Steel", role="fitting") for sy in (1, -1)],
    ),
    # A Lucerne hammer: a hammer face split into three prongs, a beak behind, a long top spike.
    "lucerne-hammer": dict(
        family="polearms", build="hafted", hands="polearm", haft=POLE, wraps=[(-0.20, 0.30, 0.046)],
        butt=dict(z=-1.55, kind="cap", r=0.042),
        head=[dict(kind="hammer", z=2.90, length=0.14, face=0.085, back="spike", back_len=0.42, top=0.80, socket=(0.13, 0.085, 0.075))]
             + [dict(spike=((-0.13, 0, 2.90 + dz), (-0.30, 0, 2.90 + dz * 1.7)), r=0.032, m="Steel", n=5) for dz in (-0.085, 0.0, 0.085)],
    ),
    # A ranseur: a long central spike flanked by two blades sweeping up and out from its foot.
    "ranseur": dict(
        family="polearms", build="hafted", hands="polearm", haft=POLE, wraps=[(-0.20, 0.30, 0.046)],
        butt=dict(z=-1.55, kind="cap", r=0.042),
        head=[dict(kind="prongs", z=3.0, n=3, spread=0.06, length=0.42, curve=0.22, centre=2.0, barbs=False, r=0.024, size=1.25)],
    ),
    # A fauchard: a deeply curved blade like a scythe's set on end, a spike on its back.
    "fauchard": dict(
        family="polearms", build="hafted", hands="polearm", haft=POLE, wraps=[(-0.20, 0.30, 0.046)],
        butt=dict(z=-1.55, kind="cap", r=0.042),
        head=[dict(kind="glaive", z=3.0, length=0.95, width=0.09, curve=0.32, back_spike=0.14, flare=0.2, size=1.3)],
    ),
    # A horsechopper: a heavy, broad axe-like blade with a hook behind it to drag at horses.
    "horsechopper": dict(
        family="polearms", build="hafted", hands="polearm", haft=POLE, wraps=[(-0.20, 0.30, 0.046)],
        butt=dict(z=-1.55, kind="cap", r=0.042),
        head=[dict(kind="axe", z=2.80, shape="broad", size=1.55, thick=0.045, socket=(0.18, 0.055, 0.05), top=0.25, hr=0.04),
              dict(kind="hook", z=2.78, reach=0.28, rise=0.24, side=1, width=0.05, down=True)],
    ),
    # A naginata: a long curved blade on a pole, a round guard at the socket, the pole bound.
    "naginata": dict(
        family="polearms", build="hafted", hands="polearm", haft=(-1.40, 2.80, 0.04, 0.036),
        butt=dict(z=-1.40, kind="cap", r=0.04),
        head=[dict(kind="glaive", z=2.82, length=1.05, width=0.085, curve=0.20, flare=0.12, point=0.2, socket=0.30, size=1.2),
              dict(prism=[(2.78, 0.12, 0.10), (2.80, 0.125, 0.105), (2.82, 0.12, 0.10)], m="DarkIron", n=20, square=1.0, role="fitting")]
             + [dict(lathe=[(z - 0.03, 0.041), (z, 0.044), (z + 0.03, 0.041)], m="Wrap", n=8, role="fitting") for z in (2.3, 2.5, 2.7, -1.0, -1.2)],
    ),
    # A tepoztopilli: a broad wooden head on a pole, edged both sides with set blades of obsidian.
    "tepoztopilli": dict(
        family="polearms", build="hafted", hands="polearm", haft=(-1.40, 2.70, 0.040, 0.036), wraps=[(-0.20, 0.30, 0.044, "Cloth", "cord")],
        head=[dict(plate=[(0.06, 2.62), (0.12, 2.9), (0.13, 3.25), (0.08, 3.5), (0.0, 3.6), (-0.08, 3.5), (-0.13, 3.25), (-0.12, 2.9), (-0.06, 2.62)], centre=(0.0, 3.1), thick=0.03,
                   closed=True, m="Wood", role="head")]
             + teeth(lambda t: (0.13 - 0.05 * abs(t - 0.4), 2.75 + 0.75 * t), 7, 1, 0.06, "Obsidian")
             + teeth(lambda t: (-(0.13 - 0.05 * abs(t - 0.4)), 2.75 + 0.75 * t), 7, -1, 0.06, "Obsidian"),
    ),
    # An ogre hook: a great iron hook on a heavy pole, big enough to catch a man.
    "ogre-hook": dict(
        family="polearms", build="hafted", hands="two", haft=(-1.40, 2.80, 0.05, 0.046), wraps=[(-0.20, 0.30, 0.054)],
        butt=dict(z=-1.40, kind="cap", r=0.05),
        head=[socket(2.55, 2.85, 0.05, "DarkIron"),
              S([(0.0, 2.80), (0.02, 3.15), (-0.12, 3.45), (-0.36, 3.48), (-0.48, 3.28), (-0.42, 3.10)], lambda s: 0.05 * (1 - 0.6 * s), lambda s: 0.03 * (1 - 0.5 * s), 0.024,
                m="DarkIron", sharp=(False, True), per=5)],
    ),
    # A planson: a heavy spiked club head of oak and iron on a pole.
    "planson": dict(
        family="polearms", build="hafted", hands="two", haft=(-1.40, 2.55, 0.04, 0.038), wraps=[(-0.20, 0.30, 0.044)],
        butt=dict(z=-1.40, kind="cap", r=0.04),
        head=club([(2.50, 0.042), (2.65, 0.07), (2.90, 0.11), (3.12, 0.12), (3.24, 0.08), (3.30, 0.0)], n=10)
             + [dict(lathe=[(z - 0.025, r), (z, r * 1.08), (z + 0.025, r)], m="DarkIron", n=10, role="head") for z, r in ((2.62, 0.07), (3.12, 0.12))]
             + [dict(spike=((0.10 * math.cos(math.radians(a)), 0.10 * math.sin(math.radians(a)), z), (0.20 * math.cos(math.radians(a)), 0.20 * math.sin(math.radians(a)), z + 0.02)),
                     r=0.02, m="DarkIron", n=5) for z in (2.85, 3.02) for a in range(0 if z < 2.9 else 30, 360, 60)]
             + [dict(spike=((0, 0, 3.28), (0, 0, 3.52)), r=0.03, m="DarkIron", n=6)],
    ),
    # A flying blade: a long, narrow, curving blade on a pole, a ribbon streaming from its socket.
    "flying-blade": dict(
        family="polearms", build="hafted", hands="polearm", haft=(-1.40, 2.90, 0.038, 0.034),
        butt=dict(z=-1.40, kind="cap", r=0.038, m="Bronze"),
        head=[dict(kind="glaive", z=2.92, length=1.25, width=0.06, curve=0.22, flare=0.1, point=0.3, size=1.15, m="Steel"),
              dict(sweep=[(0.0, 2.80), (0.10, 2.55), (0.06, 2.30), (0.14, 2.05)], wl=0.03, wr=0.03, t=0.003, m="Cloth", per=4, role="fitting", tip=None)],
    ),
    # A gnome ripsaw glaive: a glaive whose edge carries a running saw chain, an engine at its foot.
    "gnome-ripsaw-glaive": dict(
        family="polearms", build="hafted", hands="polearm", haft=POLE, wraps=[(-0.20, 0.30, 0.046)],
        butt=dict(z=-1.55, kind="cap", r=0.042),
        head=[dict(kind="glaive", z=3.0, length=0.85, width=0.10, curve=0.06, size=1.3, flare=0.15),
              dict(chain=[(-0.04, 0, 3.06), (-0.16, 0, 3.30), (-0.19, 0, 3.70), (-0.14, 0, 3.95)], link=0.05, wire=0.009, m="DarkIron"),
              dict(prism=[(2.62, 0.11, 0.08, 0.05), (2.94, 0.11, 0.08, 0.05)], m="DarkIron", role="fitting"),
              dict(lathe=[(-0.03, 0.0), (-0.025, 0.07), (0.025, 0.07), (0.03, 0.0)], m="Bronze", n=12, role="fitting", rot=(90, 0, 0), at=(0.08, -0.09, 2.78)),
              dict(tube=[(0.14, 0, 2.70), (0.20, 0, 2.62), (0.20, 0, 2.50)], per=3, r=0.016, m="Bronze", role="fitting")],
    ),
    # A crook: a shepherd's staff, its head bent over into a wide hook.
    "crook": dict(
        family="polearms", build="hafted", hands="polearm", haft=(-1.40, 2.55, 0.036, 0.034),
        butt=dict(z=-1.40, kind="cap", r=0.036),
        head=[dict(tube=[(0.0, 0, 2.50), (0.0, 0, 2.75), (-0.05, 0, 2.95), (-0.20, 0, 3.05), (-0.34, 0, 2.97), (-0.37, 0, 2.80), (-0.31, 0, 2.70)], per=4, r=0.034, m="Wood", n=8, role="head")],
    ),
    # A kumade: a bamboo pole ending in a fan of curled iron claws, the bear's paw.
    "kumade": dict(
        family="polearms", build="hafted", hands="polearm", haft=(-1.40, 2.75, 0.036, 0.034),
        butt=dict(z=-1.40, kind="cap", r=0.036),
        head=[socket(2.65, 2.80, 0.036, "DarkIron")] + tines(2.78, 5, 90, 0.50, 0.17, r=0.019),
        parts=[dict(lathe=[(z - 0.02, 0.036), (z, 0.042), (z + 0.02, 0.036)], m="Wood", n=8, role="fitting") for z in (-0.8, 0.0, 0.8, 1.6, 2.3)],
    ),
    # A collapsible kumade: the kumade in sections, sleeved with iron at each joint.
    "collapsible-kumade": dict(
        family="polearms", build="hafted", hands="polearm", haft=(-1.20, 2.45, 0.034, 0.032),
        butt=dict(z=-1.20, kind="cap", r=0.034),
        head=[socket(2.36, 2.50, 0.034, "DarkIron")] + tines(2.48, 4, 80, 0.42, 0.15, r=0.018),
        parts=[dict(lathe=[(z - 0.07, 0.038), (z - 0.06, 0.044), (z + 0.06, 0.044), (z + 0.07, 0.038)], m="DarkIron", n=8, role="fitting") for z in (-0.4, 0.6, 1.6)],
    ),
    # A mancatcher: a pole ending in a sprung fork that closes round a neck, spikes inside it.
    "mancatcher": dict(
        family="polearms", build="hafted", hands="polearm", haft=(-1.40, 2.80, 0.038, 0.034),
        butt=dict(z=-1.40, kind="cap", r=0.038),
        head=[socket(2.65, 2.85, 0.038, "DarkIron")]
             + [dict(tube=[(0.0, 0, 2.82), (sx * 0.12, 0, 2.92), (sx * 0.22, 0, 3.12), (sx * 0.20, 0, 3.36), (sx * 0.07, 0, 3.46)], per=4, r=0.016, m="DarkIron", role="head") for sx in (-1, 1)]
             + [dict(spike=((sx * 0.21, 0, z), (sx * 0.13, 0, z + 0.02)), r=0.012, m="DarkIron", n=4) for sx in (-1, 1) for z in (3.10, 3.25)]
             + coil(2.86, 2.98, 0.05, 4, "DarkIron", 0.008),
    ),
    # A tri-point double-edged sword: a broad double-edged blade on a pole whose tip parts into
    # three points.
    "tri-point-double-edged-sword": dict(
        family="polearms", build="hafted", hands="polearm", haft=(-1.40, 2.75, 0.042, 0.038), wraps=[(-0.20, 0.30, 0.046)],
        butt=dict(z=-1.40, kind="cap", r=0.042),
        head=[socket(2.50, 2.80),
              dict(prism=[(2.76, 0.10, 0.03), (2.80, 0.10, 0.03)], m="Bronze", role="fitting"),
              S([(0.0, 2.78), (0.0, 3.85)], lambda s: 0.07 * (1 - 0.15 * s), lambda s: 0.07 * (1 - 0.15 * s), 0.018, per=8, bevel=0.45)]
             + [S([(sx * 0.03, 3.45), (sx * 0.10, 3.62), (sx * 0.13, 3.85)], taper(0.03, 0.004), taper(0.03, 0.004), 0.014, per=3) for sx in (-1, 1)],
    ),
    # A dwarven longaxe: a great dwarven axe head on a reaching pole.
    "dwarven-longaxe": dict(
        family="polearms", build="hafted", hands="polearm", haft=(-1.60, 3.20, 0.048, 0.044), wraps=[(-0.22, 0.30, 0.052)],
        butt=dict(z=-1.60, kind="spike", r=0.048),
        head=[dict(kind="axe", z=2.85, shape="dwarven", size=1.55, thick=0.06, socket=(0.19, 0.064, 0.058), back="spike", back_len=0.22, langets=0.40, hr=0.046)],
        parts=[dict(lathe=[(z - 0.03, 0.052), (z, 0.058), (z + 0.03, 0.052)], m="DarkIron", role="fitting", n=8) for z in (1.0, 1.8, 2.4)],
    ),
    # A dwarven longhammer: a great block of a hammer on a reaching pole.
    "dwarven-longhammer": dict(
        family="polearms", build="hafted", hands="polearm", haft=(-1.60, 3.10, 0.048, 0.044), wraps=[(-0.22, 0.30, 0.052)],
        butt=dict(z=-1.60, kind="flare", r=0.048),
        head=[dict(kind="hammer", z=2.92, length=0.28, face=0.12, back="spike", back_len=0.30, socket=(0.17, 0.12, 0.115), m="DarkIron")],
        parts=[dict(lathe=[(z - 0.03, 0.052), (z, 0.058), (z + 0.03, 0.052)], m="DarkIron", role="fitting", n=8) for z in (1.0, 1.8, 2.4)],
    ),
    # An orc skull ram: a ram's skull, horns and all, bound with iron to the end of a pole.
    "orc-skull-ram": dict(
        family="polearms", build="hafted", hands="polearm", haft=(-1.40, 2.75, 0.048, 0.044), wraps=[(-0.22, 0.30, 0.052, "Cloth", "cord")],
        butt=dict(z=-1.40, kind="cap", r=0.048),
        head=[dict(lathe=[(2.66, 0.07), (2.76, 0.18), (2.94, 0.21), (3.10, 0.17), (3.18, 0.0)], m="Bone", n=10, role="head", oval=0.85),
              dict(lathe=[(0.0, 0.14), (0.14, 0.125), (0.27, 0.085), (0.33, 0.0)], m="Bone", n=8, role="head", rot=(0, -90, 0), at=(-0.08, 0, 2.88)),
              dict(lathe=[(2.66, 0.05), (2.69, 0.07), (2.74, 0.07), (2.76, 0.05)], m="DarkIron", n=8, role="fitting")]
             + [dict(tube=[(0.0, sy * 0.14, 3.02), (0.14, sy * 0.24, 3.13), (0.27, sy * 0.22, 2.96), (0.21, sy * 0.18, 2.76), (0.08, sy * 0.18, 2.79)], per=3, r=lambda u: 0.055 * (1 - 0.65 * u), m="Horn", role="head")
                for sy in (1, -1)],
    ),
    # A scythe: a long curved snath with two grips, its blade out at right angles near the top.
    "scythe": dict(
        family="polearms", build="hafted", hands="two",
        parts=[dict(tube=SCYTHE_SNATH, per=4, r=0.036, m="Wood", n=8, role="haft"),
               dict(tube=[(0.0, 0, 0.05), (0.18, 0, 0.12)], r=0.024, m="Wood", n=6, role="grip"),
               dict(tube=[(-0.04, 0, 1.30), (0.16, 0, 1.40)], r=0.024, m="Wood", n=6, role="grip"),
               dict(prism=[(2.48, 0.05, 0.045), (2.62, 0.05, 0.045)], m="DarkIron", role="fitting"),
               S([(0.0, 2.56), (-0.40, 2.64), (-0.85, 2.60), (-1.20, 2.46), (-1.34, 2.34)], lambda s: 0.03 * (1 - 0.5 * s), lambda s: 0.075 * (1 - 0.7 * s) + 0.005, 0.012,
                 sharp=(False, True), per=5)],
    ),
    # A switchscythe: a war scythe, its blade swung up in line with the snath on a hinge.
    "switchscythe": dict(
        family="polearms", build="hafted", hands="two",
        parts=[dict(tube=SCYTHE_SNATH, per=4, r=0.036, m="Wood", n=8, role="haft"),
               dict(tube=[(0.0, 0, 0.05), (0.18, 0, 0.12)], r=0.024, m="Wood", n=6, role="grip"),
               dict(prism=[(2.46, 0.055, 0.05), (2.66, 0.055, 0.05)], m="DarkIron", role="fitting"),
               dict(lathe=[(-0.06, 0.0), (-0.05, 0.035), (0.05, 0.035), (0.06, 0.0)], m="DarkIron", n=10, role="fitting", rot=(90, 0, 0), at=(0, 0, 2.60)),
               S([(0.0, 2.62), (-0.05, 3.10), (-0.18, 3.55), (-0.36, 3.85)], lambda s: 0.075 * (1 - 0.7 * s) + 0.005, lambda s: 0.03 * (1 - 0.5 * s), 0.012,
                 sharp=(True, False), per=5)],
    ),
    # A rhomphaia: a long, slightly curved single-edged blade on a long grip.
    "rhomphaia": dict(
        family="polearms", build="curved", hands="polearm", metal="Steel", fittings="DarkIron",
        blade=dict(base=0.32, length=1.80, sweep=0.14, width=0.095, flare=0.0, clip=0.88, toff=0.4, thick=0.02),
        guard=dict(kind="bar", z=0.30, span=0.06, height=0.02, depth=0.03, ends="plain", centre=0.0),
        grip=dict(bottom=-0.85, top=0.29, radius=0.036, style="spiral", turns=18),
        pommel=dict(kind="cap", radius=0.042, height=0.04),
    ),
    # An ankus: an elephant goad — a short haft with a straight spike and a hook curving back.
    "ankus": dict(
        family="polearms", build="hafted", hands="one", haft=(-0.25, 1.05, 0.033, 0.03), wraps=[(-0.22, 0.20, 0.037)],
        butt=dict(z=-0.25, kind="knob", r=0.033, m="Bronze"),
        head=[socket(0.95, 1.08, 0.033, "Bronze"),
              dict(spike=((0, 0, 1.06), (0, 0, 1.40)), r=0.022, m="Steel"),
              S([(0.0, 1.10), (-0.08, 1.16), (-0.13, 1.08), (-0.10, 0.98)], taper(0.022, 0.004), taper(0.022, 0.004), 0.012, sharp=(False, True), per=4)],
    ),
})


# --- flails and chains -------------------------------------------------------------------------

def eye(z, r=0.03, m="DarkIron"):
    """The iron cap and ring at the top of a flail's haft that its chain hangs from."""
    return [dict(lathe=[(z - 0.08, r * 1.05), (z - 0.06, r * 1.15), (z, r * 1.0), (z + 0.01, r * 0.6)], m=m, n=8, role="fitting"),
            dict(torus=(0.0, 0.0, z + 0.035), major=0.028, minor=0.008, m=m, plane="YZ", n=12, sides=5)]


def stick(z0, z1, r=0.03, m="Wood", at=(0, 0, 0), rot=(0, 0, 0)):
    """An eight-sided wooden stick with iron caps: a nunchaku's or a sansetsukon's section."""
    return [dict(lathe=[(z0, 0.0), (z0 + 0.005, r * 0.9), (z0 + 0.02, r), (z1 - 0.02, r), (z1 - 0.005, r * 0.9), (z1, 0.0)], m=m, n=8, faceted=True, role="head", at=at, rot=rot)]


def lash(points, r0=0.016, r1=0.004, m="Wrap"):
    return dict(tube=points, per=5, r=lambda u: r0 + (r1 - r0) * u ** 0.7, m=m, n=6, role="edge")


def cage(c, r=0.07, m="DarkIron"):
    """A ball of iron bands, a poi's or a censer's cage."""
    out = []
    for k in range(3):
        a = k * 60
        out.append(dict(torus=(0, 0, 0), major=r, minor=0.007, m=m, plane="XZ", tilt=0, n=14, sides=4, rot=(0, 0, a), at=c, role="head"))
    out.append(dict(torus=(0, 0, 0), major=r, minor=0.007, m=m, plane="XY", n=14, sides=4, at=c, role="head"))
    return out


RECIPES.update({
    # A light flail: a short haft with an iron eye, a few links of chain, a spiked ball.
    "light-flail": dict(
        family="flails", build="hafted", hands="one", haft=(-0.35, 0.60, 0.034, 0.032), wraps=[(-0.32, 0.18, 0.038)],
        butt=dict(z=-0.35, kind="knob", r=0.034),
        head=eye(0.62) + [dict(chain=[(0, 0, 0.67), (0.06, 0, 0.82), (0.16, 0, 0.94)], link=0.07),
                          dict(ball=(0.22, 0, 1.04), r=0.085, spikes=12, length=0.6, m="DarkIron")],
    ),
    # A heavy flail: two hands' haft, a chain, and a swinging bar of iron-shod oak set with spikes.
    "heavy-flail": dict(
        family="flails", build="hafted", hands="two", haft=(-0.70, 1.20, 0.042, 0.04), wraps=[(-0.66, 0.30, 0.046)],
        butt=dict(z=-0.70, kind="cap", r=0.042),
        head=eye(1.22, 0.04) + [dict(chain=[(0, 0, 1.28), (0.05, 0, 1.40), (0.10, 0, 1.48)], link=0.08, wire=0.013),
                                dict(lathe=[(0.0, 0.0), (0.01, 0.05), (0.62, 0.07), (0.64, 0.0)], m="Wood", n=8, faceted=True, role="head", dir=(0.38, 0, 1.0), at=(0.11, 0, 1.52))]
             + [dict(spike=((0.11 + 0.38 * t * 0.53 + 0.07 * dx, 0.07 * dy, 1.52 + t * 0.53 * 1.0 * 0.95), (0.11 + 0.38 * t * 0.53 + 0.15 * dx, 0.15 * dy, 1.52 + t * 0.5 + 0.01)), r=0.02, m="DarkIron", n=5)
                for t in (0.35, 0.65, 0.95) for dx, dy in ((0.93, 0.0), (-0.93, 0.0), (0.0, 1.0), (0.0, -1.0))],
    ),
    # A spiked chain: a long chain studded with spikes, a grip at each end.
    "spiked-chain": dict(
        family="flails", build="hafted", hands="two",
        head=[handle(-0.20, 0.16, 0.032, "Wrap", "spiral"),
              dict(chain=[(0, 0, 0.18), (0.10, 0, 0.6), (0.35, 0, 0.95), (0.40, 0, 1.40), (0.20, 0, 1.80), (0.05, 0, 2.05)], link=0.075, wire=0.012),
              dict(grip=(0.0, 0.34, 0.032), m="Wrap", style="spiral", turns=6, dir=(-0.4, 0, 1.0), at=(0.05, 0, 2.07))]
             + [dict(spike=((x, 0, z), (x + dx * 0.10, 0, z + 0.02)), r=0.016, m="Steel", n=4)
                for x, z, dx in ((0.10, 0.55, -1), (0.18, 0.78, 1), (0.36, 1.05, -1), (0.40, 1.30, 1), (0.33, 1.58, -1), (0.20, 1.80, 1))],
        fx=((0, 0, 0.18), (0.05, 0, 2.05)),
    ),
    # A chain hammer: a hammer in the hand and another on the end of its chain, to be hurled.
    "chain-hammer": dict(
        family="flails", build="hafted", hands="two", haft=(-0.30, 0.80, 0.034, 0.032), wraps=[(-0.27, 0.20, 0.038)],
        head=[dict(kind="hammer", z=0.70, length=0.12, face=0.05, back="face", socket=(0.07, 0.05, 0.048)),
              dict(chain=[(0, 0, 0.78), (0.10, 0, 1.05), (0.28, 0, 1.25), (0.38, 0, 1.30)], link=0.06),
              dict(prism=[(0.0, 0.05, 0.05), (0.20, 0.05, 0.05)], m="Steel", role="head", rot=(0, 90, 0), at=(0.36, 0, 1.36))],
    ),
    # A chain spear: a spear with a weighted chain fixed to its butt.
    "chain-spear": dict(
        family="flails", build="hafted", hands="two", haft=(-1.00, 2.10, 0.04, 0.036), wraps=[(-0.20, 0.25, 0.044)],
        head=[dict(kind="spear", z=2.10, length=0.55, width=0.08, shape="leaf", size=1.3), dict(kind="butt", z=-1.00, kind_="cap") if False else eye(-1.0)[1],
              dict(chain=[(0, 0, -1.04), (0.08, 0, -1.30), (0.22, 0, -1.48)], link=0.06),
              dict(ball=(0.26, 0, -1.55), r=0.06, spikes=0, m="DarkIron")],
    ),
    # A dire flail: a long haft with a chain and a spiked ball at each end.
    "dire-flail": dict(
        family="flails", build="hafted", hands="two", haft=(-0.85, 0.85, 0.042, 0.042), wraps=[(-0.30, 0.30, 0.046)],
        head=eye(0.87, 0.04) + [dict(chain=[(0, 0, 0.93), (0.06, 0, 1.08), (0.16, 0, 1.18)], link=0.07),
                                dict(ball=(0.22, 0, 1.28), r=0.10, spikes=12, length=0.55, m="DarkIron")],
        lower=eye(0.87, 0.04) + [dict(chain=[(0, 0, 0.93), (0.06, 0, 1.08), (0.16, 0, 1.18)], link=0.07),
                                 dict(ball=(0.22, 0, 1.28), r=0.10, spikes=12, length=0.55, m="DarkIron")],
        fx=((-0.22, 0, -1.40), (0.22, 0, 1.40)),
    ),
    # A meteor hammer: a long rope with an iron weight at each end, coiled for throwing.
    "meteor-hammer": dict(
        family="flails", build="hafted", hands="two",
        head=[dict(cord=[(0.0, 0, -0.30), (0.15, 0, 0.10), (0.05, 0, 0.55), (-0.25, 0, 0.75), (-0.35, 0, 0.40), (-0.10, 0, 0.20), (0.25, 0, 0.45), (0.35, 0, 0.95), (0.20, 0, 1.35)], r=0.014, m="Cloth", role="cord"),
              dict(ball=(0.0, 0, -0.38), r=0.075, m="DarkIron"), dict(ball=(0.20, 0, 1.43), r=0.085, m="DarkIron")],
        fx=((0.20, 0, 1.35), (0.20, 0, 1.52)),
    ),
    # A kusarigama: a kama, and a chain from its butt to an iron weight.
    "kusarigama": dict(
        family="flails", build="hafted", hands="two", haft=(-0.22, 0.42, 0.03, 0.028),
        head=[dict(prism=[(0.35, 0.032, 0.034), (0.43, 0.032, 0.034)], m="DarkIron", role="fitting"),
              S([(0.0, 0.40), (-0.18, 0.44), (-0.32, 0.43), (-0.42, 0.38)], lambda s: 0.034 * (1 - 0.5 * s) + 0.004, 0.012, 0.010, sharp=(True, False), per=4),
              dict(chain=[(0, 0, -0.24), (0.10, 0, -0.45), (0.30, 0, -0.55), (0.45, 0, -0.42), (0.50, 0, -0.10), (0.42, 0, 0.30)], link=0.055, wire=0.009),
              dict(ball=(0.40, 0, 0.36), r=0.05, m="DarkIron")],
    ),
    # A kyoketsu-shoge: a double-edged blade with a hooked second blade at its root, on a long
    # cord ending in an iron ring.
    "kyoketsu-shoge": dict(
        family="flails", build="hafted", hands="two",
        head=[S([(0.0, 0.0), (0.0, 0.45)], 0.03, 0.03, 0.012, per=6),
              S([(0.0, 0.08), (-0.10, 0.14), (-0.18, 0.24), (-0.20, 0.36)], taper(0.026, 0.004), taper(0.012, 0.003), 0.01, sharp=(True, False), per=4),
              dict(cord=[(0.0, 0, -0.02), (0.06, 0, -0.25), (0.25, 0, -0.35), (0.42, 0, -0.18), (0.45, 0, 0.20), (0.35, 0, 0.55)], r=0.010, m="Cloth"),
              dict(torus=(0.33, 0.0, 0.64), major=0.08, minor=0.011, m="DarkIron", n=16)],
    ),
    # A double-chained kama: two kama, their butts joined by a chain.
    "double-chained-kama": dict(
        family="flails", build="hafted", hands="two", haft=(-0.22, 0.42, 0.03, 0.028),
        head=[dict(prism=[(0.35, 0.032, 0.034), (0.43, 0.032, 0.034)], m="DarkIron", role="fitting"),
              S([(0.0, 0.40), (-0.18, 0.44), (-0.32, 0.43), (-0.42, 0.38)], lambda s: 0.034 * (1 - 0.5 * s) + 0.004, 0.012, 0.010, sharp=(True, False), per=4),
              dict(chain=[(0, 0, -0.24), (0.15, 0, -0.38), (0.35, 0, -0.30), (0.42, 0, -0.08)], link=0.055, wire=0.009),
              dict(lathe=[(0.0, 0.0), (0.01, 0.03), (0.62, 0.028), (0.63, 0.0)], m="Wood", n=8, role="haft", dir=(0.25, 0, 1.0), at=(0.42, 0, -0.06)),
              S([(0.0, 0.0), (-0.18, 0.04), (-0.32, 0.03), (-0.42, -0.02)], lambda s: 0.034 * (1 - 0.5 * s) + 0.004, 0.012, 0.010, sharp=(True, False), per=4, at=(0.57, 0, 0.55))],
    ),
    # Nunchaku: two short sticks joined by a short chain.
    "nunchaku": dict(
        family="flails", build="hafted", hands="light",
        head=stick(-0.10, 0.52, 0.03)
             + [dict(chain=[(0, 0, 0.53), (0.06, 0, 0.60), (0.14, 0, 0.62)], link=0.045, wire=0.008)]
             + stick(0.0, 0.62, 0.03, at=(0.16, 0, 0.62), rot=(0, 70, 0)),
        fx=((0, 0, -0.10), (0.75, 0, 0.82)),
    ),
    # A sansetsukon: three staff sections joined by short chains.
    "sansetsukon": dict(
        family="flails", build="hafted", hands="two",
        head=stick(-0.30, 0.65, 0.032)
             + [dict(chain=[(0, 0, 0.66), (0.05, 0, 0.72), (0.12, 0, 0.74)], link=0.045, wire=0.008)]
             + stick(0.0, 0.95, 0.032, at=(0.13, 0, 0.74), rot=(0, 60, 0))
             + [dict(chain=[(0.96, 0, 1.22), (1.02, 0, 1.24), (1.08, 0, 1.30)], link=0.045, wire=0.008)]
             + stick(0.0, 0.95, 0.032, at=(1.08, 0, 1.31), rot=(0, 10, 0)),
        fx=((0, 0, -0.30), (1.25, 0, 2.25)),
    ),
    # A nine-section whip: a handle, nine iron rods linked by rings, a dart at its end.
    "nine-section-whip": dict(
        family="flails", build="hafted", hands="one",
        head=[handle(-0.18, 0.16, 0.03, "Wrap", "cord", turns=6)]
             + [dict(lathe=[(0.0, 0.0), (0.01, 0.014), (0.17, 0.014), (0.18, 0.0)], m="Steel", n=6, role="edge",
                     dir=(math.sin(math.radians(25 * k)), 0, math.cos(math.radians(25 * k))),
                     at=(sum(0.19 * math.sin(math.radians(25 * j)) for j in range(k)), 0, 0.17 + sum(0.19 * math.cos(math.radians(25 * j)) for j in range(k)))) for k in range(9)]
             + [S([(0.0, 0.0), (0.0, 0.16)], 0.03, 0.03, 0.01, per=4, at=(sum(0.19 * math.sin(math.radians(25 * j)) for j in range(9)), 0.17 + sum(0.19 * math.cos(math.radians(25 * j)) for j in range(9))), rot=(0, 225, 0))],
    ),
    # A whip: a braided handle and a long lash tapering to its cracker, thrown out in a curve.
    "whip": dict(
        family="flails", build="hafted", hands="one",
        head=[handle(-0.16, 0.30, 0.03, "Wrap", "spiral", turns=8),
              lash([(0, 0, 0.30), (0.05, 0, 0.9), (0.35, 0, 1.4), (0.80, 0, 1.55), (1.05, 0, 1.30), (0.95, 0, 1.0), (0.70, 0, 1.05)])],
    ),
    # A scorpion whip: a whip whose last length is set with small blades and a hooked sting.
    "scorpion-whip": dict(
        family="flails", build="hafted", hands="light",
        head=[handle(-0.16, 0.28, 0.03, "Wrap", "spiral", turns=8),
              lash([(0, 0, 0.28), (0.05, 0, 0.75), (0.30, 0, 1.15), (0.65, 0, 1.28), (0.85, 0, 1.10)], 0.015, 0.008)]
             + [S([(x, z), (x + dx, z + dz)], 0.012, 0.012, 0.005, per=1) for x, z, dx, dz in ((0.32, 1.17, -0.04, 0.06), (0.48, 1.25, 0.0, 0.07), (0.64, 1.28, 0.05, 0.05), (0.78, 1.20, 0.06, 0.0))]
             + [S([(0.85, 1.10), (0.92, 0.98), (0.86, 0.90)], taper(0.02, 0.003), taper(0.012, 0.003), 0.008, sharp=(True, False), per=3)],
    ),
    # A cat-o'-nine-tails: a short handle and nine knotted cords.
    "cat-o-nine-tails": dict(
        family="flails", build="hafted", hands="light",
        head=[handle(-0.15, 0.22, 0.03, "Wrap", "spiral", turns=6)]
             + [dict(cord=[(0, 0, 0.22), (0.30 * math.sin(math.radians(a)) * 0.6, 0.02 * (k % 3 - 1), 0.22 + 0.30 * math.cos(math.radians(a)) * 0.6), (0.6 * math.sin(math.radians(a)) + 0.05, 0.03 * (k % 3 - 1), 0.22 + 0.62 * math.cos(math.radians(a)))],
                     r=0.007, m="Cloth", role="edge", per=3, n=5) for k, a in enumerate(range(-8, 41, 6))]
             + [dict(ball=(0.6 * math.sin(math.radians(a)) + 0.05, 0.03 * (k % 3 - 1), 0.22 + 0.62 * math.cos(math.radians(a))), r=0.014, m="Cloth", n=6) for k, a in enumerate(range(-8, 41, 6))],
    ),
    # An urumi: a hilt from which long ribbons of flexible steel curl out like a lash.
    "urumi": dict(
        family="flails", build="hafted", hands="one",
        head=[handle(-0.16, 0.20, 0.032, "Wrap", "spiral", turns=6),
              dict(guard=0.21, span=0.10, height=0.016, depth=0.03, ends="knob", knob=0.8, m="Bronze", role="fitting"),
              dict(lathe=[(-0.22, 0.0), (-0.21, 0.04), (-0.17, 0.034)], m="Bronze", role="fitting")]
             + [S([(dx, 0.22), (dx + 0.05 * k, 0.8), (0.25 + 0.12 * k, 1.35 + 0.1 * k), (0.65 + 0.1 * k, 1.55 + 0.05 * k), (0.90 + 0.05 * k, 1.35)], 0.014, 0.014, 0.003, per=5, sharp=(True, True))
                for k, dx in enumerate((-0.02, 0.0, 0.02))],
    ),
    # A bladed scarf: a long silk scarf with small blades sewn along its edge.
    "bladed-scarf": dict(
        family="flails", build="hafted", hands="two",
        head=[dict(sweep=[(0, -0.10), (0.05, 0.4), (-0.10, 0.9), (0.10, 1.4), (0.40, 1.65)], wl=0.05, wr=0.05, t=0.004, m="Cloth", per=6, tip=None, role="fitting")]
             + [S([(x, z), (x - 0.06, z + 0.03)], 0.02, 0.02, 0.005, per=1) for x, z in ((-0.03, 0.30), (0.0, 0.55), (-0.13, 0.85), (-0.06, 1.15), (0.08, 1.42), (0.30, 1.60))],
        fx=((0, 0, 0.2), (0.4, 0, 1.65)),
    ),
    # A battle poi: an iron cage of oil-soaked wick swung on a chain from a hand loop.
    "battle-poi": dict(
        family="flails", build="hafted", hands="light",
        head=[dict(torus=(0.0, 0.0, 0.0), major=0.05, minor=0.012, m="Wrap", plane="YZ", n=14),
              dict(chain=[(0, 0, 0.05), (0.05, 0, 0.30), (0.12, 0, 0.55)], link=0.05, wire=0.008),
              dict(lathe=[(-0.05, 0.0), (-0.04, 0.045), (0.04, 0.045), (0.05, 0.0)], m="Cloth", n=10, role="head", at=(0.15, 0, 0.66))]
             + cage((0.15, 0.0, 0.66), 0.075),
    ),
    # A halfling rope-shot: a short rope ending in a knot of lead weights.
    "halfling-rope-shot": dict(
        family="flails", build="hafted", hands="light",
        head=[handle(-0.12, 0.12, 0.028, "Wrap", "cord", turns=4),
              dict(cord=[(0, 0, 0.12), (0.05, 0, 0.40), (0.15, 0, 0.62)], r=0.011, m="Cloth"),
              dict(ball=(0.18, 0.0, 0.68), r=0.04, m="DarkIron"), dict(ball=(0.24, 0.03, 0.64), r=0.035, m="DarkIron"), dict(ball=(0.13, -0.03, 0.73), r=0.035, m="DarkIron")],
    ),
    # A flindbar: two iron bars linked by a chain, the short one in the hand.
    "flindbar": dict(
        family="flails", build="hafted", hands="one",
        head=stick(-0.15, 0.30, 0.026, "DarkIron")
             + [dict(chain=[(0, 0, 0.31), (0.05, 0, 0.38), (0.10, 0, 0.40)], link=0.045, wire=0.008)]
             + stick(0.0, 0.85, 0.03, "DarkIron", at=(0.11, 0, 0.40), rot=(0, 35, 0)),
        fx=((0.11, 0, 0.40), (0.60, 0, 1.10)),
    ),
    # A gnome flick mace: a handle, a coiled spring, a chain and a flanged mace head.
    "gnome-flick-mace": dict(
        family="flails", build="hafted", hands="one", haft=(-0.30, 0.55, 0.034, 0.032), wraps=[(-0.27, 0.15, 0.038)],
        head=coil(0.55, 0.85, 0.035, 7, "Bronze", 0.008)
             + [dict(chain=[(0, 0, 0.86), (0.04, 0, 1.00), (0.10, 0, 1.08)], link=0.05),
                dict(kind="flanged", z0=1.10, z1=1.36, r=0.12, n=6, core=0.06)],
    ),
    # A dorn-dergar: a dwarven ball and chain, a long heavy chain and a great spiked ball.
    "dwarven-dorn-dergar": dict(
        family="flails", build="hafted", hands="two", haft=(-0.30, 0.45, 0.04, 0.038), wraps=[(-0.27, 0.40, 0.044)],
        head=eye(0.48, 0.04) + [dict(chain=[(0, 0, 0.54), (0.15, 0, 1.00), (0.10, 0, 1.50), (0.30, 0, 1.90)], link=0.09, wire=0.015),
                                dict(ball=(0.36, 0, 2.05), r=0.13, spikes=16, length=0.5, m="DarkIron")],
    ),
    # A flailpole: a flail's chain and spiked ball on the end of a pole.
    "flailpole": dict(
        family="flails", build="hafted", hands="polearm", haft=(-1.40, 2.70, 0.04, 0.036), wraps=[(-0.20, 0.30, 0.044)],
        butt=dict(z=-1.40, kind="cap", r=0.04),
        head=eye(2.72, 0.038) + [dict(chain=[(0, 0, 2.78), (0.06, 0, 2.96), (0.16, 0, 3.08)], link=0.07),
                                 dict(ball=(0.22, 0, 3.18), r=0.10, spikes=12, length=0.55, m="DarkIron")],
    ),
    # A flying talon: a hooked blade on a long chain, cast to catch and drag.
    "flying-talon": dict(
        family="flails", build="hafted", hands="light",
        head=[handle(-0.15, 0.12, 0.03, "Wrap", "cord", turns=5),
              dict(chain=[(0, 0, 0.13), (0.15, 0, 0.55), (0.10, 0, 0.95), (0.25, 0, 1.25)], link=0.055, wire=0.009),
              dict(lathe=[(0.0, 0.025), (0.06, 0.028), (0.10, 0.0)], m="Steel", n=8, role="fitting", dir=(0.4, 0, 1), at=(0.26, 0, 1.27)),
              S([(0.30, 1.36), (0.36, 1.52), (0.28, 1.66), (0.16, 1.64)], taper(0.035, 0.004), taper(0.014, 0.003), 0.012, sharp=(True, False), per=4)],
    ),
    # A rope dart: a long rope with a steel dart at one end and a ring at the other.
    "rope-dart": dict(
        family="flails", build="hafted", hands="light",
        head=[dict(torus=(0.0, 0.0, -0.05), major=0.04, minor=0.008, m="DarkIron", plane="YZ", n=12),
              dict(cord=[(0, 0, 0.0), (0.12, 0, 0.35), (0.0, 0, 0.75), (-0.15, 0, 0.55), (-0.05, 0, 0.30), (0.20, 0, 0.55), (0.30, 0, 1.00)], r=0.010, m="Cloth"),
              dict(lathe=[(0.0, 0.0), (0.01, 0.022), (0.05, 0.02), (0.10, 0.04), (0.24, 0.0)], m="Steel", n=4, faceted=True, role="edge", dir=(0.2, 0, 1), at=(0.30, 0, 1.0))],
    ),
    # A traveling kettle: a cast-iron pot with a lid, swung on a chain from a handle.
    "traveling-kettle": dict(
        family="flails", build="hafted", hands="light",
        head=[handle(-0.12, 0.14, 0.03, "Wood", "plain"),
              dict(chain=[(0, 0, 0.15), (0.03, 0, 0.35), (0.08, 0, 0.48)], link=0.05, wire=0.009),
              dict(tube=[(-0.06, 0, 0.62), (0.08, 0, 0.50), (0.22, 0, 0.62)], per=3, r=0.007, m="DarkIron"),
              dict(lathe=[(0.50, 0.0), (0.51, 0.08), (0.55, 0.11), (0.66, 0.12), (0.70, 0.11), (0.72, 0.06), (0.75, 0.0)], m="DarkIron", n=14, role="head", x=0.08)],
    ),
    # A stingchuck: a clay pot of something stinging on a cord, thrown and hauled back.
    "stingchuck": dict(
        family="flails", build="hafted", hands="light",
        head=[dict(torus=(0.0, 0.0, -0.04), major=0.045, minor=0.01, m="Cloth", plane="YZ", n=12),
              dict(cord=[(0, 0, 0.0), (0.10, 0, 0.30), (0.05, 0, 0.62)], r=0.009, m="Cloth"),
              dict(lathe=[(0.62, 0.02), (0.66, 0.035), (0.74, 0.09), (0.84, 0.10), (0.92, 0.07), (0.96, 0.0)], m="Stone", n=12, role="head", x=0.05)],
    ),
    # A garrote: a length of wire between two wooden toggles.
    "garrote": dict(
        family="flails", build="hafted", hands="two",
        head=stick(-0.08, 0.08, 0.022, "Wood", rot=(0, 90, 0))
             + [dict(tube=[(0, 0, 0.0), (0.10, 0, 0.30), (0.05, 0, 0.60), (0.18, 0, 0.82)], per=4, r=0.004, m="Steel", n=4, role="edge")]
             + stick(-0.08, 0.08, 0.022, "Wood", at=(0.18, 0, 0.84), rot=(0, 90, 0)),
    ),
})


# --- bows --------------------------------------------------------------------------------------

RECIPES.update({
    # A longbow: as tall as its archer, a deep D of yew when strung, horn nocks.
    "longbow": dict(family="bows", build="bow", hands="bow", reach=2.05, belly=0.62, thick=0.052, recurve=0.0, ears="Horn"),
    # A composite longbow: wood backed and horn bellied, its tips recurving past the string.
    "composite-longbow": dict(family="bows", build="bow", hands="bow", reach=1.75, belly=0.48, thick=0.044, recurve=0.85, contact=0.80,
                              belly_material="Horn", ears="Bone"),
    # A composite shortbow: short, horn-bellied, strongly recurved.
    "composite-shortbow": dict(family="bows", build="bow", hands="bow", reach=1.15, belly=0.36, thick=0.038, recurve=1.0, contact=0.78,
                               belly_material="Horn", ears="Bone"),
    # A horse bow: short and deeply recurved, long stiff ears thrown forward, bone grip plates.
    "horse-bow": dict(family="bows", build="bow", hands="bow", reach=1.00, belly=0.32, thick=0.036, recurve=1.7, contact=0.70,
                      belly_material="Horn", ears="Bone", grip_material="Bone"),
    # An orc hornbow: a heavy, crude bow built of horn, thick-limbed, its tips capped in bone.
    "orc-hornbow": dict(family="bows", build="bow", hands="bow", reach=1.35, belly=0.42, thick=0.058, recurve=0.6, contact=0.80,
                        material="Horn", ears="Bone", grip_material="Cloth",
                        parts=[dict(spike=((0, 0.30, sz * 1.28), (0, 0.18, sz * 1.48)), r=0.03, m="Bone", n=5) for sz in (1, -1)]),
    # A reflex bow: its limbs bent so far back unstrung that strung it still sweeps forward hard.
    "reflex-bow": dict(family="bows", build="bow", hands="bow", reach=1.20, belly=0.34, thick=0.038, recurve=1.45, contact=0.74,
                       belly_material="Horn", ears="Horn"),
    # A thorn bow: a self bow of thorny wood, the thorns left on its back.
    "thorn-bow": dict(family="bows", build="bow", hands="bow", reach=1.25, belly=0.42, thick=0.04, recurve=0.0, thorns=12, ears="Wood"),
    # A tube arrow shooter: a bamboo tube to loose short arrows through, an arrow in it.
    "tube-arrow-shooter": dict(
        family="bows", build="hafted", hands="light",
        parts=[dict(lathe=[(-0.40, 0.032), (-0.39, 0.036), (0.42, 0.036), (0.43, 0.032), (0.43, 0.026), (-0.40, 0.026)], m="Wood", n=10, role="haft"),
               ] + [dict(lathe=[(z - 0.015, 0.037), (z, 0.04), (z + 0.015, 0.037)], m="Cloth", n=10, role="fitting") for z in (-0.25, 0.0, 0.25)]
              + [dict(lathe=[(0.30, 0.0), (0.31, 0.009), (0.62, 0.009), (0.63, 0.016), (0.70, 0.0)], m="Wood", n=6, role="edge"),
                 dict(torus=(0.0, 0.0, -0.46), major=0.05, minor=0.008, m="Cloth", plane="YZ", n=12)],
    ),
})


# --- crossbows ----------------------------------------------------------------------------------

def magazine(z0, z1, depth, width=0.05, lever=True, m="Wood"):
    """A repeating crossbow's magazine: a box of bolts riding on top of the stock, and the
    lever that cocks and loads it, hinged at its front and reaching back over the stock."""
    y = -depth - 0.03
    out = [dict(prism=[(z0, width, 0.05, 0, y - 0.05), (z1, width, 0.05, 0, y - 0.05)], m=m, role="fitting", square=0.4),
           dict(prism=[(z0 + 0.02, width * 0.7, 0.012, 0, y - 0.106), (z1 - 0.02, width * 0.7, 0.012, 0, y - 0.106)], m="DarkIron", role="fitting", square=0.4)]
    if lever:
        out.append(dict(tube=[(width + 0.012, y - 0.04, z1 - 0.02), (width + 0.03, y - 0.20, z1 - 0.25), (width + 0.03, y - 0.28, z0 + 0.05), (width + 0.03, y - 0.26, z0 - 0.15)],
                        per=3, r=0.014, m="Wood", role="fitting"))
    return out


def crank(z, depth, width, m="DarkIron", windlass=False):
    """A cranequin on the stock's side: a gear case, a long crank arm and its knob; or a
    windlass of two cranks across the butt."""
    if windlass:
        return [dict(lathe=[(-0.17, 0.0), (-0.165, 0.035), (0.165, 0.035), (0.17, 0.0)], m=m, n=10, role="fitting", rot=(0, 90, 0), at=(0, depth * 0.3, z)),
                dict(tube=[(0.17, depth * 0.3, z), (0.17, depth * 0.3 + 0.26, z + 0.06), (0.26, depth * 0.3 + 0.26, z + 0.06)], r=0.016, m=m),
                dict(tube=[(-0.17, depth * 0.3, z), (-0.17, depth * 0.3 - 0.26, z - 0.06), (-0.26, depth * 0.3 - 0.26, z - 0.06)], r=0.016, m=m)]
    return [dict(prism=[(z - 0.12, 0.05, 0.09, width + 0.045, 0.0), (z + 0.12, 0.05, 0.09, width + 0.045, 0.0)], m=m, role="fitting", square=0.6),
            dict(lathe=[(-0.02, 0.0), (-0.015, 0.075), (0.015, 0.075), (0.02, 0.0)], m=m, n=12, role="fitting", rot=(0, 90, 0), at=(width + 0.10, 0.0, z)),
            dict(tube=[(width + 0.12, 0.0, z), (width + 0.14, 0.20, z + 0.04), (width + 0.14, 0.32, z + 0.16)], per=2, r=0.015, m=m),
            dict(lathe=[(-0.045, 0.0), (-0.04, 0.024), (0.04, 0.024), (0.045, 0.0)], m="Wood", n=8, rot=(90, 0, 0), at=(width + 0.14, 0.36, z + 0.16))]


RECIPES.update({
    # A heavy crossbow: a long, deep stock, a steel prod, a stirrup, a windlass at the butt.
    "heavy-crossbow": dict(family="crossbows", build="crossbow", hands="crossbow", stock=(-0.65, 1.75, 0.10, 0.07),
                           prod=(1.60, 0.92, 0.20, 0.044), prod_material="Steel", stirrup_r=0.09,
                           parts=crank(-0.48, 0.10, 0.07, windlass=True)),
    # A hand crossbow: a pistol of a crossbow, small enough for one hand.
    "hand-crossbow": dict(family="crossbows", build="crossbow", hands="light", stock=(-0.05, 0.78, 0.045, 0.036), butt="pistol",
                          prod=(0.64, 0.40, 0.08, 0.022), stirrup=False, bolt=0.30),
    # A repeating light crossbow: a magazine of bolts on top and a lever to work it.
    "repeating-light-crossbow": dict(family="crossbows", build="crossbow", hands="crossbow", stock=(-0.55, 1.45, 0.08, 0.06),
                                     prod=(1.30, 0.70, 0.16, 0.035), stirrup=False, bolt=False, parts=magazine(0.40, 1.15, 0.08)),
    # A repeating heavy crossbow: bigger in every part, steel-prodded.
    "repeating-heavy-crossbow": dict(family="crossbows", build="crossbow", hands="crossbow", stock=(-0.65, 1.75, 0.10, 0.07),
                                     prod=(1.60, 0.90, 0.20, 0.044), prod_material="Steel", stirrup=False, bolt=False, parts=magazine(0.40, 1.40, 0.10, 0.06)),
    # A repeating hand crossbow: the hand crossbow with a little magazine and lever.
    "repeating-hand-crossbow": dict(family="crossbows", build="crossbow", hands="light", stock=(-0.05, 0.80, 0.045, 0.036), butt="pistol",
                                    prod=(0.66, 0.40, 0.08, 0.022), stirrup=False, bolt=False, parts=magazine(0.25, 0.62, 0.045, 0.034)),
    # A double crossbow: two prods one above the other on a deep stock, two bolts laid.
    "double-crossbow": dict(family="crossbows", build="crossbow", hands="crossbow", stock=(-0.60, 1.55, 0.10, 0.07),
                            prod=(1.25, 0.78, 0.17, 0.038), prods=[0.0, 0.22], bolts_each=True),
    # A light crank crossbow: a light crossbow spanned by a cranequin on its side.
    "light-crank-crossbow": dict(family="crossbows", build="crossbow", hands="crossbow", stock=(-0.55, 1.45, 0.085, 0.062),
                                 prod=(1.30, 0.74, 0.18, 0.036), prod_material="Steel", parts=crank(0.0, 0.085, 0.062)),
    # A heavy crank crossbow: the heavy crossbow with a cranequin.
    "heavy-crank-crossbow": dict(family="crossbows", build="crossbow", hands="crossbow", stock=(-0.65, 1.75, 0.10, 0.07),
                                 prod=(1.60, 0.92, 0.20, 0.046), prod_material="Steel", stirrup_r=0.09, parts=crank(-0.05, 0.10, 0.07)),
    # A gastraphetes: the belly bow — a long stock with a crescent to brace against the belly,
    # a horn-backed prod.
    "gastraphetes": dict(family="crossbows", build="crossbow", hands="crossbow", stock=(-0.55, 1.65, 0.075, 0.055), butt="belly",
                         prod=(1.45, 0.90, 0.26, 0.04), prod_material="Horn", stirrup=False),
    # An underwater light crossbow: a light crossbow whose bolt runs in a tube, its prod steel.
    "underwater-light-crossbow": dict(family="crossbows", build="crossbow", hands="crossbow", stock=(-0.55, 1.45, 0.08, 0.06),
                                      prod=(1.30, 0.64, 0.12, 0.034), prod_material="Steel", stirrup=False,
                                      parts=[dict(lathe=[(0.40, 0.035), (0.42, 0.045), (1.55, 0.045), (1.58, 0.05), (1.62, 0.05)], m="Bronze", n=12, role="fitting", at=(0, -0.13, 0))]),
    # An underwater heavy crossbow: the same, heavier.
    "underwater-heavy-crossbow": dict(family="crossbows", build="crossbow", hands="crossbow", stock=(-0.65, 1.75, 0.10, 0.07),
                                      prod=(1.60, 0.80, 0.15, 0.042), prod_material="Steel", stirrup=False,
                                      parts=[dict(lathe=[(0.45, 0.04), (0.47, 0.055), (1.85, 0.055), (1.88, 0.06), (1.93, 0.06)], m="Bronze", n=12, role="fitting", at=(0, -0.155, 0))]),
    # A dwarven light pelletbow: a stout steel-prodded bow that throws iron pellets from a pouch,
    # fed from a hopper on top.
    "dwarven-light-pelletbow": dict(family="crossbows", build="crossbow", hands="crossbow", stock=(-0.50, 1.30, 0.09, 0.07),
                                    prod=(1.15, 0.66, 0.14, 0.04), prod_material="Steel", draw=0.0, bolt=False, stirrup=False,
                                    parts=[dict(prism=[(0.55, 0.06, 0.06, 0, -0.17), (0.85, 0.07, 0.07, 0, -0.18)], m="DarkIron", role="fitting", square=0.4),
                                           dict(lathe=[(-0.04, 0.0), (-0.03, 0.04), (0.03, 0.04), (0.04, 0.0)], m="Wrap", n=10, role="fitting", rot=(90, 0, 0), at=(0, -0.03, 1.01))]),
    # A dwarven heavy pelletbow: the same, bigger, with a windlass.
    "dwarven-heavy-pelletbow": dict(family="crossbows", build="crossbow", hands="crossbow", stock=(-0.60, 1.55, 0.11, 0.08),
                                    prod=(1.38, 0.80, 0.17, 0.05), prod_material="Steel", draw=0.0, bolt=False, stirrup=False,
                                    parts=[dict(prism=[(0.60, 0.07, 0.07, 0, -0.20), (1.0, 0.085, 0.085, 0, -0.21)], m="DarkIron", role="fitting", square=0.4),
                                           dict(lathe=[(-0.05, 0.0), (-0.04, 0.05), (0.04, 0.05), (0.05, 0.0)], m="Wrap", n=10, role="fitting", rot=(90, 0, 0), at=(0, -0.03, 1.21))]
                                          + crank(-0.42, 0.11, 0.08, windlass=True)),
    # A stonebow: a crossbow for stones — a bent stock to clear the shot, a double string with a
    # pouch, and a sighting fork at the front.
    "stonebow": dict(family="crossbows", build="crossbow", hands="crossbow", stock=(-0.55, 1.35, 0.075, 0.055),
                     prod=(1.25, 0.70, 0.15, 0.034), bolt=False, stirrup=False,
                     parts=[dict(lathe=[(-0.035, 0.0), (-0.03, 0.035), (0.03, 0.035), (0.035, 0.0)], m="Wrap", n=10, role="fitting", rot=(90, 0, 0), at=(0, -0.03, 0.62)),
                            dict(tube=[(-0.70, -0.03, 1.11), (0, -0.03, 0.62), (0.70, -0.03, 1.11)], r=0.006, m="Cloth", n=4, role="string"),
                            dict(ball=(0, -0.03, 0.62), r=0.025, m="Stone"),
                            dict(tube=[(-0.04, -0.10, 1.40), (-0.04, -0.20, 1.40)], r=0.007, m="DarkIron"),
                            dict(tube=[(0.04, -0.10, 1.40), (0.04, -0.20, 1.40)], r=0.007, m="DarkIron")]),
    # A heavy wrist launcher: a bracer up the forearm with a small steel prod and a bolt on top.
    "heavy-wrist-launcher": dict(family="crossbows", build="hafted", hands="light", pointed=True,
                                 parts=[dict(prism=[(-0.30, 0.055, 0.05), (-0.28, 0.06, 0.056), (0.20, 0.06, 0.056), (0.22, 0.055, 0.05)], m="Wrap", role="grip", square=0.8),
                                        dict(prism=[(-0.20, 0.064, 0.012, 0, -0.06), (0.24, 0.05, 0.012, 0, -0.06)], m="DarkIron", role="fitting", square=0.4),
                                        dict(tube=[(-0.26, -0.07, 0.20), (0, -0.075, 0.24), (0.26, -0.07, 0.20)], per=3, r=0.016, m="Steel", role="limb"),
                                        dict(tube=[(-0.26, -0.075, 0.20), (0, -0.075, 0.10), (0.26, -0.075, 0.20)], r=0.005, m="Cloth", n=4, role="string"),
                                        dict(lathe=[(0.08, 0.0), (0.09, 0.009), (0.36, 0.009), (0.37, 0.015), (0.44, 0.0)], m="Wood", n=6, role="edge", at=(0, -0.085, 0))]
                                       + [dict(prism=[(z - 0.02, 0.064, 0.06), (z + 0.02, 0.064, 0.06)], m="DarkIron", role="fitting", square=0.8) for z in (-0.22, 0.12)]),
})


# --- firearms ----------------------------------------------------------------------------------
# Built standing, muzzle up +Z, top toward -Y, trigger and grip toward +Y; the exporter lays them
# forward. A barrel's axis sits at y = -(radius + 0.02).

def at_muzzle(specs, z, rot=(0, 0, -90), y=0.0):
    """Head specs built at the origin, turned and moved to hang under a barrel near its muzzle."""
    out = []
    for sp in specs:
        sp = dict(sp)
        sp["at"] = (0.0, y, z)
        sp["rot"] = rot
        out.append(sp)
    return out


def cylinder(z0, length, r, br, m="DarkIron", n=6):
    yb = -br - 0.02
    return [dict(lathe=[(z0, 0.0), (z0 + 0.003, r * 0.9), (z0 + 0.012, r), (z0 + length - 0.012, r), (z0 + length - 0.003, r * 0.9), (z0 + length, 0.0)],
                 m=m, n=n, faceted=True, role="head", at=(0, yb + br * 0.35, 0))]


def ramrod(z0, z1, br, m="Wood"):
    return [dict(tube=[(0, -br * 0.1 + 0.012, z0), (0, -br * 0.1 + 0.012, z1)], r=0.007, m=m, n=5, role="fitting")]


def vial(z, br, length=0.14, r=0.026):
    yb = -br - 0.02
    return [dict(lathe=[(z, 0.0), (z + 0.004, r), (z + length - 0.004, r), (z + length, 0.0)], m="Radium", n=10, role="fitting", at=(0, yb - br - r - 0.005, 0)),
            dict(lathe=[(z - 0.012, r * 1.25), (z + 0.006, r * 1.25)], m="Bronze", n=10, role="fitting", at=(0, yb - br - r - 0.005, 0)),
            dict(lathe=[(z + length - 0.006, r * 1.25), (z + length + 0.012, r * 1.25)], m="Bronze", n=10, role="fitting", at=(0, yb - br - r - 0.005, 0))]


PISTOL = dict(family="firearms", build="firearm", hands="one", barrel=(0.10, 0.78, 0.022), stock="pistol", grip_len=0.42, forend=0.8, lock="flint")
LONGARM = dict(family="firearms", build="firearm", hands="two", barrel=(0.10, 2.10, 0.024), stock="long", stock_len=0.95, forend=0.85, lock="flint",
               bands=[0.8, 1.45], parts=ramrod(0.25, 2.0, 0.024))

RECIPES.update({
    # A pistol: a flintlock with a long barrel, a full stock and a capped butt.
    "pistol": PISTOL,
    # A coat pistol: a pocket flintlock, short-barrelled and small.
    "coat-pistol": dict(PISTOL, hands="light", barrel=(0.06, 0.34, 0.017), grip_len=0.26, forend=0.7),
    # A double-barrelled pistol: two barrels side by side, two locks.
    "double-barreled-pistol": dict(PISTOL, barrel=(0.10, 0.74, 0.020), barrels=(2, 0.024, "side"), forend=0.6),
    # A dragon pistol: a short brass barrel flaring to a bell like a dragon's open mouth.
    "dragon-pistol": dict(PISTOL, barrel=(0.10, 0.62, 0.022), flare=0.07, barrel_material="Bronze", forend=0.5),
    # A dragoon pistol: a long horse pistol, brass-banded.
    "dragoon-pistol": dict(PISTOL, barrel=(0.10, 1.00, 0.023), bands=[0.55], forend=0.85),
    # A paddle-foot pistol: a duck's foot — four barrels splayed out from one breech.
    "paddle-foot-pistol": dict(PISTOL, barrel=(0.10, 0.62, 0.018), barrels=(4, 14, "fan"), forend=0.3),
    # A pepperbox: a cluster of six barrels turning round a central pin.
    "pepperbox": dict(PISTOL, barrel=(0.10, 0.56, 0.018), barrels=(6, 0.036, "ring"), forend=0.0, lock="percussion"),
    # A breech-loader: a pistol whose breech opens on a hinged block, the lever on top.
    "breech-loader": dict(PISTOL, barrel=(0.16, 0.80, 0.022), forend=0.7,
                          parts=[dict(prism=[(0.05, 0.034, 0.034, 0, -0.04), (0.17, 0.034, 0.034, 0, -0.04)], m="DarkIron", role="fitting", square=0.5),
                                 dict(tube=[(0.0, -0.08, 0.15), (0.0, -0.10, 0.05), (0.0, -0.09, -0.02)], per=2, r=0.009, m="DarkIron")]),
    # A dagger pistol: a dagger with a short barrel along its blade, held and used as a dagger.
    "dagger-pistol": dict(
        family="firearms", build="straight", hands="light", metal="Steel", fittings="DarkIron", pointed=False,
        blade=dict(base=0.225, tip=0.78, width=0.048, thick=0.02, section="diamond", taper=0.0, point=1.0, curve=0.92),
        guard=dict(kind="cross", z=0.218, span=0.12, height=0.016, depth=0.03, ends="knob", knob=0.8),
        grip=dict(bottom=-0.06, top=0.21, radius=0.032, style="spiral", turns=6),
        pommel=dict(kind="ball", radius=0.038, height=0.07),
        parts=[dict(lathe=[(0.20, 0.016), (0.24, 0.016), (0.60, 0.013), (0.61, 0.016), (0.62, 0.008)], m="DarkIron", n=10, role="head", at=(0, -0.042, 0)),
               dict(prism=[(0.14, 0.006, 0.02, 0.03, -0.03), (0.24, 0.006, 0.02, 0.03, -0.03)], m="DarkIron", role="fitting", square=0.5)],
        fx=((0, -0.042, 0.24), (0, -0.042, 0.62)),
    ),
    # A sword cane pistol: a walking cane with a barrel down its length and a crook for a grip.
    "sword-cane-pistol": dict(
        family="firearms", build="hafted", hands="one", pointed=False,
        parts=[dict(lathe=[(-0.10, 0.03), (0.0, 0.032), (1.70, 0.026), (1.72, 0.022)], m="Wood", n=10, role="haft"),
               dict(lathe=[(1.62, 0.028), (1.64, 0.03), (1.76, 0.028), (1.77, 0.012)], m="Bronze", n=10, role="head"),
               dict(tube=[(0, 0, -0.08), (-0.02, 0, -0.14), (-0.08, 0, -0.17), (-0.14, 0, -0.14), (-0.15, 0, -0.08)], per=3, r=0.032, m="Wood", n=8, role="grip"),
               dict(lathe=[(-0.12, 0.034), (-0.09, 0.036), (-0.06, 0.034)], m="Bronze", n=10, role="fitting"),
               dict(prism=[(-0.02, 0.006, 0.014, 0.034, 0), (0.06, 0.006, 0.014, 0.034, 0)], m="DarkIron", role="fitting", square=0.5)],
        fx=((0, 0, 1.0), (0, 0, 1.77)),
    ),
    # A buckler gun: a small steel buckler with a gun barrel through its middle, its grip behind.
    "buckler-gun": dict(
        family="firearms", build="firearm", hands="one", barrel=(-0.10, 0.62, 0.022), stock="none", lock="flint", guard=False, hold=(0, 0.0, 0.12),
        parts=[dict(lathe=[(0.22, 0.0), (0.22, 0.30), (0.235, 0.33), (0.255, 0.325), (0.29, 0.25), (0.315, 0.13), (0.325, 0.0)], m="Steel", n=24, role="fitting", at=(0, -0.042, 0)),
               dict(lathe=[(0.31, 0.03), (0.33, 0.035), (0.35, 0.0)], m="DarkIron", n=10, role="fitting", at=(0, -0.042, 0)),
               dict(tube=[(-0.10, 0.0, 0.12), (0.10, 0.0, 0.12)], r=0.02, m="Wrap", n=8, role="grip")],
    ),
    # A revolver: a revolving cylinder behind an octagonal barrel, a percussion hammer, a curved grip.
    "revolver": dict(PISTOL, barrel=(0.20, 0.66, 0.018), octagonal=True, forend=0.0, lock="percussion", grip_len=0.38,
                     parts=cylinder(0.07, 0.13, 0.05, 0.018), hold=(0, 0.13, 0.0)),
    # A radium pistol: a revolver of the advanced kind, a glowing green vial of radium above it.
    "radium-pistol": dict(PISTOL, barrel=(0.20, 0.62, 0.020), forend=0.0, lock="percussion", grip_len=0.38, metal="Steel",
                          parts=cylinder(0.07, 0.13, 0.05, 0.020) + vial(0.24, 0.020), hold=(0, 0.13, 0.0)),
    # A musket: a long smoothbore, full-stocked, brass-banded, a ramrod under its barrel.
    "musket": LONGARM,
    # A dragoon musket: a cavalry carbine, short-barrelled, with a saddle ring.
    "dragoon-musket": dict(LONGARM, barrel=(0.10, 1.55, 0.024), bands=[0.9], parts=ramrod(0.25, 1.45, 0.024)
                           + [dict(torus=(0.045, 0.02, -0.05), major=0.05, minor=0.009, m="Bronze", plane="YZ", n=12)]),
    # A double-barrelled musket: two long barrels side by side.
    "double-barreled-musket": dict(LONGARM, barrel=(0.10, 1.95, 0.022), barrels=(2, 0.026, "side"), bands=[1.2]),
    # A blunderbuss: a short brass barrel flaring to a wide bell, a short stock.
    "blunderbuss": dict(LONGARM, barrel=(0.10, 1.20, 0.028), flare=0.085, barrel_material="Bronze", stock_len=0.85, forend=0.7, bands=[], parts=[]),
    # A culverin: a heavy hand cannon, a thick barrel on a plain wooden tiller, a hook to rest it.
    "culverin": dict(LONGARM, barrel=(0.05, 2.20, 0.055), octagonal=True, stock_len=0.80, forend=0.6, bands=[0.7, 1.4], lock="none",
                     parts=[S([(0.0, 0.0), (0.18, 0.0), (0.22, -0.06)], 0.03, 0.03, 0.025, m="DarkIron", sharp=(False, False), per=2, tip=None, at=(0, 0, 1.6), rot=(0, 0, -90)),
                            dict(tube=[(0.07, -0.10, -0.02), (0.07, -0.13, 0.05), (0.07, -0.12, 0.12)], per=2, r=0.012, m="DarkIron")]),
    # A cylinder rifle: a rifle fed by a revolving cylinder.
    "cylinder-rifle": dict(LONGARM, barrel=(0.25, 2.10, 0.022), octagonal=True, lock="percussion", bands=[1.4],
                           parts=cylinder(0.10, 0.15, 0.055, 0.022) + ramrod(0.4, 2.0, 0.022)),
    # A double hackbut: two heavy barrels side by side, a hook beneath to brace on a wall.
    "double-hackbut": dict(LONGARM, barrel=(0.10, 2.00, 0.03), barrels=(2, 0.034, "side"), bands=[0.8, 1.5],
                           parts=[S([(0.0, 0.0), (0.16, 0.0), (0.20, -0.05)], 0.03, 0.03, 0.022, m="DarkIron", sharp=(False, False), per=2, tip=None, at=(0, 0, 1.30), rot=(0, 0, -90))]),
    # An air repeater: a rifle that fires on compressed air from a ball reservoir under its stock,
    # fed from a tube along its barrel.
    "air-repeater": dict(LONGARM, barrel=(0.10, 1.85, 0.020), lock="none", bands=[],
                         parts=[dict(lathe=[(-0.12, 0.0), (-0.11, 0.06), (0.0, 0.10), (0.11, 0.06), (0.12, 0.0)], m="Bronze", n=14, role="fitting", at=(0, 0.17, -0.45)),
                                dict(tube=[(0.035, -0.032, 0.15), (0.035, -0.032, 1.55)], r=0.012, m="Bronze", n=8, role="fitting")]),
    # An axe musket: a musket with an axe blade fixed under its muzzle.
    "axe-musket": dict(LONGARM, barrel=(0.10, 1.85, 0.024), bands=[0.9],
                       parts=at_muzzle([dict(plate=[(-0.04, 0.10), (-0.14, 0.12), (-0.26, 0.20), (-0.30, 0.04), (-0.29, -0.10), (-0.22, -0.18), (-0.10, -0.10), (-0.04, -0.06)],
                                             centre=(-0.04, 0.0), thick=0.03, m="Steel", cheek="DarkIron", role="edge")], 1.62)),
    # A warhammer musket: a musket with a hammer head fixed under its muzzle.
    "warhammer-musket": dict(LONGARM, barrel=(0.10, 1.85, 0.024), bands=[0.9],
                             parts=[dict(prism=[(0.0, 0.045, 0.045), (0.12, 0.05, 0.05), (0.16, 0.06, 0.06), (0.20, 0.055, 0.055)], m="DarkIron", role="head", rot=(-90, 0, 0), at=(0, 0.0, 1.66)),
                                    dict(spike=((0, -0.07, 1.66), (0, -0.07, 1.85)), r=0.022, m="DarkIron")]),
    # A fire lance: a spear with a powder tube bound behind its head, the first firearm.
    "fire-lance": dict(
        family="firearms", build="hafted", hands="two", pointed=False, haft=(-1.30, 2.40, 0.036, 0.032), wraps=[(-0.20, 0.25, 0.04)],
        head=[dict(kind="spear", z=2.40, length=0.45, width=0.07, shape="leaf", size=1.3),
              dict(lathe=[(1.90, 0.0), (1.91, 0.05), (2.40, 0.045), (2.41, 0.03), (2.38, 0.0)], m="Wood", n=10, role="head", at=(0.08, 0, 0))]
             + [dict(lathe=[(z - 0.02, 0.055), (z + 0.02, 0.055)], m="Cloth", n=10, role="fitting", at=(0.08, 0, 0)) for z in (2.0, 2.2, 2.35)],
        fx=((0.08, 0, 1.90), (0.08, 0, 2.41)),
    ),
    # A rifle: a long octagonal rifled barrel, percussion lock, sights.
    "rifle": dict(LONGARM, barrel=(0.10, 2.15, 0.022), octagonal=True, lock="percussion", bands=[1.0, 1.6],
                  parts=ramrod(0.25, 2.0, 0.022) + [dict(prism=[(2.05, 0.004, 0.012, 0, -0.054), (2.09, 0.004, 0.012, 0, -0.054)], m="Steel", role="fitting")]),
    # A shotgun: a single wide-bored barrel, a hinged breech.
    "shotgun": dict(LONGARM, barrel=(0.10, 1.70, 0.032), lock="percussion", forend=0.5, bands=[], parts=[]),
    # A double-barrelled shotgun: two wide barrels side by side.
    "double-barreled-shotgun": dict(LONGARM, barrel=(0.10, 1.65, 0.026), barrels=(2, 0.030, "side"), lock="percussion", forend=0.5, bands=[], parts=[]),
    # A pepperbox rifle: a rifle with a cluster of six turning barrels.
    "pepperbox-rifle": dict(LONGARM, barrel=(0.10, 1.75, 0.018), barrels=(6, 0.042, "ring"), lock="percussion", forend=0.4, bands=[], parts=[]),
    # A radium rifle: an advanced rifle with glowing vials of radium along its barrel.
    "radium-rifle": dict(LONGARM, barrel=(0.10, 2.00, 0.022), lock="percussion", metal="Steel", bands=[1.4], parts=vial(0.45, 0.022, 0.22, 0.03) + vial(0.85, 0.022, 0.22, 0.03)),
    # A thark rifle: a heavy rifle with a broad octagonal barrel, a muzzle brake and a sight tube.
    "thark-rifle": dict(LONGARM, barrel=(0.10, 2.10, 0.03), octagonal=True, lock="percussion", bands=[1.2],
                        parts=[dict(lathe=[(2.08, 0.04), (2.10, 0.045), (2.22, 0.045), (2.24, 0.03)], m="Steel", n=8, faceted=True, role="head", at=(0, -0.05, 0)),
                               dict(lathe=[(0.25, 0.018), (0.27, 0.02), (0.75, 0.02), (0.77, 0.018)], m="DarkIron", n=10, role="fitting", at=(0, -0.11, 0))]),
})


# --- thrown -------------------------------------------------------------------------------------

LENS = [(math.cos(a) * (1.0 if math.cos(a) > 0 else 0.55), math.sin(a) * 0.22 * (1 - abs(math.cos(a)) ** 3 if math.cos(a) > 0 else 1.0)) for a in
        [k * 2 * math.pi / 10 for k in range(10)]]


def star(points, r_out, r_in, m="Steel", thick=0.012):
    pts = []
    for k in range(points * 2):
        a = math.pi / 2 + k * math.pi / points
        r = r_out if k % 2 == 0 else r_in
        pts.append((r * math.cos(a), r * math.sin(a)))
    return dict(plate=pts, centre=(0.0, 0.0), thick=thick, closed=True, m=m, role="edge")


def ringblade(radius, width, m="Steel", n=36):
    """A flat ring sharp on its outer edge: a chakram."""
    pts = [(radius * math.cos(2 * math.pi * k / n), 0.0, radius * math.sin(2 * math.pi * k / n)) for k in range(n)]
    sec = [(-width * 0.5, 0.0), (-width * 0.2, 0.25 * width), (width * 0.2, 0.12 * width), (width * 0.5, 0.0), (width * 0.2, -0.12 * width), (-width * 0.2, -0.25 * width)]
    return dict(tube=pts, r=1.0, closed=True, section=[(u, v) for u, v in sec], m=m, role="edge")


def fletch(z, length=0.12, h=0.035, m="Cloth", r=0.0):
    return [dict(sweep=[(0.0, z), (0.0, z + length)], wl=lambda s, h=h: h * (0.6 + 0.4 * s) * (1 - s) ** 0.3, wr=0.0, t=0.002, m=m, per=3, tip=None, role="fitting",
                 at=(0, 0, 0), rot=(0, 0, a)) for a in (0, 120, 240)]


def javelin(z0=-0.85, z1=2.0, r=0.026, head=None):
    return dict(family="thrown", build="hafted", hands="one", haft=(z0, z1, r, r * 0.85), wraps=[(-0.12, 0.18, r + 0.004, "Cloth", "cord")],
                head=head or [dict(kind="spear", z=z1, length=0.30, width=0.05, shape="leaf", hr=r, socket=0.10, size=1.3)])


def ball_on(c, r, m="Stone", spikes=0):
    return dict(ball=c, r=r, m=m, spikes=spikes, length=0.5, spike_r=0.3)


RECIPES.update({
    # A dart: a short weighted shaft, a pyramid point, three vanes.
    "dart": dict(family="thrown", build="hafted", hands="light",
                 parts=[dict(lathe=[(-0.30, 0.0), (-0.29, 0.012), (0.24, 0.013), (0.25, 0.0)], m="Wood", n=6, role="haft"),
                        dict(lathe=[(0.22, 0.016), (0.26, 0.026), (0.36, 0.0)], m="DarkIron", n=4, faceted=True, role="edge")] + fletch(-0.29, 0.16, 0.04)),
    # A jolting dart: a dart wound with copper round a crystal that carries the shock.
    "jolting-dart": dict(family="thrown", build="hafted", hands="light",
                         parts=[dict(lathe=[(-0.30, 0.0), (-0.29, 0.012), (0.24, 0.013), (0.25, 0.0)], m="Wood", n=6, role="haft"),
                                dict(lathe=[(0.22, 0.016), (0.26, 0.024), (0.36, 0.0)], m="Crystal", n=6, faceted=True, role="edge")]
                               + coil(0.08, 0.22, 0.018, 6, "Bronze", 0.005) + fletch(-0.29, 0.16, 0.04)),
    # A wushu dart: a small heavy leaf of steel with a cloth tassel, no shaft.
    "wushu-dart": dict(family="thrown", build="hafted", hands="light",
                       parts=[dict(kind="spear", z=0.0, length=0.30, width=0.045, shape="leaf", socket=0.0, size=1.0),
                              dict(lathe=[(-0.06, 0.01), (-0.05, 0.016), (0.0, 0.014)], m="Steel", n=8, role="fitting"),
                              dict(torus=(0.0, 0.0, -0.08), major=0.014, minor=0.004, m="Steel", n=10)]
                             + [dict(cord=[(0, 0, -0.09), (dx, 0, -0.20), (dx * 1.5, 0, -0.30)], r=0.006, m="Cloth", n=4, role="fitting") for dx in (-0.03, 0.0, 0.03)]),
    # A shuriken: a flat four-pointed star of steel with a hole through its middle.
    "shuriken": dict(family="thrown", build="hafted", hands="light",
                     parts=[star(4, 0.17, 0.045), dict(torus=(0.0, 0.0, 0.0), major=0.025, minor=0.006, m="DarkIron", n=12)], fx=((-0.17, 0, 0), (0.17, 0, 0))),
    # A chakram: a flat ring of steel sharpened round its outside.
    "chakram": dict(family="thrown", build="hafted", hands="light", parts=[ringblade(0.20, 0.06)], fx=((-0.23, 0, 0), (0.23, 0, 0))),
    # A crystal chakram: the same ring, of glassy crystal.
    "crystal-chakram": dict(family="thrown", build="hafted", hands="light", parts=[ringblade(0.20, 0.07, "Crystal")], fx=((-0.23, 0, 0), (0.23, 0, 0))),
    # A starknife: a ring with four blades standing out of it like a compass rose.
    "starknife": dict(family="thrown", build="hafted", hands="light",
                      parts=[dict(torus=(0.0, 0.0, 0.0), major=0.09, minor=0.016, m="Steel", n=20, sides=6)]
                            + [S([(0.0, 0.08), (0.0, 0.30)], lambda s: 0.04 * (1 - s) ** 0.8 + 0.003, lambda s: 0.04 * (1 - s) ** 0.8 + 0.003, 0.01, per=4, rot=(0, a, 0)) for a in (0, 90, 180, 270)],
                      fx=((-0.3, 0, 0), (0.3, 0, 0))),
    # A boomerang: a flat bent wing of hardwood.
    "boomerang": dict(family="thrown", build="hafted", hands="light",
                      parts=[S([(-0.42, -0.16), (-0.22, 0.06), (0.0, 0.14), (0.22, 0.06), (0.42, -0.16)], 0.05, 0.05, 0.009, m="Wood", sharp=(False, True), per=6, tip=None)]),
    # Bolas: three cords from one knot, a stone weight on each.
    "bolas": dict(family="thrown", build="hafted", hands="light",
                  parts=[dict(cord=[(0, 0, 0), (dx * 0.5, dy * 0.4, 0.25 + dz * 0.5), (dx, dy, 0.5 + dz)], r=0.008, m="Wrap", n=5) for dx, dy, dz in ((-0.20, 0, 0.0), (0.18, 0.05, 0.06), (0.02, -0.06, 0.22))]
                        + [ball_on((dx, dy, 0.5 + dz), 0.055) for dx, dy, dz in ((-0.20, 0, 0.0), (0.18, 0.05, 0.06), (0.02, -0.06, 0.22))]),
    # Dire bolas: more cords, heavier, iron weights.
    "dire-bolas": dict(family="thrown", build="hafted", hands="light",
                       parts=[dict(cord=[(0, 0, 0), (dx * 0.5, dy * 0.4, 0.3 + dz * 0.5), (dx, dy, 0.6 + dz)], r=0.010, m="Wrap", n=5) for dx, dy, dz in ((-0.26, 0, 0.0), (0.24, 0.05, 0.05), (0.02, -0.06, 0.25), (-0.12, 0.08, 0.32))]
                             + [ball_on((dx, dy, 0.6 + dz), 0.07, "DarkIron") for dx, dy, dz in ((-0.26, 0, 0.0), (0.24, 0.05, 0.05), (0.02, -0.06, 0.25), (-0.12, 0.08, 0.32))]),
    # Shoanti bolas: cords weighted with spiked stones.
    "shoanti-bolas": dict(family="thrown", build="hafted", hands="light",
                          parts=[dict(cord=[(0, 0, 0), (dx * 0.5, dy * 0.4, 0.25 + dz * 0.5), (dx, dy, 0.5 + dz)], r=0.008, m="Wrap", n=5) for dx, dy, dz in ((-0.20, 0, 0.0), (0.18, 0.05, 0.06))]
                                + [ball_on((dx, dy, 0.5 + dz), 0.06, "Stone", spikes=7) for dx, dy, dz in ((-0.20, 0, 0.0), (0.18, 0.05, 0.06))]),
    # A hunga-munga: an iron throwing knife of several blades — an angled main blade, a hooked
    # spur and a back spike off one handle.
    "hunga-munga": dict(family="thrown", build="hafted", hands="light",
                        parts=[handle(-0.18, 0.12, 0.026, "Wrap", "cord", turns=5),
                               S([(0.0, 0.12), (0.03, 0.40), (-0.06, 0.58), (-0.22, 0.62)], taper(0.034, 0.004), taper(0.022, 0.004), 0.012, m="DarkIron", per=4),
                               S([(0.01, 0.30), (0.14, 0.36), (0.22, 0.30)], taper(0.026, 0.003), taper(0.012, 0.003), 0.01, m="DarkIron", sharp=(True, False), per=3),
                               S([(-0.01, 0.20), (-0.13, 0.16)], taper(0.02, 0.003), taper(0.02, 0.003), 0.01, m="DarkIron", per=2)]),
    # A kestros: a sling-dart — a short finned shaft with a long iron point.
    "kestros": dict(family="thrown", build="hafted", hands="light",
                    parts=[dict(lathe=[(-0.25, 0.0), (-0.24, 0.016), (0.05, 0.018), (0.08, 0.012)], m="Wood", n=8, role="haft"),
                           dict(lathe=[(0.06, 0.012), (0.08, 0.009), (0.36, 0.006), (0.40, 0.0)], m="DarkIron", n=4, faceted=True, role="edge")]
                          + [dict(sweep=[(0.0, -0.24), (0.0, -0.05)], wl=lambda s: 0.05 * (1 - s), wr=0.0, t=0.004, m="Wood", per=3, tip=None, role="fitting", rot=(0, 0, a)) for a in (0, 90, 180, 270)]),
    # A phaleros: a bronze throwing disc, its rim ground sharp, its face embossed.
    "phaleros": dict(family="thrown", build="hafted", hands="light",
                     parts=[dict(lathe=[(0.02, 0.0), (0.018, 0.08), (0.008, 0.17), (0.0, 0.20), (-0.008, 0.17), (-0.018, 0.08), (-0.02, 0.0)], m="Bronze", n=28, role="edge", rot=(90, 0, 0)),
                            dict(lathe=[(0.018, 0.05), (0.03, 0.03), (0.034, 0.0)], m="Gold", n=16, role="fitting", rot=(90, 0, 0))],
                     fx=((-0.2, 0, 0), (0.2, 0, 0))),
    # An aklys: a short throwing club tied to a long thong, to be hauled back.
    "aklys": dict(family="thrown", build="hafted", hands="light",
                  head=club([(-0.18, 0.0), (-0.17, 0.028), (0.20, 0.034), (0.52, 0.06), (0.62, 0.05), (0.66, 0.0)], n=8, faceted=True)
                       + [dict(torus=(0.0, 0.0, -0.20), major=0.02, minor=0.006, m="DarkIron", plane="YZ", n=10)]
                       + coil(-0.55, -0.22, 0.05, 4, "Wrap", 0.008)),
    # An atlatl: a spear-thrower — a flat stick with a hook at its back end and finger loops, a
    # dart laid along it.
    "atlatl": dict(family="thrown", build="hafted", hands="one",
                   parts=[dict(prism=[(-0.10, 0.026, 0.012), (0.75, 0.018, 0.010)], m="Wood", role="haft", square=0.7),
                          dict(spike=((0.0, 0.0, 0.73), (0.0, -0.06, 0.80)), r=0.014, m="Bone", n=5),
                          dict(torus=(0.03, 0.0, -0.02), major=0.025, minor=0.006, m="Wrap", plane="XZ", n=10),
                          dict(torus=(-0.03, 0.0, -0.02), major=0.025, minor=0.006, m="Wrap", plane="XZ", n=10),
                          dict(lathe=[(-0.10, 0.0), (-0.09, 0.012), (1.40, 0.012), (1.41, 0.0)], m="Wood", n=6, role="haft", at=(0, -0.05, 0)),
                          dict(lathe=[(1.38, 0.016), (1.42, 0.02), (1.55, 0.0)], m="Stone", n=5, faceted=True, role="edge", at=(0, -0.05, 0))]
                         + [dict(f, at=(0, -0.05, 0)) for f in fletch(-0.10, 0.18, 0.04)]),
    # A blowgun: a long cane tube with a mouthpiece and bindings.
    "blowgun": dict(family="thrown", build="hafted", hands="one",
                    parts=[dict(lathe=[(-0.40, 0.024), (-0.39, 0.028), (1.60, 0.022), (1.61, 0.018), (1.61, 0.012), (-0.40, 0.012)], m="Wood", n=10, role="haft"),
                           dict(lathe=[(-0.46, 0.035), (-0.40, 0.032), (-0.38, 0.026)], m="Bone", n=10, role="fitting")]
                          + [dict(lathe=[(z - 0.015, 0.026), (z, 0.03), (z + 0.015, 0.026)], m="Cloth", n=8, role="fitting") for z in (0.2, 0.7, 1.2)],
                    fx=((0, 0, -0.4), (0, 0, 1.61))),
    # A sling: a leather pouch between two cords, a finger loop at one end.
    "sling": dict(family="thrown", build="hafted", hands="light",
                  parts=[dict(torus=(0.0, 0.0, -0.03), major=0.025, minor=0.006, m="Cloth", plane="YZ", n=10),
                         dict(cord=[(0, 0, 0), (-0.04, 0, 0.30), (-0.07, 0, 0.62)], r=0.006, m="Cloth", n=4),
                         dict(cord=[(0, 0, 0), (0.04, 0, 0.30), (0.07, 0, 0.62)], r=0.006, m="Cloth", n=4),
                         dict(lathe=[(0.60, 0.0), (0.61, 0.05), (0.68, 0.07), (0.75, 0.05), (0.76, 0.0)], m="Wrap", n=10, role="head", oval=0.5)]),
    # A halfling double sling: one sling with two pouches, one above the other.
    "halfling-double-sling": dict(family="thrown", build="hafted", hands="light",
                                  parts=[dict(torus=(0.0, 0.0, -0.03), major=0.025, minor=0.006, m="Cloth", plane="YZ", n=10),
                                         dict(cord=[(0, 0, 0), (-0.04, 0, 0.30), (-0.07, 0, 0.62)], r=0.006, m="Cloth", n=4),
                                         dict(cord=[(0, 0, 0), (0.04, 0, 0.30), (0.07, 0, 0.62)], r=0.006, m="Cloth", n=4),
                                         dict(lathe=[(0.40, 0.0), (0.41, 0.04), (0.46, 0.055), (0.51, 0.04), (0.52, 0.0)], m="Wrap", n=10, role="head", oval=0.5),
                                         dict(lathe=[(0.60, 0.0), (0.61, 0.05), (0.68, 0.07), (0.75, 0.05), (0.76, 0.0)], m="Wrap", n=10, role="head", oval=0.5)]),
    # A halfling sling staff: a short staff with a forked top that carries a sling.
    "halfling-sling-staff": dict(family="thrown", build="hafted", hands="two", haft=(-0.55, 0.95, 0.03, 0.028), wraps=[(-0.15, 0.20, 0.034)],
                                 head=[dict(tube=[(0, 0, 0.93), (0.04, 0, 1.05), (0.07, 0, 1.18)], per=2, r=0.022, m="Wood"),
                                       dict(tube=[(0, 0, 0.93), (-0.04, 0, 1.05), (-0.07, 0, 1.18)], per=2, r=0.022, m="Wood"),
                                       dict(cord=[(0.07, 0, 1.17), (0.10, 0, 1.30), (0.05, 0, 1.45)], r=0.006, m="Cloth", n=4),
                                       dict(cord=[(-0.07, 0, 1.17), (-0.06, 0, 1.30), (0.0, 0, 1.45)], r=0.006, m="Cloth", n=4),
                                       dict(lathe=[(-0.04, 0.0), (-0.035, 0.04), (0.035, 0.04), (0.04, 0.0)], m="Wrap", n=10, role="head", oval=0.5, at=(0.025, 0, 1.48))],
                                 fx=((0, 0, 0.9), (0.03, 0, 1.52))),
    # A halfling stitched sling: a sling sewn shut round its stone and swung like a flail.
    "halfling-stitched-sling": dict(family="thrown", build="hafted", hands="light",
                                    parts=[dict(torus=(0.0, 0.0, -0.03), major=0.025, minor=0.006, m="Cloth", plane="YZ", n=10),
                                           dict(cord=[(0, 0, 0), (0.05, 0, 0.30), (0.08, 0, 0.55)], r=0.008, m="Cloth", n=4),
                                           dict(lathe=[(0.52, 0.02), (0.56, 0.06), (0.64, 0.08), (0.72, 0.06), (0.76, 0.0)], m="Wrap", n=10, role="head", x=0.09),
                                           dict(tube=[(0.09 + 0.081 * math.cos(math.radians(a)), 0.081 * math.sin(math.radians(a)) * 0.2, 0.64 + 0.08 * math.sin(math.radians(a))) for a in range(-80, 81, 20)],
                                                r=0.005, m="Cloth", n=4)]),
    # A sling glove: a leather glove with a sling pocket across its palm.
    "sling-glove": dict(family="thrown", build="hafted", hands="light", worn=True,
                        parts=hand("Wrap", 0.12)
                              + [dict(lathe=[(-0.04, 0.0), (-0.03, 0.045), (0.03, 0.05), (0.04, 0.0)], m="Cloth", n=10, role="head", rot=(90, 0, 0), at=(0, 0.07, -0.02)),
                                 dict(cord=[(0.05, 0.07, -0.02), (0.10, 0.04, 0.14), (0.06, 0.0, 0.26)], r=0.006, m="Cloth", n=4)]),
    # A spear sling: a throwing cord with a loop and a short spear to cast with it.
    "spear-sling": dict(family="thrown", build="hafted", hands="light",
                        parts=[dict(lathe=[(-0.10, 0.0), (-0.09, 0.016), (1.05, 0.015), (1.06, 0.0)], m="Wood", n=6, role="haft"),
                               dict(kind="spear", z=1.05, length=0.18, width=0.03, shape="leaf", hr=0.016, socket=0.06, size=1.3),
                               dict(cord=[(0.0, 0.02, 0.45), (0.06, 0.03, 0.30), (0.04, 0.03, 0.05), (0.0, 0.02, -0.05)], r=0.006, m="Wrap", n=4),
                               dict(torus=(0.0, 0.0, -0.10), major=0.03, minor=0.006, m="Wrap", plane="YZ", n=10)]),
    # A grappling hook: four iron flukes curling up from a shank, a ring and a coil of rope.
    "grappling-hook": dict(family="thrown", build="hafted", hands="light",
                           parts=[dict(lathe=[(0.0, 0.02), (0.30, 0.018), (0.32, 0.0)], m="DarkIron", n=8, role="head"),
                                  dict(torus=(0.0, 0.0, -0.04), major=0.04, minor=0.01, m="DarkIron", plane="YZ", n=12)]
                                 + [dict(tube=[(0.0, 0, 0.30), (0.10, 0, 0.32), (0.16, 0, 0.42), (0.13, 0, 0.52)], per=3, r=lambda u: 0.016 * (1 - 0.5 * u), m="DarkIron", role="edge", rot=(0, 0, a)) for a in (0, 90, 180, 270)]
                                 + coil(-0.45, -0.08, 0.06, 5, "Cloth", 0.012)),
    # A net: a weighted net of cord, gathered and sagging, lead weights round its edge.
    "net": dict(family="thrown", build="hafted", hands="light",
                parts=[dict(tube=[(x, 0.06 * math.sin(3 * z), z + 0.10 * math.sin(math.pi * (x + 0.4) / 0.8)) for z in [k * 0.08 for k in range(11)]], r=0.006, m="Cloth", n=4) for x in [-0.4 + j * 0.1 for j in range(9)]]
                      + [dict(tube=[(x, 0.06 * math.sin(3 * z), z + 0.10 * math.sin(math.pi * (x + 0.4) / 0.8)) for x in [-0.4 + j * 0.1 for j in range(9)]], r=0.006, m="Cloth", n=4) for z in [k * 0.08 for k in range(11)]]
                      + [ball_on((x, 0.06 * math.sin(3 * 0.8), 0.8 + 0.10 * math.sin(math.pi * (x + 0.4) / 0.8)), 0.022, "DarkIron") for x in (-0.4, -0.2, 0.0, 0.2, 0.4)],
                fx=None),
    # A javelin: a light throwing spear with a slim leaf head and a cord grip at its balance.
    "javelin": javelin(),
    # An amentum: a javelin with a leather throwing thong looped round its shaft.
    "amentum": dict(javelin(), extra=[dict(cord=[(0.0, 0.03, 0.05), (0.06, 0.04, -0.05), (0.12, 0.03, -0.25), (0.04, 0.03, -0.32), (0.0, 0.03, -0.15)], r=0.007, m="Wrap", n=4)]),
    # A pilum: a wooden shaft, a lead weight, and a long thin iron shank with a small hard point.
    "pilum": dict(family="thrown", build="hafted", hands="one", haft=(-0.85, 1.10, 0.03, 0.028), wraps=[(-0.12, 0.18, 0.034, "Cloth", "cord")],
                  head=[dict(lathe=[(1.00, 0.03), (1.04, 0.05), (1.14, 0.05), (1.18, 0.028)], m="DarkIron", n=10, role="fitting"),
                        dict(lathe=[(1.16, 0.016), (2.05, 0.011), (2.06, 0.022), (2.20, 0.0)], m="DarkIron", n=4, faceted=True, role="edge")]),
    # A stormshaft javelin: a javelin bound with copper coils and tipped with a crystal point.
    "stormshaft-javelin": dict(javelin(head=[dict(kind="spear", z=2.0, length=0.30, width=0.05, shape="lozenge", hr=0.026, socket=0.10, size=1.3, m="Crystal")]),
                               extra=coil(1.4, 1.95, 0.03, 10, "Bronze", 0.006)),
    # A shrillshaft javelin: a javelin with a bone whistle behind its head and feathers.
    "shrillshaft-javelin": dict(javelin(), extra=[dict(lathe=[(1.62, 0.028), (1.65, 0.04), (1.82, 0.036), (1.85, 0.028)], m="Bone", n=10, role="fitting")] + feathers(1.62, 3, 0.18)),
    # A throwing arrow cord: a heavy arrow with a cord looped at its nock to whip it away.
    "throwing-arrow-cord": dict(family="thrown", build="hafted", hands="light",
                                parts=[dict(lathe=[(-0.40, 0.0), (-0.39, 0.012), (0.55, 0.013), (0.56, 0.0)], m="Wood", n=6, role="haft"),
                                       dict(lathe=[(0.53, 0.016), (0.57, 0.024), (0.68, 0.0)], m="DarkIron", n=4, faceted=True, role="edge"),
                                       dict(cord=[(0, 0.015, -0.30), (0.08, 0.02, -0.40), (0.14, 0.02, -0.20), (0.05, 0.02, 0.0)], r=0.006, m="Cloth", n=4)]
                                      + fletch(-0.39, 0.16, 0.04)),
})


# --- worn on the hand and the body --------------------------------------------------------------
# Hand-worn rows set worn=True and are authored the readable way: the fist at the origin with
# its knuckle line along X, striking up +Z, the forearm down -Z, the back of the hand toward -Y.
# The builder turns them into the hand's convention (knuckles along Z, striking forward +Y).

def katar(blade_len=0.42, width=0.07, m="Steel", sides=0):
    """A katar's H-shaped grip — two side bars along the forearm, two cross bars in the fist —
    and a broad triangular blade straight ahead of the knuckles."""
    out = [dict(prism=[(-0.30, 0.008, 0.016, sx * 0.065), (0.06, 0.012, 0.018, sx * 0.065)], m=m, role="fitting", square=0.5) for sx in (-1, 1)]
    out += [dict(tube=[(-0.065, 0, z), (0.065, 0, z)], r=0.018, m="Wrap", n=8, role="grip") for z in (-0.03, -0.09)]
    out += [dict(prism=[(0.05, 0.07, 0.02), (0.08, 0.068, 0.018)], m=m, role="fitting", square=0.5)]
    out += [S([(0.0, 0.07), (0.0, 0.07 + blade_len)], lambda s: width * (1 - s) ** 0.9 + 0.003, lambda s: width * (1 - s) ** 0.9 + 0.003, 0.014, m=m, per=6, bevel=0.45)]
    for k in range(sides):
        sx = 1 if k == 0 else -1
        out.append(S([(sx * 0.05, 0.08), (sx * 0.11, 0.25), (sx * 0.15, 0.40)], taper(0.026, 0.003), taper(0.026, 0.003), 0.01, m=m, per=3))
    return out


def tail(points, m="Wrap"):
    """A kobold's tail sheath: a tapering leather sleeve along the tail with straps."""
    return [dict(tube=points, per=4, r=lambda u: 0.07 * (1 - 0.55 * u), m=m, n=10, role="grip")] + \
           [dict(torus=(0, 0, 0), major=0.07 * (1 - 0.55 * u) + 0.006, minor=0.008, m="DarkIron", plane="XY", n=14, sides=4, at=p, role="fitting")
            for u, p in ((0.15, points[1]), (0.55, points[2]))]


TAIL = [(0.0, 0, -0.10), (0.05, 0, 0.30), (-0.02, 0, 0.65), (0.05, 0, 0.95)]
# A fist's striking line, in the hand's convention: along the knuckles (Z), at their front (+Y).
KNUCKLES = ((0.0, 0.085, -0.06), (0.0, 0.085, 0.06))

RECIPES.update({
    # A gauntlet: a steel glove, plated on the back, a flared cuff.
    "gauntlet": dict(family="worn", build="hafted", hands="light", worn=True, parts=hand("Steel", 0.22, plated=True), fx=KNUCKLES),
    # A spiked gauntlet: the gauntlet with spikes on its knuckles and its back.
    "spiked-gauntlet": dict(family="worn", build="hafted", hands="light", worn=True,
                            parts=hand("Steel", 0.22, plated=True)
                                  + [dict(spike=((x, -0.02, 0.07), (x, -0.03, 0.16)), r=0.014, m="Steel", n=5) for x in (-0.042, -0.014, 0.014, 0.042)]
                                  + [dict(spike=((x, -0.045, z), (x, -0.13, z + 0.02)), r=0.013, m="Steel", n=5) for x, z in ((-0.03, -0.02), (0.03, -0.02), (0.0, -0.17), (0.0, -0.27))]),
    # A cestus: leather straps round the hand with iron bars and studs over the knuckles.
    "cestus": dict(family="worn", build="hafted", hands="light", worn=True,
                   parts=hand("Wrap", 0.16)
                         + [dict(tube=[(-0.06, -0.03, z), (0.06, -0.03, z)], r=0.014, m="DarkIron", n=6, role="head") for z in (0.055, 0.02)]
                         + [dict(spike=((x, -0.04, 0.06), (x, -0.06, 0.10)), r=0.012, m="DarkIron", n=4) for x in (-0.04, 0.0, 0.04)]),
    # Brass knuckles: a bar of brass with four rings for the fingers and a pad for the palm.
    "brass-knuckles": dict(family="worn", build="hafted", hands="light", worn=True,
                           parts=[dict(torus=(x, 0.0, 0.03), major=0.017, minor=0.008, m="Bronze", plane="YZ", n=12, sides=5, role="head") for x in (-0.042, -0.014, 0.014, 0.042)]
                                 + [dict(tube=[(-0.06, 0.0, 0.05), (0.06, 0.0, 0.05)], r=0.012, m="Bronze", n=6, role="head"),
                                    dict(tube=[(-0.05, 0.03, -0.02), (0.0, 0.035, -0.03), (0.05, 0.03, -0.02)], per=3, r=0.014, m="Bronze", n=6, role="grip")]),
    # A rope gauntlet: a hand and forearm wound thick with rope.
    "rope-gauntlet": dict(family="worn", build="hafted", hands="light", worn=True, fx=KNUCKLES,
                          parts=hand("Wrap", 0.16) + coil(-0.30, 0.05, 0.062, 9, "Cloth", 0.012)),
    # Tekko-kagi: a band across the back of the hand from which four long claws curve out past
    # the knuckles.
    "tekko-kagi": dict(family="worn", build="hafted", hands="light", worn=True,
                       parts=hand("Wrap", 0.12)
                             + [dict(prism=[(-0.06, 0.06, 0.008, 0, -0.04), (0.02, 0.06, 0.008, 0, -0.04)], m="DarkIron", role="fitting", square=0.5)]
                             + [S([(x, -0.06), (x * 1.1, 0.12), (x * 1.2, 0.26)], taper(0.010, 0.002), taper(0.010, 0.002), 0.008, m="Steel", per=3, at=(0, -0.05, 0)) for x in (-0.042, -0.014, 0.014, 0.042)]),
    # An emei piercer: a short rod pointed at both ends, worn on a ring round a finger.
    "emei-piercer": dict(family="worn", build="hafted", hands="light", worn=True,
                         parts=[dict(lathe=[(-0.24, 0.0), (-0.16, 0.012), (0.0, 0.014), (0.16, 0.012), (0.24, 0.0)], m="Steel", n=6, faceted=True, role="edge"),
                                dict(torus=(0.0, 0.0, 0.0), major=0.022, minor=0.006, m="Steel", plane="XY", n=12, sides=4)]),
    # A dwarven light axe gauntlet: a gauntlet with a small axe blade standing off its forearm.
    "dwarven-light-axe-gauntlet": dict(family="worn", build="hafted", hands="light", worn=True,
                                       parts=hand("Steel", 0.22, plated=True)
                                             + [dict(plate=[(-0.06, -0.06), (-0.12, -0.04), (-0.20, 0.04), (-0.22, -0.12), (-0.20, -0.27), (-0.12, -0.24), (-0.06, -0.20)], centre=(-0.06, -0.13), thick=0.02,
                                                     m="Steel", cheek="DarkIron", role="edge")]),
    # A dwarven heavy axe gauntlet: the same, the blade bigger and bearded.
    "dwarven-heavy-axe-gauntlet": dict(family="worn", build="hafted", hands="one", worn=True,
                                       parts=hand("Steel", 0.26, plated=True)
                                             + [dict(plate=[(-0.06, -0.02), (-0.14, 0.0), (-0.26, 0.10), (-0.30, -0.10), (-0.27, -0.30), (-0.18, -0.38), (-0.12, -0.28), (-0.06, -0.24)], centre=(-0.06, -0.13),
                                                     thick=0.025, m="Steel", cheek="DarkIron", role="edge")]),
    # A knuckle axe: a crescent axe blade across a knuckle-duster's grip.
    "knuckle-axe": dict(family="worn", build="hafted", hands="light", worn=True,
                        parts=[dict(tube=[(-0.065, 0, -0.03), (0.065, 0, -0.03)], r=0.02, m="Wrap", n=8, role="grip"),
                               dict(prism=[(-0.06, 0.08, 0.016), (0.03, 0.08, 0.016)], m="DarkIron", role="fitting", square=0.5)]
                              + [dict(torus=(x, 0.0, 0.0), major=0.017, minor=0.007, m="DarkIron", plane="YZ", n=12, sides=5) for x in (-0.042, -0.014, 0.014, 0.042)]
                              + [S([(-0.20, 0.10), (-0.12, 0.20), (0.0, 0.24), (0.12, 0.20), (0.20, 0.10)], lambda s: 0.012 + 0.05 * math.sin(math.pi * s), 0.014, 0.012,
                                   sharp=(True, False), per=5, tip="point"),
                                 S([(0.0, 0.03), (0.0, 0.21)], 0.02, 0.02, 0.012, m="DarkIron", sharp=(False, False), tip=None, per=1)]),
    # A thorn bracer: a bracer of bark and leather up the forearm, thorns standing out of it.
    "thorn-bracer": dict(family="worn", build="hafted", hands="light", worn=True,
                         parts=[dict(lathe=[(-0.40, 0.055), (-0.38, 0.062), (-0.06, 0.056), (-0.04, 0.05)], m="Wood", n=12, oval=0.85, role="grip")]
                               + [dict(spike=((0.056 * math.cos(a), 0.05 * math.sin(a), z), (0.11 * math.cos(a), 0.10 * math.sin(a), z + 0.04)), r=0.012, m="Wood", n=4)
                                  for z in (-0.32, -0.20, -0.10) for a in [math.radians(x) for x in (200, 250, 300, 340)]]),
    # A hook hand: a leather socket for the stump and an iron hook where the hand was.
    "hook-hand": dict(family="worn", build="hafted", hands="light", worn=True,
                      parts=[dict(lathe=[(-0.34, 0.06), (-0.32, 0.064), (-0.04, 0.05), (-0.01, 0.03), (0.0, 0.0)], m="Wrap", n=12, oval=0.85, role="grip"),
                             dict(lathe=[(-0.02, 0.03), (0.01, 0.034), (0.03, 0.018)], m="DarkIron", n=10, role="fitting"),
                             dict(tube=[(0.0, 0, 0.02), (0.0, 0, 0.12), (-0.04, 0, 0.20), (-0.10, 0, 0.20), (-0.12, 0, 0.14), (-0.09, 0, 0.10)], per=3, r=lambda u: 0.014 * (1 - 0.6 * u), m="DarkIron", role="edge")]),
    # A punching dagger, the katar: an H of a grip and a broad triangular blade before the fist.
    "punching-dagger": dict(family="worn", build="hafted", hands="light", worn=True, parts=katar(0.42, 0.065)),
    # A tri-bladed katar: the katar with two side blades sprung out from the middle one.
    "tri-bladed-katar": dict(family="worn", build="hafted", hands="light", worn=True, parts=katar(0.40, 0.05, sides=2)),
    # A pata: a gauntlet-sword — a steel gauntlet enclosing hand and forearm, a long straight blade.
    "pata": dict(family="worn", build="hafted", hands="one", worn=True,
                 parts=[dict(lathe=[(-0.48, 0.07), (-0.46, 0.075), (-0.10, 0.07), (0.02, 0.075), (0.07, 0.05), (0.09, 0.03)], m="Steel", n=14, oval=0.8, role="fitting"),
                        dict(blade=dict(base=0.08, tip=1.25, width=0.05, thick=0.016, section="hex", flat=0.5, taper=0.25, point=0.12, curve=0.8, fuller=(0.0, 0.5, 0.016, 0.5)), m="Steel")]),
    # A manople: a gauntlet with a short broad blade and a small blade to each side.
    "manople": dict(family="worn", build="hafted", hands="one", worn=True,
                    parts=[dict(lathe=[(-0.40, 0.07), (-0.38, 0.075), (-0.06, 0.07), (0.02, 0.072), (0.06, 0.05)], m="DarkIron", n=14, oval=0.8, role="fitting"),
                           dict(blade=dict(base=0.05, tip=0.68, width=0.07, thick=0.018, section="lens", taper=0.1, point=0.25, curve=0.9), m="Steel")]
                          + [S([(sx * 0.06, 0.04), (sx * 0.13, 0.14), (sx * 0.16, 0.26)], taper(0.024, 0.003), taper(0.024, 0.003), 0.01, per=3) for sx in (-1, 1)]),
    # A scizore: a hollow tube over the forearm ending in a half-moon blade at the hand.
    "scizore": dict(family="worn", build="hafted", hands="one", worn=True,
                    parts=[dict(lathe=[(-0.45, 0.07), (-0.43, 0.075), (0.0, 0.07), (0.04, 0.05)], m="DarkIron", n=14, oval=0.85, role="fitting"),
                           dict(plate=[(-0.16, 0.06)] + arc_path(0.0, 0.06, 0.16, 180, 0, 10)[1:] + [(0.16, 0.06), (0.0, 0.10)], centre=(0.0, 0.10), thick=0.02, closed=True, m="Steel", role="edge")]),
    # A gnome pincher: a handle working a pair of long clamping jaws.
    "gnome-pincher": dict(family="worn", build="hafted", hands="light",
                          parts=[handle(-0.15, 0.15, 0.03, "Wrap", "spiral"),
                                 dict(prism=[(0.14, 0.04, 0.03), (0.24, 0.04, 0.03)], m="Bronze", role="fitting"),
                                 dict(tube=[(0.02, 0, 0.24), (0.07, 0, 0.40), (0.04, 0, 0.58), (-0.01, 0, 0.62)], per=3, r=0.016, m="DarkIron", role="edge"),
                                 dict(tube=[(-0.02, 0, 0.24), (-0.07, 0, 0.40), (-0.04, 0, 0.58), (0.01, 0, 0.62)], per=3, r=0.016, m="DarkIron", role="edge")]
                                + coil(0.24, 0.32, 0.04, 3, "Bronze", 0.006)),
    # A dwarven boulder helmet: a heavy iron helm faced with a cap of stone for butting.
    "dwarven-boulder-helmet": dict(family="worn", build="hafted", hands="light",
                                   parts=[dict(lathe=[(-0.02, 0.21), (0.0, 0.215), (0.10, 0.20), (0.20, 0.15), (0.26, 0.07), (0.28, 0.0)], m="DarkIron", n=18, role="fitting"),
                                          dict(lathe=[(0.12, 0.19), (0.20, 0.17), (0.28, 0.12), (0.34, 0.0)], m="Stone", n=9, faceted=True, role="head"),
                                          dict(prism=[(-0.06, 0.015, 0.03, 0, -0.205), (0.08, 0.015, 0.03, 0, -0.205)], m="DarkIron", role="fitting")]
                                         + [dict(lathe=[(0.0, 0.05), (0.03, 0.04), (0.05, 0.0)], m="Stone", n=6, faceted=True, role="head", dir=d, at=(0.19 * d[0], 0.19 * d[1], 0.17 + 0.19 * d[2]))
                                            for d in ((0.6, -0.3, 0.7), (-0.6, -0.3, 0.7), (0.0, -0.7, 0.7), (0.3, 0.6, 0.75))]),
    # A barbazu beard: a jagged iron beard of blades hung from a chin strap.
    "barbazu-beard": dict(family="worn", build="hafted", hands="light",
                          parts=[dict(plate=[(-0.14, 0.0), (-0.13, -0.10), (-0.10, -0.24), (-0.07, -0.16), (-0.05, -0.32), (-0.02, -0.20), (0.0, -0.38), (0.02, -0.20), (0.05, -0.32),
                                             (0.07, -0.16), (0.10, -0.24), (0.13, -0.10), (0.14, 0.0)], centre=(0.0, -0.04), thick=0.02, closed=True, m="DarkIron", role="edge"),
                                 dict(tube=[(-0.14, 0.0, 0.0), (-0.12, 0.10, 0.12), (0.0, 0.14, 0.17), (0.12, 0.10, 0.12), (0.14, 0.0, 0.0)], per=3, r=0.012, m="Wrap", n=5)]),
    # A blade boot: a boot with a blade let into its heel.
    "blade-boot": dict(family="worn", build="hafted", hands="light",
                       parts=[dict(lathe=[(-0.02, 0.064), (0.10, 0.062), (0.30, 0.068), (0.34, 0.074)], m="Wrap", n=12, oval=0.85, role="grip"),
                              dict(prism=[(-0.10, 0.07, 0.065), (0.06, 0.07, 0.062)], m="Wrap", role="grip", square=0.8),
                              dict(prism=[(0.0, 0.065, 0.06), (0.24, 0.06, 0.05), (0.30, 0.045, 0.04), (0.32, 0.0, 0.0)], m="Wrap", role="grip", square=0.8, rot=(0, 90, 0), at=(0.0, 0, -0.06)),
                              dict(prism=[(-0.13, 0.08, 0.07), (-0.10, 0.08, 0.07)], m="DarkIron", role="fitting", square=0.8),
                              S([(-0.07, -0.10), (-0.20, -0.14), (-0.30, -0.12)], taper(0.035, 0.003), taper(0.035, 0.003), 0.008, per=3)]),
    # Claw blades: three curved blades a catfolk wears over its claws, on a band round the hand.
    "claw-blades": dict(family="worn", build="hafted", hands="light", worn=True,
                        parts=hand("Wrap", 0.06)
                              + [dict(prism=[(0.03, 0.06, 0.012, 0, -0.03), (0.06, 0.06, 0.012, 0, -0.03)], m="DarkIron", role="fitting", square=0.5)]
                              + [S([(x, 0.06), (x, 0.15), (x * 1.1, 0.22)], taper(0.012, 0.002), taper(0.005, 0.001), 0.006, m="Steel", sharp=(True, False), per=3, at=(0, -0.03, 0)) for x in (-0.03, 0.0, 0.03)]),
    # Kobold tail weapons: a leather sleeve strapped along the tail, the weapon at its tip.
    "long-lash-kobold-tail": dict(family="worn", build="hafted", hands="light",
                                  parts=tail(TAIL) + [lash([(0.05, 0, 0.95), (0.10, 0, 1.30), (0.30, 0, 1.55), (0.50, 0, 1.50)], 0.012, 0.004)]),
    "pounder-kobold-tail": dict(family="worn", build="hafted", hands="light",
                                parts=tail(TAIL) + [dict(lathe=[(0.90, 0.035), (0.96, 0.08), (1.08, 0.10), (1.18, 0.07), (1.22, 0.0)], m="DarkIron", n=10, role="head", x=0.05)]),
    "razored-kobold-tail": dict(family="worn", build="hafted", hands="light",
                                parts=tail(TAIL) + [S([(0.05, 0.92), (0.12, 1.15), (0.06, 1.38), (-0.06, 1.48)], taper(0.06, 0.004), taper(0.02, 0.004), 0.012, sharp=(True, False), per=4)]),
    "spiked-kobold-tail": dict(family="worn", build="hafted", hands="light",
                               parts=tail(TAIL) + [dict(lathe=[(0.92, 0.035), (0.97, 0.05), (1.02, 0.03)], m="DarkIron", n=8, role="head", x=0.05)]
                                     + [dict(spike=((0.05, 0, 0.98), (0.05 + dx, dy, 0.98 + dz)), r=0.016, m="Steel", n=5) for dx, dy, dz in ((0.0, 0.0, 0.18), (0.12, 0, 0.10), (-0.12, 0, 0.10), (0, 0.12, 0.10), (0, -0.12, 0.10))]),
    "sweeper-kobold-tail": dict(family="worn", build="hafted", hands="light",
                                parts=tail(TAIL) + [dict(plate=[(-0.20, 1.05), (-0.22, 1.25), (-0.10, 1.32), (0.10, 1.32), (0.25, 1.25), (0.22, 1.05), (0.05, 0.98)], centre=(0.03, 1.15), thick=0.03,
                                                         closed=True, m="Wood", role="head")]),
    # A ratfolk tailblade: a small curved blade on a ring that slides over the tail's tip.
    "ratfolk-tailblade": dict(family="worn", build="hafted", hands="light",
                              parts=[dict(lathe=[(-0.10, 0.03), (-0.08, 0.034), (0.08, 0.026), (0.10, 0.02)], m="Bronze", n=10, role="grip"),
                                     S([(0.0, 0.09), (0.04, 0.25), (0.0, 0.40), (-0.06, 0.46)], taper(0.03, 0.003), taper(0.012, 0.003), 0.01, sharp=(True, False), per=4)]),
    # A dan bong: a short stick held across the fist, its ends standing out either side.
    "dan-bong": dict(family="worn", build="hafted", hands="light",
                     parts=stick(-0.17, 0.17, 0.022) + [dict(torus=(0.0, 0.0, 0.0), major=0.035, minor=0.006, m="Cloth", plane="XY", n=12)]),
    # A tonfa: a baton with a side handle, held so the baton lies along the forearm.
    "tonfa": dict(family="worn", build="hafted", hands="light", worn=True,
                  parts=[dict(lathe=[(-0.08, 0.0), (-0.075, 0.026), (0.075, 0.026), (0.08, 0.0)], m="Wood", n=10, role="grip", rot=(0, 90, 0)),
                         dict(lathe=[(-0.55, 0.0), (-0.54, 0.03), (0.16, 0.03), (0.17, 0.0)], m="Wood", n=10, role="head", at=(0.0, -0.075, 0.0)),
                         dict(tube=[(0.0, 0.0, 0.0), (0.0, -0.075, 0.0)], r=0.022, m="Wood", n=8)]),
    # A madu: a small round shield with two antelope horns through it pointing both ways.
    "madu": dict(family="shields", build="shield", hands="shield", kind="disc", radius=0.26, dome=0.05, boss="none", face_material="Wrap", fittings="DarkIron",
                 parts=[dict(lathe=[(0.0, 0.03), (0.30, 0.022), (0.52, 0.0)], m="Horn", n=8, role="edge", rot=(0, sx * 90, 0), at=(0, -0.04, 0)) for sx in (1, -1)]
                       + [dict(lathe=[(0.50, 0.0), (0.51, 0.018), (0.58, 0.0)], m="Steel", n=6, role="edge", rot=(0, sx * 90, 0), at=(0, -0.04, 0)) for sx in (1, -1)]),
})


# --- shields ------------------------------------------------------------------------------------

RECIPES.update({
    # A spiked light shield: the light shield's planks with a long spike for a boss.
    "spiked-light-shield": dict(family="shields", build="shield", hands="shield", kind="round", radius=0.60, boards=4, boss="spike", spike=0.32),
    # A spiked heavy shield: a heavy round shield, a long boss spike and spikes round its face.
    "spiked-heavy-shield": dict(family="shields", build="shield", hands="shield", kind="round", radius=0.80, boards=5, boss="spike", spike=0.36, spikes=6, spike_len=0.14),
    # A throwing shield: a small domed disc of steel, its rim ground sharp to be thrown.
    "throwing-shield": dict(family="shields", build="shield", hands="light", kind="disc", radius=0.42, dome=0.08, boss="dome", boss_size=0.2,
                            face_material="DarkIron", fittings="Bronze", sharp_rim=True, rivets=0,
                            parts=[dict(torus=(0, 0.0, 0), major=0.43, minor=0.016, m="Steel", plane="XZ", n=32, sides=4, role="edge"),
                                   dict(torus=(0, -0.05, 0), major=0.26, minor=0.010, m="Bronze", plane="XZ", n=24, sides=4)]),
    # A dwarven war shield: a heavy round shield faced in iron plate with a great central spike.
    "dwarven-war-shield": dict(family="shields", build="shield", hands="shield", kind="disc", radius=0.66, dome=0.12, boss="spike", spike=0.42, boss_size=0.22,
                               face_material="DarkIron", fittings="Steel", rivets=12,
                               parts=[dict(torus=(0, -0.08, 0), major=0.48, minor=0.018, m="Steel", plane="XZ", n=28, sides=6)]),
    # A klar: a Shoanti shield made of a horned lizard's skull, a blade along its top edge.
    "klar": dict(family="shields", build="shield", hands="shield", kind="oval", radius=0.42, aspect=1.35, dome=0.10, boss="none", face_material="Bone", fittings="DarkIron",
                 parts=[S([(-0.30, 0.42), (-0.12, 0.55), (0.12, 0.55), (0.30, 0.42)], lambda s: 0.06 * math.sin(math.pi * s) + 0.008, 0.012, 0.012, sharp=(True, False), per=4, at=(0, -0.10, 0))]
                       + [dict(spike=((sx * 0.20, -0.14, 0.18), (sx * 0.34, -0.30, 0.30)), r=0.035, m="Horn", n=6) for sx in (-1, 1)]),
})


# --- double weapons and staves -------------------------------------------------------------------

RECIPES.update({
    # A bo staff: a long, smooth, slightly tapering staff, nothing on its ends.
    "bo-staff": dict(family="double", build="hafted", hands="two",
                     parts=[dict(lathe=[(-1.55, 0.0), (-1.545, 0.026), (-1.0, 0.033), (0.0, 0.036), (1.0, 0.033), (1.545, 0.026), (1.55, 0.0)], m="Wood", n=10, role="head")]),
    # A hanbo: a half-length staff.
    "hanbo": dict(family="double", build="hafted", hands="light",
                  parts=[dict(lathe=[(-0.62, 0.0), (-0.615, 0.03), (0.0, 0.033), (0.615, 0.03), (0.62, 0.0)], m="Wood", n=10, role="head")]),
    # A lantern staff: a staff with a lantern hung from a hook at its top.
    "lantern-staff": dict(family="double", build="hafted", hands="two", haft=(-1.25, 1.55, 0.036, 0.032),
                          butt=dict(z=-1.25, kind="cap", r=0.036),
                          head=[dict(tube=[(0, 0, 1.50), (0, 0, 1.62), (-0.08, 0, 1.68), (-0.16, 0, 1.62)], per=3, r=0.014, m="DarkIron"),
                                dict(prism=[(1.30, 0.07, 0.07, -0.16), (1.32, 0.075, 0.075, -0.16)], m="DarkIron", role="head", square=0.4),
                                dict(prism=[(1.32, 0.06, 0.06, -0.16), (1.48, 0.06, 0.06, -0.16)], m="Crystal", role="head", square=0.4),
                                dict(prism=[(1.48, 0.075, 0.075, -0.16), (1.50, 0.075, 0.075, -0.16), (1.58, 0.02, 0.02, -0.16)], m="DarkIron", role="head", square=0.4),
                                dict(torus=(-0.16, 0.0, 1.60), major=0.02, minor=0.005, m="DarkIron", plane="XZ", n=10)]
                               + [dict(tube=[(-0.16 + sx * 0.06, sy * 0.06, 1.32), (-0.16 + sx * 0.06, sy * 0.06, 1.48)], r=0.006, m="DarkIron", n=4) for sx in (-1, 1) for sy in (-1, 1)]),
    # A two-bladed sword: a long central grip with a guard and a straight blade at each end.
    "two-bladed-sword": dict(family="double", build="hafted", hands="two",
                             parts=[handle(-0.32, 0.32, 0.036, "Wrap", "spiral", turns=12),
                                    dict(guard=0.34, span=0.17, height=0.018, depth=0.024, ends="knob", knob=0.6, m="DarkIron", role="fitting"),
                                    dict(guard=-0.34, span=0.17, height=0.018, depth=0.024, ends="knob", knob=0.6, m="DarkIron", role="fitting"),
                                    dict(blade=dict(base=0.355, tip=1.70, width=0.066, thick=0.019, section="hex", flat=0.5, taper=0.4, point=0.12, curve=0.8,
                                                    fuller=(0.0, 0.6, 0.018, 0.5)), m="Steel"),
                                    dict(blade=dict(base=0.355, tip=1.70, width=0.066, thick=0.019, section="hex", flat=0.5, taper=0.4, point=0.12, curve=0.8,
                                                    fuller=(0.0, 0.6, 0.018, 0.5)), m="Steel", rot=(180, 0, 0))],
                             fx=((0, 0, -1.70), (0, 0, 1.70))),
    # An orc double axe: a heavy haft with a great axe head at each end.
    "orc-double-axe": dict(family="double", build="hafted", hands="two", haft=(-1.30, 1.30, 0.048, 0.048), wraps=[(-0.45, 0.45, 0.052, "Wrap", "spiral")],
                           head=[dict(kind="axe", z=1.05, shape="crescent", size=1.25, thick=0.05, socket=(0.18, 0.062, 0.056), top=0.16, hr=0.046)],
                           lower=[dict(kind="axe", z=1.05, shape="crescent", size=1.25, thick=0.05, socket=(0.18, 0.062, 0.056), top=0.16, hr=0.046)],
                           fx=((0, 0, -1.40), (0, 0, 1.40))),
    # A dwarven urgrosh: an axe head at one end of the haft, a spearhead at the other.
    "dwarven-urgrosh": dict(family="double", build="hafted", hands="two", haft=(-1.25, 1.25, 0.046, 0.046), wraps=[(-0.40, 0.40, 0.05, "Wrap", "spiral")],
                            head=[dict(kind="axe", z=1.0, shape="dwarven", size=1.2, thick=0.058, socket=(0.17, 0.062, 0.056), back="spike", back_len=0.16, hr=0.044)],
                            lower=[dict(kind="spear", z=1.25, length=0.50, width=0.08, shape="leaf", size=1.3, hr=0.044)],
                            fx=((0, 0, -1.9), (0, 0, 1.3))),
    # A gnome hooked hammer: a hammer at one end, a long curved hook-pick at the other.
    "gnome-hooked-hammer": dict(family="double", build="hafted", hands="two", haft=(-1.0, 1.0, 0.04, 0.04), wraps=[(-0.35, 0.35, 0.044, "Wrap", "spiral")],
                                head=[dict(kind="hammer", z=0.88, length=0.18, face=0.07, back="face", socket=(0.10, 0.075, 0.07))],
                                lower=[dict(kind="pick", z=0.92, length=0.38, drop=0.16, thick=0.022, socket=(0.07, 0.05, 0.045))],
                                fx=((0, 0, -1.0), (0, 0, 1.0))),
    # A weighted spear: a spear whose butt carries an iron weight to strike with.
    "weighted-spear": dict(family="double", build="hafted", hands="two", haft=(-1.40, 1.60, 0.04, 0.036), wraps=[(-0.25, 0.25, 0.044)],
                           head=[dict(kind="spear", z=1.60, length=0.55, width=0.09, shape="leaf", size=1.35)],
                           parts=[dict(ball=(0, 0, -1.48), r=0.10, m="DarkIron"), dict(kind="collar", z=-1.36, r=0.044, h=0.08, m="DarkIron")],
                           fx=((0, 0, -1.6), (0, 0, 2.4))),
    # A double spear: a spearhead at each end of the shaft.
    "double-spear": dict(family="double", build="hafted", hands="two", haft=(-1.50, 1.50, 0.04, 0.04), wraps=[(-0.30, 0.30, 0.044)],
                         head=[dict(kind="spear", z=1.50, length=0.55, width=0.085, shape="leaf", size=1.35)],
                         lower=[dict(kind="spear", z=1.50, length=0.55, width=0.085, shape="leaf", size=1.35)],
                         fx=((0, 0, -2.25), (0, 0, 2.25))),
    # A boarding gaff: a hook at one end to grapple with, a spike at the other.
    "boarding-gaff": dict(family="double", build="hafted", hands="two", haft=(-1.40, 1.40, 0.038, 0.038), wraps=[(-0.25, 0.25, 0.042)],
                          head=[socket(1.25, 1.42, 0.038, "DarkIron"),
                                dict(tube=[(0, 0, 1.40), (0, 0, 1.60), (-0.06, 0, 1.74), (-0.18, 0, 1.76), (-0.25, 0, 1.66), (-0.22, 0, 1.56)], per=3, r=lambda u: 0.02 * (1 - 0.6 * u), m="DarkIron", role="edge")],
                          lower=[dict(kind="spear", z=1.40, length=0.30, width=0.04, shape="needle", size=1.3, m="DarkIron")],
                          fx=((0, 0, -1.9), (0, 0, 1.76))),
    # A monk's spade: a flat spade blade at one end, a crescent moon blade at the other.
    "monks-spade": dict(family="double", build="hafted", hands="two", haft=(-1.40, 1.40, 0.038, 0.038), wraps=[(-0.30, 0.30, 0.042)],
                        head=[socket(1.30, 1.45, 0.038, "Steel"),
                              dict(plate=[(-0.14, 1.45), (-0.17, 1.62), (-0.15, 1.72), (0.0, 1.76), (0.15, 1.72), (0.17, 1.62), (0.14, 1.45), (0.0, 1.50)], centre=(0.0, 1.58), thick=0.02,
                                   closed=True, m="Steel", role="edge")],
                        lower=[socket(1.30, 1.42, 0.038, "Steel"),
                               S(arc_path(0.0, 1.30, 0.20, 160, 20, 10), lambda s: 0.03 + 0.03 * math.sin(math.pi * s), 0.016, 0.012, sharp=(True, False), per=3),
                               S([(0.0, 1.40), (0.0, 1.50)], 0.016, 0.016, 0.014, per=1, sharp=(False, False), tip=None)],
                        fx=((0, 0, -1.8), (0, 0, 1.76))),
    # A taiaha: a Maori staff — a flat blade at one end, at the other a carved head putting out
    # its tongue, a collar of feathers below it.
    "taiaha": dict(family="double", build="hafted", hands="one", haft=(-0.95, 0.95, 0.034, 0.034),
                   head=[dict(plate=[(-0.05, 0.80), (-0.08, 1.05), (-0.07, 1.30), (0.0, 1.42), (0.07, 1.30), (0.08, 1.05), (0.05, 0.80)], centre=(0.0, 1.10), thick=0.022, closed=True,
                              m="Wood", role="head")],
                   lower=[dict(lathe=[(0.90, 0.034), (0.95, 0.05), (1.02, 0.055), (1.08, 0.04), (1.12, 0.0)], m="Wood", n=8, faceted=True, role="head"),
                          dict(sweep=[(0.0, 1.10), (0.0, 1.30)], wl=lambda s: 0.03 * (1 - s), wr=lambda s: 0.03 * (1 - s), t=0.008, m="Wood", per=3, role="head")]
                         + feathers(0.88, 5, 0.12, "Cloth", 0.06),
                   fx=((0, 0, -1.3), (0, 0, 1.42))),
    # A gnome battle ladder: a short ladder, iron-shod and spiked at its ends, swung as a weapon.
    "gnome-battle-ladder": dict(family="double", build="hafted", hands="two",
                                parts=[dict(lathe=[(-1.20, 0.03), (1.20, 0.03)], m="Wood", n=6, role="haft", x=sx * 0.16) for sx in (-1, 1)]
                                      + [dict(tube=[(-0.16, 0, z), (0.16, 0, z)], r=0.022, m="Wood", n=6, role="haft") for z in (-1.0, -0.6, -0.2, 0.2, 0.6, 1.0)]
                                      + [dict(spike=((sx * 0.16, 0, sz * 1.19), (sx * 0.16, 0, sz * 1.38)), r=0.03, m="DarkIron", n=5) for sx in (-1, 1) for sz in (-1, 1)],
                                fx=((0, 0, -1.38), (0, 0, 1.38))),
    # A double walking-stick katana: a walking stick that draws apart into two blades, one
    # out of each end.
    "double-walking-stick-katana": dict(family="double", build="hafted", hands="two",
                                        parts=[dict(lathe=[(-0.40, 0.034), (0.40, 0.034)], m="Wood", n=10, role="grip"),
                                               dict(lathe=[(0.38, 0.036), (0.42, 0.038)], m="Bronze", n=10, role="fitting"),
                                               dict(lathe=[(-0.42, 0.038), (-0.38, 0.036)], m="Bronze", n=10, role="fitting"),
                                               dict(curved=dict(base=0.42, length=1.10, sweep=0.03, width=0.06, flare=-0.1, clip=0.92, toff=0.06, thick=0.016, fuller=None), m="Steel"),
                                               dict(curved=dict(base=0.42, length=1.10, sweep=0.03, width=0.06, flare=-0.1, clip=0.92, toff=0.06, thick=0.016, fuller=None), m="Steel", rot=(180, 0, 0))],
                                        fx=((0, 0, -1.52), (0, 0, 1.52))),
})


# --- named weapons -------------------------------------------------------------------------------
# Built by named_weapons.py, one function each; baked like the held items, at 2048 and with an
# emission atlas glowing at `glow`. `base` is the weapon each is in the hand.

RECIPES.update({
    "named-holy-avenger": dict(family="named", build="named", hands="one", base="longsword", glow=3.0),
    "named-flame-tongue": dict(family="named", build="named", hands="one", base="longsword", glow=4.0),
    "named-frost-brand": dict(family="named", build="named", hands="two", base="greatsword", glow=3.0),
    "named-life-drinker": dict(family="named", build="named", hands="two", base="greataxe", glow=3.5),
    "named-dwarven-thrower": dict(family="named", build="named", hands="one", base="warhammer", glow=3.0),
    "named-oathbow": dict(family="named", build="named", hands="bow", base="longbow", glow=2.5),
})


# --- lookups that need no Blender --------------------------------------------------------------

def family_of(weapon):
    r = RECIPES.get(weapon)
    if r is None:
        return LEGACY_FAMILY.get(weapon)
    while "family" not in r and "like" in r:
        r = RECIPES[r["like"]]
    return r.get("family")


def ids_in(family):
    """Every id in a family, the legacy models included."""
    return sorted([w for w in RECIPES if family_of(w) == family] + [w for w, f in LEGACY_FAMILY.items() if f == family])

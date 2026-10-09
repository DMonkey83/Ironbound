"""A variety sample: how far one catalogue weapon can vary between two drops, without magic.

Imported by the scratch renders of the variety sample; not used by the game yet. The owner asked
for "shades, glows, colours, cracks, widths, handle designs"; the named weapons answer that at the
top of the loot ladder, and this answers it for the ordinary weapon that drops a hundred times.
Each of six catalogue weapons gets three variants, `sample-<id>-a/b/c`, differing on

- shade: polished, blued, blackened, browned, damascus, rusted iron, bronze, brass, copper;
- colour: leather and cord in red, black, blue, green, ochre and white; enamel; paint; tassels;
- wear: nicks in an edge, a chipped point, rust and pitting, a split haft bound with wire;
- width and profile: broad, narrow, twin fullers, a diamond or square section, a waist, a swell
  toward the point, leaf and triangle and winged heads;
- handle: spiral leather, crisscross cord, ray skin under a diamond wrap, wire-wound, ringed,
  riveted scales; pommels wheel, faceted, pear, ring, claw and stone, skull, beast; guards
  straight, recurved, S, basket.

The variant materials are named `<Base>_<Variant>` (Steel_Blued, Wrap_Red): at run time each is
the base shared material with a tint, a pattern and a wear amount set as shader parameters, so
they cost no new textures. What changes shape — grip, pommel, guard, blade profile, nicks — is
geometry, and needs its own mesh or its own swappable piece.
"""
import surface
import weapon_families as wf
from weapon_recipes import RECIPES


def _damascus(colour):
    """Pattern-welded steel: bands of light and dark iron folded and drawn out along the blade."""
    m, nt, bsdf = surface._tree("Surface_Damascus")
    co = surface._coords(nt)
    w = surface._wave(nt, surface._stretch(nt, co, (1.0, 1.0, 0.25)), 34, 9.0, 4, "X")
    bands = surface._ramp(nt, w, [(0.30, 0.25), (0.48, 1.25), (0.62, 0.35)])
    tone = surface._shade(nt, colour, bands)
    rough = surface._span(nt, bands, 0.45, 0.25)
    surface._finish(nt, bsdf, tone, rough, surface._bump(nt, w, 0.05, 0.002), 0.5)
    return m


def _glossy(colour):
    return surface.flat(colour, 0.12, 0.0)


VARIANTS = {
    "Steel_Polished": ((0.70, 0.71, 0.73), 0.50, 0.18, lambda c: surface.steel(c, 0.9)),
    "Steel_Blued": ((0.10, 0.14, 0.30), 0.60, 0.30, lambda c: surface.steel(c, 0.7)),
    "Steel_Blackened": ((0.06, 0.06, 0.065), 0.45, 0.45, lambda c: surface.iron(c, 0.04)),
    "Steel_Browned": ((0.30, 0.18, 0.09), 0.45, 0.40, lambda c: surface.steel(c, 0.4)),
    "Steel_Damascus": ((0.46, 0.47, 0.50), 0.50, 0.30, _damascus),
    "Iron_Rusty": ((0.30, 0.22, 0.16), 0.30, 0.70, lambda c: surface.iron(c, 0.85)),
    "Brass": ((0.78, 0.58, 0.26), 0.60, 0.35, lambda c: surface.gold(c)),
    "Copper": ((0.70, 0.34, 0.18), 0.60, 0.35, lambda c: surface.gold(c)),
    "Wrap_Red": ((0.32, 0.04, 0.03), 0.0, 0.8, lambda c: surface.leather(c)),
    "Wrap_Black": ((0.03, 0.028, 0.026), 0.0, 0.8, lambda c: surface.leather(c)),
    "Wrap_Blue": ((0.05, 0.10, 0.25), 0.0, 0.8, lambda c: surface.leather(c)),
    "Wrap_Green": ((0.06, 0.17, 0.07), 0.0, 0.8, lambda c: surface.leather(c)),
    "Wrap_Ochre": ((0.48, 0.30, 0.07), 0.0, 0.8, lambda c: surface.leather(c)),
    "Wrap_White": ((0.78, 0.75, 0.66), 0.0, 0.8, lambda c: surface.leather(c)),
    "Cord_Red": ((0.45, 0.04, 0.03), 0.0, 0.9, lambda c: surface.cloth(c)),
    "Cord_Black": ((0.025, 0.025, 0.03), 0.0, 0.9, lambda c: surface.cloth(c)),
    "Cord_White": ((0.82, 0.80, 0.74), 0.0, 0.9, lambda c: surface.cloth(c)),
    "Cord_Blue": ((0.06, 0.14, 0.40), 0.0, 0.9, lambda c: surface.cloth(c)),
    "Wood_Dark": ((0.15, 0.075, 0.035), 0.0, 0.7, lambda c: surface.wood(c)),
    "Wood_Pale": ((0.74, 0.60, 0.40), 0.0, 0.7, lambda c: surface.wood(c, (0.55, 0.42, 0.27))),
    "RaySkin": ((0.86, 0.84, 0.77), 0.0, 0.6, lambda c: surface.bone(c)),
    "Enamel_Blue": ((0.04, 0.12, 0.50), 0.0, 0.12, _glossy),
    "Enamel_Red": ((0.50, 0.03, 0.03), 0.0, 0.12, _glossy),
    "Paint_Blue": ((0.06, 0.16, 0.45), 0.0, 0.6, lambda c: surface.flat(c, 0.6)),
}
wf.MATERIALS.update(VARIANTS)
for _n, (_c, _m, _r, _rec) in VARIANTS.items():
    surface.RECIPES[_n] = (lambda r: lambda c, m, rough: r(c))(_rec)

LONG = dict(like="longsword", family="variety")
SAMPLES = {
    # Longswords: one blade, three swords.
    "sample-longsword-a": dict(LONG, metal="Steel_Polished", fittings="Brass", grip_material="Wrap_Red",
                               blade=dict(width=0.084, taper=0.36, fuller=(0.0, 0.70, 0.032, 0.55), twin=True),
                               guard=dict(kind="cross", ends="flare", knob=0.7)),
    "sample-longsword-b": dict(LONG, metal="Steel_Blackened", fittings="Steel_Blued", grip_material="RaySkin",
                               blade=dict(width=0.058, taper=0.5, section="diamond", fuller=None, point=0.2, curve=0.95),
                               guard=dict(kind="recurved", span=0.26, droop=0.07, ends="knob", knob=0.9),
                               grip=dict(style="rayskin", skin="RaySkin", cord="Cord_Black"),
                               pommel=dict(kind="faceted", radius=0.05, height=0.075)),
    "sample-longsword-c": dict(LONG, metal="Steel_Damascus", fittings="Steel_Browned", grip_material="Wrap_Ochre",
                               blade=dict(width=0.074, taper=0.25, waist=0.10, swell=(0.80, 0.22), fuller=(0.0, 0.42, 0.02, 0.5),
                                          nicks=((0.34, 0.20), (0.50, 0.28), (0.62, 0.16))),
                               guard=dict(kind="s", span=0.26, droop=0.07, ends="knob", knob=1.0),
                               grip=dict(style="wire", wire="Copper"),
                               pommel=dict(kind="pear", top=-0.235, height=0.13, radius=0.055)),
    # Daggers.
    "sample-dagger-a": dict(like="dagger", family="variety", metal="Steel_Polished", fittings="Brass", grip_material="Bone",
                            blade=dict(width=0.058, point=0.55, curve=0.85, leaf=0.25, section="lens"),
                            guard=dict(kind="bar", span=0.09, ends="plain", height=0.016, depth=0.03, centre=0.0),
                            grip=dict(style="scales", rivets="Brass", tang="Steel_Polished"),
                            pommel=dict(kind="cap", radius=0.038, height=0.03)),
    "sample-dagger-b": dict(like="dagger", family="variety", metal="Steel_Blued", fittings="Steel_Blued", grip_material="Wrap_Black",
                            blade=dict(width=0.024, thick=0.022, section="square", point=0.35, curve=0.9, tip=0.86),
                            guard=dict(kind="cross", span=0.10, ends="knob", knob=1.2, height=0.012, depth=0.018),
                            pommel=dict(kind="ring", radius=0.03, ring=0.04)),
    "sample-dagger-c": dict(like="dagger", family="variety", metal="Iron_Rusty", fittings="Iron_Rusty", grip_material="Wrap_Green",
                            blade=dict(width=0.07, curve=0.55, tip=0.74, nicks=((0.30, 0.25), (0.55, 0.3))),
                            guard=dict(kind="bar", span=0.10, ends="plain", centre=0.0, height=0.014, depth=0.026),
                            grip=dict(style="rings"),
                            pommel=dict(kind="ball", radius=0.04, height=0.07)),
}

AXE = dict(family="variety", build="hafted", hands="one", butt=dict(z=-0.30, kind="cap", r=0.040))
SAMPLES.update({
    # Battleaxes: a bright fan, a blackened crescent on a split haft bound with wire, a bronze beard.
    "sample-battleaxe-a": dict(AXE, haft=(-0.30, 1.75, 0.040, 0.036), haft_material="Wood_Pale", wraps=[(-0.26, 0.22, 0.044, "Wrap_Red")],
                               butt=dict(z=-0.30, kind="cap", r=0.040, m="Brass"),
                               head=[dict(kind="axe", z=1.50, shape="broad", size=1.15, thick=0.045, cheek="Steel_Polished", m="Steel_Polished", langets=0.2, hr=0.038)]),
    "sample-battleaxe-b": dict(AXE, haft=(-0.30, 1.75, 0.040, 0.036), haft_material="Wood_Dark", wraps=[(-0.26, 0.22, 0.044, "Wrap_Green", "plain")],
                               head=[dict(kind="axe", z=1.50, shape="crescent", size=1.2, thick=0.045, cheek="Steel_Blackened", m="Steel", back="spike", back_len=0.14, hr=0.038)],
                               parts=[dict(tube=[(0.0425 * __import__("math").cos(k * 0.5), 0.0425 * __import__("math").sin(k * 0.5), 0.70 + k * 0.0045) for k in range(60)],
                                           r=0.0035, m="Copper", n=4, role="fitting")]
                                     + [dict(tube=[(-0.010 + 0.02 * (k % 2), -0.041, 0.42 + 0.06 * k), (0.010 - 0.02 * (k % 2), -0.041, 0.48 + 0.06 * k)], r=0.0018, m="Cord_Black", n=3)
                                        for k in range(5)]),
    "sample-battleaxe-c": dict(AXE, haft=(-0.30, 1.75, 0.040, 0.036), haft_material="Wood", wraps=[(-0.26, 0.22, 0.044, "Cord_Red", "cord")],
                               head=[dict(kind="axe", z=1.50, shape="bearded", size=1.2, thick=0.04, cheek="Bronze", m="Bronze", hr=0.038, rivets=False)],
                               parts=[dict(tube=[(0.0, 0.0, 1.30), (0.03 + 0.01 * k, 0.005 * k, 1.18), (0.05 + 0.02 * k, 0.01 * k, 1.05)], per=3, r=0.006, m="Cord_Red", n=4, role="fitting")
                                      for k in range(4)]),
})

SPEAR = dict(family="variety", build="hafted", hands="two", butt=dict(z=-1.40, kind="cap", r=0.042))
SAMPLES.update({
    "sample-spear-a": dict(SPEAR, haft=(-1.40, 2.60, 0.042, 0.036), haft_material="Wood_Pale", wraps=[(-0.20, 0.30, 0.046, "Wrap_Red")],
                           butt=dict(z=-1.40, kind="cap", r=0.042, m="Brass"),
                           head=[dict(kind="spear", z=2.60, length=0.60, width=0.10, shape="leaf", size=1.55, m="Steel_Polished")],
                           parts=[dict(lathe=[(2.36, 0.0), (2.37, 0.05), (2.42, 0.05), (2.43, 0.0)], m="Cord_Red", n=8, role="fitting")]
                                 + [dict(tube=[(dx, 0.0, 2.37), (dx * 1.8 + 0.02, 0.012 * (k % 3), 2.15), (dx * 2.6 + 0.05, 0.0, 1.92)], per=3, r=0.011, m="Cord_Red", n=4, role="fitting")
                                    for k, dx in enumerate((-0.04, -0.025, -0.01, 0.005, 0.02, 0.035))]),
    "sample-spear-b": dict(SPEAR, haft=(-1.40, 2.60, 0.042, 0.036), haft_material="Wood_Dark", wraps=[(-0.20, 0.30, 0.046, "Wrap_Black")],
                           head=[dict(kind="spear", z=2.60, length=0.55, width=0.14, shape="broad", size=1.45, m="Steel_Blued")]
                                + [dict(kind="collar", z=z, r=0.042, h=0.04, m="Brass") for z in (2.30, 1.80, 1.30, 0.55)]),
    "sample-spear-c": dict(SPEAR, haft=(-1.40, 2.60, 0.042, 0.036), haft_material="Wood", wraps=[(-0.20, 0.30, 0.046, "Cord_White", "cord"), (1.0, 1.3, 0.044, "Cord_White", "cord")],
                           head=[dict(kind="spear", z=2.60, length=0.75, width=0.05, shape="needle", wings=0.15, size=1.4, m="Iron_Rusty")]),
})

MACE = dict(family="variety", build="hafted", hands="one", butt=dict(z=-0.36, kind="knob", r=0.042))
SAMPLES.update({
    "sample-heavy-mace-a": dict(MACE, haft=(-0.36, 1.25, 0.042, 0.038), haft_material="Wood_Pale", wraps=[(-0.30, 0.22, 0.048, "Wrap_Blue")],
                                head=[dict(kind="flanged", z0=1.18, z1=1.62, r=0.20, n=8, core=0.10, m="Steel_Polished"), dict(kind="collar", z=1.16, r=0.05, h=0.10, m="Brass")]),
    "sample-heavy-mace-b": dict(MACE, haft=(-0.36, 1.25, 0.042, 0.038), haft_material="Wood_Dark", wraps=[(-0.30, 0.22, 0.048, "Wrap_Red", "crisscross", "Cord_Black")],
                                butt=None, head=[dict(kind="collar", z=1.20, r=0.05, h=0.10, m="Steel_Blackened"),
                                                 dict(ball=(0, 0, 1.38), r=0.15, spikes=0, m="Steel_Blackened"),
                                                 dict(pommel=dict(kind="faceted", top=-0.34, radius=0.055, height=0.08), m="Steel_Blackened")]
                                + [dict(lathe=[(0.0, 0.022), (0.012, 0.018), (0.02, 0.0)], m="Brass", n=6, role="head",
                                        dir=(__import__("math").cos(a) * __import__("math").cos(e), __import__("math").sin(a) * __import__("math").cos(e), __import__("math").sin(e)),
                                        at=(0.148 * __import__("math").cos(a) * __import__("math").cos(e), 0.148 * __import__("math").sin(a) * __import__("math").cos(e), 1.38 + 0.148 * __import__("math").sin(e)))
                                   for e in (-0.6, 0.0, 0.6) for a in [k * 1.047 + 0.5 * (e > 0) for k in range(6)]]),
    "sample-heavy-mace-c": dict(MACE, haft=(-0.36, 1.25, 0.042, 0.038), haft_material="Wood", wraps=[(-0.30, 0.22, 0.048, "Wrap_Ochre")],
                                butt=None, head=[dict(kind="flanged", z0=1.18, z1=1.58, r=0.17, n=6, core=0.10, m="Bronze", low=True),
                                                 dict(pommel=dict(kind="beast", top=-0.34, radius=0.05), m="Bronze")]
                                + [dict(kind="collar", z=z, r=0.043, h=0.035, m="Copper") for z in (0.40, 0.75, 1.12)]),
})

BOW = dict(family="variety", build="bow", hands="bow", reach=1.25, belly=0.42, thick=0.036)
SAMPLES.update({
    "sample-shortbow-a": dict(BOW, material="Wood_Dark", grip_material="Wrap_Red", ears="Horn"),
    "sample-shortbow-b": dict(BOW, material="Wood_Pale", grip_material="Cord_White", ears="Horn",
                              parts=[dict(lathe=[(z - 0.03, 0.03), (z, 0.032), (z + 0.03, 0.03)], m="Paint_Blue", n=10, role="fitting", at=(0, 0.42 * (z / 1.25) ** 2, 0))
                                     for z in (-0.95, -0.55, 0.55, 0.95)]),
    "sample-shortbow-c": dict(BOW, recurve=0.95, contact=0.78, belly=0.36, belly_material="Horn", ears="Bone", grip_material="Wrap_Green",
                              parts=[dict(tube=[(dx, 0.0, -0.15), (dx * 2, 0.01, -0.30), (dx * 3, 0.0, -0.44)], per=3, r=0.005, m="Cord_Red", n=4, role="fitting")
                                     for dx in (-0.015, 0.0, 0.015)]),
})

RECIPES.update(SAMPLES)

# What each variant changes, in a few words, for the sheet.
NOTES = {
    "sample-longsword-a": "polished · brass · red leather · broad, twin fullers",
    "sample-longsword-b": "blackened · blued · ray skin & black cord · needle diamond · recurved",
    "sample-longsword-c": "damascus · browned · copper wire · waisted, swell · nicked · S-guard",
    "sample-dagger-a": "polished · brass · riveted bone scales · leaf",
    "sample-dagger-b": "blued · black leather · square stiletto · ring",
    "sample-dagger-c": "rusted · green leather, iron rings · nicked, blunt point",
    "sample-battleaxe-a": "polished fan · pale ash · red leather · brass",
    "sample-battleaxe-b": "blackened crescent · dark haft split, copper-bound · green",
    "sample-battleaxe-c": "bronze beard · red cord grip & tassels",
    "sample-spear-a": "polished leaf · pale ash · red tassel · brass shoe",
    "sample-spear-b": "blued broad head · black haft · brass bands",
    "sample-spear-c": "rusted winged needle · white cord bindings",
    "sample-heavy-mace-a": "polished flanges · pale haft · blue leather",
    "sample-heavy-mace-b": "blackened ball, brass studs · crisscross · faceted",
    "sample-heavy-mace-c": "bronze low flanges · copper bands · beast pommel",
    "sample-shortbow-a": "dark wood · red grip · horn nocks",
    "sample-shortbow-b": "pale wood · painted blue bands · white cord",
    "sample-shortbow-c": "horn-bellied recurve · bone ears · green · tassel",
}

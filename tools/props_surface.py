"""Surfaces for the props: wood that is cut from a log, burlap, straw, rope, brass, rock.

    import props_surface as ps
    ps.register()                       # the recipes below join surface.RECIPES by name
    ...build, then surface.finish(...) as every generator does...
    ps.weather(objects, "crate", sources)   # rust run down from the nails, after the bake

Two things here that `surface.py` does not do, both because a prop is planks:

GRAIN FOLLOWS EACH BOARD. `surface.wood` runs its grain up world Z, which is right for a haft
and wrong for a plank lying across a crate. Every board built by `props_parts.board` carries a
point attribute, `grain`: the position in the board's own frame, measured from a pith outside
the board. The recipe draws growth rings round that pith, so a flat-sawn face shows arches, a
quarter-sawn one stripes and an end shows the arcs of the log; another attribute, `board`,
gives each board its own tone. Nothing about the result depends on how the board was turned.

WEATHER IS PAINTED AFTER THE BAKE. Rust bleeding from a nail is a streak that starts at the
nail and runs down the face under it, which no noise can place. `weather()` bakes the world
position and the wood mask into the finished atlas, then paints each streak in numpy from
the list of sources the builder kept, and writes the atlas back.
"""
import math
import os
import tempfile

import bpy
import numpy as np

import surface

# A board unit is five feet of floor and a man is 1.4 of them, so one unit is about 1.25 m of
# prop. The goblins' recipes were tuned at about 0.6 m to the unit; at 0.5 the same rust patch,
# leather grain and dent come out the same real size on a crate as on a goblin's belt.
SCALE = 0.5

# --- colours --------------------------------------------------------------------------------

OAK = (0.30, 0.20, 0.12)            # weathered oak: mid brown going grey
PINE = (0.38, 0.27, 0.15)           # crate deal: paler, yellower
DARK_OAK = (0.17, 0.11, 0.065)      # the strongbox, oiled and old
CART_ASH = (0.33, 0.24, 0.15)
STRAW = (0.80, 0.62, 0.27)
BURLAP = (0.40, 0.29, 0.15)
HEMP = (0.42, 0.33, 0.20)
IRON = (0.15, 0.148, 0.145)          # grey-black
BRASS = (0.55, 0.40, 0.17)
CHEST_PAINT = (0.23, 0.07, 0.045)   # an oxblood that has gone brown
ROCK = (0.20, 0.185, 0.17)


# --- node helpers in board units --------------------------------------------------------------
# surface's helpers divide every frequency by surface.SCALE; these take frequencies per board
# unit, whatever SCALE is set to while they run.

def _n(nt, co, freq, detail=2.0, rough=0.5, distortion=0.0):
    return surface._noise(nt, co, freq * surface.SCALE, detail, rough, distortion)


def _v(nt, co, freq, feature="DISTANCE_TO_EDGE", randomness=1.0):
    return surface._voronoi(nt, co, freq * surface.SCALE, feature, randomness)


def _bump(nt, height, strength, distance, previous=None):
    return surface._bump(nt, height, strength, distance * surface.SCALE, previous)


def _vec(nt, op, a, b=None, scale=None):
    n = nt.nodes.new("ShaderNodeVectorMath")
    n.operation = op
    surface._plug(nt, n.inputs[0], a)
    if b is not None:
        surface._plug(nt, n.inputs[1], b)
    if scale is not None:
        n.inputs["Scale"].default_value = scale
    return n.outputs["Value"] if op in ("LENGTH", "DOT_PRODUCT", "DISTANCE") else n.outputs["Vector"]


def _xyz(nt, vector):
    n = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(vector, n.inputs[0])
    return n.outputs[0], n.outputs[1], n.outputs[2]


def _combine(nt, x, y, z):
    n = nt.nodes.new("ShaderNodeCombineXYZ")
    surface._plug(nt, n.inputs[0], x)
    surface._plug(nt, n.inputs[1], y)
    surface._plug(nt, n.inputs[2], z)
    return n.outputs[0]


def _vattr(nt, name):
    n = nt.nodes.new("ShaderNodeAttribute")
    n.attribute_name = name
    return n.outputs["Vector"], n.outputs["Fac"]


def _smooth(nt, fac, lo, hi):
    """smoothstep(lo, hi, fac)"""
    n = nt.nodes.new("ShaderNodeMapRange")
    n.interpolation_type = "SMOOTHSTEP"
    surface._plug(nt, n.inputs["Value"], fac)
    n.inputs["From Min"].default_value = lo
    n.inputs["From Max"].default_value = hi
    return n.outputs["Result"]


def _mix_ramp(nt, fac, stops):
    """A colour ramp straight to a colour socket."""
    return surface._ramp(nt, fac, stops)


def _foot(nt, height):
    """1 at the floor fading to 0 at `height`: where damp climbs into wood standing on rock."""
    _, _, z = _xyz(nt, surface._coords(nt))
    return _math_inv(nt, _smooth(nt, z, 0.0, height))


def _math_inv(nt, fac):
    return surface._math(nt, "SUBTRACT", 1.0, fac, clamp=True)


def _grey(nt, socket):
    """A colour socket as a float, for the ramp-to-roughness paths."""
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(socket, sep.inputs[0])
    return sep.outputs[0]


# --- recipes --------------------------------------------------------------------------------

def plank(colour, paint=None, mould=1.0, rings=55.0, rough=(0.92, 0.72), mud=0.0):
    """Sawn wood. Growth rings round each board's own pith (the `grain` attribute), its own
    tone (`board`), checks along the grain, rubbed-pale edges, and mould and dark damp at the
    foot. `paint`, if given, is a colour worn through to the wood on every edge and high spot."""
    m, nt, bsdf = surface._tree("Surface_Plank")
    grain, _ = _vattr(nt, "grain")
    _, tone_id = _vattr(nt, "board")
    world = surface._coords(nt)
    x, y, z = _xyz(nt, grain)

    # Rings: distance from the pith, wandered a little by a slow noise so they are not lathe
    # turned, then folded into a sawtooth — sharp latewood, soft earlywood.
    wander = _n(nt, surface._stretch(nt, grain, (1.0, 1.0, 0.12)), 4.0, 2, 0.5)
    r = surface._math(nt, "SQRT", surface._math(nt, "ADD", surface._math(nt, "MULTIPLY", x, x), surface._math(nt, "MULTIPLY", y, y)))
    r = surface._math(nt, "ADD", r, surface._math(nt, "MULTIPLY", wander, 0.035))
    ring = surface._math(nt, "FRACT", surface._math(nt, "MULTIPLY", r, rings))
    late = surface._ramp(nt, ring, [(0.0, 0.0), (0.55, 0.15), (0.85, 1.0), (1.0, 0.2)])
    late = _grey(nt, late)

    # Long fibres and pores, squashed hard along the board.
    fibre = _n(nt, surface._stretch(nt, grain, (1.0, 1.0, 0.025)), 140.0, 3, 0.6)
    # Checks: the splits that open along the grain as a board dries — the 0.5 contour of a
    # noise squashed hard along the board, kept only where a second noise allows, so a few
    # long hairline cracks and not a crackle.
    contour = _n(nt, surface._stretch(nt, grain, (1.0, 1.0, 0.04)), 16.0, 2, 0.5)
    checks = _grey(nt, surface._ramp(nt, contour, [(0.49, 0.0), (0.5, 0.7), (0.51, 0.0)]))
    checks = surface._math(nt, "MULTIPLY", checks,
                           _grey(nt, surface._ramp(nt, _n(nt, grain, 6.0, 2), [(0.55, 0.0), (0.62, 1.0)])))

    dark = tuple(c * 0.42 for c in colour)
    light = tuple(min(1.0, c * 1.25 + 0.02) for c in colour)
    # Each board its own: some paler and greyer from the weather, some darker and redder.
    grey = sum(colour) / 3
    weathered = (grey * 1.05 + 0.03, grey * 1.0 + 0.025, grey * 0.92 + 0.02)
    red = (colour[0] * 0.80, colour[1] * 0.66, colour[2] * 0.55)
    toned = _mix_ramp(nt, tone_id, [(0.0, red), (0.45, colour), (0.75, tuple(c * 1.12 for c in colour)), (1.0, weathered)])
    wood = surface._mix(nt, toned, dark, surface._math(nt, "MULTIPLY", late, 0.8))
    wood = surface._shade(nt, wood, surface._span(nt, fibre, 0.78, 1.12))
    # Water marks: broad soft blotches, darker, that run across board joints.
    stain = _grey(nt, surface._ramp(nt, _n(nt, world, 2.6, 3, 0.55), [(0.42, 1.0), (0.62, 0.62)]))
    wood = surface._shade(nt, wood, stain)
    wood = surface._shade(nt, wood, surface._span(nt, _n(nt, world, 9.0, 2), 0.85, 1.08))
    wood = surface._mix(nt, wood, (0.02, 0.012, 0.006), surface._math(nt, "MULTIPLY", checks, 0.6))

    edges = surface._edges(nt, 5.0)
    if paint is not None:
        # Paint holds in the hollows and has gone on every edge and on the tops of the grain.
        keep = surface._math(nt, "SUBTRACT", _n(nt, world, 9.0, 4, 0.6), surface._math(nt, "MULTIPLY", edges, 0.9))
        keep = surface._math(nt, "SUBTRACT", keep, surface._math(nt, "MULTIPLY", late, 0.12))
        keep = _smooth(nt, keep, 0.30, 0.38)
        painted = surface._shade(nt, paint, surface._span(nt, _n(nt, world, 16.0, 3), 0.78, 1.10))
        wood = surface._mix(nt, wood, painted, keep)
    wood = surface._mix(nt, wood, light, surface._math(nt, "MULTIPLY", edges, 0.45))

    # The foot: dark where the damp has climbed, green-grey mould in blooms on top of that.
    if mould:
        foot = _foot(nt, 0.16)
        damp = surface._math(nt, "MULTIPLY", foot, _grey(nt, surface._span(nt, _n(nt, world, 6.0, 3), 0.4, 1.0)))
        wood = surface._mix(nt, wood, tuple(c * 0.45 for c in colour), surface._math(nt, "MULTIPLY", damp, 0.8 * mould))
        bloom = _grey(nt, surface._ramp(nt, _n(nt, world, 24.0, 4, 0.65), [(0.45, 0.0), (0.62, 1.0)]))
        bloom = surface._math(nt, "MULTIPLY", bloom, _smooth(nt, foot, 0.2, 0.9))
        wood = surface._mix(nt, wood, (0.20, 0.23, 0.15), surface._math(nt, "MULTIPLY", bloom, 0.75 * mould))

    # Mud from the trail, caked up the wheels and splashed on what was low.
    mud_mask = None
    if mud:
        low = _foot(nt, 0.22)
        clods = _grey(nt, surface._ramp(nt, _n(nt, world, 14.0, 4, 0.65), [(0.35, 0.0), (0.55, 1.0)]))
        mud_mask = surface._math(nt, "MULTIPLY", _smooth(nt, surface._math(nt, "ADD", low, surface._math(nt, "MULTIPLY", clods, 0.6)), 0.55, 0.9), mud)
        earth = surface._shade(nt, (0.11, 0.085, 0.06), surface._span(nt, _n(nt, world, 40.0, 3), 0.7, 1.2))
        wood = surface._mix(nt, wood, earth, mud_mask)

    rough_ = surface._span(nt, late, rough[0], rough[1])
    if mud_mask is not None:
        rough_ = surface._math(nt, "MAXIMUM", rough_, mud_mask)
    n = _bump(nt, late, 0.45, 0.004)                                  # weathered: latewood stands
    n = _bump(nt, fibre, 0.25, 0.0015, n)
    n = _bump(nt, checks, -0.5, 0.002, n)
    n = _bump(nt, _n(nt, world, 30.0, 2), 0.15, 0.003, n)
    if mud_mask is not None:
        n = _bump(nt, surface._math(nt, "MULTIPLY", mud_mask, _n(nt, world, 60.0, 3)), 0.6, 0.004, n)
    surface._finish(nt, bsdf, wood, rough_, n)
    return m


def burlap(colour):
    """Coarse jute: a weave you can see the gaps in, hairy, stained, darker in its folds.
    The weave runs in the cloth's own coordinates (`weave`, written before it was draped), so
    it follows every fold."""
    m, nt, bsdf = surface._tree("Surface_Burlap")
    weave_co, _ = _vattr(nt, "weave")
    world = surface._coords(nt)
    u, v, _ = _xyz(nt, weave_co)
    threads = 120.0

    def bands(c):
        s = surface._math(nt, "SINE", surface._math(nt, "MULTIPLY", c, threads * math.tau))
        return surface._math(nt, "MULTIPLY", surface._math(nt, "ADD", s, 1.0), 0.5)

    jitter_u = surface._math(nt, "ADD", u, surface._math(nt, "MULTIPLY", _n(nt, weave_co, 14.0, 2), 0.004))
    jitter_v = surface._math(nt, "ADD", v, surface._math(nt, "MULTIPLY", _n(nt, weave_co, 15.0, 2), 0.004))
    warp, weft = bands(jitter_u), bands(jitter_v)
    over = surface._math(nt, "MAXIMUM", warp, weft)
    gaps = _smooth(nt, over, 0.15, 0.55)
    tone = surface._shade(nt, colour, surface._span(nt, gaps, 0.62, 1.05))
    tone = surface._shade(nt, tone, surface._span(nt, _n(nt, world, 7.0, 3), 0.75, 1.12))
    stains = _grey(nt, surface._ramp(nt, _n(nt, world, 5.0, 4, 0.6), [(0.55, 0.0), (0.7, 1.0)]))
    tone = surface._mix(nt, tone, tuple(c * 0.45 for c in colour), surface._math(nt, "MULTIPLY", stains, 0.6))
    hairs = _n(nt, world, 160.0, 4, 0.7)
    tone = surface._shade(nt, tone, surface._span(nt, hairs, 0.85, 1.15))
    if True:
        foot = _foot(nt, 0.10)
        tone = surface._mix(nt, tone, tuple(c * 0.4 for c in colour), surface._math(nt, "MULTIPLY", foot, 0.6))
    n = _bump(nt, gaps, 0.7, 0.003)
    n = _bump(nt, hairs, 0.2, 0.001, n)
    surface._finish(nt, bsdf, tone, 0.97, n)
    return m


def straw(colour):
    """Dry stalks: pale and glossy-ish along the stalk, brown at the nodes, some gone grey."""
    m, nt, bsdf = surface._tree("Surface_Straw")
    world = surface._coords(nt)
    # Stalks lie every which way: streaks squashed along X, along Y, along both diagonals and
    # up Z, and the brightest of them wins, so the bed reads as a tangle and not a grain.
    streak = None
    for i, squash in enumerate(((0.06, 1.0, 1.0), (1.0, 0.06, 1.0), (1.0, 1.0, 0.08))):
        s = _n(nt, surface._stretch(nt, world, squash), 70.0 + 13 * i, 3, 0.6)
        streak = s if streak is None else surface._math(nt, "MAXIMUM", streak, s)
    rot = nt.nodes.new("ShaderNodeVectorRotate")
    rot.rotation_type = "Z_AXIS"
    rot.inputs["Angle"].default_value = 0.785
    nt.links.new(world, rot.inputs["Vector"])
    for i, squash in enumerate(((0.06, 1.0, 1.0), (1.0, 0.06, 1.0))):
        s = _n(nt, surface._stretch(nt, rot.outputs[0], squash), 64.0 + 9 * i, 3, 0.6)
        streak = surface._math(nt, "MAXIMUM", streak, s)
    streak = _smooth(nt, streak, 0.45, 0.75)
    blotch = _n(nt, world, 18.0, 2)
    tone = surface._shade(nt, colour, surface._span(nt, streak, 0.72, 1.18))
    tone = surface._mix(nt, tone, (0.28, 0.20, 0.10), _grey(nt, surface._ramp(nt, blotch, [(0.55, 0.0), (0.75, 0.8)])))
    tone = surface._mix(nt, tone, (0.40, 0.38, 0.33), _grey(nt, surface._ramp(nt, _n(nt, world, 7.0, 2), [(0.6, 0.0), (0.75, 0.6)])))
    surface._finish(nt, bsdf, tone, surface._span(nt, streak, 0.55, 0.8), _bump(nt, streak, 0.3, 0.002))
    return m


def rope(colour):
    m, nt, bsdf = surface._tree("Surface_Rope")
    world = surface._coords(nt)
    fibre = _n(nt, world, 220.0, 4, 0.7)
    tone = surface._shade(nt, colour, surface._span(nt, fibre, 0.70, 1.15))
    tone = surface._shade(nt, tone, surface._span(nt, _n(nt, world, 9.0, 3), 0.7, 1.1))
    surface._finish(nt, bsdf, tone, 0.95, _bump(nt, fibre, 0.5, 0.0015))
    return m


def brass(colour):
    """Cast brass: bright where hands rub it, brown tarnish in patches, verdigris in hollows."""
    m, nt, bsdf = surface._tree("Surface_Brass")
    world = surface._coords(nt)
    edges = surface._edges(nt, 6.0)
    tarnish = _grey(nt, surface._ramp(nt, _n(nt, world, 18.0, 3, 0.6), [(0.4, 0.0), (0.7, 1.0)]))
    tone = surface._shade(nt, colour, surface._span(nt, tarnish, 1.0, 0.55))
    verd = _grey(nt, surface._ramp(nt, _n(nt, world, 40.0, 3, 0.6), [(0.6, 0.0), (0.75, 1.0)]))
    verd = surface._math(nt, "MULTIPLY", verd, _math_inv(nt, edges))
    tone = surface._mix(nt, tone, (0.16, 0.26, 0.18), surface._math(nt, "MULTIPLY", verd, 0.7))
    tone = surface._mix(nt, tone, (0.86, 0.68, 0.36), surface._math(nt, "MULTIPLY", edges, 0.8))
    metal = surface._math(nt, "SUBTRACT", 0.35, surface._math(nt, "MULTIPLY", verd, 0.3))
    rough = surface._math(nt, "ADD", 0.30, surface._math(nt, "MULTIPLY", tarnish, 0.3))
    n = _bump(nt, _v(nt, world, 50.0, "F1"), 0.25, 0.002)
    surface._finish(nt, bsdf, tone, rough, n, metal)
    return m


def rock(colour):
    """Cave rock: grey-brown, cracked, damp-dark in the low places, a bloom of pale lichen."""
    m, nt, bsdf = surface._tree("Surface_Rock")
    world = surface._coords(nt)
    big = _n(nt, world, 3.0, 4, 0.55)
    cracks = _grey(nt, surface._ramp(nt, _v(nt, world, 5.0), [(0.0, 0.0), (0.015, 1.0)]))
    grit = _n(nt, world, 90.0, 3, 0.6)
    tone = surface._shade(nt, colour, surface._span(nt, big, 0.65, 1.25))
    tone = surface._shade(nt, tone, surface._span(nt, grit, 0.85, 1.12))
    tone = surface._shade(nt, tone, surface._span(nt, cracks, 0.55, 1.0))
    lichen = _grey(nt, surface._ramp(nt, _n(nt, world, 14.0, 4, 0.7), [(0.62, 0.0), (0.72, 1.0)]))
    tone = surface._mix(nt, tone, (0.34, 0.36, 0.28), surface._math(nt, "MULTIPLY", lichen, 0.5))
    n = _bump(nt, big, 0.6, 0.04)
    n = _bump(nt, cracks, 0.6, 0.01, n)
    n = _bump(nt, grit, 0.35, 0.002, n)
    surface._finish(nt, bsdf, tone, surface._span(nt, grit, 0.75, 0.95), n)
    return m


def glass(colour):
    m, nt, bsdf = surface._tree("Surface_Glass")
    world = surface._coords(nt)
    tone = surface._shade(nt, colour, surface._span(nt, _n(nt, world, 12.0, 2), 0.7, 1.2))
    tone = surface._mix(nt, tone, tuple(min(1.0, c * 2.5 + 0.1) for c in colour), surface._math(nt, "MULTIPLY", surface._edges(nt, 6.0), 0.7))
    surface._finish(nt, bsdf, tone, 0.12, _bump(nt, _n(nt, world, 30.0, 2), 0.1, 0.002))
    return m


def water(colour):
    m, nt, bsdf = surface._tree("Surface_Water")
    world = surface._coords(nt)
    surface._finish(nt, bsdf, colour, 0.04, _bump(nt, _n(nt, world, 9.0, 2), 0.25, 0.004))
    return m


def silver(colour):
    m, nt, bsdf = surface._tree("Surface_Silver")
    world = surface._coords(nt)
    tarnish = _grey(nt, surface._ramp(nt, _v(nt, world, 120.0), [(0.0, 0.0), (0.06, 1.0)]))
    tone = surface._shade(nt, colour, surface._span(nt, tarnish, 0.55, 1.0))
    tone = surface._mix(nt, tone, (0.9, 0.9, 0.88), surface._math(nt, "MULTIPLY", surface._edges(nt, 6.0), 0.6))
    surface._finish(nt, bsdf, tone, surface._span(nt, tarnish, 0.5, 0.25), _bump(nt, _n(nt, world, 60.0, 2), 0.15, 0.002), 0.7)
    return m


def copper(colour):
    m, nt, bsdf = surface._tree("Surface_Copper")
    world = surface._coords(nt)
    patina = _grey(nt, surface._ramp(nt, _n(nt, world, 30.0, 3), [(0.45, 0.0), (0.7, 1.0)]))
    tone = surface._mix(nt, colour, (0.18, 0.09, 0.05), surface._math(nt, "MULTIPLY", patina, 0.7))
    tone = surface._mix(nt, tone, (0.85, 0.48, 0.30), surface._math(nt, "MULTIPLY", surface._edges(nt, 6.0), 0.6))
    surface._finish(nt, bsdf, tone, surface._span(nt, patina, 0.35, 0.7), _bump(nt, _n(nt, world, 60.0, 2), 0.15, 0.002), 0.6)
    return m


def iron(colour, rust=0.4):
    """Forged iron for props: grey-black, pitted, a fine rust in its pores and runs, edges
    rubbed a little brighter. Barely metallic: the game has no sky for metal to reflect, and
    surface.iron's bright edges came out as camouflage on straps this thin."""
    m, nt, bsdf = surface._tree("Surface_PropIron")
    world = surface._coords(nt)
    edges = surface._edges(nt, 4.0)
    pits = _grey(nt, surface._ramp(nt, _n(nt, world, 160.0, 3, 0.6), [(0.55, 0.0), (0.68, 1.0)]))
    patch = _grey(nt, surface._ramp(nt, _n(nt, world, 18.0, 4, 0.6), [(0.62 - 0.25 * rust, 0.0), (0.80 - 0.25 * rust, 1.0)]))
    rusty = surface._math(nt, "MAXIMUM", surface._math(nt, "MULTIPLY", pits, 0.7), patch)
    rusty = surface._math(nt, "MULTIPLY", rusty, _math_inv(nt, surface._math(nt, "MULTIPLY", edges, 0.8)))
    metal = surface._shade(nt, colour, surface._span(nt, _n(nt, world, 40.0, 3), 0.75, 1.12))
    metal = surface._mix(nt, metal, tuple(min(1.0, c * 1.9 + 0.05) for c in colour), surface._math(nt, "MULTIPLY", edges, 0.45))
    rust_col = surface._shade(nt, (0.24, 0.10, 0.04), surface._span(nt, _n(nt, world, 70.0, 3), 0.6, 1.25))
    tone = surface._mix(nt, metal, rust_col, surface._math(nt, "MULTIPLY", rusty, 0.85))
    metallic = surface._math(nt, "MULTIPLY", _math_inv(nt, rusty), 0.3)
    rough = surface._math(nt, "ADD", 0.5, surface._math(nt, "MULTIPLY", rusty, 0.4))
    n = _bump(nt, _v(nt, world, 40.0, "F1"), 0.35, 0.004)               # hammer dents
    n = _bump(nt, pits, -0.5, 0.0015, n)
    n = _bump(nt, patch, 0.3, 0.002, n)
    surface._finish(nt, bsdf, tone, rough, n, metallic)
    return m


def coin_metal(colour, metallic=0.25):
    """Coin and plate: bright, a little worn, dark only in the cracks. Kept bright and only
    slightly metallic so a heap of gold reads as gold under a torch and not as a hole."""
    m, nt, bsdf = surface._tree("Surface_Coin")
    world = surface._coords(nt)
    edges = surface._edges(nt, 6.0)
    tone = surface._shade(nt, colour, surface._span(nt, _n(nt, world, 60.0, 2), 0.8, 1.1))
    tarnish = _grey(nt, surface._ramp(nt, _n(nt, world, 25.0, 3), [(0.5, 0.0), (0.75, 1.0)]))
    tone = surface._shade(nt, tone, surface._span(nt, tarnish, 1.0, 0.7))
    tone = surface._mix(nt, tone, tuple(min(1.0, c * 1.3 + 0.08) for c in colour), surface._math(nt, "MULTIPLY", edges, 0.7))
    rough = surface._span(nt, tarnish, 0.28, 0.5)
    n = _bump(nt, _n(nt, world, 300.0, 2), 0.2, 0.0008)
    surface._finish(nt, bsdf, tone, rough, n, metallic)
    return m


def _scaled(recipe, *args):
    """A surface.py recipe built at the props' scale."""
    saved = surface.SCALE
    surface.SCALE = SCALE
    try:
        return recipe(*args)
    finally:
        surface.SCALE = saved


def _ours(recipe, *args, **kw):
    saved = surface.SCALE
    surface.SCALE = SCALE
    try:
        return recipe(*args, **kw)
    finally:
        surface.SCALE = saved


# Flat stand-ins by name; register() teaches surface.finish what each becomes.
RECIPES = {
    "Prop_Pine": lambda c, m, r: _ours(plank, c),
    "Prop_Pine_Worn": lambda c, m, r: _ours(plank, c, mould=1.4, rings=48.0),
    "Prop_Cart_Wood": lambda c, m, r: _ours(plank, c, rings=60.0, mould=0.4, mud=1.0),
    "Prop_Oak": lambda c, m, r: _ours(plank, c, rings=70.0),
    "Prop_Oak_Clean": lambda c, m, r: _ours(plank, c, rings=70.0, mould=0.0),
    "Prop_Oak_Dark": lambda c, m, r: _ours(plank, c, rings=80.0, rough=(0.8, 0.6)),
    "Prop_Chest_Wood": lambda c, m, r: _ours(plank, c, paint=CHEST_PAINT, rings=70.0, mould=0.8),
    "Prop_Iron": lambda c, m, r: _ours(iron, c, 0.35),
    "Prop_Iron_Rusty": lambda c, m, r: _ours(iron, c, 0.75),
    "Prop_Leather": lambda c, m, r: _scaled(surface.leather, c),
    "Prop_Cloth": lambda c, m, r: _scaled(surface.cloth, c),
    "Prop_Blanket": lambda c, m, r: _scaled(surface.cloth, c),
    "Prop_Gold": lambda c, m, r: _ours(coin_metal, c, 0.3),
    "Prop_Steel": lambda c, m, r: _scaled(surface.steel, c, 0.5),
    "Prop_Brass": lambda c, m, r: _ours(brass, c),
    "Prop_Silver": lambda c, m, r: _ours(coin_metal, c, 0.3),
    "Prop_Copper": lambda c, m, r: _ours(coin_metal, c, 0.25),
    "Prop_Straw": lambda c, m, r: _ours(straw, c),
    "Prop_Burlap": lambda c, m, r: _ours(burlap, c),
    "Prop_Rope": lambda c, m, r: _ours(rope, c),
    "Prop_Rock": lambda c, m, r: _ours(rock, c),
    "Prop_Glass": lambda c, m, r: _ours(glass, c),
    "Prop_Water": lambda c, m, r: _ours(water, c),
    "Prop_Wax": lambda c, m, r: _scaled(surface.wet, c, 0.5),
    "Prop_Bread": lambda c, m, r: _scaled(surface.leather, c),
    "Prop_Bone": lambda c, m, r: _scaled(surface.bone, c),
    "Prop_Gem_Red": lambda c, m, r: _ours(glass, c),
    "Prop_Gem_Green": lambda c, m, r: _ours(glass, c),
    "Prop_Gem_Blue": lambda c, m, r: _ours(glass, c),
}

FLAT = {
    "Prop_Pine": PINE,
    "Prop_Pine_Worn": (0.33, 0.25, 0.16),
    "Prop_Oak": OAK,
    "Prop_Oak_Clean": OAK,
    "Prop_Oak_Dark": DARK_OAK,
    "Prop_Cart_Wood": CART_ASH,
    "Prop_Chest_Wood": (0.26, 0.17, 0.10),
    "Prop_Iron": IRON,
    "Prop_Iron_Rusty": (0.17, 0.155, 0.145),
    "Prop_Leather": (0.20, 0.10, 0.045),
    "Prop_Cloth": (0.30, 0.25, 0.18),
    "Prop_Blanket": (0.20, 0.17, 0.12),
    "Prop_Gold": (0.95, 0.70, 0.26),
    "Prop_Steel": (0.58, 0.59, 0.62),
    "Prop_Brass": BRASS,
    "Prop_Silver": (0.75, 0.75, 0.74),
    "Prop_Copper": (0.62, 0.30, 0.16),
    "Prop_Straw": STRAW,
    "Prop_Burlap": BURLAP,
    "Prop_Rope": HEMP,
    "Prop_Rock": ROCK,
    "Prop_Glass": (0.10, 0.20, 0.10),
    "Prop_Water": (0.015, 0.02, 0.02),
    "Prop_Wax": (0.80, 0.74, 0.58),
    "Prop_Bread": (0.45, 0.28, 0.12),
    "Prop_Bone": (0.70, 0.64, 0.50),
    "Prop_Gem_Red": (0.55, 0.02, 0.03),
    "Prop_Gem_Green": (0.03, 0.40, 0.12),
    "Prop_Gem_Blue": (0.05, 0.12, 0.55),
    "Prop_Void": (0.008, 0.007, 0.006),
}

WOODS = {"Prop_Pine", "Prop_Pine_Worn", "Prop_Oak", "Prop_Oak_Clean", "Prop_Oak_Dark", "Prop_Chest_Wood", "Prop_Cart_Wood"}


def register():
    surface.RECIPES.update(RECIPES)


def material(name, colour=None):
    """The flat stand-in a builder puts on a part; the bake swaps it for the recipe."""
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    colour = colour or FLAT[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
    m.diffuse_color = (*colour, 1.0)
    return m


# --- weather, after the bake -------------------------------------------------------------------

def _emit_material(name, build):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    emit = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(emit.outputs[0], out.inputs[0])
    nt.links.new(build(nt), emit.inputs[0])
    return m


def _bake_emit(objects, material, size):
    image = bpy.data.images.new(f"weather_{material.name}", size, size, alpha=True, float_buffer=True)
    image.colorspace_settings.name = "Non-Color"
    saved = [(o, [s.material for s in o.material_slots]) for o in objects]
    target = material.node_tree.nodes.new("ShaderNodeTexImage")
    target.image = image
    material.node_tree.nodes.active = target
    for o in objects:
        for s in o.material_slots:
            s.material = material
    surface._select(objects)
    scene = bpy.context.scene
    engine = scene.render.engine
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 1
    bpy.ops.object.bake(type="EMIT", target="IMAGE_TEXTURES", margin=4, use_clear=True)
    scene.render.engine = engine
    for o, mats in saved:
        for s, mat in zip(o.material_slots, mats):
            s.material = mat
    data = surface._pixels(image).copy()
    bpy.data.images.remove(image)
    return data


def weather(objects, name, sources, keep=None):
    """Rust bled down the wood from every iron source, painted into the baked atlas.

    `sources` is a list of (point, reach, strength): a nail head, a rivet, a point along the
    bottom edge of a strap. A streak runs down from each, as far as `reach`, widening a little
    and fading; on a face that looks up it spreads as a stain round the source instead. Only
    wood takes it — the `is_wood` face attribute the builder wrote says where wood is.
    """
    objects = [o for o in objects if o.type == "MESH" and len(o.data.polygons)]
    colour = bpy.data.images.get(f"{name}_colour")
    if colour is None or not sources:
        return
    size = colour.size[0]

    def position(nt):
        p = surface._coords(nt)
        return _vec(nt, "MULTIPLY_ADD", p, (0.25, 0.25, 0.25), None) if False else _offset(nt, p)

    def _offset(nt, p):
        n = nt.nodes.new("ShaderNodeVectorMath")
        n.operation = "MULTIPLY_ADD"
        nt.links.new(p, n.inputs[0])
        n.inputs[1].default_value = (0.25, 0.25, 0.25)
        n.inputs[2].default_value = (0.5, 0.5, 0.5)
        return n.outputs[0]

    def normal_and_mask(nt):
        g = nt.nodes.new("ShaderNodeNewGeometry")
        n = nt.nodes.new("ShaderNodeVectorMath")
        n.operation = "MULTIPLY_ADD"
        nt.links.new(g.outputs["Normal"], n.inputs[0])
        n.inputs[1].default_value = (0.5, 0.5, 0.5)
        n.inputs[2].default_value = (0.5, 0.5, 0.5)
        # Blue carries the wood mask, so one bake gives normal x, y and is-wood; the normal's
        # z is recovered from the other two.
        sep = nt.nodes.new("ShaderNodeSeparateXYZ")
        nt.links.new(n.outputs[0], sep.inputs[0])
        a = nt.nodes.new("ShaderNodeAttribute")
        a.attribute_name = "is_wood"
        a.attribute_type = "GEOMETRY"
        c = nt.nodes.new("ShaderNodeCombineXYZ")
        nt.links.new(sep.outputs[0], c.inputs[0])
        nt.links.new(sep.outputs[1], c.inputs[1])
        nt.links.new(a.outputs["Fac"], c.inputs[2])
        return c.outputs[0]

    pos_mat = _emit_material("Weather_Position", position)
    nrm_mat = _emit_material("Weather_Normal", normal_and_mask)
    pos = _bake_emit(objects, pos_mat, size)
    nrm = _bake_emit(objects, nrm_mat, size)
    bpy.data.materials.remove(pos_mat)
    bpy.data.materials.remove(nrm_mat)

    covered = pos[:, 3] > 0.5
    p = (pos[:, :3] - 0.5) * 4.0
    nx = (nrm[:, 0] - 0.5) * 2
    ny = (nrm[:, 1] - 0.5) * 2
    nz = 1.0 - np.minimum(1.0, nx ** 2 + ny ** 2)
    upward = np.sqrt(np.maximum(nz, 0.0))       # |normal.z|: 1 on a top, 0 on a side
    wood = (nrm[:, 2] > 0.5) & covered

    rust = np.zeros(len(p), dtype=np.float32)
    idx = np.nonzero(wood)[0]
    q = p[idx]
    side = 1.0 - upward[idx]
    hx, hy = nx[idx], ny[idx]
    hl = np.sqrt(hx ** 2 + hy ** 2) + 1e-6
    hx, hy = hx / hl, hy / hl
    rng = np.random.default_rng(len(sources))
    for (point, reach, strength) in sources:
        d = q - np.asarray(point, dtype=np.float32)
        near = np.abs(d).max(axis=1) < reach * 1.2 + 0.03
        if not near.any():
            continue
        dd = d[near]
        below = -dd[:, 2]                                  # how far under the source
        # Across the streak, measured along the face (not through it): the head stands proud
        # of the board, and its distance out of the face is not distance across it.
        out = dd[:, 0] * hx[near] + dd[:, 1] * hy[near]
        across = np.sqrt(np.maximum(dd[:, 0] ** 2 + dd[:, 1] ** 2 - out ** 2, 0.0))
        t = np.clip(below / max(reach, 1e-3), 0, 1)
        width = 0.006 + 0.016 * t
        wobble = 1.0 + 0.35 * np.sin(below * 70.0 + rng.uniform(0, 6.28))
        streak = np.exp(-(across / (width * wobble)) ** 2) * (1.0 - t) ** 1.2
        streak *= (below > -0.006)
        flat = np.sqrt(np.maximum((dd ** 2).sum(axis=1) - out ** 2 * side[near] ** 2, 0.0))
        halo = np.exp(-(flat / 0.016) ** 2)
        stain = np.exp(-(flat / (reach * 0.35)) ** 2) * 0.55
        s = side[near]
        value = s * np.maximum(streak, halo) + (1.0 - s) * np.maximum(stain, halo)
        sub = rust[idx]
        sub[near] = np.maximum(sub[near], value * strength)
        rust[idx] = sub

    rust = np.clip(rust, 0.0, 1.0)
    rgba = surface._pixels(colour)
    tint = np.array([0.24, 0.085, 0.025], dtype=np.float32)
    k = (rust * 0.85)[:, None]
    rgba[:, :3] = rgba[:, :3] * (1.0 - k) + (rgba[:, :3] * 0.35 + tint * 0.65) * k
    colour.pixels.foreach_set(rgba.ravel())
    _repack(colour)
    print(f"WEATHER {name:10} {len(sources):4} sources  {int((rust > 0.1).sum()):7} texels rusted")


def _repack(image):
    """Write a changed atlas back where the GLB exporter reads it: its file, and its packed copy."""
    image.save()
    if image.packed_file is not None:
        image.unpack(method="REMOVE")
    image.reload()
    image.pack()


def _as_jpeg(image, quality):
    folder = os.path.dirname(bpy.path.abspath(image.filepath_raw)) or tempfile.mkdtemp(prefix="ironbound_atlas_")
    stem = os.path.splitext(os.path.basename(image.filepath_raw))[0] or image.name
    image.filepath_raw = os.path.join(folder, f"{stem}.jpg")
    image.file_format = "JPEG"
    image.save(quality=quality)
    if image.packed_file is not None:
        image.unpack(method="REMOVE")
    image.reload()
    image.pack()


def floor_roughness(name, least=0.22):
    """No prop is a mirror. A sliver of a face too thin to be given a texel of its own by the
    bake keeps the ORM atlas's cleared value — roughness 0 — and under a lamp it shone as a
    white line along a board's edge. Nothing here is smoother than wet glass, so roughness is
    held at `least` or above everywhere."""
    image = bpy.data.images.get(f"{name}_orm")
    if image is None:
        return
    px = surface._pixels(image)
    px[:, 1] = np.maximum(px[:, 1], least)
    image.pixels.foreach_set(px.ravel())
    image.update()


def jpeg_colour(name, quality=90, normal_limit=1.1e6):
    """Store the atlases small. The exporter writes an image in the format it is packed in.
    Colour and ORM go to JPEG: a 1024 colour atlas is a megabyte as PNG and a sixth of that as
    JPEG, and roughness and metallic lose nothing anyone can see (Godot recompresses all three
    for the GPU on import anyway). The normal atlas stays PNG unless its noise makes it larger
    than `normal_limit` — cloth and rock do — when it goes to JPEG at a high quality."""
    floor_roughness(name)
    for kind, q in (("colour", quality), ("orm", 92)):
        image = bpy.data.images.get(f"{name}_{kind}")
        if image is not None:
            _as_jpeg(image, q)
    normal = bpy.data.images.get(f"{name}_normal")
    if normal is not None and normal.packed_file is not None and normal.packed_file.size > normal_limit:
        _as_jpeg(normal, 95)

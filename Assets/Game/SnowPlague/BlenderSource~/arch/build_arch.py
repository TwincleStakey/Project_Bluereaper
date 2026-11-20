# Snow plague architecture / landmark props (redo 2): arched walkway-bridge with red railings, one-pillar-row temple
# gate (일주문 style, tiled roof with swept eaves), stone boundary walls with tile caps (straight / broken / corner),
# two tall tiered stone lanterns with lit fire boxes, and two scanned cliff-lip rock faces (Poly Haven, decimated).
# blender -b --factory-startup -P build_arch.py -- <unity SnowPlague dir> <work dir> <rock download dir> [names]
# Sizes in metres, Z up, the long axis of bridge / gate / walls along X. Material slot names are SnowPlague kit slots
# (SP_Stone, SP_StoneLantern, SP_LanternGlow) or SP_Arch_* (made and remapped by SPBuilder.Architecture.cs).
import bpy, bmesh, sys, os, math, random, json
from mathutils import Vector, Matrix, Euler, noise
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from archlib import *

args = sys.argv[sys.argv.index("--") + 1:]
OUT = os.path.join(args[0], "Models", "Arch")
WORK = args[1]
ROCKS = args[2]
ONLY = set(args[3].split(",")) if len(args) > 3 and args[3] else None
os.makedirs(OUT, exist_ok=True)
clear_scene()
REPORT = {}
RED, TIM, DECK, TILE, WALL, STONE, LST, GLOW = ("SP_Arch_RedWood", "SP_Arch_Timber", "SP_Arch_Deck", "SP_Arch_RoofTile",
                                                "SP_Arch_WallStone", "SP_Stone", "SP_StoneLantern", "SP_LanternGlow")


def finish(P, **kw):
    ob = P.build(**kw)
    REPORT[ob.name] = tris(ob)
    export(ob, os.path.join(OUT, ob.name + ".fbx"))
    return ob


def want(n): return ONLY is None or n in ONLY


# =============================================================================================================== gate
def gate():
    P = Piece("SP_Arch_Gate", 11); rng = P.rng
    cols = [(-4.3, 0.25), (-1.9, 0.29), (1.9, 0.29), (4.3, 0.25)]
    top = 4.55
    for x, r in cols:
        # square base slab + octagonal plinth stone
        P.add(box(1.1, 1.1, 0.3, 0.04, 2, top=0.96, jit=0.015, rng=rng), STONE, (x, 0, 0.05), (0, 0, rng.uniform(-3, 3)))
        P.add(cyl(r + 0.2, r + 0.12, 0.4, 8, 0.03), STONE, (x, 0, 0.38), (0, 0, 22.5))
        # column, slight entasis, dark lacquer
        P.add(cyl(r, r * 0.9, top - 0.58, 14, 0.02), RED, (x, 0, 0.58 + (top - 0.58) / 2), grain=2, smooth=True)
        # iron band near the foot
        P.add(cyl(r + 0.015, r + 0.015, 0.1, 14), TIM, (x, 0, 0.75), smooth=True)
    # tie beams (창방 / 평방) and the lower lintel holding the plaque
    P.add(box(10.1, 0.3, 0.42, 0.03, 2), RED, (0, 0, top - 0.12), grain=0)
    P.add(box(10.7, 0.4, 0.2, 0.03, 1), TIM, (0, 0, top + 0.19), grain=0)
    P.add(box(3.8, 0.24, 0.26, 0.025, 1), RED, (0, 0, 3.35), grain=0)
    # plaque (현판): frame + board, leaning forward
    pl = Matrix.Translation((0, -0.32, 3.86)) @ Matrix.Rotation(math.radians(-12), 4, 'X')
    P.add(box(1.7, 0.1, 0.78, 0.02, 1), RED, M=pl, grain=0)
    P.add(box(1.48, 0.06, 0.58, 0.01, 1), TIM, M=pl @ Matrix.Translation((0, -0.04, 0)), grain=0)
    for sx in (-1, 1):   # little hanger posts
        P.add(box(0.08, 0.08, 0.5, 0.0), TIM, (sx * 0.55, -0.2, 4.35), (-12, 0, 0))
    # bracket sets (다포): stacked crossing arms on the beam, projecting front and back
    zb = top + 0.29
    xs = [-5.1 + i * 0.85 for i in range(13)]
    for x in xs:
        P.add(box(0.3, 0.3, 0.17, 0.0, bot=0.8), TIM, (x, 0, zb + 0.085))
        for k in range(3):
            z = zb + 0.17 + k * 0.17 + 0.08
            ly = 0.55 + 0.42 * k
            P.add(box(0.13, ly * 2, 0.15, 0.0, bot=0.9), RED if k == 1 else TIM, (x, 0, z), grain=1)
            P.add(box(0.36 + 0.14 * k, 0.13, 0.15, 0.0), TIM, (x, 0, z), grain=0)
            for sy in (-1, 1):   # small bearing blocks at the arm ends
                P.add(box(0.17, 0.17, 0.09, 0.0, bot=0.75), TIM, (x, sy * (ly - 0.06), z + 0.12))
        # tongue (살미) tips, tapered downward
        for sy in (-1, 1):
            P.add(box(0.12, 0.5, 0.2, 0.0, bot=0.6), RED, (x, sy * 1.45, zb + 0.6), (sy * -18, 0, 0), grain=1)
    # purlins: inner beam + outer round purlins front/back
    zp = zb + 0.78
    P.add(box(11.0, 0.5, 0.26, 0.03, 1), TIM, (0, 0, zp), grain=0)
    for sy in (-1, 1):
        P.add(cyl(0.13, 0.13, 11.6, 8), TIM, (0, sy * 1.55, zp + 0.05), (0, 90, 0), grain=2, smooth=True)
    # roof: ridge z, eave depth D, drop H; concave and swept up at the corners
    zr, D, H, L = 7.35, 2.75, 1.5, 12.6
    surfs = {}
    for sy in (-1, 1):
        surfs[sy] = tile_slope(P, L, D, zr, H, sy, upsweep=0.5, flare=0.28, period=0.34, amp=0.075, rows=11, thick=0.16)
    # rafters (서까래) under the eaves
    x = -L / 2 + 0.35
    while x < L / 2 - 0.3:
        u = x / L + 0.5
        for sy in (-1, 1):
            s = surfs[sy]
            a = s(u, 0.55, False) - Vector((0, 0, 0.2)); b = s(u, 1.0, False) - Vector((0, 0, 0.2)) + (s(u, 1.0, False) - s(u, 0.9, False)).normalized() * 0.05
            d = b - a
            M = Matrix.Translation((a + b) / 2) @ Vector((0, 0, 1)).rotation_difference(d).to_matrix().to_4x4()
            P.add(cyl(0.07, 0.065, d.length, 6), TIM, M=M, grain=2, smooth=True)
        x += 0.3
    # gable boards closing the ends under the roof
    for sx in (-1, 1):
        bm = bmesh.new()
        xg = sx * (L / 2 - 0.55)
        pts = [Vector((xg, -1.7, zp - 0.1)), Vector((xg, 1.7, zp - 0.1)), Vector((xg, 1.7, zr - H * (1 - (1 - 1.7 / D) ** 1.6) - 0.15)),
               Vector((xg, 0, zr - 0.1)), Vector((xg, -1.7, zr - H * (1 - (1 - 1.7 / D) ** 1.6) - 0.15))]
        vs = [bm.verts.new(p) for p in pts]
        f = bm.faces.new(vs)
        bmesh.ops.solidify(bm, geom=[f], thickness=0.08)
        P.add(bm, RED, grain=1)
        # barge board along the gable edge
        P.add(sweep_rect([surfs[1](0.5 + sx * 0.5 - sx * 0.03, v / 6, False) - Vector((0, 0, 0.12)) for v in range(7)], 0.08, 0.28), RED, grain=0)
        P.add(sweep_rect([surfs[-1](0.5 + sx * 0.5 - sx * 0.03, v / 6, False) - Vector((0, 0, 0.12)) for v in range(7)], 0.08, 0.28), RED, grain=0)
    # ridge (용마루): two stacked tile courses, end ornaments (취두) sweeping up
    P.add(box(L - 0.9, 0.42, 0.34, 0.05, 2), TILE, (0, 0, zr + 0.12), grain=0)
    P.add(box(L - 1.1, 0.3, 0.2, 0.05, 2), TILE, (0, 0, zr + 0.38), grain=0)
    for sx in (-1, 1):
        P.add(box(0.42, 0.44, 0.7, 0.06, 2, top=0.8), TILE, (sx * (L / 2 - 0.55), 0, zr + 0.45), (0, sx * -20, 0))
        P.add(box(0.26, 0.3, 0.38, 0.05, 2, top=0.6), TILE, (sx * (L / 2 - 0.35), 0, zr + 0.85), (0, sx * -38, 0))
    return finish(P, ao_height=3.0)


# =============================================================================================================== bridge
def bridge():
    P = Piece("SP_Arch_Bridge", 21); rng = P.rng
    HL, W = 12.0, 2.6
    hd = lambda x: 0.45 + 1.25 * (1 - (x / HL) ** 2)          # deck top
    def slope(x): return math.degrees(math.atan(-2.5 * x / HL ** 2))
    # planks across the deck, a few missing / askew
    x = -HL + 0.15
    while x < HL - 0.1:
        if rng.random() > 0.04 or abs(x) > HL - 1:
            ln = W + rng.uniform(-0.06, 0.12)
            P.add(box(0.27, ln, 0.08, 0.012, 1, jit=0.006, rng=rng), DECK, (x, rng.uniform(-0.05, 0.05), hd(x) - 0.04 + rng.uniform(-0.008, 0.008)),
                  (rng.uniform(-1.5, 1.5), slope(x), rng.uniform(-1.2, 1.2)), grain=1)
        x += 0.3
    path = lambda y, dz, ext=0.0: [Vector((xx, y, hd(xx) + dz)) for xx in [-HL - ext + i * (2 * HL + 2 * ext) / 48 for i in range(49)]]
    # stringers and red fascia beams
    for sy in (-1, 1):
        P.add(sweep_rect(path(sy * 0.85, -0.22), 0.2, 0.28), TIM, grain=0)
        P.add(sweep_rect(path(sy * 1.36, -0.16), 0.12, 0.3), RED, grain=0)
    # piers: stone footings, two posts, a cross beam
    for i in range(8):
        x = -10.5 + i * 3.0
        top = hd(x) - 0.36
        P.add(box(2.6, 0.3, 0.24, 0.03, 1), TIM, (x, 0, top - 0.12), grain=1)
        for sy in (-1, 1):
            P.add(box(0.62, 0.62, 0.5, 0.05, 2, top=0.85, jit=0.03, rng=rng), STONE, (x, sy * 0.95, 0.1), (0, 0, rng.uniform(-10, 10)))
            h = top - 0.24 - 0.35
            if h > 0.1: P.add(box(0.24, 0.24, h, 0.02, 1), TIM, (x, sy * 0.95, 0.35 + h / 2), grain=2)
        if top > 1.2:   # diagonal brace on the tall middle piers
            P.add(box(0.12, 0.12, 2.1, 0.0), TIM, (x, 0, 0.35 + (top - 0.6) / 2), (52, 0, 0), grain=2)
    # stone abutments at both ends
    for sx in (-1, 1):
        for k, (dx, w, h) in enumerate([(0.2, 1.4, 0.55), (-0.55, 1.0, 0.35), (0.9, 0.9, 0.3)]):
            P.add(box(w, W + 0.6 - k * 0.3, h, 0.05, 2, top=0.9, jit=0.035, rng=rng), STONE, (sx * (HL + dx), rng.uniform(-0.1, 0.1), h / 2 - 0.12), (0, 0, rng.uniform(-4, 4)))
    # railings (난간): posts with finials, three rails, short struts and vase blocks
    for sy in (-1, 1):
        y = sy * 1.2
        xs = [-HL + i * 2.0 for i in range(13)]
        broken = rng.randint(2, 9) if sy > 0 else -1
        for i, x in enumerate(xs):
            end = i in (0, 12)
            s = 0.22 if end else 0.16
            ph = 1.25 if end else 1.05
            tilt = rng.uniform(-2, 2)
            P.add(box(s, s, ph, 0.02, 1), RED, (x, y, hd(x) + ph / 2 - 0.05), (tilt, 0, 0), grain=2)
            P.add(box(s + 0.05, s + 0.05, 0.07, 0.015, 1), TIM, (x, y, hd(x) + ph - 0.02))
            P.add(lathe([(0.0, 0), (s * 0.45, 0.02), (s * 0.55, 0.1), (s * 0.4, 0.2), (0.0, 0.3)], 8), TIM, (x, y, hd(x) + ph), smooth=True)
        for (dz, w, h, mat) in ((0.12, 0.1, 0.09, RED), (0.55, 0.07, 0.08, RED), (0.95, 0.11, 0.1, RED)):
            if broken >= 0:
                # one bay of the top rail and mid rail snapped off on the far side
                xa, xb = xs[broken], xs[broken + 1]
                pa = [p for p in path(y, dz) if p.x <= xa + 0.4]; pb = [p for p in path(y, dz) if p.x >= xb - 0.3]
                if dz > 0.3:
                    P.add(sweep_rect(pa, w, h), mat, grain=0); P.add(sweep_rect(pb, w, h), mat, grain=0)
                    continue
            P.add(sweep_rect(path(y, dz), w, h), mat, grain=0)
        # struts between bottom and mid rail, vase blocks under the top rail
        for i in range(24):
            x = -HL + 1.0 + i * 1.0
            if abs(x - round(x / 2) * 2) < 0.01: continue
            if broken >= 0 and xs[broken] < x < xs[broken + 1]: continue
            P.add(box(0.07, 0.07, 0.36, 0.0), RED, (x, y, hd(x) + 0.34), grain=2)
            P.add(box(0.12, 0.09, 0.3, 0.015, 1, top=1.3, bot=0.7), TIM, (x, y, hd(x) + 0.75))
        if broken >= 0:   # the snapped rail lying on the deck
            xm = (xs[broken] + xs[broken + 1]) / 2
            P.add(box(1.5, 0.1, 0.1, 0.01, 1), RED, (xm, y - sy * 0.35, hd(xm) + 0.05), (0, slope(xm), 14), grain=0)
    return finish(P, ao_height=1.5)


# =============================================================================================================== walls
def wall_body(P, L, H, T, seed, crumble=None, rng=None, x0=0.0, rotz=0.0):
    """Stacked-stone wall body (batter, lumpy), L along x centred at x0. crumble(x) -> height factor (broken top)."""
    bm = box(L, T, H, 0.0, top=1.0)
    for v in bm.verts:
        if v.co.z > 0: v.co.y *= 0.8
    bmesh.ops.subdivide_edges(bm, edges=list(bm.edges), cuts=9, use_grid_fill=True)
    for v in bm.verts: v.co.z += H / 2
    if crumble:
        for v in bm.verts:
            f = crumble(v.co.x + x0)
            if f < 1.0 and v.co.z > 0.05: v.co.z *= f
    displace(bm, 0.035, 2.2, seed, keep=lambda co: co.z > 0.03)
    M = Matrix.Rotation(rotz, 4, 'Z') @ Matrix.Translation((x0, 0, 0))
    P.add(bm, WALL, M=M, smooth=False, sc=2.2)
    # foundation course of big stones
    x = -L / 2 + 0.3
    while x < L / 2 - 0.1:
        w = min(rng.uniform(0.45, 0.7), L / 2 + 0.05 - x)
        if w < 0.2: break
        P.add(box(w, T + 0.18, rng.uniform(0.26, 0.34), 0.05, 2, jit=0.03, rng=rng), STONE,
              M=M @ Matrix.Translation((x + w / 2, rng.uniform(-0.03, 0.03), 0.08)) @ Matrix.Rotation(math.radians(rng.uniform(-4, 4)), 4, 'Z'))
        x += w + 0.02


def wall_cap(P, L, z, T, x0=0.0, rotz=0.0, period=0.26):
    """Small tiled gable cap on a wall (기와 담장 지붕)."""
    n0 = len(P.objs)
    P.add(box(L, T + 0.06, 0.08, 0.02, 1), STONE, (x0, 0, z + 0.04))
    for sy in (-1, 1):
        tile_slope(P, L + 0.2, 0.5, z + 0.46, 0.3, sy, upsweep=0.06, flare=0.03, period=period, amp=0.05, rows=4, thick=0.07,
                   x0=x0, exp=1.4, cols_per=3)
    P.add(box(L + 0.1, 0.2, 0.14, 0.03, 1), TILE, (x0, 0, z + 0.5), grain=0)
    if rotz:
        R = Matrix.Rotation(rotz, 4, 'Z')
        for o in P.objs[n0:]: o.data.transform(R)


def wall_straight():
    P = Piece("SP_Arch_Wall_Straight", 31); rng = P.rng
    wall_body(P, 4.0, 1.5, 0.66, 1.0, rng=rng)
    wall_cap(P, 4.0, 1.5, 0.52)
    return finish(P, ao_height=1.2)


def wall_broken():
    P = Piece("SP_Arch_Wall_Broken", 32); rng = P.rng
    # the right half has collapsed: ragged top falling to knee height
    cr = lambda x: 1.0 if x < 0.2 else max(0.3, 1.0 - (x - 0.2) * 0.42 + noise.noise(Vector((x * 3.1, 0.3, 1.7))) * 0.18)
    wall_body(P, 4.0, 1.5, 0.66, 2.0, crumble=cr, rng=rng)
    wall_cap(P, 2.1, 1.5, 0.52, x0=-0.95)
    # rubble and fallen tile chunks
    for i in range(12):
        s = rng.uniform(0.18, 0.42)
        P.add(box(s, s * rng.uniform(0.7, 1.2), s * rng.uniform(0.5, 0.8), 0.04, 1, jit=0.03, rng=rng), STONE,
              (rng.uniform(0.6, 2.6), rng.uniform(-1.1, 1.1) + (0.4 if i % 2 else -0.4), s * 0.2), (rng.uniform(-20, 20), rng.uniform(-20, 20), rng.uniform(0, 360)))
    for i in range(4):
        P.add(box(0.5, 0.32, 0.05, 0.015, 1), TILE, (rng.uniform(0.8, 2.2), rng.uniform(-1.0, 1.0), 0.06), (rng.uniform(-15, 15), rng.uniform(-15, 15), rng.uniform(0, 360)))
    # a long cap section slid off and lying tilted against the base
    P.add(box(1.3, 0.55, 0.16, 0.03, 1), TILE, (1.2, 0.65, 0.3), (38, 0, 8), grain=0)
    return finish(P, ao_height=1.2)


def wall_corner():
    P = Piece("SP_Arch_Wall_Corner", 33); rng = P.rng
    # arm A along +x from the corner, arm B along +y; the corner sits at the origin
    wall_body(P, 3.0, 1.5, 0.66, 3.0, rng=rng, x0=1.5)
    wall_body(P, 3.0, 1.5, 0.66, 4.0, rng=rng, x0=1.5, rotz=math.pi / 2)
    wall_cap(P, 3.0, 1.5, 0.52, x0=1.6)
    wall_cap(P, 3.0, 1.5, 0.52, x0=1.6, rotz=math.pi / 2)
    # corner pier with its own little hip cap hides the cap intersection
    P.add(box(0.9, 0.9, 1.75, 0.05, 2, top=0.92, jit=0.02, rng=rng), STONE, (0, 0, 0.84))
    hip_roof(P, 0.56, 1.8, 0.3, 0.1, upsweep=0.07, mat=TILE, loc=Vector((0, 0, 0)))
    P.add(lathe([(0.0, 0), (0.1, 0.02), (0.12, 0.12), (0.06, 0.22), (0.0, 0.28)], 8), STONE, (0, 0, 2.15), smooth=True)
    return finish(P, ao_height=1.2)


# =============================================================================================================== lanterns
def fire_box(P, z0, h, w, post, rng, tilt=None):
    """Four stone corner posts, sill + lintel bands, window openings with a lattice, and an emissive core."""
    M0 = tilt or Matrix.Identity(4)
    def A(bm, mat, loc, rot=(0, 0, 0), **kw):
        P.add(bm, mat, M=M0 @ Matrix.Translation(loc) @ Euler([math.radians(r) for r in rot]).to_matrix().to_4x4(), **kw)
    for sx in (-1, 1):
        for sy in (-1, 1):
            A(box(post, post, h, 0.015, 1, jit=0.006, rng=rng), LST, (sx * (w / 2 - post / 2), sy * (w / 2 - post / 2), z0 + h / 2))
    for k in range(4):
        rot = (0, 0, 90 * k)
        R = Matrix.Rotation(math.radians(90 * k), 3, 'Z')
        y = -(w / 2 - 0.04)
        A(box(w - 2 * post + 0.02, 0.08, 0.1, 0.01, 1), LST, R @ Vector((0, y, z0 + 0.05)), rot)          # sill
        A(box(w - 2 * post + 0.02, 0.08, 0.08, 0.01, 1), LST, R @ Vector((0, y, z0 + h - 0.04)), rot)     # lintel
        # lattice: one mullion, two rails (dark timber), window glow behind
        A(box(0.03, 0.03, h - 0.18, 0.0), TIM, R @ Vector((0, y, z0 + h / 2)), rot)
        for zz in (0.36, 0.68):
            A(box(w - 2 * post, 0.03, 0.025, 0.0), TIM, R @ Vector((0, y, z0 + h * zz)), rot)
    A(box(w - 2 * post + 0.04, w - 2 * post + 0.04, h - 0.12, 0.02, 1), GLOW, (0, 0, z0 + h / 2))


def lantern_a():
    """3.3 m: three stepped square plinths, octagonal shaft, lotus capital, lit fire box, concave hip roof, jewel."""
    P = Piece("SP_Arch_Lantern_A", 41); rng = P.rng
    z = -0.06
    for (w, h, top) in ((1.3, 0.24, 0.96), (1.02, 0.2, 0.95), (0.8, 0.18, 0.86)):
        P.add(box(w, w, h, 0.03, 2, top=top, jit=0.012, rng=rng), LST, (0, 0, z + h / 2), (0, 0, rng.uniform(-2, 2)))
        z += h
    # lotus petal ring on the top plinth (하대석 연화)
    for k in range(8):
        a = k * math.pi / 4
        P.add(box(0.22, 0.1, 0.12, 0.03, 2, top=0.6), LST, (math.cos(a) * 0.3, math.sin(a) * 0.3, z + 0.03), (25, 0, math.degrees(a) + 90))
    P.add(cyl(0.2, 0.19, 0.08, 8, 0.015), LST, (0, 0, z + 0.04), (0, 0, 22.5))
    z += 0.08
    P.add(cyl(0.165, 0.15, 0.95, 8, 0.012), LST, (0, 0, z + 0.475), (0, 0, 22.5), grain=2)
    P.add(cyl(0.19, 0.19, 0.06, 8, 0.01), LST, (0, 0, z + 0.5), (0, 0, 22.5))   # band
    z += 0.95
    P.add(box(0.66, 0.66, 0.18, 0.03, 2, top=1.0, bot=0.72), LST, (0, 0, z + 0.09))
    z += 0.18
    fire_box(P, z, 0.74, 0.56, 0.1, rng)
    z += 0.74
    P.add(box(0.6, 0.6, 0.06, 0.015, 1, bot=0.94), LST, (0, 0, z + 0.03))
    hip_roof(P, 0.5, z + 0.16, 0.36, 0.11, upsweep=0.09, exp=1.8, loc=Vector((0, 0, 0)))
    zt = z + 0.16 + 0.36
    P.add(box(0.18, 0.18, 0.1, 0.02, 1), LST, (0, 0, zt + 0.02))
    P.add(lathe([(0.0, 0), (0.08, 0.01), (0.13, 0.08), (0.12, 0.16), (0.06, 0.24), (0.0, 0.32)], 10), LST, (0, 0, zt + 0.07), smooth=True)
    return finish(P, ao_height=1.5)


def lantern_b():
    """2.8 m, older and heavier: two rough plinths, square shaft, wide fire box, broad flat roof, small chipped jewel; leans."""
    P = Piece("SP_Arch_Lantern_B", 42); rng = P.rng
    z = -0.06
    for (w, h, top) in ((1.45, 0.3, 0.9), (1.0, 0.26, 0.88)):
        P.add(box(w, w * 0.95, h, 0.05, 2, top=top, jit=0.035, rng=rng), LST, (0, 0, z + h / 2), (0, 0, rng.uniform(-4, 4)))
        z += h
    tiltM = Matrix.Translation((0, 0, z)) @ Matrix.Rotation(math.radians(3.0), 4, 'X') @ Matrix.Rotation(math.radians(-1.5), 4, 'Y') @ Matrix.Translation((0, 0, -z))
    def T(bm, mat, loc, rot=(0, 0, 0), **kw):
        P.add(bm, mat, M=tiltM @ Matrix.Translation(loc) @ Euler([math.radians(r) for r in rot]).to_matrix().to_4x4(), **kw)
    T(box(0.34, 0.34, 0.75, 0.025, 1, top=0.88, jit=0.012, rng=rng), LST, (0, 0, z + 0.375), grain=2)
    z += 0.75
    T(box(0.78, 0.78, 0.16, 0.03, 2, bot=0.7, jit=0.012, rng=rng), LST, (0, 0, z + 0.08))
    z += 0.16
    fire_box(P, z, 0.68, 0.62, 0.11, rng, tilt=tiltM)
    z += 0.68
    T(box(0.7, 0.7, 0.05, 0.015, 1), LST, (0, 0, z + 0.025))
    n0 = len(P.objs)
    hip_roof(P, 0.62, z + 0.14, 0.28, 0.13, upsweep=0.12, exp=2.2, loc=Vector((0, 0, 0)), rotz=math.radians(4))
    for o in P.objs[n0:]: o.data.transform(tiltM)
    zt = z + 0.14 + 0.28
    T(box(0.2, 0.2, 0.08, 0.02, 1), LST, (0, 0, zt + 0.01))
    T(lathe([(0.0, 0), (0.09, 0.02), (0.12, 0.09), (0.08, 0.17), (0.0, 0.21)], 8), LST, (0, 0, zt + 0.05), smooth=True)
    return finish(P, ao_height=1.5)


# =============================================================================================================== rocks
def rock(name, src, size, mat, ratio_tris=11000, seed=51):
    bpy.ops.object.select_all(action='DESELECT')
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=src)
    obs = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
    for o in [o for o in bpy.data.objects if o not in before and o.type != 'MESH']: bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in obs: o.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    if len(obs) > 1: bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.parent = None
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    me = ob.data
    t = tris(ob)
    if t > ratio_tris:
        m = ob.modifiers.new("dec", 'DECIMATE'); m.ratio = ratio_tris / t
        bpy.ops.object.modifier_apply(modifier=m.name)
    # normalise: largest horizontal extent -> size, sitting on z=0 (slightly sunk), centred
    vs = [v.co for v in me.vertices]
    mn = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
    mx = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    s = size / max(mx.x - mn.x, mx.y - mn.y)
    c = (mn + mx) / 2
    me.transform(Matrix.Scale(s, 4) @ Matrix.Translation((-c.x, -c.y, -mn.z - 0.04 * (mx.z - mn.z))))
    me.materials.clear(); me.materials.append(bpy.data.materials.get(mat) or bpy.data.materials.new(mat))
    for p in me.polygons: p.material_index = 0; p.use_smooth = True
    # kit vertex colours
    for a in list(me.color_attributes): me.color_attributes.remove(a)
    ca = me.color_attributes.new("Col", 'BYTE_COLOR', 'CORNER')
    zmin = min(v.co.z for v in me.vertices); zmax = max(v.co.z for v in me.vertices)
    off = Vector((seed, seed * 0.3, 0))
    me.update()
    for poly in me.polygons:
        for li, vi in zip(poly.loop_indices, poly.vertices):
            v = me.vertices[vi]; p = v.co; n = v.normal
            top = smooth01(0.3, 0.85, n.z + noise.noise(p * 2.0 + off) * 0.2)
            patch = smooth01(0.25, 0.7, 0.55 + noise.noise(p * 0.6 + off) * 0.5 + noise.noise(p * 2.5 + off) * 0.25)
            ao = 0.45 + 0.55 * smooth01(0.0, (zmax - zmin) * 0.5, p.z - zmin)
            ca.data[li].color = (top * (0.3 + 0.7 * patch), ao, 0.5, 0.0)
    ob.name = name; me.name = name
    REPORT[name] = tris(ob)
    export(ob, os.path.join(OUT, name + ".fbx"))
    return ob


# =============================================================================================================== run
built = []
if want("gate"): built.append(gate())
if want("bridge"): built.append(bridge())
if want("walls"): built += [wall_straight(), wall_broken(), wall_corner()]
if want("lanterns"): built += [lantern_a(), lantern_b()]
if want("rocks"):
    built.append(rock("SP_Arch_RockFace_A", os.path.join(ROCKS, "rock_face_01_1k.fbx"), 7.0, "SP_Arch_Rock01", seed=51))
    built.append(rock("SP_Arch_RockFace_B", os.path.join(ROCKS, "rock_face_02_1k.fbx"), 7.0, "SP_Arch_Rock02", seed=52))
for o in built:
    vs = [v.co for v in o.data.vertices]
    REPORT[o.name] = {"tris": REPORT[o.name], "min": [round(min(v[i] for v in vs), 2) for i in range(3)], "max": [round(max(v[i] for v in vs), 2) for i in range(3)]}
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(WORK, "sp_arch.blend"))
print("ARCH_REPORT " + json.dumps(REPORT))

# Snow plague kit, part 2: foreground red pines (적송) with needle cards, red maple bushes, stone lanterns (석등) with
# a glow core and a light-cone mesh, ground scatter (pebbles, stone chips, twigs, bones, snow clumps) and snow drifts.
# blender -b --factory-startup -P build_kit2.py -- <unity SnowPlague dir> <work dir>
# Cards use SP_FoliageAtlas.png (make_textures.py): pine (0,.5)-(.5,1), maple (.5,.5)-(1,1), grass (0,0)-(.5,.5), twig (.5,0)-(1,.5).
import bpy, sys, os, math, random, json
from mathutils import Vector, Matrix, noise
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from splib import *

args = sys.argv[sys.argv.index("--") + 1:]
OUT = os.path.join(args[0], "Models")
WORK = args[1]
os.makedirs(OUT, exist_ok=True); os.makedirs(WORK, exist_ok=True)
clear_scene()
REPORT = {}
PINE_UV = (0.0, 0.5, 0.5, 1.0); MAPLE_UV = (0.5, 0.5, 1.0, 1.0); GRASS_UV = (0.0, 0.0, 0.5, 0.5); TWIG_UV = (0.5, 0.0, 1.0, 0.5)


def pad(K, rng, c, radius, n, uv=PINE_UV, mat="SP_Needles", flat=0.35, size=(0.9, 1.1), sway=0.6):
    """Umbrella needle pad: mostly horizontal cards fanning out from c, normals from a flattened dome so the pad
    shades as one soft mass (and catches snow on top)."""
    for i in range(n):
        a = rng.uniform(0, 2 * math.pi) if i > 2 else i * 2.1
        r = radius * math.sqrt(rng.random()) * 0.75
        out = Vector((math.cos(a), math.sin(a), 0))
        base = c + out * r * 0.5 + Vector((0, 0, rng.uniform(-0.12, 0.12)))
        ay = (out + Vector((0, 0, rng.uniform(-0.15, 0.35)))).normalized()
        ax = Vector((-out.y, out.x, 0)).normalized()
        ax = (ax + Vector((0, 0, rng.uniform(-0.25, 0.25)))).normalized()
        L = radius * rng.uniform(*size) * 0.8
        K.card(base, ax, ay, L * 0.95, L, uv, mat, normal=lambda p, c=c: ((p - c) * Vector((1, 1, 0)) * (1 / max(radius, 0.1)) + Vector((0, 0, 1.6))).normalized(),
               sway=(sway * 0.6, sway), rnd=rng.random())
    # a couple of upright cards round the rim so the pad has some thickness from the 70 degree view
    for i in range(max(2, n // 5)):
        a = rng.uniform(0, 2 * math.pi); out = Vector((math.cos(a), math.sin(a), 0))
        K.card(c + out * radius * 0.45, Vector((-out.y, out.x, 0)), (out * 0.5 + Vector((0, 0, 1))).normalized(),
               radius * 0.7, radius * 0.55, uv, mat, normal=(out + Vector((0, 0, 1.2))).normalized(), sway=(sway * 0.6, sway), rnd=rng.random())


def red_pine(name, seed, height=10.0, layers=4, lean=(0.6, 0.0)):
    """적송: S-curved trunk leaning out over the arena, flat umbrella layers of needle pads near the top."""
    rng = random.Random(seed); K = Kit(name)
    pts = []
    for k in range(13):
        t = k / 12
        x = lean[0] * height * 0.18 * math.sin(t * math.pi * 1.3) + lean[0] * t * t * height * 0.12
        y = lean[1] * height * 0.1 * math.sin(t * math.pi) + 0.25 * math.sin(t * 7 + seed)
        pts.append(Vector((x, y, -0.2 + t * height)))
    jag = lambda i, k, t: noise.noise(Vector((k * 0.9, i * 0.6, seed))) * 0.08 + 0.3 * (1 - smooth01(0, 0.1, t)) * (0.5 + 0.5 * math.sin(k * 2.3))
    K.tube(pts, lambda t: 0.3 * (1 - 0.7 * t) + 0.03, 9, "SP_PineBark", sway=lambda t: 0.12 * t * t, cap_end=True, jag=jag)
    for k in range(5):   # root flare
        a = k * 2 * math.pi / 5 + rng.uniform(-0.3, 0.3); o = Vector((math.cos(a), math.sin(a), 0))
        K.tube([Vector((0, 0, 0.5)) + o * 0.1, o * 0.45 + Vector((0, 0, 0.05)), o * 0.95 + Vector((0, 0, -0.1))], lambda t: 0.15 * (1 - t) ** 1.2 + 0.01, 5, "SP_PineBark")
    tops = []
    for L in range(layers):
        f = 0.48 + 0.5 * L / max(1, layers - 1)
        i = min(12, int(f * 12)); p = pts[i]
        nb = 2 if L < layers - 1 else 3
        for b in range(nb):
            a = rng.uniform(0, 2 * math.pi) + b * 2.4
            reach = height * rng.uniform(0.16, 0.26) * (1.2 - 0.4 * L / layers)
            d = Vector((math.cos(a), math.sin(a), rng.uniform(0.15, 0.4)))
            bp = gnarl_path(rng, p, d, reach, 0.5, 0.3, -0.03, seed + L * 3 + b)
            K.tube(bp, lambda t: 0.09 * (1 - 0.7 * t) + 0.015, 5, "SP_PineBark", sway=lambda t: 0.15 + 0.4 * t)
            end = bp[-1]
            pad(K, rng, end + Vector((0, 0, 0.15)), rng.uniform(1.2, 1.7), rng.randint(26, 34), size=(1.1, 1.4))
            mid = bp[len(bp) // 2]
            if rng.random() < 0.7: pad(K, rng, mid + Vector((0, 0, 0.12)), rng.uniform(0.8, 1.1), rng.randint(14, 18), size=(1.1, 1.4))
            # dead twigs under the pads
            for tw in range(2):
                K.card(bp[-2], Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), 0)), Vector((0, 0, -1)) + d * 0.3, 0.9, 0.9, TWIG_UV, "SP_Needles",
                       normal=Vector((0, 0, 1)), sway=(0.3, 0.6), rnd=rng.random())
        tops.append(p)
    pad(K, rng, pts[-1] + Vector((0, 0, 0.1)), 1.6, 34, size=(1.1, 1.4))
    return K


def maple_bush(name, seed, height=1.6, clusters=8):
    """Red maple bush (단풍): a few dark stems with leaf-cluster cards at the tips (the only saturated foliage)."""
    rng = random.Random(seed); K = Kit(name)
    ends = []
    for s in range(rng.randint(4, 6)):
        a = rng.uniform(0, 2 * math.pi); tilt = rng.uniform(10, 40)
        d = Vector((math.cos(a) * math.sin(math.radians(tilt)), math.sin(a) * math.sin(math.radians(tilt)), math.cos(math.radians(tilt))))
        pts = gnarl_path(rng, Vector((0, 0, -0.05)), d, height * rng.uniform(0.6, 1.0), 0.25, 0.4, 0.05, seed + s)
        K.tube(pts, lambda t: 0.03 * (1 - 0.8 * t) + 0.005, 4, "SP_Bark", sway=lambda t: t)
        ends += [pts[-1], pts[len(pts) * 2 // 3]]
    for i in range(clusters):
        c = ends[i % len(ends)] + Vector((rng.uniform(-0.2, 0.2), rng.uniform(-0.2, 0.2), rng.uniform(-0.1, 0.1)))
        cen = Vector((0, 0, height * 0.6))
        for k in range(3):
            ax = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), 0)).normalized()
            ay = (Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(0.2, 1.0)))).normalized()
            K.card(c, ax, ay, 0.8, 0.8, MAPLE_UV, "SP_Maple", normal=lambda p, cen=cen: (p - cen + Vector((0, 0, 0.4))).normalized(),
                   sway=(0.6, 1.0), rnd=rng.random(), pivot=0.5)
    return K


def dry_grass_cards(name, seed, n=5, h=0.7):
    """Dry grass clump from crossed tight cards (far/edge use; near the play area the mesh tufts are used)."""
    rng = random.Random(seed); K = Kit(name)
    for i in range(n):
        a = i * math.pi / n + rng.uniform(-0.2, 0.2)
        ax = Vector((math.cos(a), math.sin(a), 0))
        K.card(Vector((rng.uniform(-0.1, 0.1), rng.uniform(-0.1, 0.1), -0.02)), ax, Vector((rng.uniform(-0.15, 0.15), rng.uniform(-0.15, 0.15), 1)),
               h * 0.9, h * rng.uniform(0.8, 1.1), GRASS_UV, "SP_GrassCard", normal=lambda p: (p * Vector((1, 1, 0)) + Vector((0, 0, 0.8))).normalized(),
               sway=(0.0, 1.0), rnd=rng.random())
    return K


# ---- stone lanterns --------------------------------------------------------------------------------------------
R45 = Matrix.Rotation(math.radians(45), 3, 'Z')


def stone_lantern(name, seed, broken=False):
    rng = random.Random(seed); K = Kit(name)
    z = 0.0
    for w, h in ((1.15, 0.16), (0.9, 0.16), (0.66, 0.14)):
        K.box((0, 0, z + h / 2), (w, w, h), "SP_Stone", jitter=0.012, rng=rng); z += h
    K.lathe([(0.3, z), (0.33, z + 0.05), (0.24, z + 0.16), (0.15, z + 0.18)], 8, "SP_Stone"); z += 0.18
    ph = 0.95 if not broken else 0.5
    K.tube([Vector((0, 0, z - 0.02)), Vector((0, 0, z + ph))], lambda t: 0.13 - 0.02 * t, 8, "SP_Stone",
           jag=(lambda i, k, t: (0.25 if k % 2 else -0.2) * (t > 0.99)) if broken else None, cap_end=broken)
    if broken:
        # the upper half lies on the ground beside the base, the roof cracked off
        top = Kit("tmp")
        top.tube([Vector((0, 0, 0)), Vector((0, 0, 0.45))], lambda t: 0.11, 8, "SP_Stone", cap_start=True)
        top.lathe([(0.0, 0.45), (0.55, 0.45), (0.58, 0.5), (0.42, 0.6), (0.18, 0.74), (0.07, 0.8), (0.0, 0.82)], 4, "SP_Stone", rot=R45)
        M = Matrix.Rotation(math.radians(82), 3, 'Y'); T = Vector((1.05, 0.35, 0.2))
        base = len(K.v)
        for p, uv, ex in zip(top.v, top.uv, top.extra): K.vert(M @ p + T, uv, *ex)
        for idx, mi in top.f: K.face([base + i for i in idx], top.mats[mi])
        return K
    z += ph
    K.lathe([(0.15, z), (0.3, z + 0.08), (0.34, z + 0.14), (0.0, z + 0.14)], 8, "SP_Stone"); z += 0.14
    # firebox: corner posts, sills, a lattice bar per window, glowing core
    fb, fh = 0.5, 0.46
    K.box((0, 0, z + 0.03), (fb + 0.06, fb + 0.06, 0.06), "SP_Stone")
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box((sx * (fb / 2 - 0.05), sy * (fb / 2 - 0.05), z + fh / 2), (0.1, 0.1, fh), "SP_Stone", jitter=0.006, rng=rng)
    for k in range(4):
        a = k * math.pi / 2; o = Vector((math.cos(a), math.sin(a), 0)); s = Vector((-o.y, o.x, 0))
        K.box(o * (fb / 2 - 0.03) + Vector((0, 0, z + fh / 2)), (0.03, 0.03, fh - 0.08) if False else (0.03, 0.03, fh - 0.08), "SP_Wood",
              rot=Matrix.Rotation(a, 3, 'Z'))
    K.box((0, 0, z + fh * 0.45), (0.3, 0.3, 0.3), "SP_LanternGlow")
    z += fh
    K.box((0, 0, z + 0.03), (fb + 0.06, fb + 0.06, 0.06), "SP_Stone"); z += 0.06
    # roof: square, flared, upturned corners (4-sided lathe turned 45 degrees)
    K.lathe([(0.0, z), (0.6, z), (0.66, z + 0.07), (0.5, z + 0.14), (0.24, z + 0.3), (0.1, z + 0.38), (0.0, z + 0.4)], 4, "SP_Stone", rot=R45)
    z += 0.4
    K.lathe([(0.0, z - 0.02), (0.08, z), (0.11, z + 0.08), (0.06, z + 0.18), (0.0, z + 0.24)], 8, "SP_Stone")
    return K


def light_cone(name):
    """Light shaft under a lantern: open cone 1 m long, 1 m radius at the floor. UV.y = 0 at the source."""
    K = Kit(name)
    prof = [(0.12, 0.0), (0.4, -0.3), (0.75, -0.65), (1.0, -1.0)]
    rows = []
    for i, (r, z) in enumerate(prof):
        row = []
        for k in range(17):
            a = 2 * math.pi * k / 16
            row.append(K.vert((math.cos(a) * r, math.sin(a) * r, z), (k / 16, -z)))
        rows.append(row)
    for i in range(len(rows) - 1):
        for k in range(16): K.face((rows[i][k], rows[i + 1][k], rows[i + 1][k + 1], rows[i][k + 1]), "SP_LightCone")
    return K


# ---- scatter -------------------------------------------------------------------------------------------------
def pebble(name, seed, r=(0.08, 0.06, 0.04), sharp=False):
    K = Kit(name)
    K.blob(Vector((0, 0, r[2] * 0.4)), r, "SP_Stone", subdiv=1 if sharp else 2, lumpy=0.35 if sharp else 0.18, flatten_below=-r[2] * 0.3, seed=seed)
    return K


def pebble_cluster(name, seed, n=7, spread=0.35):
    rng = random.Random(seed); K = Kit(name)
    for i in range(n):
        s = rng.uniform(0.03, 0.09)
        K.blob(Vector((rng.uniform(-spread, spread), rng.uniform(-spread, spread), s * 0.25)), (s * rng.uniform(1, 1.5), s, s * rng.uniform(0.5, 0.8)),
               "SP_Stone", subdiv=1, lumpy=0.22, flatten_below=-s * 0.2, seed=seed + i, rot=Matrix.Rotation(rng.uniform(0, 6.28), 3, 'Z'))
    return K


def bone(name, seed, L=0.38):
    rng = random.Random(seed); K = Kit(name)
    K.tube([Vector((-L / 2, 0, 0.025)), Vector((0, 0.01, 0.028)), Vector((L / 2, 0, 0.025))], lambda t: 0.018 + 0.006 * abs(t - 0.5) * 2, 6, "SP_Bone")
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.blob(Vector((sx * L / 2, sy * 0.016, 0.028)), (0.024, 0.022, 0.022), "SP_Bone", subdiv=1, lumpy=0.1, seed=seed + sx + sy * 3)
    return K


def twig_bit(name, seed):
    rng = random.Random(seed); K = Kit(name)
    branch(K, rng, Vector((-0.3, 0, 0.015)), Vector((1, rng.uniform(-0.3, 0.3), 0)), 0.6, 0.014, 0, 1, "SP_Bark", 4, child_n=(1, 2),
           spread=(25, 45), step=0.2, wob=0.3, tropism=-0.04, seed=seed)
    for i, p in enumerate(K.v): K.v[i] = Vector((p.x, p.y, max(0.004, p.z * 0.2 + 0.012)))
    return K


def snow_clump(name, seed, r=(0.25, 0.18, 0.08)):
    K = Kit(name)
    K.blob(Vector((0, 0, 0)), r, "SP_Snow", subdiv=2, lumpy=0.25, flatten_below=0.0, seed=seed)
    return K


def snow_drift(name, seed, r=(2.0, 1.2, 0.4), lumps=3):
    rng = random.Random(seed); K = Kit(name)
    K.blob(Vector((0, 0, 0)), r, "SP_Snow", subdiv=3, lumpy=0.18, flatten_below=-0.02, seed=seed)
    for i in range(lumps):
        c = Vector((rng.uniform(-r[0], r[0]) * 0.6, rng.uniform(-r[1], r[1]) * 0.5, 0))
        K.blob(c, (r[0] * 0.4, r[1] * 0.45, r[2] * rng.uniform(0.6, 1.1)), "SP_Snow", subdiv=2, lumpy=0.2, flatten_below=-0.02, seed=seed + i + 1)
    # sink the bottom just under the ground
    for i, p in enumerate(K.v): K.v[i] = Vector((p.x, p.y, p.z - 0.05))
    return K


JOBS = []
def job(K, frost=1.0, bias=0.0, ao=0.6, fn=0.35):
    ob = K.build(frost=frost, frost_bias=bias, ao_height=ao, seed=hash(K.name) % 97, frost_noise=fn)
    JOBS.append(ob)
    REPORT[ob.name] = {"tris": tris(ob), "mats": [m.name for m in ob.data.materials],
                       "size": [round(ob.dimensions.x, 2), round(ob.dimensions.y, 2), round(ob.dimensions.z, 2)]}
    print("built", ob.name, REPORT[ob.name])


job(red_pine("SP_RedPine_A", 201, 11.0, 4, (0.7, 0.1)), ao=3.0, bias=0.15)
job(red_pine("SP_RedPine_B", 202, 9.0, 3, (-0.5, 0.3)), ao=3.0, bias=0.15)
job(red_pine("SP_RedPine_C", 203, 12.5, 5, (0.3, -0.5)), ao=3.0, bias=0.15)
job(maple_bush("SP_MapleBush_A", 211, 1.6, 9), ao=0.8, bias=0.1)
job(maple_bush("SP_MapleBush_B", 212, 2.4, 13), ao=1.2, bias=0.1)
job(dry_grass_cards("SP_GrassCards", 221), ao=0.4, frost=0.6)
job(stone_lantern("SP_StoneLantern", 231), ao=1.2)
job(stone_lantern("SP_StoneLantern_Broken", 232, broken=True), ao=0.8)
job(light_cone("SP_LightCone"), frost=0.0)
for i, (r, sharp) in enumerate([((0.09, 0.07, 0.045), False), ((0.13, 0.08, 0.05), False), ((0.06, 0.05, 0.04), False), ((0.18, 0.12, 0.07), False)]):
    job(pebble(f"SP_Pebble_{'ABCD'[i]}", 300 + i, r, sharp), ao=0.05)
job(pebble("SP_StoneChip_A", 310, (0.12, 0.07, 0.035), True), ao=0.04)
job(pebble("SP_StoneChip_B", 311, (0.2, 0.1, 0.05), True), ao=0.05)
job(pebble_cluster("SP_PebbleCluster", 320), ao=0.05)
job(bone("SP_Bone", 330), ao=0.03)
job(twig_bit("SP_TwigBit", 340), ao=0.02)
job(snow_clump("SP_SnowClump", 350), ao=0.05, bias=0.3)
job(snow_drift("SP_SnowDrift_A", 360), ao=0.3, bias=0.4)
job(snow_drift("SP_SnowDrift_B", 361, (1.3, 0.9, 0.3), 2), ao=0.25, bias=0.4)
job(snow_drift("SP_SnowBank", 362, (3.5, 0.9, 0.45), 4), ao=0.3, bias=0.4)

x = 0.0; y = 0.0
for ob in JOBS:
    w = max(ob.dimensions.x, 1.0)
    if x > 30: x = 0.0; y -= 12.0
    ob.location = (x + w * 0.5, y, 0); x += w + 1.0
for ob in JOBS:
    loc = ob.location.copy(); ob.location = (0, 0, 0)
    export(ob, os.path.join(OUT, ob.name + ".fbx"))
    ob.location = loc
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(WORK, "sp_kit2.blend"))
with open(os.path.join(WORK, "kit2_report.json"), "w") as f: json.dump(REPORT, f, indent=1)
print("DONE", len(JOBS))

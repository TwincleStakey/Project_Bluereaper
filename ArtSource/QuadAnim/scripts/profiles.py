# Source-rig profiles for retarget.py. Each profile maps the 장산범 AutoRig quadruped bones (target) to
# points on the source skeleton. A point is (bone, 'h'|'t') = that bone's head/tail in world space.
#   aim[target] = (from_point, to_point, twist_bone)  -> target bone points from->to, roll follows twist_bone
#   pelvis      = source bone whose head drives the Hips translation
#   legs        = per target leg: (target upper, lower, paw, toes) <- (source joint point, source toe-base point)
# New source rig = new profile here (copy one, rename bones). Target rig is the same for every AutoRig boss.

TARGET_LEGS = {
    'hind.L': ('Thigh.L', 'Shin.L', 'HindPaw.L', 'HindToes.L'),
    'hind.R': ('Thigh.R', 'Shin.R', 'HindPaw.R', 'HindToes.R'),
    'front.L': ('UpperArm.L', 'Forearm.L', 'FrontPaw.L', 'FrontToes.L'),
    'front.R': ('UpperArm.R', 'Forearm.R', 'FrontPaw.R', 'FrontToes.R'),
}


def _side(d, s):
    return {k.replace('.X', s): tuple((v.replace('.X', s) if isinstance(v, str) else (v[0].replace('.X', s), v[1])) for v in val)
            for k, val in d.items()}


# ---------------------------------------------------------------- Siberian Tiger (animalanimator, Rigify-like DEF-)
_tiger_limbs = {
    'Scapula.X':   (('DEF-scapula.X', 'h'), ('DEF-scapula.X', 't'), 'DEF-scapula.X'),
    'UpperArm.X':  (('DEF-upper_arm.X', 'h'), ('DEF-forearm.X', 'h'), 'DEF-upper_arm.X'),
    'Forearm.X':   (('DEF-forearm.X', 'h'), ('DEF-hand.X', 'h'), 'DEF-forearm.X'),
    'FrontPaw.X':  (('DEF-hand.X', 'h'), ('DEF-digit_3.01.X', 'h'), 'DEF-hand.X'),
    'FrontToes.X': (('DEF-digit_3.01.X', 'h'), ('DEF-digit_3.03.X', 't'), 'DEF-digit_3.01.X'),
    'Thigh.X':     (('DEF-thigh.X', 'h'), ('DEF-shin.X', 'h'), 'DEF-thigh.X'),
    'Shin.X':      (('DEF-shin.X', 'h'), ('DEF-foot.X', 'h'), 'DEF-shin.X'),
    'HindPaw.X':   (('DEF-foot.X', 'h'), ('DEF-toe_3.01.X', 'h'), 'DEF-foot.X'),
    'HindToes.X':  (('DEF-toe_3.01.X', 'h'), ('DEF-toe_3.03.X', 't'), 'DEF-toe_3.01.X'),
}
TIGER = {
    'aim': {
        'Hips':   (('DEF-spine.01', 'h'), ('DEF-spine.02', 'h'), 'DEF-spine.01'),
        'Spine1': (('DEF-spine.02', 'h'), ('DEF-spine.03', 'h'), 'DEF-spine.02'),
        'Spine2': (('DEF-spine.03', 'h'), ('DEF-spine.05', 'h'), 'DEF-spine.04'),
        'Spine3': (('DEF-spine.05', 'h'), ('DEF-neck.01', 'h'), 'DEF-spine.06'),
        'Neck1':  (('DEF-neck.01', 'h'), ('DEF-neck.02', 'h'), 'DEF-neck.01'),
        'Neck2':  (('DEF-neck.02', 'h'), ('DEF-head', 'h'), 'DEF-neck.03'),
        'Head':   (('DEF-head', 'h'), ('DEF-head', 't'), 'DEF-head'),
        **_side(_tiger_limbs, '.L'), **_side(_tiger_limbs, '.R'),
    },
    'pelvis': 'DEF-spine.01',
    'legs': {
        'hind.L': (('DEF-thigh.L', 'h'), ('DEF-toe_3.01.L', 'h')),
        'hind.R': (('DEF-thigh.R', 'h'), ('DEF-toe_3.01.R', 'h')),
        'front.L': (('DEF-upper_arm.L', 'h'), ('DEF-digit_3.01.L', 'h')),
        'front.R': (('DEF-upper_arm.R', 'h'), ('DEF-digit_3.01.R', 'h')),
    },
    'armature': 'Tiger_Rig',
    'ref': ('01_Idle', 0),
}

# ---------------------------------------------------------------- Smilodon (BPoole, Maya AdvancedSkeleton DeformationSystem)
_smil_limbs = {   # AdvancedSkeleton quadruped: *Rump = hip/shoulder joint, *Hip = knee/elbow, *Knee = hock/wrist, *Ankle = paw
    'Scapula.X':   (('Scapular.X', 'h'), ('frontRump.X', 'h'), 'Scapular.X'),
    'UpperArm.X':  (('frontRump.X', 'h'), ('frontHip.X', 'h'), 'frontRump.X'),
    'Forearm.X':   (('frontHip.X', 'h'), ('frontKnee.X', 'h'), 'frontHip.X'),
    'FrontPaw.X':  (('frontKnee.X', 'h'), ('frontToes.X', 'h'), 'frontKnee.X'),
    'FrontToes.X': (('frontToes.X', 'h'), ('frontToesEnd.X', 'h'), 'frontToes.X'),
    'Thigh.X':     (('backRump.X', 'h'), ('backHip.X', 'h'), 'backRump.X'),
    'Shin.X':      (('backHip.X', 'h'), ('backKnee.X', 'h'), 'backHip.X'),
    'HindPaw.X':   (('backKnee.X', 'h'), ('backToes.X', 'h'), 'backKnee.X'),
    'HindToes.X':  (('backToes.X', 'h'), ('backToesEnd.X', 'h'), 'backToes.X'),
}
SMILODON = {
    'aim': {
        'Hips':   (('Root_M', 'h'), ('Spine2_M', 'h'), 'Spine1_M'),
        'Spine1': (('Spine2_M', 'h'), ('Spine3_M', 'h'), 'Spine2_M'),
        'Spine2': (('Spine3_M', 'h'), ('Chest_M', 'h'), 'Spine3_M'),
        'Spine3': (('Chest_M', 'h'), ('Neck_M', 'h'), 'Chest_M'),
        'Neck1':  (('Neck_M', 'h'), ('NeckPart2_M', 'h'), 'Neck_M'),
        'Neck2':  (('NeckPart2_M', 'h'), ('Head_M', 'h'), 'Neck1_M'),
        'Head':   (('Head_M', 'h'), ('JawEnd_M', 'h'), 'Head_M'),
        **{k.replace('.X', '.L'): tuple((v.replace('.X', '_L') if isinstance(v, str) else (v[0].replace('.X', '_L'), v[1])) for v in val) for k, val in _smil_limbs.items()},
        **{k.replace('.X', '.R'): tuple((v.replace('.X', '_R') if isinstance(v, str) else (v[0].replace('.X', '_R'), v[1])) for v in val) for k, val in _smil_limbs.items()},
    },
    'pelvis': 'Root_M',
    'legs': {
        'hind.L': (('backRump_L', 'h'), ('backToes_L', 'h')),
        'hind.R': (('backRump_R', 'h'), ('backToes_R', 'h')),
        'front.L': (('frontRump_L', 'h'), ('frontToes_L', 'h')),
        'front.R': (('frontRump_R', 'h'), ('frontToes_R', 'h')),
    },
    'armature': 'DeformationSystem',
    'ref': ('DeformationSystem|Idle|BaseLayer', 2),
}

# ---------------------------------------------------------------- Four Legged Predator (timsblends, Rigify; hind legs are named arm bones)
_pred_limbs = {
    'Scapula.X':   (('DEF-shoulder.X', 'h'), ('DEF-shoulder.X', 't'), 'DEF-shoulder.X'),
    'UpperArm.X':  (('DEF-thigh.X', 'h'), ('DEF-shin.X', 'h'), 'DEF-thigh.X'),
    'Forearm.X':   (('DEF-shin.X', 'h'), ('DEF-foot.X', 'h'), 'DEF-shin.X'),
    'FrontPaw.X':  (('DEF-foot.X', 'h'), ('DEF-r_toe.X', 'h'), 'DEF-foot.X'),
    'FrontToes.X': (('DEF-r_toe.X', 'h'), ('DEF-r_toe.X', 't'), 'DEF-r_toe.X'),
    'Thigh.X':     (('DEF-upper_arm.X', 'h'), ('DEF-forearm.X', 'h'), 'DEF-upper_arm.X'),
    'Shin.X':      (('DEF-forearm.X', 'h'), ('DEF-hand.X', 'h'), 'DEF-forearm.X'),
    'HindPaw.X':   (('DEF-hand.X', 'h'), ('DEF-f_toe.X', 'h'), 'DEF-hand.X'),
    'HindToes.X':  (('DEF-f_toe.X', 'h'), ('DEF-f_toe.X', 't'), 'DEF-f_toe.X'),
}
PREDATOR = {
    'aim': {
        'Hips':   (('DEF-spine', 'h'), ('DEF-spine.001', 'h'), 'DEF-spine'),
        'Spine1': (('DEF-spine.001', 'h'), ('DEF-spine.002', 'h'), 'DEF-spine.001'),
        'Spine2': (('DEF-spine.002', 'h'), ('DEF-spine.003', 'h'), 'DEF-spine.002'),
        'Spine3': (('DEF-spine.003', 'h'), ('DEF-spine.004', 'h'), 'DEF-spine.003'),
        'Neck1':  (('DEF-spine.004', 'h'), ('DEF-spine.005', 'h'), 'DEF-spine.004'),
        'Neck2':  (('DEF-spine.005', 'h'), ('DEF-spine.006', 'h'), 'DEF-spine.005'),
        'Head':   (('DEF-spine.006', 'h'), ('DEF-spine.006', 't'), 'DEF-spine.006'),
        **_side(_pred_limbs, '.L'), **_side(_pred_limbs, '.R'),
    },
    'pelvis': 'DEF-spine',
    'legs': {
        'hind.L': (('DEF-upper_arm.L', 'h'), ('DEF-f_toe.L', 'h')),
        'hind.R': (('DEF-upper_arm.R', 'h'), ('DEF-f_toe.R', 'h')),
        'front.L': (('DEF-thigh.L', 'h'), ('DEF-r_toe.L', 'h')),
        'front.R': (('DEF-thigh.R', 'h'), ('DEF-r_toe.R', 'h')),
    },
    'armature': 'rig',
    'ref': ('rig|rig|Idle', 1),
    'lateral': 0.35,   # sprawled reptile legs: keep only 35 % of the sideways paw motion
}

PROFILES = {'tiger': TIGER, 'smilodon': SMILODON, 'predator': PREDATOR}

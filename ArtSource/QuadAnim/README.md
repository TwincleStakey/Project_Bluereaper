# QuadAnim: free quadruped clips -> AutoRig boss (장산범 and later bosses)

Reusable pipeline: take any downloaded quadruped animation (tiger, wolf, big cat, reptile...) and put its motion on a
boss rigged with the video2rig AutoRig quadruped skeleton (Hips, Spine1-3, Neck1-2, Head, Scapula/UpperArm/Forearm/
FrontPaw/FrontToes, Thigh/Shin/HindPaw/HindToes, .L/.R).

## Files
- `src/` downloaded packs (not in git, re-download from the links in `Assets/Game/Jangsanbeom/QuadAnim/CREDITS.txt`).
- `scripts/profiles.py` one profile per source skeleton: which source bones drive each target bone, the paw/hip joints, the
  "neutral standing" reference frame. A new source = a new profile (copy one, rename the bones).
- `scripts/retarget.py` the retarget (run on the boss .blend):
  `blender -b werewolf_snake_v3.blend -P retarget.py -- <profile> <src file> <action> out/<name> [speed] [fps] [src f0 f1]`
  writes `out/<name>.fbx` (Unity) and `out/<name>.blend`.
- `scripts/batch.sh [filter]` all clips in the list + review renders `prev/<name>_side.png / _q34.png`.
- `scripts/preview.py`, `scripts/src_preview.py` (render the source clip to compare), `scripts/sheet.py` (contact sheets),
  `scripts/inspect.py` (dump a rig's bones and actions).

## How the retarget works
1. Torso and limbs copy the source bone's change from its neutral pose (not its absolute direction), so the boss keeps its
   own proportions and stance (a cat's dipping spine does not flatten a hunched beast).
2. Hips translation = source pelvis offset x hip-height ratio.
3. Paws: goal = neutral target paw + source paw offset x leg ratio, then analytic 2-bone IK (knee/elbow bend plane from
   step 1). Planted source paws stay planted. `lateral` in a profile shrinks sideways paw motion (sprawled reptiles).

## Unity
Copy the chosen FBX into `Assets/Game/Jangsanbeom/QuadAnim/Models/jsbq_<move>.fbx`, write `Temp/jsbq_build.trigger`
(JSBQuadAnimSetup: Generic import x25, animator with one state per clip, JSBQ_Boss prefab with the glow materials,
JSBQ_Showcase scene). `Temp/jsbq_play.trigger` with `record` plays the move list (`Temp/jsbq_moves.txt`, lines
`state seconds [speed] [hit,hit] [turn]`) in the BossSeal arena in Play mode and writes frames to
`ArtSource/QuadAnim/renders/play`, then leaves Play. Hit reactions are procedural (JSBQuadShowcase: spine/neck/head spring
kick + hit-stop + red flash); locomotion moves the root by the planted paw, so in-place clips do not slide.

For another boss: same skeleton names -> only its .blend changes (`TGT` in batch.sh). Different skeleton -> rename the
target bones in `TARGET_LEGS`/profiles.

#!/bin/bash
# Retarget every clip onto the 장산범 v2 rig (11-18 quadruped model). Defaults only: the v2 body has real big-cat proportions,
# so none of the QA_ stance knobs the v1 biped-shaped model needed. Usage: bash scripts/batch_v2.sh [filter]
cd "$(dirname "$0")/.."
B="/c/Program Files (x86)/Steam/steamapps/common/Blender/blender.exe"
TGT=C:/Users/PC/video2rig/projects/jsb_v2/autorig/rigged.blend
TIG=src/siberian-tiger-rigged-animated-fur-amur/source/siberian_tiger_v3_sketchfab.glb
PRE="src/four-legged-predator/source/Four Legged Predator.fbx"
SMI=src/smilodon-game-animations/source/Smilodon.fbx
while IFS=';' read -r prof file act out speed trim; do
  [ -z "$prof" ] && continue
  [ -n "$1" ] && [[ "$out" != *$1* ]] && continue
  echo "== $out"
  "$B" -b $TGT -P scripts/retarget.py -- "$prof" "$file" "$act" "out/$out" ${speed:-1} 30 $trim 2>&1 | grep -E "TOE_MIN|FBX_OK|Traceback|Error|assert" | cut -c1-160
done <<LIST
tiger;$TIG;01_Idle;v2_idle;1
tiger;$TIG;02_Walk;v2_walk;1
tiger;$TIG;03_Trot;v2_trot;1
tiger;$TIG;04_Gallop;v2_gallop;1
tiger;$TIG;05_Roar;v2_roar;1
predator;$PRE;rig|rig|attack;v2_swipe;1;1 45
predator;$PRE;rig|rig|Powerfull_attack;v2_slam;1;1 80
predator;$PRE;rig|rig|Roar;v2_roar2;1;1 90
smilodon;$SMI;DeformationSystem|Death|BaseLayer;v2_death;1
LIST

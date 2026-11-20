#!/bin/bash
# Retarget every listed clip onto the 장산범 rig, then render review previews. Usage: bash scripts/batch.sh [filter]
cd "$(dirname "$0")/.."
B="/c/Program Files (x86)/Steam/steamapps/common/Blender/blender.exe"
TGT=C:/Users/PC/video2rig/projects/werewolf/props/werewolf_snake_v3.blend
Q=$PWD
TIG=src/siberian-tiger-rigged-animated-fur-amur/source/siberian_tiger_v3_sketchfab.glb
PRE="src/four-legged-predator/source/Four Legged Predator.fbx"
SMI=src/smilodon-game-animations/source/Smilodon.fbx
while IFS=';' read -r prof file act out speed trim; do
  [ -z "$prof" ] && continue
  [ -n "$1" ] && [[ "$out" != *$1* ]] && continue
  echo "== $out"
  "$B" -b $TGT -P scripts/retarget.py -- "$prof" "$file" "$act" "out/$out" ${speed:-1} 30 $trim 2>&1 | grep -E "SCALES|TOE_MIN|FBX_OK|Traceback|Error|assert" 
  rm -rf "prev/$out"; "$B" -b "out/$out.blend" -P scripts/preview.py -- "$Q/prev/$out" 3 2>&1 | grep -E "Traceback"
  python scripts/sheet.py "prev/$out" side "prev/${out}_side.png" 6 >/dev/null; python scripts/sheet.py "prev/$out" q34 "prev/${out}_q34.png" 6 >/dev/null
done <<LIST
tiger;$TIG;01_Idle;q_tiger_idle;1
tiger;$TIG;02_Walk;q_tiger_walk;1
tiger;$TIG;03_Trot;q_tiger_trot;1
tiger;$TIG;04_Gallop;q_tiger_gallop;1
tiger;$TIG;05_Roar;q_tiger_roar;1
predator;$PRE;rig|rig|attack;q_pred_attack;1;1 45
predator;$PRE;rig|rig|Powerfull_attack;q_pred_powerattack;1;1 80
predator;$PRE;rig|rig|Roar;q_pred_roar;1;1 90
predator;$PRE;rig|rig|Run;q_pred_run;1
smilodon;$SMI;DeformationSystem|Bite|BaseLayer;q_smil_bite;1
smilodon;$SMI;DeformationSystem|Death|BaseLayer;q_smil_death;1
smilodon;$SMI;DeformationSystem|Jump|BaseLayer;q_smil_jump;1
smilodon;$SMI;DeformationSystem|Run|BaseLayer;q_smil_run;1
LIST

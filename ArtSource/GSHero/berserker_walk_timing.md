# Berserker walk / drag reference (11-15, 2nd)

Sheet: `berserker_walk_ref_sheet.png` (rows 1-2: A = 193843 f152/156/160/166/172/178 crop; rows 3-4: B = 193939 f126/131/136/141/146/151).

## What the sources have
- **No walk or run cycle in either recording.** Both are a training-dummy skill video; every move between skills is a dash. Checked every 3rd frame of A (210 f) and B (202 f).
- YouTube: yt-dlp install was blocked on this PC (auto-mode permission). Viewing in the browser only: the Brelshaza solo video (avUsxfLS2cY) is mostly Mayhem form + VFX at a far camera, the all-skills showcase (lYrecr253lY) is skills only. No usable walk cycle found yet.
- Usable: the end of the dash (A f152-178, B f131-151), which Kido named as the base pose ("몸을 숙이고 칼을 한 손에 쥔 채 뒤로 끄는").

## Measured from the dash end (A f166-178 settled)
| | value |
|---|---|
| hands on the sword | **one (right)**; left hand free, not on the handle |
| blade direction | trails straight back from the hand, **~165-175 deg from the travel direction** (seen from above) |
| blade height | **lying almost flat at ground level**, tip on / just above the floor (no shadow gap visible) |
| blade length on screen | ~1.3-1.5x the crouched body length -> tip ~1.1-1.3 m behind the hand at our scale |
| grip | beside / a little behind the right hip, arm nearly straight |
| torso | crouched and leaning forward strongly (head over the front knee); right shoulder dropped back toward the sword |
| settle | after the 9-10 f dash the pose is held still >= 15 f (0.5 s): it is the drag pose at rest |

## Not measurable here (needs a walk/run clip)
- cycle length in frames, stride length, how much the shoulders yaw while running, left-arm swing amplitude.
- Kido: "몸도 살짝 틀고" -> used as: chest turned ~15 deg toward the sword side (right shoulder back), hips ~8 deg.

## Values used for the build (until a run recording arrives)
- Run legs: the existing 20 f cycle (unchanged, per brief).
- Drag: grip at the right hip slightly back, tip 0.06 m above the floor ~1.2 m behind, blade edge down, one hand.
- Lean: run hips pitch 24 + spine 12 (as before); idle hips 12 + spine 10, knees bent a little.
- Left arm: aimed in the chest frame, by the side (out 0.15), swing -15..+25 deg (+ = back) against the left leg, elbow 70-85 deg; idle 5 deg back, elbow 35 deg.
- Attacks: first key (f0) = the drag idle with the left hand off; the left hand joins by the f2 windup. Slash3 / Dash end back in the drag idle.

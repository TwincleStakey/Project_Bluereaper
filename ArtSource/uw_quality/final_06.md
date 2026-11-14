# 저승/지옥 boss room — final review 06 (2026-11-14, pass6)

## Verdict: REJECT
The edges are fixed. The pruning has gone too far, though. The room no longer meets the owner's "prominent glow" direction, and lava still runs under the player outside the protected lanes.

## final_05 status
- **Fixed:** the south ruler line, since the shoulders now break the lip at -2_-24. The plaza candle post. The spawn and gate lanes are clean (-2_-2, -2_18).
- **Partial:** the lava sea. It is dimmer, but it is still loud caramel at 38_20 z2.6 (800-1600, 0-500), at -38_0 z2.6 on the left, and at -25_12 z2.6 at the top.
- **Open:** lava under the player at 3 of 9 points. Portal steps.

## Remaining issues
1. **Too dark and empty (direction regression).** At -15_-8, -2_-2 and -25_12 z1.6, about 60% of the frame is black. The only light is the player pool, and the glow survives only at the screen edges. Fix: give the existing dark vein network a dim ember underglow (crimson, emission 0.3-0.5, no bright core) across the whole floor. Add sparse 0.5 m glowing fissure flecks every 4-6 m. Raise ambient by about 15%. Bright orange stays reserved for real rivers, so the hazard still reads.
2. **Lava under the player off-lane.** At 38_20 the player stands on the river (z1.6, 770,400-900). At -38_0 a tip reaches the head (720,240-420). At 25_-15 the river grazes the right shoulder (800,480). Lanes alone don't cover this. Fix: either cut every river to crust (emission 0) inside a 2.5 m radius of the walkable-floor medial axis, or make rivers a real damage hazard with a crust rim. Pick one; don't leave it ambiguous.
3. **Leftovers.** The portal steps are still there (6_8 z2.6 1040-1190,175; 38_20 z2.6 150-330,420; -2_18 z2.6 1280-1450,370). The lane cut left an orphan "S" glyph beside the gate (-2_18 z1.6 980-1130,280-580) that reads as a decal. Fix: merge the steps into the dais. Delete or reconnect the orphan so it reaches the lily line.

## Design test
In every z1.6 frame: no lava under the player, and at least three glow sources inside the middle 60% of the screen.

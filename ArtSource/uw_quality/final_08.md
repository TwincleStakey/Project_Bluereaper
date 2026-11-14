# 저승/지옥 boss room — final review 08 (2026-11-14, pass8)

## Verdict: REJECT
The outer room still holds. But the spawn hub, the item this pass was meant to fix, cannot be verified. Both -2_-2 frames are a full-screen cyan/ice wash with frost cracks: no basalt, no lava, no seam ring.

## final_07 status
- **Fixed:** the lava sea. It is now magma red and dimmer than every river (-38_0 z2.6, 38_20 z2.6). The lanes are clean (-2_18, -2_-24).
- **Partial:** the hub heat. The 6_8 frame shows the crimson ring, but the flecks are dim red slivers, not glow sources.
- **Open:** the 38_20 river still runs down the ledge spine under the player (z1.6 and z2.6). The owner's note tolerates occasional proximity, but this is permanent straddling.

## Remaining issues
1. **The -2_-2 hub is a cyan whiteout.** The cause is likely a snow-stage look volume or frost-boss post effect leaking into 저승. Fix: make sure UWLook owns the grade at spawn, disable any frost/snow volume in this room, then recapture -2_-2 at z1.6 and z2.6.
2. **The middle 60% fails the 3-glow test at -25_12 and is marginal at 6_8.** Fix: raise the fleck emission to about 60% of a river crust, and add 2-3 ember motes or seam glints 5-9 m from the player position in each frame.
3. **The 38_20 ledge river.** Fix: shift that segment about 1.5 m east, or cut it into two fissures with a dark gap where the ledge is narrowest.

## Design test (unchanged)
Every z1.6 frame, including -2_-2, has 3+ glow sources in the middle 60%. The sea is never brighter than a river.

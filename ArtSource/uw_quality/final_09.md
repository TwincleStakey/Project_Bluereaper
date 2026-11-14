# 저승/지옥 boss room — final review 09 (2026-11-14, pass9)

## Verdict: APPROVE WITH NOTES
The room reads well now: basalt in darkness, lava at the outer edge, and a readable player in every frame. One design-test frame is still short of glow, so this is not yet unconditional.

## final_08 status
- **Fixed:** the -2_-2 hub. It shows a crimson seam web around the plaza, bright flecks and a river to the east, and passes the glow test easily at both zooms.
- **Fixed:** the 38_20 ledge. No river runs under the player any more.
- **Fixed:** the sea. It is dim brown-red at -38_0 z2.6 and dimmer than every river.
- **Fixed:** 6_8. Flecks plus a river put 3+ sources in the middle.
- **Open:** -25_12. In the middle 60% there is only the dim vein web on the left. The nearest fleck (920,170) and ember (735,748) sit just outside the box. The orange sparks are too small to count.

## Remaining issues
1. **-25_12 fails the 3-glow test.** Fix: place 2 seam flecks 4-7 m from the capture point, NE and SW of the player, at the new fleck brightness.
2. **The 38_20 ledge is marginal** (2 flecks inside, 1 ember on the edge). With the river gone, the ledge reads as an empty slab. Fix: add 1-2 short dead-end fissures (no river) along the ledge's east lip, 3-5 m from the player.
3. **The ember sparks are under 4 px at z1.6.** Fix: scale the spark particles about 1.5x so they count as glow points, not noise.

## Design test (unchanged)
Every z1.6 frame has 3+ glow sources in the middle 60%. The sea is never brighter than a river.

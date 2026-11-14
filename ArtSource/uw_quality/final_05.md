# 저승/지옥 boss room — final review 05 (2026-11-14, pass5)

## Verdict: APPROVE WITH NOTES
The tapered rivers read as cracks now, where before they looked like painted ribbons. The gate hall roof is the landmark it should be at z2.6, and the blotches are gone. The spawn and boss ring and -15_-8 are now clean. It is still not unconditional, because both parts of the design test fail in gameplay frames.

## final_04 status
- **Fixed:** red blotches. Ghost and smeared rivers. Gate roof. Rivers ending flat against the edge.
- **Partial:** lava sea. Plate seams now read at 38_20 z2.6, but -38_0 z2.6 on the left is still two-tone caramel. West seam: -38_0 z2.6 still shows a vertical light/dark boundary at x≈570. NE edge is now organic.
- **Open:** south straight edge. Lava under the player. Portal steps. Plaza post.
- **Exempt:** the gate plinth's straight front edge (-2_18, y≈200). It is architecture, not terrain.

## Remaining issues
1. **South edge is still a ruler line.** In -2_-24 it runs the full width at y≈740 (z1.6) and y≈610 (z2.6). The shoulders sit *below* the line, so they frame it rather than break it. Fix: move the shoulders 1.5-2 m inward so their tops cut over the lip every 3-5 m, at varied heights (0.6-2 m). Apply the ±1.5 m noise displacement to the UWGround edge vertices, which is still not done. Apply the same fix to the west seam.
2. **Lava under the player at 3 of 7 sample points.** At 6_8 the branch passes through the player (650-800,525). At 38_20 a tip sits at the feet (740,300). At -38_0 a tip touches the head (720,420). Three hits in seven samples is not chance; these points sit on the natural lanes. Fix: carve 4 m half-width exclusion capsules along spawn→gate, spawn→portal and across the portal plateau. Where a river must cross a lane, fade it to dark crust (emission 0) for 3 m. Re-shoot the same points plus 2 random ones.
3. **Leftovers.** The portal dais steps are still there (6_8 z2.6 1040-1190,175; 38_20 z2.6 140-310,390). So is the plaza candle post (-2_18 z1.6 705,290). Fix: sink the steps 0.3 m and merge them into the dais. Move the post 2 m north behind the lily line. Darken the remaining caramel sea patches to base albedo 0.03.

## Design test
Unchanged: in every z1.6 frame, no straight terrain line longer than a quarter of the screen width, and no lava under the player except deliberate hazards.

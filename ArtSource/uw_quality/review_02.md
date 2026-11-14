# 저승 review 02 (2026-11-14, pass2 captures, 16 shots)

## Verdict: FAIL
The arena floor is close: dark basalt, glowing cracks, no dirt. Every off-arena frame still shows quads with seams and posterized lava. Nothing East Asian reads yet. The new dead trees sit over the play area instead of at the edge. Any z2.6 shot would embarrass a Steam page.

## review_01 status
- Fixed: none fully.
- Partial: #6 (secondary veins are dimmer, but the even Voronoi net still shows in -15_-8 centre and 5_5 left). #8 (rock under the player now reads, but beyond about 8 m it is still void). #5 (silhouettes were added, but in the wrong place). #4 (fire sits on the gate box, but there is still no roof shape).
- Still open: #1 rectangular lava planes, #2 plane overlapping the arena at -38_0, #3 straight south border, #7 cartoon outline and white-core rivers, the round red patch, and the repeated crown decal.

## Top 6 remaining problems
1. **Off-arena lava quads** (-2_18 z1.6/z2.6 top third, -38_0 z1.6 left half, 38_20 z2.6 right half, 6_8 z2.6 top). There are hard rectangular edges, blocky posterized texels and cream-white fills, and stacked quads at different heights. Fix: disable these planes. Lower the void fog plane (UW_FogVoid) 2-3 m and give it the UW_Floor vein shader at 1/4 emission. Add a radial alpha falloff starting 3 m outside the arena rim. If any plane has to stay, set its texture filter to Bilinear, turn off compression, clamp the emission colour to orange (no white) and multiply by an edge-noise mask.
2. **Silhouettes inside the play area** (-2_-24 z1.6: a trunk cuts through screen centre, branches cross the player; 38_20 z2.6: a black pillar at top centre). This breaks the "edge only" rule and hides bullets. Fix: move the dead trees and pillars to beyond the south and outer rim (south of the -2_-24 edge line, y > 75% of screen at z1.6). Scale them 1.5x so they cross the frame edge. Nothing tall may stand within the arena radius plus 2 m.
3. **Main lava rivers run under the player and burn out** (5_5 z1.6 centre, -38_0 z1.6 centre, 38_20 z2.6 centre, 30_-5 z1.6 bottom right with a white diamond). The cores clip to cream, the outline is a cartoon black, and the player's silhouette dissolves on the brightest pixels. Fix: cap the core emission (HDR 2.5, orange-yellow, hue 30-35°). Replace the black rim with a 0.4 m charred gradient. Give main rivers a 4 m exclusion from the player spawn and the boss arena centre. Turn on the player rim/outline (warm-white fresnel, 0.6).
4. **Straight south border and plank boardwalk** (-2_-24 z1.6/z2.6 lower third, -38_0 z2.6 bottom left). The ruler-straight black band and brown wooden deck read as a level-editor cut. Fix: hide the deck. Offset the floor edge vertices with noise (UWGround edge displacement, or swap in the existing cliff-lip mesh). Run 2-3 vein fissures off the edge with an ember particle drip.
5. **Gate and temple read as boxes** (-2_18 z2.6 top centre, 6_8 z2.6 top right: flat red slab, straight stair lines, stilt posts). Fix: put the existing pagoda or jangseung props from hell_props on the box. Add 3 fire emitters along the roof line plus one warm point light (2400 K, 6 m). Cluster red emissive higanbana along the gate approach and the arena rim (not the centre).
6. **Disc lighting and blotches** (every z2.6 frame: a hard cool-white pool around the player, round red rock patches at 5_5 right, 38_20 bottom right and -15_-8 left). The pool is blue-white against the target of 3500 K, and the patches look like stains. Fix: set the player light to 3500 K, 7 m high, range 16 m. Remove the round red patch decal or blend it into the macro noise at 30%. Raise ambient to 0.035/0.04/0.05.

## Keep
- The arena interior: dark cracked basalt, no dirt or ash, no blockers.
- Orange emissive as the main light source, and the warm embers and lantern motes.
- Blue player shots against the orange floor (the best gameplay read).
- The swirling portal landmark and its chained platform.
- Fire VFX on the gate (keep it and add more, see #5).
- The dead-tree silhouettes as assets; only their placement is wrong.

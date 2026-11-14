# 저승 review 01 (2026-11-14, newest captures 5_5 / -2_18 / -15_-8 / 20_-15 / -2_-26 / -40_0)

## Verdict: FAIL
The floor is no longer covered in dirt or ash, and the glow now carries the image. But the area outside the arena breaks the brief everywhere: rectangles, straight lines, low-res planes. None of the East Asian hell set dressing reads yet.

## Top 8 problems (priority order)
1. **Rectangular lava planes outside the arena** (-2_18 north, -40_0 west, -2_-26 south). Hard straight horizontal and vertical edges, a blocky/posterized low-res texture, and cream-white lava. Change: replace them with an unlit black abyss fading out radially, or use the same vein shader at 1/3 emission. Every edge must be feathered and irregular, with no visible quad border.
2. **Plane overlaps the play area at -40_0.** A lava quad corner lies over the arena floor next to the player, with a z-step seam. Change: pull every off-arena plane outside the arena radius plus a margin of at least 4 m, and drop it 0.5 m or more below floor height.
3. **Straight arena border at the south** (-2_-26). The floor ends in a ruler-straight black band. Change: give the floor edge a noise-displaced cliff lip with glowing fissures falling into the void.
4. **No East Asian hell reads.** The temple/gate is a flat black box with straight stair lines (5_5 z2.6, -2_18). The west structure is plain brown planks. I see no spider lilies, and the stone lanterns are tiny dots. Change: put a burning roof silhouette (curved eaves) with fire VFX on top of the box, cluster red emissive higanbana in drifts along the arena rim, and give the lanterns a warm 2-3 m light pool each.
5. **No tall black foreground silhouettes.** Nothing frames the screen. Change: put pillars, torii, dead trees or chains just outside the arena edge, unlit pure black, tall enough to cross the camera's frustum edge at z1.6.
6. **Uniform Voronoi vein net.** Every secondary crack has the same cell size, width and orange, so it reads as a net decal laid over the whole floor. Change: cut secondary vein glow to about 40%, vary cell size 2-4x with noise, break continuity so 30-50% of segments fade out, and keep roughly the central 40% of the screen mostly calm rock.
7. **Mismatched rendering style on the main lava rivers.** They have a cartoon black outline and blown near-white cores next to photoreal rock. Change: clamp the core to saturated orange-yellow (no pure white), soften the dark rim into a charred gradient, and add slow flow scroll.
8. **About 60% of every frame is near-black void.** The lit area is a player-centred disc, and dark round blotches sit off to the side (5_5 right). Change: raise ambient floor luma slightly outside the disc, and remove or blend the circular reddish rock patch. The same "crown" shard decal also repeats at 5_5 and -2_18, so randomize it.

## QA defects
- Hard quad seams, posterized/blocky lava texture, and blown cream whites on the off-arena planes.
- A z-overlap seam at -40_0, and a straight black band at the south edge.
- Readability: the player (dark navy) nearly vanishes on dark rock away from veins. Add a rim or outline. The brown/yellow enemy at 5_5 blends into the floor and its dust puffs. Blue bullets read well everywhere.
- I could not run luma measurements (no shell in this pass). Every finding here comes from looking at the images.

## Keep
- Dark base with orange emissive as the main light source. This is the right direction versus the "before" brown and blue shots.
- The cracked black stone texture under the player: clean, with no ash or dirt look.
- Blue shots against the orange floor (complementary contrast, best gameplay read).
- The swirling portal as a landmark, the sparse embers and motes, and the soft vignette.
- An arena interior free of blockers.

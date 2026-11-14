# 저승/지옥 boss room — final review 03 (2026-11-14, pass3)

## Verdict: APPROVE WITH NOTES
The arena now reads as hell: dark cracked basalt, glowing lava fissures, a burning gate, and nothing tall inside the play area. Both QA blockers are gone. The notes below must be fixed before any Steam capture. None of them blocks gameplay.
(The 0_0 and 5_5 frames were cyan shader-compile placeholders and were deleted, so they were not reviewed.)

## review_02 / qa_02 status
- **Fixed:** silhouettes over the play area (R2, QA1). White pool under the player (QA2). Plank deck (R4). Floating rim props (QA8). Cool-white light disc (R6). Off-arena fog quads (R1, partly).
- **Partial:** river width and brightness (R3, QA3). Rivers are narrower, but the cores are still cream with a black cartoon edge. Gate (R5): it is bigger and on fire, but it is still a red box with no roof.
- **Still open:** blocky lava-sea texels (QA6), straight south and north edges (R4, QA11), portal platform steps (QA4), 45° NE corner (QA5), west slab seam (QA7), round red blotches (R6), lit posts on the plaza (QA10).

## Ranked remaining issues
1. **Rivers run through the spawn and under the player** (6_8 z1.6 centre, -38_0 z1.6, 38_20 z1.6). Fix: keep a 4 m river-free radius around the spawn and the boss centre. Clamp the core emission to HDR 2.5 at hue 30-35° (no cream). Swap the black outline for a 0.4 m charred gradient.
2. **Lava sea is still posterized** (-2_18 z1.6/z2.6 top-left, 38_20 z2.6 right). Fix: UW_LavaSea texture to Bilinear, no compression, mip on. Use the UW_Floor vein noise for the sea mottle at 1/4 emission.
3. **Hard straight edges** (west vertical seam at -38_0 z1.6 x≈450; south black band at -2_-24 and 20_-22 z2.6; NE 45° cut at 38_20). Fix: noise-displace the floor edge vertices (UWGround) and fill the NE rect corner. Push the -38_0 boulder cluster 3 m west so the seam sits under it.
4. **Gate and portal platform read as level-editor boxes** (-2_18 z2.6, 6_8 z2.6). Fix: put the pagoda roof and jangseung pair from hell_props on the gate and add 3 roof-line fire emitters. Move the portal dais north of zMax or flatten its steps. Add a red higanbana cluster along the gate approach, at the rim only.
5. **Round red blotches and lit posts** (6_8 right, -15_-8 z2.6 lower-left, -2_18 post at 770,320, 20_-22 post at 463,300). Fix: blend the red patch into the macro noise at 30%. Move the posts past the lip and remove their colliders.

## Before → after (for the owner)
At the start of the night the 저승 room was a pale-blue cobblestone plaza in a white fog, with ghost sprites and grey box props: a "normal field" with no heat and no danger. Pass 3 is a dark basalt plateau split by molten fissures over a lava sea. A burning gate hall and a swirling portal anchor the north edge, dead trees and rock silhouettes stand only at the outer rim, and the player reads clearly as a cool silhouette in a warm pool of light. The direction matches the Diablo IV/POE2 hell boards. What's left is polish: tame the rivers near the player, smooth the lava sea and the straight edges, and give the gate an East-Asian roofline. Perf holds: 200 enemies and 3254 bullets cost 4.5 ms on the GPU.

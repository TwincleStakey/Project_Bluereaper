# 저승/지옥 boss room — final review 04 (2026-11-14, pass4)

## Verdict: APPROVE WITH NOTES
Pass 4 is the best yet. The spawn and boss ring is clean, the river cores are amber instead of cream, and the lilies add the East-Asian note from board 1. It is not unconditional yet: three gameplay-camera frames still show level-editor geometry, and those would show up in a Steam screenshot. (0_0 skipped: placeholder capture.)

## final_03 status
- **Fixed:** river-free spawn and boss centre (-2_-2). Cream cores. Higanbana on the gate approach and rim. Fire on the gate roof.
- **Partial:** black river bank. It is softer but still reads as an outline at 6_8 z1.6. Gate: it still reads as a red box at z2.6.
- **Still open:** straight edges (west, south, NE). Flat caramel lava sea. Portal steps. Red blotches. Lit post on the plaza (-2_18 at 705,290).

## Ranked remaining issues
1. **Straight arena edges.** These show at gameplay zoom: the west seam in -38_0 z1.6 at x≈450, the south band in -2_-24 z1.6 at y≈740 (where rivers end flat against it), and the NE 45° cut in 38_20 z1.6 at 780-930,330. Fix: noise-displace the UWGround edge vertices by ±1.5 m. Push the -38_0 boulder silhouettes 3 m inward so they overlap the seam. Fade river alpha to 0 over the last 2 m before the edge.
2. **Rivers under the player outside the 5 m ring** (6_8, -38_0, 38_20 z1.6). There are also smeared ghost rivers with stretched UVs (-15_-8 z1.6 right side, z2.6 at 640,780). Fix: cap river density to 1 per 12 m and reject segments under 8 m from the walk lanes. Delete or re-UV the faded duplicate river meshes.
3. **The lava sea still reads as flat caramel smears** (-38_0 z2.6 left, 38_20 z2.6 top). The crust plates are not visible at all. Fix: raise plate coverage to about 60%, with a darker plate albedo (0.03) and seam emission ×2. Add slow UV scroll plus the ember particles you already have over the sea.
4. **Boxy landmarks and leftovers.** The gate has no visible roof at z2.6. The portal dais steps are still there (6_8, 38_20). There are red blotches (6_8 right, -15_-8 lower-left) and the plaza post. Fix: move the hell_props pagoda roof and jangseung pair onto the gate. Flatten the dais steps or sink them 0.3 m. Blend the blotch to 30%. Move the post past the lip.

## Design test
Unconditional approval comes when every z1.6 frame passes two checks: no straight line longer than a quarter of the screen width, and no lava under the player outside deliberate hazards.

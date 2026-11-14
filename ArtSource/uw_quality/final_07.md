# 저승/지옥 boss room — final review 07 (2026-11-14, pass7)

## Verdict: APPROVE WITH NOTES
The outer and gate frames now deliver the brief. The lava-crack floor reads as the look, the rivers have crust halos and read as deliberate scenery, the gate, lilies and fire anchor the north, and the player stays readable everywhere. The spawn hub is still under-lit. That is a tuning note, not a structural one.

## final_06 status
- **Fixed:** the empty/too-dark problem on the outer floor (-25_12, -38_0, 25_-15, -15_-8 z2.6). The stray S is gone. The lanes are clean (-2_18, -2_-2).
- **Accepted per the owner's note:** player-on-river frames. At 38_20 and -38_0, the crust halos make them read as intentional. The portal steps read as dais stairs and hurt no frame.
- **Partial:** the lava sea. It is dimmer, but it posterizes to tan marbling (-38_0 z2.6 left; 38_20 z2.6 top right).

## Remaining issues
1. **The spawn hub is still a light pool in black.** At -2_-2, 6_8 and -2_-24 z1.6, the seam heat never appears. The dark veins stay dark and the middle 60% has at most one or two glow sources. Fix: extend the seam-heat patch mask into the hub at about half the outer intensity, add 3-4 seam flecks within 6-10 m of spawn, and keep the lanes clean.
2. **The 38_20 river runs down the spine of a narrow ledge,** so the player always straddles it. Fix: offset the segment about 1.5 m toward the ledge's east rim, or split it into two shorter fissures.
3. **The lava sea's tan bands look like toffee, not magma.** Fix: shift the bright band hue toward orange-red (about 15-25° hue), clamp the brightest band about 20% lower and add a slow noise scroll, so it reads as molten and stays below river brightness.

## Design test
Every z1.6 frame, including the spawn hub, has at least three glow sources in the middle 60%. The lava sea never outshines a river.

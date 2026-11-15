# Berserker reference timing (11-15)

Sources (read only, not moved):
- **A** = `C:\Users\imsoh\Downloads\녹음 2026-11-15 193843.mp4` (Kido screen recording of a YouTube video, chapter "기본 공격"), 1768x1014, 30 fps, 7.0 s, 210 frames.
  f0-47 = player buffering (no motion). Basic attack f48-145, dash f151-160, then a still frame.
- **B** = `C:\Users\imsoh\Downloads\녹음 2026-11-15 193939.mp4`, 1788x1030, 30 fps, 6.73 s, 202 frames. Kido: only the part from about 5 s on (the dodge).
  The dodge starts at f127 (4.23 s) and the recording pauses at f152 (5.07 s).
- Frame numbers = 0-based frames of the recording at 30 fps (1 frame = 33 ms). The screen recorder may drop or duplicate a frame here and there, so ±1 frame.
- Camera: LoA top-down, enemy (golden statue) to the character's right (east) in A. Sheet: `berserker_ref_sheet.png` (A 52-100, B 125-145).

## Basic attack, 3 hits (A)

| | ready / windup | windup (fast) | swing (blade travels) | contact | follow-through | hold / settle |
|---|---|---|---|---|---|---|
| **Hit 1**: rising sweep, back-left → under → front-right → up | f48-54: blade upright on the right shoulder, small step | f55-56 (2 f): blade flicked back to the left, horizontal, chest coiled away | f57-59 (3 f, ~200°): blade sweeps low across the front | **f59** (A 1.97 s) | f60-62 (3 f): blade keeps rising, ends upright over the right shoulder | f63 (~1 f) |
| **Hit 2**: overhead diagonal + whirl | f64-70 (7 f, slow drift, also waiting on input): blade lowered to the left/back, horizontal | f71-72 (2 f): blade lifted up-left | f72-74 (3 f): blade comes over the top onto the target | **f74** (2.47 s) | f75-82 (8 f): the swing does not stop; the body whirls a full 360° (south → west → north → east) with the blade flat | f83-87 (5 f) |
| **Hit 3**: big raised blow (heavy finisher) | f87-93 (7 f): blade raised overhead and tilted back, body turned away | (part of 87-93) | f94-95 (2 f): one-frame-looking strike | **f95** (3.17 s), biggest flash | f96-99 (4 f): blade carries on low to the left of the body | f100-117 (18 f, blade held raised = recovery), f118-127 (10 f) back to rest with the blade lowered to the left |

- Contact to contact: hit 1 → hit 2 = **15 f (0.50 s)**, hit 2 → hit 3 = **21 f (0.70 s)**. Total from the first windup to the last contact: f55 → f95 = 40 f (1.33 s). Full chain incl. recovery: f55 → f127 ≈ 72 f (2.4 s).
- Swing (blade travel to contact): **2-3 frames** on every hit. The visible windup is 2 frames on hits 1-2; only the finisher has a long, readable lift (7 f).
- On contact the pose barely moves for ~1 frame while the flash plays (the "hit stop" is in the VFX and in the blade stopping at full extension, not in a frozen body).
- Weight: the feet step toward the target on every hit (hit 1 step at f56-57, hit 3 a planted wide stance at f93-95). The chest coils away before each swing (hit 1: back-left, hit 3: turned away and up), and the hips lead the shoulders.
- In LoA, hit 2 and the whirl are one input. For our 3-hit combo: hit 1 = sweep, hit 2 = reverse/overhead diagonal (whirl optional, shortened), hit 3 = big raised blow driven down (Kido asked for 내려찍기).

## Dodge (B from ~4.2 s; same move also in A f151-160)

- Not a tumble: a **low forward dash**. The body drops and leans ~35-45° forward, the sword trails behind low in one hand (blade pointing back).
- Start (crouch/push-off): f125-127 (2 f). Travel: **f127-136, 9 f (0.30 s)**. A (non-burst) dash: f151-160, 9-10 f, the same length.
- Distance: about 2.5 body heights in screen space measured against the boss (≈ 4 m at our scale); fast at the start, eases into the stop.
- Recovery: f136-151, crouched with the sword still trailing, ≥ 15 f (0.5 s) before the recording pauses (actionable earlier in game).

## Run

- **Not in either recording.** Kido's brief: a heavy warrior run with the greatsword on the shoulder or at the side, upper body leaning forward.
  A reference for the cycle length and arm pose is still needed (a short recording like A/B, or a YouTube clip; nothing downloaded yet).

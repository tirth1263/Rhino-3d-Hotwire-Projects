# Native hot-wire path package

This package contains five independent Rhino 8 + Grasshopper + KUKA|prc simulations. Every design has its own `.3dm`, `.gh`, and QA image. The two supplied reference files were read only and were not edited.

## Open a design

1. Open the design's `.3dm` in Rhino 8.
2. Start Grasshopper and open the matching `.gh` file.
3. Let the definition solve, then move the `Simulation Slider` connected to the KUKA|prc Core.
4. Confirm that the robot, bow, target path, and foam block match the included QA image.

| Design | Native path strategy | KUKA motion | Verified targets |
|---|---|---:|---:|
| `01_Ruled_Wave` | Synchronized paired rails / ruled wire spans | LIN | 33 |
| `02_Helical_Flute` | Continuous spatial guide with stable frames | SPL | 65 planes |
| `03_Layered_Wave` | Stacked serpentine section passes | LIN + Cartesian Offset | 93 |
| `04_Radial_Fan` | Indexed radial chords with alternating order | LIN + Cartesian Offset | 77 |
| `05_Adaptive_Acoustic` | Field-modulated raster with native reduction | LIN + Reduce Toolpath | 121 |

The fresh-open automated report is in `FRESH_OPEN_VERIFICATION.tsv`: all five files open, contain zero script components, produce native KUKA|prc commands and robot geometry, and report zero Grasshopper runtime warnings or errors in the installed environment.

## Fixed simulation setup

- Rhino units: millimetres
- Robot: KUKA Agilus KR6-10 R1100-2
- Tool: native KUKA|prc Custom Tool, tool number 8
- TCP: `X 0, Y 0, Z 421.35, A 0, B -90, C 0`
- Nominal programmed speed: `0.12 m/s`
- Output-to-controller/save is intentionally disabled
- No Python, C#, VB, GhPython, or Grasshopper Script components are present

## Before any physical run

These are verified simulations, not a safety-rated digital twin. A physical cut cannot be promised as “100% accurate” until the real cell supplies the missing physical facts: robot mastering, measured base frame, calibrated wire TCP, bow deflection, foam density, wire temperature, feed/kerf relation, fixture geometry, and safety interlocks.

Before exporting KRL or moving the robot:

1. Replace the assumed base/TCP with measured cell values.
2. Add the actual table, clamps, block, fence, and nearby equipment as KUKA|prc collision geometry.
3. Measure kerf on a coupon at the intended wire temperature and speed; apply half the measured kerf as the native path offset appropriate to the kept side.
4. Run the complete program in KUKA|prc, then on the controller in T1 at reduced override with the wire off.
5. Prove entry and exit motions, singularity clearance, cable/bow clearance, and emergency-stop behavior before heating the wire.

See `RESEARCH_AND_ALGORITHMS.md` for the algorithm logic and video-derived design decisions.

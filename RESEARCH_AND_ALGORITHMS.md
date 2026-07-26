# Research and native algorithm notes

## What the video review changed

The research included both screen-recorded Rhino/Grasshopper/KUKA workflows and footage of real robot cells. Sampled footage showed that the wire is a moving line segment, not merely a TCP point: its two endpoints and the bow's clearance must be respected throughout the motion. It also showed the importance of a continuous wrist posture, a clean approach outside the foam, positive workholding, and separating fast roughing from later finishing.

Reviewed YouTube videos:

- [01_0 Hotwire cutting custom tool KUKA Robot arm Grasshopper Rhinoceros KUKAprc](https://www.youtube.com/watch?v=_7WRUIWZOJ4) — builds a custom bow/tool, creates curve-driven targets, connects native KUKA|prc commands, and runs the simulation slider. This informed the tool/TCP and guide-curve structure.
- [KUKA robot arm hot wire simulation using two curves](https://www.youtube.com/watch?v=3a7nglF7hrI) — visibly synchronizes two rails and displays the wire spans across a foam volume. This is the direct precedent for Algorithm 1.
- [Hotwire foam cutter on KUKA robot — first test](https://www.youtube.com/watch?v=_gAZAGTkWJk) — a real KUKA KR60 carries a long C-frame bow through large wrist sweeps. This reinforced the need for a calibrated TCP, stable frame orientation, and bow/cable clearance.
- [Robotic hot wire cutting a huge part](https://www.youtube.com/watch?v=uAXOxcj7ggM) — removes large foam regions with sequential sweeping cuts before conventional finishing. This informed the layered and fan roughing strategies.
- [RoboCut — KUKA industrial robotic arm](https://www.youtube.com/watch?v=67SZ9Y1GKzA) — shows the alternative of robot-held foam moving against a stationary wire. The deliverables deliberately use the opposite, supplied setup: robot-mounted bow with fixed foam.
- [Robotic Hot Wire Cutting Fabrication](https://www.youtube.com/watch?v=4ahdvReebrQ) — shows block logistics, tool fabrication, robot-cell operation, and finished foam geometry. This informed the emphasis on workholding and cell clearance.
- [Robotic Hot Wire Cutting Team Oikos AADRL 1](https://www.youtube.com/watch?v=2DsTvaM_Fds) and [Team Oikos AADRL 2](https://www.youtube.com/watch?v=dobtIj4tGUE) — show digital path construction alongside physical cuts and the resulting ruled foam pieces.
- [Robotic Hot Wire Cutting — Architects' Journal](https://www.youtube.com/watch?v=IOyj62lmxFY) — illustrates a transportable fabrication cell and foam-block handling sequence, emphasizing that robot reach is only one part of the production workflow.

Additional technical references:

- [Linking Robotic Hotwirecutting and Assembly](https://vimeo.com/94077181) — Grasshopper + KUKA|prc workflow and EPS assembly context.
- [RoboCut: Hot-wire Cutting with Robot-controlled Flexible Rods](https://crl.ethz.ch/papers/hotwirecutter.pdf) — demonstrates that tool deformation can be deliberately modeled, and therefore cannot be ignored when it is unintended.
- [Thermo-electro-mechanical model for hot-wire cutting of polystyrene foam](https://www.sciencedirect.com/science/article/pii/S0890695516300475) — supports calibrating temperature, feed, cutting angle, and kerf instead of treating geometry alone as physical truth.
- [Robotic Stereotomy / Hot Wire Cutting](https://www.iaacblog.com/programs/robotic-stereotomy-hot-wire-cutting/) — architectural robotic hot-wire fabrication context.

## Algorithm 1 — synchronized paired rails

Use for ruled, doubly curved, and twisted surfaces where the hot wire must span two boundary curves.

Native graph logic:

`Rail A + Rail B → Reparameterize → Divide Curve (same N) → matched point pairs → Line → midpoint + averaged travel tangent → Plane → Flip Plane → KUKA LIN → KUKA Core`

The two rails must have matching direction and parameter order. Each matched point pair is the physical wire segment. The TCP is placed at the segment midpoint; frame continuity is checked before generating LIN targets. This is the most geometrically faithful general hot-wire strategy in the package.

File: `01_Ruled_Wave/01_Ruled_Wave.gh`

## Algorithm 2 — continuous spatial guide / flute

Use for a single flowing cut where a smooth robot motion is preferable to point-to-point linear segments.

Native graph logic:

`Guide curve → Divide Curve → stable continuous planes → KUKA SPL → KUKA Core`

The plane field is kept continuous instead of allowing independent curve frames to flip. A native KUKA SPL command consumes all 65 target planes as one smooth motion command.

File: `02_Helical_Flute/02_Helical_Flute.gh`

## Algorithm 3 — layered serpentine sections

Use for stepped relief, topographic, ribbed, or sliced foam geometry.

Native graph logic:

`Section levels → wave/profile per level → alternate every second curve → divide → stable planes → Cartesian start/end offset → KUKA LIN → KUKA Core`

Alternating the row direction prevents repeated long retracts. Cartesian offsets provide approach/exit clearance beyond the nominal cut, and the stacked paths create a controlled multi-pass roughing strategy.

File: `03_Layered_Wave/03_Layered_Wave.gh`

## Algorithm 4 — radial fan chords

Use for fan, saddle, faceted, or radiating patterns made from a sequence of straight wire sweeps.

Native graph logic:

`Angular series → paired boundary points → chord lines → alternate ordering → divide → stable planes → Cartesian start/end offset → KUKA LIN → KUKA Core`

The indexed angular sequence makes each chord explicit and auditable. Alternating order reduces non-cut travel while preserving a consistent bow posture.

File: `04_Radial_Fan/04_Radial_Fan.gh`

## Algorithm 5 — adaptive field raster

Use for acoustic reliefs, gradients, or variable-density patterns.

Native graph logic:

`Raster index → attractor/field remap → local amplitude + spacing → serpentine points → stable planes → KUKA LIN → Reduce Toolpath → KUKA Core`

The field controls local geometry while the robot path remains a continuous ordered raster. Native KUKA|prc Reduce Toolpath is set to `1.5 mm / 1°` to remove redundant targets without replacing the path with a scripted approximation.

File: `05_Adaptive_Acoustic/05_Adaptive_Acoustic.gh`

## General geometry-to-hot-wire rules

1. Represent the cutter as a finite line plus bow geometry, not only a point.
2. Parameterize paired boundaries consistently; reverse one rail when the endpoint pairing crosses.
3. Sample by physical distance when cut speed must be uniform.
4. Construct one continuous orientation field and flip discontinuous planes before KUKA commands.
5. Add approach and exit targets outside the stock; do not energize the wire while dwelling in foam.
6. Prefer a constant posture inside a safe reach envelope unless changing orientation is required by the ruled surface.
7. Apply kerf compensation only after a temperature/speed/material coupon test.
8. Validate the whole bow and cell, then verify on the real controller at low override.

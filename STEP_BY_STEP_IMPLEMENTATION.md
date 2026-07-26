# C# hot-wire scripts - step-by-step implementation

## Demonstration design

The example is one twisted, tapered foam blade approximately 340 mm long.

- `DESIGN_SOLID` is a closed loft used by the multi-angle section algorithm.
- `CUTTING_SKIN` is a four-sided open surface used by the UV and boundary algorithms.
- `DESIGN_CENTERLINE` is the blade guide curve used by the width/twist algorithm.
- All geometry is stored in `Hotwire_TwistedBlade_Implementation.3dm`.
- All four working C# branches are stored in `Hotwire_TwistedBlade_AllAlgorithms.gh`.

## Common implementation used by all four scripts

1. Grasshopper supplies geometry and numeric settings to the embedded C# component.
2. The script unwraps Grasshopper data into RhinoCommon geometry.
3. The geometry is sampled into an ordered sequence of cutting stations.
4. Every station produces endpoint `A` and endpoint `B`.
5. A finite wire line is created from `A` to `B`.
6. The midpoint of each wire line is calculated.
7. The ordered `A` points form `RailA`.
8. The ordered `B` points form `RailB`.
9. The ordered midpoints form `MidPath`.
10. A straight loft between `RailA` and `RailB` produces `Ruled`.
11. One robot plane is calculated at every wire midpoint.
12. The results are returned to Grasshopper for preview and KUKA|prc handoff.

## Robot-plane convention

Every script uses the same plane convention.

- Origin: midpoint of the finite wire.
- X-axis: endpoint `A` toward endpoint `B`.
- Y-axis: local cutting travel direction.
- Z-axis: cross product of X and Y.
- `Flip = true`: reverses endpoints A and B and therefore reverses plane X.

The travel vector is projected perpendicular to the wire before constructing the plane. Adjacent Z-axes are compared to reduce sudden 180-degree frame reversals.

## Shared outputs

| Output | Meaning |
|---|---|
| `Planes` | Ordered midpoint robot targets |
| `WireLines` | Finite hot-wire positions |
| `Ruled` | Surface swept between both endpoint rails |
| `RailA` | Motion of the first wire endpoint |
| `RailB` | Motion of the second wire endpoint |
| `MidPath` | Motion of the wire midpoint |
| `Report` | Settings, result counts, assumptions, and warnings |

## Branch 01 - Surface UV sampling

### Grasshopper inputs

- `S`: the four-sided `CUTTING_SKIN`.
- `WireAcrossU`: `true`.
- `Count`: `41`.
- `Extension`: `10 mm`.
- `Flip`: `false`.
- `Tolerance`: `0.01 mm`.

### C# execution

1. Accept a Surface or the largest face of a Brep.
2. Read its U and V parameter domains.
3. Divide the advancing V direction into 41 normalized stations.
4. At each station, evaluate the minimum-U and maximum-U points.
5. Extend both endpoints by 10 mm along the wire direction.
6. Create 41 wire lines.
7. Create the two endpoint rails and midpoint path.
8. Straight-loft the endpoint rails.
9. Sample each wire and measure distance back to the source surface.
10. Return maximum ruled-approximation deviation as `Deviation`.

### When to use it

- Use it when surface UV directions already follow the intended cut.
- Rebuild or swap the surface UV directions if the generated path runs the wrong way.
- Do not use it when trims or holes must control the wire because it samples the underlying surface.

## Branch 02 - Automatic opposite-boundary pairing

### Grasshopper inputs

- `G`: the same four-sided `CUTTING_SKIN` converted to a Brep.
- `PreferLong`: `false`.
- `Count`: `41`.
- `Extension`: `10 mm`.
- `Flip`: `false`.
- `Tolerance`: `0.01 mm`.

### C# execution

1. Duplicate the naked boundary edges.
2. Require exactly four unjoined naked edges.
3. Detect edge pairs that do not share an endpoint.
4. Resolve the two possible opposite-edge pairs.
5. Match the directions of both curves in every pair.
6. Sample both curves by normalized arc length.
7. Calculate the mean synchronized span of both candidate pairs.
8. Select the shorter mean span because `PreferLong = false`.
9. Create synchronized endpoint pairs at 41 stations.
10. Create wire lines, planes, rails, midpoint path, and ruled surface.
11. Return both candidate measurements through `PairScores`.

### When to use it

- Use it for clean four-sided open surfaces.
- Set `PreferLong = true` when the other cutting direction is required.
- Repair or rebuild surfaces that have more than four naked edges.

## Branch 03 - Centerline width/twist sweep

### Grasshopper inputs

- `Path`: `DESIGN_CENTERLINE`.
- `ReferencePlane`: X-axis across the blade and Y-axis vertical.
- `WidthStart`: `150 mm`.
- `WidthEnd`: `95 mm`.
- `TwistDegrees`: `45 degrees`.
- `Count`: `41`.
- `Extension`: `10 mm`.
- `Flip`: `false`.

### C# execution

1. Divide the centerline by normalized arc length.
2. Evaluate a position and tangent at every station.
3. Request a perpendicular curve frame.
4. Align the first lateral direction with `ReferencePlane`.
5. Compare consecutive lateral vectors to prevent frame reversal.
6. Rotate the lateral vector gradually about the tangent.
7. Interpolate the programmed width from 150 mm to 95 mm.
8. Place endpoints at half the width on both sides of the centerline.
9. Add 10 mm extension at both ends.
10. Create wire lines, planes, rails, midpoint path, and the exact ruled ribbon.
11. Return the effective width at every station through `Widths`.

### When to use it

- Use it when the intended cut is better described by path, width, and twist.
- Use it for generative blade, ribbon, or ruled-strip studies.
- It does not check whether the generated ribbon matches an existing solid.

## Branch 04 - Multi-angle section envelope

### Grasshopper inputs

- `G`: the closed `DESIGN_SOLID`.
- `PathDir`: world X-axis.
- `ReferenceWireDir`: world Z-axis.
- `AngleCount`: `18`.
- `Count`: `41`.
- `Extension`: `10 mm`.
- `Flip`: `false`.
- `Tolerance`: `0.01 mm`.

### C# execution

1. Calculate the geometry extent along `PathDir`.
2. Create 41 section planes normal to the path direction.
3. Rotate a candidate wire direction around `PathDir`.
4. Test 18 directions over 180 degrees.
5. Intersect the solid with every section plane.
6. Evaluate each separate section contour.
7. Select the widest individual contour along the tested wire direction.
8. Create a finite wire line from the contour extrema.
9. Calculate valid-section coverage for each candidate direction.
10. Calculate the coefficient of variation of wire lengths.
11. Calculate the maximum adjacent wire-direction change.
12. Score each direction using coverage, length variation, and direction stability.
13. Select the highest-scoring candidate.
14. Create planes, rails, midpoint path, and ruled envelope.
15. Return the selected rotation through `BestAngle`.
16. Return every candidate score through `Scores`.

### When to use it

- Use it to investigate a Brep, Surface, Extrusion, SubD, or Mesh.
- Use the scores to compare possible wire orientations.
- Treat the result as an envelope heuristic.
- Concave or multiply connected solids may not be physically hot-wire manufacturable.

## How to inspect the Grasshopper demonstration

1. Open `Hotwire_TwistedBlade_Implementation.3dm` in Rhino 8.
2. Open `Hotwire_TwistedBlade_AllAlgorithms.gh` in Grasshopper.
3. Keep the Grasshopper solver enabled.
4. Inspect each colored branch separately.
5. Read the report panel beside each C# component.
6. Preview `WireLines` to see the finite wire positions.
7. Preview `Ruled` to see the swept cutting surface.
8. Preview `Planes` to check target order and orientation.
9. Change `Count` to test path resolution.
10. Change `Extension` to match the physical wire clearance.
11. Toggle `Flip` if the tool X-axis is reversed.
12. Connect `Planes` to the required native KUKA|prc motion component.

## KUKA|prc handoff

1. Select the algorithm that matches the intended fabrication logic.
2. Connect its ordered `Planes` output to the chosen KUKA|prc motion instruction.
3. Set the real robot model.
4. Set the calibrated robot base.
5. Set the calibrated hot-wire tool and TCP.
6. Set motion speed and interpolation.
7. Inspect joint values and configuration changes.
8. Check reachability and singularities.
9. Run collision checking with the complete cell.
10. Export controller code only after simulation passes.

## Required physical validation

The C# scripts generate geometry and orientation targets. They do not guarantee a safe or accurate physical cut.

- Confirm the real usable wire length.
- Confirm foam-block position and dimensions.
- Calibrate the tool, TCP, base, and work object.
- Test temperature, feed rate, kerf, and material behavior.
- Add approach, lead-in, lead-out, and retract motions.
- Check robot, tool, fixture, foam, and environment collisions.
- Confirm controller configuration and external-axis behavior.





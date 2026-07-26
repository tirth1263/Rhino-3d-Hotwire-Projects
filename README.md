# Hotwire Geometry To Planes - Grasshopper C#

This folder is separate from the earlier design files. It contains one reusable geometry-to-hot-wire component rather than a collection of designs.

## Files

- `Hotwire_GeometryToPlanes_Rails.gh` - exact paired-rail example with the C# code embedded inside a C# Script component.
- `Hotwire_GeometryToPlanes_Sections.gh` - geometry-in/planes-and-ruled-surface-out example using the same embedded C# component.
- `Hotwire_GeometryToPlanes.cs` - the same source code as a separate editable/versionable file.
- `Hotwire_GeometryToPlanes_Sample.3dm` - neutral validation geometry and paired rails; not a design proposal.
- `Hotwire_GeometryToPlanes_QA.png` - fresh-open viewport verification.
- `VERIFICATION.tsv` - fresh-open checks for both files plus an in-memory Mesh-input test.

## Component inputs

| Input | Type | Meaning |
|---|---|---|
| `G` | Geometry | Brep, Surface, SubD, Extrusion, or Mesh used in automatic section mode. |
| `RailA` | Curve | Optional first boundary rail. Supply with `RailB` for exact ruled mode. |
| `RailB` | Curve | Optional second boundary rail. Supply with `RailA` for exact ruled mode. |
| `PathDir` | Vector | Direction in which section planes advance. Default in the example is World X. |
| `WireDir` | Vector | Direction used to find the two extreme points on each section. It is projected perpendicular to `PathDir`. |
| `Count` | Integer | Number of requested wire positions/planes. Clamped to 2-2000; values below 2 use 41. |
| `Extension` | Number | Extra wire length added beyond each selected endpoint, in model units. |
| `Flip` | Boolean | Reverses the A-to-B wire direction and therefore the plane X axis. |
| `Tolerance` | Number | Intersection tolerance. Values at or below zero use the Rhino document tolerance. |

## Component outputs

- `Planes`: ordered TCP frames at the center of the wire.
- `WireLines`: ordered finite hot-wire positions.
- `Ruled`: straight loft swept between the two generated endpoint rails.
- `RailAOut`, `RailBOut`: synchronized endpoint rails.
- `MidPath`: ordered wire-midpoint path.
- `WireLengths`: wire span at every sample.
- `Report`: mode, counts, span range, frame-change diagnostics, and limitations.

## Two operating modes

### Paired rails - exact ruled workflow

Connect both `RailA` and `RailB`. The component aligns their directions, samples both by equal normalized arc length, joins paired points with the wire, builds midpoint planes, and creates the ruled surface. `G`, `PathDir`, and `WireDir` do not control the geometry in this mode.

### Geometry sections - automatic initialization

Leave either rail input empty and connect `G`. The component advances planes normal to `PathDir`, intersects the geometry, selects the widest individual contour along `WireDir`, and uses its extrema as each straight-wire position. This follows the transferable initialization idea in RoboCut: intersect the target with candidate cutting planes, resample an ordered path, then derive tool states.

For arbitrary closed or double-curved geometry this is a ruled envelope/initial path, not a mathematical guarantee of an exact fit. A straight wire can sweep only ruled surfaces; RoboCut's non-ruled results require a flexible rod controlled by two robot end-effectors and an elasticity/trajectory optimizer.

## Plane convention and KUKA use

Each output plane uses:

- origin = wire midpoint;
- X = wire direction from A to B;
- Y = local travel direction projected perpendicular to X;
- Z = X cross Y.

The planes are intentionally robot-brand-neutral. Before KUKA|prc commands, remap/rotate them to match the calibrated custom-tool TCP axes. The component does not perform inverse kinematics, collision checking, kerf compensation, temperature/feed calibration, or controller validation.

## Verification performed

- Both `.gh` files were reopened from disk in Rhino 8, rebuilt, and solved.
- Each mode returned 41 planes, 41 finite wire lines, one ruled surface, two endpoint rails, one midpoint path, and 41 wire-length values.
- Grasshopper reported zero warnings and zero errors in both fresh-open tests.
- The section algorithm was also rerun with a Mesh input and returned the same output counts with zero warnings or errors.
- The embedded C# source was confirmed present in both definitions.
- The sample `.3dm` reopened with 3 objects, 3 layers, and millimetre units.

Exact machine execution still requires the calibrated tool-axis transform, KUKA|prc reach/collision checking, cell geometry, kerf compensation, and process testing.

## Research basis

- Simon Duenser et al., [RoboCut: Hot-wire Cutting with Robot-controlled Flexible Rods](https://crl.ethz.ch/papers/hotwirecutter.pdf), ACM TOG 39(4), 2020.
- [SIGGRAPH 2020 RoboCut video](https://www.youtube.com/watch?v=lLKI0HWV3dc).

The paper explicitly distinguishes conventional straight-wire ruled surfaces from its flexible-rod, dual-arm extension. It also initializes cuts by intersecting target geometry with cutting planes, offsetting/resampling candidate curves, and then optimizing collision-free trajectories. This component implements the straight-wire geometric portion only.

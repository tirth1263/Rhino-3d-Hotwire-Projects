# Verification — what has been checked, and what you check on the canvas

Three levels. The first two are **done and passing**. The third needs Rhino open
and takes about twenty minutes.

> **Level 2 summary — 25 + 26 = 51 automated checks, ALL PASSED**, executed
> inside Rhino 8.33.26188.13001 on 3 and 4 August 2026. Full output in
> `TEST_RESULTS.txt`. Between them they found **four real bugs**, listed below.
> That is why it was worth running.

---

## Level 1 — done: it compiles against the real Rhino 8 libraries

All three components were assembled into a single .NET assembly and compiled
against the actual DLLs on this machine:

```
C:\Program Files\Rhino 8\System\RhinoCommon.dll
C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Grasshopper.dll
C:\Program Files\Rhino 8\Plug-ins\Grasshopper\GH_IO.dll
```

**Result: compiles clean. No errors, no warnings.**

This is not a formatting check. It proves every RhinoCommon and Grasshopper
call in the three files exists, with the argument types and overloads I used —
`Intersection.MeshPlane`, `Curve.LengthParameter`, `Quaternion.Rotation` /
`GetRotation`, `Plane.RemapToPlaneSpace`, `Mesh.SolidOrientation`,
`Mesh.ClosestPoint`, `Curve.ToPolyline`, `Brep.ClosestPoint`, `DataTree<T>`,
`GH_Path`, `GH_RuntimeMessageLevel`, all of it. Those are exactly the mistakes
that otherwise turn up as a red component ten minutes into a build.

It also confirms the code is within the C# language level the script component
accepts, so nothing will fail to build once pasted.

### What Level 1 does not prove

That the maths gives the right answer. Compiling and being correct are
different things. That is Level 2.

---

## Level 2 — done: it runs, inside real Rhino, and gives right answers

Rhino 8.33 was started headless in-process, Grasshopper loaded, and the three
components' helper classes executed against real geometry. **25 checks, all
passed.** Full transcript in `TEST_RESULTS.txt`; images of the output in
`renders/`.

Headline numbers, all measured rather than asserted:

| Check | Result |
|---|---|
| FL-01 orientation, seam pinned | origin drift **1.3e-6 mm**, rotation drift **0.0012°** on a 268 mm part |
| FL-01 orientation, seam automatic | path identical; only the loop start moves — see below |
| FL-01 residual is discretisation | path drift **1.2543 → 0.1392** when samples go 28 → 112 (9× for 4×) |
| FL-01 `rollMode` moves no point | worst **7.1e-15 mm** |
| FL-01 `tiltDeg` moves no point on the work | worst **0.0 mm** exactly |
| TF-09 orientation, whole cell rotated | origin drift **0.000000 mm**, rotation drift **0.000000°** |
| TF-09 KUKA A/B/C round trip | worst **2.1e-8 rad** (1.2e-6 degrees) |
| TF-09 draw-order optimisation | air travel **9.595 m → 2.895 m**, pen changes **14 → 3** |
| TF-09 resume at index 7 | acquires the right pen before the first `LIN` |
| Simulator ends | `t=0` and `t=1` sit exactly on the first and last target |
| Simulator timing | halving the feed exactly doubles the process time |

### The three bugs it found

**1. Sections could come back broken, silently.** `Intersection.MeshPlane` does
not promise one polyline per loop — where the plane grazes a vertex it returns a
section as several open arcs. The code kept the longest and dropped the rest.
On the test model one section came back **25% short with a straight 60 mm jump
across the middle of the part**, and nothing warned. Fixed: the arcs are stitched
before use, and anything still open afterwards is reported as a hole in the mesh.

**2. The loop start jumped when the model was rotated.** The seam fell back to
the mid principal axis, and an eigenvector's *sign* is not determined by the
shape when the shape is symmetric about that axis — which most real parts are.
The sign then came from a world-dependent tie-break. Fixed: the seam is now read
from the loop's own outline, and `seamGuide` pins it when determinism matters.

**3. Sampling phase was tied to the polyline's start.** Arc length was measured
from wherever the intersection routine began the polyline, which is not a
property of the shape. Fixed: winding, seam and sampling are now done in that
order, by direct arithmetic on the section vertices.

Two further "failures" turned out to be **wrong tests, not wrong code**: the
lead-in/out points are *supposed* to move with `tiltDeg` (they follow the tool
axis), and a 1e-9 rad tolerance on the A/B/C round trip is tighter than `asin`
and `atan2` can deliver. Both assertions were corrected, not the code.

### The second run — 4 August, checking TF-09 against the end-effector spec

The first run proved the code did what *it* set out to do. It did not check
that against the drawing end-effector's own specification
(`end-effectors/01-drawing/README.md`). Reading the two side by side found four
things the spec asks for and the code did not do. 26 new checks, all passing.

**4. `$BASE` was hard-coded, so BASE[2] silently stopped working.**
`PEN_SET_TOOL` set `$BASE = BASE_DATA[1]` unconditionally. The end-effector
README defines BASE[1] as the worktable centre and BASE[2] as the large-format
shift. A job set up on BASE[2] therefore drew correctly right up until the first
pen change, then reverted to BASE[1] for the rest of the sheet — the worst shape
of bug, because the first stroke looks fine. The paper base and the magazine
base are now separate (`PEN_BASENO` / `PEN_MAGBASE`), and every `$BASE` write
goes through `PEN_USE_BASE()`, which forces an advance-run stop first.

The other three were **missing**, not wrong:

- **Z press offset (3 mm).** There was none. The pen was commanded exactly to
  the drawing plane, which defeats the point of a spring-loaded holder: any
  error in that plane came out as a line that was absent rather than light.
- **Curved-surface drawing.** `drawGeo` supplied the tool's *normal* but not its
  *position*, so a flat drawing over a scanned sheet kept its flat coordinates.
  The pen axis would have been correct and the pen would have been in mid-air.
  Strokes are now projected onto `drawGeo`.
- **Three defaults did not match the spec's table** — draw 50 vs 100 mm/s, air
  250 vs 500 mm/s, lift 20 vs 30 mm.

---

## Level 3 — run these on the canvas

Tick them off. Anything that fails, tell me the number and what it said.

### FL-01

| # | Check | How | Expected |
|---|---|---|---|
| 1 | It solves | Wire a mesh to `geo`, `sections` 10, `samples` 28 | `Status` starts with `OK` |
| 2 | It found the right axis | Preview `PartFrame` | Red X arrow runs down the model's longest direction |
| 3 | Planes point the right way | Preview `Planes` zoomed in | Blue Z arrows point **into** the material |
| 4 | **Orientation proof** | `selfTest` → True | `RESULT: PASS`, or `PASS (GEOMETRY)` if the sections are near-symmetric. Wire a Point to `seamGuide` to get the exact pass |
| 5 | Roll is genuinely free | Note `Points`. Flip `rollMode` 0 → 1 | **Not one point moves.** Only the frames spin. If a point moves, roll has been coupled to geometry and that is a bug |
| 6 | Tilt is genuinely free | Set `tiltDeg` to 23 | Same: not one point moves |
| 7 | Ambiguity is reported | Feed it a **sphere** | Orange warning: *"…'long axis' is ambiguous…"*. It must warn, not guess |
| 8 | Ambiguity is fixable | On the sphere, `axisMode` → 5, wire a vector | Warning clears, slices follow your vector |
| 9 | Vertical model | `Rotate3D` the mesh 90°, re-solve, nothing else changed | Same toolpath, rotated with the part |

### TF-09

| # | Check | How | Expected |
|---|---|---|---|
| 10 | It solves | Wire curves, `drawPlane`, `slotPlanes`, both toggles True | `Status` starts with `OK` |
| 11 | Minimum swaps | `groupByPen` True, 3 pens in `penIds` | `SwapCount` = 3. More means `penIds` does not line up with `curves` |
| 12 | Nothing is lost | Look at `StrokeOrder` | Same length as the curve list, every index appearing exactly once |
| 13 | Ordering helps | `optimize` off, note `TravelDist`; turn it on | Travel goes **down**, never up. `Log` prints the percentage |
| 14 | Grouping helps | `groupByPen` off | `SwapCount` goes **up**. That is the trade being made visible |
| 15 | **Orientation proof** | `selfTest` → True | `RESULT: PASS`, including the KUKA A/B/C round trip line |
| 16 | Vertical paper | Rotate `drawPlane` to vertical, nothing else changed | Whole job rotates with it. Lead-ins still perpendicular to the paper |
| 17 | Lead-in is clean | Zoom to one stroke start | Hover point directly above the first point, straight drop, nothing in between |
| 18 | Dry run is the default | Disconnect `liveRun` entirely | `KRL` header still says `DRY RUN ON` |
| 19 | Live run is loud | `liveRun` → True | Orange warning on the component saying the pen will touch the paper |
| 20 | **Resume fetches, not assumes** | Note `PenSequence` at index 7. Set `startIndex` = 7 | The first `PEN_ENSURE(n)` in `KRL` is for **that** pen, and it appears before any `LIN` |
| 21 | KRL is well formed | Read the `KRL` panel | Header block with counts; `LIN {X …, Y …, Z …, A …, B …, C …}`; `PEN_ENSURE` between stroke blocks; ends `PEN_ENSURE(-1)` then `PTP XHOME` |
| 21a | **Press goes into the paper** | `liveRun` on, zoom to one stroke | Drawing targets sit **3 mm below** the paper plane, hover points 35 mm above. On a vertical `drawPlane` the 3 mm goes into the wall, not downwards |
| 21b | Press does not move the drawing | Note `Flat`. Change `pressDepth` 3 → 8 | Every point moves **only along the pen axis**. Nothing shifts sideways. If it does, press has been coupled to the in-plane geometry and that is a bug |
| 21c | Defaults are the spec | Disconnect `feedDraw`, `feedRapid`, `hover`, `pressDepth` | `KRL` header reads `draw 100 mm/s   air 500 mm/s   lift 30 mm   press 3 mm` — the end-effector README's table |
| 21d | **Base is declared and restored** | `baseIndex` = 2, `magBaseIndex` = 1 | Header shows both; `$BASE = BASE_DATA[2]` appears **before** the first `LIN`; `PEN_SET_TOOL` hands base 2 back after each swap |
| 21e | Curved sheet is projected | Wire a curved surface to `drawGeo`, flat curves to `curves` | Targets land **on the surface**, not in the plane the curves were drawn in. `Log` prints the largest pull |
| 21f | Curved sheet is orientation safe | With `drawGeo` wired, `selfTest` → True | Still `RESULT: PASS` |

### Simulator

| # | Check | How | Expected |
|---|---|---|---|
| 22 | Ends are exact | `t` = 0, then `t` = 1 | `TCP` sits exactly on the first / last target |
| 23 | Timing is real | Halve `feedProcess` | `CycleTime` for the process portion doubles |
| 24 | Swaps cost time | Set `dwellSeconds` = 25 with TF-09 data | `CycleTime` rises by 25 × the swap count |
| 25 | It flags the right thing | Read `MaxTurnRate` and `HotSpots` | Hotspots land on tight corners, not on long straight moves |

### Robot side

Not verifiable on the canvas. It is the checklist in
`02_TF09_pen_switching/krl/PENSWAP_README.md`, and the abort table in Step 3
of that file is the evidence for the board's "safe-abort never strands a pen".

---

## Why checks 5 and 6 are in here

They look like trivia. They are not.

Spinning a round tool about its own axis, or leaning it, must not move the
point it is touching. If flipping `rollMode` or nudging `tiltDeg` moves a
single output point, then the orientation controls have been accidentally
wired into the geometry — which means every clearance adjustment silently
changes the cut. That class of bug is invisible on screen and shows up in the
material. Two five-second checks rule it out.

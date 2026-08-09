# How everything is implemented, step by step

`RUN_ON_ROBOT.md` tells you which buttons to press. **This file explains what
happens after you press them** — every stage, in the order the data moves
through it, and why each one is built the way it is.

You do not need this to run the tools. You need it to change them, to defend a
number to somebody, or to work out where something went wrong.

> **The tool-and-orientation story for each job has its own document**, because
> both turned out to be longer and more surprising than a section here would
> hold:
>
> - the hotwire on FL-01 — [`HOTWIRE_ORIENTATION.md`](HOTWIRE_ORIENTATION.md)
> - the pen tool on TF-09 — [`TF09_ORIENTATION.md`](TF09_ORIENTATION.md)
>
> Each ends with the numbers worth carrying into the next project, and each one
> is reproducible: `_build\render_steps.ps1` and `_build\verify_tf09.ps1`.

### Contents

1. [The shape of the whole thing](#1-the-shape-of-the-whole-thing)
2. [The one design rule everything obeys](#2-the-one-design-rule-everything-obeys)
3. [FL-01, stage by stage](#3-fl-01-stage-by-stage)
4. [TF-09, stage by stage](#4-tf-09-stage-by-stage)
5. [From a plane to a KUKA target](#5-from-a-plane-to-a-kuka-target)
6. [The robot side: PENSWAP](#6-the-robot-side-penswap)
7. [The simulator](#7-the-simulator)
8. [The orientation proof](#8-the-orientation-proof)
9. [How the Grasshopper canvases are assembled](#9-how-the-grasshopper-canvases-are-assembled)
10. [How it is all tested](#10-how-it-is-all-tested)
11. [Where each claim comes from](#11-where-each-claim-comes-from)

---

## 1. The shape of the whole thing

Four pieces. Three run in Grasshopper, one runs on the robot.

```
                    ┌───────────────────────────────────────┐
   a mesh  ────────▶│ FL-01   mesh → planes                 │────▶ Planes (tree)
                    └───────────────────────────────────────┘         │
                                                                      ▼
                                                              KUKA|prc writes .src

   ┌──────────────┐ ┌───────────────────────────────────────┐
   │ PENTOOL      │▶│ TF-09   pen-switching drawing job     │────┬─▶ KRL  (the .src)
   │ board switch │ └───────────────────────────────────────┘    │
   └──────────────┘   ▲              ▲                           └─▶ Targets → prc
     DrawPlane ───────┘              │                               (reach check only)
     PenLean ────────────────────────┘
     ToolPlane ──────────────────────────────────────────────────▶ prc Custom Tool
                                     ▲
   curves (authored flat) ──▶ Orient ┘        slot planes ────────┘
                    ┌───────────────────────────────────────┐
   planes  ────────▶│ SIM     playback + timing             │────▶ pictures, cycle time
                    └───────────────────────────────────────┘

   on the controller:  DRAW_JOB.src  +  PENSWAP.src  +  PENSWAP.dat
```

Each Grasshopper tool is **one C# component**, not a cluster of native
components. Three source files feed the three panes of that component:

| Pane | File | Holds |
|---|---|---|
| Usings | `*_usings.cs` | `using` lines, nothing else |
| Script | `*_body.cs` | reads the inputs, calls the helper, assigns the outputs |
| Members | `*_helpers.cs` | the entire algorithm, as one static class |

The body is deliberately thin — about a hundred lines of plumbing. Everything
that could be wrong lives in the members pane, in one class, which is what
makes it testable outside Grasshopper (section 10).

---

## 2. The one design rule everything obeys

**Nothing keys off world Z.**

This is the requirement that shapes almost every decision below, so it is worth
stating precisely. The tools must produce the same job whether the part lies
flat, stands upright, or sits at 37°, and whether the paper is on a table or
taped to a wall.

That rules out the obvious implementations:

| The obvious way | Why it is not used |
|---|---|
| Slice along world Z | Turns the part on its side and you slice it the wrong way |
| "Up" is `+Z` | Tape the paper to a wall and the pen lifts sideways |
| Lift the pen by `+30` in Z | On a vertical sheet that drags the pen across the page |
| Sort strokes by Z height | Meaningless once the work is tilted |

What is used instead:

- **FL-01** derives the slice axis from the *model's own* principal axes
  (§3.2). Rotate the model and those axes rotate with it.
- **TF-09** takes "up off the paper" from `drawPlane.ZAxis`, or from the real
  surface normal when a curved sheet is supplied. Press, lift, lead-in and
  lead-out all travel along the **pen's own axis**, never along a world axis.
- Magazine slots are supplied as full **planes**, not heights.

The single place a world axis genuinely appears is the KUKA A/B/C Euler
conversion (§5), because that convention is defined against the robot's base
frame. So that conversion is round-tripped and checked separately.

Both tools carry a `selfTest` switch that proves the property rather than
asserting it (§8).

---

## 3. FL-01, stage by stage

**Input:** a mesh. **Output:** a tree of planes, one branch per slice, that
KUKA|prc's LIN component can eat directly.

### 3.1 Condition the mesh

`Condition()` — heal what can be healed, and report what cannot.

Duplicate vertices are welded, degenerate faces dropped, the mesh unified so
the face normals agree with each other. This matters because the very next step
intersects planes with it, and a mesh with a flipped face or a zero-area
sliver hands back broken sections.

Anything that could not be fixed becomes a warning rather than a silent repair.

### 3.2 Find the model's own frame

`PrincipalFrame()` — this is what makes orientation irrelevant.

1. Take every triangle.
2. Weight each by **its area**.
3. Build the 3×3 covariance matrix of the triangle centres about the
   area-weighted centroid.
4. Diagonalise it with a Jacobi rotation solver (`Jacobi3()`).
5. The eigenvectors are the directions the model is longest, middling and
   shortest in.

Area weighting rather than vertex counting is the important detail. Vertices
cluster wherever the modeller happened to add detail — a densely tessellated
end cap would drag the axis towards itself. Area is a property of the shape,
not of how it was built.

Those three directions are welded to the model: rotate the model, they rotate
with it. That is exactly the property §2 requires.

**The honest limit.** A shape that is symmetric about an axis — a sphere, a
cube, a plain cylinder — has no unique longest direction, because two or three
eigenvalues are equal. No maths can invent one. `Skew()` and `PinSign()` detect
the near-degenerate case, the component warns, and the self-test reports FAIL
rather than pretending. The fix is to supply the axis by hand (`axisMode = 5`).

### 3.3 Choose the slice direction

`ChooseAxis()` maps `axisMode` onto a direction:

| Mode | Direction |
|---|---|
| 0 | the model's longest axis (default) |
| 1 | the model's shortest axis |
| 2/3/4 | world X / Y / Z |
| 5 | whatever you wired into `customAxis` |

Modes 2–4 exist because sometimes you genuinely do want the world — but they
break the orientation guarantee, so the component **warns when you use them**.

`ProjectExtents()` then measures how far the mesh reaches along that axis, and
the slice positions are spread evenly across that span.

### 3.4 Slice, and stitch the pieces back together

This is where the worst bug in the project lived, so it is worth reading.

`Intersection.MeshPlane` does **not** promise one polyline per loop. Where the
plane grazes a vertex, or where conditioning removed a sliver, it returns a
single section as several **open arcs**.

The original code took the longest arc and dropped the rest. The result looked
completely plausible — a closed-ish loop, sensible planes, no warning — but was
about 25% short, and put a **straight 60 mm jump across the middle of the
part**. On the hot-wire cutter that reaches the foam.

`JoinAndFilter()` fixes the ordering: **stitch the arcs on their shared
endpoints first, then decide which loops to keep.** Anything still open after
stitching is a real hole in the mesh, and is reported rather than hidden.

`loopMode` decides whether to keep only the largest loop per slice or every
loop (a part with a fork has two loops at some heights).

### 3.5 Pick where each loop starts, and which way round

Two neighbouring slices must start at roughly the same place and run the same
way round. Otherwise the tool sprints across the model at every slice and the
wrist unwinds itself.

- **Winding:** `SignedArea()` measures the loop's signed area in the slice
  plane and reverses it if the sign disagrees with its neighbour.
- **Seam:** a smooth closed loop has no natural start — there is no corner to
  call the beginning. `HarmonicSeam()` picks one from the loop's own outline,
  which is stable when the section has a clear long direction and a coin toss
  when it is nearly circular.

  Wire a point into `seamGuide` and every loop starts at the point nearest to
  it instead, which makes the result exactly reproducible. Do that when
  re-cutting a part to match an earlier run.

  Either way **the cut is identical** — only the point the tool enters at
  moves.

`Thin()` then drops points closer together than `minSpacing`, because a target
every 0.2 mm is not precision, it is a full command buffer.

### 3.6 Build the frames

`BuildFrames()`. The convention, which must match what KUKA|prc's LIN expects:

```
origin : the point on the model
X      : direction of travel along the loop
Z      : the approach — points FROM the tool INTO the material
Y      : Z × X, so the frame is right handed
```

`Outward()` finds the surface direction, either from the mesh normal
(`normalMode = 0`) or radially from the slice centre (`normalMode = 1`). Radial
is the better choice on a noisy scan, where individual mesh normals wobble.

`MinimiseRoll()` is the quiet one that earns its keep. A round tool — a router
bit, a pen — does not care how it is rolled about its own axis. So instead of
letting each frame's X chase every wiggle in the curve, each frame aims its X
as close as possible to the **previous** frame's. That spends the free rotation
on holding axis 6 still. `rollMode = 0` disables it for a hot wire, which is a
rigid bow and very much does care.

`AddLeads()` adds the lead-in and lead-out, along the tool axis.

`TurnDegrees()` measures the wrist rotation between neighbouring targets and
warns above `maxTurnDeg` — that is the number that predicts a wrist snap.

### 3.7 Optionally move the job into the cell

`fromFrame` / `toFrame` are a pair: supply both and the whole result is
transformed from one to the other by `Remap()`. That is how a job authored
around the origin gets placed where the work actually sits.

---

## 4. TF-09, stage by stage

**Input:** curves, a paper plane, magazine slot planes.
**Output:** targets, and a complete `.src`.

### 4.1 Assign a pen to each stroke

`PenFor()`. `penIds` gives one number per curve; missing entries reuse the
last, and anything past the end of the magazine is clamped to the last slot.
`ToolOf()` maps a slot to a KUKA `TOOL[]` number, defaulting to slot 0 → TOOL[1]
— which matches the end-effector spec's TOOL[1] technical pen, TOOL[2] marker,
TOOL[3] brush.

### 4.2 Order the strokes — board item D1-01

`OrderStrokes()`. **Two costs, and they are not the same size:**

- a pen change is **tens of seconds**
- a hop between two strokes is **tenths of a second**

So the ordering is hierarchical and never trades a swap for travel:

1. **Group by pen** (`groupByPen`), so each pen is finished before it is put
   back. This alone takes the swap count down to the number of pens, which is
   the minimum possible.
2. **Visit the groups nearest-first**, so the group order is not just pen 0, 1,
   2 by accident of numbering.
3. Inside a group, `GreedyChain()` runs nearest-neighbour **considering both
   ends of every candidate** — if the far end is nearer, the curve is reversed.
   That is the flip half of D1-01.
4. `TwoOpt()` then un-paints the corner greedy always paints itself into, by
   reversing sub-runs while they keep helping.
5. `ReflowFlips()` re-decides every flip once the final order is known, because
   a flip that was right mid-search may not be right at the end.

`ChainLength()` measures before and after, and the saving is printed to `Log`.

### 4.3 Build the frames for one stroke — board item D1-03

`BuildStroke()`. Convention identical to FL-01: origin on the paper, X along
travel, **Z down the pen from holder into paper**, Y = Z × X.

`SurfaceUp()` returns "away from the paper": `drawPlane.ZAxis` normally, or the
real surface normal at that point when `drawGeo` is wired. `SampleStroke()`
projects each sampled point onto `drawGeo` first, so on a curved sheet the pen
both sits on the surface and stays square to it.

**Roll continuity — and the bug that hid in it.** A pen is round about its own
axis, so the roll is free, and it is spent on keeping axis 6 still: each frame
aims its X as close as possible to the previous one rather than chasing the
curve.

That was originally done *within* a stroke only. At every stroke start the roll
reset to the new travel direction — so two strokes drawn in opposite directions
asked axis 6 for an **instant 180° flip** between them. Measured over the
eight-stroke demo job: **1,170° of total roll change**, which KUKA|prc rejected
as unreachable even though every individual target and every consecutive pair
was fine.

`BuildStroke` now takes `ref Plane lastFrame`, the same pattern FL-01 already
used, and the chain runs through the entire job:

- seeded from `homePlane` when there is one, since that is where the arm starts;
- carried from the **magazine slot plane** across a swap, because that trip
  lands between the two strokes and is what the wrist actually rolls on from;
- rolled on a copy, so a stroke that produces no frames commits nothing.

Slot planes themselves are never re-rolled — they are taught poses and their
orientation is a physical fact.

The same job now asks for **0°**. The drawing is unchanged, because roll is
rotation about the pen's own axis: same tip positions, same pen direction, only
axis 6 moves differently.

**Press and lift, from one signed number:**

```csharp
double press = o.DryRun ? -o.Hover : o.PressDepth;
```

- **live:** `+PressDepth`, i.e. the tip is commanded *past* the paper. The
  holder is spring-loaded and absorbs the overtravel. This is the point: a
  half-millimetre of calibration error then gives a *firmer line* instead of
  *no line*.
- **dry:** `-Hover`, i.e. the whole stroke lifts clear. Same shape, same
  rhythm, never touches anything.

One number covers both cases, so there is only one path to get wrong.

Lead-in and lead-out travel **straight down the pen's own axis** with no
intermediate point, which is what stops the sideways creep at touch-down that
leaves a witness mark. The clearance is measured from the *paper*, not from the
pressed point, so `hover` means the same thing whether or not press is applied.

### 4.4 Insert the magazine trips

In `Core()`, whenever the next stroke needs a different pen from the one held, a
marker frame at that slot's plane is inserted with move type `2`. That is what
makes the preview and the simulator show the trip to the magazine instead of
teleporting.

Note that **slot planes are used verbatim as robot targets**, so they carry the
same convention as everything else: a slot plane's Z runs *from the tool into
the slot*. Get that backwards and the arm is asked to reach the magazine from
underneath the floor.

### 4.5 Write the KRL

`WriteKrl()`. The header records the settings — speeds, lift, press, both base
numbers, and whether dry run is on — so the generated file is readable without
opening Grasshopper.

Then, for each stroke:

```
PEN_ENSURE(n)         ; "I need pen n" — see §6
LIN {frame}           ; targets, C_DIS on the air moves
```

**The job never says "park" or "acquire".** It states which pen it needs, and
one routine on the robot works out whether that means doing nothing, fetching
one, or putting one back first. That single choke point is what makes the abort
case tractable (§6).

`startIndex` lets a job resume from a given stroke after a stop, and the
generated file re-establishes the correct pen before it moves.

---

## 5. From a plane to a KUKA target

`PlaneToAbc()` converts a Rhino plane into KUKA's `{X,Y,Z,A,B,C}`.

KUKA uses **Z-Y'-X'' intrinsic Euler angles**, so the rotation matrix is
`R = Rz(A)·Ry(B)·Rx(C)`. Reading the angles back out of the frame's axis
vectors:

```
B = asin(-r20)
A = atan2(r10, r00)
C = atan2(r21, r22)
```

**Gimbal lock is handled explicitly.** When `B` is ±90° the other two angles
stop being independent — only `A + C` (or `C − A`) is determined. The code
detects it, pins `A = 0`, which is the usual convention, and solves for `C`.
Without that branch the `atan2` calls return garbage in exactly the pose where
a wire cutter spends much of its time.

`AbcToPlane()` rebuilds a plane from the angles. It exists so the self-test can
round-trip every output plane through the conversion and back, and check it
lands on itself. This is the one place a world axis genuinely appears, so it is
the one place that gets its own check.

The round trip is limited by `asin`/`atan2` precision — about 1e-9 rad is as
tight as it can deliver, so assertions are set just above that floor.

---

## 6. The robot side: PENSWAP

`PENSWAP.src` is **subroutines only**. Running `PENSWAP` itself just configures
the magazine and reports what it thinks it is holding; it does not move.

### The one rule that makes abort safe

> The collet is only ever commanded to change state while the tool is at a
> slot's **seated** pose. Never in mid-air, never while moving, never at the
> approach pose.

Therefore, at every instant, a pen is either **fully in its slot** or **fully in
the gripper**. There is no position in the whole cycle from which a stop can
drop one. That is the whole safety argument, and everything else is bookkeeping
to preserve it.

### The phase table

`PEN_PHASE` records which side of each irreversible step the robot is on, and is
written to a **persistent DAT variable**, so it survives a power cycle:

| # | Phase | Meaning |
|---|---|---|
| 0 | `IDLE` | nothing in flight; `PEN_HELD` is the truth |
| 1 | `TO_PARK` | holding a pen, travelling to its slot's approach pose |
| 2 | `SEATING` | holding a pen, moving approach → seat |
| 3 | `RELEASING` | at the seat, collet commanded **open** |
| 4 | `WITHDRAW` | collet open and confirmed, pen is in the slot, backing out |
| 5 | `TO_PICK` | gripper empty, travelling to the target slot's approach |
| 6 | `ENGAGING` | gripper empty, moving approach → seat, collet open |
| 7 | `CLAMPING` | at the seat, collet commanded **closed** |
| 8 | `EXTRACT` | collet closed and confirmed, pen held, backing out |

`PEN_INIT()` reads the phase on the next start and **finishes the half-done
transaction before anything else is allowed to move**. `PEN_RECOVER()` has a
branch per phase; each one ends with the pen either in its slot or in the
gripper. Where the program cannot tell which, it asks the sensor; where it
cannot ask, it stops and says what to check.

### The routines

| Routine | Does |
|---|---|
| `PEN_CONFIG()` | the slot table — poses and tool numbers |
| `PEN_INIT()` | reads the phase, finishes anything half-done |
| `PEN_RECOVER()` | the per-phase recovery branches |
| `PEN_ENSURE(want)` | **the only entry point a job uses** |
| `PEN_DO_PARK(s)` / `PEN_DO_ACQUIRE(s)` | the two halves of a swap |
| `PEN_GOTO_APPR(s)` / `PEN_GOTO_SEAT(s)` | the two magazine poses |
| `PEN_COLLET_OPEN()` / `PEN_COLLET_CLOSE()` | the only actuation, seat only |
| `PEN_SET_TOOL(s)` | sets `$TOOL` for the slot |
| `PEN_USE_BASE(b)` | **the only place `$BASE` is ever written** |

### Why `PEN_USE_BASE` exists

`PEN_SET_TOOL` used to end with a hard-coded `$BASE = BASE_DATA[1]`. The
end-effector spec defines BASE[1] as worktable centre and BASE[2] as the
large-format shift — so a job set up on BASE[2] drew correctly right up until
the first pen change, then referenced the rest of the sheet to the wrong base.
The beginning looks fine, which is the worst possible shape for a bug.

Now the paper has a base (`PEN_BASENO`) and the magazine has its own
(`PEN_MAGBASE`), because the magazine is bolted to the cell and must not be
dragged through the large-format offset. Every `$BASE` write goes through one
routine, which:

- rejects a base number below 1 with a message rather than moving,
- issues `WAIT SEC 0` **first** — an advance-run stop, because writing `$BASE`
  while a move is already planned gets that move recomputed in the new base.

`PEN_DRYRUN = TRUE` suppresses every collet command, so the whole cycle can be
watched at T1 speed with nothing actuating.

---

## 7. The simulator

`PathSim.Run()` in `03_toolpath_sim`. It takes a flat list of planes and move
types and a slider `t` from 0 to 1, and reports where the tool is at that
moment.

Feeds differ by move type (process vs rapid vs a dwell at the magazine), so the
time axis is not simply proportional to distance — which is the point, since
that is what makes the cycle-time estimate worth anything.

**What it does not do:** check reach, joint limits or singularities. KUKA|prc's
own Analysis component does that properly and it stays in the definition. This
runs alongside it, because prc's playback cannot answer *how long will this
take* or *how fast is the wrist turning here*.

The number that matters most is `MaxTurnRate` — **degrees of wrist rotation per
millimetre of travel**. A big turn over a long move is gentle; the same turn
over a short move is what makes the wrist fault or gouge. `HotSpots` lists every
segment over the limit by index.

---

## 8. The orientation proof

Both tools carry a `selfTest` input. It does not check a formula — it checks
the property directly:

1. Take the whole scene — for TF-09 that means curves, paper, magazine **and**
   home position; for FL-01 the mesh and the seam guide.
2. Rotate all of it by a random rotation.
3. Re-run the **entire pipeline** from scratch on the rotated copy.
4. Rotate the answer back.
5. Measure how far it moved from the original.
6. Repeat 8 times.

If any stage secretly depended on world Z, the rotated run would produce a
different answer and the drift would be large. Measured drift is ~1e-9 mm,
which is the last digit a double can hold.

TF-09 additionally checks that the **stroke order is byte-identical** — because
an ordering that quietly re-sorted under rotation would still land every target
in the right place while drawing them in a different sequence.

It also round-trips every output plane through the A/B/C conversion (§5),
separately, because that is the one genuinely world-referenced step.

**A FAIL is not always a bug.** A part symmetric about its own long axis has two
equally correct answers, and the tools report the ambiguity rather than hiding
it. During testing a rectangular-section bar produced exactly 180.000000000° of
drift — which was the tools working correctly on a part that has no unique
answer, not a defect. The test part was rebuilt with five unequal lobes and no
symmetry; the symmetric one was kept as a positive check that the ambiguity
stays *reported*.

---

## 9. How the Grasshopper canvases are assembled

Covered in full in `HOW_THESE_WERE_BUILT.md`; the short version:

The `.gh` files are **generated**, by `_build/GhBuild.cs` running inside headless
Rhino. It creates the C# component, injects the three panes by reflection onto
`ScriptSource`, registers all 24 (or 17) inputs with the right type hint, access
mode and optional flag, wires the sliders and the KUKA|prc chain, and writes the
archive.

Then it **reopens both files and solves them**, which is the only way to prove
the pasted C# actually compiles inside a component.

Hand-placing 41 typed inputs is where transcription mistakes live, and the worst
of them are silent — a hint set to `double` where the code expects `int` does
not raise an error, it quietly rounds.

---

## 10. How it is all tested

Everything in the members pane is a plain static class with no Grasshopper UI
dependency, which means it can be compiled and executed outside Grasshopper.

The harness hosts Rhino in-process inside `powershell.exe`
(`Rhino.Runtime.InProcess.RhinoCore`) and calls the classes directly against
real Rhino geometry. It runs there rather than as a compiled `.exe` because
Application Control on this machine blocks freshly built binaries.

**63 automated checks pass** — 26 for TF-09, 12 for FL-01, and an earlier run of
25. Between them they found and fixed four real bugs, the two most serious
being the broken-section bug in §3.4 and the hard-coded `$BASE` in §6.

Two tolerances are floors rather than targets, and assertions are set just above
them:

- Rhino stores mesh vertices as **single-precision floats**, so mesh-derived
  points are good to ~1e-4 mm on a 260 mm part and no better.
- The A/B/C round trip is limited by `asin`/`atan2` to about 1e-9 rad.

An assertion tighter than the floor does not test the code, it tests the
hardware's mantissa.

---

## 11. Where each claim comes from

| Number | Source |
|---|---|
| draw 100 mm/s, air 500 mm/s, lift 30 mm, press 3 mm | `end-effectors/01-drawing/README.md`, "Key parameters" |
| BASE[1] worktable centre, BASE[2] large-format shift | same, "Frames" |
| TOOL[1] pen, TOOL[2] marker, TOOL[3] brush | same, "Capabilities" |
| "mesh in, usable planes out" | `meetings/2026-07-23-paola-wk9-email.html` |
| Agilus KR6-10 R1100-2, 1101 mm reach | `READ_ME_FIRST_Toolpath_Handoff.md` |
| Legacy KUKA\|prc, never PRC Preview | same |
| Wire tool TOOL[4] at ~350 mm, a guess | same |

If the Key parameters table ever changes, the defaults change with it. They live
in **one place** in the code — the `Options` class in `TF09_helpers.cs` — and
are mirrored in `TF09_body.cs` with a comment pointing back.

---

## What is deliberately not implemented

- **The physics-based toolpath direction** from the SIGGRAPH ruled-surfaces
  paper (Steenstrup et al.) that the 23 July email also raises. It is a separate
  line of work that *consumes* FL-01's `Planes` rather than changing it. Flagged
  so it is not mistaken for something missed.
- **Reach and singularity checking.** KUKA|prc's Analysis component does it
  properly; reimplementing it would be worse and would drift.
- **TF-08** — assume vs re-measure TCP per slot. The hook is shaped to take
  either answer, and the loop currently assumes, posting a smartPAD message on
  every swap saying so.
- **PTP for air moves.** Every move is `LIN`. This was suspected as the cause
  of prc's reachability error; splitting air moves onto `PTP` fixed the first
  25 moves and no more, so the real cause was elsewhere (§4.3). `PTP` through
  the air remains the more usual choice and would be slightly faster, but it is
  no longer load-bearing.
- **The turntable.** FL-01 cannot reach a full loop around a part with a fixed
  robot — 25 of 65 planes in one pass need the tool to come from underneath.
  That is what index-then-cut is for, and it needs the real cell geometry.

---

| Related reading | |
|---|---|
| How to actually run it | `RUN_ON_ROBOT.md` |
| How the `.gh` files are generated | `HOW_THESE_WERE_BUILT.md` |
| The build story in plain language | `../04_progress/PROGRESS.md` |
| Test transcript | `../04_progress/TEST_RESULTS.txt` |
| Every TF-09 input in detail | `../02_TF09_pen_switching/TF09_README.md` |
| Every FL-01 input in detail | `../01_FL01_mesh_to_planes/FL01_README.md` |
| **Before moving the robot** | `../02_TF09_pen_switching/krl/PENSWAP_README.md` |
 
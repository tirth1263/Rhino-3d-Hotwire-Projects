# TF-09 — Pen-switching loop (KRL routine + Grasshopper front end)

**Board item:** TF-09 · Evan, Tirth · Class Deliverable / Tooling + Fixtures / D1 Drawing / Software
**Definition of done:** "Park → release → acquire → set TOOL → resume at the right curve index;
safe-abort never strands a pen; T1 dry run passes."

---

## 1. The two halves

| Half | Lives in | Owns |
|---|---|---|
| **Robot side** | `krl/PENSWAP.src` + `krl/PENSWAP.dat` | The actual motion of parking and picking up a pen, the collet, `$TOOL`, and the recovery after an abort. |
| **Grasshopper side** | `TF09_*.cs` | What order to draw in, which way round each stroke goes, *when* a pen has to change, and writing the `.src` that calls the robot side. |

They meet at exactly one routine: **`PEN_ENSURE(n)`**. The drawing job never
says "park" or "acquire". It says *"I need pen n"*, and the robot side figures
out whether that means doing nothing, picking one up, or putting one back
first. Every path through a pen change goes through that one door, which is
what makes the abort case something you can actually reason about.

---

## 2. How each line of the definition of done is met

### "Park → release → acquire → set TOOL"

`PEN_ENSURE(n)` → `PEN_DO_PARK(held)` → `PEN_DO_ACQUIRE(n)` → `PEN_SET_TOOL(n)`.
`PEN_SET_TOOL` writes `$TOOL`, `$LOAD` and `$BASE` from the slot table, so the
controller is holding the right TCP the instant the pen is in the gripper.

### "Resume at the right curve index"

Set `startIndex` in Grasshopper. Two things happen:

- The generated program starts at that stroke and skips everything before it.
- The first thing it does is `PEN_ENSURE(that stroke's pen)`. It does **not**
  assume the right pen is already in the gripper. If a different one is held,
  it parks it first. If the gripper is empty, it fetches one.

The robot also writes `PEN_INDEX` before every stroke, into the persistent DAT
file. So after an abort you can read `PEN_INDEX` off the smartPAD, type that
number into `startIndex`, regenerate, and carry on from the exact stroke that
was interrupted.

### "Safe-abort never strands a pen"

This is the important one, so here is the actual argument.

**The rule:** the collet is only ever commanded to change state while the tool
is sitting at a slot's *seated* pose. Never in mid-air, never while moving,
never at the approach pose. Look at `PEN_COLLET_OPEN` / `PEN_COLLET_CLOSE` —
they are only called from `PEN_DO_PARK` and `PEN_DO_ACQUIRE`, and in both cases
the line immediately before is a move to `PEN_SEAT[S]`.

**The consequence:** at every instant in time, the pen is either fully in its
slot or fully in the gripper. There is no moment in the cycle from which a stop
can drop one on the floor.

**The bookkeeping:** `PEN_PHASE` is written to the persistent DAT immediately
before and immediately after every irreversible step. There are nine phases
(the table is at the top of `PENSWAP.src`). Because the DAT survives a program
reset, a deselect *and* a power cycle, `PEN_INIT()` can read the phase on the
next start and know exactly which half-finished action was in flight.
`PEN_RECOVER()` has a branch for every one of the nine, and every branch ends
with the pen in a slot or in the gripper and the phase back to 0.

Where it cannot tell — phase 7, collet commanded closed, did we get the pen or
not? — it asks the presence sensor. If there is no sensor fitted, it takes the
conservative option (leave the pen in the slot) and says so on the smartPAD
rather than guessing.

**It never guesses.** Every branch that runs out of information calls `HALT`
with a message telling the operator what to check.

### "T1 dry run passes"

The Grasshopper input is called **`liveRun`**, not `dryRun`, and that is
deliberate. An unwired boolean in Grasshopper is `false`. If the input were
called `dryRun`, forgetting to wire it would silently generate a program that
puts a real pen on real paper. Named this way round, forgetting it gives you a
dry run. **The unsafe option has to be asked for.**

With `liveRun` off:

- The generated `.src` sets `PEN_DRYRUN = TRUE`.
- `PEN_COLLET_OPEN` and `PEN_COLLET_CLOSE` return without touching the output,
  but still take the same time, so the rehearsal has the same rhythm as the
  real thing.
- Every drawing stroke is pushed back along the pen's own axis by `hover`, so
  the path is the same shape but never reaches the paper.

The motion is otherwise identical. That is what makes the T1 pass meaningful.

---

## 3. Orientation independence

There is no world Z in the Grasshopper front end. "Up off the paper" is
`drawPlane.ZAxis`, or the real surface normal if you feed a curved sheet into
`drawGeo`. The magazine slots are supplied as full **planes**, not heights.
Press and lift are applied along the pen's own axis, so on a vertical sheet the
3 mm of press goes *into the wall* and not 3 mm downwards.

So: tape the paper to a wall, tilt the table thirty degrees, hang the whole rig
upside down — the job is the same job, just rotated.

Set `selfTest` to `True` to prove it. The component rotates *the entire cell* —
curves, paper, magazine, home position — eight times, re-runs, rotates the
answer back, and checks that (a) the stroke order is byte-identical and (b)
every target lands on the rotated copy of the original.

It also round-trips every output plane through the KUKA A/B/C Euler conversion
and back, because that conversion is the one place in the whole file where a
world axis genuinely appears, so it gets checked separately.

You want `RESULT: PASS`.

---

## 4. Building the Grasshopper component

One Rhino 8 **C# Script** component. Paste `TF09_usings.cs` / `TF09_body.cs` /
`TF09_helpers.cs` into the three panes.

| # | Name | Type hint | Access | Optional | Typical wire |
|---|---|---|---|---|---|
| 0 | `curves` | Curve | **list** | no | Curve param, referenced from Rhino |
| 1 | `penIds` | int | **list** | yes | one number per curve; from layer index or a list |
| 2 | `drawPlane` | Plane | item | no | the plane of the paper |
| 3 | `drawGeo` | Geometry Base | item | yes | only for a curved sheet |
| 4 | `slotPlanes` | Plane | **list** | no | one plane per magazine slot |
| 5 | `slotTools` | int | **list** | yes | KUKA TOOL number per slot |
| 6 | `homePlane` | Plane | item | yes | where the arm starts and ends |
| 7 | `leadIn` | double | item | yes | slider 0–50 |
| 8 | `leadOut` | double | item | yes | slider 0–50 |
| 9 | `hover` | double | item | yes | slider 5–100 |
| 10 | `tiltDeg` | double | item | yes | slider −45…45 |
| 11 | `resolution` | double | item | yes | slider 0.1–5 |
| 12 | `optimize` | bool | item | **no** | Boolean Toggle |
| 13 | `groupByPen` | bool | item | **no** | Boolean Toggle |
| 14 | `startIndex` | int | item | yes | slider 0–N |
| 15 | `liveRun` | bool | item | yes | Boolean Toggle — **leave it off** |
| 16 | `feedDraw` | double | item | yes | slider 5–200 (mm/s) |
| 17 | `feedRapid` | double | item | yes | slider 50–500 (mm/s) |
| 18 | `swapSeconds` | double | item | yes | slider 5–90 |
| 19 | `jobName` | string | item | yes | Panel |
| 20 | `selfTest` | bool | item | yes | Boolean Toggle |
| 21 | `pressDepth` | double | item | yes | slider 0–10 (mm) |
| 22 | `baseIndex` | int | item | yes | slider 1–4, integer |
| 23 | `magBaseIndex` | int | item | yes | slider 1–4, integer |

21–23 are **appended, not inserted**, so a canvas built against the earlier
list keeps working — none of the existing input numbers move.

`optimize` and `groupByPen` are deliberately **not** optional. An unwired
boolean is false, and silently turning off the ordering would leave you
wondering why the travel numbers look bad. Grasshopper will show "no data"
until you wire a toggle, which is the right prompt.

### The defaults are the end-effector spec

Every number below comes from the **Key parameters** table in
`end-effectors/01-drawing/README.md`. Leave the slider unwired and you get the
documented value — you do not have to remember it.

| Input | Unwired default | Where it comes from |
|---|---|---|
| `feedDraw` | 100 mm/s | "Draw speed" |
| `feedRapid` | 500 mm/s | "Air-move speed" |
| `hover` | 30 mm | "Z lift between strokes" |
| `pressDepth` | 3 mm | "Z press offset" |
| `baseIndex` | 1 | BASE[1] worktable centre |
| `magBaseIndex` | 1 | magazine taught in BASE[1] |

If that table in the end-effector README ever changes, change these with it.
They are written in one place in the code — the `Options` class in
`TF09_helpers.cs` — and mirrored in `TF09_body.cs`.

Outputs, in order:

`Targets`, `MoveTypes`, `Flat`, `FlatMoves`, `PenSequence`, `StrokeOrder`,
`OrderedCurves`, `TravelMoves`, `SwapLog`, `SwapCount`, `TravelDist`,
`DrawDist`, `CycleTime`, `KRL`, `Status`, `Log`, `SelfTest`

---

## 5. Draw order — and why it is not just "shortest path"

This also closes board items **D1-01** (flip each curve to start nearest the
previous curve's end) and **D1-03** (lead-in / lead-out), because they are the
same code path.

There are two costs and they are not the same size:

- A pen change is **tens of seconds**.
- A hop between two strokes is **tenths of a second**.

So the ordering is hierarchical and never trades a swap for travel:

1. Group the strokes by pen. Visit the groups nearest-first, so the pen order
   is driven by where the drawing actually is, not by which pen happens to be
   numbered 0.
2. Inside a group, greedy nearest-neighbour — considering **both ends** of
   every remaining stroke, and reversing the curve if its far end is closer.
   That is D1-01 exactly.
3. Run 2-opt over the resulting order to undo the corner greedy always paints
   itself into.
4. One final pass re-checking every curve's direction, because 2-opt can leave
   a flip in a worse state than it found it.

Set `groupByPen` off to see what pure travel optimisation costs you in swaps —
it is a good number to have in the report.

The `Log` prints the before/after air travel and the percentage saved.

### Lead-in / lead-out (D1-03)

Each stroke is: hover above the start along the pen's own axis → straight down
to the first point → draw → straight up. Nothing is inserted between the hover
point and the first point, so there is no sideways creep at the moment the pen
touches. Sideways creep at touch-down is what leaves a witness mark.

Because the retract is along the *pen's* axis and not world Z, this behaves
identically on a tilted or vertical drawing surface.

---

## 5a. Press offset, and what the three heights mean

The holder is spring-loaded, so the tip is commanded **past** the paper and the
spring takes up the difference. Without that, any error in the drawing plane —
a millimetre of paper thickness, a slightly-off touch-up — shows up as a line
that is missing rather than a line that is light.

Along the pen's own axis, measured from the paper surface:

| | Live run | Dry run |
|---|---|---|
| Drawing targets | `pressDepth` **into** the paper (3 mm) | `hover` **above** it (30 mm) |
| Hover / lead points | `hover` + `leadIn` above (35 mm) | `hover` + `leadIn` above (35 mm) |

Two things are worth being explicit about:

- The hover height is measured **from the paper**, not from the pressed point,
  so the clearance is the number in the table whether press is 0 or 3.
- Press moves the tip **only along the pen axis** — never sideways. There is a
  check for exactly that in the test run, because a press that leaked into the
  in-plane geometry would quietly distort every drawing.

Set `pressDepth` from the pen: a technical pen wants very little, a brush wants
more. It is a slider so you can find the number on scrap paper.

---

## 5b. BASE[1], BASE[2] and the magazine

The drawing end-effector README defines two work bases — **BASE[1]** worktable
centre and **BASE[2]** the large-format shift — and the point of the second one
is that paper sizes sharing a base need no recalibration between them.

So the job carries **two** base numbers, not one:

| Input | What it is |
|---|---|
| `baseIndex` | the base the paper is taught in. Every `LIN` in the generated program is in this base. |
| `magBaseIndex` | the base the **magazine** is taught in. |

They are separate because the magazine is bolted to the cell. It does not move
when you switch the paper to BASE[2], and if a swap ran in the paper's base the
slot poses would be dragged through the large-format offset — the tool would
drive at the magazine from the wrong place.

On the robot side, `PEN_GOTO_APPR` / `PEN_GOTO_SEAT` switch to `PEN_MAGBASE`,
and `PEN_SET_TOOL` puts `PEN_BASENO` back. Every change of `$BASE` goes through
`PEN_USE_BASE()`, which forces an advance-run stop first — assigning `$BASE`
while a move is still in the advance run gets that move recomputed in the new
base, which is a genuinely dangerous class of bug and worth the one choke point.

> This was previously wrong: `PEN_SET_TOOL` hard-coded `$BASE = BASE_DATA[1]`,
> so a job set up on BASE[2] silently reverted to the small worktable base after
> the very first pen change. Fixed 2026-08-04.

---

## 5c. Drawing on a curved surface

Wire the scanned sheet — mesh, Brep or surface — into **`drawGeo`** and every
stroke is dropped onto it by closest point before the frames are built. That is
the "curved-surface drawing via scan-projected paths" line in the end-effector
README, and it is what pairs this with `04-3d-scanning`.

You therefore draw the artwork **flat**, in a plane, and let the component put
it on the real surface. The pen axis follows the true surface normal at each
projected point, so it stays square to the material as the sheet curves away.

Leaving `drawGeo` wired costs nothing when the curves already lie on the sheet —
the projection is a no-op to within 1e-14 mm, which is checked. The `Log` prints
the largest distance any point was pulled, and you get a warning if that exceeds
half the lift, because that usually means `drawGeo` is not the sheet those
curves belong to.

Projection is closest-point, which commutes with rotation, so it costs nothing
in orientation independence — the self-test passes with a curved sheet wired.

---

## 5d. Wrist roll runs through the whole job

A pen is round about its own axis, so how it is rolled is free. That freedom is
spent on keeping **axis 6** still: each frame aims its X as close as possible to
the previous frame's rather than chasing the travel direction.

**This chain runs across the entire job, not per stroke.** It is seeded from
`homePlane` when one is supplied, carried from the magazine slot plane across a
swap — because that trip happens between the two strokes and is what the wrist
actually rolls on from — and handed on to the next stroke.

It used to reset at every stroke start, and that was a real fault rather than an
inefficiency. Two strokes drawn in opposite directions asked axis 6 for an
instant **180°** flip between them; over the eight-stroke demo job the program
demanded **1,170°** of total roll change, and KUKA|prc refused it as
unreachable. Every individual target and every consecutive pair was reachable,
so nothing in the geometry looked wrong. The same job now asks for **0°**.

Two things this does *not* change:

- **The drawing.** Roll is rotation about the pen's own axis. Tip positions and
  the pen direction are identical either way.
- **Orientation independence.** Aligning to the previous frame is
  rotation-equivariant, so the self-test still passes at exactly 0.000000000 mm
  drift — flat, on a curved sheet, and with the paper vertical.

Slot planes are never re-rolled themselves. They are taught poses, and their
orientation is a physical fact about the magazine.

If you are reading a generated `.src` and wondering why the `C` angle drifts
smoothly instead of snapping per stroke — that is this.

---

## 6. Getting the program onto the robot

1. Wire the `KRL` output to a **Panel**.
2. Check the header block at the top — it states the stroke count, the swap
   count, the estimated cycle time, and whether dry run is on.
3. Save it. Either right-click the panel → *Stream Contents* to a `.src` file,
   or copy-paste into a text editor. Name it to match `jobName`.
4. Copy `PENSWAP.src`, `PENSWAP.dat` and your generated `.src` to the
   controller.
5. **Read `krl/PENSWAP_README.md` before you run anything.** Commissioning
   order and safety are in there, not here.

---

## 7. Reading the outputs

| Output | What it tells you |
|---|---|
| `Targets` | Tree, one branch per stroke → KUKA|prc, or → the simulator. |
| `MoveTypes` | 0 air, 1 drawing, 2 trip to the magazine. |
| `Flat` / `FlatMoves` | The same thing as one flat list, in program order. This is what the simulator wants. |
| `StrokeOrder` | The original curve indices, in the new order. Sanity-check the optimiser with this. |
| `SwapLog` | Human readable: `stroke 47 : pen 0 -> pen 2`. |
| `SwapCount` | Should equal the number of distinct pens when `groupByPen` is on. If it is higher, your `penIds` do not line up with your curves. |
| `DrawDist` / `TravelDist` | Millimetres on the paper / in the air. |
| `CycleTime` | Seconds, including the swaps at `swapSeconds` each. |
| `KRL` | The program. |
| `SelfTest` | The orientation proof. |

---

## 8. What is not done yet

- **`PEN_CONFIG()` holds placeholder coordinates.** Every slot pose in
  `PENSWAP.src` is a made-up number until the magazine is touched up on the
  real cell. Touch them up with `BASE[PEN_MAGBASE]` selected on the smartPAD,
  or they will be out by the large-format offset.
- **`BASE_DATA[1]` and `BASE_DATA[2]` have to exist and be taught.** The code
  now selects between them, but teaching the worktable centre and the
  large-format shift is a commissioning job, not a Grasshopper one.
- **The slot → TOOL mapping follows the end-effector README** — TOOL[1]
  technical pen, TOOL[2] marker, TOOL[3] brush, TOOL[4] spare — and is written
  in `PEN_CONFIG()`. Slot number and TOOL number are kept separate on purpose:
  re-arranging the magazine must not mean re-measuring a TCP.
- **The TCP per slot is assumed, not measured.** That is board item **TF-08**,
  and `PEN_SET_TOOL` posts a smartPAD message saying so on every swap. When
  TF-08 lands, either the assumption is confirmed or a re-measure step gets
  added to `PEN_DO_ACQUIRE` — the hook is already the right shape for both.
- **`PEN_SENSORS_FITTED` is `FALSE`.** Until the presence and collet sensors
  are wired, the routines trust the commanded state instead of feedback, which
  is fine for supervised T1 and **not** fine for anything unattended.

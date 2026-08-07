# Tirth — SU26 board work (TF-09, FL-01)

Rhino 3D + **native Grasshopper**. C# script components only. **No Python
anywhere**, no third-party plugins beyond the KUKA|prc that is already in the
lab canvas.

Everything here is new and written from scratch. The existing
`RuledWireToolpath_v4/v5/v6` definitions in the parent folder were opened
read-only for reference and **were not modified**.

> **Note on the public copy.** The source documents this work was written
> against — the 23 July meeting email, the live lab kanban and its CSV export —
> are lab-internal and are **not** included in the GitHub copy of this folder.
> They are referred to by name below so the reasoning can still be followed.
> Everything technical is here.

---

## Start here

| If you want to… | Open |
|---|---|
| **Just run it — open a file and simulate the robot** | `05_grasshopper/RUN_ON_ROBOT.md` |
| **Understand how every stage works, step by step** | `05_grasshopper/IMPLEMENTATION.md` |
| **Orientation, reach and the approach switch** | `05_grasshopper/HOTWIRE_ORIENTATION.md` |
| **Set the hotwire TCP, or flip the planes** | `06_hotwire_tool/HW_README.md` |
| Understand what was built and why, in plain words | `04_progress/PROGRESS.md` |
| **See it working** | `04_progress/renders/` (index in its README) |
| **Read the test output** | `04_progress/TEST_RESULTS.txt` |
| Check it actually works, step by step | `04_progress/VERIFICATION.md` |
| See what to connect to what | `04_progress/diagram_*.svg` |
| Understand the orientation requirement | `04_progress/diagram_orientation_proof.svg` |
| Capture the progress screenshots | `04_progress/SCREENSHOT_CHECKLIST.md` |
| Build the mesh → planes tool | `01_FL01_mesh_to_planes/FL01_README.md` |
| Build the pen-switching tool | `02_TF09_pen_switching/TF09_README.md` |
| Put the robot program on the controller | `02_TF09_pen_switching/krl/PENSWAP_README.md` |
| Build the simulator | `03_toolpath_sim/SIM_README.md` |

---

## Layout

```
Tirth Work - 2/
├── README.md                       ← you are here
│
├── 01_FL01_mesh_to_planes/         FL-01 · mesh in, KUKA|prc planes out
│   ├── FL01_usings.cs                → paste into the "usings" pane
│   ├── FL01_body.cs                  → paste into the script body pane
│   ├── FL01_helpers.cs               → paste into the "members" pane
│   └── FL01_README.md                build steps, every input explained
│
├── 02_TF09_pen_switching/          TF-09 · pen-switching loop
│   ├── TF09_usings.cs
│   ├── TF09_body.cs
│   ├── TF09_helpers.cs
│   ├── TF09_README.md
│   └── krl/
│       ├── PENSWAP.src               robot subroutines: park, acquire, recover
│       ├── PENSWAP.dat               persistent state — this is what makes abort safe
│       └── PENSWAP_README.md         commissioning checklist. READ BEFORE MOVING THE ROBOT.
│
├── 03_toolpath_sim/                native playback + timing for either job
│   ├── SIM_usings.cs
│   ├── SIM_body.cs
│   ├── SIM_helpers.cs
│   └── SIM_README.md
│
├── 05_grasshopper/                 READY-TO-OPEN definitions — start here
│   ├── RUN_ON_ROBOT.md               step-by-step, written for a beginner
│   ├── IMPLEMENTATION.md             how every stage works, and why
│   ├── TF09_pen_drawing.gh           open it; demo drawing baked in, prc wired
│   ├── FL01_mesh_to_planes.gh        open it; carries the hotwire end-effector
│   ├── TirthWork_Cell.3dm            the Rhino model — tool, TCPs, foam, demo geo
│   ├── HOW_THESE_WERE_BUILT.md       why they are generated, not hand-placed
│   └── _build/                       the generators + their self-check
│
├── 06_hotwire_tool/                hotwire TCP from KUKA A/B/C + plane flipping
│   ├── HW_usings.cs
│   ├── HW_body.cs
│   ├── HW_helpers.cs
│   └── HW_README.md                  the TCP, the flip toggles, what is not modelled
│
└── 04_progress/
    ├── PROGRESS.md                 step-by-step write-up, plain language
    ├── TEST_RESULTS.txt            transcript of the 63 automated checks
    ├── VERIFICATION.md             what has been checked + the canvas checks
    ├── SCREENSHOT_CHECKLIST.md     numbered captures with captions
    ├── diagram_FL01_canvas.svg     wiring diagrams (drawings, not captures)
    ├── diagram_TF09_canvas.svg
    ├── diagram_orientation_proof.svg
    ├── renders/                    images of the REAL output, with an index
    └── screenshots/                canvas captures - empty until taken
```

---

## Board items covered

| ID | Task | Board's source doc | Covered by |
|---|---|---|---|
| **TF-09** | Pen-switching loop (KRL/PRC routine + GH front end) | `end-effectors/01-drawing/krl/README.md` | `02_TF09_pen_switching/` — front end complete and **checked line by line against that README**, robot side written and awaiting commissioning |
| **FL-01** | Mesh → KUKA|prc planes pipeline | `meetings/2026-07-23-paola-wk9-email.html` | `01_FL01_mesh_to_planes/` — complete, re-verified against the email's wording |
| D1-01 | Draw-order optimise: flip each curve to start nearest the previous curve's END | — | Falls out of TF-09's ordering — same code path |
| D1-03 | Lead-in / lead-out, no witness marks | — | Falls out of TF-09's stroke builder — same code path |
| TF-08 | Assume vs re-measure TCP per slot | — | **Not covered.** The pen loop currently assumes, and posts a smartPAD message on every swap saying so. The hook is shaped to take either answer. |

### What the two source docs asked for, and where it is

`end-effectors/01-drawing/README.md` is the drawing end-effector's own spec, and
TF-09 now implements all of it:

| The README says | Where |
|---|---|
| Draw 100 mm/s · air 500 mm/s · Z press offset 3 mm · Z lift 30 mm | TF-09 input defaults, printed in every generated program's header |
| BASE[1] worktable centre · BASE[2] large-format shift | `baseIndex` / `magBaseIndex`, and `PEN_USE_BASE()` on the robot side |
| TOOL[1] technical pen · TOOL[2] marker · TOOL[3] brush | the slot table in `PEN_CONFIG()` |
| Multi-pen swappable mount | the whole of `PENSWAP.src` |
| Flat paper at multiple sizes, no recalibration within a base | what the BASE split is for |
| Curved-surface drawing via scan-projected paths | `drawGeo` — strokes are projected onto the scanned sheet |

The 23 July email's *Toolpath software* bullet — "takes in a mesh and outputs
KUKA|prc planes directly" — is FL-01, and the four things "usable" has to mean
are each measured. See `01_FL01_mesh_to_planes/FL01_README.md` §0.

**Deliberately out of scope:** the physics-based toolpath direction from the
SIGGRAPH ruled-surfaces paper (Steenstrup et al.) that the same email raises.
It is a separate line of work; it consumes FL-01's `Planes` output rather than
changing it. Flagged here so it is not mistaken for something that was missed.

---

## Build order

**You probably do not need this.** `05_grasshopper/` already contains TF-09 and
FL-01 built, wired to KUKA|prc and ready to open. The order below is for
building a component by hand — worth doing once to understand the pieces, and
the route to take if you are adding a fourth tool.

1. **Simulator first** (`03_toolpath_sim`). It is the smallest component and it
   is the thing you will use to check the other two. Getting it working proves
   the paste-into-three-panes workflow before there is anything complicated to
   debug.
2. **FL-01** (`01_FL01_mesh_to_planes`). Preview `PartFrame` before wiring
   anything else — it is the fastest way to see whether the pipeline understood
   your model.
3. **TF-09** (`02_TF09_pen_switching`). Grasshopper side only. Leave `liveRun`
   off.
4. **The robot side**, following `krl/PENSWAP_README.md` step by step. Do not
   skip the abort test in Step 3 of that checklist — it is the evidence for the
   "safe-abort never strands a pen" line on the board.

---

## The orientation requirement

Both tools work identically whichever way the model or the drawing surface is
turned — flat, vertical, or at any angle in between. This is not a claim;
each tool carries a `selfTest` switch that rotates the input eight times,
re-runs the entire pipeline, rotates the answers back, and prints the measured
difference. You want `RESULT: PASS`.

`04_progress/diagram_orientation_proof.svg` explains the idea in one page.

**One honest caveat**, stated in every relevant README: a model that is
perfectly round about an axis — a sphere, a cube, a plain cylinder — has no
unique "long direction", and no maths can invent one. The tools detect this,
warn, and the self-test reports FAIL. The fix is to supply the axis by hand.

---

## Current state

- All code written and documented. **Compiles clean against Rhino 8's
  RhinoCommon and Grasshopper DLLs — zero errors, zero warnings.**
- **Executed inside Rhino 8.33 headless: 63 automated checks, all pass.**
  Transcript in `04_progress/TEST_RESULTS.txt`, images in
  `04_progress/renders/`. Between them the runs found and fixed four real
  bugs — the most serious being sections silently coming back broken, which put
  a straight 60 mm jump across the middle of a part with no warning, and a
  hard-coded `$BASE` that made BASE[2] stop working after the first pen change.
- **TF-09 has been checked line by line against the drawing end-effector's own
  spec** (`end-effectors/01-drawing/README.md`) — frames, capabilities and the
  key-parameters table. See Step 15 of `PROGRESS.md`. The Z press offset,
  curved-surface projection and the BASE[1]/BASE[2] split all came out of that
  reading; they were missing before it.
- **Assembled on a Grasshopper canvas and wired to KUKA|prc** —
  `05_grasshopper/`. Both files open, solve and simulate against the lab's
  Agilus KR6-10 R1100-2, with demo geometry baked in so they need no Rhino
  model. Generated from the same `.cs` panes rather than hand-placed, and the
  build re-opens and solves them as its own check.
- **Simulating in KUKA|prc found a fifth real bug, now fixed.** TF-09 kept the
  wrist still within a stroke but reset the roll at every stroke start, so two
  strokes drawn in opposite directions demanded an instant 180° flip of axis 6
  — **1,170° of total roll change** over the demo job, which prc rejected as
  unreachable. Every individual target and every consecutive pair was fine, so
  nothing in the geometry looked wrong. The roll now runs through the whole
  job, including across the trip to the magazine: **0°**, and TF-09 simulates
  clean. The drawing is unchanged — roll is rotation about the pen's own axis.
  All 26 checks still pass. `05_grasshopper/RUN_ON_ROBOT.md` §6.
- **FL-01 still reports unreachable poses, and that one is not a bug.** 25 of
  the 65 planes in a pass need the tool to approach from *underneath* the part
  — unavoidable when a slice wraps the whole way round. That is what the
  indexed turntable is for, and it closes when the cell is measured.
- **The hotwire end-effector is now on the arm.** The lab's own Rev2.1 tool
  geometry rides through the FL-01 simulation, with the TCP taken from the
  pendant's CUSTOM TOOL dialog (Z 422, A −90, B −90, C 0). That the frame really
  follows the wire was checked, not assumed: stepping along the TCP's Z by half
  the measured span lands **0.65 mm** from the modelled wire ends. There is a
  Rhino model to go with it, `05_grasshopper/TirthWork_Cell.3dm`.
- **A wire is a line, not a point, and that changed something.** FL-01's planes
  point Z into the material, which is right for a pen and wrong for a wire — fed
  straight in, all 65 targets would drive the wire into the foam end-on. The new
  `06_hotwire_tool` component carries flip/tilt toggles and *reports which of
  the three cases you are in*, so the answer is read off the canvas rather than
  reasoned about. `tiltDeg = 90` is the shipped default and is the cutting one.
- The KRL is written and the safety argument holds, but **no coordinate in it
  has touched hardware** and every slot pose is a placeholder. It is a program
  to be commissioned, not a program to run.
- Screenshots not yet taken — that needs Rhino open on a desktop.
  `04_progress/SCREENSHOT_CHECKLIST.md` lists them, and `05_grasshopper/` is
  now the fastest way to get them: open a file and everything is already there.

Questions on the board items → Evan.

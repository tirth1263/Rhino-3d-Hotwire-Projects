# Running TF-09 and FL-01 in Grasshopper, with KUKA|prc

**Start here if you have never opened these before.** Nothing below assumes you
already know Grasshopper or KUKA|prc.

Two ready-made files live in this folder. You do not have to build anything,
paste any code, or wire any components. Open a file and it works.

| File | What it is |
|---|---|
| `TF09_pen_drawing.gh` | The pen-switching drawing job (board item TF-09) |
| `FL01_mesh_to_planes.gh` | Mesh in, robot planes out (board item FL-01) — **carries the hotwire** |
| `TirthWork_Cell.3dm` | The Rhino model: the hotwire tool, the TCPs, the foam block, the demo geometry |

### About `TirthWork_Cell.3dm`

You do **not** need it to run the `.gh` files — they still stand alone. It is
there so you can see, measure and edit the things the definitions only describe.
Nine layers:

| Layer | What |
|---|---|
| `01_Flange_and_Robot_Base` | flange origin + axis cross, robot footprint |
| `02_Hotwire_Tool_FLANGE_COORDS` | the real Rev2.1 tool, full and reduced |
| `03_Wire` | the wire, 415.8 mm |
| `04_TCP_Frames` | TOOL[4] midpoint, TOOL[5]/[6] wire ends, drawn as axes |
| `05_Foam_Block_BASE3` | EPS block + the BASE[3] corner |
| `06_FL01_Demo_Part` | the same part baked into the `.gh` |
| `07_TF09_Demo_Drawing` / `08_TF09_Paper` | the same drawing, on its paper |
| `09_TF09_Magazine` | the four placeholder pen slots |

The tool sits in **flange coordinates** — flange face at the world origin,
reaching up +Z — because that is the frame KUKA|prc wants for tool geometry.
The cell content (foam, part, drawing) is in world coordinates. Turn layers on
and off rather than expecting them to make sense all at once.

---

## 0. The one idea to understand before you click anything

A robot program is a file called a `.src`. **These two board items produce that
file in two completely different ways**, and mixing them up is the easiest way
to lose an afternoon.

```
FL-01     mesh ──▶ FL-01 ──▶ Planes ──▶ KUKA|prc ──▶ prc writes the .src
                                          ▲
                                   prc IS the program generator


TF-09   curves ──▶ TF-09 ──┬──▶ KRL panel ──▶ THIS is the .src
                           │
                           └──▶ KUKA|prc ──▶ a picture and a reach check only
```

Why the difference: **KUKA|prc does not know the pen magazine exists.** It has
no concept of "park this pen and pick up a different one", so it cannot write
those lines. TF-09 writes its own KRL, swap calls included, and that is what
goes on the controller.

In the TF-09 file, prc answers one question — *can the arm reach these poses* —
and nothing else.

---

## 1. Check your setup

Already verified on this machine, so this is just what "correct" looks like:

| Thing | Expected | Yours |
|---|---|---|
| Rhino | 8.x | 8.33.26188.13001 ✓ |
| KUKA\|prc | installed as a Rhino package | 1.0.9692.38318 ✓ |
| Where it lives | `%APPDATA%\McNeel\Rhinoceros\packages\8.0\kukaprc\` | ✓ |
| Components | a `KUKA\|prc` tab in Grasshopper | 136 components ✓ |
| Robot | Agilus KR6-10 R1100-2, 1101 mm reach | ✓ wired in both files |

**One rule the lab is strict about:** use the components on the **`KUKA|prc`**
tab. There is a similar-looking plugin called **PRC Preview** — do not rewire
anything to it. Also avoid any component whose name begins with `OLD`.

---

## 2. Open the file

Double-click `TF09_pen_drawing.gh`. Rhino starts, Grasshopper opens, the
definition solves by itself.

**You do not need to open a Rhino model.** A demo drawing and a demo part are
baked into the two files so they stand alone. You swap in your own geometry in
section 7.

If Grasshopper reports missing plugins, stop — KUKA|prc did not load and
nothing below will work.

---

## 3. What you are looking at

Read the canvas left to right; it is laid out in the order the data flows.

**Left — the things you change.** Sliders and toggles, each one named. Yellow
boxes explain the ones with a catch.

**Middle — one big component.** That is the whole of TF-09 (or FL-01): a single
C# component with the code inside. You never need to open it. If you are
curious, double-click — the code sits in three panes (usings, main body, helper
maths).

**Right — result panels.** `Status` is the one-line verdict, `Log` the full
story. TF-09 also has a `KRL` panel, which is the actual robot program.

**Far right — the KUKA|prc chain,** in a grey-blue group: simulation slider,
robot, tool, CORE.

### Getting around
- Scroll wheel zooms, middle-drag pans.
- `Ctrl+Shift+E` zooms to fit everything.
- Panels only draw their text above a certain zoom — if one looks blank, zoom in.

---

## 4. Do this first: prove it works in any orientation

This is the claim the whole project rests on and it takes ten seconds.

1. Find the **`selfTest`** toggle on the left.
2. Double-click it so it reads `True`.
3. Wait — it re-runs the entire job eight times.
4. Read the **`SelfTest`** panel on the right.

You want **`RESULT: PASS`**.

What it just did: rotated the whole scene — drawing, paper, magazine, home
position — by eight random rotations, re-ran everything from scratch each time,
rotated the answers back, and measured the drift. It comes out around 1e-9 mm,
the last digit a computer can hold.

**Turn `selfTest` back off afterwards.** It costs eight extra solves every time
anything changes.

---

## 5. Play the simulation

1. Find the **`SIM`** slider in the KUKA|prc group.
2. Drag it slowly from 0 to 1.
3. Watch the arm move in the Rhino viewport.

Cannot see the robot? In Rhino type `Zoom`, then `Extents`.

To record it: right-click `SIM` → **Animate** → pick a folder and a frame
count. Grasshopper writes every frame to disk.

---

## 6. Reading the messages

Components turn **orange** for a warning, **red** for an error. Hover the
balloon to read it. Every message these files can currently show:

| Message | On | Meaning | Care? |
|---|---|---|---|
| `DRY RUN is on…` | TF-09 | The pen is held clear of the paper. | **No — that is correct.** See §8 |
| `Frame-to-frame rotation reaches N deg` | FL-01 | Hard wrist turn between neighbouring targets. | Only if N is large. Raise `samples` |
| `collisions or unreachable positions` | prc CORE | FL-01 only now. See below. | **Yes — read this** |
| `possible singularities…` | prc CORE | Passes near a pose needing huge joint speed. | Note it, confirm on the real cell |
| `Save directory not set…` | prc CORE | You have not told prc where to write. | Only when exporting. §8 |
| `only available to users with a valid license` | Analysis | prc's Analysis is a licensed component. | No — it ships **locked** deliberately |

### TF-09 — this was a real bug, and it is fixed

TF-09 used to show the reachability error. **It no longer does**, and the
canvas comes up clean apart from the dry-run notice.

The cause is worth knowing, because it is invisible in the geometry. Each step
was tested against the virtual KR6-10 R1100-2:

| Test | Result |
|---|---|
| Each of the 669 targets, one at a time | all 669 reachable |
| Each of the 668 consecutive pairs | all 668 reachable |
| First 24 moves as a continuous path | clean |
| First 25 moves as a continuous path | **fails** |
| Adding a leading `PTP` to a safe pose | no change |
| Tilting the pen 5°–40° | no change |

No pose was out of reach and no single step was — the path only failed once
history had accumulated. Measuring the wrist roll explained it:

> The generated program asked for **1,170° of total tool-roll change, with a
> worst single step of 180°** — an instant half-turn of the wrist between two
> targets.

TF-09 already kept the wrist still *within* a stroke: a pen is round about its
own axis, so the roll is free, and it was spent on holding axis 6 steady. But
it **reset that roll at the start of every stroke**, snapping the tool back to
the new travel direction. Two strokes drawn in opposite directions therefore
demanded a 180° flip between them.

The fix carries the roll through the whole job — across strokes, and across the
trip to the magazine — instead of restarting it. Same code path FL-01 already
used. The same job now asks for **0°**.

**Nothing about the drawing changed.** Roll is rotation about the pen's own
axis; the tip positions and the pen direction are identical. Only axis 6 moves
differently. All 26 TF-09 checks still pass, including all three orientation
self-tests at exactly 0.000000000 mm drift.

### FL-01 — this one is not a bug, and not fixable here

FL-01 still shows the error, for an entirely different reason. Of the 65 planes
in one pass:

> **25 are unreachable on their own — and all 25 need the tool to approach the
> part from underneath.**

That is inherent. A slice is a closed loop *all the way around* the part, so
somewhere in every loop the tool has to come at it from below, and a robot
bolted to the floor cannot get there. No amount of moving the part helps: turn
it to reach the bottom and you lose the top.

This is exactly why the workflow is **index-then-cut**: cut the arc you can
reach, rotate the turntable, cut the next. Getting the real robot-base ↔
turntable relationship into the model is task 1 in
`READ_ME_FIRST_Toolpath_Handoff.md` (in the lab's Hotwire project folder,
alongside this one), and it closes when the cell is measured, not in
Grasshopper.

Until then, treat FL-01's CORE error as *"this pass needs indexing"* rather
than *"the toolpath is wrong"*. The planes themselves are verified correct —
orthonormal, on the mesh, tool axis into the material.

**If you do get a reach error on a job you expect to be reachable:** move the
work, not the maths. Drag `paper X` / `paper Y` / `paper Z` and watch CORE. The
virtual robot stands at the world origin, so anything near the origin is
underneath the arm.

---

## 6a. The hotwire, and the flip toggles

The FL-01 file carries the **real hotwire end-effector**: the lab's own Rev2.1
tool mesh, in flange coordinates, wired into prc so the arm carries it through
the whole simulation. The TCP comes from the **HOTWIRE** component, not from a
slider — type the four numbers from the pendant's CUSTOM TOOL dialog and the
simulation matches the cell. They default to the real ones: **Z 422, A −90,
B −90, C 0**.

That puts the tool's **Z axis down the wire**, which was checked rather than
assumed: stepping along it by half the measured span lands 0.65 mm from the
modelled wire ends.

**The flip toggles are on the left of that component.** `flipZ` approaches from
the other side, `flipX` reverses travel, `spinDeg` rolls about the approach, and
**`tiltDeg` rotates about the travel direction** — which is the one that matters
for a wire.

Why: the cutting element is a *line*, not a point, so it matters which way the
line lies. FL-01's planes point Z **into** the material, which is right for a
pen and wrong for a wire — fed straight in, the wire goes into the foam end-on
and melts a pocket instead of cutting. `tiltDeg = 90` lays it across the travel.
**That is why the shipped file has tiltDeg at 90.**

Drag it to 0 and the component goes orange and tells you what is wrong. That is
the loop: toggle, read `Log`, decide. Preview `WireLines` to see where the wire
actually is.

Full detail in `../06_hotwire_tool/HW_README.md`, including what is *not*
modelled — wire sag, kerf, and the usable span as opposed to the modelled one.

---

## 7. Using your own geometry

### TF-09 — your own drawing
1. Draw curves in Rhino, anywhere.
2. Find the `curves` param on the far left of the canvas.
3. Right-click → **Clear values**, then right-click → **Set multiple Curves**,
   and pick them in Rhino.
4. `paper X/Y/Z` moves them into the cell. If your curves are *already* placed
   in the cell, delete the `Move` component and wire `curves` straight into the
   TF-09 component.

For more than one pen, wire a list of numbers into `penIds` — one per curve,
`0` for the first pen, `1` for the second, and so on.

### FL-01 — your own part
1. Right-click the `geo` param → **Clear values** → **Set one Mesh**.
2. Pick your mesh in Rhino.
3. **Look at `PartFrame` in the viewport before anything else.** If the red
   arrow runs down the long direction of your part, the pipeline has understood
   it and everything downstream is trustworthy.

---

## 8. Getting the `.src` file

### From TF-09 — the pen job

The program is already sitting in the `KRL` panel.

1. **Leave `liveRun` off for the first run.** Off means dry run: the pen stays
   one lift height clear of the paper throughout and the clamp never fires.
   Nothing touches anything. This is what you take to the robot first.
2. Click into the `KRL` panel, `Ctrl+A`, `Ctrl+C`.
3. Paste into Notepad, save as `DRAW_JOB.src`.
   *(Or right-click the panel → Stream contents… and pick a file. That keeps it
   updated automatically, which is handy and also easy to forget.)*
4. Copy **three** files to the controller, not one:
   - `DRAW_JOB.src` — the job you just saved
   - `PENSWAP.src` — the pen-swap routines
   - `PENSWAP.dat` — the persistent state that makes an abort safe

   The last two are in `../02_TF09_pen_switching/krl/`.

The job calls `PEN_ENSURE(n)` and lets `PENSWAP.src` decide whether that means
doing nothing, fetching a pen, or parking one first. **Without those two files
the job will not compile on the controller.**

### From FL-01 — the cutting job

Here prc writes the file.

1. Set the **`pass select`** slider. `Planes` is a tree with one branch per
   slice, and the robot cuts one slice per run.
2. Right-click **KUKA|prc CORE** → its settings → set the output directory and
   file name.
3. The `Save directory not set` remark disappears and the `.src` appears.
4. Repeat per pass, naming them so the order survives: `cut_pass0.src`,
   `cut_pass1.src`, …

---

## 9. On the robot

**Do not skip ahead to this.**
`../02_TF09_pen_switching/krl/PENSWAP_README.md` is the commissioning
checklist, written in the order it has to happen:

| Step | What |
|---|---|
| 0 | Before anything — prerequisites |
| 0a | Teach the `BASE` frames |
| 1 | Magazine empty, T1, no pens at all |
| 2 | One dummy pen, T1, still dry |
| **3** | **The abort test** — this is the board deliverable |
| 4 | Sensors |
| 5 | Live collet, still T1 |
| 6 | The actual drawing job |

Step 3 is the evidence for *"safe-abort never strands a pen"* on the board. It
is the one step you cannot claim without having done it.

Everything stays in **T1** — reduced speed, deadman held — until step 6.

---

## 10. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Component **red**, code will not compile | The C# was edited | Undo. Source of truth is the `.cs` files in `01_`/`02_` |
| Nothing in the viewport | Preview off, or camera elsewhere | `Ctrl+Shift+E` in GH; `Zoom Extents` in Rhino |
| `KRL` panel empty | TF-09 did not solve | Read `Status` — it says why in one line |
| Very slow | `selfTest` on, or `samples` high | Turn `selfTest` off |
| Panels show no text | Zoomed out too far | Zoom in |
| Robot invisible in the sim | CORE red, or robot unwired | Check the Agilus component still feeds CORE's `ROBOT` |
| A slider will not reach a value | Sliders have a range | Right-click → Edit → change min/max |
| Want to start over | — | Close without saving, reopen |

---

## 11. What is genuinely not done yet

The difference between a definition that solves and a robot that draws:

- **FL-01 needs the turntable modelled** before a full pass is reachable. §6.
- **Air moves are still `LIN`, not `PTP`.** Once the wrist roll was fixed this
  stopped mattering to prc, so it was left alone. `PTP` through the air is
  still the more usual choice and would be a little faster; it is a change to
  the KRL writer, worth making deliberately if the air moves ever misbehave.
- **No coordinate in the KRL has touched hardware.** Every magazine slot pose
  is a placeholder. The four slots in a straight row exist so the swap logic
  can be watched working, not because the magazine looks like that.
- **The `BASE` frames are not taught.** `baseIndex` and `magBaseIndex` name
  BASE[1] and BASE[2]; teaching them is Step 0a of the checklist.
- **Both tool planes are guesses** — 150 mm off the flange for the pen, 350 mm
  for the wire, each labelled as such on the canvas. Until they are the measured
  TCP, prc's reach check is indicative only.
- **`pressDepth` needs tuning on scrap.** 3 mm is the end-effector README's
  number; the right value depends on the pen and the spring.
- **TF-08 is a separate board item** — assume a known TCP per slot, or
  re-measure after each swap. The hook takes either answer.

---

## Where everything else lives

| You want | Read |
|---|---|
| **How every stage actually works, step by step** | `IMPLEMENTATION.md` |
| **Orientation, reach and the approach switch** | `HOTWIRE_ORIENTATION.md` |
| **The hotwire TCP and the flip toggles** | `../06_hotwire_tool/HW_README.md` |
| What was built and why, in plain words | `../04_progress/PROGRESS.md` |
| The test transcript | `../04_progress/TEST_RESULTS.txt` |
| Everything TF-09 can do | `../02_TF09_pen_switching/TF09_README.md` |
| Everything FL-01 can do | `../01_FL01_mesh_to_planes/FL01_README.md` |
| **Before moving the robot** | `../02_TF09_pen_switching/krl/PENSWAP_README.md` |
| How these two `.gh` files were generated | `HOW_THESE_WERE_BUILT.md` |

Questions on the board items → Evan.

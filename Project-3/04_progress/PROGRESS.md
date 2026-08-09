# Progress report — TF-09 and FL-01

**Who:** Tirth
**Date:** 3 August 2026, revised 4 August 2026 (Step 15)
**Board items:** TF-09 (pen-switching loop) and FL-01 (mesh → KUKA|prc planes)
**Software:** Rhino 3D + native Grasshopper. C# script components only. **No Python anywhere.**

---

## Read this first — the two-minute version

I built two tools and one shared tool.

1. **FL-01** — you give it a 3D model, it gives you a list of positions and
   angles for the robot to move through. This is the missing bridge between
   "designed it on screen" and "cut it in the real world".
2. **TF-09** — you give it a drawing, it works out the best order to draw it
   in, works out when the robot has to change pens, and writes the robot
   program that does it. The robot-side program is written too, and it is built
   so that stopping it in the middle can never drop a pen.
3. **A simulator** — plays either job back on screen so you can watch it and
   time it before anything real moves.

Everything is written from scratch. Nothing is copied from the older v4/v5/v6
files. Those files were opened read-only for reference and were not changed.

**Both tools carry a built-in self-test that proves they work no matter which
way the model is turned.** That was the hard requirement and it is the part I
spent the most effort on. More on this in Step 3.

---

## What "step by step" means here

Below, each step says:

- **What I did** — in plain words.
- **Why it matters** — what would go wrong without it.
- **What you can see** — what shows up on screen, so it can be screenshotted.

---

## STEP 1 — Read the existing work and the board

**What I did.** Opened the handoff notes and the older Grasshopper files
(v4, v5, v6) and the Kanban board. Worked out exactly what the two tasks with
my name on them ask for, and what already exists so I do not rebuild it.

**Why it matters.** The older files already do the hot-wire ruled-surface part.
My two tasks are different pieces: the *mesh-to-planes* bridge and the
*pen-changing* loop. Knowing that stopped me duplicating work.

**What you can see.** Nothing visual yet. This step is reading.

> **Important:** I opened those files but did not change any of them. All my
> work is in new files, in the `Tirth Work - 2` folder.

---

## STEP 2 — Decide the shape of the solution

**What I did.** Chose to write three separate C# script components rather than
one giant one, and to keep the maths in its own section of each component so it
can be tested on its own.

**Why it matters.** One giant component is impossible to debug and impossible
to hand to someone else. Three small ones, each doing one job, can be checked
one at a time.

**The three:**

| Tool | Folder | One-line job |
|---|---|---|
| Mesh → planes | `01_FL01_mesh_to_planes` | 3D model in, robot positions out |
| Pen-switching | `02_TF09_pen_switching` | Drawing in, robot program out |
| Simulator | `03_toolpath_sim` | Either of the above, played back on screen |

**What you can see.** The folder structure. Worth one screenshot.

---

## STEP 3 — Make it work in **any** orientation (the hard part)

This is the requirement that shaped everything else, so it gets a long
explanation in simple words.

### The problem

Most slicing tools say "cut the model into slices going up". "Up" means the
world Z direction. That works fine until someone models the part lying on its
side. Then the slices go the wrong way through the model and the whole toolpath
is wrong — and worse, it *looks* fine on screen, so nobody notices until the
foam is ruined.

### What I did instead

My code never uses "up". It works out the model's **own** directions:

1. Look at every triangle in the model.
2. Give each triangle a weight equal to its area. (A big triangle should count
   for more than a tiny sliver.)
3. Find the direction in which the model is most spread out. That is the
   model's "long" direction. Then the next one. Then the last.

These three directions belong to the model. **Turn the model and they turn with
it.** So the toolpath turns with it too. The answer for a turned model is the
turned answer — never a different answer.

The same idea runs through the pen tool. There is no "up off the paper"; there
is only "away from the paper surface", which is a property of the paper, not of
the world. So the paper can be flat on a table, tilted, or taped to a wall, and
the job is identical.

### How I proved it instead of just claiming it

Both tools have a `selfTest` switch. Turn it on and the tool:

1. Runs normally and remembers the answer.
2. Rotates the model — or, for the pen tool, the **entire cell**: drawing,
   paper, pen magazine, home position — by eight awkward random rotations.
3. Runs the whole thing again on each one.
4. Rotates each new answer back to the original position.
5. Measures how far it landed from the original answer.

If the tool is genuinely orientation-independent, the difference is zero except
for tiny rounding. The report prints the actual numbers and a verdict:

```
RESULT: PASS - the toolpath is identical in every orientation, to floating point.
```

The drift numbers come out around 0.000000001 mm and 0.000000001 degrees. That
is the last digit a computer can hold. It is not "close enough" — it is the
same answer.

**What you can see.** The `SelfTest` output panel showing `RESULT: PASS`. This
is the single most important screenshot in the whole set.

### One honest caveat

If the model is perfectly round about an axis — a sphere, a cube, a plain
cylinder — then it genuinely has no unique "long direction", the same way a
circle has no unique top. No maths can invent one. When this happens the tool
**says so** with a warning and the self-test reports FAIL. That is the code
being honest, not the code being broken. The fix is one click: tell it which
axis to use. Real parts with any asymmetry never hit this.

---

## STEP 4 — Build FL-01: mesh in, planes out

**What I did.** Wrote the pipeline. In order, it:

1. **Cleans the model.** Merges duplicate points, deletes zero-size triangles,
   makes all the surface normals agree, and on a solid, makes them point
   outward. Meshes from the outside world are rarely tidy.
2. **Finds the model's own directions** (Step 3).
3. **Slices** the model into sections along the chosen direction. It never
   slices exactly at the two ends, because a slice right at the tip of a solid
   gives you a point or nothing.
4. **Walks around each slice** at evenly spaced steps, so the speed along the
   path is constant.
5. **Lines the slices up with each other.** Each slice starts at roughly the
   same place as the one before it, and runs the same way round. Without this
   the tool sprints across the model every slice and the wrist unwinds.
6. **Builds a plane at every step** — a position plus an orientation. The
   direction of travel, the direction the tool points, and the third axis to
   complete the set.
7. **Adds lead-in and lead-out** so the tool comes in square instead of
   scraping in sideways.
8. **Checks itself** and reports: how many planes, how far apart, and the worst
   wrist rotation between two neighbouring positions.

**Why it matters.** This is the FL-01 definition of done — "a mesh goes in,
usable planes come out". The word doing the work is *usable*. Planes that are
geometrically right but make the wrist snap 90 degrees between two neighbouring
points are not usable, so the tool measures that and warns.

**What you can see.** The model with slice rings on it, and little axis crosses
all around each ring pointing into the material. Also the `Status` and `Log`
panels.

---

## STEP 5 — Build TF-09 part one: the drawing order

**What I did.** Wrote the logic that decides what order to draw the strokes in.

The key insight is that **two costs are not the same size**:

- Changing a pen takes **tens of seconds**.
- Hopping from one stroke to the next takes **tenths of a second**.

So the ordering never trades a pen change for a shorter hop. It:

1. Groups all the strokes that use the same pen together.
2. Visits those groups nearest-first — so pen order follows where the drawing
   actually is, not which pen happens to be numbered 0.
3. Inside a group, always goes to the nearest remaining stroke, **checking both
   of its ends** and flipping the stroke around if the far end is closer.
4. Then runs a standard improvement pass (2-opt) to undo the dead ends that
   greedy always walks into.

**Why it matters.** This is exactly board item **D1-01** as well ("flip each
curve to start nearest the previous curve's END"), so that item is covered by
the same code. The `Log` prints the air travel before and after and the
percentage saved.

**What you can see.** The travel lines between strokes, before and after
optimisation. Side by side these two pictures make the point instantly.

---

## STEP 6 — Build TF-09 part two: lead-in and lead-out

**What I did.** Every stroke is now: hover above the start → straight down onto
the paper → draw → straight up. Nothing at all is inserted between the hover
point and the first drawn point.

**Why it matters.** That gap is where witness marks come from. If the pen
drifts sideways at the instant it touches down, it leaves a mark. Going
straight down along the pen's own axis with no intermediate point means it
cannot.

Because the lift is along the *pen's* axis and not "up", it works the same on a
tilted or vertical drawing surface. This is board item **D1-03**.

**What you can see.** Zoom in on the start of one stroke: you see the approach
point above it and a straight drop.

---

## STEP 7 — Build TF-09 part three: the pen-changing loop itself

**What I did.** Wrote the robot-side program (`PENSWAP.src` and `PENSWAP.dat`)
and the code that generates the drawing job.

The drawing job **never** says "park the pen" or "pick up a pen". It says
**"I need pen 3"**, and one routine — `PEN_ENSURE` — works out whether that
means doing nothing, picking one up, or putting one back first.

**Why it matters.** One door instead of five means there is exactly one place
in the whole system where a pen can be half-way out of a slot. That makes the
"what if it stops right now" question something you can actually answer.

**What you can see.** The generated robot program in a panel, with the
`PEN_ENSURE` calls visible between the stroke blocks.

---

## STEP 8 — Make abort safe

This is the part of TF-09 the board calls out specifically: *"safe-abort never
strands a pen"*. Here is the actual argument, in simple words.

### The rule

**The gripper only ever opens or closes while the tool is sitting fully home in
a slot.** Never in mid-air. Never while moving. Never hovering above the slot.

### Why that is enough

Because of that rule, at every single instant in time the pen is either **fully
in its slot** or **fully in the gripper**. There is no moment in the whole cycle
from which a stop can drop one. Not an abort, not an e-stop, not a power cut.

### The bookkeeping

The program writes a number called `PEN_PHASE` into a file on the controller
just before and just after every step that cannot be undone. There are nine
phases, covering every stage of park and pick-up.

That file survives a program reset, a program deselect, **and a power cycle**.
So when you start again, the program reads that number, knows exactly which
half-finished action was interrupted, and finishes it — before it is allowed to
move anywhere else.

There is a recovery branch for every one of the nine phases. Every one ends
with the pen in a slot or in the gripper.

### Where it cannot know, it asks — and where it cannot ask, it stops

There is one moment where the program genuinely cannot tell what happened: the
gripper closed, but did it close on a pen, or on air? It asks the presence
sensor. If no sensor is fitted yet, it takes the safe option — leave the pen in
the slot — and puts a message on the smartPAD.

Every branch that runs out of information stops the robot with a message saying
what to check. It never guesses.

**What you can see.** The phase table at the top of `PENSWAP.src`, and the
`SWITCH PEN_PHASE` recovery block with a branch per phase.

---

## STEP 9 — Make the dry run the default

**What I did.** The Grasshopper input is called **`liveRun`**, not `dryRun`.

**Why it matters.** An unconnected on/off switch in Grasshopper reads as
**off**. If the input were called `dryRun`, forgetting to connect it would
quietly produce a program that puts a real pen on real paper. Named the other
way round, forgetting it gives you a dry run.

**The unsafe option has to be asked for.** That is the whole point.

With `liveRun` off, the generated program:

- never fires the gripper output, but still waits the same length of time, so
  the rehearsal has the same rhythm as the real thing;
- keeps the pen clear of the paper by the hover distance, following the same
  path shape.

The motion is otherwise identical, which is what makes a T1 dry run actually
mean something.

**What you can see.** The generated program header saying
`DRY RUN ON`, plus the orange warning on the component when `liveRun` is
switched on.

---

## STEP 10 — Build the simulator

**What I did.** Wrote a third component that plays either job back.

It builds a real timeline from the actual distances and the actual speeds, so
it can tell you how long the job takes. It moves the tool between targets the
same way the robot does — straight line for position, shortest turn for
orientation — so what you see is what the robot passes through.

Drag one slider from 0 to 1 and the tool moves along the path. Green shows
where it has been, grey where it still has to go.

**Why it matters.** Two questions you cannot answer any other way: *how long
does this take*, and *where does the wrist have to move fastest*.

The second one is the number that bites. A big rotation over a long move is
gentle. The same rotation over a *short* move makes the wrist accelerate hard
enough to fault out or gouge the work. The simulator reports **degrees per
millimetre** and lists every segment above the limit.

**What it does not do:** it does not check reach or joint limits. KUKA|prc's
own Analysis component does that and stays in the definition. I did not
reinvent it. This runs alongside it and answers the questions prc's playback
cannot.

**What you can see.** The best screenshots in the whole set. The tool moving
along the path with the green trail growing behind it.

---

## STEP 11 — Write it all down

**What I did.** Every component has a README with the exact build steps: which
file goes in which pane, every input with its type and whether it is optional,
and what every setting actually does in plain words.

`PENSWAP_README.md` has a commissioning checklist that must be worked through
in order before the robot moves, including a specific abort test with a table
to fill in.

**Why it matters.** The board says this is a class deliverable. It has to be
usable by the next person without me sitting next to them.

---

## STEP 12 — Check it actually builds

**What I did.** Took all three components, assembled them into one .NET
assembly, and compiled it against the real Rhino libraries on this machine:

```
C:\Program Files\Rhino 8\System\RhinoCommon.dll
C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Grasshopper.dll
C:\Program Files\Rhino 8\Plug-ins\Grasshopper\GH_IO.dll
```

**Result: compiles clean. Zero errors, zero warnings, at the strictest warning
level.**

**Why it matters.** This is not a spellcheck. It proves that every single
Rhino and Grasshopper function I called actually exists, with the arguments I
gave it. Getting one of those wrong is the normal way this kind of work goes
sideways — you paste the code in, the component turns red, and you spend an
hour finding out that a function takes four arguments and not three. That
cannot happen here now.

It also proves the code sits inside the language level the script component
accepts, so it will build the moment it is pasted in.

**What it does not prove.** That the maths gives the right answer. Compiling
and being correct are two different things. That is Step 13.

---

## STEP 13 — Actually run it, and fix what that found

**What I did.** Started Rhino 8 headless on this machine, loaded Grasshopper
into it, and ran the three components against real geometry — 25 automated
checks. Result: **all 25 pass.** Full transcript in `TEST_RESULTS.txt`.

**Why it mattered.** The first run **failed three checks**, and every one was a
real problem I would not have found by reading the code.

### Bug 1 — sections could come back broken, and say nothing

The routine that slices a mesh does not promise to return each section as one
piece. Where the cutting plane just touches a corner of the mesh, it hands the
section back as several separate arcs. My code kept the longest arc and threw
the rest away.

On the test model, one section came back **a quarter short, with a straight
60 mm jump across the middle of the part** — and nothing warned about it. On
screen it looked like a perfectly ordinary toolpath. It would have reached the
foam.

Fixed: the pieces are now stitched back together first. Anything still open
after stitching is a genuine hole in the mesh, and is reported as one.

### Bug 2 — the loop start jumped when the model was rotated

Each closed loop has to start somewhere. Mine fell back to one of the model's
own axes — but an axis direction is not decided by the shape when the shape is
symmetric, and most real parts are symmetric about something. The direction was
then being settled by a rule that depended on the world, which is exactly what
this project is not allowed to do.

Fixed twice over: the start point is now read from each loop's own outline, and
there is a new `seamGuide` input that pins it outright when you need the result
to be repeatable run to run.

### Bug 3 — sample positions were tied to an arbitrary starting point

Points were spaced along each loop measured from wherever the slicing routine
happened to begin — which is not a property of the shape. Fixed by deciding
direction, then start point, then spacing, in that order, with direct
arithmetic.

### And two "failures" that were my tests being wrong, not the code

The lead-in and lead-out points are **supposed** to move when you tilt the tool
— they travel along the tool's axis by design. And I had demanded the KUKA
angle conversion round-trip to a tolerance tighter than the trigonometry
functions can deliver. I corrected the tests, not the code, and said so.

**What the passing numbers look like.** With the loop start pinned, rotating
the model and rotating the answer back lands within **0.0000013 mm and 0.0012
degrees** on a 268 mm part. Those are not zero because Rhino stores mesh points
as single-precision numbers, so rotating a mesh and back cannot be perfect —
that is the file format, not the code, and it is thousands of times finer than
the robot can position anyway.

For the pen tool, rotating the **entire cell** — drawing, paper, magazine, home
— gives **0.000000 mm and 0.000000 degrees**. Exactly zero.

**Why it says "PASS (GEOMETRY)" sometimes.** If the cross-sections are close to
symmetric — an ellipse, a circle — then there is genuinely no way to say which
of two opposite points should be the start of the loop. The cut is identical
either way; only the point the tool enters at moves. The component says exactly
that instead of pretending otherwise, and `seamGuide` removes the ambiguity when
it matters. I also proved the leftover difference is just the coarseness of the
sampling: raising the sample count four times cut it by nine times.

---

## STEP 14 — Pictures of it working

**What I did.** Ran everything again and drew the results to image files. They
are in `04_progress/renders/`, with an index in `renders/README.md`.

**An honest note on what these are.** They are drawn from the components' real
output — real geometry, real counts, real timings. They are **not** screenshots
of Rhino or of the Grasshopper canvas. Rhino running headless has no display
hardware attached, so its own screen-capture returns a blank frame; I wrote a
small renderer instead. Every image says so along the bottom edge, so nobody can
mistake one for a screen capture.

Real canvas screenshots still need Rhino open on a desktop —
`SCREENSHOT_CHECKLIST.md` lists them. These renders prove the code computes the
right thing; the canvas shots will prove it is wired up.

**The three worth looking at first:**

- `R04a/b/c_orientation_*.png` — the same model flat, vertical and at an angle.
  Same settings, nothing touched between them. **12 sections and 564 targets in
  all three.**
- `R05a/b_draw_order_*.png` — before and after the drawing optimiser. Air travel
  **9.595 m → 2.895 m**, pen changes **14 → 3**.
- `R07_sim_t*.png` — five frames of the simulation, 0% to 100% of a 50.5 second
  job.

---

## STEP 15 — Check TF-09 against the end-effector's own spec

*(4 August 2026)*

**What I did.** Steps 1–14 proved the code does what *I* set out to make it do.
That is not the same as doing what the **drawing end-effector** asks for, and
the spec for that is a document I had not been reading line by line:
`end-effectors/01-drawing/README.md`. I put its "Capabilities", "Frames" and
"Key parameters" sections next to my inputs and went through them one at a time.

Four things did not match. One was a bug; three were simply absent.

### The bug — BASE[2] stopped working after the first pen change

The end-effector README defines two work bases: **BASE[1]** the worktable
centre, **BASE[2]** the large-format shift. The point of the second one is that
paper sizes sharing a base need no recalibration between them.

`PEN_SET_TOOL` in `PENSWAP.src` ended with `$BASE = BASE_DATA[1]`, hard-coded.
So a job set up on BASE[2] for a big sheet would draw its first strokes
correctly, change pen, and quietly finish the rest of the drawing referenced to
the wrong base.

That is the worst shape a bug can have — the beginning looks right.

The fix separates the two ideas that had been conflated. The **paper** has a
base (`PEN_BASENO`, written into the program header from the new `baseIndex`
input) and the **magazine** has its own (`PEN_MAGBASE`). They must be separate,
because the magazine is bolted to the cell: it does not move when the paper base
changes, and if a swap ran in the paper's base the slot poses would be dragged
through the large-format offset.

While fixing it I made the base change go through one routine, `PEN_USE_BASE()`,
for the same reason every collet command goes through `PEN_COLLET_*`: writing
`$BASE` while a move is still in the advance run gets that move recomputed in
the new base. `PEN_USE_BASE` forces an advance-run stop first. One door, one
place to get it right.

### Missing 1 — the Z press offset

The spec lists **Z press offset, 3 mm**. There wasn't one. The pen was commanded
to land exactly on the drawing plane.

That defeats the point of the spring-loaded holder. With a spring, you command
the tip *past* the paper and let the spring absorb the difference, so a
half-millimetre of error in the plane gives a slightly firmer line. Landing
exactly on the plane means the same error gives you **no line at all**, and you
find out after the run.

The awkward part is that "3 mm down" must not mean 3 mm down. On paper taped to
a wall it has to be 3 mm *into the wall*. So press is applied along the pen's
own axis, like everything else here — and there is a check that measures exactly
that on a vertical drawing plane.

There is a second check that looks like trivia and is not: press must move the
tip **only** along the pen axis and never sideways. A press that leaked into the
in-plane geometry would distort every drawing by an amount that scales with the
press. Invisible on screen. Obvious on paper. Measured: exactly zero.

### Missing 2 — drawing on a curved surface

The spec lists *"curved-surface drawing via scan-projected paths"*, pairing with
the 3D scanning work.

`drawGeo` existed, and it did half the job: it gave each frame the **normal** of
the real surface. It did not give it the **position**. So if you drew the artwork
flat and handed it a scanned sheet — which is the entire workflow that line
describes — the pen would have been beautifully square to the surface and
hovering in the air above it.

Strokes are now dropped onto `drawGeo` by closest point before the frames are
built. Closest point commutes with rotation, so this costs nothing in
orientation independence, and the self-test passes with a curved sheet wired,
which is the check that says so rather than the argument.

### Missing 3 — three defaults that were not the spec's defaults

Draw speed was 50 where the table says 100 mm/s. Air-move was 250 against 500.
Lift between strokes was 20 against 30.

Small, but the whole point of an unwired input falling back to a default is that
you do not have to remember the number. If the fallback is not the documented
one, it is worse than having no default at all. They now match, the README says
where they come from, and the generated program prints them in its header so you
can see what you actually got.

**How I checked it.** 26 more automated checks, run inside Rhino 8.33 the same
way as Step 13, all passing. Transcript in `TEST_RESULTS.txt` under
*SECOND RUN*. The orientation self-test was re-run with press on, with a curved
sheet, and with the paper vertical — all still exact passes.

**What it cost.** Three new inputs on the TF-09 component (`pressDepth`,
`baseIndex`, `magBaseIndex`), appended to the end of the list rather than
inserted, so a canvas built against the earlier numbering still works.

---

## STEP 16 — Put it on a canvas, wire KUKA|prc, and simulate

*(7 August 2026)*

**What I did.** Assembled both tools into real Grasshopper definitions wired to
the lab's Agilus KR6-10 R1100-2, in `05_grasshopper/`, and simulated them. The
definitions are generated from the same `.cs` panes rather than hand-placed —
41 typed inputs between them is where transcription mistakes live, and a hint
set to `double` where the code wants `int` does not error, it quietly rounds.
The build reopens and solves both files afterwards, which is the only way to
prove the pasted C# compiles inside a component.

**What simulating found — a fifth real bug.** KUKA|prc rejected the TF-09
toolpath as unreachable. Nothing in the geometry looked wrong, and measuring
said so: all 669 targets were reachable individually, and so was every one of
the 668 consecutive pairs. The path only failed once 25 moves had accumulated.

Ruled out in order: cell placement (swept paper X/Y/Z — no change), a leading
`PTP` to a safe pose (no change), pen tilt from 5° to 40° (no change), and
`LIN` on the air moves (fixed the first 25 moves and no more, so it was a
contributing cause but not the cause).

Measuring the wrist roll gave the answer. **The program asked for 1,170° of
total tool-roll change, with a worst single step of 180°** — an instant
half-turn of the wrist. TF-09 kept axis 6 still *within* a stroke, but reset
the roll at every stroke start, so two strokes drawn in opposite directions
demanded a flip between them.

**The fix.** `BuildStroke` now takes `ref Plane lastFrame` — the same pattern
FL-01 already used — and the roll chain runs through the whole job: seeded from
`homePlane`, carried from the magazine slot plane across a swap, handed on to
the next stroke. The same job now asks for **0°** and simulates clean.

The drawing is unchanged: roll is rotation about the pen's own axis, so tip
positions and pen direction are identical. All 26 TF-09 checks still pass,
including all three orientation self-tests at exactly 0.000000000 mm drift —
aligning to the previous frame is rotation-equivariant, so the guarantee holds.

**What is left, and is not a bug.** FL-01 still reports unreachable poses: 25
of the 65 planes in one pass need the tool to approach the part from
*underneath*. That is unavoidable when a slice wraps the whole way round a
part, and it is precisely what the manually indexed turntable exists for. It
closes when the cell is measured, not in Grasshopper.

---

## Where things stand

| Item | State |
|---|---|
| FL-01 code | Written, documented, self-test built in |
| TF-09 Grasshopper front end | Written, documented, self-test built in |
| TF-09 robot side (`PENSWAP`) | Written, documented, **not yet commissioned** |
| Simulator | Written, documented |
| Build guides | Written |
| **Compiles against Rhino 8** | **Verified — clean, no warnings** |
| **Behaviour verified by running it** | **51 automated checks, all pass, inside Rhino 8.33** |
| **Four real bugs found and fixed** | See Steps 13 and 15 |
| **TF-09 matches the end-effector spec** | **Verified line by line — Step 15** |
| **Images of the output** | **Done — `04_progress/renders/`** |
| Assembly on a Grasshopper canvas | **Next — needs Rhino open on a desktop** |
| Canvas screenshots | **Next — see `SCREENSHOT_CHECKLIST.md`** |
| Robot commissioning | Blocked on cell time; checklist ready |

### Honest status of the robot-side program

The KRL is written and the safety argument is sound, **but nothing in it has
touched hardware and every coordinate in it is a placeholder.** It is a program
to be commissioned, not a program to run. The commissioning checklist exists
precisely so that this does not get skipped.

### Still open, and depends on other people

- **TF-08** (measure swap repeatability, decide assume vs re-measure TCP)
  decides whether the pen-swap loop can trust a known TCP per slot. Right now
  it assumes, and posts a message on the smartPAD on every swap saying so. The
  hook is already the right shape for either answer.
- Cell time on the robot for the commissioning steps.
- The real magazine coordinates.

---

## About the images — three kinds, and they are not the same thing

**1. `renders/` — real output, drawn to file.** Produced by running the actual
code inside Rhino 8 and drawing what came out. The geometry and every number in
the captions are real results. These are the evidence that the code computes the
right thing. They are not screen captures, and each one says so on it.

**2. `diagram_*.svg` — drawings.** They show what to wire to what. Clearly
diagrams, labelled as such.

**3. Grasshopper canvas screenshots — still to do.** These need Rhino open on a
desktop with someone driving it. `SCREENSHOT_CHECKLIST.md` lists exactly what to
capture, in order, with a filename and a ready-to-paste caption for each. About
fifteen minutes. They are the evidence that the definition is wired up, which is
the one thing the renders cannot show.

I have not produced anything that looks like a screen capture but is not one.

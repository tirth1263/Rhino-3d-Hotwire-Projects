# FL-01 — Mesh → KUKA|prc planes pipeline

**Board item:** FL-01 · *Mesh -> KUKA|prc planes pipeline* · Evan, Tirth · D2 Hotwire / Full Loop + Toolpath SW
**Definition of done:** "A mesh goes in, usable planes come out — the bridge from create-in-3D to cut-in-the-real-world."
**Reference:** the 23 July check-in email (`meetings/2026-07-23-paola-wk9-email.html`),
*Toolpath software* bullet.

---

## 0. Status against the email

The email states the deliverable as *"software that takes in a **mesh** and
outputs **KUKA|prc planes** directly — the bridge from 'create in 3D' to 'cut in
the real world' in the Full Loop."* That is what this is, and it is done.

Re-checked on 2026-08-04 on a part it had not seen before — 12 checks, all
passing, transcript in `04_progress/TEST_RESULTS.txt` under *FL-01 RE-CHECK*.
"Usable" was checked as four separate things, because it is the word doing all
the work in that sentence:

| "Usable" means | Measured |
|---|---|
| Every plane valid and **orthonormal** — prc reads A/B/C off the frame, and a skewed frame gives a wrong orientation rather than an error | worst non-orthogonality 2.2e-16 |
| Every origin lies **on the mesh** | worst gap 1.3e-6 mm on a 260 mm part |
| Every tool axis points **into the material** — probed 2 mm along +Z and asked the mesh | 396 in, 0 out |
| Tree structure matches the slices, so `Tree Branch` selects one cut | 12 branches for 12 sections |

**Not in this component, and not claimed by it:** the physics-based toolpath
direction from the SIGGRAPH ruled-surfaces paper (Steenstrup et al.) that the
same email raises. That is a separate line of work with its own definition of
done; FL-01 is the mesh → planes bridge and stops there. The two meet at this
component's output, so that work can consume `Planes` without FL-01 changing.

---

## 1. What this does, in one paragraph

You give it a mesh. It works out which way the model is "long", slices it into
sections along that direction, walks around each section, and puts a **tool
plane** at every step — a plane being a position plus an orientation, which is
exactly what KUKA|prc's LIN component eats. Out the other end come ordered
planes, grouped one branch per slice, plus every number you need to judge
whether the path is sane before anything moves.

---

## 2. Why it works no matter how the model is turned

This is the part that took the most care, so it is worth being precise about.

A naive slicer says "slice along Z". That works until somebody models the part
lying on its side, and then the slices come out along the wrong direction and
the toolpath is nonsense. Ours never mentions Z.

Instead it computes the model's **own** axes:

1. Take every triangle in the mesh.
2. Weight each one by its area — a big triangle should count for more than a
   sliver.
3. Find the direction in which the model is most spread out. That is the long
   axis. Then the next one, then the last. These three are the model's
   principal axes.
4. Slice along whichever of those the user asked for.

Those axes are welded to the model. Rotate the model and they rotate with it,
by exactly the same rotation. So the toolpath rotates with it too, and the
result is the same toolpath in a new position — never a different toolpath.

**The proof is built in.** Set `selfTest` to `True`. The component rotates the
mesh eight times by awkward random rotations, runs the entire pipeline again on
each, rotates the answers back, and measures how far they land from the
original. There are three possible verdicts.

### `RESULT: PASS`

Every target lands on the rotated copy of the original, to floating point.
Verified on 2026-08-03 in Rhino 8.33: **origin drift 1.3e-6 mm, rotation drift
0.0012°** on a 268 mm part. You get this when `seamGuide` is supplied.

### `RESULT: PASS (GEOMETRY) — SEAM NOT CANONICAL`

Every loop is the same loop, in the same place, cut the same way round. What
moved is only **where along each loop the tool starts**.

This is not a bug and it cannot be engineered away. A smooth closed loop has no
natural starting point — there is no corner to call the beginning. Left to
itself the component picks one from the loop's own outline, which is stable when
the section has a clear long direction and becomes a coin toss when the section
is close to symmetric. An ellipse looks identical after a half turn, so two
candidate start points are genuinely indistinguishable.

The residual the report prints in this case is the **chord error** between two
polygons drawn through the same loop at different phases. It is discretisation,
and it shrinks as you raise `samples`. Measured: **1.2543 at 28 samples →
0.1392 at 112 samples**, a 9× reduction for a 4× increase in samples.

**If the start point matters, supply `seamGuide`** — a point anywhere near the
model. Every loop then starts at the point nearest to it and the whole result
becomes exactly reproducible. Do that when you are re-cutting a part to match an
earlier run.

### `RESULT: FAIL`

The path itself differs. Two causes, both reported by name:

- **Slice-axis reversal.** The model is symmetric end-for-end, so the shape
  cannot say which end comes first. Both orders cut the same geometry but
  enumerate the slices in reverse. Pin it with `axisMode = 5`.
- **Ambiguous principal axes.** The model is round about an axis — a sphere, a
  cube, a plain cylinder — so it has no unique long direction, the same way a
  circle has no unique "top". The component warns before the self-test even
  runs. Again, `axisMode = 5`.

### Why the tolerances are not zero

Rhino stores mesh vertices as **single-precision floats**. Rotating a mesh and
rotating it back cannot be bit-exact: every vertex moves by about 1e-7 of the
model size, and normals derived from those vertices wobble by roughly a
thousandth of a degree. That is a property of the mesh format, not of this code,
and it is four orders of magnitude below anything a KR6 can resolve.

---

## 3. Building the component

You need **one** Rhino 8 C# Script component. Native Grasshopper, no plugins,
no Python.

1. Drop a **C# Script** component on the canvas (Maths → Script → C# Script).
2. Double-click it to open the script editor.
3. Paste the three files into the three panes:

   | File | Pane |
   |---|---|
   | `FL01_usings.cs` | the top pane, `using` statements |
   | `FL01_body.cs` | the middle pane, the script body |
   | `FL01_helpers.cs` | the bottom pane, "Members" / "Additional code" |

   Do **not** wrap the body in a class or a `RunScript(...)` — the component
   builds those around it.

4. Add the inputs. Zoom in on the component and use the `+` on the left edge.
   For each one: right-click the parameter → rename it, set **Type hint**, set
   **Access**, and tick **Optional** where the table says so.

   | # | Name | Type hint | Access | Optional | Typical wire |
   |---|---|---|---|---|---|
   | 0 | `geo` | Mesh | item | no | Mesh param, referenced from Rhino |
   | 1 | `axisMode` | int | item | yes | Number Slider 0–5, integer |
   | 2 | `customAxis` | Vector3d | item | yes | leave empty unless axisMode = 5 |
   | 3 | `sections` | int | item | yes | Number Slider 1–200, integer |
   | 4 | `samples` | int | item | yes | Number Slider 8–400, integer |
   | 5 | `loopMode` | int | item | yes | Number Slider 0–1, integer |
   | 6 | `normalMode` | int | item | yes | Number Slider 0–1, integer |
   | 7 | `flipApproach` | bool | item | yes | Boolean Toggle |
   | 8 | `tiltDeg` | double | item | yes | Number Slider −90 … 90 |
   | 9 | `rollMode` | int | item | yes | Number Slider 0–1, integer |
   | 10 | `leadLen` | double | item | yes | Number Slider 0–100 |
   | 11 | `minSpacing` | double | item | yes | Number Slider 0–20 |
   | 12 | `maxTurnDeg` | double | item | yes | Number Slider 5–90 |
   | 13 | `seamGuide` | Point3d | **list** | yes | a Point, or leave empty |
   | 14 | `fromFrame` | Plane | item | yes | leave empty at first |
   | 15 | `toFrame` | Plane | item | yes | leave empty at first |
   | 16 | `selfTest` | bool | item | yes | Boolean Toggle |

   `seamGuide` is **list** access on purpose. An unwired `Point3d` **item**
   arrives as (0,0,0), which is a perfectly valid point — the component could
   not tell "not supplied" from "the origin" and would silently pin every loop
   to world zero. An unwired list arrives empty, which is unambiguous.

5. Add the outputs, in this order. Outputs have no type hint — just names.

   `Planes`, `Points`, `MoveTypes`, `Sections`, `SlicePlanes`, `PartFrame`,
   `SliceAxis`, `Count`, `MaxTurn`, `Status`, `Log`, `SelfTest`

6. The component should compile with no errors the moment `geo` has a mesh.

---

## 4. First run — do it in this order

Do not wire KUKA|prc yet. Get the geometry right first.

1. `sections` = 12, `samples` = 32, everything else at zero / false.
2. Wire `PartFrame` to a **Plane** preview. **Look at it.** The red X arrow
   should run down the long direction of your model. If it does, the whole
   thing is working.
3. Wire `Sections` to a Custom Preview. You should see clean rings.
4. Wire `Planes` to a Custom Preview or just let the wire preview draw them.
   Every plane's blue Z arrow should point **into** the material. If they all
   point outward, tick `flipApproach`.
5. Read `Status`. It is one line and it tells you the truth.
6. Read `Log`. It tells you the long story, including which slices came back
   empty and why.
7. Turn `selfTest` on once. Read `SelfTest`. Turn it back off — it costs eight
   extra full solves, so leave it off during normal work.

---

## 5. Wiring it into KUKA|prc

`Planes` is a tree, one branch per slice. KUKA|prc wants one continuous list of
targets per program.

```
FL-01 [Planes] ──▶ Tree Branch ──▶ LINear Movement - KUKA|prc ──▶ KUKA|prc CORE
                        ▲
              Number Slider "Slice Select"
```

Use **legacy KUKA|prc** components (category `KUKA|prc`), not the "PRC Preview"
ones — the canvas has both installed and they look almost identical.

Set the Core's Custom Tool to the measured wire TCP, not a guess.

---

## 6. What every input actually does

| Input | Plain English |
|---|---|
| `axisMode` | Which way to slice. **0** = along the model's longest direction (usual). **1** = along its thinnest direction (flat contour slices). **2/3/4** = world X/Y/Z — these tie the result to the world, so a rotated model gives a different answer, and the component warns you. **5** = you supply the vector. |
| `sections` | How many slices. More slices = finer cut, more targets, longer cycle. |
| `samples` | How many points around each slice. Too few and corners get cut off; too many and the file is enormous. 32 is a good start. |
| `loopMode` | **0** keeps only the biggest ring in each slice. **1** keeps every ring — needed for a model that splits into two legs partway down. |
| `normalMode` | Where the tool aims from. **0** uses the real mesh normal (accurate). **1** aims straight out from the middle of the slice (bulletproof on messy meshes). |
| `flipApproach` | The tool is aiming the wrong way. One click. |
| `tiltDeg` | Leans the tool sideways about its direction of travel. Use it to buy clearance. |
| `rollMode` | **0** locks the tool's X to the travel direction — correct for a hot wire, where the wire has to lie a particular way. **1** lets the tool spin freely about its own axis and picks whatever spin moves the wrist least — correct for a pen or a router bit, wrong for a wire. |
| `leadLen` | How far to back off before and after each loop, so the tool arrives square instead of scraping in sideways. |
| `minSpacing` | Throws away points closer together than this. Cleans up dense meshes. |
| `maxTurnDeg` | Above this much wrist rotation between two neighbouring targets, you get a warning. The wrist will visibly snap there. |
| `seamGuide` | Where closed loops start. Empty = worked out from each loop's own outline. Supply a point to pin it and make the result exactly reproducible. |
| `fromFrame` / `toFrame` | Pick the job up from where the model is and put it down where the robot is. Wire both or neither. |
| `selfTest` | Runs the orientation proof. Slow. Use it when you change the code, not every solve. |

---

## 7. Reading the outputs

| Output | What to do with it |
|---|---|
| `Planes` | The deliverable. Tree, one branch per slice. → KUKA|prc. |
| `MoveTypes` | 0 = the tool is in the air, 1 = the tool is working. Feed this to the simulator so air moves run at rapid speed. |
| `PartFrame` | **Preview this every single time.** It is the fastest way to see whether the pipeline understood your model. |
| `MaxTurn` | Worst wrist rotation between two neighbouring targets, in degrees. |
| `Status` | One line. If it starts with `OK`, you are fine. |
| `Log` | Everything: mesh stats, chosen axes, slice count, spacing, warnings. |
| `SelfTest` | The orientation proof. Empty unless you asked for it. |

---

## 8. Known limits — stated plainly

- **A round part has no long axis.** Covered above. Warned about, detected by
  the self-test, fixed with `axisMode = 5`.
- **A smooth closed loop has no natural start point.** Covered above. Fixed
  with `seamGuide` when it matters.
- **Sections can arrive in pieces.** `Intersection.MeshPlane` does not promise
  one polyline per loop: where the plane grazes a vertex it hands a section back
  as several open arcs. The component stitches them before use. It found this on
  a test model — one section came back 25% short with a straight 60 mm jump
  across the middle of the part, and it was silent. Anything still open after
  stitching is a real hole in the mesh, and is reported.
- **Reach and singularities are not checked here.** This component makes
  geometrically correct planes. Whether the KR6/KR10 can actually get to them
  is KUKA|prc's Analysis component's job, and it stays in the definition.
- **`normalMode = 0` needs a mesh whose normals agree.** The component unifies
  them and, on a closed solid, forces them outward. On an open shell it cannot,
  so it warns and you check the arrows.
- **The self-test is skipped, not failed, when you pin the axis to the world.**
  `axisMode` 2/3/4/5 are deliberately world-locked; testing them for
  orientation independence would be testing the wrong thing.

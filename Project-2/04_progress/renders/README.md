# Renders

**What these are:** images drawn from the components' **real output data**, produced
by running the actual code inside Rhino 8 (8.33) headless on 3 August 2026.
The geometry, the counts and the numbers in every caption are real results.

**What these are not:** screenshots of Rhino or of the Grasshopper canvas.
Headless Rhino has no display pipeline — no GPU context — so `ViewCaptureToFile`
and `RhinoView.CaptureToBitmap` either throw or return a blank frame. Rather
than ship nothing, the geometry is drawn through a small perspective renderer
written for the purpose. Every image says so along the bottom edge.

Real Grasshopper canvas screenshots still have to be taken on a machine with
Rhino open — see `../SCREENSHOT_CHECKLIST.md`. These renders are the evidence
that the code produces correct results; the canvas shots are the evidence that
it is wired up.

---

## FL-01 — mesh to KUKA|prc planes

| File | Shows |
|---|---|
| `R01_fl01_part_axes.png` | The model's own axes, from area-weighted PCA of its triangles. Red = the direction it is longest in. No world X/Y/Z is read. |
| `R02_fl01_sections.png` | 12 sections along that axis, each stitched closed before use. |
| `R03_fl01_target_planes.png` | The deliverable — 564 target planes in 12 branches. Red = travel direction, blue = tool axis into the material. Worst wrist turn 29.2°. |

## FL-01 — the orientation test

Same model, same settings, nothing adjusted between the three.

| File | Pose |
|---|---|
| `R04a_orientation_flat.png` | lying flat |
| `R04b_orientation_vertical.png` | stood vertical |
| `R04c_orientation_tilted.png` | arbitrary angle |

All three: **12 sections, 564 targets.** The slices follow the model, not the
world.

## TF-09 — draw order

| File | Result |
|---|---|
| `R05a_draw_order_before.png` | Supplied order. Air travel **9.595 m**, 14 pen changes. |
| `R05b_draw_order_after.png` | Optimised. Air travel **2.895 m**, **3** pen changes. |

**69.8% of the air travel removed, and pen changes down from 14 to 3** — the
minimum possible for three pens. The long grey lines are the trips to the pen
magazine, which sits off to one side of the paper.

## TF-09 — vertical drawing surface

| File | Surface |
|---|---|
| `R06a_paper_flat.png` | paper flat on the table |
| `R06b_paper_vertical.png` | the same drawing on a wall |

Both: 73 targets, 3 pen changes. Blue = the pen axis at each target; it stays
square to the paper either way, because "off the paper" is the paper's own
normal and not world Z.

## Simulator

Five frames off the real timeline — built from the real distances and the real
commanded feed rates. Total cycle **50.5 s**.

| File | t | Elapsed |
|---|---|---|
| `R07_sim_t000.png` | 0.00 | 0.0 s |
| `R07_sim_t025.png` | 0.25 | 12.6 s |
| `R07_sim_t050.png` | 0.50 | 25.3 s |
| `R07_sim_t075.png` | 0.75 | 37.9 s |
| `R07_sim_t100.png` | 1.00 | 50.5 s |

Green = path already run. Grey = still to go. Red = the tool.

---

## If you only show five

`R04b` (vertical model) · `R05b` (order optimised) · `R03` (target planes) ·
`R07_sim_t050` (simulator mid-run) · `R06b` (drawing on a wall).

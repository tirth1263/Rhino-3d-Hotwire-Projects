# Screenshot checklist

Work through this with Rhino and Grasshopper open. About 20 minutes.
Save every image into `04_progress/screenshots/` using the filename given.

Captions are written ready to paste under each image in an email or slide.

**How to capture**
- Grasshopper canvas: `Ctrl+Shift+S` in Grasshopper, or just a normal
  screen capture of the canvas area.
- Rhino viewport: type `ViewCaptureToFile` in the Rhino command line. Set the
  scale to 2 for a crisp image.
- Animate the simulator: right-click the `t` slider → **Animate** → choose the
  screenshots folder and a frame count. Grasshopper writes every frame to disk
  by itself.

---

## Part A — setting up (4 shots)

### `01_folder_structure.png`
Windows Explorer showing the `Tirth Work - 2` folder with all four subfolders
open one level.

> Caption: Project layout. Three Grasshopper tools, one progress folder.
> All new files — the existing v4/v5/v6 definitions were not modified.

### `02_component_panes.png`
The Rhino 8 C# script editor open on the FL-01 component, showing the three
panes with code in them.

> Caption: Each tool is one native Grasshopper C# component. Usings, script
> body and helper maths go into the three panes. No Python, no plugins.

### `03_fl01_inputs.png`
The FL-01 component zoomed in on the canvas, all 16 inputs visible with their
names, sliders and toggles wired.

> Caption: FL-01 inputs. Every automatic setting is derived from the model
> itself; the world-locked options are marked and warn when used.

### `04_tf09_inputs.png`
Same for the TF-09 component — all 24 inputs, including `pressDepth`,
`baseIndex` and `magBaseIndex` at the end of the list.

> Caption: TF-09 inputs. Note `liveRun` rather than `dryRun` — an unwired
> toggle reads as off, so forgetting it gives a dry run, not a live one.

---

## Part B — FL-01 working (5 shots)

### `05_fl01_partframe.png`
Rhino viewport. The mesh, with the `PartFrame` output previewed on it.

> Caption: The model's own axes, worked out from the shape itself. The red
> arrow runs down the model's longest direction. This is what makes the
> pipeline independent of how the model is oriented.

### `06_fl01_sections.png`
Rhino viewport. The mesh with the `Sections` output previewed — the slice rings.

> Caption: Slices taken along the model's own long axis, not along world Z.

### `07_fl01_planes.png`
Rhino viewport, zoomed in on a few slices, with `Planes` previewed so the axis
crosses are visible.

> Caption: One tool plane per step around each slice. The blue axis points
> into the material — this is the approach direction the robot will use.

### `08_fl01_status_log.png`
Two Grasshopper panels: `Status` and `Log`.

> Caption: The tool reports on itself: plane count, spacing, and the worst
> wrist rotation between neighbouring targets.

### `09_fl01_selftest_PASS.png` ⭐ **most important shot in the set**
The `SelfTest` panel with `selfTest` toggled on, showing `RESULT: PASS`.

> Caption: The orientation proof. The tool rotates the model eight times by
> random rotations, re-runs the entire pipeline on each, rotates the answers
> back, and measures the difference. Drift is ~1e-9 mm — the last digit a
> computer can hold. The toolpath is the same in every orientation.

---

## Part C — orientation demonstrated visually (3 shots)

This is the set that makes the point to someone who will not read the numbers.

### `10_orientation_flat.png`
Rhino viewport. The model lying flat, with slices and planes previewed.

> Caption: Model lying flat.

### `11_orientation_vertical.png`
**Same model, rotated to stand vertically** (`Rotate3D` in Rhino), same
Grasshopper settings, same view distance.

> Caption: The same model stood upright. The slices follow the model, not the
> world. Identical toolpath, just rotated with the part.

### `12_orientation_tilted.png`
Same model rotated to some awkward angle — not 90°, something like 37°.

> Caption: And at an arbitrary angle. No settings changed between these three.

---

## Part D — TF-09 working (5 shots)

### `13_tf09_order_before.png`
`TravelMoves` previewed with `optimize` **off**.

> Caption: Draw order before optimisation. Every line is the pen travelling
> through the air, doing no useful work.

### `14_tf09_order_after.png`
Same view, `optimize` **on**. Also capture the `Log` panel line showing the
before/after travel distance and percentage saved.

> Caption: After optimisation. Strokes are grouped by pen, ordered
> nearest-first, and each one flipped to start at whichever end is closer.
> Board item D1-01.

### `15_tf09_leadin_detail.png`
Zoomed right in on the start of one stroke, showing the hover point above and
the straight drop onto the paper. Set `liveRun` on for this one shot so the
press offset is visible, and turn it straight back off.

> Caption: Lead-in. Straight down along the pen's own axis, with no
> intermediate point, so there is no sideways creep at touch-down — which is
> what leaves a witness mark. Board item D1-03.

### `15a_tf09_press_offset.png`
The same stroke start, sectioned or viewed edge-on against the `drawPlane`, so
the 3 mm of press below the paper and the 30 mm lift above it are both readable.

> Caption: Z press offset, 3 mm below the paper, and the 30 mm lift above it —
> the drawing end-effector README's numbers. The holder is spring-loaded, so
> commanding the tip past the surface is what makes a half-millimetre of
> calibration error give a firmer line instead of no line.

### `16_tf09_swaplog.png`
The `SwapLog` and `SwapCount` panels.

> Caption: Where the pen has to change, and how many times. With grouping on,
> the swap count equals the number of pens — the minimum possible.

### `17_tf09_krl_output.png`
The `KRL` panel, scrolled so the header block, the `$BASE = BASE_DATA[n]` line
and a `PEN_ENSURE` call between two stroke blocks are all visible.

> Caption: The generated robot program. The job never says "park" or
> "acquire" — it says which pen it needs, and one routine works out the rest.
> Header confirms dry run is on and states the speeds, lift, press and both
> base numbers, so what you got is readable without opening Grasshopper.

---

## Part E — drawing surfaces that are not a flat table (4 shots)

### `18_tf09_paper_flat.png`
`OrderedCurves` and `Targets` previewed with `drawPlane` flat on the table.

> Caption: Drawing on a flat table.

### `19_tf09_paper_vertical.png`
**Same drawing, `drawPlane` rotated to vertical** — paper on a wall. Nothing
else changed.

> Caption: The same drawing on a vertical surface. "Away from the paper" is a
> property of the paper, not of the world, so the whole job — including the
> lead-ins and the trips to the magazine — just rotates with it.

### `19a_tf09_curved_sheet.png`
The same flat drawing again, with a **curved surface wired to `drawGeo`**.
Preview `OrderedCurves` (still flat) and `Targets` (on the surface) together,
viewed from an angle where the curvature is obvious.

> Caption: Curved-surface drawing. The artwork is drawn flat; the scanned sheet
> goes into `drawGeo` and the strokes are projected onto it. This is the
> "scan-projected paths" line in the drawing end-effector README, and the pair
> with the 3D scanning work.

### `19b_tf09_curved_normals.png`
Zoomed in on a few targets on the curved sheet, plane display on, so the tool
axes are visible fanning with the surface.

> Caption: The pen stays square to the material as the sheet curves away — the
> axis is the real surface normal at each projected point, not the plane's.
> Wire `Log` to a panel for this shot: it prints how far the projection pulled.

---

## Part F — the simulator (4 shots, or an animation)

### `20_sim_start.png`
`t` = 0.0.

> Caption: Simulation at the start. Grey is the path still to run.

### `21_sim_mid.png`
`t` ≈ 0.45.

> Caption: Part-way through. Green is the path already run, the cone is the
> tool. The panel reads elapsed time, current move type and commanded feed.

### `22_sim_end.png`
`t` = 1.0.

> Caption: Job complete. Estimated cycle time including pen changes.

### `23_sim_hotspots.png`
The `Log` and `HotSpots` panels.

> Caption: The number that matters — degrees of wrist rotation per millimetre
> of travel. A big turn over a long move is gentle; the same turn over a short
> move is what makes the wrist fault or gouge. Every segment over the limit is
> listed by number.

### Optional: `sim_animation/` folder
Right-click the `t` slider → Animate → 60 frames into
`screenshots/sim_animation/`. Makes a much better demo than any still.

---

## Part G — the robot side (2 shots)

### `24_krl_phase_table.png`
`PENSWAP.src` open in a text editor, showing the phase table at the top.

> Caption: Nine phases covering every stage of a pen change. The number is
> written to a file that survives a power cycle, so after any stop the robot
> knows exactly which half-finished action to complete.

### `25_krl_recovery.png`
The `SWITCH PEN_PHASE` block in `PEN_RECOVER`.

> Caption: A recovery branch for every phase. Each one ends with the pen
> either in its slot or in the gripper. Where the program cannot tell which,
> it asks the sensor; where it cannot ask, it stops and says what to check.

---

## Checklist

- [ ] 01–04 setup
- [ ] 05–09 FL-01 working
- [ ] 10–12 orientation demonstrated
- [ ] 13–17 TF-09 working
- [ ] 18–19 vertical drawing surface
- [ ] 20–23 simulator
- [ ] 24–25 robot side
- [ ] Optional animation

**If you only have time for five:**
`09` (self-test PASS), `11` (vertical model), `14` (order optimised),
`21` (simulator mid-run), `17` (generated robot program).

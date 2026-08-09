# TF-09: standing the board up, and the pen lean that made it work

**What this covers:** the same treatment FL-01 got, applied to the pen job —
the tool on the arm, the work standing vertical, the Z facing the robot — and
the one thing that turned out to be different.

Everything below was **measured against KUKA|prc**, not reasoned out. The
sweeps are reproducible: `powershell -File _build\verify_tf09.ps1`.

Companion to [`HOTWIRE_ORIENTATION.md`](HOTWIRE_ORIENTATION.md). Read that one
first if you have not — several numbers here are the pen's version of a hotwire
number, and the comparison is the point.

---

## 1. The short version

Three things were asked for and all three are done:

| Asked | Done |
|---|---|
| the object **vertical** | the sheet stands on an easel at 900 / 0 / 450 |
| the **pen holder on the arm**, correct dimensions and orientation | Pen_Tool Rev 008, rebuilt in flange coordinates, nib 227.8 mm out |
| the **Z axis facing the robot** | the board's blue Z runs back at the arm, and `zToRobot` keeps it there |

And one thing that was not asked for, because nobody knew it was there:

> **The job was unreachable with every target well inside the reach ring.**
> Standing the sheet up square-on aims the pen back down the arm's own reach
> line, the wrist goes flat, and the pose is singular. Leaning the pen 20°
> fixes it. Nothing moves; the pen just leans.

---

## 2. The tool

### From the CUSTOM TOOL dialog

| Field | Value |
|---|---|
| Tool X / Y | 0 / 0 |
| **Tool Z** | **227.8** |
| **Tool A / B / C** | **0 / 0 / 0** |

All three angles zero. That is the whole difference from the hotwire's
`−90 / −90 / 0`: a crossbar needs describing, a pen does not.

### Dimensions, measured off `Pen_Tool Rev 008.3dm`

| Part | Size |
|---|---|
| Mount plate | 100 × 100 × 15 |
| Spring body | 62.5 sq × 50 |
| Pen carriage | 25 sq × 69 |
| Spring | ⌀15.5, 20 mm travel |
| Pen barrel | ⌀10.5 × 121.4 |
| **Nib** | **227.8 mm from the flange face** |

The stack is 15 + 50 + 69 = 134 mm of structure, and the pen's top end lands at
227.8 − 121.4 = 106.4 — inside the carriage, which spans 65 to 134. Two
independently measured numbers agreeing is what says the stack is real.

> Rev 008 is a **workshop layout**, not a flange-referenced model — 1.95 m
> across, with 2D drawings and a test rig in it. So the tool is **rebuilt from
> its dimensions**, unlike the hotwire which could be lifted whole. Where the
> flange face sits is therefore a **definition**, not a measurement. See
> `../07_pen_tool/PN_README.md` §2.

---

## 3. Which Z is which

The thing that trips everyone up:

| | Direction |
|---|---|
| the **BOARD's** Z | out of the sheet, **back at the robot** |
| the **TARGET's** Z | into the sheet, **down the pen** |

Opposite by definition, and they have to be. The pen reaches 227.8 mm ahead of
the wrist, so the flange sits **between the robot and the paper**, or the arm
cannot reach past its own tool.

So "the blue Z faces the robot" is true of the **sheet** — which is the thing
you are looking at in Rhino — and cannot be true of the pen.

This is the same constraint that made `zToRobot` refuse on the hotwire, showing
up in a form where it *can* be satisfied. There, the wire lay on tool Z, so Z
and the cutting element were the same axis and it could not also face the
robot. Here the pen and the sheet are different objects, so the sheet is free
to face wherever it likes.

---

## 4. The pen lean — the expensive one

### What it looked like

- KUKA|prc: *the toolpaths contains collisions or unreachable positions*
- Every one of 669 targets inside the ring: flange **681 to 832 mm** against a
  460–1101 ring
- Board nearer, further, higher, sideways: no change
- Magazine moved anywhere in the cell: no change
- Tool mesh cleared entirely: no change
- **Board lying flat: solved instantly**

### What it was

Not where the tool is. Which way it points.

A sheet standing square-on to the robot has its normal pointing back down the
arm's reach line. A pen perpendicular to that paper is aimed at the shoulder.
The wrist has to go flat, axis 4 lines up with axis 6, and the pose is singular
no matter how much room there is around it.

### Measured

Board at 900 / 0 / 450, `boardOrient 0`:

| penLeanDeg | KUKA\|prc |
|---|---|
| 0, 5, 10, 12 | **UNREACHABLE** |
| **15, 20, 25, 30** | **runs** |
| 35, 40 | **UNREACHABLE** — too far the other way |
| −20 | runs |

Ten is not enough, thirty-five is too much, and **20 is shipped**. It is also
what a hand does — nobody draws with the pen dead perpendicular.

`penLeanDeg` lives on the PENTOOL component and feeds TF-09's `tiltDeg`
directly; TF-09 has no slider of its own for it any more. One component owns
the orientation story and can check its own advice. Drag it to 0 on the canvas
and watch prc go red — the switch proving the point rather than this document
asking you to take it on trust.

---

## 5. The board switch

### `boardOrient`

| Value | Meaning | prc at lean 20 |
|---|---|---|
| **0 VERTICAL** | on an easel, facing the robot — **shipped** | **runs** |
| 1 FLAT | on a table, normal at the ceiling | unreachable |
| 2 TILTED | drafting table, `leanDeg` 0 = FLAT, 90 = VERTICAL | unreachable |
| 3 AWAY | turned away — kept so you can see wrong | runs |

1 and 2 want a different lean. **The board attitude and the pen lean are one
question, not two**, which is why they are on the same component. Change
`boardOrient` and expect to re-tune `penLeanDeg`.

### `cardinal` — AUTO follows the board

Same rule as the hotwire's: drop the vertical, snap to whichever of ±X / ±Y
points from the robot to the work.

| Board | AUTO picks | Sheet Z faces the robot | prc |
|---|---|---|---|
| 900 / 0 in front | +X | 1.00 | runs |
| 0 / 900 left | +Y | 1.00 | runs¹ |
| 0 / −900 right | −Y | 1.00 | runs¹ |
| −900 / 0 behind | −X | 1.00 | blind spot |

¹ **once the magazine is brought round too.** See §7 — this cost an hour and is
the least obvious thing in the file.

### The reach ring

Board straight in front, sheet 280 × 210:

| board X | prc |
|---|---|
| 500, 600 | **UNREACHABLE** — too close |
| **700 … 1200** | **runs** |
| 1300 | **UNREACHABLE** — too far |

Same shape as the hotwire's ring and for the same reason, but the band sits
about 194 mm further in, because the pen reaches 227.8 mm ahead of the wrist
where the hotwire reaches 422.

> **That 194 mm is the number to remember when swapping tools.** The ring is a
> property of the robot; where the *work* can go is a property of the robot
> **and** the tool length.

---

## 6. Numbers to carry forward

| | Value |
|---|---|
| Pen TCP (nib off the flange) | **227.8 mm** |
| Tool A / B / C | **0 / 0 / 0** |
| Pen lean | **20°** (band 15–30) |
| Board | 280 × 210 at **900 / 0 / 450**, vertical |
| Board normal | **−X**, back at the robot |
| Usable board distance | **700 … 1200 mm** |
| Flange ring | 460 … 1101 mm |
| Blind spot astern | 175–185° |
| Job | 669 targets, 3 pen swaps, **1 min 44 s** |
| Feeds (end-effector README) | draw 100 mm/s, air 500 mm/s, lift 30 mm, press 3 mm |

---

## 7. The magazine does not move with the board

`cardinal AUTO` turns the **sheet**. It does not turn the **magazine**, and it
should not — a magazine is bolted to the cell.

But it does mean that swinging the board round with the sliders leaves the swap
trips reaching across the cell. Measured:

| | prc |
|---|---|
| board left at 0 / 900, slots left at 380 / −420 | **UNREACHABLE** |
| board left at 0 / 900, slots at **420 / 380** | **runs**, same as the front |

420 / 380 is the exact 90° image of 380 / −420: rotate `(x, y)` to `(−y, x)`.
The canvas note by the magazine chain says the same thing.

This was worth chasing rather than shrugging at, because the symptom — "the
switch works in front and not to the side" — reads like the switch being
broken, and it is not. Nothing about the board was wrong.

---

## 8. The files

| File | What |
|---|---|
| `TF09_pen_drawing.gh` | the definition, pen tool wired in, board switch on the canvas |
| `TirthWork_TF09_Cell.3dm` | the Rhino model — pen tool in flange coords, TCP frame, board upright with the artwork on it, flange positions |
| `TirthWork_Cell.3dm` | the shared cell; its TF-09 sheet now stands up too |
| `../07_pen_tool/` | the PENTOOL component's three panes + its README |
| `renders_tf09/` | nine renders, one per step |
| `_build/verify_tf09.ps1` | the sweeps that produced every table above |

### About `renders_tf09/`

These are **renders, not screen captures.** Rhino runs headless in this
workflow, so there is no window to photograph — every image is drawn from the
geometry the definition actually computed, and the verdict in the corner is
read off KUKA|prc rather than typed in.

| Render | Shows |
|---|---|
| R01 flat | `boardOrient 1`. The sheet on a table, normal at the ceiling. Where the file started |
| R02 stood up, pen square | The sheet Z now faces the arm — and prc refuses, with every target inside the ring |
| R03 the fix | `penLeanDeg 20`. Nothing moved. The pen leans and the same job runs |
| R04 turned away | `boardOrient 3`. The arm reaching round the back of its own work |
| R05–R07 AUTO | board in front / left / right — the sheet turns to follow |
| R08 too close | board at 600. Fails for being **near**, not far |
| R09 shipped | 900 / 0 / 450, lean 20, 669 targets, 3 swaps |

---

## 9. Still open

- **The flange definition.** Where the flange face sits on the pen tool is the
  one assumption in the model. `PN_README.md` §2.
- **The residual singularity warning.** prc runs the shipped job but reports
  *possible singularities that can lead to excessive axis speed*. It survives
  every lean in the working band and every magazine placement, so it is
  inherent to drawing on a sheet that stands square-on rather than a setting
  that has been missed. **Watch the axis speeds on the first T1 run**, and if
  they are unacceptable the next thing to try is `boardOrient 2` with the lean
  re-tuned, which takes the sheet off square.
- **Pen force.** The spring gives 20 mm of travel. What force it delivers over
  that travel is not modelled, so `pressDepth` is a geometric number, not a
  force one.
- **Collision.** prc checks the robot against itself. The **board, the easel
  and the magazine are not in the collision model.**
- **The magazine is still placeholder coordinates.** Not one of those numbers
  has touched hardware. See `../02_TF09_pen_switching/krl/PENSWAP_README.md`.

**liveRun ships OFF**, so the generated KRL is a dry run: the clamp never fires
and the pen stays one lift height clear of the paper. Turn it on only after the
dry run has passed in T1.

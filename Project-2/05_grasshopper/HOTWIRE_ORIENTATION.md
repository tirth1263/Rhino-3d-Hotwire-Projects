# Hotwire orientation, reach and the approach switch

**What this covers:** why the Z axis was wrong, what replaced it, and the
numbers worth carrying into the next project.

Everything below was **measured against KUKA|prc**, not taken from a datasheet
or reasoned out. Where a number came from a sweep, the sweep is quoted.

---

## 1. The short version

The FL-01 simulation was reporting unreachable poses and singularities. There
were **three separate causes**, and only the first was visible.

| # | Cause | Fix |
|---|---|---|
| 1 | FL-01's Z is **radial** — it swings right round the loop, so somewhere in every pass it points back at the robot | the `frameMode` / `cardinal` switch |
| 2 | The **frame's X**, not Z, decides where the arm stands — the TCP is 422 mm along tool X | `frameMode 1` aims X away from the robot |
| 3 | The reachable zone is a **ring**, not a maximum — too close fails like too far | reach check now tests both walls |

Result: **KUKA|prc solves clean on all 12 passes.**

One correction worth stating plainly: I first said the part in `HoitWire_V1.3dm`
was too far away at 1273 mm. **That was wrong.** The flange sits 422 mm back
from the cut, so 1273 mm is comfortably inside the working ring. The part
placement was fine all along — orientation was the entire problem. The default
in the shipped file is now your original 1273 / −20 / 302.

---

## 2. Numbers to carry forward

### The tool — from the pendant's CUSTOM TOOL dialog

| Field | Value |
|---|---|
| Tool X / Y | 0 / 0 |
| **Tool Z** | **422** |
| **Tool A / B / C** | **−90 / −90 / 0** |

### What those angles actually produce

KUKA is Z-Y'-X'' intrinsic, so `R = Rz(A)·Ry(B)·Rx(C)`. Feeding −90 / −90 / 0
through it:

```
tool X  =  flange +Z     out along the tool
tool Y  =  flange +X
tool Z  =  flange +Y      <-- ALONG THE WIRE
```

**Two consequences that drive everything else:**

- **The wire lies on tool Z.** So a target plane's Z *is* the wire direction.
- **The TCP is 422 mm along tool X.** So the flange lands **422 mm back along
  the target's X** — the frame's X decides where the arm has to stand.

Verified rather than assumed: stepping from the TCP along its own Z by half the
measured wire span lands **0.65 mm** from the modelled wire ends.

> **A −90 / B −90 / C 0 is exactly gimbal lock.** At `B = ±90` only `A + C` is
> determined, so the pendant may read back **A 0 / B −90 / C −90** for the same
> pose. Nothing is wrong when that happens.

### The tool geometry — measured from `Hotwire_2.1.3dm`

| | Value |
|---|---|
| Modelled in | **flange coordinates** — flange face at origin, tool along +Z |
| Wire | along flange **Y**, at **Z 421.35** |
| Wire span | −207.9 … +207.9 → **415.8 mm** |
| Wire diameter | ~1 mm |
| Frame plate | 150 × 150 |
| Bracket arms | Y ±200…220, up to Z 421.35 |

`415.8` is the **modelled** span, bracket face to bracket face. The **usable**
cutting span is shorter and is still unmeasured — it is an open item on the
end-effector README.

### The working envelope — swept, not looked up

Robot: **KUKA Agilus KR6-10 R1100-2**, standing at the world origin.

**Radial — a ring, not a maximum.** Part distance from the base, straight in
front:

| Part radius | Flange radius | prc |
|---|---|---|
| 700, 800, 850 | 287–470 | **ERROR** — flange folds into the robot |
| **900 … 1500** | **459–1050** | **clean** |
| 1550, 1600 | 1128+ | **ERROR** — arm cannot stretch |

Because the flange sits 422 mm back from the cut, **bringing the work closer is
not automatically the fix.** That is the least intuitive thing in this document.

**Angular — there is a blind spot behind.** Part swept round at R = 1273 mm,
0° = straight in front:

| Bearing | prc |
|---|---|
| 0° … 170° | clean |
| **175° … 185°** | **ERROR** — axis 1 cannot wrap that far |
| 190° … 345° | clean |

A ~10° wedge directly astern that no orientation reaches. Swing the part 15–20°
either side and it comes back.

### Frames from the end-effector README

| Frame | Meaning |
|---|---|
| **BASE[3]** | foam-block origin |
| **TOOL[4]** | wire midpoint — the TCP |
| **TOOL[5] / TOOL[6]** | wire ends A and B, for ruled surfaces |

### Feeds and power

| | EPS | XPS |
|---|---|---|
| Feed rate | 30–50 mm/s | 20–40 mm/s |
| Wire voltage | 8–10 V | 10–14 V |
| Motion type | LIN (CP) | LIN (CP) |

---

## 3. The switch

Two sliders on the FL-01 canvas, in the HOTWIRE group.

### `cardinal` — which direction

| Value | Meaning |
|---|---|
| **0 AUTO** | read from where the part sits — **shipped** |
| 1 / 2 | +X / −X |
| 3 / 4 | +Y / −Y |

AUTO takes the part's centroid, drops the vertical, and snaps to whichever of
±X / ±Y it points along. In front → +X, behind → −X, either side → ±Y. **Drag
the part sliders and the approach re-picks itself.**

### `frameMode` — what that direction drives

| Value | Meaning |
|---|---|
| 0 KEEP | FL-01's own radial Z. The original behaviour |
| **1 CUT** | the cardinal drives the **ARM**; the wire lies across the travel — **shipped** |
| 2 WIRE | the cardinal drives the **WIRE**; Z forced literally |

**Why mode 1 and not mode 2**, which is what "force Z to +X" says literally:

- Reach is governed by **X** (the flange sits 422 mm back along it).
- Cutting is governed by **Z** (the wire lies on it).

Different axes, so **both can be satisfied at once**. Mode 2 satisfies only the
first and points the wire end-on into the foam. Measured, part in front, pass 0:

| frameMode | prc | hotwire warnings |
|---|---|---|
| 0 KEEP | **ERROR** | 2 |
| **1 CUT** | **CLEAN** | **0** |
| 2 WIRE | **ERROR** | 3 |

In mode 1 on an upright part with horizontal slices, the wire comes out
**vertical** — spanning the part's height and sweeping round its profile. That
is the orientation that cuts.

### What the component tells you

`Log` reports, every solve:

- which cardinal was chosen and why,
- whether the wire is **across** the travel (cutting), **along** it (sliding
  down its own kerf), or **along the approach** (stabbing in end-on),
- the flange distance range against **both** walls of the ring,
- the part's bearing, and a warning if it is in the blind spot.

`FlangePts` is the output worth previewing — it shows where the **wrist** has to
be, which is what the reach test actually measures.

---

## 4. The rule of thumb, for the next project

> **Point the frame's X at the robot's shoulder, and the wire across the cut.**

Then check three things, in this order:

1. **Bearing** — is the part within 170° either side of front?
2. **Radius** — is it between 900 and 1500 mm?
3. **Wire attitude** — does `Log` say *across the travel*?

If all three pass, prc will almost certainly solve.

And the trap, once more: **if prc says unreachable, try moving the part
further away** before you try moving it closer.

---

## 5. The files

| File | What |
|---|---|
| `FL01_mesh_to_planes.gh` | the definition, hotwire wired in, switch on the canvas |
| `TirthWork_Cell.3dm` | the Rhino model — tool in flange coords, TCP frames, foam, part upright at 1273 / −20 / 302 |
| `../06_hotwire_tool/` | the HOTWIRE component's three panes + its README |
| `renders_hotwire/` | nine renders, one per configuration |

### About `renders_hotwire/`

These are **renders, not screen captures.** Rhino runs headless in this
workflow, so there is no window to photograph — every image is drawn from the
geometry the definition actually computed, and each one says so on its face.
Screenshots of the Grasshopper canvas itself need Rhino open on a desktop;
`../04_progress/SCREENSHOT_CHECKLIST.md` lists those.

| Render | Shows |
|---|---|
| R01 the problem | `frameMode 0`. Red arm-axes fan out in a starburst, blue flange dots form a **complete ring around the part** — the arm asked to orbit the work. UNREACHABLE |
| R02 forcing Z literally | `frameMode 2`. Wire points straight at the part, end-on |
| R03 the fix | `frameMode 1`. Wire vertical through the part, flange dots on one side. CLEAN |
| R04–R06 AUTO | part in front / left / right — approach follows it |
| R07 blind spot | part behind. AUTO picks −X correctly and it is still unreachable |
| R08 too close | part at 800 mm. Fails for being **near**, not far |
| R09 shipped setup | 1273 / −20 / 302, all 12 passes clean |

---

## 6. Still open

- **Usable wire span** — 415.8 mm is modelled, not usable. Measure it.
- **Wire sag** — treated as a straight line. It is not; it sags into a catenary,
  more when hot and long. The end-effector README calls sag a form-generator
  worth chasing.
- **Kerf** — a hot wire removes a band wider than itself. Not modelled.
- **BASE[3]** — not taught.
- **`reachMin` 460 mm** is measured against this tool and this cell. Re-measure
  it if the tool changes; it is an input, not a constant.
- **Collision** — prc checks the robot against itself. The **foam block and the
  fixture are not in the collision model.** A clean prc solve is not a promise
  that the frame will not hit the workpiece.

**Never pause the robot with the wire in the foam** — it burns through and melts
a pocket. Ventilation mandatory: styrene fumes.

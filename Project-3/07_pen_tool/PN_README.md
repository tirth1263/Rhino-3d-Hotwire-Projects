# PENTOOL — the pen tool and the board switch

The TF-09 counterpart of `06_hotwire_tool/`. Three panes for one Rhino 8 C#
Script component, plus this note.

| Pane | File |
|---|---|
| Usings | `PN_usings.cs` |
| Script | `PN_body.cs` — the input/output contract lives in its header comment |
| Members | `PN_helpers.cs` — `public static class PenTool` |

You do not have to assemble it by hand. `05_grasshopper/_build/build_gh.ps1`
reads these three files and writes `TF09_pen_drawing.gh` with the component
already built and wired.

---

## 1. What it does, and why it sits on the other side

The hotwire component takes FL-01's finished planes and re-lays the **tool**.
This one builds the **board** and hands it to TF-09.

That is not an inconsistency, it follows from the tool:

| | hotwire | pen |
|---|---|---|
| Cutting element | a **line** | a **point** |
| So the interesting question is | how do I lay the tool | how do I hang the work |
| Which puts the component | downstream of FL-01 | **upstream of TF-09** |

A wire has three usable attitudes and only one of them cuts, so the hotwire
component earns its keep by re-orienting targets. A pen has exactly one — down
into the paper — so there is nothing to choose there, and the whole question
moves to the sheet.

It also *has* to be upstream. TF-09 needs a draw plane before it can build a
single target, so a component deriving the board from TF-09's own output would
be a cycle, and Grasshopper will not run a cycle. The reach check therefore
samples a 5×5 grid over the sheet rather than the finished toolpath — the same
set of points, since every drawing target lies on the sheet.

---

## 2. The tool — measured, and the one thing that is not

Read out of the lab's own
`End-Effector Development/Drawing/Pen_Tool Rev 008.3dm`.

**That file is a workshop layout, not a flange-referenced model.** It is 1.95 m
across and carries 2D dimension drawings, print nests, an 8020 test rig and
loose hardware alongside the assembly. So unlike `Hotwire_2.1.3dm` — which is
already in flange coordinates and could be used verbatim — the pen tool is
**rebuilt from its dimensions**.

### Measured

The assembly sits as a coherent cluster at X −171…−71, Y −31…+31, Z −152.8…+75:

| Part | Size | Read from |
|---|---|---|
| Mount plate | 100 × 100 × **15** | `3D Print`, X −171.1…−71.2, Y 16.2…31.3 |
| Spring body | 62.5 sq × **50** | `Holder2`, Y −31.2…31.3, Z −87.5…−37.5 |
| Pen carriage | 25 sq × **69** | `Holder`, X −133.7…−108.6, Z −119.6…−50.6 |
| Spring | ⌀15.5, **20 mm** travel | `Spring`, Z −84.6…−64.6 |
| Pen barrel | ⌀10.5 × **121.4** | `Pens`, Z −152.8…−31.4 |
| **Nib, from the mounting face** | **227.8** | the assembly's own extent along its axis |

### The check that makes it believable

The stack is plate 15 + body 50 + carriage 69 = **134 mm** of structure. The
pen is **121.4** long and its nib is **227.8** out, so its top end lands at
227.8 − 121.4 = **106.4** — *inside* the carriage, which spans 65 to 134.

Nothing forced that. Two independently measured numbers agree, which is what
says the stack is the real one rather than a plausible one.

### The definition

> **Where the flange face sits, and which way the pen points from it, is a
> DEFINITION — not a measurement.**

Rev 008 does not record either, because a bench layout has no flange. The
definition taken here is the simple one:

- the **mounting face is the flange face**, at the origin;
- the **pen runs straight out along the flange axis**, +Z.

Which gives the CUSTOM TOOL dialog:

| Field | Value |
|---|---|
| Tool X / Y | 0 / 0 |
| **Tool Z** | **227.8** |
| **Tool A / B / C** | **0 / 0 / 0** |

All three angles zero, and that is the whole difference from the hotwire. A
crossbar needs `A −90 / B −90 / C 0` to describe where it lies. A pen has
nothing to twist.

**If the real bracket turns out to be an L rather than in-line, change
`toolA/B/C` on the canvas — nothing downstream is hard-coded to zero.**

---

## 3. The board switch

### `boardOrient` — how the sheet is hung

| Value | Meaning |
|---|---|
| **0 VERTICAL** | standing on an easel, facing the robot — **shipped** |
| 1 FLAT | lying on a table, normal at the ceiling |
| 2 TILTED | a drafting table; `leanDeg` 0 is FLAT and 90 is VERTICAL |
| 3 AWAY | turned to face away from the robot — kept so you can see wrong |

Measured at the shipped pen lean of 20°, board at 900 / 0 / 450:

| boardOrient | KUKA\|prc |
|---|---|
| **0 VERTICAL** | **runs** (singularity warning) |
| 1 FLAT | unreachable |
| 2 TILTED (lean 45) | unreachable |
| 3 AWAY | runs |

1 and 2 want a *different* lean. The board attitude and the pen lean are one
question, not two — which is exactly why they live on the same component.
Change `boardOrient` and expect to re-tune `penLeanDeg`.

### `cardinal` — which way it faces

| Value | Meaning |
|---|---|
| **0 AUTO** | read from where the board sits — **shipped** |
| 1 / 2 | +X / −X |
| 3 / 4 | +Y / −Y |

Same rule as the hotwire's: drop the vertical, snap to whichever of ±X / ±Y
points from the robot to the work. Drag `board X/Y/Z` and the sheet turns to
keep facing the arm.

### All four modes name their axes the same way

```
X   across the sheet, left to right
Y   up the sheet
Z   out of the sheet, towards the reader
```

Which is what lets the artwork be authored once in world XY and oriented onto
whichever board you pick. **The drawing does not know how the board is hung.**

---

## 4. Which Z is which

The thing that confuses everyone, so it is worth stating flatly:

| | Direction |
|---|---|
| the **BOARD's** Z | out of the sheet, **back at the robot** |
| the **TARGET's** Z | into the sheet, **down the pen** |

They are opposite by definition, and they have to be. The pen reaches 227.8 mm
ahead of the wrist, so the flange has to sit **between the robot and the
paper** — otherwise the arm cannot reach past its own tool. Preview
`FlangePts` and you are looking at exactly that.

So "the blue Z faces the robot" is true of the sheet, and cannot be true of the
pen. `zToRobot` is on by default and enforces it on the sheet, and it reports
when it cannot — a board lying FLAT has a vertical normal and no horizontal
component to turn, and it says so rather than quietly doing nothing.

---

## 5. `penLeanDeg` — the one that actually bit

**The failure is invisible in the geometry, which is why it gets its own
section.**

Every target in the job sat comfortably inside the reach ring — flange 681 to
832 mm against a 460 to 1101 ring — and KUKA|prc refused the whole thing.
Moving the board nearer, further, higher and sideways changed nothing. Swinging
the magazine round the cell changed nothing. Clearing the tool mesh changed
nothing. The same job with the board lying **flat** solved instantly.

The difference is not where the tool is. It is which way it points.

> A sheet standing square-on to the robot has its normal pointing back down the
> arm's own reach line. A pen held perpendicular to that paper is therefore
> aimed straight at the shoulder. The wrist has to go flat to manage it, axis 4
> lines up with axis 6, and the pose is singular however much room there is
> around it.

Lean the pen and the wrist has something to bend around. Measured, board at
900 / 0 / 450:

| penLeanDeg | KUKA\|prc |
|---|---|
| 0, 5, 10, 12 | **UNREACHABLE** |
| **15, 20, 25, 30** | **runs** |
| 35, 40 | **UNREACHABLE** — too far the other way |
| −20 | runs |

Ten degrees is not enough and thirty-five is too much. The band is roughly
15–30 either side of square, and **20 is shipped**.

It is also just what a hand does. Nobody draws with the pen dead perpendicular
to the paper.

`penLeanDeg` goes straight through to TF-09's `tiltDeg` — TF-09 has no slider
of its own for it any more — so one component owns the whole orientation story
and can check its own advice. Leave it unwired and it reads 0, which is
*exactly* the bad case, so 0 is taken literally and **warned about** rather
than silently replaced with 20. Hiding it would hide the lesson.

---

## 6. What it tells you

`Log`, every solve:

- which cardinal was chosen and why;
- how far the sheet's own Z is off facing the robot, measured **horizontally**
  (measuring it raw would score a downward normal well against a base that is
  also on the floor — the same bug the hotwire component's `ZReport` had);
- how square-on the sheet is to the arm, and whether the lean is enough;
- the flange distance range over the sheet, against **both** walls of the ring;
- the board's bearing, and a warning if it is in the blind spot astern.

---

## 7. Still open

- **The flange definition** — section 2. The single assumption in the file.
- **Pen force** — the spring gives 20 mm of travel; the force it delivers over
  that travel is not modelled, and `pressDepth` is currently a geometric
  number rather than a force one.
- **The residual singularity warning.** prc runs the shipped job but reports
  *possible singularities that can lead to excessive axis speed*. It is a
  warning, not an error, and it survives every lean in the working band and
  every magazine placement — it is inherent to drawing on a sheet that stands
  square-on. **Watch the axis speeds on the first T1 run.**
- **Collision** — prc checks the robot against itself. The **board, the easel
  and the magazine are not in the collision model.** A clean prc solve is not a
  promise that the tool will not hit the work.

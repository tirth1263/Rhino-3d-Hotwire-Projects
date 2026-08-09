# Hotwire tool — TCP frame and plane flipping

One Rhino 8 **C# Script** component. Paste `HW_usings.cs` / `HW_body.cs` /
`HW_helpers.cs` into the three panes — or just open
`../05_grasshopper/FL01_mesh_to_planes.gh`, where it is already wired.

It does two things, and they are the same thing twice: **which way round is
this frame?**

1. Turns the numbers on the pendant's **CUSTOM TOOL** dialog — X, Y, Z, A, B, C
   — into the Plane that KUKA|prc's *Custom Tool: Plane* wants.
2. Lets you flip and rotate the toolpath targets with toggles, and **tells you
   whether the flip helped**.

---

## 1. The tool, and where its numbers come from

Nothing here is invented. The defaults are read out of two things the lab
already has.

**From the CUSTOM TOOL dialog:**

| Field | Value |
|---|---|
| Tool X / Y | 0 / 0 |
| Tool Z | **422** |
| Tool A / B / C | **−90 / −90 / 0** |

**From `End-Effector Development/Hotwire/Rev2.1/Hotwire_2.1.3dm`,** read
directly rather than eyeballed:

| Measured | Value |
|---|---|
| Flange face | at the model origin, tool reaching along **+Z** |
| Wire | along flange **Y**, at **Z 421.35** |
| Wire span | −207.9 … +207.9 → **415.8 mm** |
| Wire diameter | ~1 mm |
| Frame plate | 150 × 150, bracket arms at Y ±200…220 |

### These two agree, and that was checked rather than assumed

Feeding A −90 / B −90 / C 0 through the KUKA Euler convention gives a tool
frame whose axes are:

```
tool X  =  flange +Z    out along the tool
tool Y  =  flange +X
tool Z  =  flange +Y    <-- ALONG THE WIRE
```

So the TCP's **Z axis runs down the wire**. The test: step along that Z by half
the measured span and see where you land. It lands **0.65 mm** from the
modelled wire ends. That is what says the frame really follows the wire rather
than merely looking plausible.

That also means the three TCPs in the end-effector README fall out for free:

| | Where |
|---|---|
| **TOOL[4]** wire midpoint | the TCP itself |
| **TOOL[5]** wire end A | TCP origin − Z × span/2 |
| **TOOL[6]** wire end B | TCP origin + Z × span/2 |

### A −90 / B −90 / C 0 is exactly gimbal lock

`B = ±90` is the pose where only `A + C` is determined, so **A −90 / B −90 / C 0
and A 0 / B −90 / C −90 are the same orientation**. The component's `ToolAbc`
output reads back the second form. Nothing is wrong when the pendant and the
panel disagree like that — they are the same pose written two ways.

---

## 2. The flip toggles

You asked for a way to try the other orientation without editing code. There
are four, and they compose:

| Input | Does |
|---|---|
| `flipZ` | approach from the other side (half turn about travel) |
| `flipX` | reverse the travel direction (half turn about approach) |
| **`tiltDeg`** | **rotate about the travel direction — the important one** |
| `spinDeg` | free spin about the approach |

Plus two on the tool frame itself, `flipToolZ` and `flipToolSpin`, for when the
tool is the thing that is backwards.

### Why `tiltDeg` matters more than the rest

The cutting element is a **line, not a point**. Unlike a router bit, it matters
which way the line lies relative to travel. There are three cases and only one
of them cuts:

| Wire lies | What happens |
|---|---|
| **across** the travel, tangent to the surface | slicing. This is the one you want. |
| **along** the travel | the wire slides down the kerf it already made and removes nothing |
| **along** the approach | the wire goes in end-on and melts a pocket |

FL-01 hands out planes whose **Z points into the material** — correct for a
point tool, and wrong for a wire, because the wire is on tool Z. Fed straight
in, all 65 targets in a pass put the wire in end-on.

`tiltDeg = 90` swings it round to lie across the travel. **That is why the
shipped file has `tiltDeg` at 90 and not 0.**

### The component tells you which case you are in

Every solve, `Log` reports the angle between the wire and both the travel and
the approach direction, and warns when it goes wrong:

```
WIRE VS  travel  worst |cos| 0.000   approach  worst |cos| 0.000
  OK   the wire lies across the travel and tangent to the surface,
       which is the slicing case.
```

Drag `tiltDeg` to 0 and the component goes orange and says why. That feedback
is the point — toggle, read, decide, rather than reasoning about it in your
head.

Measured on the demo part: **tilt 90 clean, tilt 0 warns on 65 of 65 targets.**

One subtlety worth knowing: `spinDeg` rotates about the **approach**, which
once `tiltDeg` is 90 is the wire's own axis — so spinning does not move the
wire, and the diagnostic correctly stays clean. That is not a bug in either.

---

## 3. Inputs

| # | Name | Type | Access | Optional | Default |
|---|---|---|---|---|---|
| 0 | `toolX` | double | item | yes | 0 |
| 1 | `toolY` | double | item | yes | 0 |
| 2 | `toolZ` | double | item | yes | **422** |
| 3 | `toolA` | double | item | **no** | — |
| 4 | `toolB` | double | item | **no** | — |
| 5 | `toolC` | double | item | **no** | — |
| 6 | `wireSpan` | double | item | yes | **415.8** |
| 7 | `wireAxis` | int | item | **no** | — (canvas wires 2 = Z) |
| 8 | `flipToolZ` | bool | item | yes | false |
| 9 | `flipToolSpin` | bool | item | yes | false |
| 10 | `targets` | Plane | **list** | yes | — |
| 11 | `flipZ` | bool | item | yes | false |
| 12 | `flipX` | bool | item | yes | false |
| 13 | `tiltDeg` | double | item | yes | 0 (canvas wires **90**) |
| 14 | `spinDeg` | double | item | yes | 0 |

**Why 3, 4, 5 and 7 are not optional.** Everywhere else in this project an
unwired number reads as 0 and that is taken to mean "use the documented
default". That trick cannot work for the angles or the axis index, because
**0 is a real value for all four** — A 0 / B 0 / C 0 is the identity
orientation and `wireAxis` 0 is the X axis. So rather than guess, they are
required and Grasshopper shows *no data* until they are wired.

## 4. Outputs

| # | Name | Feeds |
|---|---|---|
| 1 | `ToolPlane` | KUKA\|prc **Custom Tool: Plane** → *Tool Plane* |
| 2 | `ToolAbc` | a panel — the X/Y/Z/A/B/C read back for the pendant |
| 3 | `Targets` | the **LINear Movement** component |
| 4 | `WireLines` | preview this — it is where the wire actually is |
| 5 | `WireEndA` | TOOL[5] frames |
| 6 | `WireEndB` | TOOL[6] frames |
| 7 | `Status` | one line |
| 8 | `Log` | the wire-vs-travel diagnosis |

---

## 5. Open items — please read before cutting foam

- **`wireSpan` 415.8 is the MODELLED span**, bracket face to bracket face. The
  **usable** cutting span is shorter — the wire needs tension length and the
  brackets get in the way before the ends do. The end-effector README lists
  measuring it as an open item, and it is still open.
- **Wire sag is not modelled.** The wire is treated as a straight line. It is
  not: it sags into a catenary, more so when hot and long. The hotwire README
  calls sag "an error worth chasing" and a form-generator in its own right.
  Nothing here accounts for it.
- **The kerf is not modelled.** A hot wire removes a band wider than itself.
- **Feed and voltage are not set here.** EPS 30–50 mm/s at 8–10 V, XPS
  20–40 mm/s at 10–14 V, motion type LIN — from the end-effector README's key
  parameters table. They belong in the KRL, not in this component.
- **BASE[3] is the foam-block origin** and has not been taught.

**Never pause the robot with the wire in the foam.** It will burn through and
melt a pocket. Ventilation is mandatory — styrene fumes. See the lab's
`docs/safety-protocols.md`.

# Toolpath simulator (native Grasshopper)

Shared by FL-01 and TF-09. One Rhino 8 **C# Script** component, no plugins,
no Python.

---

## What it is, and what it is not

**It is** a real time-and-motion simulation of the TCP. It builds a timeline
from the actual distances between your targets and the actual commanded feed
rates, interpolates position linearly and orientation by shortest-arc rotation,
and tells you where the tool is at any moment, how long the job takes, and
where the wrist has to snap.

**It is not** a joint-space simulation. It knows nothing about axis limits,
reach or singularities. **KUKA|prc's own Analysis component does that, and it
stays in the definition.** This runs alongside it, because prc's playback
cannot answer "how long does this take" or "how fast is the orientation
changing right here", and those are the two questions that decide whether a
path is worth sending to the robot.

Nothing in it is approximated for convenience. The interpolation between two
targets is the same shortest-arc rotation a `LIN` move performs, so the
orientation you see mid-segment is the orientation the robot passes through.

---

## Building it

Paste `SIM_usings.cs` / `SIM_body.cs` / `SIM_helpers.cs` into the three panes
of a C# Script component.

| # | Name | Type hint | Access | Optional | Wire |
|---|---|---|---|---|---|
| 0 | `targets` | Plane | **list** | no | FL-01 `Planes` (flattened) or TF-09 `Flat` |
| 1 | `moveTypes` | int | **list** | yes | the matching `MoveTypes` / `FlatMoves` |
| 2 | `feedProcess` | double | item | yes | slider 5–200 |
| 3 | `feedRapid` | double | item | yes | slider 50–500 |
| 4 | `dwellSeconds` | double | item | yes | slider 0–90 |
| 5 | `t` | double | item | **no** | **Number Slider 0.000 – 1.000** |
| 6 | `toolLength` | double | item | yes | slider 20–400 |
| 7 | `toolRadius` | double | item | yes | slider 1–30 |
| 8 | `turnRateLimit` | double | item | yes | slider 0.5–10 (deg/mm) |

Outputs, in order: `TCP`, `ToolAxis`, `ToolBody`, `Trail`, `Remaining`, `Time`,
`CycleTime`, `Index`, `Feed`, `MoveLabel`, `HotSpots`, `MaxTurn`,
`MaxTurnRate`, `Status`, `Log`.

---

## Playing it

Drag the `t` slider. That is the whole interface.

For a hands-off playback, right-click the `t` slider → **Animate**. Set the
frame count and an output folder and Grasshopper writes every frame to disk —
which is also the easiest way to produce the progress images without doing
anything by hand.

Suggested preview wiring:

```
Trail      ──▶ Custom Preview  (green,  the path already done)
Remaining  ──▶ Custom Preview  (grey,   the path still to go)
ToolBody   ──▶ Custom Preview  (red,    the tool itself)
TCP        ──▶ let the wire preview draw it, so you can see the axes
Log        ──▶ Panel
```

---

## The number that matters most

`MaxTurnRate`, in **degrees per millimetre**.

A big rotation is not itself a problem — a big rotation over a long move is
gentle. A big rotation over a *short* move is what makes the wrist accelerate
hard enough to fault out or to gouge the work. That ratio is what
`turnRateLimit` tests, and every segment above it lands in `HotSpots`.

Wire `HotSpots` to a panel. If it is empty, the orientation is smooth. If it is
not, the segment numbers tell you exactly where to look — usually a corner
where the section changed shape faster than the sampling could follow. The fix
is normally more `samples` upstream, or `rollMode = 1` if the tool is round
about its own axis.

---

## Reading the rest

| Output | Meaning |
|---|---|
| `TCP` | The tool plane at this instant. |
| `ToolBody` | A cone with its apex on the TCP, built by hand so there is no doubt which end is the tip. |
| `Trail` / `Remaining` | Done / still to go. Preview both in different colours. |
| `Time` | Seconds elapsed at this slider position. |
| `CycleTime` | Seconds for the whole job, including magazine dwells. |
| `Feed` | Commanded mm/s right now. Reads 0 while the tool is holding at the magazine. |
| `MoveLabel` | "process move", "air move", or "trip to the magazine". |
| `Log` | Distance and time split between process, air and dwell, plus the turn statistics. |

---

## Orientation independence

There is no world axis anywhere in this component. Every quantity is derived
from the target planes themselves, so a rotated job simulates identically.

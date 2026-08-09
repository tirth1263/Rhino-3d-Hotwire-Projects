# PENSWAP — commissioning and safety

**Read this before the robot moves.** These files are written and reasoned
through, but nothing in them has touched hardware. Every coordinate is a
placeholder. Treat this as a program to be commissioned, not a program to be
run.

---

## Files

| File | What |
|---|---|
| `PENSWAP.src` | Subroutines. Park, acquire, collet, tool, recovery. Running `PENSWAP` itself only configures the magazine — it does not move the robot. |
| `PENSWAP.dat` | Persistent state and magazine geometry. The persistence is what makes safe-abort possible. |
| *your generated job* | Written by the TF-09 Grasshopper component. Calls `PEN_ENSURE()` and `LIN`. |

---

## The one safety rule this design rests on

> **The collet only ever changes state while the tool is at a slot's seated
> pose.**

Never in mid-air. Never during a move. Never at the approach pose. Because of
this, at every instant the pen is either fully in its slot or fully in the
gripper — so no stop, abort, e-stop or power cut can drop one.

If you ever find yourself adding a `$OUT[PEN_OUT_CLAMP] = ...` anywhere other
than inside `PEN_COLLET_OPEN` / `PEN_COLLET_CLOSE`, stop. That single rule is
the whole safety argument, and it is only worth anything while it is true
everywhere.

**Wire the collet spring-closed.** `$OUT[PEN_OUT_CLAMP] = TRUE` must mean
*open*. A power loss must let the collet close, not open.

---

## Commissioning order

Do these in order. Do not skip ahead because a step looks trivial.

### Step 0 — before anything

- [ ] Both files copied to the controller. `PENSWAP` selects and compiles with
      no errors.
- [ ] `PEN_DRYRUN = TRUE` in the DAT.
- [ ] `PEN_SENSORS_FITTED = FALSE` until the sensors are actually wired.
- [ ] `XHOME` exists and is somewhere sane. The generated job starts and ends
      with `PTP XHOME`.
- [ ] `TOOL_DATA[1]` is the bare gripper, no pen. `PEN_DO_PARK` reverts to it.
- [ ] `BASE_DATA[]` entries exist for both bases in the drawing end-effector
      README: **BASE[1]** worktable centre, **BASE[2]** large-format shift.
      Teach the ones you are going to use.
- [ ] `PEN_MAGBASE` in the DAT names the base you are about to touch the
      magazine up in. Set it **before** Step 1, not after.

### Step 0a — bases

The job's targets and the magazine live in separate bases, and getting this
wrong drives the tool at the magazine from the wrong place. Two variables:

| Variable | Means |
|---|---|
| `PEN_BASENO` | the base the drawing job's `LIN` targets are in. The generated `.src` writes it in its header from the `baseIndex` input. |
| `PEN_MAGBASE` | the base the magazine was taught in. Set it here and leave it. |

- [ ] Select `BASE[PEN_MAGBASE]` on the smartPAD before touching up any slot.
- [ ] Confirm every `$BASE` assignment in `PENSWAP.src` goes through
      `PEN_USE_BASE()`. That routine forces an advance-run stop first —
      assigning `$BASE` with a move still in the advance run gets that move
      recomputed in the new base. If you add a direct `$BASE =` anywhere,
      you have removed the protection.
- [ ] With the magazine empty, run a job generated with `baseIndex = 2` and
      confirm the magazine poses are unchanged from the `baseIndex = 1` run.
      They must be: the magazine does not move when the paper does.

### Step 1 — magazine empty, T1, no pens at all

Touch up the slot poses. For each slot:

- [ ] Jog to where the pen sits fully home. Record as `PEN_SEAT[n]`.
- [ ] Jog straight out along the slot axis until clear of the magazine. Record
      as `PEN_APPR[n]`.
- [ ] Check that the straight line between the two is genuinely clear. The
      pen enters and leaves along that line and nothing else.
- [ ] Paste the values into `PEN_CONFIG()` in `PENSWAP.src`.

Then, still with the magazine empty:

- [ ] Run `PEN_ENSURE(1)` at T1, hand on the enabling switch. Watch it go to
      approach, then seat, then back out. Nothing should actuate.
- [ ] Repeat for every slot.

### Step 2 — one dummy pen, T1, still dry

- [ ] Put a scrap pen in slot 1.
- [ ] `PEN_ENSURE(1)` then `PEN_ENSURE(2)` then `PEN_ENSURE(-1)`. Watch the
      full park-and-fetch cycle. Nothing actuates; the pen stays put.
- [ ] Check `PEN_HELD` and `PEN_PHASE` on the smartPAD variable display after
      each. `PEN_PHASE` must be back to `0` every time.

### Step 3 — the abort test (this is the deliverable)

Still dry, still T1, one dummy pen.

- [ ] Start a swap. **Stop the program mid-swap.** Do it several times, at
      different points — while travelling to the slot, while seating, right
      after the collet call, while withdrawing.
- [ ] Each time, read `PEN_PHASE` on the smartPAD and write it down.
- [ ] Reselect the program and run it. `PEN_INIT()` calls `PEN_RECOVER()`.
      Watch it finish the interrupted half.
- [ ] Confirm: the pen ends up in a slot or in the gripper. Never anywhere
      else. `PEN_PHASE` back to `0`.

**Log every one of these.** "Safe-abort never strands a pen" is a claim, and
this table is the evidence for it:

| # | Phase when stopped | What recovery did | Pen ended up | Pass |
|---|---|---|---|---|
|  |  |  |  |  |

### Step 4 — sensors

- [ ] Wire presence, collet-open and collet-closed.
- [ ] Check the input numbers in `PENSWAP.dat` match the real I/O.
- [ ] Set `PEN_SENSORS_FITTED = TRUE`.
- [ ] Repeat Step 3. The phase-7 branch now takes the sensor path, which is
      the one that actually distinguishes "we got the pen" from "we closed on
      air". Test both: run an acquire on an **empty** slot and confirm it
      opens, backs out and halts with a message instead of drawing with
      nothing in the gripper.

### Step 5 — live collet, still T1

- [ ] `PEN_DRYRUN = FALSE`.
- [ ] Full swap cycle, T1, hand on the enabling switch, one dummy pen.
- [ ] Repeat Step 3 once more with the collet live.

### Step 6 — the drawing job

- [ ] Generate a short job in Grasshopper with `liveRun` **off**.
- [ ] Check the header: `PEN_BASENO`, `PEN_MAGBASE` and the press value are all
      printed there. They should be what you asked for.
- [ ] Run it in T1. Confirm the pen never reaches the paper and the swaps
      happen at the right stroke indices.
- [ ] After a swap, read `$BASE` on the smartPAD. It must be back to
      `BASE_DATA[PEN_BASENO]`, not the magazine base. This is the check for the
      bug that used to send every job back to BASE[1] after the first swap.
- [ ] Abort it halfway. Read `PEN_INDEX`. Type that into `startIndex`,
      regenerate, run. Confirm it resumes at the right stroke **and** fetches
      the correct pen first.
- [ ] Only then: `liveRun` on, T1, paper down, low override.
- [ ] Tune `pressDepth` on scrap. It starts at the README's 3 mm. Too little
      and the line breaks up; too much and the spring bottoms out and the tip
      drags. Note the number that works **per pen type** — a technical pen and
      a brush will not want the same one.

---

## Recovering from an abort, day to day

1. Read `PEN_PHASE` and `PEN_HELD` on the smartPAD.
2. Reselect the program. `PEN_INIT()` runs `PEN_RECOVER()` automatically.
3. If it halts with a message, read the message. It names what to check. Do
   not clear it and re-run without looking.
4. Read `PEN_INDEX`. That is the stroke it got to.
5. Put that number into `startIndex` in Grasshopper, regenerate, reload.

**Never hand-edit `PEN_PHASE` or `PEN_HELD` to make an error go away.** Those
two variables are the only record of what is physically where. If they are
wrong, clear the gripper by hand, set `PEN_HELD = -1` and `PEN_PHASE = 0`
together, and check every slot visually before restarting.

---

## Open items

- Slot coordinates in `PEN_CONFIG()` are placeholders.
- `BASE_DATA[1]` and `BASE_DATA[2]` have to be taught. The code selects between
  them; it cannot invent them.
- `pressDepth` starts at the README's 3 mm and has never met paper. Expect to
  change it, and expect it to differ per pen type.
- `PEN_TOOLNO[]` assumes one TOOL number per slot. Whether that assumption
  survives repeated swaps is board item **TF-08** — measure the drift over N
  swaps before trusting it. `PEN_SET_TOOL` posts a message on every swap
  saying the TCP is assumed; delete that message only when TF-08 says you can.
- The collet timeout (`PEN_CLAMP_MS`, 1500 ms) and the settle time
  (`PEN_SETTLE_S`, 0.3 s) are guesses. Time the real mechanism.
- `PEN_VEL_INSERT` is 20 mm/s. Slow on purpose — the magazine is the one place
  the tool goes inside a fixture. Do not raise it to save cycle time until
  everything above is signed off.

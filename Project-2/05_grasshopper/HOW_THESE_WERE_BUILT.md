# How the two `.gh` files were generated

You do not need to read this to use them. It is here so the files are not a
black box, and so they can be rebuilt when the `.cs` panes change.

## Why generate them instead of placing components by hand

TF-09 has **24 inputs** and FL-01 has **17**, each with a name, a type hint, an
access mode and an optional flag. Wiring that by hand is exactly where
transcription mistakes live, and the worst of them are silent: a type hint set
to `double` where the code expects `int` does not raise an error, it quietly
rounds. Generating the canvas from the same table the README documents means
the two cannot drift apart.

It also means that when a `.cs` pane changes, rebuilding is one command instead
of re-pasting three panes into two files and hoping.

## What is in `_build/`

| File | What |
|---|---|
| `GhBuild.cs` | Builds both documents in memory and writes the `.gh` files |
| `build_gh.ps1` | Boots headless Rhino + Grasshopper, compiles `GhBuild.cs`, runs it, then **reopens and solves both files to check them** |

This is tooling, not a deliverable. The deliverables are the `.cs` panes in
`01_`/`02_`/`03_` and the two `.gh` files. **There is no Python anywhere** —
`GhBuild.cs` is C#, driven by PowerShell.

## Running it

```powershell
powershell -File _build\build_gh.ps1
```

It needs nothing installed beyond Rhino 8 and KUKA|prc. It runs Rhino headless
inside `powershell.exe` — see the note in
`../04_progress/PROGRESS.md` about why a compiled `.exe` is not used here.

Expected output ends with both files reopening cleanly:

```
=== TF09_pen_drawing.gh
  opened OK - 62 objects
  Targets    = 8 branches, 669 items
  SwapCount  = 3
  KRL        = 41845 chars, 770 lines
  Status     = OK WITH WARNINGS: 669 targets, 3 swap(s), 1 min 39 s

=== FL01_mesh_to_planes.gh
  opened OK - 42 objects
  Planes     = 12 branches, 780 items
  Status     = OK WITH WARNINGS: 780 planes, 12 branches.
```

**That reopen-and-solve step is the real test.** It proves the C# actually
compiles inside the component, which is the one thing that cannot be checked by
looking at the file.

## Decisions worth knowing about

**The C# component is `a9a8ebd2-…`, the three-pane "C# Script".** Rhino 8 ships
several script components. This is the one whose panes are usings / body /
members, matching how the `.cs` files are written and documented.

**Duplicate usings are stripped on the way in.** The component declares eight
namespaces itself, so a pane repeating them produces `CS0105` warnings — a
component wearing a warning balloon the first time someone opens it is not
free. The `.cs` files keep all their usings, because pasting by hand is still a
supported route and a pane has to stand on its own.

**Demo geometry is internalised.** Both files solve the moment they open, with
no Rhino model. The FL-01 demo part is deliberately lopsided: a section with
any rotational symmetry about its own long axis has two equally correct seam
answers, and FL-01 correctly refuses to choose — right, but a confusing first
thing to meet.

**Analysis ships locked.** It is a licensed prc component and raises an error on
an unlicensed install. Locked, it is present and wired for a machine that has
the licence, and silent on one that does not.

**Files are written locally and copied in.** The destination is a Google Drive
streaming mount, which truncated an archive to one 4 KB cluster during
development. A local write cannot half-succeed and `File.Copy` either lands the
whole thing or throws; the size is checked both sides.

**Only `.gh`, no `.ghx`.** The XML twin was dropped — this is not a git repo, so
there is nothing to diff against, and it was the file that kept truncating.

## Rebuilding after changing a `.cs` pane

1. Edit the pane in `01_`/`02_`/`03_`.
2. Run `build_gh.ps1`.
3. Check the reopen-and-solve output reports 0 errors on the C# component.

The generated files carry no hand edits, so rebuilding never loses work — if
you have customised a canvas, save it under a different name first.

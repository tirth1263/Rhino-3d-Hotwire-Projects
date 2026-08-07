# Rhino 3D Hot-Wire Projects

Rhino 3D, Grasshopper, and C# examples for generating and studying geometric hot-wire toolpaths.

## Projects

| Project | Purpose | Branch |
|---|---|---|
| `Hotwire_CSharp_Toolpath_Component` | General geometry-to-tool-plane C# component and examples | `toolpath-component` |
| `Hotwire_CSharp_Implementation_Demo` | Complete TwistedBlade and WaveFin implementations, documentation, screenshots, and verification | `implementation-demo` |
| `Hotwire_CSharp_Algorithms_Additional` | Surface UV, automatic boundary-pair, centerline-twist, and multi-angle algorithms | `additional-algorithms` |
| `Hotwire_Native_Algorithms` | Five Grasshopper-native design studies | `native-algorithms` |
| `Project-2` | SU26 board work: mesh → KUKA\|prc planes (FL-01), the pen-switching drawing loop (TF-09), a toolpath simulator, and two ready-to-open Grasshopper definitions wired to KUKA\|prc | `main` |

The `main` branch contains the complete collection. Each project branch contains a focused copy of the corresponding project.

## Start Here

For the SU26 board work, open `Project-2/05_grasshopper/RUN_ON_ROBOT.md` — the
two `.gh` files there open and simulate against a KUKA Agilus KR6-10 R1100-2
with demo geometry already baked in, so they need no Rhino model.

For the documented WaveFin example, open:

- `Hotwire_CSharp_Implementation_Demo/Hotwire_WaveFin_IMPLEMENTATION.md`
- `Hotwire_CSharp_Implementation_Demo/Hotwire_WaveFin_SCREENSHOT_EXPLAINED.md`
- `Hotwire_CSharp_Implementation_Demo/Hotwire_WaveFin_Implementation.3dm`
- `Hotwire_CSharp_Implementation_Demo/Hotwire_WaveFin_AllAlgorithms.gh`

## Requirements

- Rhino 8
- Grasshopper
- C# Script component support

## Important Scope

The examples generate and verify geometric hot-wire positions, planes, rails, and ruled cutting surfaces. Production robot use additionally requires a robot model, cell calibration, reachability and collision checks, safe approach paths, and controller-specific code generation.


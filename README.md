# Rhino 3D Hot-Wire Projects

Rhino 3D, Grasshopper, and C# examples for generating and studying geometric hot-wire toolpaths.

## Projects

| Project | Purpose | Branch |
|---|---|---|
| `Hotwire_CSharp_Toolpath_Component` | General geometry-to-tool-plane C# component and examples | `toolpath-component` |
| `Hotwire_CSharp_Implementation_Demo` | Complete TwistedBlade and WaveFin implementations, documentation, screenshots, and verification | `implementation-demo` |
| `Hotwire_CSharp_Algorithms_Additional` | Surface UV, automatic boundary-pair, centerline-twist, and multi-angle algorithms | `additional-algorithms` |
| `Hotwire_Native_Algorithms` | Five Grasshopper-native design studies | `native-algorithms` |

The `main` branch contains the complete collection. Each project branch contains a focused copy of the corresponding project.

## Start Here

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


# Hot-wire C# implementation demonstration

This folder is the hands-on Rhino 8 and Grasshopper learning package.

## Start with the combined design

1. Read `HOTWIRE_SIMPLE_IMPLEMENTATION_GUIDE.docx` for the simple step-by-step explanation.
2. Open `Hotwire_TwistedBlade_Implementation.3dm` in Rhino 8.
3. Open `Hotwire_TwistedBlade_AllAlgorithms.gh` in Grasshopper.
4. Inspect the four labeled C# branches applied to the same twisted, tapered blade.
5. Use `Hotwire_TwistedBlade_QA.png` as the expected viewport reference.
6. Read `VERIFICATION.tsv` for the fresh-open solve results.

The combined definition was reopened and solved after saving. All four embedded C# components returned 41 planes, 41 wire lines, one ruled result, zero warnings, and zero errors.

## Separate verified examples

`Verified_Design_Examples` contains one self-contained `.3dm` + `.gh` + `.cs` set for each approach:

- `00_General_GeometryToPlanes`: paired-rail and geometry-section modes.
- `01_Surface_UV`: surface UV boundary sampling.
- `02_Auto_Boundary_Pair`: automatic opposite-edge selection.
- `03_Centerline_Twist`: centerline, width, and twist ribbon generation.
- `04_MultiAngle_Envelope`: section-envelope angle search.

`Verified_Design_Examples/PACKAGE_MANIFEST.tsv` contains SHA-256 hashes proving that the copied design files match their previously verified originals.

These are geometrically verified examples. Robot execution still requires calibrated tool/base frames, axis remapping, inverse kinematics, reach and collision checks, wire limits, kerf, temperature, feed-rate, and physical test cuts.

## Second implemented design: S-curved wave fin

Open `Hotwire_WaveFin_Implementation.3dm` and then `Hotwire_WaveFin_AllAlgorithms.gh`. This separate design applies all four scripts to an S-curved, tapered, cambered fin. See `Hotwire_WaveFin_IMPLEMENTATION.md` and `VERIFICATION_WaveFin_FreshOpen.tsv`.

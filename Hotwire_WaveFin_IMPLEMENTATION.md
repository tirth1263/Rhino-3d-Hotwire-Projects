# Design 02 - S-curved tapered wave fin

## Files

- `Hotwire_WaveFin_Implementation.3dm`: Rhino 8 model containing the wave-fin solid, four-sided cutting skin, centerline, and baked outputs from all four algorithms.
- `Hotwire_WaveFin_AllAlgorithms.gh`: Grasshopper definition with all four embedded C# scripts and internalized wave-fin inputs.
- `Hotwire_WaveFin_Design_QA.png`: clean design screenshot.
- `Hotwire_WaveFin_AllAlgorithms_QA.png`: complete implementation screenshot.
- `VERIFICATION_WaveFin.tsv`: output counts from the generated working document.
- `VERIFICATION_WaveFin_FreshOpen.tsv`: output counts after reopening the saved `.gh` file from disk and solving it again.

## Design geometry

The second design is about 420 mm long. Its centerline bends sideways in an S-curve and rises vertically. The section width tapers from about 164 mm to 74 mm. Section rotation changes from approximately -18 degrees to +40 degrees. A cambered four-sided surface is used by the UV and boundary algorithms, while a capped lofted solid is used by the multi-angle algorithm.

## Implemented branches

1. Surface UV: 41 positions, wire across U, 10 mm extension.
2. Automatic boundary pair: shorter opposite-edge pair, 41 positions, 10 mm extension.
3. Centerline twist: 170 mm to 82 mm programmed width, 58-degree total twist, 41 positions.
4. Multi-angle envelope: 18 tested orientations, 41 sections, World X path direction, World Z reference wire direction.

## Verified result

The saved Grasshopper file was reopened from disk and solved again. Every branch returned 41 planes, 41 wire lines, one ruled result, zero warnings, and zero errors.

These are geometric paths and planes. Robot execution still requires calibrated tool/base transforms, TCP-axis remapping, inverse kinematics, reach and collision checking, wire-length validation, kerf, temperature, feed rate, and physical test cuts.

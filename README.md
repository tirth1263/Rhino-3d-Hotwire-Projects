# Four additional C# hot-wire path algorithms

Each numbered folder is self-contained. It contains an editable `.cs` source, an embedded Rhino 8 Grasshopper definition, neutral Rhino test geometry, and a short guide.

1. `01_Surface_UV` samples opposite sides of one surface UV domain.
2. `02_Auto_Boundary_Pair` detects the two opposite-edge pairs of a four-sided open surface.
3. `03_Centerline_Twist` generates a ruled ribbon from a centerline, width, and twist.
4. `04_MultiAngle_Envelope` sections Brep/Mesh geometry and searches for a stable wire orientation.

All scripts use the same pose convention: origin at the wire midpoint, X along endpoint A to B, Y along travel, and Z as X×Y.

The scripts produce geometry and planes only. Physical cutting still requires validation of reachability, singularities, collisions, wire length, calibration, foam placement, temperature, kerf, and feed rate.

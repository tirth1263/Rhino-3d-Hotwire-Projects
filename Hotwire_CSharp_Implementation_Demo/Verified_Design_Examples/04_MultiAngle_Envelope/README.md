# Algorithm 04 — Multi-angle section-envelope search

Use this when you have a Brep, Surface, Extrusion, SubD, or Mesh and want to compare several possible hot-wire orientations about a chosen travel direction.

The component sections the geometry, tests directions over 180 degrees, extracts the widest individual contour at every station, and ranks each candidate using valid-section coverage, wire-length variation, and adjacent direction change.

Inputs: `G`, `PathDir`, `ReferenceWireDir`, `AngleCount`, `Count`, `Extension`, `Flip`, `Tolerance`.

Outputs: `Planes`, `WireLines`, `Ruled`, `RailA`, `RailB`, `MidPath`, `BestAngle`, `Scores`, `Report`.

The result is a geometric envelope heuristic, not a proof that concave or multiply connected geometry is hot-wire manufacturable. It does not validate robot IK, collisions, cell limits, wire kerf, feed rate, or controller code.

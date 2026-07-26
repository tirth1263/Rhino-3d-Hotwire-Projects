# Algorithm 03 — Centerline width/twist sweep

Use this to generate a controlled hot-wire ribbon directly from one spatial path. It does not require an input surface.

`WidthStart` and `WidthEnd` define the finite wire span along the path. `TwistDegrees` applies a gradual rotation about the path tangent. `ReferencePlane` seeds the initial lateral direction.

Inputs: `Path`, `ReferencePlane`, `WidthStart`, `WidthEnd`, `TwistDegrees`, `Count`, `Extension`, `Flip`.

Outputs: `Planes`, `WireLines`, `Ruled`, `RailA`, `RailB`, `MidPath`, `Widths`, `Report`.

This is a geometric path generator. It does not validate robot IK, collisions, cell limits, wire kerf, feed rate, or controller code.

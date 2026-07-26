# Algorithm 01 — Surface UV sampling

Use this when the input is one Surface or Brep face and its UV directions already describe the desired cutting logic.

The component samples opposite UV boundaries, creates finite wire lines, builds midpoint robot planes, and lofts the two synchronized endpoint rails. `Deviation` reports the maximum distance between sampled wire points and the source surface.

Inputs: `S`, `WireAcrossU`, `Count`, `Extension`, `Flip`, `Tolerance`.

Outputs: `Planes`, `WireLines`, `Ruled`, `RailA`, `RailB`, `MidPath`, `Deviation`, `Report`.

Trim holes are ignored because the underlying surface UV domain is sampled. This is a geometric path generator and does not validate robot IK, collisions, kerf, feed, or controller code.

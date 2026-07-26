# Algorithm 02 — Automatic opposite-boundary pairing

Use this when the input is a single four-sided open surface/Brep and the definition should choose its synchronized rails automatically.

The component finds the two non-adjacent naked-edge pairs, matches curve directions, measures their synchronized span, and chooses the shorter pair by default. Set `PreferLong` to `true` to choose the other pair.

Inputs: `G`, `PreferLong`, `Count`, `Extension`, `Flip`, `Tolerance`.

Outputs: `Planes`, `WireLines`, `Ruled`, `RailA`, `RailB`, `MidPath`, `PairScores`, `Report`.

This is a geometric path generator. It does not validate robot IK, collisions, cell limits, wire kerf, feed rate, or controller code.

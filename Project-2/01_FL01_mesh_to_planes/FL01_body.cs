// ---------------------------------------------------------------------------
// FL-01  Mesh -> KUKA|prc planes pipeline
// PANE: "Script" / the main body  (Rhino 8 C# Script component, middle pane)
//
// Do NOT wrap this in a class or a RunScript(...) signature. The component
// builds those around it. The names below are the component's own parameters
// and are already in scope - they are not declared here.
//
// COMPONENT INPUTS  (name, type hint, access, optional?)   -- build in this order
//    0  geo          Mesh      item   required   the model to cut
//    1  axisMode     int       item   optional   0 long / 1 short / 2 X / 3 Y / 4 Z / 5 custom
//    2  customAxis   Vector3d  item   optional   only read when axisMode = 5
//    3  sections     int       item   optional   how many slices          (default 12)
//    4  samples      int       item   optional   points around each slice (default 32)
//    5  loopMode     int       item   optional   0 largest loop / 1 every loop
//    6  normalMode   int       item   optional   0 mesh normal / 1 radial from slice centre
//    7  flipApproach bool      item   optional   tick if the tool aims the wrong way
//    8  tiltDeg      double    item   optional   roll about the travel direction
//    9  rollMode     int       item   optional   0 rigid (wire) / 1 free spin (pen, bit)
//   10  leadLen      double    item   optional   lead-in / lead-out distance
//   11  minSpacing   double    item   optional   drop points closer than this
//   12  maxTurnDeg   double    item   optional   warn above this wrist turn (default 30)
//   13  seamGuide    Point3d   LIST   optional   pins where every loop starts (see below)
//
//        LIST access, not item, and deliberately so: an unwired Point3d item
//        arrives as (0,0,0), which is a perfectly valid point, so the component
//        could not tell "not supplied" from "the origin" and would silently pin
//        every loop to world zero. An unwired list arrives empty, which is
//        unambiguous. Only the first point is used.
//   14  fromFrame    Plane     item   optional   pair with toFrame to place the job in the cell
//   15  toFrame      Plane     item   optional
//   16  selfTest     bool      item   optional   run the orientation proof
//
//   ABOUT seamGuide
//   A smooth closed loop has no natural starting point - there is no corner to
//   call the beginning. Left empty, the component picks one from the loop's own
//   outline, which is stable when the section has a clear long direction and a
//   coin toss when the section is nearly symmetric (a circle, an ellipse).
//   Either way the CUT is the same; only the point the tool enters at moves.
//   Wire a Point to seamGuide and every loop starts at the point nearest to it,
//   which makes the whole result exactly reproducible. Do that when you are
//   re-cutting a part to match an earlier run.
//
// COMPONENT OUTPUTS  -- build in this order
//    0  out          (the component's own message stream, always there)
//    1  Planes       tree of Plane   -> this is the deliverable, feed KUKA|prc
//    2  Points       tree of Point3d -> preview only
//    3  MoveTypes    tree of int     -> 0 air move, 1 process move
//    4  Sections     list of Curve   -> the raw slices, preview only
//    5  SlicePlanes  list of Plane   -> where the model was cut, preview only
//    6  PartFrame    Plane           -> the model's own axes, preview this first
//    7  SliceAxis    Vector3d
//    8  Count        int
//    9  MaxTurn      double          -> worst wrist rotation between two targets, deg
//   10  Status       string          -> the one-line verdict
//   11  Log          string          -> the full story
//   12  SelfTest     string          -> the orientation proof, when selfTest is on
// ---------------------------------------------------------------------------

MeshPlanes.Options opt = new MeshPlanes.Options();

opt.AxisMode      = axisMode;
opt.CustomAxis    = customAxis;
opt.Sections      = sections > 0 ? sections : 12;
opt.Samples       = samples  > 0 ? samples  : 32;
opt.LoopMode      = loopMode;
opt.NormalMode    = normalMode;
opt.FlipApproach  = flipApproach;
opt.TiltDeg       = tiltDeg;
opt.RollMode      = rollMode;
opt.LeadLen       = leadLen;
opt.MinSpacing    = minSpacing;
opt.MaxTurnDeg    = maxTurnDeg > 0 ? maxTurnDeg : 30.0;
opt.SeamGuide     = (seamGuide != null && seamGuide.Count > 0)
                    ? seamGuide[0] : Point3d.Unset;
opt.FromFrame     = fromFrame;
opt.ToFrame       = toFrame;
opt.SelfTest      = selfTest;
opt.SelfTestCount = 8;

MeshPlanes.Result res = MeshPlanes.Generate(geo, opt);

Planes      = res.Planes;
Points      = res.Points;
MoveTypes   = res.MoveTypes;
Sections    = res.Sections;
SlicePlanes = res.SlicePlanes;
PartFrame   = res.PartFrame;
SliceAxis   = res.SliceAxis;
Count       = res.Count;
MaxTurn     = res.MaxTurnDeg;
Status      = res.Status;
Log         = res.Log;
SelfTest    = res.SelfTest;

// Put the verdict on the component itself, so a bad solve is visible on the
// canvas without opening a panel.
if (Component != null)
{
  if (!string.IsNullOrEmpty(res.Status) && res.Status.StartsWith("FAIL"))
    Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, res.Status);

  foreach (string w in res.Warnings)
    Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, w);

  if (selfTest && !string.IsNullOrEmpty(res.SelfTest) && !MeshPlanes.SelfTestPassed(res.SelfTest))
    Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
      "Orientation self-test did not pass. Read the SelfTest output.");
}

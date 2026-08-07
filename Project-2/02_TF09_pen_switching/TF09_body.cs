// ---------------------------------------------------------------------------
// TF-09  Pen-switching loop - Grasshopper front end
// PANE: "Script" / the main body  (Rhino 8 C# Script component, middle pane)
//
// COMPONENT INPUTS  (name, type hint, access, optional?)   -- build in this order
//    0  curves       Curve         list   REQUIRED   the strokes to draw
//    1  penIds       int           list   optional   one pen number per curve
//    2  drawPlane    Plane         item   REQUIRED   the plane of the paper
//    3  drawGeo      Geometry Base item   optional   curved sheet to follow instead
//    4  slotPlanes   Plane         list   REQUIRED   one plane per magazine slot
//    5  slotTools    int           list   optional   KUKA TOOL number per slot
//    6  homePlane    Plane         item   optional   where the arm starts and ends
//    7  leadIn       double        item   optional   pen-down approach, mm
//    8  leadOut      double        item   optional   pen-up retract, mm
//    9  hover        double        item   optional   travel clearance, mm
//   10  tiltDeg      double        item   optional   pen lean
//   11  resolution   double        item   optional   curve -> polyline chord tol, mm
//   12  optimize     bool          item   REQUIRED   reorder + flip for least travel
//   13  groupByPen   bool          item   REQUIRED   finish a pen before changing it
//   14  startIndex   int           item   optional   resume from this stroke
//   15  liveRun      bool          item   optional   OFF = dry run. See the note below.
//   16  feedDraw     double        item   optional   mm/s on the paper
//   17  feedRapid    double        item   optional   mm/s in the air
//   18  swapSeconds  double        item   optional   measured cost of one swap
//   19  jobName      string        item   optional   name of the generated .src
//   20  selfTest     bool          item   optional   run the orientation proof
//   21  pressDepth   double        item   optional   Z press offset, mm (default 3)
//   22  baseIndex    int           item   optional   KUKA BASE[] of the paper (default 1)
//   23  magBaseIndex int           item   optional   KUKA BASE[] of the magazine (default 1)
//
//   21-23 are appended rather than inserted so that a canvas built against the
//   earlier input list keeps working - the existing numbering does not move.
//
//   DEFAULTS come from the "Key parameters" table in
//   end-effectors/01-drawing/README.md: draw 100 mm/s, air 500 mm/s,
//   Z press offset 3 mm, Z lift between strokes 30 mm. Leave a slider unwired
//   and you get the documented value.
//
//   WHY liveRun AND NOT dryRun
//   An unwired boolean in Grasshopper is false. If the input were called
//   dryRun, forgetting to wire it would silently produce a program that puts a
//   real pen on real paper. Named this way round, forgetting it gives you a dry
//   run. The unsafe option has to be asked for.
//
// COMPONENT OUTPUTS  -- build in this order
//    0  out            (the component's own message stream)
//    1  Targets        tree of Plane   -> branch per stroke, feed KUKA|prc
//    2  MoveTypes      tree of int     -> 0 air, 1 drawing, 2 trip to the magazine
//    3  Flat           list of Plane   -> the whole program in order, for the simulator
//    4  FlatMoves      list of int
//    5  PenSequence    list of int     -> which pen each ordered stroke uses
//    6  StrokeOrder    list of int     -> original curve index, in the new order
//    7  OrderedCurves  list of Curve   -> preview, already flipped
//    8  TravelMoves    list of Line    -> the air moves, preview
//    9  SwapLog        list of string  -> human readable swap list
//   10  SwapCount      int
//   11  TravelDist     double          -> mm in the air
//   12  DrawDist       double          -> mm on the paper
//   13  CycleTime      double          -> seconds, including swaps
//   14  KRL            string          -> the generated .src, panel this and save it
//   15  Status         string
//   16  Log            string
//   17  SelfTest       string
// ---------------------------------------------------------------------------

PenJob.Options opt = new PenJob.Options();

opt.DrawPlane   = drawPlane;
opt.DrawGeo     = drawGeo;
opt.SlotPlanes  = slotPlanes != null ? new List<Plane>(slotPlanes) : new List<Plane>();
opt.SlotTools   = slotTools  != null ? new List<int>(slotTools)    : new List<int>();
opt.HomePlane   = homePlane;

opt.LeadIn      = leadIn      > 0 ? leadIn      : 5.0;
opt.LeadOut     = leadOut     > 0 ? leadOut     : 5.0;
opt.Hover       = hover       > 0 ? hover       : 30.0;   // README: Z lift 30 mm
opt.TiltDeg     = tiltDeg;
opt.Resolution  = resolution  > 0 ? resolution  : 0.5;

// README: Z press offset 3 mm. Same idiom as every other number here - an
// unwired input is 0 and gets the documented default, because a spring-loaded
// holder always wants some press and "exactly zero" is not a real ask.
opt.PressDepth  = pressDepth  > 0 ? pressDepth  : 3.0;

opt.BaseIndex    = baseIndex    > 0 ? baseIndex    : 1;
opt.MagBaseIndex = magBaseIndex > 0 ? magBaseIndex : 1;

opt.Optimize    = optimize;
opt.GroupByPen  = groupByPen;
opt.StartIndex  = startIndex;
opt.DryRun      = !liveRun;

opt.FeedDraw    = feedDraw    > 0 ? feedDraw    : 100.0;  // README: draw 100 mm/s
opt.FeedRapid   = feedRapid   > 0 ? feedRapid   : 500.0;  // README: air  500 mm/s
opt.SwapSeconds = swapSeconds > 0 ? swapSeconds : 25.0;

opt.JobName     = string.IsNullOrEmpty(jobName) ? "DRAW_JOB" : jobName;
opt.SelfTest    = selfTest;

PenJob.Result res = PenJob.Build(curves != null ? new List<Curve>(curves) : null,
                                 penIds != null ? new List<int>(penIds)   : null,
                                 opt);

Targets       = res.Targets;
MoveTypes     = res.MoveTypes;
Flat          = res.Flat;
FlatMoves     = res.FlatMoves;
PenSequence   = res.PenSequence;
StrokeOrder   = res.StrokeOrder;
OrderedCurves = res.OrderedCurves;
TravelMoves   = res.TravelMoves;
SwapLog       = res.SwapLog;
SwapCount     = res.SwapCount;
TravelDist    = res.TravelDist;
DrawDist      = res.DrawDist;
CycleTime     = res.CycleTime;
KRL           = res.Krl;
Status        = res.Status;
Log           = res.Log;
SelfTest      = res.SelfTest;

if (Component != null)
{
  if (!string.IsNullOrEmpty(res.Status) && res.Status.StartsWith("FAIL"))
    Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, res.Status);

  foreach (string w in res.Warnings)
    Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, w);

  if (liveRun)
    Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
      "LIVE RUN. This KRL will fire the clamp and put the pen on the paper. " +
      "Do not load it until the dry run has passed in T1.");

  if (selfTest && !string.IsNullOrEmpty(res.SelfTest) && !PenJob.SelfTestPassed(res.SelfTest))
    Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
      "Orientation self-test did not pass. Read the SelfTest output.");
}

// ---------------------------------------------------------------------------
// TOOLPATH SIMULATOR - shared by FL-01 and TF-09
// PANE: "Script" / the main body  (Rhino 8 C# Script component, middle pane)
//
// COMPONENT INPUTS  (name, type hint, access, optional?)   -- build in this order
//    0  targets        Plane   list   REQUIRED   the flat program order
//    1  moveTypes      int     list   optional   0 air, 1 process, 2 magazine
//    2  feedProcess    double  item   optional   mm/s on the work   (default 50)
//    3  feedRapid      double  item   optional   mm/s in the air    (default 250)
//    4  dwellSeconds   double  item   optional   pause at a magazine stop
//    5  t              double  item   REQUIRED   0..1, wire a slider - this is play
//    6  toolLength     double  item   optional   for the preview    (default 100)
//    7  toolRadius     double  item   optional   for the preview    (default 4)
//    8  turnRateLimit  double  item   optional   deg per mm before it is flagged
//
// COMPONENT OUTPUTS  -- build in this order
//    0  out          (the component's own message stream)
//    1  TCP          Plane      -> where the tool is right now
//    2  ToolAxis     Line
//    3  ToolBody     Mesh       -> shade this, it reads far better than a line
//    4  Trail        Polyline   -> where it has been
//    5  Remaining    Polyline   -> where it still has to go
//    6  Time         double     -> seconds elapsed at this slider position
//    7  CycleTime    double     -> seconds for the whole job
//    8  Index        int        -> which move it is on
//    9  Feed         double     -> mm/s commanded right now
//   10  MoveLabel    string
//   11  HotSpots     list<int>  -> segments that rotate too fast, wire to a panel
//   12  MaxTurn      double     -> worst single-move rotation, deg
//   13  MaxTurnRate  double     -> worst rotation per mm, deg/mm
//   14  Status       string
//   15  Log          string
//
// HOW TO PLAY IT
//   Wire a Number Slider 0.000 to 1.000 into t and drag it. For a hands-off
//   playback, feed t from a native Grasshopper timer: a Boolean Toggle into a
//   Grasshopper Timer component driving a counter, or simply animate the slider
//   (right-click the slider -> Animate) which also writes the frames to disk
//   for the progress screenshots.
// ---------------------------------------------------------------------------

PathSim.Result res = PathSim.Run(
  targets != null ? new List<Plane>(targets) : null,
  moveTypes != null ? new List<int>(moveTypes) : null,
  feedProcess,
  feedRapid,
  dwellSeconds,
  t,
  toolLength,
  toolRadius,
  turnRateLimit);

TCP         = res.Tcp;
ToolAxis    = res.ToolAxis;
ToolBody    = res.ToolBody;
Trail       = res.Trail;
Remaining   = res.Remaining;
Time        = res.Time;
CycleTime   = res.CycleTime;
Index       = res.Index;
Feed        = res.Feed;
MoveLabel   = res.MoveLabel;
HotSpots    = res.HotSpots;
MaxTurn     = res.MaxTurnDeg;
MaxTurnRate = res.MaxTurnRate;
Status      = res.Status;
Log         = res.Log;

if (Component != null)
{
  if (!string.IsNullOrEmpty(res.Status) && res.Status.StartsWith("FAIL"))
    Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, res.Status);
  else if (res.HotSpots.Count > 0)
    Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
      res.HotSpots.Count + " segment(s) rotate faster than the limit. Wire HotSpots to a panel.");
}

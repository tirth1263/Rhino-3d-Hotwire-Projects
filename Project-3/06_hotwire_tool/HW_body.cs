// ---------------------------------------------------------------------------
// HOTWIRE TOOL - TCP frame from KUKA A/B/C, plus plane flipping
// PANE: "Script" / the main body  (Rhino 8 C# Script component, middle pane)
//
// COMPONENT INPUTS  (name, type hint, access, optional?)   -- build in this order
//    0  toolX        double   item   optional   TCP offset from the flange, mm
//    1  toolY        double   item   optional
//    2  toolZ        double   item   optional   default 422 - the dialog's Tool Z
//    3  toolA        double   item   REQUIRED   the dialog's Tool A  (-90)
//    4  toolB        double   item   REQUIRED   the dialog's Tool B  (-90)
//    5  toolC        double   item   REQUIRED   the dialog's Tool C  (0)
//    6  wireSpan     double   item   optional   default 415.8 (modelled span)
//    7  wireAxis     int      item   REQUIRED   0 X / 1 Y / 2 Z.  Wire lies on Z.
//
//   WHY 3, 4, 5 AND 7 ARE NOT OPTIONAL
//   Every other number here treats an unwired input - which Grasshopper reads
//   as 0 - as "use the documented default". That trick cannot work for the
//   angles or the axis index, because 0 is a perfectly good value for all four:
//   A 0 / B 0 / C 0 is the identity orientation and wireAxis 0 is the X axis.
//   So rather than guess, they are required, and Grasshopper shows "no data"
//   until they are wired. That is the right prompt.
//    8  flipToolZ    bool     item   optional   send the tool axis the other way
//    9  flipToolSpin bool     item   optional   half turn about the tool axis
//   10  targets      Plane    LIST   optional   the toolpath planes to re-orient
//   11  flipZ        bool     item   optional   approach from the other side
//   12  flipX        bool     item   optional   reverse travel
//   13  tiltDeg      double   item   optional   rotate about travel (zMode 0 only)
//   14  spinDeg      double   item   optional   free spin about the approach
//   15  frameMode    int      item   REQUIRED   0 keep / 1 CUT / 2 wire-on-cardinal
//   16  cardinal     int      item   REQUIRED   0 AUTO / 1 +X / 2 -X / 3 +Y / 4 -Y
//   17  robotBase    Plane    item   optional   where the robot stands (default world XY)
//   18  reachMax     double   item   optional   flange reach, mm (default 1101)
//   19  reachMin     double   item   optional   inner limit, mm (default 460)
//   20  cutOrient    int      item   REQUIRED   0 vertical / 1 across / 2 along / 3 cardinal
//   21  zToRobot     bool     item   optional   turn frame Z back towards the robot
//
//   cutOrient IS THE ONE TO REACH FOR PER PART
//   It says what the WIRE should do, and different parts want different things:
//
//     0 VERTICAL   wire straight up and down. Best on a part that stands up -
//                  the wire spans the full height and sweeps round the profile,
//                  so each pass cuts the whole flank instead of nibbling it.
//     1 ACROSS     wire across the travel and tangent to the surface. Same
//                  thing whenever the slices are horizontal, but derived from
//                  the surface, so it follows a part lying down or tilted.
//     2 ALONG      wire along the travel. It slides down its own kerf.
//     3 CARDINAL   wire along the approach. It goes in end-on.
//
//   2 and 3 are the wrong answers, kept so you can see what wrong looks like.
//
//   zToRobot AND WHY IT MAY REFUSE
//   With the tool taught A -90 / B -90 / C 0 the wire lies ON tool Z, so Z and
//   the wire are the same axis. Stand the wire vertical and Z is vertical too -
//   it cannot also point at the robot. The component says so rather than
//   quietly ignoring you. If you want Z to be a true approach axis, teach the
//   tool A 0 / B 0 / C 0 and set wireAxis = 1: Z is then the flange axis and
//   the wire sits on Y, and the two stop fighting.
//
//   THE SWITCH, AND WHY IT IS TWO SWITCHES
//   FL-01 hands out planes whose Z is RADIAL - it swings right around the
//   loop, so somewhere in every pass it points back at the robot and asks the
//   arm to reach through the part. That is what was wrong.
//
//   `cardinal` picks one direction instead: AUTO reads where the part sits
//   relative to the robot - in front is +X, behind is -X, either side is
//   +/-Y - so moving the part re-orients the job by itself. 1..4 force it.
//
//   `frameMode` decides what that direction is applied TO, and the two
//   choices are not interchangeable:
//
//     1 CUT   the cardinal drives the ARM (frame X). The wire is laid across
//             the travel and tangent to the surface. Both constraints met.
//     2 WIRE  the cardinal drives the WIRE (frame Z), literally. For this
//             tool that points the wire end-on into the material.
//
//   They can both be satisfied because reach acts on X - the TCP is 422 mm
//   along tool X, so the flange lands 422 mm back along the target's X - and
//   cutting acts on Z, where the wire is. Different axes, no conflict.
//
//   tiltDeg only applies when frameMode = 0, because a rebuilt frame wins.
//
//   THE DEFAULTS ARE THE REAL TOOL
//   toolZ / toolA / toolB / toolC default to the numbers in the KUKA CUSTOM
//   TOOL dialog, and wireSpan to the span measured in Hotwire_2.1.3dm. Leave
//   every input unwired and you get the lab's actual hotwire.
//
//   WHY THE FLIPS ARE INPUTS AND NOT CODE
//   Which way a frame should face is not something you reason out, it is
//   something you look at. Toggling should cost one click, and the Log output
//   tells you whether the click helped - it reports whether the wire ends up
//   across the travel (cutting), along the travel (sliding down its own kerf),
//   or along the approach (stabbing in end-on).
//
// COMPONENT OUTPUTS  -- build in this order
//    0  out            (the component's own message stream)
//    1  ToolPlane      Plane        -> KUKA|prc "Custom Tool: Plane", TOOL PLANE
//    2  ToolAbc        string       -> the X/Y/Z/A/B/C read back, for the pendant
//    3  Targets        list<Plane>  -> the flipped targets, feed the LIN component
//    4  WireLines      list<Line>   -> where the wire actually is, preview this
//    5  WireEndA       list<Plane>  -> TOOL[5]
//    6  WireEndB       list<Plane>  -> TOOL[6]
//    7  FlangePts      list<Point3d>-> where the WRIST has to be. Preview this
//                                      against the robot to see reach at a glance.
//    8  Approach       Vector3d     -> the direction that was chosen
//    9  Status         string
//   10  Log            string
// ---------------------------------------------------------------------------

HotwireTool.Options opt = new HotwireTool.Options();

opt.ToolX = toolX;
opt.ToolY = toolY;

// toolZ and wireSpan can use the usual idiom: an unwired input reads 0, and a
// TCP 0 mm off the flange or a wire 0 mm long are not real asks, so 0 safely
// means "give me the documented value".
opt.ToolZ    = toolZ    > 0 ? toolZ    : 422.0;
opt.WireSpan = wireSpan > 0 ? wireSpan : 415.8;

// The angles and the axis index cannot do that - 0 is a real value for all
// four - so they are required inputs and are taken exactly as given.
opt.ToolA    = toolA;
opt.ToolB    = toolB;
opt.ToolC    = toolC;
opt.WireAxis = wireAxis;

opt.FlipToolZ    = flipToolZ;
opt.FlipToolSpin = flipToolSpin;

opt.FlipZ   = flipZ;
opt.FlipX   = flipX;
opt.TiltDeg = tiltDeg;
opt.SpinDeg = spinDeg;

opt.FrameMode = frameMode;
opt.Cardinal  = cardinal;
opt.RobotBase = robotBase.IsValid ? robotBase : Plane.WorldXY;
opt.ReachMax  = reachMax > 0 ? reachMax : 1101.0;
opt.ReachMin  = reachMin > 0 ? reachMin :  460.0;

opt.CutOrientation = cutOrient;
opt.ZToRobot       = zToRobot;

HotwireTool.Result res = HotwireTool.Build(
  targets != null ? new List<Plane>(targets) : null, opt);

ToolPlane = res.ToolPlane;
ToolAbc   = res.ToolAbc;
Targets   = res.Targets;
WireLines = res.WireLines;
WireEndA  = res.WireEndA;
WireEndB  = res.WireEndB;
FlangePts = res.FlangePts;
Approach  = res.ApproachUsed;
Status    = res.Status;
Log       = res.Log;

if (Component != null)
{
  foreach (string w in res.Warnings)
    Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, w);
}

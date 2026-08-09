// ---------------------------------------------------------------------------
// PEN TOOL - TCP frame from KUKA A/B/C, plus the drawing board's orientation
// PANE: "Script" / the main body  (Rhino 8 C# Script component, middle pane)
//
// COMPONENT INPUTS  (name, type hint, access, optional?)   -- build in this order
//    0  toolX        double    item   optional   TCP offset from the flange, mm
//    1  toolY        double    item   optional
//    2  toolZ        double    item   optional   default 227.8 - the pen's length
//    3  toolA        double    item   REQUIRED   the CUSTOM TOOL dialog's Tool A (0)
//    4  toolB        double    item   REQUIRED   the dialog's Tool B (0)
//    5  toolC        double    item   REQUIRED   the dialog's Tool C (0)
//    6  penAxis      int       item   REQUIRED   0 X / 1 Y / 2 Z.  Pen lies on Z.
//
//   WHY 3, 4, 5 AND 6 ARE NOT OPTIONAL
//   Every other number here treats an unwired input - which Grasshopper reads
//   as 0 - as "use the documented default". That cannot work for the angles or
//   the axis index, because 0 is a perfectly good value for all four: A 0 /
//   B 0 / C 0 is the identity orientation, and it happens to be exactly the
//   right answer for this tool. Defaulting them would hide that rather than
//   state it. So they are required and Grasshopper shows "no data" until they
//   are wired, which is the right prompt.
//
//    7  boardOrigin  Point3d   item   optional   centre of the sheet
//    8  boardW       double    item   optional   across the sheet, mm (280)
//    9  boardH       double    item   optional   up the sheet, mm (210)
//   10  boardOrient  int       item   REQUIRED   0 VERTICAL / 1 FLAT / 2 TILTED / 3 AWAY
//   11  cardinal     int       item   REQUIRED   0 AUTO / 1 +X / 2 -X / 3 +Y / 4 -Y
//   12  leanDeg      double    item   optional   only bites when boardOrient = 2
//   13  spinDeg      double    item   optional   roll the sheet in its own plane
//   14  flipBoardZ   bool      item   optional   turn the sheet round by hand
//   15  zToRobot     bool      item   optional   insist the sheet faces the robot
//   16  robotBase    Plane     item   optional   where the robot stands (world XY)
//   17  reachMax     double    item   optional   flange reach, mm (1101)
//   18  reachMin     double    item   optional   inner limit, mm (460)
//   19  grid         int       item   optional   reach samples per side (5)
//   20  penLeanDeg   double    item   optional   pen lean off the paper (20)
//
//   penLeanDeg IS NOT COSMETIC, AND IT IS THE THING THAT BIT
//   It goes straight through to TF-09's tiltDeg, so this component owns the
//   whole orientation story and can check its own advice.
//
//   A sheet standing square-on to the robot has its normal pointing back down
//   the arm's reach line. A pen held perpendicular to that paper is therefore
//   aimed straight at the shoulder, the wrist has to go flat to manage it,
//   axis 4 lines up with axis 6, and the pose is singular no matter how much
//   room there is around it. Measured against prc, board at 900 / 0 / 450:
//
//       lean  0,  5, 10       UNREACHABLE
//       lean 15, 20, 25, 30   clean
//       lean 40               UNREACHABLE - too far the other way
//       lean -20              clean
//
//   Every one of those targets was inside the reach ring. Nothing about the
//   position was wrong; only the attitude. The component warns rather than
//   silently defaulting, because the band is narrow and not obvious.
//
//   boardOrient IS THE ONE TO REACH FOR PER JOB
//   It says how the WORK is hung, and it is the switch because with a pen the
//   TOOL has only one sensible attitude - straight into the paper.
//
//     0 VERTICAL  the sheet stands up on an easel and faces the robot. Its
//                 own blue Z runs back at the arm; the pen comes in the other
//                 way, so the flange sits between the robot and the work.
//                 This is the one the cell actually uses.
//     1 FLAT      the sheet lies on a table, normal straight up. Works, but
//                 its normal is vertical, so nothing about it faces the robot
//                 and zToRobot will say so.
//     2 TILTED    a drafting table. leanDeg 0 is FLAT and 90 is VERTICAL, so
//                 this is the continuum the other two are the ends of.
//     3 AWAY      the sheet turned to face away from the robot. Kept so you
//                 can see what wrong looks like - the arm has to reach round
//                 the back of its own work and the reach check says so.
//
//   THE FRAME CONVENTION, AND WHY IT MATTERS HERE
//   All four modes name their axes the same way:
//       X  across the sheet, left to right
//       Y  up the sheet
//       Z  out of the sheet, towards the reader
//   so the artwork is authored once in world XY and oriented onto whichever
//   board you pick. The drawing does not know how the board is hung.
//
//   WHY THIS COMPONENT IS UPSTREAM OF TF-09
//   TF-09 needs a draw plane before it can build a single target, so a
//   component that derived the board FROM TF-09's targets would be a cycle,
//   and Grasshopper will not run one. The reach check therefore samples a grid
//   over the sheet rather than the finished toolpath - which is the same set
//   of points, since every drawing target lies on the sheet.
//
//   THE DEFAULTS ARE THE REAL TOOL
//   toolZ defaults to 227.8 mm, measured in the lab's own Pen_Tool Rev 008.3dm:
//   mount plate 15 + body 50 + carriage 69, with the pen protruding to a nib
//   227.8 mm from the mounting face. See PN_README.md for the full table.
//
// COMPONENT OUTPUTS  -- build in this order
//    0  out            (the component's own message stream)
//    1  DrawPlane      Plane         -> TF-09's drawPlane, AND the Orient target
//                                       that stands the artwork up with it
//    2  ToolPlane      Plane         -> KUKA|prc "Custom Tool: Plane", TOOL PLANE
//    3  ToolAbc        string        -> the X/Y/Z/A/B/C read back, for the pendant
//    4  PenLean        double        -> wire into TF-09's tiltDeg
//    5  Board          Curve         -> the sheet outline, preview this
//    6  Corners        list<Point3d>
//    7  FlangePts      list<Point3d> -> where the WRIST has to be. Preview this
//                                       against the robot to see reach at a glance.
//    8  PenLines       list<Line>    -> flange to nib, the tool drawn in place
//    9  Approach       Vector3d      -> the cardinal that was chosen
//   10  Status         string
//   11  Log            string
// ---------------------------------------------------------------------------

PenTool.Options opt = new PenTool.Options();

opt.ToolX = toolX;
opt.ToolY = toolY;

// toolZ can use the usual idiom: an unwired input reads 0, and a TCP 0 mm off
// the flange is not a real ask, so 0 safely means "give me the documented one".
opt.ToolZ = toolZ > 0 ? toolZ : 227.8;

// The angles and the axis index cannot do that - 0 is a real value for all
// four - so they are required inputs and are taken exactly as given.
opt.ToolA   = toolA;
opt.ToolB   = toolB;
opt.ToolC   = toolC;
opt.PenAxis = penAxis;

opt.BoardOrigin = boardOrigin.IsValid ? boardOrigin : new Point3d(900, 0, 450);
opt.BoardW      = boardW > 0 ? boardW : 280.0;
opt.BoardH      = boardH > 0 ? boardH : 210.0;

opt.BoardOrient = boardOrient;
opt.Cardinal    = cardinal;
opt.LeanDeg     = leanDeg;
opt.SpinDeg     = spinDeg;
opt.FlipBoardZ  = flipBoardZ;
opt.ZToRobot    = zToRobot;

opt.RobotBase = robotBase.IsValid ? robotBase : Plane.WorldXY;
opt.ReachMax  = reachMax > 0 ? reachMax : 1101.0;
opt.ReachMin  = reachMin > 0 ? reachMin :  460.0;
opt.Grid      = grid     > 1 ? grid     :  5;

// penLeanDeg is the one number where an unwired 0 is BOTH a real value and the
// dangerous one, so it cannot use the usual "0 means default" idiom. Left
// unwired it reads 0, which is exactly the flat-wrist pose prc refuses - so 0
// is taken literally and the component WARNS about it rather than silently
// substituting 20 and hiding the lesson.
opt.PenLeanDeg = penLeanDeg;

PenTool.Result res = PenTool.Build(opt);

DrawPlane = res.DrawPlane;
ToolPlane = res.ToolPlane;
ToolAbc   = res.ToolAbc;
PenLean   = res.PenLean;
Board     = res.Board;
Corners   = res.Corners;
FlangePts = res.FlangePts;
PenLines  = res.PenLines;
Approach  = res.ApproachUsed;
Status    = res.Status;
Log       = res.Log;

if (Component != null)
{
  foreach (string w in res.Warnings)
    Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, w);
}

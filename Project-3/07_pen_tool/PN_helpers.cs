// ---------------------------------------------------------------------------
// PEN TOOL - TCP frame from KUKA A/B/C, plus the drawing board's orientation
// PANE: "Members" / additional code  (Rhino 8 C# Script component, bottom pane)
//
// WHAT THIS IS FOR
//   The same two jobs the HOTWIRE component does for FL-01, done for TF-09:
//
//   1. Turn the numbers typed into the KUKA CUSTOM TOOL dialog - X, Y, Z, A, B,
//      C - into the Plane that KUKA|prc's "Custom Tool: Plane" wants.
//
//   2. Decide which way the WORK faces, with a switch rather than a code edit,
//      and prove the arm can reach it before anything moves.
//
//   The difference from the hotwire is which end the switch acts on. A hotwire
//   cuts with a LINE, so the question was how to lay the tool. A pen draws with
//   a POINT, so the tool has only one sensible attitude - straight into the
//   paper - and the interesting question moves to the BOARD. That is why this
//   component is upstream of TF-09 and the hotwire one is downstream of FL-01.
//
//   It also has to be upstream. TF-09 needs a draw plane before it can build a
//   single target, so a component that produced the draw plane FROM TF-09's
//   targets would be a cycle, and Grasshopper will not run a cycle. The reach
//   check therefore measures a grid over the board rather than the finished
//   toolpath - which is the same set of points, since every drawing target
//   lies on the board.
//
// WHY THE BOARD STANDS UP AND FACES THE ROBOT
//   With the pen on tool Z, the flange sits one whole tool length BACK along
//   the target's Z. The target's Z runs into the paper. So the flange is always
//   on the near side of the sheet, between the robot and the work - which is
//   the only arrangement where the arm can reach past its own tool.
//
//   Read the other way round: the BOARD's own normal is what points back at
//   the robot. That is the blue axis you see on the board in Rhino, and
//   boardOrient 0 is what puts it there.
//
// MEASURED, NOT ASSUMED
//   The tool dimensions come from the lab's own
//     End-Effector Development/Drawing/Pen_Tool Rev 008.3dm
//   read directly. See PN_README.md for the measurement table and for the one
//   thing in it that is a DEFINITION rather than a measurement - where the
//   flange face sits, which that file does not record because it is a bench
//   layout rather than a flange-referenced model.
// ---------------------------------------------------------------------------

public static class PenTool
{
  private const double EPS = 1e-12;

  // =========================================================================
  // 0.  CONTAINERS
  // =========================================================================

  public class Options
  {
    // --- the TCP, straight off the CUSTOM TOOL dialog ---
    //
    // 227.8 is the pen tool's own length along its axis, measured in
    // Pen_Tool Rev 008.3dm: mount plate 15 + body 50 + carriage 69, with the
    // pen protruding to a tip 227.8 mm from the mounting face.
    //
    // A/B/C are all zero because the pen runs straight out along the flange
    // axis. Unlike the hotwire - whose wire is a crossbar and needs
    // A -90 / B -90 / C 0 to describe it - a pen has nothing to twist.
    public double ToolX = 0.0;
    public double ToolY = 0.0;
    public double ToolZ = 227.8;
    public double ToolA = 0.0;
    public double ToolB = 0.0;
    public double ToolC = 0.0;

    // Which axis of the tool frame the pen runs along. 2 = Z, which is what
    // A 0 / B 0 / C 0 gives, and what TF-09's own frame convention expects:
    // the target's Z runs down the pen, from the holder into the paper.
    public int PenAxis = 2;

    // --- where the work is ---
    public Point3d BoardOrigin = new Point3d(900, 0, 450);
    public double BoardW = 280.0;   // across the sheet
    public double BoardH = 210.0;   // up the sheet

    // --- HOW THE BOARD IS HUNG ------------------------------------------
    //
    // 0 VERTICAL  standing up, normal horizontal, facing back at the robot.
    //             The sheet is on an easel and the pen draws on it sideways.
    // 1 FLAT      lying on a table, normal straight up. The old behaviour.
    // 2 TILTED    a drafting table: leanDeg from flat towards vertical.
    //             0 is FLAT and 90 is VERTICAL, so this is the continuum the
    //             other two are the ends of.
    // 3 AWAY      normal along the cardinal, i.e. the sheet turned to face
    //             AWAY from the robot. Kept because being able to see the
    //             wrong one is how you come to trust the right one - the arm
    //             has to reach round the back of its own work, and the reach
    //             check says so.
    //
    // VERTICAL is the default because it is what the cell actually does, and
    // because it is the only one of the four where the board's own Z points
    // at the robot.
    public int BoardOrient = 0;

    // 0 AUTO  from where the board sits relative to the robot
    // 1..4    +X / -X / +Y / -Y, forced by hand
    //
    // AUTO reads the board's own position: in front of the robot is +X,
    // behind is -X, either side is +/-Y. Drag the board sliders and the board
    // turns to keep facing the arm.
    public int Cardinal = 0;

    public double LeanDeg = 45.0;   // only bites when BoardOrient = 2
    public double SpinDeg = 0.0;    // roll the sheet in its own plane
    public bool FlipBoardZ = false; // turn the sheet round

    // --- HOW FAR THE PEN LEANS OFF THE PAPER --------------------------
    //
    // Wired straight through to TF-09's tiltDeg, so one component owns the
    // whole orientation story and can check its own advice.
    //
    // 20 degrees, and it is NOT cosmetic. A sheet that stands up facing the
    // robot is square-on to the arm, so a pen held perpendicular to it points
    // straight back down the arm's own reach line - the wrist goes flat and
    // axis 4 lines up with axis 6. Measured against KUKA|prc, board at
    // 900 / 0 / 450:
    //
    //     lean  0  5  10        UNREACHABLE
    //     lean 15 20 25 30      clean
    //     lean 40               UNREACHABLE  (too far the other way)
    //     lean -20              clean
    //
    // A ten-degree lean is not enough and forty is too much; the band is
    // roughly 15 to 30 either side of square, and 20 sits in the middle of it.
    //
    // It is also what a hand does. Nobody draws with the pen dead
    // perpendicular to the paper.
    public double PenLeanDeg = 20.0;

    // Insist the board's normal faces the robot, and SAY SO when it cannot.
    //
    // On boardOrient 0 it is already true and this changes nothing. On 1 FLAT
    // the normal is vertical and there is no horizontal component to turn, so
    // it refuses and explains. On 3 AWAY it flips the sheet back, and reports
    // that it overrode you.
    public bool ZToRobot = true;

    public Plane RobotBase = Plane.WorldXY;

    // The envelope is a RING, not a maximum.
    //
    // 1101 mm is the KR6-10 R1100-2's flange reach. The inner wall is a
    // property of the ROBOT, not of the tool - the arm cannot fold the flange
    // into its own body - so the 460 mm measured for the hotwire cell applies
    // here too, and is the default.
    //
    // What the tool changes is how far the WORK can be for a given flange
    // position. The pen reaches 227.8 mm ahead of the wrist against the
    // hotwire's 422, so the whole usable band for the WORK sits about 194 mm
    // closer in. That is the number to remember when swapping tools.
    public double ReachMax = 1101.0;
    public double ReachMin = 460.0;

    // How finely the board is sampled for the reach check. 5 x 5 is enough:
    // the flange positions are a rigid copy of the board, so the extremes are
    // at the corners and the grid is only there to catch the inner wall.
    public int Grid = 5;
  }

  public class Result
  {
    public Plane DrawPlane = Plane.Unset;
    public Plane ToolPlane = Plane.Unset;
    public string ToolAbc = "";
    public double PenLean = 0.0;
    public Curve Board = null;
    public List<Point3d> Corners = new List<Point3d>();
    public List<Point3d> FlangePts = new List<Point3d>();
    public List<Line> PenLines = new List<Line>();
    public Vector3d ApproachUsed = Vector3d.Unset;
    public int OutOfReach = 0;
    public double ReachNear = 0.0, ReachFar = 0.0;
    public string Status = "";
    public string Log = "";
    public List<string> Warnings = new List<string>();
  }

  // =========================================================================
  // 1.  ENTRY POINT
  // =========================================================================

  public static Result Build(Options o)
  {
    Result r = new Result();
    StringBuilder log = new StringBuilder();

    if (o == null) o = new Options();
    if (o.PenAxis < 0 || o.PenAxis > 2) o.PenAxis = 2;
    if (o.BoardW <= 0.0) o.BoardW = 280.0;
    if (o.BoardH <= 0.0) o.BoardH = 210.0;
    if (o.Grid < 2) o.Grid = 2;

    // ---- 1. the tool frame -------------------------------------------------
    Plane tool = AbcToPlane(new Point3d(o.ToolX, o.ToolY, o.ToolZ), o.ToolA, o.ToolB, o.ToolC);
    r.ToolPlane = tool;
    r.PenLean = o.PenLeanDeg;

    double a, b, c;
    PlaneToAbc(tool, out a, out b, out c);
    r.ToolAbc = "X " + F(o.ToolX) + "   Y " + F(o.ToolY) + "   Z " + F(o.ToolZ) +
                "   A " + F(a) + "   B " + F(b) + "   C " + F(c);

    log.AppendLine("TOOL     " + r.ToolAbc);
    log.AppendLine("         pen runs along the tool's " + AxisName(o.PenAxis) +
                   " axis " + V(AxisOf(tool, o.PenAxis)));
    log.AppendLine("         TCP is the NIB. " + F(o.ToolZ) + " mm from the flange face, so the");
    log.AppendLine("         flange always sits that far BACK along the target's Z - between");
    log.AppendLine("         the robot and the paper, which is the only way it reaches.");

    // ---- 2. which way does the board face ---------------------------------
    Vector3d card = (o.Cardinal == 0) ? AutoCardinal(o.BoardOrigin, o.RobotBase)
                                      : CardinalOf(o.Cardinal);
    r.ApproachUsed = card;
    log.AppendLine("CARDINAL " + (o.Cardinal == 0 ? "AUTO -> " : "forced ") + V(card) +
                   "   (" + CardinalName(card) + ")");
    log.AppendLine("BOARD    " + BoardName(o.BoardOrient) +
                   (o.BoardOrient == 2 ? "   lean " + F(o.LeanDeg) + " deg" : ""));

    Plane board = BuildBoard(o, card, r, log);
    r.DrawPlane = board;

    log.AppendLine("         origin " + P(board.Origin));
    log.AppendLine("         X " + V(board.XAxis) + "   across the sheet");
    log.AppendLine("         Y " + V(board.YAxis) + "   up the sheet");
    log.AppendLine("         Z " + V(board.ZAxis) + "   OUT of the sheet - this is the one");
    log.AppendLine("           that should point at the robot. The pen goes the other way.");

    // ---- 3. the sheet itself ----------------------------------------------
    Rectangle3d rect = new Rectangle3d(board,
      new Interval(-o.BoardW * 0.5, o.BoardW * 0.5),
      new Interval(-o.BoardH * 0.5, o.BoardH * 0.5));
    r.Board = rect.ToNurbsCurve();
    for (int i = 0; i < 4; i++) r.Corners.Add(rect.Corner(i));

    ZReport(r, o, log);
    WristCheck(r, o, board, log);
    DeadZone(o, r, log);
    Reach(r, o, board, log);

    r.Status = (r.Warnings.Count == 0 ? "OK: " : "OK WITH WARNINGS: ") +
               BoardName(o.BoardOrient) + ", " + F(o.BoardW) + " x " + F(o.BoardH) +
               " mm, pen " + F(o.ToolZ) + " mm off the flange, flange " +
               r.ReachNear.ToString("0") + " .. " + r.ReachFar.ToString("0") + " mm" +
               (r.OutOfReach > 0 ? ", " + r.OutOfReach + " SAMPLE(S) OUT OF REACH"
                                 : ", the whole sheet is in reach");
    r.Log = log.ToString();
    return r;
  }

  // =========================================================================
  // 2.  BUILDING THE BOARD
  //
  //     All four modes name their axes the same way, which is what lets the
  //     artwork be authored once in world XY and oriented onto whichever one
  //     you pick:
  //
  //       X  across the sheet, left to right
  //       Y  up the sheet
  //       Z  out of the sheet, towards the reader - and, in mode 0, towards
  //          the robot
  //
  //     Keep that and a drawing does not know or care how the board is hung.
  // =========================================================================

  private static Plane BuildBoard(Options o, Vector3d card, Result r, StringBuilder log)
  {
    Vector3d c = card;
    if (!c.Unitize()) c = Vector3d.XAxis;

    Vector3d z, x;

    switch (o.BoardOrient)
    {
      case 1:                                   // FLAT on a table
        z = Vector3d.ZAxis;
        // Travel runs away from the robot, so the drawing reads the right way
        // up when you stand where the arm stands.
        x = Vector3d.CrossProduct(Vector3d.ZAxis, c);
        if (!x.Unitize()) x = Vector3d.XAxis;
        break;

      case 2:                                   // TILTED - a drafting table
      {
        // Start flat and tip the top edge back towards the robot. The hinge is
        // the horizontal edge, which is the axis across the cardinal.
        Vector3d hinge = Vector3d.CrossProduct(Vector3d.ZAxis, c);
        if (!hinge.Unitize()) hinge = Vector3d.YAxis;
        Plane flat = new Plane(o.BoardOrigin, hinge, Vector3d.CrossProduct(Vector3d.ZAxis, hinge));
        flat.Rotate(RhinoMath.ToRadians(-o.LeanDeg), hinge, o.BoardOrigin);
        z = flat.ZAxis;
        x = flat.XAxis;
        break;
      }

      case 3:                                   // AWAY - the wrong one
        z = c;
        x = Vector3d.CrossProduct(Vector3d.ZAxis, c);
        if (!x.Unitize()) x = Vector3d.XAxis;
        break;

      default:                                  // 0 VERTICAL, facing the robot
        // The sheet stands up, so "up the sheet" is world up and the normal is
        // horizontal, pointing back down the cardinal at the arm.
        z = -c;
        x = Vector3d.CrossProduct(Vector3d.ZAxis, z);
        if (!x.Unitize()) x = Vector3d.XAxis;
        break;
    }

    Vector3d y = Vector3d.CrossProduct(z, x);
    if (!y.Unitize()) y = AnyPerp(z);

    Plane p = new Plane(o.BoardOrigin, x, y);
    if (!p.IsValid) p = new Plane(o.BoardOrigin, Vector3d.XAxis, Vector3d.YAxis);

    if (o.FlipBoardZ) p = new Plane(p.Origin, p.XAxis, -p.YAxis);
    if (Math.Abs(o.SpinDeg) > EPS) p.Rotate(RhinoMath.ToRadians(o.SpinDeg), p.ZAxis, p.Origin);

    // ---- turn the sheet to face the robot, if that is even possible ----
    if (o.ZToRobot)
    {
      Vector3d flat = o.RobotBase.Origin - p.Origin;
      flat.Z = 0.0;
      if (flat.Unitize() && (p.ZAxis * flat) < 0.0)
      {
        p = new Plane(p.Origin, p.XAxis, -p.YAxis);
        log.AppendLine("zToRobot TURNED THE SHEET ROUND - its normal was facing away.");
      }
    }
    return p;
  }

  // =========================================================================
  // 2b. DID zToRobot ACTUALLY MANAGE IT
  //
  //     Worth measuring rather than assuming. A board lying FLAT has a
  //     vertical normal and no horizontal component at all, so there is
  //     nothing to turn - and reporting success there would be the same bug
  //     the hotwire component had, where a Z pointing at the floor scored well
  //     against a robot base that is also on the floor.
  //
  //     Measured horizontally, for exactly that reason.
  // =========================================================================

  private static void ZReport(Result r, Options o, StringBuilder log)
  {
    Plane p = r.DrawPlane;
    Vector3d flat = o.RobotBase.Origin - p.Origin;
    flat.Z = 0.0;

    double vertical = Math.Abs(p.ZAxis.Z);
    double dot = flat.Unitize() ? (p.ZAxis * flat) : 0.0;

    log.AppendLine("BOARD Z  faces the robot " + dot.ToString("0.00") +
                   "   (1.00 = straight at it, 0 = side on, -1 = away;  the normal is " +
                   (100.0 * vertical).ToString("0") + "% vertical)");

    if (!o.ZToRobot) return;

    if (dot > 0.9)
    {
      log.AppendLine("  OK   the sheet's blue Z runs back at the arm. The pen comes in " +
                     "along the other way, which is what puts the flange between the " +
                     "robot and the work.");
    }
    else if (dot > 0.1)
    {
      log.AppendLine("  OK   the sheet is angled towards the robot rather than square on.");
    }
    else
    {
      string m = "zToRobot is on but the sheet's normal cannot be turned to face the " +
                 "robot - it is " + (100.0 * vertical).ToString("0") + "% vertical. A board " +
                 "lying FLAT has no horizontal component to turn. Set boardOrient to 0 for " +
                 "a sheet that stands up and faces the arm, or 2 with a lean to meet it " +
                 "part way.";
      r.Warnings.Add(m);
      log.AppendLine("  !! " + m);
    }
  }

  // =========================================================================
  // 2bb. IS THE WRIST GOING TO GO FLAT
  //
  //      The failure this catches is invisible in the geometry and cost a
  //      full afternoon to find, so it is worth stating plainly.
  //
  //      Every target in the job sat comfortably inside the reach ring -
  //      flange 681 to 832 mm against a 460 to 1101 ring - and KUKA|prc still
  //      refused the whole thing. Moving the board nearer, further, higher,
  //      sideways and swinging the magazine round the cell changed nothing.
  //      The same job with the board lying FLAT solved instantly.
  //
  //      The difference is not where the tool is, it is which way it points.
  //      A sheet standing square-on to the robot has its normal pointing back
  //      down the arm's own reach line, so a pen held perpendicular to the
  //      paper is aimed straight back at the shoulder. The wrist has to go
  //      flat to do that, axis 4 lines up with axis 6, and the pose is
  //      singular however much room there is around it.
  //
  //      Lean the pen and the wrist has something to bend around. Measured
  //      against prc, board at 900 / 0 / 450:
  //
  //          lean  0,  5, 10       UNREACHABLE
  //          lean 15, 20, 25, 30   clean
  //          lean 40               UNREACHABLE - too far the other way
  //          lean -20              clean
  //
  //      Hence the warning below rather than a silent default: the number
  //      matters, it has a band, and the band is not obvious.
  // =========================================================================

  private static void WristCheck(Result r, Options o, Plane board, StringBuilder log)
  {
    // How square-on is the sheet to the arm? The pen axis is the board's
    // normal reversed, and the reach line is the direction from the robot to
    // the work. Parallel means the pen is aimed back down the arm.
    Vector3d pen = -board.ZAxis;
    Vector3d reach = board.Origin - o.RobotBase.Origin;
    if (!reach.Unitize()) return;

    double cos = pen * reach;
    if (cos > 1.0) cos = 1.0;
    if (cos < -1.0) cos = -1.0;
    double offAxis = RhinoMath.ToDegrees(Math.Acos(Math.Abs(cos)));

    double lean = Math.Abs(o.PenLeanDeg);

    log.AppendLine("WRIST    the sheet is " + offAxis.ToString("0") +
                   " deg off square to the arm's reach line, pen leaning " +
                   F(o.PenLeanDeg) + " deg");

    // Square-on and no lean is the singular case. 25 deg of slack on the
    // squareness because the strokes wander over a 280 x 210 sheet and the
    // reach line is measured to its centre.
    bool squareOn = offAxis < 25.0;

    if (squareOn && lean < 12.0)
    {
      string m = "The sheet is nearly square-on to the robot and the pen is only leaning " +
                 F(o.PenLeanDeg) + " deg, so the tool points back down the arm's own reach " +
                 "line and the wrist has to go flat - axis 4 lines up with axis 6 and the " +
                 "pose is singular. KUKA|prc will refuse the job even though every target " +
                 "is well inside the reach ring. Measured band on this cell: 15 to 30 deg " +
                 "of lean solves, 0 to 10 does not, and 40 is too far the other way. " +
                 "20 is the shipped value.";
      r.Warnings.Add(m);
      log.AppendLine("  !! " + m);
    }
    else if (squareOn && lean > 35.0)
    {
      string m = "The pen is leaning " + F(o.PenLeanDeg) + " deg. Measured on this cell, " +
                 "40 deg puts the wrist out the other side and prc refuses again. Stay " +
                 "between 15 and 30.";
      r.Warnings.Add(m);
      log.AppendLine("  !! " + m);
    }
    else if (squareOn)
    {
      log.AppendLine("  OK   the pen leans far enough off the reach line for the wrist to " +
                     "have something to bend around.");
    }
    else
    {
      log.AppendLine("  OK   the sheet is angled to the arm, so the wrist is not being asked " +
                     "to go flat in the first place.");
    }
  }

  // =========================================================================
  // 2c. WHICH WAY IS THE WORK FROM THE ROBOT
  //
  //     Same rule as the hotwire component, and deliberately the same code
  //     shape: whichever of +X, -X, +Y, -Y points from the robot towards the
  //     work, taken horizontally. Coming at a board from straight above is a
  //     different kind of job and not what this switch is for.
  // =========================================================================

  private static Vector3d AutoCardinal(Point3d work, Plane robotBase)
  {
    Vector3d v = new Vector3d(work.X - robotBase.Origin.X, work.Y - robotBase.Origin.Y, 0.0);
    if (!v.Unitize()) return Vector3d.XAxis;

    return Math.Abs(v.X) >= Math.Abs(v.Y)
         ? (v.X >= 0 ? Vector3d.XAxis : -Vector3d.XAxis)
         : (v.Y >= 0 ? Vector3d.YAxis : -Vector3d.YAxis);
  }

  /// The blind spot directly behind the robot. Axis 1 cannot wrap the whole
  /// way round, so a wedge roughly 175-185 deg astern is unreachable at any
  /// orientation - swept against KUKA|prc in this cell. Ten degrees wide, and
  /// easy to sit work in by accident because nothing about it looks wrong.
  private static void DeadZone(Options o, Result r, StringBuilder log)
  {
    double dx = o.BoardOrigin.X - o.RobotBase.Origin.X;
    double dy = o.BoardOrigin.Y - o.RobotBase.Origin.Y;
    if (Math.Abs(dx) < EPS && Math.Abs(dy) < EPS) return;

    double bearing = RhinoMath.ToDegrees(Math.Atan2(dy, dx));
    double behind = 180.0 - Math.Abs(bearing);

    log.AppendLine("BEARING  board sits " + bearing.ToString("0") +
                   " deg round from the front of the robot");

    if (behind <= 10.0)
    {
      string m = "The board is " + behind.ToString("0") + " deg from DIRECTLY BEHIND the " +
                 "robot. Axis 1 cannot wrap that far, so there is a blind wedge roughly " +
                 "175-185 deg that no orientation reaches. Swing the board 15-20 deg to " +
                 "either side and it comes back.";
      r.Warnings.Add(m);
      log.AppendLine("  !! " + m);
    }
  }

  // =========================================================================
  // 3.  CAN THE ARM REACH THE WHOLE SHEET
  //
  //     Measuring the distance to the PAPER is the wrong test, and it is the
  //     one everybody does first. The robot's 1101 mm is the reach of its
  //     FLANGE, and the flange is a whole tool length away from the nib.
  //
  //     The TCP offset is read out of the tool plane's own basis, so this stays
  //     correct if the tool is re-taught with different A/B/C.
  // =========================================================================

  private static void Reach(Result r, Options o, Plane board, StringBuilder log)
  {
    Plane tool = r.ToolPlane;
    Vector3d off = new Vector3d(tool.Origin);
    double a = off * tool.XAxis, b = off * tool.YAxis, c = off * tool.ZAxis;

    double far = 0.0, near = double.MaxValue;
    int over = 0, under = 0, n = 0;

    for (int i = 0; i < o.Grid; i++)
      for (int j = 0; j < o.Grid; j++)
      {
        double u = -o.BoardW * 0.5 + o.BoardW * i / (o.Grid - 1);
        double v = -o.BoardH * 0.5 + o.BoardH * j / (o.Grid - 1);
        Point3d pt = board.PointAt(u, v);

        // The target frame TF-09 will build here: Z runs down the pen, INTO
        // the paper, so it is the board's normal reversed. X is the travel
        // direction and does not affect reach with a TCP that is purely along
        // the tool's own axis - but it is built properly anyway, so the sum
        // below stays right if someone teaches a tool with X or Y offsets.
        Plane t = new Plane(pt, board.XAxis, -board.YAxis);   // Z = -board Z

        Point3d flange = t.Origin - (a * t.XAxis + b * t.YAxis + c * t.ZAxis);
        r.FlangePts.Add(flange);
        r.PenLines.Add(new Line(flange, pt));

        double d = flange.DistanceTo(o.RobotBase.Origin);
        if (d > far) far = d;
        if (d < near) near = d;
        if (d > o.ReachMax) over++;
        if (d < o.ReachMin) under++;
        n++;
      }

    r.OutOfReach = over + under;
    r.ReachNear = near;
    r.ReachFar = far;

    log.AppendLine("REACH    flange " + near.ToString("0") + " .. " + far.ToString("0") +
                   " mm from the robot base, over " + n + " samples on the sheet");
    log.AppendLine("         working ring is " + o.ReachMin.ToString("0") + " .. " +
                   o.ReachMax.ToString("0") + " mm. It is a RING - the flange sits " +
                   F(o.ToolZ) + " mm back from the nib, so a sheet that is too CLOSE " +
                   "fails as surely as one too far.");

    if (over > 0)
    {
      string m = over + " of " + n + " samples put the FLANGE beyond " +
                 o.ReachMax.ToString("0") + " mm - worst " + far.ToString("0") +
                 " mm. The arm cannot stretch that far. Move the board TOWARDS the robot.";
      r.Warnings.Add(m);
      log.AppendLine("  !! " + m);
    }
    if (under > 0)
    {
      string m = under + " of " + n + " samples put the FLANGE inside " +
                 o.ReachMin.ToString("0") + " mm - closest " + near.ToString("0") +
                 " mm. The arm would have to fold into its own body. Move the board " +
                 "AWAY from the robot - counter-intuitive, but the pen reaches " +
                 F(o.ToolZ) + " mm ahead of the wrist.";
      r.Warnings.Add(m);
      log.AppendLine("  !! " + m);
    }
    if (over == 0 && under == 0)
      log.AppendLine("  OK   every point on the sheet sits inside the working ring.");
  }

  // =========================================================================
  // 4.  NAMES
  // =========================================================================

  private static Vector3d CardinalOf(int mode)
  {
    if (mode == 1) return Vector3d.XAxis;
    if (mode == 2) return -Vector3d.XAxis;
    if (mode == 3) return Vector3d.YAxis;
    if (mode == 4) return -Vector3d.YAxis;
    return Vector3d.XAxis;
  }

  private static string CardinalName(Vector3d v)
  {
    if (Math.Abs(v.X) > 0.5) return v.X > 0 ? "+X, out in front of the robot" : "-X, behind the robot";
    if (Math.Abs(v.Y) > 0.5) return v.Y > 0 ? "+Y, to the left" : "-Y, to the right";
    return "off-axis";
  }

  private static string BoardName(int m)
  {
    if (m == 1) return "FLAT - lying on a table, normal straight up";
    if (m == 2) return "TILTED - a drafting table leaning back towards the robot";
    if (m == 3) return "AWAY - turned to face away from the robot, which is the wrong one";
    return "VERTICAL - standing up, facing the robot";
  }

  private static Vector3d AnyPerp(Vector3d v)
  {
    Vector3d a = Math.Abs(v.Z) < 0.9 ? Vector3d.ZAxis : Vector3d.XAxis;
    Vector3d p = Vector3d.CrossProduct(v, a);
    p.Unitize();
    return p;
  }

  private static Vector3d AxisOf(Plane p, int axis)
  {
    if (axis == 0) return p.XAxis;
    if (axis == 1) return p.YAxis;
    return p.ZAxis;
  }

  private static string AxisName(int axis)
  {
    if (axis == 0) return "X";
    if (axis == 1) return "Y";
    return "Z";
  }

  // =========================================================================
  // 5.  KUKA A/B/C
  //
  //     Z-Y'-X'' intrinsic, so R = Rz(A) . Ry(B) . Rx(C). Same convention and
  //     the same gimbal-lock branch as TF09_helpers.cs, FL01_helpers.cs and
  //     HW_helpers.cs - deliberately duplicated rather than shared, because a
  //     Grasshopper script component has to stand on its own in one canvas.
  // =========================================================================

  public static Plane AbcToPlane(Point3d origin, double A, double B, double C)
  {
    double a = RhinoMath.ToRadians(A), b = RhinoMath.ToRadians(B), c = RhinoMath.ToRadians(C);
    double ca = Math.Cos(a), sa = Math.Sin(a);
    double cb = Math.Cos(b), sb = Math.Sin(b);
    double cc = Math.Cos(c), sc = Math.Sin(c);

    Vector3d X = new Vector3d(ca * cb, sa * cb, -sb);
    Vector3d Y = new Vector3d(ca * sb * sc - sa * cc, sa * sb * sc + ca * cc, cb * sc);
    return new Plane(origin, X, Y);
  }

  public static void PlaneToAbc(Plane p, out double A, out double B, out double C)
  {
    Vector3d X = p.XAxis; X.Unitize();
    Vector3d Y = p.YAxis; Y.Unitize();
    Vector3d Z = p.ZAxis; Z.Unitize();

    double r00 = X.X, r10 = X.Y, r20 = X.Z;
    double r01 = Y.X, r11 = Y.Y, r21 = Y.Z;
    double r22 = Z.Z;

    double sB = -r20;
    if (sB > 1.0) sB = 1.0;
    if (sB < -1.0) sB = -1.0;

    if (Math.Abs(sB) > 1.0 - 1e-10)
    {
      B = sB > 0 ? 90.0 : -90.0;
      A = 0.0;
      C = sB > 0 ? RhinoMath.ToDegrees(Math.Atan2(r01, r11))
                 : RhinoMath.ToDegrees(Math.Atan2(-r01, r11));
      return;
    }

    B = RhinoMath.ToDegrees(Math.Asin(sB));
    A = RhinoMath.ToDegrees(Math.Atan2(r10, r00));
    C = RhinoMath.ToDegrees(Math.Atan2(r21, r22));
  }

  // =========================================================================
  // 6.  ODDS AND ENDS
  // =========================================================================

  private static string F(double d)
  { return d.ToString("0.###", CultureInfo.InvariantCulture); }

  private static string V(Vector3d v)
  {
    CultureInfo inv = CultureInfo.InvariantCulture;
    return "(" + v.X.ToString("0.##", inv) + ", " + v.Y.ToString("0.##", inv) +
           ", " + v.Z.ToString("0.##", inv) + ")";
  }

  private static string P(Point3d p)
  {
    CultureInfo inv = CultureInfo.InvariantCulture;
    return "(" + p.X.ToString("0.#", inv) + ", " + p.Y.ToString("0.#", inv) +
           ", " + p.Z.ToString("0.#", inv) + ")";
  }
}

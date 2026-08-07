// ---------------------------------------------------------------------------
// HOTWIRE TOOL - TCP frame from KUKA A/B/C, plus plane flipping
// PANE: "Members" / additional code  (Rhino 8 C# Script component, bottom pane)
//
// WHAT THIS IS FOR
//   Two jobs that both come down to "which way round is this frame".
//
//   1. Turn the numbers typed into the KUKA CUSTOM TOOL dialog - X, Y, Z, A, B,
//      C - into the Plane that KUKA|prc's "Custom Tool: Plane" wants. Type the
//      same numbers the pendant has and the simulation matches the cell.
//
//   2. Flip the toolpath target planes with toggles instead of code edits.
//      Which way a frame should face is a question you answer by looking at the
//      simulation, not by reasoning, and it should cost one click to try the
//      other way.
//
// MEASURED, NOT ASSUMED
//   The defaults come from the lab's own Hotwire_2.1.3dm, read directly:
//     flange face at the origin, tool reaching along +Z
//     wire along flange Y at Z 421.35, spanning -207.9 .. +207.9  (415.8 mm)
//   and from the CUSTOM TOOL dialog: Z 422, A -90, B -90, C 0.
//
//   Those two agree. Offsetting along the TCP's own Z by half the span lands
//   0.65 mm from the modelled wire ends, which is what says the frame really
//   does run down the wire rather than merely looking like it does.
//
// A NOTE ON B = -90
//   A -90 / B -90 / C 0 sits exactly on gimbal lock. At B = +/-90 only A + C
//   (or C - A) is determined, so A -90 / B -90 / C 0 and A 0 / B -90 / C -90
//   are the SAME orientation. If the pendant reads back different numbers from
//   the ones you typed, that is why, and nothing is wrong.
// ---------------------------------------------------------------------------

public static class HotwireTool
{
  private const double EPS = 1e-12;

  // =========================================================================
  // 0.  CONTAINERS
  // =========================================================================

  public class Options
  {
    // --- the TCP, straight off the CUSTOM TOOL dialog ---
    public double ToolX = 0.0;
    public double ToolY = 0.0;
    public double ToolZ = 422.0;
    public double ToolA = -90.0;
    public double ToolB = -90.0;
    public double ToolC = 0.0;

    // --- the wire ---
    // 415.8 mm is the span MODELLED in Hotwire_2.1.3dm, bracket face to bracket
    // face. The USABLE cutting span is shorter and has to be measured on the
    // real frame - the end-effector README lists that as an open item.
    public double WireSpan = 415.8;

    // Which axis of the tool frame the wire lies along. 2 = Z, which is what
    // A -90 / B -90 / C 0 gives.
    public int WireAxis = 2;

    // --- flipping the tool frame itself ---
    public bool FlipToolZ = false;    // send the tool axis the other way
    public bool FlipToolSpin = false; // half turn about the tool axis

    // --- HOW THE TARGET FRAMES ARE BUILT --------------------------------
    //
    // 0 KEEP  leave FL-01's frames alone - Z radial, into the material
    // 1 CUT   the cardinal direction drives the ARM, the wire lies across the
    //         travel and tangent to the surface. This is the one that cuts.
    // 2 WIRE  the cardinal direction drives the WIRE - Z is forced to
    //         +/-X or +/-Y literally.
    //
    // WHY THERE ARE TWO, AND WHY 1 IS THE DEFAULT
    //   Two different things want fixing and they are not the same axis.
    //
    //   Reach is governed by the frame's X. The TCP sits 422 mm along the
    //   tool's X, so the FLANGE lands 422 mm back along the target's X. Aim X
    //   away from the robot and the flange is pulled 422 mm closer to it.
    //
    //   Cutting is governed by the frame's Z, because the wire lies on tool Z.
    //   The wire has to be ACROSS the direction of travel and tangent to the
    //   surface. Point it along the approach instead and it goes into the foam
    //   end-on and melts a pocket.
    //
    //   Mode 2 does what "force Z to +X" says literally, and for this tool
    //   that puts the wire end-on. It is kept because it is occasionally what
    //   you want and because being able to see the wrong one is how you trust
    //   the right one. Mode 1 satisfies both constraints at once, which is
    //   possible precisely because they act on different axes.
    public int FrameMode = 1;

    // 0 AUTO  from where the part sits relative to the robot
    // 1..4    +X / -X / +Y / -Y, forced by hand
    public int Cardinal = 0;

    public Plane RobotBase = Plane.WorldXY;

    // The envelope is a RING, not a maximum, and the inner wall matters here
    // more than it usually would. The flange sits 422 mm back from the cut, so
    // bringing the part CLOSER pushes the flange towards the robot's own body
    // until the arm would have to fold inside itself.
    //
    // 460 mm is measured, not from a datasheet: swept against KUKA|prc with
    // this tool, the job is clean from about 460 mm of flange radius outwards
    // and fails below it.
    public double ReachMax = 1101.0;   // KR6-10 R1100-2, flange
    public double ReachMin = 460.0;

    // --- flipping the toolpath targets ---
    public bool FlipZ = false;        // approach from the other side
    public bool FlipX = false;        // reverse travel (half turn about approach)
    public double TiltDeg = 0.0;      // rotate about the travel direction
    public double SpinDeg = 0.0;      // free spin about approach
  }

  public class Result
  {
    public Plane ToolPlane = Plane.Unset;
    public string ToolAbc = "";
    public List<Plane> Targets = new List<Plane>();
    public List<Line> WireLines = new List<Line>();
    public List<Plane> WireEndA = new List<Plane>();
    public List<Plane> WireEndB = new List<Plane>();
    public List<Point3d> FlangePts = new List<Point3d>();
    public Vector3d ApproachUsed = Vector3d.Unset;
    public int OutOfReach = 0;
    public double ReachWorst = 0.0;
    public string Status = "";
    public string Log = "";
    public List<string> Warnings = new List<string>();
  }

  // =========================================================================
  // 1.  ENTRY POINT
  // =========================================================================

  public static Result Build(List<Plane> targets, Options o)
  {
    Result r = new Result();
    StringBuilder log = new StringBuilder();

    if (o == null) o = new Options();
    if (o.WireSpan < 0.0) o.WireSpan = 0.0;
    if (o.WireAxis < 0 || o.WireAxis > 2) o.WireAxis = 2;

    // ---- 1. the tool frame -------------------------------------------------
    Plane tool = AbcToPlane(new Point3d(o.ToolX, o.ToolY, o.ToolZ), o.ToolA, o.ToolB, o.ToolC);
    if (o.FlipToolZ)    tool = FlipAboutX(tool);
    if (o.FlipToolSpin) tool = SpinAboutZ(tool, 180.0);
    r.ToolPlane = tool;

    double a, b, c;
    PlaneToAbc(tool, out a, out b, out c);
    r.ToolAbc = "X " + F(o.ToolX) + "   Y " + F(o.ToolY) + "   Z " + F(o.ToolZ) +
                "   A " + F(a) + "   B " + F(b) + "   C " + F(c);

    log.AppendLine("TOOL     " + r.ToolAbc);
    log.AppendLine("         X axis " + V(tool.XAxis) + "   (out of the flange when A/B/C are the defaults)");
    log.AppendLine("         Y axis " + V(tool.YAxis));
    log.AppendLine("         Z axis " + V(tool.ZAxis));

    if (Math.Abs(Math.Abs(o.ToolB) - 90.0) < 1e-6)
      log.AppendLine("         B is +/-90, which is gimbal lock: A and C trade off against each " +
                     "other and the pendant may read back different numbers for the same pose.");

    Vector3d wireDir = AxisOf(tool, o.WireAxis);
    log.AppendLine("WIRE     " + o.WireSpan.ToString("0.#") + " mm along the tool's " +
                   AxisName(o.WireAxis) + " axis " + V(wireDir));
    log.AppendLine("         that is the MODELLED span. The usable cutting span is shorter " +
                   "and has to be measured on the real frame.");

    // ---- 2. the targets ----------------------------------------------------
    List<Plane> sources = new List<Plane>();

    if (targets != null)
    {
      // ---- the cardinal direction ----------------------------------------
      Vector3d card = Vector3d.Unset;
      if (o.FrameMode >= 1)
      {
        card = (o.Cardinal == 0) ? AutoApproach(targets, o.RobotBase)
                                 : CardinalOf(o.Cardinal);
        r.ApproachUsed = card;
        log.AppendLine("CARDINAL " + (o.Cardinal == 0 ? "AUTO -> " : "forced ") + V(card) +
                       "   (" + CardinalName(card) + ")");

        if (o.FrameMode == 1)
        {
          log.AppendLine("MODE     CUT - the cardinal drives the ARM. X points that way, so the");
          log.AppendLine("         flange is pulled " + Math.Abs(o.ToolZ).ToString("0") +
                         " mm back towards the robot. The wire is laid ACROSS the");
          log.AppendLine("         travel and tangent to the surface, which is the orientation that cuts.");
        }
        else
        {
          log.AppendLine("MODE     WIRE - the cardinal drives the WIRE. Z is forced to it literally.");
          log.AppendLine("         For this tool the wire is on tool Z, so it will go into the");
          log.AppendLine("         material end-on. Use mode 1 unless you specifically want this.");
        }
        if (Math.Abs(o.TiltDeg) > EPS)
          log.AppendLine("         tiltDeg is ignored while frameMode is on - the rebuilt frame wins.");

        DeadZone(targets, o, r, log);
      }
      else
      {
        log.AppendLine("MODE     KEEP - using FL-01's own radial Z. It swings right round the loop,");
        log.AppendLine("         so somewhere in every pass it points back at the robot.");
      }

      int flipped = 0;
      foreach (Plane p in targets)
      {
        if (!p.IsValid) continue;
        sources.Add(p);
        Plane q = p;

        if (o.FrameMode >= 1)
        {
          q = Reframe(p, card, o);
        }
        else
        {
          // Tilt is applied about the ORIGINAL travel direction and before the
          // spin, so that "tilt 90" always means the same thing no matter what
          // the other toggles are doing.
          if (Math.Abs(o.TiltDeg) > EPS) q = RotateAbout(q, o.TiltDeg, q.XAxis);
        }

        if (o.FlipZ) { q = FlipAboutX(q); flipped++; }
        if (o.FlipX) q = SpinAboutZ(q, 180.0);
        if (Math.Abs(o.SpinDeg) > EPS) q = SpinAboutZ(q, o.SpinDeg);
        r.Targets.Add(q);

        Vector3d w = AxisOf(q, o.WireAxis);
        Point3d pa = q.Origin - w * (o.WireSpan * 0.5);
        Point3d pb = q.Origin + w * (o.WireSpan * 0.5);
        r.WireLines.Add(new Line(pa, pb));

        Plane fa = q; fa.Origin = pa; r.WireEndA.Add(fa);
        Plane fb = q; fb.Origin = pb; r.WireEndB.Add(fb);
      }

      Reach(r, o, log);

      bool touched = o.FlipZ || o.FlipX ||
                     Math.Abs(o.TiltDeg) > EPS || Math.Abs(o.SpinDeg) > EPS;
      log.AppendLine("TARGETS  " + r.Targets.Count + " in" +
                     (o.FlipZ ? ", approach flipped" : "") +
                     (o.FlipX ? ", travel reversed" : "") +
                     (Math.Abs(o.TiltDeg) > EPS ? ", tilted " + F(o.TiltDeg) + " deg about travel" : "") +
                     (Math.Abs(o.SpinDeg) > EPS ? ", spun " + F(o.SpinDeg) + " deg" : "") +
                     (touched ? "" : ", unchanged"));

      Diagnose(r, sources, o, log);
    }
    else
    {
      log.AppendLine("TARGETS  none wired - tool frame only");
    }

    r.Status = (r.Warnings.Count == 0 ? "OK: " : "OK WITH WARNINGS: ") +
               r.Targets.Count + " target(s), wire " + o.WireSpan.ToString("0.#") +
               " mm along tool " + AxisName(o.WireAxis) +
               (r.Targets.Count > 0
                  ? ", flange to " + r.ReachWorst.ToString("0") + " mm" +
                    (r.OutOfReach > 0 ? ", " + r.OutOfReach + " OUT OF REACH" : ", all in reach")
                  : "");
    r.Log = log.ToString();
    return r;
  }

  // =========================================================================
  // 2.  IS THE WIRE POINTING A SENSIBLE WAY?
  //
  //     The cutting element is a LINE, not a point, so unlike a router bit it
  //     matters which way the line lies relative to the direction of travel.
  //     Three cases, and only one of them cuts:
  //
  //       wire ACROSS the travel, tangent to the surface   -> slicing. good.
  //       wire ALONG the travel                            -> dragging the wire
  //            lengthwise through its own kerf. It will not cut, it will just
  //            follow the slot it already made.
  //       wire ALONG the approach                          -> stabbing the wire
  //            end-on into the foam. It will melt a pocket.
  //
  //     This is the check that makes the flip toggles useful: flip something,
  //     read this, and you know whether you improved it.
  // =========================================================================

  private static void Diagnose(Result r, List<Plane> sources, Options o, StringBuilder log)
  {
    if (r.Targets.Count == 0 || sources.Count != r.Targets.Count) return;

    double worstTravel = 0.0, worstApproach = 0.0;
    int nTravel = 0, nApproach = 0;

    for (int i = 0; i < r.Targets.Count; i++)
    {
      // The wire direction comes from the FINAL frame - that is where the wire
      // ends up. Travel and approach come from the ORIGINAL frame, because
      // those are properties of the toolpath and do not move when the tool is
      // rotated. Measuring both on the final frame compares the wire against
      // itself and can only ever report "parallel".
      Vector3d w = AxisOf(r.Targets[i], o.WireAxis);
      double alongTravel   = Math.Abs(w * sources[i].XAxis);
      double alongApproach = Math.Abs(w * sources[i].ZAxis);

      if (alongTravel   > worstTravel)   worstTravel = alongTravel;
      if (alongApproach > worstApproach) worstApproach = alongApproach;
      if (alongTravel   > 0.7) nTravel++;
      if (alongApproach > 0.7) nApproach++;
    }

    log.AppendLine("WIRE VS  travel  worst |cos| " + worstTravel.ToString("0.###") +
                   "   approach  worst |cos| " + worstApproach.ToString("0.###") +
                   "     (0 = perpendicular, 1 = parallel)");

    if (nApproach > 0)
    {
      string m = nApproach + " of " + r.Targets.Count + " targets point the WIRE along the " +
                 "approach direction. The wire would go into the material end-on and melt a " +
                 "pocket rather than cut. Set tiltDeg = 90 to lay it across the travel.";
      r.Warnings.Add(m);
      log.AppendLine("  !! " + m);
    }
    if (nTravel > 0)
    {
      string m = nTravel + " of " + r.Targets.Count + " targets lay the WIRE along the " +
                 "direction of travel. The wire then slides down its own kerf and removes " +
                 "nothing. The wire wants to be ACROSS the travel.";
      r.Warnings.Add(m);
      log.AppendLine("  !! " + m);
    }
    if (nApproach == 0 && nTravel == 0)
      log.AppendLine("  OK   the wire lies across the travel and tangent to the surface, " +
                     "which is the slicing case.");
  }

  // =========================================================================
  // 2b. WHICH WAY DOES THE TOOL COME IN
  //
  //     A wire does not have to come at the work radially the way a router bit
  //     does. It is a straight line, and one consistent attitude is easier to
  //     cut with AND easier to reach. So the approach is forced to one
  //     direction, and AUTO picks that direction from where the part actually
  //     is relative to the robot: whichever of +X, -X, +Y, -Y points from the
  //     robot towards the work.
  //
  //     Front of the robot -> +X. Behind -> -X. Left and right -> +/-Y.
  //     That is the whole rule, and it is the one the user asked for.
  // =========================================================================

  private static Vector3d AutoApproach(List<Plane> targets, Plane robotBase)
  {
    Point3d c = Point3d.Origin;
    int n = 0;
    foreach (Plane p in targets) { if (p.IsValid) { c += p.Origin; n++; } }
    if (n == 0) return Vector3d.XAxis;
    c = new Point3d(c.X / n, c.Y / n, c.Z / n);

    // Horizontal only. Coming at a part from straight above or below is a
    // different kind of job and not what this switch is for.
    Vector3d v = new Vector3d(c.X - robotBase.Origin.X, c.Y - robotBase.Origin.Y, 0.0);
    if (!v.Unitize()) return Vector3d.XAxis;

    return Math.Abs(v.X) >= Math.Abs(v.Y)
         ? (v.X >= 0 ? Vector3d.XAxis : -Vector3d.XAxis)
         : (v.Y >= 0 ? Vector3d.YAxis : -Vector3d.YAxis);
  }

  /// The blind spot directly behind the robot.
  ///
  /// Axis 1 cannot wrap the whole way round, so there is a narrow wedge behind
  /// the robot that it simply cannot turn to face. Swept against KUKA|prc with
  /// this cell, everything from 0 to 170 degrees and from 190 to 360 is fine,
  /// and 175 to 185 fails. Ten degrees wide, and easy to sit a part in by
  /// accident because nothing about the geometry looks wrong.
  private static void DeadZone(List<Plane> targets, Options o, Result r, StringBuilder log)
  {
    Point3d c = Point3d.Origin;
    int n = 0;
    foreach (Plane p in targets) { if (p.IsValid) { c += p.Origin; n++; } }
    if (n == 0) return;
    c = new Point3d(c.X / n, c.Y / n, c.Z / n);

    double dx = c.X - o.RobotBase.Origin.X, dy = c.Y - o.RobotBase.Origin.Y;
    if (Math.Abs(dx) < EPS && Math.Abs(dy) < EPS) return;

    double bearing = RhinoMath.ToDegrees(Math.Atan2(dy, dx));   // 0 = in front
    double behind = 180.0 - Math.Abs(bearing);                  // 0 = dead astern

    log.AppendLine("BEARING  part sits " + bearing.ToString("0") +
                   " deg round from the front of the robot");

    if (behind <= 10.0)
    {
      string m = "The part is " + behind.ToString("0") + " deg from DIRECTLY BEHIND the " +
                 "robot. Axis 1 cannot wrap that far, so there is a blind wedge roughly " +
                 "175-185 deg that no orientation reaches. Swing the part 15-20 deg to " +
                 "either side and it comes back.";
      r.Warnings.Add(m);
      log.AppendLine("  !! " + m);
    }
  }

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

  /// Rebuild one target frame, keeping its origin.
  ///
  /// MODE 1, CUT - the one that works:
  ///   X = the cardinal direction. The arm comes from there, and because the
  ///       TCP is 422 mm along tool X the flange is pulled 422 mm back towards
  ///       the robot.
  ///   Z = FL-01's own Y, squared up against X. FL-01 builds Y as Z cross X,
  ///       i.e. tangent to the surface and ACROSS the direction of travel -
  ///       which is exactly where a cutting wire wants to be. On a part
  ///       standing upright with horizontal slices, that comes out vertical,
  ///       so the wire spans the part's height and sweeps round its profile.
  ///
  /// MODE 2, WIRE - literal:
  ///   Z = the cardinal direction, X = whatever is left.
  ///
  /// The two constraints can both be met at once only because they act on
  /// different axes. That is the whole trick.
  private static Plane Reframe(Plane p, Vector3d card, Options o)
  {
    Vector3d c = card;
    if (!c.Unitize()) return p;

    Vector3d x, z;

    if (o.FrameMode == 2)
    {
      z = c;
      x = p.XAxis - (p.XAxis * z) * z;             // the travel, flattened
      if (!x.Unitize()) x = AnyPerp(z);
    }
    else
    {
      x = c;
      z = p.YAxis - (p.YAxis * x) * x;             // tangent, across the travel
      if (!z.Unitize())
      {
        // The tangent is parallel to the arm direction - happens at the very
        // top and bottom of a part, where the slice degenerates. Fall back to
        // the travel direction rather than emitting a broken frame.
        z = p.XAxis - (p.XAxis * x) * x;
        if (!z.Unitize()) z = AnyPerp(x);
      }
    }

    Vector3d y = Vector3d.CrossProduct(z, x);
    if (!y.Unitize()) return p;
    return new Plane(p.Origin, x, y);
  }

  private static Vector3d AnyPerp(Vector3d v)
  {
    Vector3d a = Math.Abs(v.Z) < 0.9 ? Vector3d.ZAxis : Vector3d.XAxis;
    Vector3d p = Vector3d.CrossProduct(v, a);
    p.Unitize();
    return p;
  }

  // =========================================================================
  // 2c. CAN THE ARM ACTUALLY GET THERE
  //
  //     Measuring the distance to the TARGET is the wrong test, and it is the
  //     one everybody does first. The robot's 1101 mm is the reach of its
  //     FLANGE; the target is where the TCP goes, and the TCP is 422 mm away
  //     from the flange. So the flange position has to be worked out properly
  //     before anything is compared to anything.
  //
  //     The TCP offset is expressed in the tool plane's own basis, so this
  //     stays correct if the tool is re-taught with different A/B/C.
  // =========================================================================

  private static void Reach(Result r, Options o, StringBuilder log)
  {
    if (r.Targets.Count == 0) return;

    Plane tool = r.ToolPlane;
    Vector3d off = new Vector3d(tool.Origin);        // flange -> TCP, flange coords
    double a = off * tool.XAxis, b = off * tool.YAxis, c = off * tool.ZAxis;

    double worst = 0.0, best = double.MaxValue;
    int over = 0, under = 0;

    foreach (Plane q in r.Targets)
    {
      Point3d flange = q.Origin - (a * q.XAxis + b * q.YAxis + c * q.ZAxis);
      r.FlangePts.Add(flange);

      double d = flange.DistanceTo(o.RobotBase.Origin);
      if (d > worst) worst = d;
      if (d < best) best = d;
      if (d > o.ReachMax) over++;
      if (d < o.ReachMin) under++;
    }

    r.OutOfReach = over + under;
    r.ReachWorst = worst;

    log.AppendLine("REACH    flange " + best.ToString("0") + " .. " + worst.ToString("0") +
                   " mm from the robot base");
    log.AppendLine("         working ring is " + o.ReachMin.ToString("0") + " .. " +
                   o.ReachMax.ToString("0") + " mm. Note it is a RING - the flange sits " +
                   Math.Abs(o.ToolZ).ToString("0") + " mm back from the cut, so too CLOSE " +
                   "fails as surely as too far.");

    if (over > 0)
    {
      string m = over + " of " + r.Targets.Count + " targets put the FLANGE beyond " +
                 o.ReachMax.ToString("0") + " mm - worst " + worst.ToString("0") +
                 " mm. The arm cannot stretch that far. Move the part TOWARDS the robot.";
      r.Warnings.Add(m);
      log.AppendLine("  !! " + m);
    }
    if (under > 0)
    {
      string m = under + " of " + r.Targets.Count + " targets put the FLANGE inside " +
                 o.ReachMin.ToString("0") + " mm - closest " + best.ToString("0") +
                 " mm. The arm would have to fold into its own body. Move the part " +
                 "AWAY from the robot - counter-intuitive, but the tool reaches 422 mm " +
                 "ahead of the wrist.";
      r.Warnings.Add(m);
      log.AppendLine("  !! " + m);
    }
    if (over == 0 && under == 0)
      log.AppendLine("  OK   every target sits inside the working ring.");
  }

  // =========================================================================
  // 3.  FLIPS
  //
  //     Both are expressed as rebuilding the plane from two of its own axes,
  //     which keeps the frame exactly orthonormal. Negating an axis and
  //     leaving the others alone would make a left-handed frame, and KUKA|prc
  //     reads A/B/C off the frame - a left-handed one gives a silently wrong
  //     orientation rather than an error.
  // =========================================================================

  /// Reverse Z, keep X. Y follows, so the frame stays right handed.
  private static Plane FlipAboutX(Plane p)
  {
    return new Plane(p.Origin, p.XAxis, -p.YAxis);
  }

  /// Rotate about the plane's own Z. 180 degrees reverses X - the travel
  /// direction - without disturbing the approach.
  private static Plane SpinAboutZ(Plane p, double deg)
  {
    return RotateAbout(p, deg, p.ZAxis);
  }

  /// Rotate about any axis through the plane's origin.
  ///
  /// The one that matters is 90 degrees about X, the travel direction: it
  /// swings the tool's Z from pointing INTO the material to lying tangent
  /// across it. With the wire on tool Z, that is the difference between
  /// stabbing the wire in end-on and laying it across the cut.
  private static Plane RotateAbout(Plane p, double deg, Vector3d axis)
  {
    Plane q = p;
    q.Rotate(RhinoMath.ToRadians(deg), axis, p.Origin);
    return q;
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
  // 4.  KUKA A/B/C
  //
  //     Z-Y'-X'' intrinsic, so R = Rz(A) . Ry(B) . Rx(C). Same convention and
  //     the same gimbal-lock branch as TF09_helpers.cs and FL01_helpers.cs -
  //     deliberately duplicated rather than shared, because a Grasshopper
  //     script component has to stand on its own in one canvas.
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
  // 5.  ODDS AND ENDS
  // =========================================================================

  private static string F(double d)
  { return d.ToString("0.###", CultureInfo.InvariantCulture); }

  private static string V(Vector3d v)
  {
    CultureInfo inv = CultureInfo.InvariantCulture;
    return "(" + v.X.ToString("0.##", inv) + ", " + v.Y.ToString("0.##", inv) +
           ", " + v.Z.ToString("0.##", inv) + ")";
  }
}

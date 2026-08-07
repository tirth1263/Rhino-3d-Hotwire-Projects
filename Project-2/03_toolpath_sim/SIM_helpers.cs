// ---------------------------------------------------------------------------
// TOOLPATH SIMULATOR - shared by FL-01 and TF-09
// PANE: "Members" / "Additional code"  (Rhino 8 C# Script component, bottom pane)
//
// ASU Robotics Lab.  Author: Tirth.
//
// WHAT THIS IS, AND WHAT IT IS NOT
//   IS:     a real time-and-motion simulation of the TCP. It builds a timeline
//           from the actual distances and the actual commanded feed rates,
//           interpolates position linearly and orientation by shortest-arc
//           rotation, and reports where the tool is at any moment, how long the
//           job takes, and where the wrist has to snap.
//   IS NOT: a joint-space simulation. It does not know about axis limits,
//           singularities or reach. KUKA|prc's own Analysis component does
//           that, and it stays in the definition. This runs alongside it,
//           because prc's playback cannot answer "how long" or "how fast is
//           the orientation changing here".
//
//   Nothing in this file is approximated for convenience. The interpolation
//   between two targets is the same shortest-arc rotation a LIN move performs,
//   so the orientation shown mid-segment is the orientation the robot will
//   actually pass through.
//
// ORIENTATION INDEPENDENCE
//   There is no world axis anywhere in this file. Every quantity is derived
//   from the target planes themselves.
// ---------------------------------------------------------------------------

public static class PathSim
{
  public const double EPS = 1e-12;

  public class Segment
  {
    public int FromIndex;
    public Plane A, B;
    public double Length;      // mm
    public double Feed;        // mm/s
    public double Duration;    // s
    public double StartTime;   // s
    public int MoveType;       // 0 air, 1 process, 2 magazine trip
    public double TurnDeg;     // total orientation change across the segment
    public double Dwell;       // s of standing still at the end of this segment
  }

  public class Result
  {
    public List<Segment> Segments = new List<Segment>();
    public Plane Tcp = Plane.Unset;
    public Polyline Trail = new Polyline();
    public Polyline Remaining = new Polyline();
    public Line ToolAxis = Line.Unset;
    public Mesh ToolBody = null;
    public double Time = 0.0;
    public double CycleTime = 0.0;
    public double ProcessTime = 0.0;
    public double AirTime = 0.0;
    public double DwellTime = 0.0;
    public double ProcessDist = 0.0;
    public double AirDist = 0.0;
    public int Index = 0;
    public double Feed = 0.0;
    public string MoveLabel = "";
    public double MaxTurnDeg = 0.0;
    public double MaxTurnRate = 0.0;   // deg per mm - the number that bites
    public int MaxTurnAt = -1;
    public List<int> HotSpots = new List<int>();
    public string Status = "";
    public string Log = "";
  }

  // =========================================================================
  // 1.  BUILD THE TIMELINE
  // =========================================================================

  public static Result Run(List<Plane> targets, List<int> moveTypes,
                           double feedProcess, double feedRapid, double dwellSeconds,
                           double t, double toolLength, double toolRadius,
                           double turnRateLimit)
  {
    Result r = new Result();
    StringBuilder log = new StringBuilder();

    if (targets == null || targets.Count < 2)
    {
      r.Status = "FAIL: need at least two targets.";
      r.Log = r.Status;
      return r;
    }
    if (feedProcess <= EPS) feedProcess = 50.0;
    if (feedRapid <= EPS) feedRapid = 250.0;
    if (toolLength <= EPS) toolLength = 100.0;
    if (toolRadius <= EPS) toolRadius = 4.0;
    if (turnRateLimit <= EPS) turnRateLimit = 2.0;

    // --- segments -----------------------------------------------------------
    double clock = 0.0;
    for (int i = 0; i < targets.Count - 1; i++)
    {
      Segment s = new Segment();
      s.FromIndex = i;
      s.A = targets[i];
      s.B = targets[i + 1];
      s.MoveType = MoveTypeAt(moveTypes, i + 1);
      s.Length = s.A.Origin.DistanceTo(s.B.Origin);
      s.Feed = (s.MoveType == 1) ? feedProcess : feedRapid;
      s.Duration = s.Length / s.Feed;
      s.TurnDeg = TurnDegrees(s.A, s.B);

      // A magazine trip is a real pause in the cell, not an instant.
      s.Dwell = (s.MoveType == 2) ? Math.Max(0.0, dwellSeconds) : 0.0;

      s.StartTime = clock;
      clock += s.Duration + s.Dwell;
      r.Segments.Add(s);

      if (s.MoveType == 1) { r.ProcessDist += s.Length; r.ProcessTime += s.Duration; }
      else { r.AirDist += s.Length; r.AirTime += s.Duration; }
      r.DwellTime += s.Dwell;

      if (s.TurnDeg > r.MaxTurnDeg) { r.MaxTurnDeg = s.TurnDeg; r.MaxTurnAt = i; }

      // The real risk is not a big turn, it is a big turn over a short move.
      // That is what makes the wrist accelerate hard enough to fault or gouge.
      double rate = s.Length > 1e-6 ? s.TurnDeg / s.Length : (s.TurnDeg > 1e-9 ? 1e9 : 0.0);
      if (rate > r.MaxTurnRate) r.MaxTurnRate = rate;
      if (rate > turnRateLimit) r.HotSpots.Add(i);
    }
    r.CycleTime = clock;

    // --- where are we now ----------------------------------------------------
    double u = Clamp01(t);
    double now = u * r.CycleTime;
    r.Time = now;

    int seg = FindSegment(r.Segments, now);
    r.Index = seg;
    Segment cur = r.Segments[seg];
    double local = cur.Duration > EPS
      ? Clamp01((now - cur.StartTime) / cur.Duration)
      : 1.0;

    r.Tcp = Interpolate(cur.A, cur.B, local);
    r.Feed = (now - cur.StartTime) > cur.Duration ? 0.0 : cur.Feed;   // 0 while dwelling
    r.MoveLabel = LabelFor(cur.MoveType) + (r.Feed <= EPS ? " (holding at the magazine)" : "");

    // --- trail and remainder --------------------------------------------------
    for (int i = 0; i <= seg; i++) r.Trail.Add(r.Segments[i].A.Origin);
    r.Trail.Add(r.Tcp.Origin);

    r.Remaining.Add(r.Tcp.Origin);
    for (int i = seg + 1; i < r.Segments.Count; i++) r.Remaining.Add(r.Segments[i].A.Origin);
    r.Remaining.Add(r.Segments[r.Segments.Count - 1].B.Origin);

    // --- the tool ---------------------------------------------------------------
    // Z points from the holder into the work, so the body of the tool is behind
    // the tip along -Z. This is the same convention FL-01 and TF-09 emit.
    Point3d tip = r.Tcp.Origin;
    Point3d back = tip - r.Tcp.ZAxis * toolLength;
    r.ToolAxis = new Line(back, tip);
    r.ToolBody = ConeMesh(tip, back, toolRadius, 24);

    // --- verdict -----------------------------------------------------------------
    log.AppendLine("SEGMENTS   " + r.Segments.Count);
    log.AppendLine("PROCESS    " + (r.ProcessDist / 1000.0).ToString("0.###") + " m in " +
                   FormatTime(r.ProcessTime));
    log.AppendLine("AIR        " + (r.AirDist / 1000.0).ToString("0.###") + " m in " +
                   FormatTime(r.AirTime));
    if (r.DwellTime > EPS)
      log.AppendLine("DWELL      " + FormatTime(r.DwellTime) + " at the magazine");
    log.AppendLine("CYCLE      " + FormatTime(r.CycleTime));
    log.AppendLine("NOW        " + FormatTime(r.Time) + "  segment " + seg +
                   " of " + r.Segments.Count + "  " + r.MoveLabel);
    log.AppendLine("TURN       worst " + r.MaxTurnDeg.ToString("0.##") + " deg on one move" +
                   (r.MaxTurnAt >= 0 ? " (segment " + r.MaxTurnAt + ")" : ""));
    log.AppendLine("TURN RATE  worst " + r.MaxTurnRate.ToString("0.###") + " deg/mm" +
                   "   limit " + turnRateLimit.ToString("0.###"));

    if (r.HotSpots.Count > 0)
    {
      log.AppendLine("HOTSPOTS   " + r.HotSpots.Count + " segment(s) turn faster than the limit:");
      log.AppendLine("           " + string.Join(", ", r.HotSpots.Take(20).Select(i => i.ToString())) +
                     (r.HotSpots.Count > 20 ? " ..." : ""));
      r.Status = "OK WITH WARNINGS: " + r.HotSpots.Count +
                 " segment(s) rotate faster than " + turnRateLimit.ToString("0.##") + " deg/mm. " +
                 "Cycle " + FormatTime(r.CycleTime) + ".";
    }
    else
    {
      r.Status = "OK: " + r.Segments.Count + " moves, cycle " + FormatTime(r.CycleTime) +
                 ", worst turn rate " + r.MaxTurnRate.ToString("0.###") + " deg/mm.";
    }

    log.AppendLine("STATUS     " + r.Status);
    r.Log = log.ToString();
    return r;
  }

  // =========================================================================
  // 2.  INTERPOLATION
  //     Position is linear. Orientation takes the shortest arc between the two
  //     frames - the same thing a LIN move does - so what you see mid-segment
  //     is what the robot passes through, not a convenient approximation.
  // =========================================================================

  public static Plane Interpolate(Plane a, Plane b, double u)
  {
    Plane p = a;

    Quaternion q = Quaternion.Rotation(a, b);
    double angle; Vector3d axis;
    if (q.GetRotation(out angle, out axis) && axis.IsValid && !axis.IsZero)
    {
      // Take the short way round.
      if (angle > Math.PI) angle -= 2.0 * Math.PI;
      if (Math.Abs(angle) > 1e-12)
        p.Rotate(angle * u, axis, a.Origin);
    }

    p.Origin = a.Origin + (b.Origin - a.Origin) * u;
    return p;
  }

  private static int FindSegment(List<Segment> segs, double now)
  {
    if (now <= 0) return 0;
    for (int i = 0; i < segs.Count; i++)
    {
      double end = segs[i].StartTime + segs[i].Duration + segs[i].Dwell;
      if (now < end) return i;
    }
    return segs.Count - 1;
  }

  private static int MoveTypeAt(List<int> moveTypes, int i)
  {
    if (moveTypes == null || moveTypes.Count == 0) return 1;
    return moveTypes[Math.Min(i, moveTypes.Count - 1)];
  }

  private static string LabelFor(int moveType)
  {
    if (moveType == 1) return "process move (on the work)";
    if (moveType == 2) return "trip to the magazine";
    return "air move";
  }

  // =========================================================================
  // 3.  THE TOOL BODY
  //     Built by hand rather than with a primitive, so there is no doubt about
  //     which end the apex is at. Apex sits on the TCP; the base is back up the
  //     tool axis. Twenty-four sides is plenty for a shaded preview.
  // =========================================================================

  private static Mesh ConeMesh(Point3d tip, Point3d back, double radius, int sides)
  {
    Mesh m = new Mesh();
    Vector3d axis = tip - back;
    if (!axis.Unitize()) return m;

    Plane basePlane = new Plane(back, axis);
    m.Vertices.Add(tip);                            // 0 = apex
    for (int i = 0; i < sides; i++)
    {
      double a = 2.0 * Math.PI * i / sides;
      m.Vertices.Add(basePlane.PointAt(radius * Math.Cos(a), radius * Math.Sin(a)));
    }
    m.Vertices.Add(back);                           // sides+1 = base centre

    for (int i = 0; i < sides; i++)
    {
      int v0 = 1 + i;
      int v1 = 1 + ((i + 1) % sides);
      m.Faces.AddFace(0, v0, v1);                   // side
      m.Faces.AddFace(sides + 1, v1, v0);           // base cap
    }
    m.Normals.ComputeNormals();
    m.Compact();
    return m;
  }

  // =========================================================================
  // 4.  ODDS AND ENDS
  // =========================================================================

  public static double TurnDegrees(Plane a, Plane b)
  {
    Quaternion q = Quaternion.Rotation(a, b);
    double angle; Vector3d axis;
    if (!q.GetRotation(out angle, out axis)) return 0.0;
    angle = Math.Abs(angle);
    if (angle > Math.PI) angle = 2.0 * Math.PI - angle;
    return RhinoMath.ToDegrees(angle);
  }

  private static double Clamp01(double v)
  {
    if (v < 0) return 0;
    if (v > 1) return 1;
    return v;
  }

  public static string FormatTime(double seconds)
  {
    if (seconds < 60) return seconds.ToString("0.##") + " s";
    int m = (int) (seconds / 60);
    double s = seconds - m * 60;
    if (m < 60) return m + " min " + s.ToString("0") + " s";
    int h = m / 60;
    return h + " h " + (m - h * 60) + " min";
  }
}

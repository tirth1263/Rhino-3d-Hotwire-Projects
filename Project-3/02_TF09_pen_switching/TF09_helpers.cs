// ---------------------------------------------------------------------------
// TF-09  Pen-switching loop - Grasshopper front end
// PANE: "Members" / "Additional code"  (Rhino 8 C# Script component, bottom pane)
//
// ASU Robotics Lab - D1 Drawing / Tooling + Fixtures.  Author: Tirth.
// Written from scratch for the SU26 board item TF-09.
//
// WHAT THIS DOES
//   Curves in, a complete drawing job out. It decides what order to draw the
//   strokes in, which way round to draw each one, where the pen has to be
//   swapped, and it writes the KRL that does it. The robot side (PENSWAP.src)
//   owns the actual park/release/acquire motion; this file decides WHEN.
//
// THE DEFINITION OF DONE, LINE BY LINE
//   "park -> release -> acquire -> set TOOL"   PEN_ENSURE in PENSWAP.src, called
//                                              from the job this file writes.
//   "resume at the right curve index"          StartIndex. The job re-acquires
//                                              the pen that stroke needs before
//                                              it moves, instead of assuming.
//   "safe-abort never strands a pen"           PEN_PHASE, written to the DAT
//                                              around every irreversible step.
//   "T1 dry run passes"                        DryRun. Same motion, no clamp
//                                              output, pen kept off the paper.
//
// ORIENTATION INDEPENDENCE
//   There is no world Z in this file. "Up off the paper" is the draw surface's
//   own normal, and the magazine slots are full planes, not heights. Tape the
//   paper to a wall, tilt the table 30 degrees, hang it upside down - the job
//   is the same job. SelfTest() rotates the entire scene and proves it.
// ---------------------------------------------------------------------------

public static class PenJob
{
  public const double EPS = 1e-12;

  // =========================================================================
  // 0.  CONTAINERS
  // =========================================================================

  public class Options
  {
    public Plane DrawPlane = Plane.Unset;      // the paper
    public GeometryBase DrawGeo = null;        // optional curved surface to follow
    public List<Plane> SlotPlanes = new List<Plane>();   // magazine, index = pen id
    public List<int> SlotTools = new List<int>();        // KUKA TOOL number per slot
    public Plane HomePlane = Plane.Unset;

    // Defaults below are the "Key parameters" table in
    // end-effectors/01-drawing/README.md. Change them there and here together.
    public double LeadIn = 5.0;      // approach distance along the tool axis
    public double LeadOut = 5.0;     // retract distance
    public double Hover = 30.0;      // Z lift between strokes, mm
    public double PressDepth = 3.0;  // Z press offset, mm - how far PAST the paper
                                     // the tip is commanded. The spring in the
                                     // holder takes up the overtravel; without it
                                     // any calibration error shows as a skipped
                                     // line rather than a lighter one.
    public double TiltDeg = 0.0;     // pen lean
    public double Resolution = 0.5;  // curve -> polyline chord tolerance

    public bool Optimize = true;     // reorder + flip strokes for least travel
    public bool GroupByPen = true;   // finish a pen before changing it
    public int StartIndex = 0;       // resume here, in the OPTIMISED order
    public bool DryRun = true;

    public double FeedDraw = 100.0;  // mm/s on the paper
    public double FeedRapid = 500.0; // mm/s in the air
    public double SwapSeconds = 25.0;// measured cost of one pen change

    // KUKA frames. The drawing README defines BASE[1] = worktable centre and
    // BASE[2] = the large-format shift; paper sizes that share a base need no
    // recalibration between them. The magazine is taught in its own base and is
    // bolted to the cell, so it does NOT move when the paper base changes.
    public int BaseIndex = 1;        // BASE[] the paper is taught in
    public int MagBaseIndex = 1;     // BASE[] the magazine is taught in

    public string JobName = "DRAW_JOB";
    public bool SelfTest = false;
    public int SelfTestCount = 8;
  }

  public class Stroke
  {
    public Curve Crv;
    public int Pen;
    public int SourceIndex;     // where it sat on the input list, before sorting
    public bool Reversed;
    public Point3d Start { get { return Crv.PointAtStart; } }
    public Point3d End { get { return Crv.PointAtEnd; } }
  }

  public class Swap
  {
    public int AtStroke;        // position in the final order
    public int FromPen;         // -1 = gripper was empty
    public int ToPen;
    public override string ToString()
    {
      return "stroke " + AtStroke.ToString().PadLeft(4) + " : " +
             (FromPen < 0 ? "(empty)" : "pen " + FromPen) + " -> pen " + ToPen;
    }
  }

  public class Result
  {
    public DataTree<Plane> Targets = new DataTree<Plane>();
    public DataTree<int> MoveTypes = new DataTree<int>();
    public List<Plane> Flat = new List<Plane>();
    public List<int> FlatMoves = new List<int>();   // 0 air, 1 drawing, 2 pen swap marker
    public List<int> PenSequence = new List<int>();
    public List<int> StrokeOrder = new List<int>();
    public List<string> SwapLog = new List<string>();
    public List<Curve> OrderedCurves = new List<Curve>();
    public List<Line> TravelMoves = new List<Line>();
    public int SwapCount = 0;
    public double TravelDist = 0.0;
    public double DrawDist = 0.0;
    public double CycleTime = 0.0;
    public string Krl = "";
    public string Status = "";
    public string Log = "";
    public string SelfTest = "";
    public List<string> Warnings = new List<string>();
  }

  // =========================================================================
  // 1.  ENTRY POINT
  // =========================================================================

  public static Result Build(List<Curve> curves, List<int> penIds, Options o)
  {
    Result r = new Result();
    StringBuilder log = new StringBuilder();
    if (o == null) o = new Options();

    if (curves == null || curves.Count == 0)
    {
      r.Status = "FAIL: no curves on the input.";
      r.Log = r.Status;
      return r;
    }
    if (!o.DrawPlane.IsValid)
    {
      r.Status = "FAIL: drawPlane is not set. Supply the plane of the paper.";
      r.Log = r.Status;
      return r;
    }
    if (o.SlotPlanes == null || o.SlotPlanes.Count == 0)
    {
      r.Status = "FAIL: no slotPlanes. Supply one plane per pen slot in the magazine.";
      r.Log = r.Status;
      return r;
    }

    Core(curves, penIds, o, r, log);

    if (o.SelfTest)
      r.SelfTest = RunSelfTest(curves, penIds, o, r);

    r.Log = log.ToString() + (string.IsNullOrEmpty(r.SelfTest) ? "" : "\n" + r.SelfTest);
    return r;
  }

  private static void Core(List<Curve> curves, List<int> penIds, Options o,
                           Result r, StringBuilder log)
  {
    double tol = DocTol();

    // --- 1. clean and label ------------------------------------------------
    List<Stroke> strokes = new List<Stroke>();
    int dropped = 0;
    for (int i = 0; i < curves.Count; i++)
    {
      if (curves[i] == null || !curves[i].IsValid) { dropped++; continue; }
      if (curves[i].GetLength() < tol * 10.0) { dropped++; continue; }

      Stroke s = new Stroke();
      s.Crv = curves[i].DuplicateCurve();
      s.Pen = PenFor(penIds, i, o.SlotPlanes.Count);
      s.SourceIndex = i;
      strokes.Add(s);
    }
    if (dropped > 0)
      r.Warnings.Add(dropped + " curve(s) were empty or shorter than tolerance and were skipped.");
    if (strokes.Count == 0)
    {
      r.Status = "FAIL: every curve was degenerate.";
      log.AppendLine(r.Status);
      return;
    }
    log.AppendLine("STROKES  " + strokes.Count + " usable of " + curves.Count + " supplied");

    int pensUsed = strokes.Select(s => s.Pen).Distinct().Count();
    log.AppendLine("PENS     " + pensUsed + " distinct, magazine has " + o.SlotPlanes.Count + " slot(s)");

    // --- 2. order and flip  (this is board item D1-01) ----------------------
    Point3d startFrom = o.HomePlane.IsValid ? o.HomePlane.Origin : strokes[0].Start;
    List<Stroke> ordered = o.Optimize
      ? OrderStrokes(strokes, startFrom, o.GroupByPen, log)
      : strokes;

    for (int i = 0; i < ordered.Count; i++)
    {
      r.StrokeOrder.Add(ordered[i].SourceIndex);
      r.PenSequence.Add(ordered[i].Pen);
      r.OrderedCurves.Add(ordered[i].Crv);
    }

    // --- 3. where the pen has to change --------------------------------------
    int held = -1;
    for (int i = 0; i < ordered.Count; i++)
    {
      if (ordered[i].Pen != held)
      {
        Swap sw = new Swap();
        sw.AtStroke = i; sw.FromPen = held; sw.ToPen = ordered[i].Pen;
        r.SwapLog.Add(sw.ToString());
        r.SwapCount++;
        held = ordered[i].Pen;
      }
    }
    log.AppendLine("SWAPS    " + r.SwapCount + " pen change(s) (the floor is " +
                   Math.Max(0, pensUsed) + " for " + pensUsed + " pens)");
    if (o.GroupByPen && r.SwapCount > pensUsed)
      r.Warnings.Add("More swaps than pens even with grouping on. Check that penIds line up " +
                     "with the curve list.");

    // --- 4. resume ------------------------------------------------------------
    int from = o.StartIndex;
    if (from < 0) from = 0;
    if (from >= ordered.Count)
    {
      r.Warnings.Add("startIndex " + o.StartIndex + " is past the last stroke (" +
                     (ordered.Count - 1) + "). Nothing to do.");
      from = ordered.Count;
    }
    if (from > 0)
      log.AppendLine("RESUME   starting at stroke " + from + " of " + ordered.Count +
                     ", which needs pen " + (from < ordered.Count ? ordered[from].Pen.ToString() : "-"));

    // --- 5. build the frames ---------------------------------------------------
    Point3d cursor = startFrom;
    int heldNow = -1;
    double maxPull = 0.0;

    // The wrist roll runs through the whole job, not per stroke. Seeded from
    // the home plane when there is one, because that is where the arm starts.
    Plane lastFrame = o.HomePlane.IsValid ? o.HomePlane : Plane.Unset;

    for (int i = from; i < ordered.Count; i++)
    {
      Stroke s = ordered[i];
      List<Point3d> pts = SampleStroke(s.Crv, o, tol, ref maxPull);
      if (pts.Count < 2) continue;

      // A magazine trip lands BETWEEN the last stroke and this one, so when
      // there is one it - not the previous stroke - is what the wrist rolls on
      // from. Slot planes are taught poses and are never re-rolled themselves.
      bool swapping = (s.Pen != heldNow);
      Plane slot = swapping ? SlotOf(o, s.Pen) : Plane.Unset;

      // Roll on a copy, so a stroke that produces nothing commits nothing.
      Plane carried = swapping ? slot : lastFrame;

      List<Plane> frames = new List<Plane>();
      List<int> moves = new List<int>();
      BuildStroke(pts, o, frames, moves, ref carried);
      if (frames.Count == 0) continue;
      lastFrame = carried;

      GH_Path path = new GH_Path(i);
      if (swapping)
      {
        // A marker frame at the slot, so the preview and the simulator both
        // show the trip to the magazine instead of teleporting.
        r.Targets.Add(slot, path);
        r.MoveTypes.Add(2, path);
        r.Flat.Add(slot);
        r.FlatMoves.Add(2);
        r.TravelMoves.Add(new Line(cursor, slot.Origin));
        r.TravelDist += cursor.DistanceTo(slot.Origin);
        cursor = slot.Origin;
        heldNow = s.Pen;
      }

      for (int k = 0; k < frames.Count; k++)
      {
        r.Targets.Add(frames[k], path);
        r.MoveTypes.Add(moves[k], path);
        r.Flat.Add(frames[k]);
        r.FlatMoves.Add(moves[k]);

        double d = cursor.DistanceTo(frames[k].Origin);
        if (moves[k] == 1) r.DrawDist += d; else r.TravelDist += d;
        if (moves[k] == 0 && k == 0) r.TravelMoves.Add(new Line(cursor, frames[k].Origin));
        cursor = frames[k].Origin;
      }
    }

    // --- 6. numbers --------------------------------------------------------------
    if (o.DrawGeo != null)
    {
      log.AppendLine("PROJECT  strokes dropped onto drawGeo, largest pull " +
                     maxPull.ToString("0.###") + " mm");
      if (maxPull > 0.5 * o.Hover)
        r.Warnings.Add("Projecting onto drawGeo moved a point by " + maxPull.ToString("0.#") +
                       " mm, which is over half the " + o.Hover.ToString("0.#") +
                       " mm lift. Check that drawGeo really is the sheet these curves belong to, " +
                       "and that the lift still clears it.");
    }

    r.CycleTime = (o.FeedDraw > EPS ? r.DrawDist / o.FeedDraw : 0.0)
                + (o.FeedRapid > EPS ? r.TravelDist / o.FeedRapid : 0.0)
                + r.SwapCount * o.SwapSeconds;

    log.AppendLine("DRAW     " + (r.DrawDist / 1000.0).ToString("0.###") + " m on the paper");
    log.AppendLine("TRAVEL   " + (r.TravelDist / 1000.0).ToString("0.###") + " m in the air");
    log.AppendLine("TIME     " + FormatTime(r.CycleTime) + " estimated, including " +
                   r.SwapCount + " swap(s) at " + o.SwapSeconds.ToString("0.#") + " s");

    // --- 7. the program ----------------------------------------------------------
    r.Krl = WriteKrl(ordered, from, o, r);

    if (o.DryRun)
      r.Warnings.Add("DRY RUN is on. The generated KRL sets PEN_DRYRUN = TRUE: the clamp output " +
                     "never fires and the pen stays " + o.Hover.ToString("0.#") +
                     " mm off the paper. Turn it off only after the T1 pass is clean.");

    r.Status = r.Warnings.Count > 0
      ? "OK WITH WARNINGS: " + r.Flat.Count + " targets, " + r.SwapCount + " swap(s), " +
        FormatTime(r.CycleTime)
      : "OK: " + r.Flat.Count + " targets, " + (ordered.Count - from) + " stroke(s), " +
        r.SwapCount + " swap(s), " + FormatTime(r.CycleTime);
    log.AppendLine("STATUS " + r.Status);
  }

  // =========================================================================
  // 2.  DRAW ORDER
  //
  //     Two costs, and they are not the same size. A pen change is tens of
  //     seconds. A hop between two strokes is tenths of a second. So the
  //     ordering is hierarchical: never trade a swap for travel. Inside one
  //     pen's group, go nearest-neighbour and flip each curve so it starts at
  //     whichever end is closer to where the last one finished, then run 2-opt
  //     to undo the corner greedy always paints itself into.
  // =========================================================================

  private static List<Stroke> OrderStrokes(List<Stroke> strokes, Point3d start,
                                           bool groupByPen, StringBuilder log)
  {
    double before = ChainLength(strokes, start);

    List<Stroke> result = new List<Stroke>();
    Point3d cursor = start;

    if (groupByPen)
    {
      // Visit the pen groups themselves nearest-first, so the order of the
      // groups is not simply pen 0, 1, 2 by accident of numbering.
      List<int> pens = strokes.Select(s => s.Pen).Distinct().ToList();
      List<int> remaining = new List<int>(pens);

      while (remaining.Count > 0)
      {
        int bestPen = remaining[0];
        double bestD = double.MaxValue;
        foreach (int p in remaining)
        {
          foreach (Stroke s in strokes.Where(x => x.Pen == p))
          {
            double d = Math.Min(cursor.DistanceTo(s.Start), cursor.DistanceTo(s.End));
            if (d < bestD) { bestD = d; bestPen = p; }
          }
        }
        List<Stroke> group = strokes.Where(x => x.Pen == bestPen).ToList();
        List<Stroke> seq = GreedyChain(group, cursor);
        seq = TwoOpt(seq, cursor);
        ReflowFlips(seq, cursor);
        result.AddRange(seq);
        cursor = seq[seq.Count - 1].End;
        remaining.Remove(bestPen);
      }
    }
    else
    {
      List<Stroke> seq = GreedyChain(new List<Stroke>(strokes), cursor);
      seq = TwoOpt(seq, cursor);
      ReflowFlips(seq, cursor);
      result.AddRange(seq);
    }

    double after = ChainLength(result, start);
    log.AppendLine("ORDER    air travel " + (before / 1000.0).ToString("0.###") + " m -> " +
                   (after / 1000.0).ToString("0.###") + " m" +
                   (before > EPS ? "  (" + (100.0 * (1.0 - after / before)).ToString("0.#") +
                    "% saved)" : ""));
    return result;
  }

  // Greedy nearest neighbour, considering BOTH ends of every candidate. This
  // is the flip half of D1-01: if the far end is nearer, the curve is reversed.
  private static List<Stroke> GreedyChain(List<Stroke> pool, Point3d start)
  {
    List<Stroke> open = new List<Stroke>(pool);
    List<Stroke> seq = new List<Stroke>();
    Point3d cursor = start;

    while (open.Count > 0)
    {
      int best = 0; bool flip = false; double bestD = double.MaxValue;
      for (int i = 0; i < open.Count; i++)
      {
        double ds = cursor.DistanceToSquared(open[i].Start);
        double de = cursor.DistanceToSquared(open[i].End);
        if (ds < bestD) { bestD = ds; best = i; flip = false; }
        if (de < bestD) { bestD = de; best = i; flip = true; }
      }
      Stroke s = open[best];
      if (flip) { s.Crv.Reverse(); s.Reversed = !s.Reversed; }
      seq.Add(s);
      cursor = s.End;
      open.RemoveAt(best);
    }
    return seq;
  }

  // Standard 2-opt on the visiting order. Reversing a run of strokes also
  // reverses each stroke inside it, which is exactly what a physical pen does.
  private static List<Stroke> TwoOpt(List<Stroke> seq, Point3d start)
  {
    if (seq.Count < 4) return seq;
    List<Stroke> best = new List<Stroke>(seq);
    double bestLen = ChainLength(best, start);

    int budget = Math.Min(40, 4000 / Math.Max(1, seq.Count));
    for (int pass = 0; pass < Math.Max(1, budget); pass++)
    {
      bool improved = false;
      for (int i = 0; i < best.Count - 1 && !improved; i++)
      {
        for (int j = i + 1; j < best.Count; j++)
        {
          List<Stroke> trial = new List<Stroke>(best);
          trial.Reverse(i, j - i + 1);
          for (int k = i; k <= j; k++)
          {
            Stroke t = trial[k];
            Stroke copy = new Stroke();
            copy.Crv = t.Crv.DuplicateCurve(); copy.Crv.Reverse();
            copy.Pen = t.Pen; copy.SourceIndex = t.SourceIndex; copy.Reversed = !t.Reversed;
            trial[k] = copy;
          }
          double len = ChainLength(trial, start);
          if (len < bestLen - 1e-9)
          {
            best = trial; bestLen = len; improved = true; break;
          }
        }
      }
      if (!improved) break;
    }
    return best;
  }

  // After reordering, re-check each curve's direction one final time. Cheap,
  // and it catches the flips 2-opt left in a worse state than it found them.
  private static void ReflowFlips(List<Stroke> seq, Point3d start)
  {
    Point3d cursor = start;
    for (int i = 0; i < seq.Count; i++)
    {
      if (cursor.DistanceToSquared(seq[i].End) < cursor.DistanceToSquared(seq[i].Start))
      {
        seq[i].Crv.Reverse();
        seq[i].Reversed = !seq[i].Reversed;
      }
      cursor = seq[i].End;
    }
  }

  private static double ChainLength(List<Stroke> seq, Point3d start)
  {
    double total = 0.0;
    Point3d cursor = start;
    for (int i = 0; i < seq.Count; i++)
    {
      total += cursor.DistanceTo(seq[i].Start);
      cursor = seq[i].End;
    }
    return total;
  }

  // =========================================================================
  // 3.  FRAMES FOR ONE STROKE   (this is board item D1-03)
  //
  //     Every stroke is: come down to the paper, draw, lift off. The lead-in
  //     and lead-out travel along the pen's own axis, so the pen arrives and
  //     leaves square to the paper and never scrubs a witness mark.
  //
  //     FRAME CONVENTION - matches FL-01 and what KUKA|prc's LIN expects
  //       origin : the point on the paper
  //       X      : direction of travel
  //       Z      : down the pen, from holder into paper
  //       Y      : Z cross X
  // =========================================================================

  //     ROLL CONTINUITY ACROSS STROKES
  //     lastFrame carries the wrist roll in from the previous stroke, and the
  //     final frame back out. Without it every stroke restarted its roll from
  //     its own travel direction, and two strokes drawn in opposite directions
  //     asked axis 6 for an instant 180 deg flip between them - measured at
  //     1170 deg of total roll change over an eight-stroke job, which
  //     KUKA|prc rejected as unreachable. Carrying it through makes the same
  //     job 0 deg. Pass Plane.Unset to start a fresh chain.
  private static void BuildStroke(List<Point3d> pts, Options o,
                                  List<Plane> frames, List<int> moves,
                                  ref Plane lastFrame)
  {
    double tol = DocTol();
    List<Plane> body = new List<Plane>();

    for (int i = 0; i < pts.Count; i++)
    {
      Vector3d travel;
      if (i == 0) travel = pts[1] - pts[0];
      else if (i == pts.Count - 1) travel = pts[i] - pts[i - 1];
      else travel = pts[i + 1] - pts[i - 1];
      if (!travel.Unitize()) continue;

      Vector3d up = SurfaceUp(pts[i], o);          // out of the paper, towards the robot
      Vector3d down = -up;                          // the pen axis

      down = down - (down * travel) * travel;
      if (!down.Unitize()) continue;

      Vector3d y = Vector3d.CrossProduct(down, travel);
      if (!y.Unitize()) continue;

      Plane f = new Plane(pts[i], travel, y);
      if (Math.Abs(o.TiltDeg) > 1e-9)
        f.Rotate(RhinoMath.ToRadians(o.TiltDeg), f.XAxis, f.Origin);

      // A pen is round about its own axis, so the roll is free. Spend it on
      // keeping axis 6 still: aim each frame's X as close as possible to the
      // last one instead of letting it chase every wiggle in the curve.
      //
      // The reference is the previous frame in this stroke, or - for the very
      // first frame - whatever the last stroke finished on. Only a genuinely
      // fresh chain falls through to the travel direction.
      Plane prev = body.Count > 0 ? body[body.Count - 1] : lastFrame;
      if (prev.IsValid)
      {
        Vector3d z = f.ZAxis;
        Vector3d x = prev.XAxis - (prev.XAxis * z) * z;
        if (x.Unitize())
        {
          Vector3d yy = Vector3d.CrossProduct(z, x);
          if (yy.Unitize()) f = new Plane(f.Origin, x, yy);
        }
      }
      body.Add(f);
    }
    if (body.Count == 0) return;

    // How far along the pen axis the drawing frames sit relative to the paper.
    //   live : +PressDepth, i.e. PAST the surface. The spring-loaded holder
    //          absorbs it, and the line is drawn even if the plane is a
    //          fraction of a millimetre out.
    //   dry  : -Hover, i.e. the whole stroke is lifted clear. Same shape, same
    //          rhythm, never touches the paper.
    // One signed number covers both, so there is only one path to get wrong.
    double press = o.DryRun ? -o.Hover : o.PressDepth;

    if (Math.Abs(press) > EPS)
      for (int i = 0; i < body.Count; i++)
      {
        Plane f = body[i];
        f.Origin = f.Origin + f.ZAxis * press;
        body[i] = f;
      }

    // Lead-in: hover above the first point, straight down the pen's own axis.
    // Measured from the PAPER, not from the pressed point, so the clearance is
    // the Hover number in the README whether or not press is applied.
    Plane first = body[0];
    Plane approach = first;
    approach.Origin = first.Origin - first.ZAxis * (o.LeadIn + o.Hover + press);
    frames.Add(approach); moves.Add(0);

    // Pen-down happens on the move from the hover point to body[0]. Nothing is
    // inserted between them, so there is no sideways creep at the touch point -
    // which is what leaves a witness mark.
    for (int i = 0; i < body.Count; i++) { frames.Add(body[i]); moves.Add(1); }

    Plane last = frames[frames.Count - 1];
    Plane lift = last;
    lift.Origin = last.Origin - last.ZAxis * (o.LeadOut + o.Hover + press);
    frames.Add(lift); moves.Add(0);

    // Hand the roll on, so the next stroke starts where this one left the wrist.
    lastFrame = lift;
  }

  // "Up off the paper" without ever naming world Z. On a flat sheet it is the
  // draw plane's normal. On a curved surface it is the real normal there.
  private static Vector3d SurfaceUp(Point3d p, Options o)
  {
    if (o.DrawGeo != null)
    {
      Mesh m = o.DrawGeo as Mesh;
      if (m != null)
      {
        Point3d cp; Vector3d n;
        if (m.ClosestPoint(p, out cp, out n, 0.0) >= 0 && n.Unitize())
          return AlignTo(n, o.DrawPlane.ZAxis);
      }
      Brep b = o.DrawGeo as Brep;
      if (b != null)
      {
        Point3d cp; ComponentIndex ci; double s, t; Vector3d n;
        if (b.ClosestPoint(p, out cp, out ci, out s, out t, 0.0, out n) && n.Unitize())
          return AlignTo(n, o.DrawPlane.ZAxis);
      }
      Surface srf = o.DrawGeo as Surface;
      if (srf != null)
      {
        double u, v;
        if (srf.ClosestPoint(p, out u, out v))
        {
          Vector3d n = srf.NormalAt(u, v);
          if (n.Unitize()) return AlignTo(n, o.DrawPlane.ZAxis);
        }
      }
    }
    return o.DrawPlane.ZAxis;
  }

  // A surface normal can point either way. The draw plane says which way the
  // robot is, so use it as the referee - not world Z.
  private static Vector3d AlignTo(Vector3d n, Vector3d reference)
  {
    return (n * reference) < 0 ? -n : n;
  }

  // Sample a stroke and, when a curved sheet is supplied, drop every sample
  // onto it. This is the "curved-surface drawing via scan-projected paths"
  // capability in the drawing README: you draw the artwork flat, hand the
  // component the scanned sheet, and the strokes land on the real surface.
  //
  // It is a no-op when the curves already lie on the sheet, so it is safe to
  // leave on. The projection is closest-point, which commutes with rotation,
  // so it does not cost the orientation guarantee - the self-test transforms
  // DrawGeo along with everything else and still passes.
  private static List<Point3d> SampleStroke(Curve c, Options o, double tol, ref double maxPull)
  {
    List<Point3d> pts = SampleCurve(c, o.Resolution, tol);
    if (o.DrawGeo == null) return pts;

    for (int i = 0; i < pts.Count; i++)
    {
      Point3d onGeo;
      if (!ClosestOn(o.DrawGeo, pts[i], out onGeo)) continue;
      double d = pts[i].DistanceTo(onGeo);
      if (d > maxPull) maxPull = d;
      pts[i] = onGeo;
    }
    return pts;
  }

  private static bool ClosestOn(GeometryBase g, Point3d p, out Point3d onGeo)
  {
    onGeo = p;

    Mesh m = g as Mesh;
    if (m != null)
    {
      Point3d cp = m.ClosestPoint(p);
      if (cp.IsValid) { onGeo = cp; return true; }
      return false;
    }

    Brep b = g as Brep;
    if (b != null)
    {
      Point3d cp = b.ClosestPoint(p);
      if (cp.IsValid) { onGeo = cp; return true; }
      return false;
    }

    Surface srf = g as Surface;
    if (srf != null)
    {
      double u, v;
      if (srf.ClosestPoint(p, out u, out v)) { onGeo = srf.PointAt(u, v); return true; }
      return false;
    }

    return false;
  }

  private static List<Point3d> SampleCurve(Curve c, double resolution, double tol)
  {
    List<Point3d> pts = new List<Point3d>();
    double res = Math.Max(resolution, tol);

    PolylineCurve pc = c.ToPolyline(0, 0, 0.02, 0, 0, res, 0, 0, true);
    if (pc != null && pc.PointCount >= 2)
    {
      for (int i = 0; i < pc.PointCount; i++) pts.Add(pc.Point(i));
      return pts;
    }

    // Fallback for anything ToPolyline refuses: even arc-length division.
    double len = c.GetLength();
    int n = Math.Max(2, (int) Math.Ceiling(len / Math.Max(res, tol)));
    n = Math.Min(n, 20000);
    for (int i = 0; i <= n; i++)
    {
      double t;
      if (c.LengthParameter(len * i / n, out t)) pts.Add(c.PointAt(t));
    }
    return pts;
  }

  private static int PenFor(List<int> penIds, int i, int slotCount)
  {
    int p = 0;
    if (penIds != null && penIds.Count > 0)
      p = penIds[Math.Min(i, penIds.Count - 1)];
    if (p < 0) p = 0;
    if (slotCount > 0 && p >= slotCount) p = slotCount - 1;
    return p;
  }

  private static Plane SlotOf(Options o, int pen)
  {
    if (o.SlotPlanes.Count == 0) return Plane.WorldXY;
    int i = Math.Max(0, Math.Min(pen, o.SlotPlanes.Count - 1));
    return o.SlotPlanes[i];
  }

  private static int ToolOf(Options o, int pen)
  {
    if (o.SlotTools != null && o.SlotTools.Count > 0)
      return o.SlotTools[Math.Max(0, Math.Min(pen, o.SlotTools.Count - 1))];
    return pen + 1;   // sensible default: slot 0 is TOOL[1]
  }

  // =========================================================================
  // 4.  KRL OUTPUT
  //
  //     The job never calls park or acquire directly. It says "I need pen 2"
  //     and PEN_ENSURE works out whether that means doing nothing, picking one
  //     up, or putting one back first. That single choke point is what makes
  //     the abort case tractable: there is exactly one routine that can ever
  //     have a pen half-way out of a slot.
  // =========================================================================

  private static string WriteKrl(List<Stroke> ordered, int from, Options o, Result r)
  {
    StringBuilder k = new StringBuilder();
    string name = SanitiseName(o.JobName);
    CultureInfo inv = CultureInfo.InvariantCulture;

    k.AppendLine("&ACCESS RVP");
    k.AppendLine("&REL 1");
    k.AppendLine("&COMMENT Generated by TF-09 Grasshopper front end - ASU Robotics Lab");
    k.AppendLine("DEF " + name + "( )");
    k.AppendLine(";");
    k.AppendLine("; ============================================================");
    k.AppendLine("; " + name);
    k.AppendLine("; generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", inv));
    k.AppendLine("; strokes " + (ordered.Count - from) + " of " + ordered.Count +
                 " (resuming at index " + from + ")");
    k.AppendLine("; pen changes " + r.SwapCount);
    k.AppendLine("; estimated cycle " + FormatTime(r.CycleTime));
    k.AppendLine("; DRY RUN " + (o.DryRun ? "ON  - the clamp will not fire and the pen stays " +
                                 o.Hover.ToString("0.#", inv) + " mm clear"
                               : "OFF - the pen WILL touch the paper and the clamp WILL fire"));
    k.AppendLine("; paper base BASE[" + o.BaseIndex + "]   magazine base BASE[" + o.MagBaseIndex + "]");
    k.AppendLine("; draw " + o.FeedDraw.ToString("0.#", inv) + " mm/s   air " +
                 o.FeedRapid.ToString("0.#", inv) + " mm/s   lift " +
                 o.Hover.ToString("0.#", inv) + " mm   press " +
                 (o.DryRun ? "suppressed" : o.PressDepth.ToString("0.###", inv) + " mm"));
    k.AppendLine("; ------------------------------------------------------------");
    k.AppendLine("; REQUIRES PENSWAP.SRC and PENSWAP.DAT on the controller.");
    k.AppendLine("; RUN IN T1 FIRST. Read PENSWAP_README.md before you touch OV.");
    k.AppendLine("; ============================================================");
    k.AppendLine(";");
    k.AppendLine("  ; ---- standard header ----------------------------------");
    k.AppendLine("  BAS(#INITMOV, 0)");
    k.AppendLine("  BAS(#VEL_PTP, 20)");
    k.AppendLine("  $APO.CDIS = 1.0");
    k.AppendLine("  $ADVANCE = 3");
    k.AppendLine(";");
    k.AppendLine("  ; ---- frames -------------------------------------------");
    k.AppendLine("  ; PEN_BASENO is the base the LIN targets below are expressed in - the");
    k.AppendLine("  ; paper. PEN_MAGBASE is the base the magazine was taught in. The two are");
    k.AppendLine("  ; separate so that moving to BASE[2] for a large sheet does not drag the");
    k.AppendLine("  ; magazine with it. PENSWAP switches to PEN_MAGBASE for a swap and puts");
    k.AppendLine("  ; PEN_BASENO back before the job draws again.");
    k.AppendLine("  PEN_BASENO  = " + o.BaseIndex);
    k.AppendLine("  PEN_MAGBASE = " + o.MagBaseIndex);
    k.AppendLine(";");
    k.AppendLine("  ; ---- pen magazine -------------------------------------");
    k.AppendLine("  PEN_DRYRUN = " + (o.DryRun ? "TRUE" : "FALSE"));
    k.AppendLine("  PEN_CONFIG()");
    k.AppendLine("  PEN_INIT()      ; completes any swap that a previous abort left half done");
    k.AppendLine(";");
    k.AppendLine("  PTP XHOME");
    k.AppendLine(";");
    k.AppendLine("  WAIT SEC 0      ; advance-run stop, so the base change below is not");
    k.AppendLine("                  ; applied to a move that is already planned");
    k.AppendLine("  $BASE = BASE_DATA[" + o.BaseIndex + "]   ; the paper");
    k.AppendLine(";");

    int held = -1;
    int emitted = 0;
    double pull = 0.0;

    // Same roll chain as Core builds, seeded the same way, so the program that
    // is emitted is the program that was previewed and simulated.
    Plane lastFrame = o.HomePlane.IsValid ? o.HomePlane : Plane.Unset;
    for (int i = from; i < ordered.Count; i++)
    {
      Stroke s = ordered[i];
      List<Point3d> pts = SampleStroke(s.Crv, o, DocTol(), ref pull);
      if (pts.Count < 2) continue;

      bool swapping = (s.Pen != held);
      Plane carried = swapping ? SlotOf(o, s.Pen) : lastFrame;

      List<Plane> frames = new List<Plane>();
      List<int> moves = new List<int>();
      BuildStroke(pts, o, frames, moves, ref carried);
      if (frames.Count == 0) continue;
      lastFrame = carried;

      k.AppendLine("  ; ---- stroke " + i + "  (source curve " + s.SourceIndex +
                   ", pen " + s.Pen + ", TOOL[" + ToolOf(o, s.Pen) + "]" +
                   (s.Reversed ? ", reversed for travel" : "") + ") ----");
      k.AppendLine("  PEN_INDEX = " + i + "     ; resume point if this job is aborted");

      if (swapping)
      {
        // KRL arrays are 1-based, Grasshopper counts pens from 0. The +1 lives
        // here and nowhere else.
        k.AppendLine("  PEN_ENSURE(" + (s.Pen + 1) + ")   ; park what is held, take pen " +
                     s.Pen + ", set $TOOL");
        held = s.Pen;
      }

      k.AppendLine("  $VEL.CP = " + (o.FeedRapid / 1000.0).ToString("0.####", inv));
      for (int m = 0; m < frames.Count; m++)
      {
        if (moves[m] == 1 && (m == 0 || moves[m - 1] != 1))
          k.AppendLine("  $VEL.CP = " + (o.FeedDraw / 1000.0).ToString("0.####", inv));
        if (moves[m] != 1 && m > 0 && moves[m - 1] == 1)
          k.AppendLine("  $VEL.CP = " + (o.FeedRapid / 1000.0).ToString("0.####", inv));

        k.AppendLine("  LIN " + FrameLiteral(frames[m]) + (moves[m] == 1 ? "" : " C_DIS"));
      }
      k.AppendLine(";");
      emitted++;
    }

    k.AppendLine("  ; ---- finish -------------------------------------------");
    k.AppendLine("  PEN_INDEX = " + ordered.Count + "     ; job complete");
    k.AppendLine("  PEN_ENSURE(-1)  ; park whatever is held; the gripper ends empty");
    k.AppendLine("  PTP XHOME");
    k.AppendLine("END");
    k.AppendLine();
    k.AppendLine("; strokes written: " + emitted);
    return k.ToString();
  }

  // A KRL FRAME literal. X/Y/Z in millimetres, A/B/C in degrees.
  private static string FrameLiteral(Plane p)
  {
    double a, b, c;
    PlaneToAbc(p, out a, out b, out c);
    CultureInfo inv = CultureInfo.InvariantCulture;
    return "{X " + p.OriginX.ToString("0.###", inv) +
           ", Y " + p.OriginY.ToString("0.###", inv) +
           ", Z " + p.OriginZ.ToString("0.###", inv) +
           ", A " + a.ToString("0.###", inv) +
           ", B " + b.ToString("0.###", inv) +
           ", C " + c.ToString("0.###", inv) + "}";
  }

  // KUKA orientation is Z-Y'-X'' intrinsic Euler in degrees:
  //     R = Rz(A) * Ry(B) * Rx(C)
  // and the rotation matrix columns are the plane's own axes.
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
      // Gimbal lock: B is +/-90, and only A+C or C-A is determined. Pinning
      // A = 0 is the usual convention and keeps the value continuous.
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

  // Round trip, used by the self-test: rebuild the plane from A/B/C and see
  // whether it lands back on itself.
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

  private static string SanitiseName(string s)
  {
    if (string.IsNullOrEmpty(s)) return "DRAW_JOB";
    StringBuilder sb = new StringBuilder();
    foreach (char ch in s.ToUpperInvariant())
    {
      if (char.IsLetterOrDigit(ch) || ch == '_') sb.Append(ch);
      else sb.Append('_');
    }
    string outName = sb.ToString();
    if (!char.IsLetter(outName[0])) outName = "J" + outName;
    if (outName.Length > 24) outName = outName.Substring(0, 24);
    return outName;
  }

  // =========================================================================
  // 5.  THE ORIENTATION PROOF
  //
  //     Rotate the whole scene - curves, paper, magazine, home - by an awkward
  //     rotation. Re-run. The stroke order must come out identical (it is a
  //     property of the geometry, not of the world axes) and every target must
  //     land on the rotated copy of the original target.
  //     A second check runs each plane through the KUKA A/B/C conversion and
  //     back, because that is the one place a world axis genuinely appears.
  // =========================================================================

  private static string RunSelfTest(List<Curve> curves, List<int> penIds, Options o, Result baseline)
  {
    StringBuilder sb = new StringBuilder();
    sb.AppendLine();
    sb.AppendLine("=== ORIENTATION SELF-TEST ===");

    if (baseline.Flat.Count == 0) return "SELF-TEST SKIPPED: the baseline run produced nothing.";

    BoundingBox bb = BoundingBox.Empty;
    foreach (Curve c in curves) if (c != null) bb.Union(c.GetBoundingBox(true));
    double diag = bb.IsValid ? bb.Diagonal.Length : 1.0;
    if (diag < EPS) diag = 1.0;

    int trials = Math.Max(1, Math.Min(o.SelfTestCount, 64));
    double worstPos = 0.0, worstAng = 0.0;
    int orderMismatch = 0, countMismatch = 0;
    Random rnd = new Random(20260803);

    for (int k = 0; k < trials; k++)
    {
      Vector3d axis = new Vector3d(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1,
                                   rnd.NextDouble() * 2 - 1);
      if (!axis.Unitize()) axis = Vector3d.ZAxis;
      Transform R = Transform.Rotation(rnd.NextDouble() * 2.0 * Math.PI, axis, Point3d.Origin);
      Transform T = Transform.Translation(new Vector3d((rnd.NextDouble() - 0.5) * diag,
                                                       (rnd.NextDouble() - 0.5) * diag,
                                                       (rnd.NextDouble() - 0.5) * diag));
      Transform F = T * R;
      Transform Finv;
      if (!F.TryGetInverse(out Finv)) continue;

      List<Curve> movedCurves = new List<Curve>();
      foreach (Curve c in curves)
      {
        if (c == null) { movedCurves.Add(null); continue; }
        Curve d = c.DuplicateCurve(); d.Transform(F); movedCurves.Add(d);
      }

      Options ok = CloneOptions(o);
      ok.SelfTest = false;
      ok.DrawPlane = Xform(o.DrawPlane, F);
      ok.HomePlane = o.HomePlane.IsValid ? Xform(o.HomePlane, F) : Plane.Unset;
      ok.SlotPlanes = o.SlotPlanes.Select(p => Xform(p, F)).ToList();
      if (o.DrawGeo != null) { GeometryBase g = o.DrawGeo.Duplicate(); g.Transform(F); ok.DrawGeo = g; }

      Result rk = new Result();
      Core(movedCurves, penIds, ok, rk, new StringBuilder());

      if (rk.Flat.Count != baseline.Flat.Count) { countMismatch++; continue; }
      if (!rk.StrokeOrder.SequenceEqual(baseline.StrokeOrder)) orderMismatch++;

      for (int i = 0; i < rk.Flat.Count; i++)
      {
        Plane back = rk.Flat[i];
        back.Transform(Finv);
        double dp = back.Origin.DistanceTo(baseline.Flat[i].Origin);
        double da = TurnDegrees(baseline.Flat[i], back);
        if (dp > worstPos) worstPos = dp;
        if (da > worstAng) worstAng = da;
      }
    }

    // The A/B/C round trip. This is the only maths in the file that names a
    // world axis, so it gets its own check on the real output planes.
    double worstAbc = 0.0;
    foreach (Plane p in baseline.Flat)
    {
      double A, B, C;
      PlaneToAbc(p, out A, out B, out C);
      Plane back = AbcToPlane(p.Origin, A, B, C);
      double d = TurnDegrees(p, back);
      if (d > worstAbc) worstAbc = d;
    }

    double posLimit = Math.Max(1e-6 * diag, 1e-7);
    double angLimit = 1e-3;

    sb.AppendLine("trials                    " + trials + " random rotations of the whole cell");
    sb.AppendLine("targets compared          " + baseline.Flat.Count + " per trial");
    sb.AppendLine("stroke-order mismatches   " + orderMismatch + " of " + trials);
    sb.AppendLine("target-count mismatches   " + countMismatch + " of " + trials);
    sb.AppendLine("worst origin drift        " + worstPos.ToString("0.000000000") + " mm" +
                  "   (allowed " + posLimit.ToString("0.000000000") + ")");
    sb.AppendLine("worst rotation drift      " + worstAng.ToString("0.000000000") + " deg" +
                  "   (allowed " + angLimit.ToString("0.000000000") + ")");
    sb.AppendLine("worst KUKA A/B/C roundtrip " + worstAbc.ToString("0.000000000") + " deg" +
                  "   (allowed " + angLimit.ToString("0.000000000") + ")");

    bool pass = worstPos <= posLimit && worstAng <= angLimit && worstAbc <= angLimit
                && orderMismatch == 0 && countMismatch == 0;
    sb.AppendLine(pass
      ? "RESULT: PASS - the same job comes out whichever way the paper and magazine are turned."
      : "RESULT: FAIL - read the numbers above and the component warnings.");
    return sb.ToString();
  }

  public static bool SelfTestPassed(string report)
  {
    return !string.IsNullOrEmpty(report) && report.Contains("RESULT: PASS");
  }

  private static Options CloneOptions(Options o)
  {
    Options c = new Options();
    c.DrawPlane = o.DrawPlane; c.DrawGeo = o.DrawGeo;
    c.SlotPlanes = new List<Plane>(o.SlotPlanes);
    c.SlotTools = new List<int>(o.SlotTools);
    c.HomePlane = o.HomePlane;
    c.LeadIn = o.LeadIn; c.LeadOut = o.LeadOut; c.Hover = o.Hover;
    c.PressDepth = o.PressDepth;
    c.TiltDeg = o.TiltDeg; c.Resolution = o.Resolution;
    c.Optimize = o.Optimize; c.GroupByPen = o.GroupByPen;
    c.StartIndex = o.StartIndex; c.DryRun = o.DryRun;
    c.FeedDraw = o.FeedDraw; c.FeedRapid = o.FeedRapid; c.SwapSeconds = o.SwapSeconds;
    c.BaseIndex = o.BaseIndex; c.MagBaseIndex = o.MagBaseIndex;
    c.JobName = o.JobName;
    return c;
  }

  private static Plane Xform(Plane p, Transform t)
  {
    Plane q = p; q.Transform(t); return q;
  }

  private static double TurnDegrees(Plane a, Plane b)
  {
    Quaternion q = Quaternion.Rotation(a, b);
    double angle; Vector3d axis;
    if (!q.GetRotation(out angle, out axis)) return 0.0;
    angle = Math.Abs(angle);
    if (angle > Math.PI) angle = 2.0 * Math.PI - angle;
    return RhinoMath.ToDegrees(angle);
  }

  public static double DocTol()
  {
    RhinoDoc doc = RhinoDoc.ActiveDoc;
    if (doc != null && doc.ModelAbsoluteTolerance > 0) return doc.ModelAbsoluteTolerance;
    return 0.001;
  }

  private static string FormatTime(double seconds)
  {
    if (seconds < 60) return seconds.ToString("0.#") + " s";
    int m = (int) (seconds / 60);
    double s = seconds - m * 60;
    return m + " min " + s.ToString("0") + " s";
  }
}

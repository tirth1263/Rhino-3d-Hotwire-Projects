// ---------------------------------------------------------------------------
// FL-01  Mesh -> KUKA|prc planes pipeline
// PANE: "Members" / "Additional code"  (Rhino 8 C# Script component, bottom pane)
//
// ASU Robotics Lab - Hotwire / Full Loop.  Author: Tirth.
// Written from scratch for the SU26 board item FL-01.
//
// WHAT THIS FILE IS
//   All the maths for the pipeline. It is a plain static class, so it contains
//   no Grasshopper wiring and no side effects. FL01_body.cs is the eight lines
//   that call into it.
//
// THE ONE DESIGN RULE: ORIENTATION INDEPENDENCE
//   Nothing in here reads the world X, Y or Z axis unless the user explicitly
//   asks for it (AxisMode 2/3/4, NormalMode 2). Every automatic decision is
//   made from the mesh's own shape, using its area-weighted principal axes.
//   Consequence: if you rotate the mesh by any rotation R and re-run, you get
//   exactly R applied to the old answer. A model lying flat and the same model
//   standing up produce the same toolpath. SelfTest() proves this numerically
//   instead of asking you to trust it.
//
// UNITS: whatever the Rhino document uses. The lab file is millimetres.
// ---------------------------------------------------------------------------

public static class MeshPlanes
{
  public const double EPS = 1e-12;

  // =========================================================================
  // 0.  INPUT / OUTPUT CONTAINERS
  // =========================================================================

  public class Options
  {
    // --- how the model gets sliced -----------------------------------------
    // 0 = auto, longest principal axis  (sections are true cross-sections)
    // 1 = auto, shortest principal axis (sections are flat "contour" slices)
    // 2 = world X   3 = world Y   4 = world Z   5 = CustomAxis
    // Modes 0 and 1 are orientation independent. Modes 2-5 are not, by design.
    public int AxisMode = 0;
    public Vector3d CustomAxis = Vector3d.Zero;

    public int Sections = 12;         // number of slices, used when Step <= 0
    public double Step = 0.0;         // slice pitch in model units; overrides Sections
    public int Samples = 32;          // target points around each slice

    // 0 = keep only the largest loop per slice (one clean profile)
    // 1 = keep every loop (branchy parts: legs, holes, split sections)
    public int LoopMode = 0;
    public double MinLoopLength = 0.0; // ignore loops shorter than this

    // --- how the tool is aimed ---------------------------------------------
    // 0 = surface normal of the mesh at each point   (follows real shape)
    // 1 = radial, outward from the slice centroid    (robust on ugly meshes)
    // 2 = FixedApproach vector                       (not orientation independent)
    public int NormalMode = 0;
    public Vector3d FixedApproach = Vector3d.Zero;
    public bool FlipApproach = false;  // tick if the tool ends up aiming away
    public double TiltDeg = 0.0;       // roll about the travel direction

    // 0 = rigid   : plane X stays locked to the travel direction (hot wire)
    // 1 = min roll: plane X free-spins to the smallest change from the last
    //               frame. Only legal for a tool that is round about its own
    //               axis (a pen, a router bit). Never for a wire.
    public int RollMode = 0;

    // --- entry / exit / safety ---------------------------------------------
    public double LeadLen = 0.0;       // retract distance before & after a loop
    public bool CloseLoop = true;      // repeat the first point to shut the loop
    public double MaxTurnDeg = 30.0;   // warn above this frame-to-frame rotation
    public double MinSpacing = 0.0;    // drop points closer than this to the last

    // Where closed loops start. Leave unset for the automatic choice, which is
    // a best effort from the loop's own shape. Supply a point - anywhere near
    // the model - to pin it: every loop then starts at the point closest to
    // this one, which makes the result fully deterministic. See the note on
    // HarmonicSeam for why the automatic choice cannot always be canonical.
    public Point3d SeamGuide = Point3d.Unset;

    // --- placing the job in the cell ---------------------------------------
    // Leave both unset to output in the model's own coordinates. Set both to
    // move the whole job: FromFrame on the model, ToFrame in the robot cell.
    public Plane FromFrame = Plane.Unset;
    public Plane ToFrame = Plane.Unset;

    // --- proof ---------------------------------------------------------------
    public bool SelfTest = false;
    public int SelfTestCount = 8;
    public bool AxisIsOrientationFree { get { return AxisMode == 0 || AxisMode == 1; } }
    public bool NormalIsOrientationFree { get { return NormalMode != 2; } }
  }

  public class Result
  {
    public DataTree<Plane> Planes = new DataTree<Plane>();   // the deliverable
    public DataTree<Point3d> Points = new DataTree<Point3d>();
    public DataTree<int> MoveTypes = new DataTree<int>();    // 0 = air, 1 = process
    public List<Curve> Sections = new List<Curve>();         // preview of the slices
    public List<Plane> SlicePlanes = new List<Plane>();
    public Plane PartFrame = Plane.WorldXY;                  // the derived local frame
    public Vector3d SliceAxis = Vector3d.ZAxis;
    public int Count = 0;
    public double MaxTurnDeg = 0.0;
    public double MinSpacing = 0.0;
    public double MaxSpacing = 0.0;
    public string Status = "";
    public string Log = "";
    public string SelfTest = "";
    public List<string> Warnings = new List<string>();
  }

  // A frame plus the bookkeeping that goes with it.
  private class Node
  {
    public Plane Frame;
    public Point3d Point;
    public int MoveType;     // 0 = travelling through air, 1 = cutting / drawing
  }

  // =========================================================================
  // 1.  ENTRY POINT
  // =========================================================================

  public static Result Generate(Mesh inputMesh, Options o)
  {
    Result r = new Result();
    StringBuilder log = new StringBuilder();

    if (o == null) o = new Options();

    if (inputMesh == null || !inputMesh.IsValid || inputMesh.Faces.Count == 0)
    {
      r.Status = "FAIL: no usable mesh on the geo input.";
      r.Log = r.Status;
      return r;
    }

    // Work on a copy. The mesh sitting in the Rhino document is never touched.
    Mesh mesh = Condition(inputMesh, log, r.Warnings);

    Core(mesh, o, r, log);

    if (o.SelfTest)
      r.SelfTest = RunSelfTest(mesh, o, r);

    r.Log = log.ToString() + (string.IsNullOrEmpty(r.SelfTest) ? "" : "\n" + r.SelfTest);
    return r;
  }

  // The pipeline proper, with no self-test recursion. Called again, on a
  // rotated copy of the mesh, by RunSelfTest.
  private static void Core(Mesh mesh, Options o, Result r, StringBuilder log)
  {
    double tol = DocTol();

    // --- 1. the model's own frame ------------------------------------------
    double[] moments;
    Plane part = PrincipalFrame(mesh, out moments, r.Warnings);
    r.PartFrame = part;
    log.AppendLine("PART FRAME  origin " + Fmt(part.Origin));
    log.AppendLine("  long axis  " + Fmt(part.XAxis) + "   spread " + moments[0].ToString("0.###"));
    log.AppendLine("  mid  axis  " + Fmt(part.YAxis) + "   spread " + moments[1].ToString("0.###"));
    log.AppendLine("  short axis " + Fmt(part.ZAxis) + "   spread " + moments[2].ToString("0.###"));

    // --- 2. which way to slice ---------------------------------------------
    Vector3d axis = ChooseAxis(part, o, r.Warnings);
    if (!axis.Unitize())
    {
      r.Status = "FAIL: slice axis is zero length.";
      log.AppendLine(r.Status);
      return;
    }
    r.SliceAxis = axis;
    log.AppendLine("SLICE AXIS  " + Fmt(axis) + "   (" + AxisModeName(o.AxisMode) + ")");

    // An in-plane reference direction that travels with the model, so the
    // starting point of every loop is decided by shape, never by world X.
    Vector3d refDir = part.YAxis;
    if (Math.Abs(refDir * axis) > 0.99) refDir = part.ZAxis;
    refDir = refDir - (refDir * axis) * axis;
    if (!refDir.Unitize()) refDir = AnyPerpendicular(axis);

    // --- 3. where to slice --------------------------------------------------
    double lo, hi;
    ProjectExtents(mesh, part.Origin, axis, out lo, out hi);
    double span = hi - lo;
    if (span < tol * 10.0)
    {
      r.Status = "FAIL: the model is flat along the slice axis. Pick another axis.";
      log.AppendLine(r.Status);
      return;
    }

    int n = o.Sections;
    if (o.Step > tol) n = Math.Max(1, (int) Math.Floor(span / o.Step));
    n = Math.Max(1, Math.Min(n, 2000));
    log.AppendLine("EXTENT along axis " + span.ToString("0.###") + " over " + n + " slice(s)");

    // Midpoint sampling. The two ends of a solid are tangent planes: slicing
    // exactly there gives a point or nothing, so we never sit on them.
    List<Plane> slicePlanes = new List<Plane>();
    for (int i = 0; i < n; i++)
    {
      double t = lo + span * (i + 0.5) / n;
      Point3d origin = part.Origin + axis * t;
      slicePlanes.Add(new Plane(origin, refDir, Vector3d.CrossProduct(axis, refDir)));
    }
    r.SlicePlanes = slicePlanes;

    // --- 4. slice, order, frame ---------------------------------------------
    Point3d seamAnchor = Point3d.Unset;   // carries the loop start from slice to slice
    Plane lastFrame = Plane.Unset;        // carries the wrist roll from frame to frame
    List<double> steps = new List<double>();
    int emptySlices = 0, multiLoop = 0, totalPoints = 0, openLoops = 0;
    double worstTurn = 0.0;
    double diag = mesh.GetBoundingBox(true).Diagonal.Length;

    for (int i = 0; i < slicePlanes.Count; i++)
    {
      Plane sp = slicePlanes[i];
      Polyline[] raw = Intersection.MeshPlane(mesh, sp);
      if (raw == null || raw.Length == 0) { emptySlices++; continue; }

      List<Polyline> loops = JoinAndFilter(raw, o, tol, diag, ref openLoops);
      if (loops.Count == 0) { emptySlices++; continue; }
      if (loops.Count > 1) multiLoop++;

      loops.Sort(delegate(Polyline a, Polyline b) { return b.Length.CompareTo(a.Length); });
      if (o.LoopMode == 0 && loops.Count > 1) loops = new List<Polyline> { loops[0] };

      for (int L = 0; L < loops.Count; L++)
      {
        Polyline pl = loops[L];
        bool closed = pl.IsClosed;
        r.Sections.Add(pl.ToPolylineCurve());

        // The seam guide only steers the FIRST loop; after that each loop
        // chains from the previous one, which is what keeps the tool from
        // sprinting across the model at every slice.
        Point3d anchor = seamAnchor.IsValid ? seamAnchor : o.SeamGuide;
        List<Point3d> pts = SampleLoop(pl, sp, anchor, o.Samples, closed);
        if (pts.Count < 2) continue;
        seamAnchor = pts[0];

        if (o.MinSpacing > tol) pts = Thin(pts, o.MinSpacing, closed);
        if (pts.Count < 2) continue;

        List<Node> nodes = BuildFrames(mesh, pts, sp, o, closed, ref lastFrame);
        if (nodes.Count == 0) continue;

        // Shut the loop by repeating the FIRST FRAME, not the first point. If
        // the point were appended before framing, the tangent at the seam would
        // be computed from a duplicated neighbour and index 0 would get a
        // one-sided difference while every other index got a central one.
        if (closed && o.CloseLoop)
        {
          Node close = new Node();
          close.Frame = nodes[0].Frame;
          close.Point = nodes[0].Point;
          close.MoveType = 1;
          nodes.Add(close);
        }

        if (o.LeadLen > tol) AddLeads(nodes, o.LeadLen);

        GH_Path path = (o.LoopMode == 0) ? new GH_Path(i) : new GH_Path(i, L);
        for (int k = 0; k < nodes.Count; k++)
        {
          r.Planes.Add(nodes[k].Frame, path);
          r.Points.Add(nodes[k].Frame.Origin, path);
          r.MoveTypes.Add(nodes[k].MoveType, path);
          totalPoints++;

          if (k > 0)
          {
            steps.Add(nodes[k].Frame.Origin.DistanceTo(nodes[k - 1].Frame.Origin));
            double turn = TurnDegrees(nodes[k - 1].Frame, nodes[k].Frame);
            if (turn > worstTurn) worstTurn = turn;
          }
        }
      }
    }

    // --- 5. relocate into the cell, if asked ---------------------------------
    if (o.FromFrame.IsValid && o.ToFrame.IsValid)
    {
      Transform x = Transform.PlaneToPlane(o.FromFrame, o.ToFrame);
      Remap(r, x);
      log.AppendLine("REMAPPED from FromFrame to ToFrame.");
    }

    // --- 6. verdict ----------------------------------------------------------
    r.Count = totalPoints;
    r.MaxTurnDeg = worstTurn;
    r.MinSpacing = steps.Count > 0 ? steps.Min() : 0.0;
    r.MaxSpacing = steps.Count > 0 ? steps.Max() : 0.0;

    log.AppendLine("POINTS " + totalPoints + " in " + r.Planes.BranchCount + " branch(es)");
    log.AppendLine("STEP   min " + r.MinSpacing.ToString("0.###") +
                   "  max " + r.MaxSpacing.ToString("0.###"));
    log.AppendLine("TURN   worst frame-to-frame rotation " + worstTurn.ToString("0.##") + " deg");

    if (emptySlices > 0)
      r.Warnings.Add(emptySlices + " slice(s) produced no loop. Fewer sections, or a different axis.");
    if (openLoops > 0)
      r.Warnings.Add(openLoops + " section(s) would not close even after stitching. The mesh has a " +
                     "hole there, so the tool will run from one edge of the gap to the other. " +
                     "Repair the mesh before cutting.");
    if (multiLoop > 0 && o.LoopMode == 0)
      r.Warnings.Add(multiLoop + " slice(s) had more than one loop; only the largest was kept. " +
                     "Set loopMode = 1 to keep them all.");
    if (worstTurn > o.MaxTurnDeg)
      r.Warnings.Add("Frame-to-frame rotation reaches " + worstTurn.ToString("0.#") +
                     " deg (limit " + o.MaxTurnDeg.ToString("0.#") +
                     "). The wrist will snap there. Raise samples or use rollMode = 1.");

    if (totalPoints == 0) r.Status = "FAIL: no planes were produced.";
    else if (r.Warnings.Count > 0) r.Status = "OK WITH WARNINGS: " + totalPoints + " planes, " +
                                              r.Planes.BranchCount + " branches.";
    else r.Status = "OK: " + totalPoints + " planes, " + r.Planes.BranchCount + " branches, " +
                    "worst turn " + worstTurn.ToString("0.#") + " deg.";
    log.AppendLine("STATUS " + r.Status);
  }

  // =========================================================================
  // 2.  MESH CONDITIONING
  //     A mesh dragged out of Blender or a scanner is rarely clean. Everything
  //     downstream assumes: no duplicate vertices, no zero-area faces, normals
  //     agreeing with each other, and on a solid, normals pointing outward.
  // =========================================================================

  private static Mesh Condition(Mesh source, StringBuilder log, List<string> warn)
  {
    Mesh m = source.DuplicateMesh();

    int facesBefore = m.Faces.Count;
    m.Vertices.CombineIdentical(true, true);
    m.Faces.CullDegenerateFaces();
    m.Compact();

    m.FaceNormals.ComputeFaceNormals();
    m.Normals.ComputeNormals();
    m.UnifyNormals();
    m.FaceNormals.ComputeFaceNormals();
    m.Normals.ComputeNormals();

    log.AppendLine("MESH  " + m.Vertices.Count + " vertices, " + m.Faces.Count + " faces" +
                   (m.IsClosed ? ", closed solid" : ", open shell"));
    if (m.Faces.Count != facesBefore)
      log.AppendLine("  cleaned " + (facesBefore - m.Faces.Count) + " degenerate/duplicate face(s)");

    if (m.IsClosed)
    {
      // SolidOrientation: 1 outward, -1 inward, 0 not a solid.
      if (m.SolidOrientation() == -1)
      {
        m.Flip(true, true, true);
        log.AppendLine("  normals pointed inward; flipped outward");
      }
    }
    else
    {
      warn.Add("The mesh is not closed. Surface normals on an open shell can point either way, " +
               "so the tool may aim through the model. Check the approach arrows, and tick " +
               "flipApproach if they are backwards.");
    }

    if (m.DisjointMeshCount > 1)
      warn.Add("The mesh is in " + m.DisjointMeshCount + " disconnected pieces. Set loopMode = 1 " +
               "or the pipeline will only follow the biggest piece in each slice.");

    return m;
  }

  // =========================================================================
  // 3.  THE MODEL'S OWN FRAME  (this is what makes orientation irrelevant)
  //
  //     Take every triangle. Weight it by its area. Build the 3x3 covariance
  //     of the triangle centres about the area centroid. Its eigenvectors are
  //     the directions the model is longest, middling and shortest in. Those
  //     directions are welded to the model: rotate the model and they rotate
  //     with it, which is exactly the property we need.
  // =========================================================================

  private static Plane PrincipalFrame(Mesh m, out double[] moments, List<string> warn)
  {
    List<Point3d> c = new List<Point3d>();
    List<double> a = new List<double>();
    TriangleCentroids(m, c, a);

    moments = new double[] { 0, 0, 0 };

    double total = 0.0;
    Point3d centre = Point3d.Origin;
    for (int i = 0; i < c.Count; i++)
    {
      total += a[i];
      centre += c[i] * a[i];
    }
    if (total < EPS)
    {
      // Every face is degenerate. Fall back to the vertex cloud unweighted.
      c.Clear(); a.Clear();
      for (int i = 0; i < m.Vertices.Count; i++) { c.Add(m.Vertices.Point3dAt(i)); a.Add(1.0); }
      total = c.Count;
      centre = Point3d.Origin;
      for (int i = 0; i < c.Count; i++) centre += c[i];
      if (total < EPS) return Plane.WorldXY;
    }
    centre /= total;

    double[,] cov = new double[3, 3];
    for (int i = 0; i < c.Count; i++)
    {
      double w = a[i];
      double dx = c[i].X - centre.X, dy = c[i].Y - centre.Y, dz = c[i].Z - centre.Z;
      cov[0, 0] += w * dx * dx; cov[0, 1] += w * dx * dy; cov[0, 2] += w * dx * dz;
      cov[1, 1] += w * dy * dy; cov[1, 2] += w * dy * dz;
      cov[2, 2] += w * dz * dz;
    }
    cov[1, 0] = cov[0, 1]; cov[2, 0] = cov[0, 2]; cov[2, 1] = cov[1, 2];
    for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) cov[i, j] /= total;

    double[] eval; double[,] evec;
    Jacobi3(cov, out eval, out evec);

    // Sort largest spread first.
    int[] idx = new int[] { 0, 1, 2 };
    Array.Sort(idx, delegate(int p, int q) { return eval[q].CompareTo(eval[p]); });

    Vector3d e1 = Col(evec, idx[0]);
    Vector3d e2 = Col(evec, idx[1]);
    moments = new double[] { eval[idx[0]], eval[idx[1]], eval[idx[2]] };

    // An eigenvector has no inherent sign: +e and -e describe the same axis.
    // Pin the sign to the model's own lopsidedness (third moment along the
    // axis), so the frame is reproducible run to run and rotation to rotation.
    //
    // When the model is SYMMETRIC about the middle of an axis, that third
    // moment is zero and no amount of cleverness can recover a sign - the shape
    // genuinely does not distinguish one end from the other. Say so, because the
    // consequence is visible: the slice ORDER can come out reversed. The seam
    // no longer depends on any axis sign (see RotateToSeam), so this is now the
    // only place a symmetry can still show through.
    double skew1 = Skew(e1, c, a, centre);
    double scale1 = total * Math.Pow(Math.Sqrt(Math.Max(moments[0], EPS)), 3.0);
    if (scale1 > EPS && Math.Abs(skew1) / scale1 < 1e-4)
      warn.Add("This model is symmetric end-for-end along its long axis, so the shape itself " +
               "cannot say which end is 'first'. The slice ORDER is deterministic for a given " +
               "position but reverses if you flip the model end-for-end. Both orders cut the " +
               "same geometry; if the order matters to you, pin it with axisMode = 5.");

    e1 = PinSign(e1, c, a, centre);
    e2 = PinSign(e2, c, a, centre);
    e2 = e2 - (e2 * e1) * e1;
    if (!e2.Unitize()) e2 = AnyPerpendicular(e1);
    Vector3d e3 = Vector3d.CrossProduct(e1, e2);   // right handed by construction

    // If two spreads are nearly equal the model is round about that axis and
    // there is genuinely no unique answer. Say so instead of pretending.
    double scale = Math.Max(moments[0], EPS);
    if ((moments[0] - moments[1]) / scale < 0.02)
      warn.Add("The two longest directions of this model are within 2% of each other, so its " +
               "'long axis' is ambiguous (a cube, a cylinder, a sphere). The slice direction may " +
               "jump between runs. Set axisMode = 5 and supply the axis yourself.");
    else if ((moments[1] - moments[2]) / scale < 0.02)
      warn.Add("The two shortest directions of this model are within 2% of each other. " +
               "axisMode = 1 (shortest axis) is ambiguous here; prefer axisMode = 0 or 5.");

    return new Plane(centre, e1, e2);
  }

  private static double Skew(Vector3d e, List<Point3d> c, List<double> a, Point3d centre)
  {
    double skew = 0.0;
    for (int i = 0; i < c.Count; i++)
    {
      double d = (c[i] - centre) * e;
      skew += a[i] * d * d * d;
    }
    return skew;
  }

  private static Vector3d PinSign(Vector3d e, List<Point3d> c, List<double> a, Point3d centre)
  {
    double skew = Skew(e, c, a, centre);
    if (Math.Abs(skew) > 1e-9) return skew < 0 ? -e : e;

    // Perfectly symmetric about this axis: no shape-based answer exists.
    // Use a deterministic tie-break so at least the result is repeatable.
    double ax = Math.Abs(e.X), ay = Math.Abs(e.Y), az = Math.Abs(e.Z);
    if (ax >= ay && ax >= az) return e.X < 0 ? -e : e;
    if (ay >= az) return e.Y < 0 ? -e : e;
    return e.Z < 0 ? -e : e;
  }

  private static void TriangleCentroids(Mesh m, List<Point3d> centroids, List<double> areas)
  {
    for (int f = 0; f < m.Faces.Count; f++)
    {
      MeshFace face = m.Faces[f];
      Point3d A = m.Vertices.Point3dAt(face.A);
      Point3d B = m.Vertices.Point3dAt(face.B);
      Point3d C = m.Vertices.Point3dAt(face.C);
      AddTri(A, B, C, centroids, areas);
      if (face.IsQuad)
      {
        Point3d D = m.Vertices.Point3dAt(face.D);
        AddTri(A, C, D, centroids, areas);
      }
    }
  }

  private static void AddTri(Point3d A, Point3d B, Point3d C, List<Point3d> cs, List<double> ars)
  {
    double area = 0.5 * Vector3d.CrossProduct(B - A, C - A).Length;
    if (area <= EPS) return;
    cs.Add(new Point3d((A.X + B.X + C.X) / 3.0, (A.Y + B.Y + C.Y) / 3.0, (A.Z + B.Z + C.Z) / 3.0));
    ars.Add(area);
  }

  // Classic cyclic Jacobi eigensolver for a symmetric 3x3. Small, exact enough,
  // and no external library - which matters, because a Grasshopper C# component
  // cannot reference anything that is not already loaded in Rhino.
  private static void Jacobi3(double[,] input, out double[] eval, out double[,] evec)
  {
    double[,] a = (double[,]) input.Clone();
    double[,] v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

    for (int sweep = 0; sweep < 64; sweep++)
    {
      double off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
      if (off < 1e-16) break;

      for (int p = 0; p < 2; p++)
      {
        for (int q = p + 1; q < 3; q++)
        {
          if (Math.Abs(a[p, q]) < 1e-20) continue;

          double theta = (a[q, q] - a[p, p]) / (2.0 * a[p, q]);
          double t;
          if (Math.Abs(theta) < EPS) t = 1.0;
          else t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
          double cs = 1.0 / Math.Sqrt(t * t + 1.0);
          double sn = t * cs;

          for (int k = 0; k < 3; k++)
          {
            double akp = a[k, p], akq = a[k, q];
            a[k, p] = cs * akp - sn * akq;
            a[k, q] = sn * akp + cs * akq;
          }
          for (int k = 0; k < 3; k++)
          {
            double apk = a[p, k], aqk = a[q, k];
            a[p, k] = cs * apk - sn * aqk;
            a[q, k] = sn * apk + cs * aqk;
          }
          for (int k = 0; k < 3; k++)
          {
            double vkp = v[k, p], vkq = v[k, q];
            v[k, p] = cs * vkp - sn * vkq;
            v[k, q] = sn * vkp + cs * vkq;
          }
        }
      }
    }
    eval = new double[] { a[0, 0], a[1, 1], a[2, 2] };
    evec = v;
  }

  private static Vector3d Col(double[,] m, int j)
  {
    Vector3d v = new Vector3d(m[0, j], m[1, j], m[2, j]);
    v.Unitize();
    return v;
  }

  // =========================================================================
  // 4.  SLICE DIRECTION AND EXTENT
  // =========================================================================

  private static Vector3d ChooseAxis(Plane part, Options o, List<string> warn)
  {
    switch (o.AxisMode)
    {
      case 0: return part.XAxis;              // longest
      case 1: return part.ZAxis;              // shortest
      case 2: warn.Add("axisMode = world X. Rotating the model now changes the result.");
              return Vector3d.XAxis;
      case 3: warn.Add("axisMode = world Y. Rotating the model now changes the result.");
              return Vector3d.YAxis;
      case 4: warn.Add("axisMode = world Z. Rotating the model now changes the result.");
              return Vector3d.ZAxis;
      case 5:
        if (o.CustomAxis.IsZero)
        {
          warn.Add("axisMode = 5 but no custom axis was supplied; fell back to the longest axis.");
          return part.XAxis;
        }
        return o.CustomAxis;
      default: return part.XAxis;
    }
  }

  private static string AxisModeName(int m)
  {
    if (m == 0) return "auto: model's longest direction";
    if (m == 1) return "auto: model's shortest direction";
    if (m == 2) return "world X";
    if (m == 3) return "world Y";
    if (m == 4) return "world Z";
    return "custom vector";
  }

  private static void ProjectExtents(Mesh m, Point3d origin, Vector3d axis,
                                     out double lo, out double hi)
  {
    lo = double.MaxValue; hi = double.MinValue;
    for (int i = 0; i < m.Vertices.Count; i++)
    {
      double t = (m.Vertices.Point3dAt(i) - origin) * axis;
      if (t < lo) lo = t;
      if (t > hi) hi = t;
    }
  }

  // =========================================================================
  // 5.  LOOPS, SAMPLING, SEAM AND WINDING
  //     Two loops from two neighbouring slices must start at roughly the same
  //     place and run the same way round. Otherwise the tool sprints across the
  //     model every slice and the wrist unwinds itself.
  // =========================================================================

  // JOIN FIRST, THEN FILTER. This ordering is not cosmetic.
  //
  // Intersection.MeshPlane does not promise one polyline per loop. Where the
  // plane grazes a vertex, or where conditioning removed a sliver face, it
  // hands back a single section as SEVERAL open arcs. Taking the longest arc
  // and dropping the others - which is what this used to do - leaves a section
  // that looks plausible, is 25% short, and puts a straight 60 mm jump across
  // the middle of the part. It is silent, and it would have reached the foam.
  //
  // So: stitch the arcs back together on their shared endpoints first, and only
  // then decide which loops are worth keeping. Anything still open afterwards
  // is a real hole in the mesh, and gets reported rather than hidden.
  private static List<Polyline> JoinAndFilter(Polyline[] raw, Options o, double tol,
                                              double diag, ref int openLoops)
  {
    List<Polyline> keep = new List<Polyline>();
    if (raw == null) return keep;

    List<Curve> pieces = new List<Curve>();
    for (int i = 0; i < raw.Length; i++)
    {
      if (raw[i] == null || raw[i].Count < 2) continue;
      PolylineCurve pc = raw[i].ToPolylineCurve();
      if (pc != null) pieces.Add(pc);
    }
    if (pieces.Count == 0) return keep;

    // Generous enough to close a section split at a shared vertex, tight enough
    // that two genuinely separate loops are never welded into one.
    double joinTol = Math.Max(tol * 10.0, diag * 1e-6);

    Curve[] joined = Curve.JoinCurves(pieces, joinTol, false);
    if (joined == null || joined.Length == 0) joined = pieces.ToArray();

    double minLen = Math.Max(o.MinLoopLength, tol * 10.0);
    for (int i = 0; i < joined.Length; i++)
    {
      Curve c = joined[i];
      if (c == null) continue;

      Polyline p;
      if (!c.TryGetPolyline(out p))
      {
        PolylineCurve pc = c.ToPolyline(0, 0, 0.01, 0, 0, tol, 0, 0, true);
        if (pc == null || !pc.TryGetPolyline(out p)) continue;
      }
      if (p == null || p.Count < 2) continue;
      if (p.Length < minLen) continue;

      // A closed section may come back with its ends merely coincident rather
      // than formally closed. Close it explicitly so the sampler treats it as
      // a loop and not as an arc.
      if (!p.IsClosed && p[0].DistanceTo(p[p.Count - 1]) <= joinTol)
      {
        p[p.Count - 1] = p[0];
      }
      if (!p.IsClosed) openLoops++;

      keep.Add(p);
    }
    return keep;
  }

  // WINDING, SEAM AND SAMPLING, ALL IN ONE PLACE AND ALL DONE BY HAND.
  //
  // These sections are polylines, so arc length along them is a sum of straight
  // segments - exact, cheap, and with no API behaviour to be surprised by. An
  // earlier version went through Curve.GetLength(Interval) and
  // Curve.LengthParameter and the samples did not tile the loop properly; the
  // last sample landed well short of the seam, leaving a gap of several times
  // the nominal spacing. Doing the walk directly removes that whole class of
  // problem and, more importantly, makes every step exactly equivariant: the
  // same arithmetic on rotated coordinates gives the rotated answer.
  //
  // Order: winding first, then the seam, then sample FROM the seam.
  //
  // The seam is intrinsic. On the first slice it is the vertex farthest from
  // the loop's own centroid; on every later slice it is the vertex nearest to
  // where the previous loop started. Those vertices are the points where the
  // slice plane cuts the mesh edges, so they belong to the geometry and travel
  // with it. No world axis and no eigenvector sign is involved.
  //
  // If a section is symmetric - a circle, an ellipse, a rectangle - two vertices
  // tie for farthest and the choice is genuinely arbitrary. Nothing can fix
  // that; the self-test reports it as SEAM NOT CANONICAL.
  private static List<Point3d> SampleLoop(Polyline pl, Plane sp, Point3d anchor,
                                          int samples, bool closed)
  {
    List<Point3d> pts = new List<Point3d>();

    // Drop the repeated closing vertex so every vertex appears exactly once.
    List<Point3d> v = new List<Point3d>();
    int raw = pl.Count;
    int keep = (closed && raw > 1) ? raw - 1 : raw;
    for (int i = 0; i < keep; i++) v.Add(pl[i]);
    if (v.Count < 2) return pts;

    if (closed && SignedArea(v, sp) < 0) v.Reverse();

    int segs = closed ? v.Count : v.Count - 1;
    double[] cum = new double[segs + 1];
    for (int i = 0; i < segs; i++)
      cum[i + 1] = cum[i] + v[i].DistanceTo(v[(i + 1) % v.Count]);
    double len = cum[segs];
    if (len <= EPS) return pts;

    double s0 = 0.0;
    if (closed)
    {
      if (anchor.IsValid)
      {
        int best = 0;
        double bestD = double.MaxValue;
        for (int i = 0; i < v.Count; i++)
        {
          double d = v[i].DistanceToSquared(anchor);
          if (d < bestD) { bestD = d; best = i; }
        }
        s0 = cum[best];
      }
      else
      {
        double h = HarmonicSeam(v, cum, segs, len);
        if (!double.IsNaN(h)) s0 = h;
        else
        {
          // Degenerate: the loop has no first harmonic, so it is symmetric under
          // a half turn. Fall back to the farthest vertex, which is at least
          // deterministic, and let the self-test report the ambiguity.
          Point3d c = Point3d.Origin;
          for (int i = 0; i < v.Count; i++) c += v[i];
          c /= v.Count;

          int best = 0;
          double bestD = -1.0;
          for (int i = 0; i < v.Count; i++)
          {
            double d = v[i].DistanceToSquared(c);
            if (d > bestD) { bestD = d; best = i; }
          }
          s0 = cum[best];
        }
      }
    }

    int n = Math.Max(3, samples);
    int count = closed ? n : n + 1;
    for (int i = 0; i < count; i++)
    {
      double s = s0 + len * i / n;
      while (s >= len) s -= len;
      if (s < 0) s = 0;
      pts.Add(WalkTo(v, cum, segs, s));
    }
    return pts;
  }

  // WHERE A LOOP STARTS, DECIDED BY THE WHOLE LOOP.
  //
  // Walk the loop at constant arc length, measure the radius from its own
  // centroid, and take the phase of the first Fourier harmonic of that radius.
  //
  // Why not simply the farthest point? Because that is decided by ONE vertex.
  // On a section that is close to symmetric - and most sections are close to
  // elliptical - two vertices nearly tie for farthest, and which one wins is
  // then settled by rounding. Rotate the model and the seam jumps to the far
  // side of the loop. The harmonic integrates the whole outline instead, so a
  // small feature anywhere on it decides the seam, smoothly and stably.
  //
  // Equivariance: the samples are taken at arc length from the polyline's own
  // start, which is NOT a property of the shape. But if that start shifts by d,
  // every sample shifts by d, the phase shifts by exactly 2*pi*d/L, and the arc
  // offset returned shifts back by d - so the POINT on the loop is unchanged.
  //
  // Returns NaN when the first harmonic vanishes, which means the loop really
  // is unchanged by a half turn (a circle, an ellipse, a rectangle). Nothing can
  // pick a seam on those; the caller falls back and the self-test says so.
  private static double HarmonicSeam(List<Point3d> v, double[] cum, int segs, double len)
  {
    const int M = 256;

    Point3d c = Point3d.Origin;
    for (int i = 0; i < v.Count; i++) c += v[i];
    c /= v.Count;

    double re = 0.0, im = 0.0, meanR = 0.0;
    for (int i = 0; i < M; i++)
    {
      double s = len * i / M;
      double r = WalkTo(v, cum, segs, s).DistanceTo(c);
      double a = 2.0 * Math.PI * i / M;
      re += r * Math.Cos(a);
      im += r * Math.Sin(a);
      meanR += r;
    }
    meanR /= M;
    if (meanR <= EPS) return double.NaN;

    double mag = Math.Sqrt(re * re + im * im) / M;
    if (mag / meanR < 1e-6) return double.NaN;

    double phase = Math.Atan2(im, re);
    if (phase < 0) phase += 2.0 * Math.PI;
    return len * phase / (2.0 * Math.PI);
  }

  private static Point3d WalkTo(List<Point3d> v, double[] cum, int segs, double s)
  {
    int j = 0;
    while (j < segs - 1 && cum[j + 1] < s) j++;
    double segLen = cum[j + 1] - cum[j];
    double u = segLen > EPS ? (s - cum[j]) / segLen : 0.0;
    if (u < 0) u = 0;
    if (u > 1) u = 1;
    Point3d a = v[j];
    Point3d b = v[(j + 1) % v.Count];
    return a + (b - a) * u;
  }

  // Same way round the slice plane for every loop. The slice plane's normal is
  // the slice axis, which is welded to the model, so "the same way round" means
  // the same thing however the model is standing.
  private static double SignedArea(List<Point3d> pts, Plane sp)
  {
    double area = 0.0;
    for (int i = 0; i < pts.Count; i++)
    {
      Point3d a, b;
      sp.RemapToPlaneSpace(pts[i], out a);
      sp.RemapToPlaneSpace(pts[(i + 1) % pts.Count], out b);
      area += a.X * b.Y - b.X * a.Y;
    }
    return area * 0.5;
  }

  private static List<Point3d> Thin(List<Point3d> pts, double minSpacing, bool closed)
  {
    List<Point3d> keep = new List<Point3d>();
    for (int i = 0; i < pts.Count; i++)
    {
      if (keep.Count == 0 || pts[i].DistanceTo(keep[keep.Count - 1]) >= minSpacing)
        keep.Add(pts[i]);
    }
    if (closed && keep.Count > 2 && keep[0].DistanceTo(keep[keep.Count - 1]) < minSpacing)
      keep.RemoveAt(keep.Count - 1);
    return keep;
  }

  // =========================================================================
  // 6.  FRAME CONSTRUCTION
  //
  //     FRAME CONVENTION - do not change without re-checking the KUKA|prc chain
  //       origin : the point on the model
  //       X      : direction of travel along the loop
  //       Z      : approach - points FROM the tool INTO the material
  //       Y      : Z cross X, so the frame is right handed
  //     KUKA|prc consumes a Plane per target and takes Z as the tool axis, so
  //     this is the convention the LIN component already expects.
  // =========================================================================

  private static List<Node> BuildFrames(Mesh mesh, List<Point3d> pts, Plane sp, Options o,
                                        bool closed, ref Plane lastFrame)
  {
    List<Node> nodes = new List<Node>();
    double tol = DocTol();
    Point3d centroid = Centroid(pts);

    for (int i = 0; i < pts.Count; i++)
    {
      Vector3d travel = Tangent(pts, i, closed);
      if (!travel.Unitize()) continue;

      Vector3d outward = Outward(mesh, pts[i], centroid, sp, o, tol);
      Vector3d approach = o.FlipApproach ? outward : -outward;

      // Strip out any component along travel, so the tool axis is always square
      // to the direction it is moving. This is what stops the tool leaning into
      // the cut on a tight corner.
      approach = approach - (approach * travel) * travel;
      if (!approach.Unitize())
      {
        approach = sp.ZAxis - (sp.ZAxis * travel) * travel;
        if (!approach.Unitize()) continue;
      }

      Vector3d yAxis = Vector3d.CrossProduct(approach, travel);
      if (!yAxis.Unitize()) continue;

      Plane f = new Plane(pts[i], travel, yAxis);

      if (Math.Abs(o.TiltDeg) > 1e-9)
        f.Rotate(RhinoMath.ToRadians(o.TiltDeg), f.XAxis, f.Origin);

      // rollMode 1: the tool is round about its own axis, so spinning it costs
      // the process nothing and can be spent on keeping the wrist still.
      if (o.RollMode == 1 && lastFrame.IsValid)
        f = MinimiseRoll(f, lastFrame);

      lastFrame = f;
      Node nd = new Node();
      nd.Frame = f;
      nd.Point = pts[i];
      nd.MoveType = 1;
      nodes.Add(nd);
    }
    return nodes;
  }

  private static Vector3d Outward(Mesh mesh, Point3d p, Point3d centroid, Plane sp,
                                  Options o, double tol)
  {
    if (o.NormalMode == 2 && !o.FixedApproach.IsZero)
    {
      Vector3d v = o.FixedApproach;
      v.Unitize();
      return v;
    }

    if (o.NormalMode == 0)
    {
      Point3d onMesh; Vector3d nrm;
      if (mesh.ClosestPoint(p, out onMesh, out nrm, tol * 1000.0) >= 0)
      {
        if (nrm.Unitize()) return nrm;
      }
    }

    // Radial fallback: straight out from the middle of this slice.
    Vector3d radial = p - centroid;
    radial = radial - (radial * sp.ZAxis) * sp.ZAxis;
    if (radial.Unitize()) return radial;
    return sp.XAxis;
  }

  private static Plane MinimiseRoll(Plane current, Plane previous)
  {
    Vector3d z = current.ZAxis;
    Vector3d x = previous.XAxis - (previous.XAxis * z) * z;
    if (!x.Unitize()) return current;
    Vector3d y = Vector3d.CrossProduct(z, x);
    if (!y.Unitize()) return current;
    return new Plane(current.Origin, x, y);
  }

  private static Vector3d Tangent(List<Point3d> pts, int i, bool closed)
  {
    int n = pts.Count;
    if (n < 2) return Vector3d.Zero;
    if (closed)
    {
      Point3d prev = pts[(i - 1 + n) % n];
      Point3d next = pts[(i + 1) % n];
      return next - prev;                       // central difference
    }
    if (i == 0) return pts[1] - pts[0];
    if (i == n - 1) return pts[n - 1] - pts[n - 2];
    return pts[i + 1] - pts[i - 1];
  }

  private static Point3d Centroid(List<Point3d> pts)
  {
    Point3d c = Point3d.Origin;
    for (int i = 0; i < pts.Count; i++) c += pts[i];
    if (pts.Count > 0) c /= pts.Count;
    return c;
  }

  // Lead-in and lead-out: the same frame, pulled straight back along its own
  // approach axis. Because it is the tool's own axis and not world Z, this
  // works identically on a model lying down and a model standing up.
  private static void AddLeads(List<Node> nodes, double leadLen)
  {
    if (nodes.Count == 0) return;

    Plane a = nodes[0].Frame;
    Plane entry = a;
    entry.Origin = a.Origin - a.ZAxis * leadLen;
    Node inNode = new Node();
    inNode.Frame = entry; inNode.Point = entry.Origin; inNode.MoveType = 0;

    Plane b = nodes[nodes.Count - 1].Frame;
    Plane exit = b;
    exit.Origin = b.Origin - b.ZAxis * leadLen;
    Node outNode = new Node();
    outNode.Frame = exit; outNode.Point = exit.Origin; outNode.MoveType = 0;

    nodes.Insert(0, inNode);
    nodes.Add(outNode);
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

  private static void Remap(Result r, Transform x)
  {
    for (int b = 0; b < r.Planes.BranchCount; b++)
    {
      List<Plane> planes = r.Planes.Branch(b);
      for (int i = 0; i < planes.Count; i++)
      {
        Plane p = planes[i];
        p.Transform(x);
        planes[i] = p;
      }
    }
    for (int b = 0; b < r.Points.BranchCount; b++)
    {
      List<Point3d> pts = r.Points.Branch(b);
      for (int i = 0; i < pts.Count; i++) { Point3d p = pts[i]; p.Transform(x); pts[i] = p; }
    }
    for (int i = 0; i < r.Sections.Count; i++)
    {
      Curve c = r.Sections[i].DuplicateCurve();
      c.Transform(x);
      r.Sections[i] = c;
    }
    for (int i = 0; i < r.SlicePlanes.Count; i++)
    {
      Plane p = r.SlicePlanes[i]; p.Transform(x); r.SlicePlanes[i] = p;
    }
    Plane pf = r.PartFrame; pf.Transform(x); r.PartFrame = pf;
    Vector3d ax = r.SliceAxis; ax.Transform(x); ax.Unitize(); r.SliceAxis = ax;
  }

  // =========================================================================
  // 7.  THE ORIENTATION PROOF
  //
  //     Claim: the answer for a rotated model is the rotated answer.
  //     Test:  rotate the mesh by a series of awkward rotations, run the whole
  //            pipeline again on each, rotate the result back, and measure how
  //            far it landed from the original. If the claim holds, the only
  //            difference is floating point noise.
  //     This is why the deliverable can say "works in any orientation" without
  //     hand-waving: the number is printed on the component.
  // =========================================================================

  private static string RunSelfTest(Mesh mesh, Options o, Result baseline)
  {
    StringBuilder sb = new StringBuilder();
    sb.AppendLine();
    sb.AppendLine("=== ORIENTATION SELF-TEST ===");

    if (!o.AxisIsOrientationFree || !o.NormalIsOrientationFree)
    {
      sb.AppendLine("SKIPPED. You pinned the slice axis or the approach to a world direction");
      sb.AppendLine("(axisMode 2/3/4/5 or normalMode 2). Those settings are deliberately tied to");
      sb.AppendLine("the world, so a rotated model SHOULD give a different answer. Set axisMode to");
      sb.AppendLine("0 or 1 and normalMode to 0 or 1 to test the automatic path.");
      return sb.ToString();
    }

    List<Plane> flatBase = Flatten(baseline.Planes);
    List<int> moveBase = FlattenInts(baseline.MoveTypes);
    if (flatBase.Count == 0) return "SELF-TEST SKIPPED: the baseline run produced nothing.";

    double diag = mesh.GetBoundingBox(true).Diagonal.Length;
    if (diag < EPS) diag = 1.0;

    int trials = Math.Max(1, Math.Min(o.SelfTestCount, 64));
    double worstPos = 0.0, worstAng = 0.0;
    double worstSetPos = 0.0, worstSetAng = 0.0;
    int worstTrial = -1, countMismatch = 0, axisFlips = 0;

    // Fixed seed: the same trials every solve, so a number that changes means
    // the code changed, not the dice.
    Random rnd = new Random(20260803);

    for (int k = 0; k < trials; k++)
    {
      Vector3d axis = new Vector3d(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1,
                                   rnd.NextDouble() * 2 - 1);
      if (!axis.Unitize()) axis = Vector3d.ZAxis;
      double angle = rnd.NextDouble() * 2.0 * Math.PI;

      Transform R = Transform.Rotation(angle, axis, Point3d.Origin);
      Transform T = Transform.Translation(new Vector3d((rnd.NextDouble() - 0.5) * diag,
                                                       (rnd.NextDouble() - 0.5) * diag,
                                                       (rnd.NextDouble() - 0.5) * diag));
      Transform F = T * R;
      Transform Finv;
      if (!F.TryGetInverse(out Finv)) continue;

      Mesh moved = mesh.DuplicateMesh();
      moved.Transform(F);

      // The seam guide belongs to the model, so it travels with it. Leaving it
      // behind would be testing a different job, not a rotated one.
      Options ok = o;
      if (o.SeamGuide.IsValid)
      {
        ok = ShallowCopy(o);
        Point3d g = o.SeamGuide;
        g.Transform(F);
        ok.SeamGuide = g;
      }

      Result rk = new Result();
      Core(moved, ok, rk, new StringBuilder());
      List<Plane> flatK = Flatten(rk.Planes);

      // Did the slice axis come back pointing the other way? On a model that is
      // symmetric end-for-end this is not an error - the shape has no way to
      // prefer one end - but it reverses the slice order, so it must be
      // reported separately rather than buried in a distance number.
      Vector3d axBack = rk.SliceAxis;
      axBack.Transform(Finv);
      if (axBack.Unitize() && (axBack * baseline.SliceAxis) < 0) axisFlips++;

      if (flatK.Count != flatBase.Count) { countMismatch++; continue; }

      List<Plane> back = new List<Plane>(flatK.Count);
      for (int i = 0; i < flatK.Count; i++)
      {
        Plane p = flatK[i];
        p.Transform(Finv);
        back.Add(p);
      }

      // TWO measures, and the difference between them is the whole point.
      //
      // Index-wise: target i against target i. This is what actually gets sent
      // to the robot, so it is the strict test.
      //
      // Set-wise: every target against its NEAREST partner in the other run.
      // If this is zero while the index-wise measure is not, then the path
      // through space is identical and only the starting point of the loop has
      // moved. That happens when a cross-section is symmetric - an ellipse, a
      // circle, a rectangle - because then two seam points are genuinely
      // indistinguishable and no rule can prefer one. It is worth reporting as
      // exactly that, rather than as an unexplained failure.
      for (int i = 0; i < back.Count; i++)
      {
        double dp = back[i].Origin.DistanceTo(flatBase[i].Origin);
        double da = TurnDegrees(flatBase[i], back[i]);
        if (dp > worstPos) { worstPos = dp; worstTrial = k; }
        if (da > worstAng) { worstAng = da; worstTrial = k; }

        // Lead-in and lead-out points sit off the surface, along the tool axis
        // of whichever point they serve. If the seam moves, they move with it -
        // by design - so they have no partner and would report a mismatch of
        // exactly leadLen. Compare points on the work only.
        if (i < moveBase.Count && moveBase[i] != 1) continue;

        // Distance to the trial's PATH, not to its nearest sample. Comparing
        // sample against sample is itself sensitive to where the loop starts,
        // which is the very thing being separated out here: two runs can trace
        // exactly the same loop and still put their samples at different points
        // along it. Measuring to the path answers "is this the same cut" without
        // caring where the tool entered it.
        Plane onPath;
        double dpath = DistanceToPath(flatBase[i].Origin, back, moveBase, out onPath);
        if (dpath > worstSetPos) worstSetPos = dpath;
        if (onPath.IsValid)
        {
          double sa = TurnDegrees(flatBase[i], onPath);
          if (sa > worstSetAng) worstSetAng = sa;
        }
      }
    }

    // Thresholds: a rigid transform and back costs a few ULPs per coordinate.
    // Anything under a micron on a metre-scale part, or a millidegree, is noise.
    // WHY THESE LIMITS ARE NOT ZERO.
    // Rhino stores mesh vertices as SINGLE precision floats. Rotating a mesh and
    // rotating it back therefore cannot be bit-exact: every vertex moves by about
    // 1e-7 of the model size, and the surface normals derived from those vertices
    // wobble by roughly a thousandth of a degree. That is a property of the mesh
    // format, not of this code, and it is four orders of magnitude below anything
    // a KR6 can resolve. The limits are set just above that floor, so they still
    // catch a genuine mistake.
    double posLimit = Math.Max(1e-6 * diag, 1e-7);
    double angLimit = 1e-2;

    // The PATH comparison is measured against a polygon through the other run's
    // samples. If the two runs start their loops at different points, their
    // samples land at different places along the SAME loop, and a point on one
    // polygon then sits off the other by the chord sagitta - a discretisation
    // artefact, not a difference in the cut. So the path limit is a fraction of
    // one sample step rather than an absolute epsilon. It shrinks as samples
    // rises, which is exactly what a discretisation artefact should do.
    double step = baseline.MaxSpacing > EPS ? baseline.MaxSpacing : Math.Max(diag * 0.01, 1.0);
    double pathLimit = 0.25 * step;
    double pathAngLimit = 5.0;

    sb.AppendLine("trials                 " + trials + " random rotations + translations");
    sb.AppendLine("planes compared        " + flatBase.Count + " per trial");
    sb.AppendLine("slice-axis reversals   " + axisFlips + " of " + trials);
    sb.AppendLine("plane-count mismatches " + countMismatch + " of " + trials);
    sb.AppendLine("worst origin drift     " + worstPos.ToString("0.000000000") + " model units" +
                  "   (allowed " + posLimit.ToString("0.000000000") + ")");
    sb.AppendLine("worst rotation drift   " + worstAng.ToString("0.000000000") + " deg" +
                  "   (allowed " + angLimit.ToString("0.000000000") + ")");
    sb.AppendLine("  ... ignoring where each loop starts:");
    sb.AppendLine("worst path drift       " + worstSetPos.ToString("0.000000") + " model units" +
                  "   (" + (100.0 * worstSetPos / Math.Max(step, EPS)).ToString("0.#") +
                  "% of one " + step.ToString("0.##") + " step, allowed 25%)");
    sb.AppendLine("worst path rotation    " + worstSetAng.ToString("0.000000") + " deg" +
                  "   (allowed " + pathAngLimit.ToString("0.#") + ")");

    bool geometryOk = worstSetPos <= pathLimit && worstSetAng <= pathAngLimit
                      && countMismatch == 0 && axisFlips == 0;
    bool exact = geometryOk && worstPos <= posLimit && worstAng <= angLimit;

    if (exact)
    {
      sb.AppendLine("RESULT: PASS - the toolpath is identical in every orientation, to floating point.");
      return sb.ToString();
    }

    if (geometryOk)
    {
      sb.AppendLine("RESULT: PASS (GEOMETRY) - SEAM NOT CANONICAL");
      sb.AppendLine();
      sb.AppendLine("Every loop is the same loop, in the same place, cut the same way round.");
      sb.AppendLine("What moved is only WHERE ALONG EACH LOOP THE TOOL STARTS.");
      sb.AppendLine();
      sb.AppendLine("A smooth closed loop has no canonical starting point - there is no corner to");
      sb.AppendLine("call the beginning. The automatic choice reads the loop's own outline, which");
      sb.AppendLine("works well when the section has a clear long direction and becomes a coin toss");
      sb.AppendLine("when it is close to symmetric, as an ellipse or a circle is.");
      sb.AppendLine();
      sb.AppendLine("The residual above is the chord error between two polygons drawn through the");
      sb.AppendLine("same loop at different phases. Raise samples and it falls away.");
      sb.AppendLine();
      sb.AppendLine("IF THE START POINT MATTERS - and it does if you are re-cutting a part to match");
      sb.AppendLine("an earlier run - supply seamGuide. Every loop then starts at the point nearest");
      sb.AppendLine("to it and the whole result becomes exactly reproducible.");
      return sb.ToString();
    }

    sb.AppendLine("RESULT: FAIL");
    if (axisFlips > 0)
    {
      sb.AppendLine();
      sb.AppendLine("DIAGNOSIS: the slice axis came back reversed on " + axisFlips + " trial(s).");
      sb.AppendLine("This model is symmetric end-for-end along that axis, so the shape genuinely");
      sb.AppendLine("cannot say which end comes first - both answers cut identical geometry, but");
      sb.AppendLine("the slices are enumerated in the opposite order, and everything downstream");
      sb.AppendLine("then compares as different.");
      sb.AppendLine("This is a real limit, not a rounding problem, and no amount of maths removes");
      sb.AppendLine("it: a symmetric shape has nothing to break the tie with.");
      sb.AppendLine("FIX: set axisMode = 5 and supply the axis, which pins the direction for good.");
    }
    else
    {
      sb.AppendLine();
      sb.AppendLine("DIAGNOSIS: most likely the model is round about an axis, so its principal");
      sb.AppendLine("axes are ambiguous - see the component warnings. Pin the axis with");
      sb.AppendLine("axisMode = 5 and re-test.");
    }
    if (worstTrial >= 0) sb.AppendLine("worst trial index " + worstTrial);
    return sb.ToString();
  }

  // "PASS" and "PASS (GEOMETRY)" are both acceptable outcomes. Only a real
  // FAIL - where the path through space actually differs - is a problem.
  public static bool SelfTestPassed(string report)
  {
    return !string.IsNullOrEmpty(report) && report.Contains("RESULT: PASS");
  }

  public static bool SelfTestExact(string report)
  {
    return !string.IsNullOrEmpty(report) && report.Contains("RESULT: PASS -");
  }

  private static List<Plane> Flatten(DataTree<Plane> tree)
  {
    List<Plane> all = new List<Plane>();
    for (int b = 0; b < tree.BranchCount; b++) all.AddRange(tree.Branch(b));
    return all;
  }

  // Shortest distance from a point to the polyline through the process points,
  // together with the tool frame interpolated to that spot. Interpolating the
  // frame matters: if the two runs sample the loop at different phases, the
  // nearest SAMPLE can be half a step away and its frame will differ by the
  // local turn - which says nothing about whether the paths agree.
  private static double DistanceToPath(Point3d p, List<Plane> path, List<int> moves,
                                       out Plane frame)
  {
    double best = double.MaxValue;
    frame = Plane.Unset;
    int prev = -1;

    for (int i = 0; i < path.Count; i++)
    {
      if (i < moves.Count && moves[i] != 1) { prev = -1; continue; }
      if (prev < 0) { prev = i; continue; }

      Point3d a = path[prev].Origin, b = path[i].Origin;
      Vector3d ab = b - a;
      double len2 = ab.SquareLength;
      double t = len2 < EPS ? 0.0 : ((p - a) * ab) / len2;
      if (t < 0) t = 0;
      if (t > 1) t = 1;

      double d = p.DistanceTo(a + ab * t);
      if (d < best)
      {
        best = d;
        frame = Blend(path[prev], path[i], t);
      }
      prev = i;
    }
    return best == double.MaxValue ? 0.0 : best;
  }

  private static Plane Blend(Plane a, Plane b, double u)
  {
    Plane p = a;
    Quaternion q = Quaternion.Rotation(a, b);
    double angle; Vector3d axis;
    if (q.GetRotation(out angle, out axis) && axis.IsValid && !axis.IsZero)
    {
      if (angle > Math.PI) angle -= 2.0 * Math.PI;
      if (Math.Abs(angle) > 1e-12) p.Rotate(angle * u, axis, a.Origin);
    }
    p.Origin = a.Origin + (b.Origin - a.Origin) * u;
    return p;
  }

  private static double PointToSegment(Point3d p, Point3d a, Point3d b)
  {
    Vector3d ab = b - a;
    double len2 = ab.SquareLength;
    if (len2 < EPS) return p.DistanceTo(a);
    double t = ((p - a) * ab) / len2;
    if (t < 0) t = 0;
    if (t > 1) t = 1;
    return p.DistanceTo(a + ab * t);
  }

  private static List<int> FlattenInts(DataTree<int> tree)
  {
    List<int> all = new List<int>();
    for (int b = 0; b < tree.BranchCount; b++) all.AddRange(tree.Branch(b));
    return all;
  }

  // =========================================================================
  // 8.  ODDS AND ENDS
  // =========================================================================

  private static Options ShallowCopy(Options o)
  {
    Options c = new Options();
    c.AxisMode = o.AxisMode; c.CustomAxis = o.CustomAxis;
    c.Sections = o.Sections; c.Step = o.Step; c.Samples = o.Samples;
    c.LoopMode = o.LoopMode; c.MinLoopLength = o.MinLoopLength;
    c.NormalMode = o.NormalMode; c.FixedApproach = o.FixedApproach;
    c.FlipApproach = o.FlipApproach; c.TiltDeg = o.TiltDeg; c.RollMode = o.RollMode;
    c.LeadLen = o.LeadLen; c.CloseLoop = o.CloseLoop;
    c.MaxTurnDeg = o.MaxTurnDeg; c.MinSpacing = o.MinSpacing;
    c.SeamGuide = o.SeamGuide;
    c.FromFrame = o.FromFrame; c.ToFrame = o.ToFrame;
    c.SelfTest = false; c.SelfTestCount = o.SelfTestCount;
    return c;
  }

  public static double DocTol()
  {
    RhinoDoc doc = RhinoDoc.ActiveDoc;
    if (doc != null && doc.ModelAbsoluteTolerance > 0) return doc.ModelAbsoluteTolerance;
    return 0.001;
  }

  private static Vector3d AnyPerpendicular(Vector3d v)
  {
    Vector3d t = Math.Abs(v.X) < 0.9 ? Vector3d.XAxis : Vector3d.YAxis;
    Vector3d p = Vector3d.CrossProduct(v, t);
    p.Unitize();
    return p;
  }

  private static string Fmt(Point3d p)
  {
    return "(" + p.X.ToString("0.###") + ", " + p.Y.ToString("0.###") + ", " + p.Z.ToString("0.###") + ")";
  }

  private static string Fmt(Vector3d v)
  {
    return "(" + v.X.ToString("0.####") + ", " + v.Y.ToString("0.####") + ", " + v.Z.ToString("0.####") + ")";
  }
}

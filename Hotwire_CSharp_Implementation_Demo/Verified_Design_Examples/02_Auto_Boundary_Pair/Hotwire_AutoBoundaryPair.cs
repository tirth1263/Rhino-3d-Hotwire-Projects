// #! csharp
// Hot-wire Algorithm 02: automatic opposite-boundary pairing.
// Finds the two possible opposite-edge pairs on a four-sided open surface,
// selects the shorter or longer synchronized span, and builds hot-wire poses.

using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(
    object G,
    object PreferLong,
    object Count,
    object Extension,
    object Flip,
    object Tolerance,
    ref object Planes,
    ref object WireLines,
    ref object Ruled,
    ref object RailA,
    ref object RailB,
    ref object MidPath,
    ref object PairScores,
    ref object Report)
  {
    Brep brep = UnwrapBrep(G);
    if (brep == null)
    {
      Report = "ERROR: G must be a Surface or open Brep.";
      return;
    }

    bool preferLong = AsBool(PreferLong, false);
    int count = Math.Max(2, Math.Min(2000, AsInt(Count, 41)));
    double extension = Math.Max(0.0, AsDouble(Extension, 0.0));
    bool flip = AsBool(Flip, false);
    double tol = AsDouble(Tolerance, 0.0);
    if (tol <= 0.0) tol = RhinoDoc.ActiveDoc == null ? 0.01 : RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;

    Curve[] raw = brep.DuplicateNakedEdgeCurves(true, false);
    if (raw == null || raw.Length != 4)
    {
      Report = "ERROR: This algorithm requires exactly four unjoined naked boundary edges. Found " + (raw == null ? 0 : raw.Length) + ".";
      return;
    }

    var candidates = new List<PairCandidate>();
    for (int i = 0; i < raw.Length; i++)
    {
      for (int j = i + 1; j < raw.Length; j++)
      {
        if (!ShareEndpoint(raw[i], raw[j], tol))
          candidates.Add(EvaluatePair(raw[i], raw[j], i, j, count));
      }
    }

    if (candidates.Count != 2 || candidates.Any(c => !c.Valid))
    {
      Report = "ERROR: The four naked edges could not be resolved into two valid opposite pairs.";
      return;
    }

    PairCandidate chosen = preferLong
      ? candidates.OrderByDescending(c => c.MeanSpan).First()
      : candidates.OrderBy(c => c.MeanSpan).First();

    var sideA = new List<Point3d>(count);
    var sideB = new List<Point3d>(count);
    for (int i = 0; i < count; i++)
    {
      double t = (double)i / (count - 1);
      Point3d a = chosen.A.PointAtNormalizedLength(t);
      Point3d b = chosen.B.PointAtNormalizedLength(t);
      AddPair(a, b, extension, flip, tol, sideA, sideB);
    }

    if (sideA.Count < 2)
    {
      Report = "ERROR: The selected boundary pair did not produce at least two valid wire positions.";
      return;
    }

    List<Line> lines = sideA.Zip(sideB, (a, b) => new Line(a, b)).ToList();
    List<Point3d> mids = lines.Select(line => line.PointAt(0.5)).ToList();
    Curve railA = SmoothCurve(sideA);
    Curve railB = SmoothCurve(sideB);
    Curve midPath = SmoothCurve(mids);
    List<Plane> planes = BuildPlanes(lines, mids);

    Planes = planes;
    WireLines = lines;
    Ruled = StraightLoft(railA, railB);
    RailA = railA;
    RailB = railB;
    MidPath = midPath;
    PairScores = candidates.Select(c => string.Format("Edges {0}-{1}: mean span {2:0.###}", c.IndexA, c.IndexB, c.MeanSpan)).ToList();
    Report = string.Join(System.Environment.NewLine, new[]
    {
      "HOTWIRE 02 - AUTOMATIC OPPOSITE BOUNDARIES",
      "Selection rule: " + (preferLong ? "longer opposite pair" : "shorter opposite pair"),
      string.Format("Selected naked edges: {0} and {1}; mean span {2:0.###}", chosen.IndexA, chosen.IndexB, chosen.MeanSpan),
      "Wire positions: " + lines.Count,
      "Plane convention: Origin=wire midpoint, X=wire A->B, Y=travel, Z=X cross Y.",
      "Scope: a single four-sided open surface/Brep with exactly four naked edges.",
      "PairScores exposes both possible synchronized boundary pairings.",
      "No IK, collision, kerf, feed, or controller validation is performed."
    });
  }

  private class PairCandidate
  {
    public Curve A;
    public Curve B;
    public int IndexA;
    public int IndexB;
    public double MeanSpan;
    public bool Valid;
  }

  private static PairCandidate EvaluatePair(Curve sourceA, Curve sourceB, int indexA, int indexB, int sampleCount)
  {
    Curve a = sourceA.DuplicateCurve();
    Curve b = sourceB.DuplicateCurve();
    double same = a.PointAtStart.DistanceTo(b.PointAtStart) + a.PointAtEnd.DistanceTo(b.PointAtEnd);
    double reversed = a.PointAtStart.DistanceTo(b.PointAtEnd) + a.PointAtEnd.DistanceTo(b.PointAtStart);
    if (reversed < same) b.Reverse();

    var spans = new List<double>();
    for (int i = 0; i < sampleCount; i++)
    {
      double t = (double)i / (sampleCount - 1);
      Point3d pa = a.PointAtNormalizedLength(t);
      Point3d pb = b.PointAtNormalizedLength(t);
      if (pa.IsValid && pb.IsValid) spans.Add(pa.DistanceTo(pb));
    }
    return new PairCandidate
    {
      A = a,
      B = b,
      IndexA = indexA,
      IndexB = indexB,
      MeanSpan = spans.Count == 0 ? double.PositiveInfinity : spans.Average(),
      Valid = spans.Count == sampleCount
    };
  }

  private static bool ShareEndpoint(Curve a, Curve b, double tol)
  {
    return a.PointAtStart.DistanceTo(b.PointAtStart) <= tol ||
      a.PointAtStart.DistanceTo(b.PointAtEnd) <= tol ||
      a.PointAtEnd.DistanceTo(b.PointAtStart) <= tol ||
      a.PointAtEnd.DistanceTo(b.PointAtEnd) <= tol;
  }

  private static Brep UnwrapBrep(object value)
  {
    if (value is Brep) return ((Brep)value).DuplicateBrep();
    if (value is Surface) return ((Surface)value).ToBrep();
    if (value is BrepFace) return ((BrepFace)value).DuplicateFace(false);
    var wrapper = value as GH_ObjectWrapper;
    if (wrapper != null) return UnwrapBrep(wrapper.Value);
    var surfaceGoo = value as GH_Surface;
    if (surfaceGoo != null) return UnwrapBrep(surfaceGoo.Value);
    var brepGoo = value as GH_Brep;
    if (brepGoo != null) return UnwrapBrep(brepGoo.Value);
    return null;
  }

  private static bool AsBool(object value, bool fallback)
  {
    try
    {
      if (value is GH_Boolean) return ((GH_Boolean)value).Value;
      if (value is GH_ObjectWrapper) value = ((GH_ObjectWrapper)value).Value;
      return value == null ? fallback : Convert.ToBoolean(value);
    }
    catch { return fallback; }
  }

  private static int AsInt(object value, int fallback)
  {
    try
    {
      if (value is GH_Integer) return ((GH_Integer)value).Value;
      if (value is GH_ObjectWrapper) value = ((GH_ObjectWrapper)value).Value;
      return value == null ? fallback : Convert.ToInt32(value);
    }
    catch { return fallback; }
  }

  private static double AsDouble(object value, double fallback)
  {
    try
    {
      if (value is GH_Number) return ((GH_Number)value).Value;
      if (value is GH_ObjectWrapper) value = ((GH_ObjectWrapper)value).Value;
      return value == null ? fallback : Convert.ToDouble(value);
    }
    catch { return fallback; }
  }

  private static void AddPair(Point3d a, Point3d b, double extension, bool flip, double tol, List<Point3d> sideA, List<Point3d> sideB)
  {
    Vector3d axis = b - a;
    if (a.DistanceTo(b) <= tol || !axis.Unitize()) return;
    a -= axis * extension;
    b += axis * extension;
    if (flip) { Point3d swap = a; a = b; b = swap; }
    sideA.Add(a);
    sideB.Add(b);
  }

  private static Curve SmoothCurve(IList<Point3d> points)
  {
    if (points.Count == 2) return new LineCurve(points[0], points[1]);
    return Curve.CreateInterpolatedCurve(points, Math.Min(3, points.Count - 1), CurveKnotStyle.Chord) ?? new PolylineCurve(points);
  }

  private static List<Plane> BuildPlanes(IList<Line> lines, IList<Point3d> mids)
  {
    var planes = new List<Plane>(lines.Count);
    Vector3d previousZ = Vector3d.Unset;
    for (int i = 0; i < lines.Count; i++)
    {
      Vector3d x = lines[i].Direction;
      x.Unitize();
      Vector3d travel = i == 0 ? mids[1] - mids[0] : (i == mids.Count - 1 ? mids[i] - mids[i - 1] : mids[i + 1] - mids[i - 1]);
      Vector3d y = travel - x * (travel * x);
      if (!y.Unitize()) y = Perpendicular(x);
      Vector3d z = Vector3d.CrossProduct(x, y);
      z.Unitize();
      y = Vector3d.CrossProduct(z, x);
      y.Unitize();
      if (i > 0 && previousZ.IsValid && z * previousZ < 0.0) y = -y;
      Plane plane = new Plane(mids[i], x, y);
      planes.Add(plane);
      previousZ = plane.ZAxis;
    }
    return planes;
  }

  private static Vector3d Perpendicular(Vector3d normal)
  {
    Vector3d axis = Math.Abs(normal * Vector3d.XAxis) < 0.8 ? Vector3d.XAxis : Vector3d.YAxis;
    axis -= normal * (axis * normal);
    axis.Unitize();
    return axis;
  }

  private static object StraightLoft(Curve railA, Curve railB)
  {
    Brep[] lofts = Brep.CreateFromLoft(new[] { railA, railB }, Point3d.Unset, Point3d.Unset, LoftType.Straight, false);
    if (lofts == null || lofts.Length == 0) return null;
    return lofts.Length == 1 ? (object)lofts[0] : lofts.ToList();
  }
}

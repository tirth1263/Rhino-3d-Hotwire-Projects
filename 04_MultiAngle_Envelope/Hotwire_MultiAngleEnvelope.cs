// #! csharp
// Hot-wire Algorithm 04: multi-angle section-envelope search.
// Sections a Brep or Mesh along a path direction, tests wire directions around
// that axis, and keeps the direction with the most stable valid envelope.

using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(
    object G,
    object PathDir,
    object ReferenceWireDir,
    object AngleCount,
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
    ref object BestAngle,
    ref object Scores,
    ref object Report)
  {
    Brep brep;
    Mesh mesh;
    if (!UnwrapGeometry(G, out brep, out mesh))
    {
      Report = "ERROR: G must be a Brep, Surface, Extrusion, SubD, or Mesh.";
      return;
    }

    Vector3d path = UnwrapVector(PathDir, Vector3d.XAxis);
    if (!path.Unitize()) path = Vector3d.XAxis;
    Vector3d reference = UnwrapVector(ReferenceWireDir, Vector3d.ZAxis);
    reference -= path * (reference * path);
    if (!reference.Unitize()) reference = Perpendicular(path);

    int angleCount = Math.Max(2, Math.Min(180, AsInt(AngleCount, 18)));
    int count = Math.Max(3, Math.Min(500, AsInt(Count, 41)));
    double extension = Math.Max(0.0, AsDouble(Extension, 0.0));
    bool flip = AsBool(Flip, false);
    double tol = AsDouble(Tolerance, 0.0);
    if (tol <= 0.0) tol = RhinoDoc.ActiveDoc == null ? 0.01 : RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;

    BoundingBox box = brep != null ? brep.GetBoundingBox(true) : mesh.GetBoundingBox(true);
    double minPath;
    double maxPath;
    ProjectionRange(box, path, out minPath, out maxPath);
    if (maxPath - minPath <= tol)
    {
      Report = "ERROR: Geometry has no usable extent along PathDir.";
      return;
    }

    Point3d boxCenter = box.Center;
    var candidates = new List<Candidate>();
    for (int angleIndex = 0; angleIndex < angleCount; angleIndex++)
    {
      double angleDegrees = 180.0 * angleIndex / angleCount;
      Vector3d wireDirection = reference;
      wireDirection.Rotate(RhinoMath.ToRadians(angleDegrees), path);
      wireDirection.Unitize();
      Candidate candidate = EvaluateCandidate(brep, mesh, boxCenter, path, wireDirection, minPath, maxPath, count, extension, flip, tol);
      candidate.AngleDegrees = angleDegrees;
      candidates.Add(candidate);
    }

    Candidate best = candidates
      .Where(c => c.Lines.Count >= 2)
      .OrderByDescending(c => c.Score)
      .FirstOrDefault();
    if (best == null)
    {
      Report = "ERROR: No tested wire direction produced at least two usable sections.";
      Scores = candidates.Select(ScoreText).ToList();
      return;
    }

    List<Point3d> sideA = best.Lines.Select(line => line.From).ToList();
    List<Point3d> sideB = best.Lines.Select(line => line.To).ToList();
    List<Point3d> mids = best.Lines.Select(line => line.PointAt(0.5)).ToList();
    Curve railA = SmoothCurve(sideA);
    Curve railB = SmoothCurve(sideB);
    Curve midPath = SmoothCurve(mids);

    Planes = BuildPlanes(best.Lines, mids);
    WireLines = best.Lines;
    Ruled = StraightLoft(railA, railB);
    RailA = railA;
    RailB = railB;
    MidPath = midPath;
    BestAngle = best.AngleDegrees;
    Scores = candidates.Select(ScoreText).ToList();
    Report = string.Join(System.Environment.NewLine, new[]
    {
      "HOTWIRE 04 - MULTI-ANGLE SECTION ENVELOPE",
      string.Format("Best rotation from ReferenceWireDir: {0:0.###} degrees", best.AngleDegrees),
      string.Format("Valid sections: {0}/{1} ({2:P0}); score {3:0.###}", best.Lines.Count, count, best.Coverage, best.Score),
      string.Format("Wire-length coefficient of variation: {0:0.####}; maximum adjacent direction change: {1:0.###} degrees", best.LengthCv, best.MaxDirectionChange),
      "Plane convention: Origin=wire midpoint, X=wire A->B, Y=travel, Z=X cross Y.",
      "Each station uses the widest individual section contour along the tested wire direction.",
      "This is an envelope heuristic, not proof that a complex/non-convex solid is hot-wire manufacturable.",
      "No IK, collision, kerf, feed, or controller validation is performed."
    });
  }

  private class Candidate
  {
    public double AngleDegrees;
    public double Coverage;
    public double LengthCv;
    public double MaxDirectionChange;
    public double Score;
    public List<Line> Lines = new List<Line>();
  }

  private static Candidate EvaluateCandidate(Brep brep, Mesh mesh, Point3d boxCenter, Vector3d path, Vector3d wireDirection,
    double minPath, double maxPath, int stationCount, double extension, bool flip, double tol)
  {
    var result = new Candidate();
    for (int i = 0; i < stationCount; i++)
    {
      double t = 0.001 + 0.998 * i / (stationCount - 1);
      double coordinate = minPath + (maxPath - minPath) * t;
      double centerCoordinate = boxCenter.X * path.X + boxCenter.Y * path.Y + boxCenter.Z * path.Z;
      Point3d origin = boxCenter + path * (coordinate - centerCoordinate);
      Plane sectionPlane = new Plane(origin, path);

      Line widest;
      if (!WidestSection(brep, mesh, sectionPlane, wireDirection, tol, out widest)) continue;
      Vector3d axis = widest.Direction;
      if (!axis.Unitize() || widest.Length <= tol) continue;
      Point3d a = widest.From - axis * extension;
      Point3d b = widest.To + axis * extension;
      if (flip) { Point3d swap = a; a = b; b = swap; }
      result.Lines.Add(new Line(a, b));
    }

    result.Coverage = (double)result.Lines.Count / stationCount;
    if (result.Lines.Count == 0)
    {
      result.LengthCv = double.PositiveInfinity;
      result.MaxDirectionChange = 180.0;
      result.Score = double.NegativeInfinity;
      return result;
    }

    List<double> lengths = result.Lines.Select(line => line.Length).ToList();
    double mean = lengths.Average();
    double variance = lengths.Select(x => (x - mean) * (x - mean)).Average();
    result.LengthCv = mean <= tol ? double.PositiveInfinity : Math.Sqrt(variance) / mean;
    result.MaxDirectionChange = 0.0;
    for (int i = 1; i < result.Lines.Count; i++)
    {
      Vector3d a = result.Lines[i - 1].Direction;
      Vector3d b = result.Lines[i].Direction;
      a.Unitize();
      b.Unitize();
      double degrees = RhinoMath.ToDegrees(Vector3d.VectorAngle(a, b));
      if (degrees > result.MaxDirectionChange) result.MaxDirectionChange = degrees;
    }
    result.Score = result.Coverage * 1000.0 - result.LengthCv * 100.0 - result.MaxDirectionChange * 0.25;
    return result;
  }

  private static bool WidestSection(Brep brep, Mesh mesh, Plane plane, Vector3d direction, double tol, out Line widest)
  {
    widest = Line.Unset;
    double bestWidth = -1.0;
    if (brep != null)
    {
      Curve[] curves;
      Point3d[] points;
      if (!Intersection.BrepPlane(brep, plane, tol, out curves, out points) || curves == null) return false;
      foreach (Curve curve in curves)
      {
        Point3d[] samples;
        curve.DivideByCount(128, true, out samples);
        if (samples == null || samples.Length < 2) samples = new[] { curve.PointAtStart, curve.PointAtEnd };
        Line candidate;
        double width;
        if (Envelope(samples, direction, out candidate, out width) && width > bestWidth)
        {
          bestWidth = width;
          widest = candidate;
        }
      }
    }
    else
    {
      Polyline[] polylines = Intersection.MeshPlane(mesh, plane);
      if (polylines == null) return false;
      foreach (Polyline polyline in polylines)
      {
        Line candidate;
        double width;
        if (Envelope(polyline, direction, out candidate, out width) && width > bestWidth)
        {
          bestWidth = width;
          widest = candidate;
        }
      }
    }
    return bestWidth > tol && widest.IsValid;
  }

  private static bool Envelope(IEnumerable<Point3d> points, Vector3d direction, out Line line, out double width)
  {
    line = Line.Unset;
    width = 0.0;
    bool any = false;
    double min = double.PositiveInfinity;
    double max = double.NegativeInfinity;
    Point3d minPoint = Point3d.Unset;
    Point3d maxPoint = Point3d.Unset;
    foreach (Point3d point in points)
    {
      if (!point.IsValid) continue;
      double projection = point.X * direction.X + point.Y * direction.Y + point.Z * direction.Z;
      if (projection < min) { min = projection; minPoint = point; }
      if (projection > max) { max = projection; maxPoint = point; }
      any = true;
    }
    if (!any || !minPoint.IsValid || !maxPoint.IsValid) return false;
    width = max - min;
    line = new Line(minPoint, maxPoint);
    return line.IsValid;
  }

  private static string ScoreText(Candidate candidate)
  {
    return string.Format("{0,6:0.###} deg | score {1,9:0.###} | coverage {2:P0} | CV {3:0.####} | max dAngle {4:0.###}",
      candidate.AngleDegrees, candidate.Score, candidate.Coverage, candidate.LengthCv, candidate.MaxDirectionChange);
  }

  private static bool UnwrapGeometry(object value, out Brep brep, out Mesh mesh)
  {
    brep = null;
    mesh = null;
    if (value is Brep) brep = ((Brep)value).DuplicateBrep();
    else if (value is Surface) brep = ((Surface)value).ToBrep();
    else if (value is Extrusion) brep = ((Extrusion)value).ToBrep();
    else if (value is SubD) brep = ((SubD)value).ToBrep();
    else if (value is Mesh) mesh = ((Mesh)value).DuplicateMesh();
    else if (value is GH_ObjectWrapper) return UnwrapGeometry(((GH_ObjectWrapper)value).Value, out brep, out mesh);
    else if (value is GH_Brep) brep = ((GH_Brep)value).Value.DuplicateBrep();
    else if (value is GH_Surface) brep = ((GH_Surface)value).Value.DuplicateBrep();
    else if (value is GH_Mesh) mesh = ((GH_Mesh)value).Value.DuplicateMesh();
    return brep != null || mesh != null;
  }

  private static Vector3d UnwrapVector(object value, Vector3d fallback)
  {
    if (value is Vector3d) return (Vector3d)value;
    if (value is GH_Vector) return ((GH_Vector)value).Value;
    var wrapper = value as GH_ObjectWrapper;
    return wrapper == null ? fallback : UnwrapVector(wrapper.Value, fallback);
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

  private static void ProjectionRange(BoundingBox box, Vector3d direction, out double min, out double max)
  {
    min = double.PositiveInfinity;
    max = double.NegativeInfinity;
    foreach (Point3d corner in box.GetCorners())
    {
      double projection = corner.X * direction.X + corner.Y * direction.Y + corner.Z * direction.Z;
      min = Math.Min(min, projection);
      max = Math.Max(max, projection);
    }
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

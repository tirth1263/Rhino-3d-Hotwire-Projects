// #! csharp
// Hotwire Geometry -> synchronized wire lines, ruled surface, and robot planes.
// Rhino 8 / Grasshopper C# Script component, SDK mode.
//
// Plane convention:
//   Origin = midpoint of the hot wire
//   X axis = wire direction (Rail A -> Rail B)
//   Y axis = local travel direction, projected perpendicular to X
//   Z axis = X cross Y
//
// Modes:
//   1. If RailA and RailB are supplied, they are divided by equal normalized
//      length and used directly. This is the exact ruled-surface workflow.
//   2. Otherwise G is sectioned by planes normal to PathDir. On every section,
//      the widest intersection contour along WireDir supplies the wire endpoints.
//      This produces a ruled envelope/initialization for a Brep, Surface, SubD,
//      Extrusion, or Mesh; it is not an exact fit for arbitrary double curvature.

using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(
    object G,
    object RailA,
    object RailB,
    object PathDir,
    object WireDir,
    object Count,
    object Extension,
    object Flip,
    object Tolerance,
    ref object Planes,
    ref object WireLines,
    ref object Ruled,
    ref object RailAOut,
    ref object RailBOut,
    ref object MidPath,
    ref object WireLengths,
    ref object Report)
  {
    var notes = new List<string>();
    var sideA = new List<Point3d>();
    var sideB = new List<Point3d>();

    Curve inputRailA = UnwrapCurve(RailA);
    Curve inputRailB = UnwrapCurve(RailB);
    Vector3d inputPathDir = UnwrapVector(PathDir, Vector3d.XAxis);
    Vector3d inputWireDir = UnwrapVector(WireDir, Vector3d.YAxis);
    int countValue = UnwrapInt(Count, 41);
    double extensionValue = UnwrapDouble(Extension, 0.0);
    bool flipValue = UnwrapBool(Flip, false);
    double toleranceValue = UnwrapDouble(Tolerance, 0.0);

    int requestedCount = countValue <= 1 ? 41 : Math.Min(countValue, 2000);
    double extension = Math.Max(0.0, extensionValue);
    double tol = toleranceValue > 0.0
      ? toleranceValue
      : (RhinoDoc.ActiveDoc == null ? 0.01 : RhinoDoc.ActiveDoc.ModelAbsoluteTolerance);

    string mode;
    int missedSections = 0;

    if (IsUsableCurve(inputRailA) && IsUsableCurve(inputRailB))
    {
      mode = "PAIRED RAILS (exact ruled workflow)";
      Curve a = inputRailA.DuplicateCurve();
      Curve b = inputRailB.DuplicateCurve();
      MatchCurveDirections(a, b);

      for (int i = 0; i < requestedCount; i++)
      {
        double t = requestedCount == 1 ? 0.5 : (double)i / (requestedCount - 1);
        Point3d pa = PointAtNormalizedLengthSafe(a, t);
        Point3d pb = PointAtNormalizedLengthSafe(b, t);
        AddExtendedPair(pa, pb, extension, flipValue, sideA, sideB, tol);
      }
    }
    else
    {
      mode = "GEOMETRY SECTIONS (ruled-envelope initialization)";
      GeometryBase geometry = UnwrapGeometry(G);
      if (geometry == null)
      {
        Report = "ERROR: Supply both RailA + RailB, or supply G as a Brep, Surface, SubD, Extrusion, or Mesh.";
        return;
      }

      Vector3d path = NormalizedOrFallback(inputPathDir, Vector3d.XAxis);
      Vector3d wire = ProjectPerpendicular(inputWireDir, path);
      if (!wire.Unitize())
        wire = BestPerpendicularAxis(path);

      BoundingBox bbox = geometry.GetBoundingBox(true);
      if (!bbox.IsValid)
      {
        Report = "ERROR: Input geometry has no valid bounding box.";
        return;
      }

      double minPath;
      double maxPath;
      ProjectionRange(bbox, path, out minPath, out maxPath);
      double depth = maxPath - minPath;
      if (depth <= tol)
      {
        Report = "ERROR: Input geometry has no measurable depth along PathDir.";
        return;
      }

      Point3d anchor = bbox.Center;
      double anchorProjection = Dot(anchor, path);
      double inset = Math.Min(depth * 0.01, Math.Max(tol * 5.0, depth * 1e-6));
      double start = minPath + inset;
      double end = maxPath - inset;

      for (int i = 0; i < requestedCount; i++)
      {
        double t = requestedCount == 1 ? 0.5 : (double)i / (requestedCount - 1);
        double d = start + (end - start) * t;
        Point3d origin = anchor + path * (d - anchorProjection);
        Plane sectionPlane = new Plane(origin, path);

        Point3d pa;
        Point3d pb;
        if (TrySectionEndpoints(geometry, sectionPlane, wire, tol, out pa, out pb))
          AddExtendedPair(pa, pb, extension, flipValue, sideA, sideB, tol);
        else
          missedSections++;
      }

      notes.Add("Section mode selects the widest individual contour along WireDir at each slice.");
      notes.Add("For arbitrary double-curved or closed geometry, the result is a straight-wire ruled approximation/initialization, not an exact surface reconstruction.");
    }

    if (sideA.Count < 2 || sideB.Count != sideA.Count)
    {
      Report = "ERROR: Fewer than two valid wire positions were generated. Check rail overlap, PathDir, WireDir, and input geometry.";
      return;
    }

    Curve outRailA = CreateSmoothRail(sideA);
    Curve outRailB = CreateSmoothRail(sideB);
    Curve midPath = CreateSmoothRail(sideA.Zip(sideB, (a, b) => 0.5 * (a + b)).ToList());

    var wireLines = new List<Line>(sideA.Count);
    var lengths = new List<double>(sideA.Count);
    var midpoints = new List<Point3d>(sideA.Count);
    for (int i = 0; i < sideA.Count; i++)
    {
      Line line = new Line(sideA[i], sideB[i]);
      wireLines.Add(line);
      lengths.Add(line.Length);
      midpoints.Add(line.PointAt(0.5));
    }

    var planes = BuildContinuousPlanes(sideA, sideB, midpoints, inputPathDir, tol);
    object ruledOutput = CreateRuledBrep(outRailA, outRailB);

    double minLength = lengths.Min();
    double maxLength = lengths.Max();
    double maxFrameChange = MaximumFrameChangeDegrees(planes);

    if (missedSections > 0)
      notes.Add(string.Format("Skipped {0} section plane(s) with no usable intersection.", missedSections));
    if (maxFrameChange > 30.0)
      notes.Add(string.Format("WARNING: maximum adjacent frame rotation is {0:0.###} degrees; inspect robot wrist continuity.", maxFrameChange));
    if (maxLength - minLength > Math.Max(tol * 10.0, minLength * 0.10))
      notes.Add("Wire span changes by more than 10%; verify that the physical bow provides sufficient clearance.");

    notes.Add("Planes are geometry frames only: Origin=wire midpoint, X=wire A->B, Y=travel, Z=X cross Y.");
    notes.Add("Apply the required axis remap for the calibrated KUKA tool/TCP before generating robot commands.");
    notes.Add("No IK, collision, kerf, temperature, feed, or controller validation is performed here.");

    Planes = planes;
    WireLines = wireLines;
    Ruled = ruledOutput;
    RailAOut = outRailA;
    RailBOut = outRailB;
    MidPath = midPath;
    WireLengths = lengths;
    Report = string.Join(Environment.NewLine, new[]
    {
      "HOTWIRE GEOMETRY -> PLANES",
      "Mode: " + mode,
      string.Format("Wire positions: {0} / requested {1}", sideA.Count, requestedCount),
      string.Format("Wire length range: {0:0.###} to {1:0.###} model units", minLength, maxLength),
      string.Format("Maximum adjacent frame rotation: {0:0.###} degrees", maxFrameChange),
      string.Join(Environment.NewLine, notes)
    });
  }

  private static bool IsUsableCurve(Curve curve)
  {
    return curve != null && curve.IsValid && curve.GetLength() > RhinoMath.ZeroTolerance;
  }

  private static GeometryBase UnwrapGeometry(object value)
  {
    if (value == null) return null;
    GeometryBase geometry = value as GeometryBase;
    if (geometry != null) return geometry;

    var wrapper = value as GH_ObjectWrapper;
    if (wrapper != null && wrapper.Value is GeometryBase)
      return (GeometryBase)wrapper.Value;

    var goo = value as IGH_GeometricGoo;
    if (goo != null)
      return goo.DuplicateGeometry() as GeometryBase;

    return null;
  }

  private static Curve UnwrapCurve(object value)
  {
    if (value == null) return null;
    Curve curve = value as Curve;
    if (curve != null) return curve;

    var wrapper = value as GH_ObjectWrapper;
    if (wrapper != null && wrapper.Value is Curve)
      return (Curve)wrapper.Value;

    var ghCurve = value as GH_Curve;
    return ghCurve == null ? null : ghCurve.Value;
  }

  private static Vector3d UnwrapVector(object value, Vector3d fallback)
  {
    if (value is Vector3d) return (Vector3d)value;
    var wrapper = value as GH_ObjectWrapper;
    if (wrapper != null && wrapper.Value is Vector3d)
      return (Vector3d)wrapper.Value;
    var ghVector = value as GH_Vector;
    return ghVector == null ? fallback : ghVector.Value;
  }

  private static int UnwrapInt(object value, int fallback)
  {
    try
    {
      if (value is GH_Integer) return ((GH_Integer)value).Value;
      var wrapper = value as GH_ObjectWrapper;
      if (wrapper != null) value = wrapper.Value;
      return value == null ? fallback : Convert.ToInt32(value);
    }
    catch { return fallback; }
  }

  private static double UnwrapDouble(object value, double fallback)
  {
    try
    {
      if (value is GH_Number) return ((GH_Number)value).Value;
      var wrapper = value as GH_ObjectWrapper;
      if (wrapper != null) value = wrapper.Value;
      return value == null ? fallback : Convert.ToDouble(value);
    }
    catch { return fallback; }
  }

  private static bool UnwrapBool(object value, bool fallback)
  {
    try
    {
      if (value is GH_Boolean) return ((GH_Boolean)value).Value;
      var wrapper = value as GH_ObjectWrapper;
      if (wrapper != null) value = wrapper.Value;
      return value == null ? fallback : Convert.ToBoolean(value);
    }
    catch { return fallback; }
  }

  private static void MatchCurveDirections(Curve a, Curve b)
  {
    double same = a.PointAtStart.DistanceToSquared(b.PointAtStart)
                + a.PointAtEnd.DistanceToSquared(b.PointAtEnd);
    double crossed = a.PointAtStart.DistanceToSquared(b.PointAtEnd)
                   + a.PointAtEnd.DistanceToSquared(b.PointAtStart);
    if (crossed < same) b.Reverse();
  }

  private static Point3d PointAtNormalizedLengthSafe(Curve curve, double t)
  {
    t = Math.Max(0.0, Math.Min(1.0, t));
    Point3d point = curve.PointAtNormalizedLength(t);
    return point.IsValid ? point : curve.PointAt(curve.Domain.ParameterAt(t));
  }

  private static void AddExtendedPair(
    Point3d a,
    Point3d b,
    double extension,
    bool flip,
    List<Point3d> sideA,
    List<Point3d> sideB,
    double tol)
  {
    Vector3d axis = b - a;
    if (!axis.Unitize() || a.DistanceTo(b) <= tol) return;

    a -= axis * extension;
    b += axis * extension;
    if (flip)
    {
      Point3d swap = a;
      a = b;
      b = swap;
    }

    sideA.Add(a);
    sideB.Add(b);
  }

  private static Vector3d NormalizedOrFallback(Vector3d vector, Vector3d fallback)
  {
    if (vector.Unitize()) return vector;
    fallback.Unitize();
    return fallback;
  }

  private static Vector3d ProjectPerpendicular(Vector3d vector, Vector3d normal)
  {
    if (!vector.IsValid || vector.IsTiny()) return Vector3d.Unset;
    return vector - normal * (vector * normal);
  }

  private static Vector3d BestPerpendicularAxis(Vector3d normal)
  {
    Vector3d[] axes = { Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis };
    Vector3d best = axes.OrderBy(axis => Math.Abs(axis * normal)).First();
    best -= normal * (best * normal);
    best.Unitize();
    return best;
  }

  private static double Dot(Point3d point, Vector3d direction)
  {
    return point.X * direction.X + point.Y * direction.Y + point.Z * direction.Z;
  }

  private static void ProjectionRange(BoundingBox bbox, Vector3d direction, out double min, out double max)
  {
    min = double.PositiveInfinity;
    max = double.NegativeInfinity;
    foreach (Point3d corner in bbox.GetCorners())
    {
      double value = Dot(corner, direction);
      min = Math.Min(min, value);
      max = Math.Max(max, value);
    }
  }

  private static bool TrySectionEndpoints(
    GeometryBase geometry,
    Plane sectionPlane,
    Vector3d wireDirection,
    double tol,
    out Point3d a,
    out Point3d b)
  {
    a = Point3d.Unset;
    b = Point3d.Unset;
    var contours = new List<List<Point3d>>();

    Mesh mesh = geometry as Mesh;
    if (mesh != null)
    {
      Polyline[] polylines = Intersection.MeshPlane(mesh, sectionPlane);
      if (polylines != null)
        foreach (Polyline polyline in polylines)
          if (polyline != null && polyline.Count >= 2)
            contours.Add(polyline.ToList());
    }
    else
    {
      Brep brep = ToBrep(geometry);
      if (brep == null) return false;

      Curve[] curves;
      Point3d[] points;
      if (!Intersection.BrepPlane(brep, sectionPlane, tol, out curves, out points))
        return false;

      if (curves != null)
      {
        foreach (Curve curve in curves)
        {
          if (!IsUsableCurve(curve)) continue;
          var samples = new List<Point3d>();
          Point3d[] divided;
          int segments = Math.Max(12, Math.Min(128, (int)Math.Ceiling(curve.GetLength() / Math.Max(tol * 20.0, 1.0))));
          double[] parameters = curve.DivideByCount(segments, true, out divided);
          if (divided != null && divided.Length > 1)
            samples.AddRange(divided);
          else
          {
            samples.Add(curve.PointAtStart);
            samples.Add(curve.PointAtEnd);
          }
          if (samples.Count >= 2) contours.Add(samples);
        }
      }
    }

    double bestSpan = tol;
    foreach (List<Point3d> contour in contours)
    {
      Point3d localMin = Point3d.Unset;
      Point3d localMax = Point3d.Unset;
      double minProjection = double.PositiveInfinity;
      double maxProjection = double.NegativeInfinity;

      foreach (Point3d point in contour)
      {
        double projection = Dot(point, wireDirection);
        if (projection < minProjection)
        {
          minProjection = projection;
          localMin = point;
        }
        if (projection > maxProjection)
        {
          maxProjection = projection;
          localMax = point;
        }
      }

      double span = maxProjection - minProjection;
      if (span > bestSpan && localMin.IsValid && localMax.IsValid)
      {
        bestSpan = span;
        a = localMin;
        b = localMax;
      }
    }

    return a.IsValid && b.IsValid && a.DistanceTo(b) > tol;
  }

  private static Brep ToBrep(GeometryBase geometry)
  {
    Brep brep = geometry as Brep;
    if (brep != null) return brep;

    Surface surface = geometry as Surface;
    if (surface != null) return Brep.CreateFromSurface(surface);

    Extrusion extrusion = geometry as Extrusion;
    if (extrusion != null) return extrusion.ToBrep();

    SubD subd = geometry as SubD;
    if (subd != null) return subd.ToBrep();

    return null;
  }

  private static Curve CreateSmoothRail(IList<Point3d> points)
  {
    if (points.Count == 2) return new LineCurve(points[0], points[1]);
    int degree = Math.Min(3, points.Count - 1);
    Curve interpolated = Curve.CreateInterpolatedCurve(points, degree, CurveKnotStyle.Chord);
    return interpolated ?? new PolylineCurve(points);
  }

  private static List<Plane> BuildContinuousPlanes(
    IList<Point3d> sideA,
    IList<Point3d> sideB,
    IList<Point3d> midpoints,
    Vector3d fallbackTravel,
    double tol)
  {
    var planes = new List<Plane>(sideA.Count);
    Vector3d previousY = Vector3d.Unset;
    Vector3d previousZ = Vector3d.Unset;

    for (int i = 0; i < sideA.Count; i++)
    {
      Vector3d x = sideB[i] - sideA[i];
      x.Unitize();

      Vector3d travel;
      if (i == 0) travel = midpoints[1] - midpoints[0];
      else if (i == midpoints.Count - 1) travel = midpoints[i] - midpoints[i - 1];
      else travel = midpoints[i + 1] - midpoints[i - 1];

      Vector3d y = ProjectPerpendicular(travel, x);
      if (!y.Unitize())
      {
        y = ProjectPerpendicular(fallbackTravel, x);
        if (!y.Unitize()) y = BestPerpendicularAxis(x);
      }

      Vector3d z = Vector3d.CrossProduct(x, y);
      if (!z.Unitize()) z = BestPerpendicularAxis(x);
      y = Vector3d.CrossProduct(z, x);
      y.Unitize();

      if (i > 0 && previousZ.IsValid && (z * previousZ) < 0.0)
      {
        y = -y;
        z = -z;
      }
      else if (i > 0 && previousY.IsValid && Math.Abs(z * previousZ) < tol && (y * previousY) < 0.0)
      {
        y = -y;
        z = -z;
      }

      planes.Add(new Plane(midpoints[i], x, y));
      previousY = y;
      previousZ = z;
    }

    return planes;
  }

  private static object CreateRuledBrep(Curve railA, Curve railB)
  {
    Brep[] lofts = Brep.CreateFromLoft(
      new[] { railA, railB },
      Point3d.Unset,
      Point3d.Unset,
      LoftType.Straight,
      false);

    if (lofts == null || lofts.Length == 0) return null;
    if (lofts.Length == 1) return lofts[0];
    return lofts.ToList();
  }

  private static double MaximumFrameChangeDegrees(IList<Plane> planes)
  {
    double maximum = 0.0;
    for (int i = 1; i < planes.Count; i++)
    {
      double xAngle = Vector3d.VectorAngle(planes[i - 1].XAxis, planes[i].XAxis);
      double zAngle = Vector3d.VectorAngle(planes[i - 1].ZAxis, planes[i].ZAxis);
      maximum = Math.Max(maximum, Math.Max(xAngle, zAngle));
    }
    return RhinoMath.ToDegrees(maximum);
  }
}

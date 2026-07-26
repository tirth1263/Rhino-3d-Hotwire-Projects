// #! csharp
// Hot-wire Algorithm 01: Surface UV sampling.
// Samples opposite sides of one surface, then outputs finite wire positions,
// center-TCP planes, synchronized rails, a midpoint path, and a ruled loft.

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
    object S,
    object WireAcrossU,
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
    ref object Deviation,
    ref object Report)
  {
    Surface surface = UnwrapSurface(S);
    if (surface == null)
    {
      Report = "ERROR: S must be a Surface or Brep with at least one face.";
      return;
    }

    bool acrossU = AsBool(WireAcrossU, true);
    int count = Math.Max(2, Math.Min(2000, AsInt(Count, 41)));
    double extension = Math.Max(0.0, AsDouble(Extension, 0.0));
    bool flip = AsBool(Flip, false);
    double tol = AsDouble(Tolerance, 0.0);
    if (tol <= 0.0) tol = RhinoDoc.ActiveDoc == null ? 0.01 : RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;

    Interval uDomain = surface.Domain(0);
    Interval vDomain = surface.Domain(1);
    var sideA = new List<Point3d>(count);
    var sideB = new List<Point3d>(count);

    for (int i = 0; i < count; i++)
    {
      double t = (double)i / (count - 1);
      Point3d a;
      Point3d b;
      if (acrossU)
      {
        double v = vDomain.ParameterAt(t);
        a = surface.PointAt(uDomain.Min, v);
        b = surface.PointAt(uDomain.Max, v);
      }
      else
      {
        double u = uDomain.ParameterAt(t);
        a = surface.PointAt(u, vDomain.Min);
        b = surface.PointAt(u, vDomain.Max);
      }
      AddPair(a, b, extension, flip, tol, sideA, sideB);
    }

    if (sideA.Count < 2)
    {
      Report = "ERROR: Surface sampling did not produce at least two valid wire positions.";
      return;
    }

    List<Line> lines = sideA.Zip(sideB, (a, b) => new Line(a, b)).ToList();
    List<Point3d> mids = lines.Select(line => line.PointAt(0.5)).ToList();
    Curve railA = SmoothCurve(sideA);
    Curve railB = SmoothCurve(sideB);
    Curve midPath = SmoothCurve(mids);
    List<Plane> planes = BuildPlanes(lines, mids);
    object ruled = StraightLoft(railA, railB);

    var deviations = new List<double>();
    foreach (Line line in lines)
    {
      for (int j = 0; j <= 8; j++)
      {
        Point3d point = line.PointAt((double)j / 8.0);
        double u;
        double v;
        if (surface.ClosestPoint(point, out u, out v))
          deviations.Add(point.DistanceTo(surface.PointAt(u, v)));
      }
    }
    double maxDeviation = deviations.Count == 0 ? double.NaN : deviations.Max();
    double meanDeviation = deviations.Count == 0 ? double.NaN : deviations.Average();

    Planes = planes;
    WireLines = lines;
    Ruled = ruled;
    RailA = railA;
    RailB = railB;
    MidPath = midPath;
    Deviation = maxDeviation;
    Report = string.Join(System.Environment.NewLine, new[]
    {
      "HOTWIRE 01 - SURFACE UV",
      "Wire direction: " + (acrossU ? "across U; advancing in V" : "across V; advancing in U"),
      "Wire positions: " + lines.Count,
      string.Format("Maximum ruled approximation deviation: {0:0.###} model units", maxDeviation),
      string.Format("Mean ruled approximation deviation: {0:0.###} model units", meanDeviation),
      "Plane convention: Origin=wire midpoint, X=wire A->B, Y=travel, Z=X cross Y.",
      "Trim boundaries and holes are ignored because the underlying surface UV domain is sampled.",
      "A non-ruled input surface is approximated; inspect Deviation before fabrication.",
      "No IK, collision, kerf, feed, or controller validation is performed."
    });
  }

  private static Surface UnwrapSurface(object value)
  {
    if (value is Surface) return (Surface)((Surface)value).Duplicate();
    if (value is Brep)
    {
      Brep brep = (Brep)value;
      if (brep.Faces.Count == 0) return null;
      BrepFace face = brep.Faces.OrderByDescending(f =>
      {
        AreaMassProperties amp = AreaMassProperties.Compute(f);
        return amp == null ? 0.0 : amp.Area;
      }).First();
      return face.DuplicateSurface();
    }
    var wrapper = value as GH_ObjectWrapper;
    if (wrapper != null) return UnwrapSurface(wrapper.Value);
    var surfaceGoo = value as GH_Surface;
    if (surfaceGoo != null) return UnwrapSurface(surfaceGoo.Value);
    var brepGoo = value as GH_Brep;
    if (brepGoo != null) return UnwrapSurface(brepGoo.Value);
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

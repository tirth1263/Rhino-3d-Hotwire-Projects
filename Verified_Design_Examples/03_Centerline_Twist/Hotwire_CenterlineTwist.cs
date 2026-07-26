// #! csharp
// Hot-wire Algorithm 03: centerline, width, and twist sweep.
// Converts one spatial guide curve into a programmable ruled ribbon and
// center-TCP planes without needing an input surface.

using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(
    object Path,
    object ReferencePlane,
    object WidthStart,
    object WidthEnd,
    object TwistDegrees,
    object Count,
    object Extension,
    object Flip,
    ref object Planes,
    ref object WireLines,
    ref object Ruled,
    ref object RailA,
    ref object RailB,
    ref object MidPath,
    ref object Widths,
    ref object Report)
  {
    Curve path = UnwrapCurve(Path);
    if (path == null || !path.IsValid || path.GetLength() <= 1e-9)
    {
      Report = "ERROR: Path must be a valid non-zero Curve.";
      return;
    }

    Plane reference = UnwrapPlane(ReferencePlane, Plane.WorldXY);
    double widthStart = Math.Max(1e-6, AsDouble(WidthStart, 120.0));
    double widthEnd = Math.Max(1e-6, AsDouble(WidthEnd, widthStart));
    double twistDegrees = AsDouble(TwistDegrees, 0.0);
    int count = Math.Max(2, Math.Min(2000, AsInt(Count, 41)));
    double extension = Math.Max(0.0, AsDouble(Extension, 0.0));
    bool flip = AsBool(Flip, false);

    var sideA = new List<Point3d>(count);
    var sideB = new List<Point3d>(count);
    var widths = new List<double>(count);
    var stations = new List<Point3d>(count);
    Vector3d previousBase = Vector3d.Unset;

    for (int i = 0; i < count; i++)
    {
      double t = (double)i / (count - 1);
      double parameter;
      if (!path.NormalizedLengthParameter(t, out parameter)) parameter = path.Domain.ParameterAt(t);
      Point3d center = path.PointAt(parameter);
      Vector3d tangent = path.TangentAt(parameter);
      if (!tangent.Unitize()) continue;

      Plane frame;
      Vector3d lateral;
      if (path.PerpendicularFrameAt(parameter, out frame))
      {
        lateral = frame.XAxis;
        Vector3d target = reference.XAxis - tangent * (reference.XAxis * tangent);
        if (!target.Unitize()) target = reference.YAxis - tangent * (reference.YAxis * tangent);
        if (i == 0 && target.Unitize() && lateral * target < 0.0) lateral = -lateral;
      }
      else
      {
        lateral = reference.XAxis - tangent * (reference.XAxis * tangent);
        if (!lateral.Unitize()) lateral = Perpendicular(tangent);
      }

      lateral -= tangent * (lateral * tangent);
      if (!lateral.Unitize()) lateral = Perpendicular(tangent);
      if (previousBase.IsValid && lateral * previousBase < 0.0) lateral = -lateral;
      previousBase = lateral;

      lateral.Rotate(Rhino.RhinoMath.ToRadians(twistDegrees * t), tangent);
      lateral.Unitize();
      double width = widthStart + (widthEnd - widthStart) * t;
      Point3d a = center - lateral * (0.5 * width + extension);
      Point3d b = center + lateral * (0.5 * width + extension);
      if (flip) { Point3d swap = a; a = b; b = swap; }

      sideA.Add(a);
      sideB.Add(b);
      stations.Add(center);
      widths.Add(width + 2.0 * extension);
    }

    if (sideA.Count < 2)
    {
      Report = "ERROR: Centerline sampling did not produce at least two valid stations.";
      return;
    }

    List<Line> lines = sideA.Zip(sideB, (a, b) => new Line(a, b)).ToList();
    Curve railA = SmoothCurve(sideA);
    Curve railB = SmoothCurve(sideB);
    Curve midPath = SmoothCurve(stations);
    List<Plane> planes = BuildPlanes(lines, stations);

    Planes = planes;
    WireLines = lines;
    Ruled = StraightLoft(railA, railB);
    RailA = railA;
    RailB = railB;
    MidPath = midPath;
    Widths = widths;
    Report = string.Join(System.Environment.NewLine, new[]
    {
      "HOTWIRE 03 - CENTERLINE WIDTH/TWIST",
      "Wire positions: " + lines.Count,
      string.Format("Programmed width: {0:0.###} to {1:0.###}; total twist: {2:0.###} degrees", widthStart, widthEnd, twistDegrees),
      "Plane convention: Origin=wire midpoint, X=wire A->B, Y=travel, Z=X cross Y.",
      "ReferencePlane seeds the transported lateral direction; Flip reverses wire X.",
      "Ruled is the exact ribbon implied by the generated finite wire segments.",
      "No IK, collision, kerf, feed, or controller validation is performed."
    });
  }

  private static Curve UnwrapCurve(object value)
  {
    if (value is Curve) return ((Curve)value).DuplicateCurve();
    var wrapper = value as GH_ObjectWrapper;
    if (wrapper != null) return UnwrapCurve(wrapper.Value);
    var goo = value as GH_Curve;
    if (goo != null) return UnwrapCurve(goo.Value);
    return null;
  }

  private static Plane UnwrapPlane(object value, Plane fallback)
  {
    if (value is Plane) return (Plane)value;
    if (value is GH_Plane) return ((GH_Plane)value).Value;
    var wrapper = value as GH_ObjectWrapper;
    return wrapper == null ? fallback : UnwrapPlane(wrapper.Value, fallback);
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

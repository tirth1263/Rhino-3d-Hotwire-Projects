// ---------------------------------------------------------------------------
// CellBuild - writes TirthWork_Cell.3dm, the Rhino model that goes with the two
// Grasshopper definitions.
//
// The hotwire tool geometry is lifted from the lab's own
//   End-Effector Development/Hotwire/Rev2.1/Hotwire_2.1.3dm
// which is already modelled in FLANGE COORDINATES - flange face at the origin,
// tool reaching up +Z, wire along Y at Z 421.35. That is exactly the frame
// KUKA|prc's "Custom Tool: Plane" wants for its geometry input, so it is used
// as-is rather than re-modelled.
//
// Tooling, not a deliverable. C# only, no Python.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

using Rhino;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;

public static class CellBuild
{
  /// Where the lab's hotwire model lives, relative to this project.
  ///
  /// Resolved rather than hard-coded: an absolute path would carry a machine's
  /// drive letter and the shared-drive id into the repository, and would break
  /// for anyone who cloned it. HOTWIRE_REF_3DM overrides, for a cell whose
  /// folders are arranged differently.
  static readonly string[] RefCandidates = {
    @"..\..\..\End-Effector Development\Hotwire\Rev2.1\Hotwire_2.1.3dm",
    @"..\..\End-Effector Development\Hotwire\Rev2.1\Hotwire_2.1.3dm",
    @"reference\Hotwire_2.1.3dm"
  };

  static string _refTool;

  public static string RefTool(string root)
  {
    if (_refTool != null) return _refTool;

    // System.Environment spelled out - Rhino.DocObjects has an Environment too.
    string env = System.Environment.GetEnvironmentVariable("HOTWIRE_REF_3DM");
    if (!string.IsNullOrEmpty(env) && File.Exists(env)) { _refTool = env; return _refTool; }

    foreach (string rel in RefCandidates)
    {
      string p = Path.GetFullPath(Path.Combine(root, rel));
      if (File.Exists(p)) { _refTool = p; return _refTool; }
    }

    throw new FileNotFoundException(
      "Could not find the hotwire reference model Hotwire_2.1.3dm. Looked " +
      "relative to " + root + " in: " + string.Join(" ; ", RefCandidates) +
      ". Set HOTWIRE_REF_3DM to its full path, or drop a copy in a " +
      "'reference' folder beside the project.");
  }

  // ---- measured off the reference file, not guessed ----
  public const double WireZ      = 421.35;   // wire centre height above the flange
  public const double WireHalf   = 207.90;   // wire reaches +/- this along flange Y
  public const double ToolZ      = 422.0;    // the CUSTOM TOOL dialog's Tool Z
  public const double ToolA      = -90.0;
  public const double ToolB      = -90.0;
  public const double ToolC      = 0.0;

  // Where the work sits in the cell, matching the part sliders on the FL-01
  // canvas. The virtual robot stands at the world origin, so the work has to
  // be out in front of it - and not too far: the flange only reaches 1101 mm.
  public const double PartX = 1273.0, PartY = -20.0, PartZ = 302.0;

  // Set by Run(). FullTool() is public and GhBuild may call it first, so it
  // falls back to walking up from this assembly's own folder.
  static string _root;

  /// For callers that want the tool mesh without running a full build.
  /// Needed because Add-Type compiles this into a temporary assembly, so the
  /// assembly's own location says nothing about where the project is.
  public static void SetRoot(string root) { _root = root; }

  static string Root()
  {
    if (string.IsNullOrEmpty(_root))
      throw new InvalidOperationException(
        "CellBuild does not know where the project is. Call Run(root, out) or " +
        "SetRoot(root) before asking for the tool mesh.");
    return _root;
  }

  static StringBuilder _log;
  // FullTool()/ReducedTool() are public and can be called by GhBuild before
  // Run() has started a log, so this has to stand on its own.
  static void L(string s) { if (_log == null) _log = new StringBuilder(); _log.AppendLine(s); }

  public static string Run(string root, string outDir)
  {
    _log = new StringBuilder();
    Directory.CreateDirectory(outDir);
    _root = root;
    L("tool source: " + RefTool(root));

    File3dm doc = new File3dm();
    doc.Settings.ModelUnitSystem = UnitSystem.Millimeters;
    doc.Settings.ModelAbsoluteTolerance = 0.001;

    int lFlange = AddLayer(doc, "01_Flange_and_Robot_Base", Color.DimGray);
    int lTool   = AddLayer(doc, "02_Hotwire_Tool_FLANGE_COORDS", Color.SteelBlue);
    int lWire   = AddLayer(doc, "03_Wire", Color.OrangeRed);
    int lTcp    = AddLayer(doc, "04_TCP_Frames", Color.Magenta);
    int lFoam   = AddLayer(doc, "05_Foam_Block_BASE3", Color.LightGoldenrodYellow);
    int lPart   = AddLayer(doc, "06_FL01_Demo_Part", Color.MediumSeaGreen);
    int lDraw   = AddLayer(doc, "07_TF09_Demo_Drawing", Color.Black);
    int lPaper  = AddLayer(doc, "08_TF09_Paper", Color.Gainsboro);
    int lMag    = AddLayer(doc, "09_TF09_Magazine", Color.SaddleBrown);

    ToolMesh(doc, lTool);
    FlangeMarkers(doc, lFlange);
    WireAndTcp(doc, lWire, lTcp);
    Foam(doc, lFoam);
    DemoPart(doc, lPart);
    DemoDrawing(doc, lDraw, lPaper);
    Magazine(doc, lMag);

    string path = Path.Combine(outDir, "TirthWork_Cell.3dm");
    string tmp  = Path.Combine(Path.GetTempPath(), "TirthWork_Cell.3dm");

    if (!doc.Write(tmp, new File3dmWriteOptions())) throw new Exception("could not write " + tmp);
    long n = new FileInfo(tmp).Length;
    if (n < 5000) throw new Exception("suspiciously small .3dm: " + n + " bytes");
    File.Copy(tmp, path, true);
    if (new FileInfo(path).Length != n) throw new Exception("copy landed short");
    File.Delete(tmp);

    L("");
    L("wrote " + Path.GetFileName(path) + "  (" + n + " bytes, " +
      doc.Objects.Count + " objects, " + doc.AllLayers.Count + " layers)");
    return _log.ToString();
  }

  // =========================================================================
  static int AddLayer(File3dm doc, string name, Color c)
  {
    Layer l = new Layer();
    l.Name = name;
    l.Color = c;
    doc.AllLayers.Add(l);
    return doc.AllLayers.Count - 1;
  }

  static ObjectAttributes At(int layer, string name)
  {
    ObjectAttributes a = new ObjectAttributes();
    a.LayerIndex = layer;
    if (!string.IsNullOrEmpty(name)) a.Name = name;
    return a;
  }

  // =========================================================================
  //  THE TOOL
  //
  //  84 separate meshes, 62k faces. Joined into one and reduced, because this
  //  mesh also gets internalised into the .gh file and prc only ever uses it
  //  to draw the tool and check collisions - it is not the cutting geometry.
  //  The wire itself is kept out of the reduction and drawn exactly.
  // =========================================================================
  static Mesh _full, _reduced;

  /// The tool as one mesh, straight from the lab's file.
  public static Mesh FullTool()
  {
    if (_full != null) return _full;

    string refPath = RefTool(Root());
    File3dm src = File3dm.Read(refPath);
    if (src == null) throw new Exception("could not read " + refPath);

    Mesh joined = new Mesh();
    int n = 0;
    foreach (File3dmObject o in src.Objects)
    {
      Mesh m = o.Geometry as Mesh;
      if (m == null) continue;
      joined.Append(m);
      n++;
    }
    joined.Vertices.CombineIdentical(true, true);
    joined.Faces.CullDegenerateFaces();
    joined.Normals.ComputeNormals();
    joined.Compact();

    L("tool: joined " + n + " meshes -> " + joined.Faces.Count + " faces, " +
      joined.Vertices.Count + " vertices");
    _full = joined;
    return _full;
  }

  /// The copy that gets internalised into the .gh files. GhBuild calls this,
  /// so the tool in the Grasshopper file and the tool in the Rhino file are the
  /// same mesh rather than two things that merely look alike.
  public static Mesh ReducedTool()
  {
    if (_reduced != null) return _reduced;

    Mesh red = FullTool().DuplicateMesh();
    try
    {
      red.Reduce(6000, true, 10, false);
      red.Normals.ComputeNormals();
      red.Compact();
      L("tool: reduced copy " + red.Faces.Count + " faces");
    }
    catch (Exception e) { L("tool: reduce failed (" + e.Message + "), using full mesh"); }
    _reduced = red;
    return _reduced;
  }

  static void ToolMesh(File3dm doc, int layer)
  {
    doc.Objects.AddMesh(FullTool(), At(layer, "Hotwire_Rev2.1_full"));
    doc.Objects.AddMesh(ReducedTool(), At(layer, "Hotwire_Rev2.1_reduced_for_GH"));
  }

  // =========================================================================
  static void FlangeMarkers(File3dm doc, int layer)
  {
    doc.Objects.AddPoint(Point3d.Origin, At(layer, "FLANGE_ORIGIN"));

    // flange axis cross, matching the two 150 mm curves in the reference file
    doc.Objects.AddCurve(new LineCurve(new Point3d(-75, 0, 0), new Point3d(75, 0, 0)),
                         At(layer, "FLANGE_X"));
    doc.Objects.AddCurve(new LineCurve(new Point3d(0, -75, 0), new Point3d(0, 75, 0)),
                         At(layer, "FLANGE_Y"));
    doc.Objects.AddCurve(new LineCurve(new Point3d(0, 0, 0), new Point3d(0, 0, 150)),
                         At(layer, "FLANGE_Z_out_along_tool"));

    doc.Objects.AddTextDot(new TextDot("FLANGE (0,0,0)", Point3d.Origin), At(layer, null));

    // the robot's own base, so it is obvious how far out the work sits
    doc.Objects.AddCurve(new ArcCurve(new Circle(Plane.WorldXY, 160.0)),
                         At(layer, "ROBOT_BASE_footprint"));
    doc.Objects.AddTextDot(new TextDot("ROBOT BASE - world origin", Point3d.Origin),
                           At(layer, null));
  }

  // =========================================================================
  //  WIRE + THE THREE TCPs
  //
  //  README: TOOL[4] wire midpoint, TOOL[5]/[6] wire ends A and B.
  //  All three are drawn in flange coordinates, same as the tool mesh.
  // =========================================================================
  static void WireAndTcp(File3dm doc, int lWire, int lTcp)
  {
    Point3d a = new Point3d(0, -WireHalf, WireZ);
    Point3d b = new Point3d(0,  WireHalf, WireZ);

    doc.Objects.AddCurve(new LineCurve(a, b), At(lWire, "WIRE_measured_span"));
    doc.Objects.AddTextDot(new TextDot("wire span " + (2 * WireHalf).ToString("0.#") + " mm",
                                       new Point3d(0, 0, WireZ + 25)), At(lWire, null));

    Plane tcp = AbcToPlane(new Point3d(0, 0, ToolZ), ToolA, ToolB, ToolC);
    Frame(doc, lTcp, tcp, "TOOL4_wire_midpoint", 90.0);

    // The wire ends sit along the TCP's Z, which is what the A/B/C above make
    // point down the wire. Offsetting along that axis is the check that the
    // frame is right: it has to land on the wire.
    Plane pa = tcp; pa.Origin = tcp.Origin - tcp.ZAxis * WireHalf;
    Plane pb = tcp; pb.Origin = tcp.Origin + tcp.ZAxis * WireHalf;
    Frame(doc, lTcp, pa, "TOOL5_wire_end_A", 60.0);
    Frame(doc, lTcp, pb, "TOOL6_wire_end_B", 60.0);

    double da = pa.Origin.DistanceTo(a), db = pb.Origin.DistanceTo(b);
    L("TCP: TOOL[4] at " + Fmt(tcp.Origin) + "   Z axis " + Fmt(tcp.ZAxis));
    L("TCP: TOOL[5]/[6] land " + da.ToString("0.###") + " / " + db.ToString("0.###") +
      " mm from the modelled wire ends" +
      ((da < 1.0 && db < 1.0) ? "   OK - the frame follows the wire" : "   *** CHECK ***"));
  }

  static void Frame(File3dm doc, int layer, Plane p, string name, double len)
  {
    doc.Objects.AddCurve(new LineCurve(p.Origin, p.Origin + p.XAxis * len), At(layer, name + "_X"));
    doc.Objects.AddCurve(new LineCurve(p.Origin, p.Origin + p.YAxis * len), At(layer, name + "_Y"));
    doc.Objects.AddCurve(new LineCurve(p.Origin, p.Origin + p.ZAxis * len), At(layer, name + "_Z"));
    doc.Objects.AddTextDot(new TextDot(name, p.Origin), At(layer, null));
  }

  // =========================================================================
  static void Foam(File3dm doc, int layer)
  {
    // EPS block sized round the demo part, which now stands upright, so the
    // block is tall rather than long.
    Interval x = new Interval(PartX - 70, PartX + 70);
    Interval y = new Interval(PartY - 70, PartY + 70);
    Interval z = new Interval(PartZ - 170, PartZ + 170);
    Box box = new Box(Plane.WorldXY, x, y, z);
    doc.Objects.AddBrep(box.ToBrep(), At(layer, "FOAM_BLOCK_EPS"));

    Point3d o = new Point3d(x.Min, y.Min, z.Min);
    Frame(doc, layer, new Plane(o, Vector3d.XAxis, Vector3d.YAxis), "BASE3_foam_origin", 80.0);
    L("foam: block " + x.Length + " x " + y.Length + " x " + z.Length +
      " mm, BASE[3] at its lower corner " + Fmt(o));
  }

  // =========================================================================
  static void DemoPart(File3dm doc, int layer)
  {
    // GhBuild builds it standing vertical about the origin and the canvas
    // moves it. Same move here, same numbers, so the model matches the .gh.
    Mesh m = GhBuild.DemoPart().DuplicateMesh();
    m.Translate(new Vector3d(PartX, PartY, PartZ));
    doc.Objects.AddMesh(m, At(layer, "FL01_demo_part_VERTICAL"));

    BoundingBox b = m.GetBoundingBox(true);
    double near = double.MaxValue, far = 0.0;
    foreach (Point3d v in m.Vertices.ToPoint3dArray())
    {
      double h = Math.Sqrt(v.X * v.X + v.Y * v.Y);
      if (h < near) near = h;
      if (h > far) far = h;
    }
    L("part: " + m.Faces.Count + " faces, centre " + Fmt(b.Center) +
      ", standing " + (b.Max.Z - b.Min.Z).ToString("0") + " mm tall");
    L("      horizontal distance from the robot base: " + near.ToString("0") +
      " .. " + far.ToString("0") + " mm");
  }

  static void DemoDrawing(File3dm doc, int lDraw, int lPaper)
  {
    List<int> pens = new List<int>();
    List<Curve> cs = GhBuild.DemoDrawing(pens);

    // GhBuild authors the drawing round the origin and the canvas moves it onto
    // the paper. Same move here, same numbers, so the model matches the .gh.
    Vector3d mv = new Vector3d(600, 0, 200);
    for (int i = 0; i < cs.Count; i++)
    {
      Curve c = cs[i].DuplicateCurve();
      c.Translate(mv);
      doc.Objects.AddCurve(c, At(lDraw, "stroke_" + i + "_pen" + pens[i]));
    }

    Plane paper = new Plane(new Point3d(600, 0, 200), Vector3d.XAxis, Vector3d.YAxis);
    Rectangle3d r = new Rectangle3d(paper, new Interval(-140, 140), new Interval(-105, 105));
    doc.Objects.AddCurve(r.ToNurbsCurve(), At(lPaper, "PAPER_A4ish"));
    Frame(doc, lPaper, paper, "drawPlane", 70.0);
    L("drawing: " + cs.Count + " strokes, pens " + string.Join(",", pens.ConvertAll(p => p.ToString()).ToArray()));
  }

  static void Magazine(File3dm doc, int layer)
  {
    // Matches the placeholder chain on the TF-09 canvas: 4 slots, 80 mm pitch,
    // starting at X 380, at Y -420, Z 150. Placeholders, same as there.
    for (int i = 0; i < 4; i++)
    {
      Point3d o = new Point3d(380 + i * 80, -420, 150);
      // Z points INTO the slot, which is the convention a slot plane must carry
      Plane p = new Plane(o, -Vector3d.ZAxis);
      Frame(doc, layer, p, "SLOT_" + i + "_TOOL" + (i + 1), 50.0);
      doc.Objects.AddCurve(new ArcCurve(new Circle(new Plane(o, Vector3d.ZAxis), 18.0)),
                           At(layer, "slot_" + i + "_mouth"));
    }
    L("magazine: 4 placeholder slots at Y -420, Z 150, 80 mm pitch");
  }

  // =========================================================================
  //  KUKA Z-Y'-X'' intrinsic, identical to AbcToPlane() in TF09_helpers.cs
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

  static string Fmt(Point3d p)
  { return "(" + p.X.ToString("0.#") + ", " + p.Y.ToString("0.#") + ", " + p.Z.ToString("0.#") + ")"; }
  static string Fmt(Vector3d v)
  { return "(" + v.X.ToString("0.##") + ", " + v.Y.ToString("0.##") + ", " + v.Z.ToString("0.##") + ")"; }
}

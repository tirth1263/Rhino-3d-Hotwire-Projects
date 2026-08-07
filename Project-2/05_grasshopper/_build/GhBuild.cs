// ---------------------------------------------------------------------------
// GhBuild - assembles the TF-09 and FL-01 Grasshopper definitions in memory
// and writes them to .gh / .ghx.
//
// This is TOOLING, not a deliverable. The deliverables are the .cs panes in
// 01_/02_/03_ and the .gh files this writes. Nothing here is Python.
//
// Why generate rather than hand-place: the two definitions carry 24 and 17
// inputs. Hand-wiring that is where transcription mistakes live, and a
// mis-set type hint fails silently at solve time rather than loudly here.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;

using Rhino.Geometry;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

public static class GhBuild
{
  // ---- component GUIDs, all read off this machine's own component server ---
  static readonly Guid G_CSharp   = new Guid("a9a8ebd2-fff5-4c44-a8f5-739736d129ba"); // C# Script (3 panes)
  static readonly Guid G_XYPlane  = new Guid("17b7152b-d30d-4d50-b9ef-c9fe25576fc2");
  static readonly Guid G_Series   = new Guid("e64c5fb1-845c-4ab1-8911-5f338516ba67");
  static readonly Guid G_ConPoint = new Guid("3581f42a-9592-4549-bd6b-1c0fc39d067b");
  static readonly Guid G_PlaneNorm= new Guid("cfb6b17f-ca82-4f5d-b604-d4f69f569de3");
  static readonly Guid G_Move     = new Guid("e9eb1dcf-92f6-4d4d-84ae-96222d60f56b");
  static readonly Guid G_TreeBr   = new Guid("3a710c1e-1809-4e19-8c15-82adce31cd62");

  // KUKA|prc (legacy category "KUKA|prc" - NOT "PRC Preview")
  static readonly Guid G_prcCore  = new Guid("944339be-e143-491a-acab-b1ad6c53d8d6");
  static readonly Guid G_prcLin   = new Guid("cc36c78c-faaf-407d-bbd8-5004d61b3f7c");
  static readonly Guid G_prcTool  = new Guid("6a291b3f-3543-42dd-a1f5-abf9cc9684f3");
  static readonly Guid G_prcAnal  = new Guid("038498ff-17b9-4207-9bbb-9095ecc8bf66");
  static readonly Guid G_prcRobot = new Guid("3fa34cbe-f9fc-48d2-bdbf-1828a851d3f9"); // Agilus KR6-10 R1100-2

  static StringBuilder _log;
  static void L(string s) { _log.AppendLine(s); }

  // =========================================================================
  //  ENTRY
  // =========================================================================
  public static string Run(string root, string outDir)
  {
    _log = new StringBuilder();
    Directory.CreateDirectory(outDir);

    L("root   = " + root);
    L("outDir = " + outDir);
    L("");

    BuildTf09(root, outDir);
    L("");
    BuildFl01(root, outDir);

    return _log.ToString();
  }

  // =========================================================================
  //  SMALL HELPERS
  // =========================================================================

  static IGH_DocumentObject Emit(Guid id, string what)
  {
    IGH_DocumentObject o = Instances.ComponentServer.EmitObject(id);
    if (o == null) throw new Exception("component server has no " + what + " (" + id + ")");
    if (o.Attributes == null) o.CreateAttributes();
    return o;
  }

  static T Place<T>(GH_Document doc, T obj, float x, float y) where T : IGH_DocumentObject
  {
    if (obj.Attributes == null) obj.CreateAttributes();
    obj.Attributes.Pivot = new PointF(x, y);
    doc.AddObject(obj, false);
    return obj;
  }

  static IGH_Component Comp(GH_Document doc, Guid id, string what, float x, float y)
  {
    IGH_Component c = (IGH_Component)Emit(id, what);
    Place(doc, c, x, y);
    return c;
  }

  static GH_NumberSlider Slider(GH_Document doc, string nick, double min, double max,
                                double val, int decimals, float x, float y)
  {
    GH_NumberSlider s = new GH_NumberSlider();
    s.CreateAttributes();
    s.Slider.Minimum = (decimal)min;
    s.Slider.Maximum = (decimal)max;
    s.Slider.DecimalPlaces = decimals;
    s.SetSliderValue((decimal)val);
    s.NickName = nick;
    Place(doc, s, x, y);
    return s;
  }

  static GH_BooleanToggle Toggle(GH_Document doc, string nick, bool val, float x, float y)
  {
    GH_BooleanToggle t = new GH_BooleanToggle();
    t.CreateAttributes();
    t.Value = val;
    t.NickName = nick;
    Place(doc, t, x, y);
    return t;
  }

  static GH_Panel Panel(GH_Document doc, string nick, string text,
                        float x, float y, float w, float h, Color? colour)
  {
    GH_Panel p = new GH_Panel();
    p.CreateAttributes();
    p.NickName = nick;
    if (text != null) { p.UserText = text; }
    if (colour.HasValue) p.Properties.Colour = colour.Value;
    Place(doc, p, x, y);
    p.Attributes.Bounds = new RectangleF(x, y, w, h);
    return p;
  }

  /// A read-only note on the canvas. Panels are used rather than scribbles
  /// because a panel keeps its size and wraps, and this text has to stay
  /// readable at the zoom level someone reads a screenshot at.
  static GH_Panel Note(GH_Document doc, string text, float x, float y, float w, float h)
  {
    return Panel(doc, "note", text, x, y, w, h, Color.FromArgb(255, 250, 232, 178));
  }

  static void Wire(IGH_Component target, int inIdx, IGH_Param source)
  {
    target.Params.Input[inIdx].AddSource(source);
  }

  static void Wire(IGH_Component target, int inIdx, IGH_Component source, int outIdx)
  {
    target.Params.Input[inIdx].AddSource(source.Params.Output[outIdx]);
  }

  static void Group(GH_Document doc, string name, Color c, params IGH_DocumentObject[] members)
  {
    GH_Group g = new GH_Group();
    g.CreateAttributes();
    g.NickName = name;
    g.Colour = c;
    doc.AddObject(g, false);
    foreach (IGH_DocumentObject m in members) g.AddObject(m.InstanceGuid);
    g.ExpireCaches();
  }

  // ---- type hints ---------------------------------------------------------
  static IGH_TypeHint Hint(string t)
  {
    switch (t)
    {
      case "Mesh":         return new Grasshopper.Kernel.Parameters.Hints.GH_MeshHint();
      case "Curve":        return new Grasshopper.Kernel.Parameters.Hints.GH_CurveHint();
      case "Plane":        return new Grasshopper.Kernel.Parameters.Hints.GH_PlaneHint();
      case "Point3d":      return new Grasshopper.Kernel.Parameters.Hints.GH_Point3dHint();
      case "Vector3d":     return new Grasshopper.Kernel.Parameters.Hints.GH_Vector3dHint();
      case "int":          return new Grasshopper.Kernel.Parameters.Hints.GH_IntegerHint_CS();
      case "double":       return new Grasshopper.Kernel.Parameters.Hints.GH_DoubleHint_CS();
      case "bool":         return new Grasshopper.Kernel.Parameters.Hints.GH_BooleanHint_CS();
      case "string":       return new Grasshopper.Kernel.Parameters.Hints.GH_StringHint_CS();
      case "GeometryBase": return new Grasshopper.Kernel.Parameters.Hints.GH_GeometryBaseHint();
      default: throw new Exception("no type hint for '" + t + "'");
    }
  }

  // ---- the C# script component -------------------------------------------
  class In
  {
    public string Name, Type;
    public GH_ParamAccess Access;
    public bool Optional;
    public In(string n, string t, GH_ParamAccess a, bool opt)
    { Name = n; Type = t; Access = a; Optional = opt; }
  }

  static IGH_Component CSharp(GH_Document doc, string nick,
                              string usings, string body, string members,
                              In[] ins, string[] outs, float x, float y)
  {
    IGH_Component cs = (IGH_Component)Emit(G_CSharp, "C# Script component");

    // ---- inject the three panes ----
    PropertyInfo srcProp = cs.GetType().GetProperty("ScriptSource");
    if (srcProp == null) throw new Exception("C# component has no ScriptSource property");
    object src = srcProp.GetValue(cs, null);
    Type st = src.GetType();
    st.GetProperty("CustomUsing").SetValue(src, true, null);
    st.GetProperty("UsingCode").SetValue(src, DedupeUsings(usings), null);
    st.GetProperty("ScriptCode").SetValue(src, body, null);
    st.GetProperty("AdditionalCode").SetValue(src, members, null);

    // ---- replace the stock x/y inputs ----
    while (cs.Params.Input.Count > 0)
      cs.Params.UnregisterInputParameter(cs.Params.Input[0]);

    foreach (In i in ins)
    {
      Param_ScriptVariable p = new Param_ScriptVariable();
      p.Name = i.Name;
      p.NickName = i.Name;
      p.Access = i.Access;
      p.Optional = i.Optional;
      p.TypeHint = Hint(i.Type);
      cs.Params.RegisterInputParam(p);
    }

    // ---- outputs: keep "out" (the component's own message stream) ----
    while (cs.Params.Output.Count > 1)
      cs.Params.UnregisterOutputParameter(cs.Params.Output[cs.Params.Output.Count - 1]);

    foreach (string o in outs)
    {
      Param_GenericObject p = new Param_GenericObject();
      p.Name = o;
      p.NickName = o;
      cs.Params.RegisterOutputParam(p);
    }

    cs.Params.OnParametersChanged();
    cs.NickName = nick;
    Place(doc, cs, x, y);

    L("  C# '" + nick + "': " + cs.Params.Input.Count + " in, " +
      cs.Params.Output.Count + " out, " +
      (usings.Length + body.Length + members.Length) + " chars of code");
    return cs;
  }

  static string ReadPane(string path)
  {
    if (!File.Exists(path)) throw new Exception("missing source pane: " + path);
    return File.ReadAllText(path);
  }

  /// The C# component already opens with its own using block, so the eight
  /// namespaces it declares come out as CS0105 "appeared previously" warnings
  /// if the pane repeats them. Harmless, but a component wearing a warning
  /// balloon on a canvas someone is opening for the first time is not free.
  ///
  /// The .cs files keep all their usings - they have to, because pasting by
  /// hand is still the documented route and a pane has to stand on its own.
  /// This strips only the duplicates, on the way in.
  static readonly string[] BuiltInUsings = {
    "System", "System.Collections", "System.Collections.Generic",
    "Rhino", "Rhino.Geometry",
    "Grasshopper", "Grasshopper.Kernel",
    "Grasshopper.Kernel.Data", "Grasshopper.Kernel.Types"
  };

  static string DedupeUsings(string pane)
  {
    string[] lines = pane.Replace("\r\n", "\n").Split('\n');
    List<string> keep = new List<string>();
    int dropped = 0;

    foreach (string line in lines)
    {
      string t = line.Trim();
      bool drop = false;
      if (t.StartsWith("using ") && t.EndsWith(";"))
      {
        string ns = t.Substring(6, t.Length - 7).Trim();
        foreach (string b in BuiltInUsings)
          if (ns == b) { drop = true; break; }
      }
      if (drop) dropped++; else keep.Add(line);
    }

    keep.Add("");
    keep.Add("// " + dropped + " using directive(s) that the C# component already");
    keep.Add("// declares itself were removed here to keep the component free of");
    keep.Add("// CS0105 warnings. They are still in the .cs file this came from,");
    keep.Add("// which is the copy to paste from if you rebuild this by hand.");
    L("  deduped usings: dropped " + dropped);
    return string.Join("\r\n", keep.ToArray());
  }

  // =========================================================================
  //  SHARED: the KUKA|prc chain
  //
  //  Identical in both files apart from the tool number and tool plane, so it
  //  is built once. Legacy KUKA|prc components only - the lab's handoff note
  //  is explicit that nothing may be rewired to "PRC Preview".
  // =========================================================================
  static void PrcChain(GH_Document doc, IGH_Param cmdSource,
                       int toolNumber, double toolZ, string toolNote,
                       float x, float y)
  {
    IGH_Component lin = Comp(doc, G_prcLin, "LINear Movement", x, y);
    lin.Params.Input[0].AddSource(cmdSource);
    // Speed (input 1) is deliberately left unwired so KUKA|prc's own default
    // applies. The authoritative feeds live in the generated KRL, not here.

    // ---- tool ----
    GH_NumberSlider tz = Slider(doc, "toolZ mm", 0, 600, toolZ, 0, x - 40, y + 190);
    IGH_Component tpt = Comp(doc, G_ConPoint, "Construct Point", x + 150, y + 190);
    tpt.Params.Input[2].AddSource(tz);
    IGH_Component tpl = Comp(doc, G_XYPlane, "XY Plane", x + 330, y + 190);
    Wire(tpl, 0, tpt, 0);

    GH_NumberSlider tn = Slider(doc, "TOOL id", 0, 16, toolNumber, 0, x - 40, y + 250);

    IGH_Component tool = Comp(doc, G_prcTool, "Custom Tool: Plane", x + 480, y + 190);
    tool.Params.Input[1].AddSource(tn);
    Wire(tool, 2, tpl, 0);

    Note(doc, toolNote, x - 40, y + 300, 300, 78);

    // ---- robot + core ----
    IGH_Component rob  = Comp(doc, G_prcRobot, "Agilus KR6-10 R1100-2", x + 480, y + 90);
    GH_NumberSlider sim = Slider(doc, "SIM", 0, 1, 0, 3, x + 480, y - 60);

    IGH_Component core = Comp(doc, G_prcCore, "KUKA|prc CORE", x + 760, y);
    core.Params.Input[0].AddSource(sim);
    Wire(core, 1, lin, 0);
    Wire(core, 2, tool, 0);
    Wire(core, 3, rob,  0);

    // Analysis is a licensed component. On an unlicensed install it does not
    // just return nothing, it raises an error, so it ships LOCKED: present and
    // wired, contributing no red on a canvas that is otherwise clean.
    IGH_Component anal = Comp(doc, G_prcAnal, "Analysis", x + 980, y + 120);
    Wire(anal, 0, core, 1);
    anal.Locked = true;

    Note(doc,
      "ANALYSIS IS LOCKED - THIS IS DELIBERATE\r\n\r\n" +
      "It needs a licensed KUKA|prc. On this machine it raises a licence " +
      "error, so it is switched off rather than left to sit there red.\r\n\r\n" +
      "On the lab machine: right-click it > Enable. You then get numeric axis " +
      "values, reachability and singularity per move.\r\n\r\n" +
      "You do not need it to check reach. CORE itself goes orange or red and " +
      "says so - read the balloon on the CORE component.",
      x + 980, y + 200, 340, 190);

    Note(doc,
      "KUKA|prc CORE\r\n\r\n" +
      "Drag SIM from 0 to 1 to play the job.\r\n\r\n" +
      "If CORE reports unreachable or collided poses, the fix is almost " +
      "always to move the WORK, not the maths - the virtual robot stands at " +
      "the world origin, so anything you draw near the origin is underneath " +
      "it. Use the paper / part position sliders.\r\n\r\n" +
      "To write the .src: right-click CORE, open its settings, set the output " +
      "directory and file name, then tick the export.",
      x + 760, y + 190, 380, 210);

    Group(doc, "KUKA|prc  (legacy - never PRC Preview)",
          Color.FromArgb(60, 43, 107, 107), lin, tool, rob, core, anal, sim, tz, tn, tpt, tpl);
  }

  // =========================================================================
  //  TF-09
  // =========================================================================
  static void BuildTf09(string root, string outDir)
  {
    L("=== TF-09 ===");
    GH_Document doc = new GH_Document();
    // (GH_Document.DisplayName is read-only - the file name is the name.)

    string dir = Path.Combine(root, "02_TF09_pen_switching");
    string usings  = ReadPane(Path.Combine(dir, "TF09_usings.cs"));
    string body    = ReadPane(Path.Combine(dir, "TF09_body.cs"));
    string members = ReadPane(Path.Combine(dir, "TF09_helpers.cs"));

    In[] ins = new In[] {
      new In("curves",       "Curve",        GH_ParamAccess.list, false),
      new In("penIds",       "int",          GH_ParamAccess.list, true),
      new In("drawPlane",    "Plane",        GH_ParamAccess.item, false),
      new In("drawGeo",      "GeometryBase", GH_ParamAccess.item, true),
      new In("slotPlanes",   "Plane",        GH_ParamAccess.list, false),
      new In("slotTools",    "int",          GH_ParamAccess.list, true),
      new In("homePlane",    "Plane",        GH_ParamAccess.item, true),
      new In("leadIn",       "double",       GH_ParamAccess.item, true),
      new In("leadOut",      "double",       GH_ParamAccess.item, true),
      new In("hover",        "double",       GH_ParamAccess.item, true),
      new In("tiltDeg",      "double",       GH_ParamAccess.item, true),
      new In("resolution",   "double",       GH_ParamAccess.item, true),
      new In("optimize",     "bool",         GH_ParamAccess.item, false),
      new In("groupByPen",   "bool",         GH_ParamAccess.item, false),
      new In("startIndex",   "int",          GH_ParamAccess.item, true),
      new In("liveRun",      "bool",         GH_ParamAccess.item, true),
      new In("feedDraw",     "double",       GH_ParamAccess.item, true),
      new In("feedRapid",    "double",       GH_ParamAccess.item, true),
      new In("swapSeconds",  "double",       GH_ParamAccess.item, true),
      new In("jobName",      "string",       GH_ParamAccess.item, true),
      new In("selfTest",     "bool",         GH_ParamAccess.item, true),
      new In("pressDepth",   "double",       GH_ParamAccess.item, true),
      new In("baseIndex",    "int",          GH_ParamAccess.item, true),
      new In("magBaseIndex", "int",          GH_ParamAccess.item, true)
    };

    string[] outs = new string[] {
      "Targets","MoveTypes","Flat","FlatMoves","PenSequence","StrokeOrder",
      "OrderedCurves","TravelMoves","SwapLog","SwapCount","TravelDist",
      "DrawDist","CycleTime","KRL","Status","Log","SelfTest"
    };

    IGH_Component cs = CSharp(doc, "TF-09", usings, body, members, ins, outs, 640, 60);

    // ---- 0 curves : a demo drawing, internalised so the file solves on open
    Param_Curve pc = new Param_Curve();
    pc.CreateAttributes();
    pc.NickName = "curves";
    pc.Access = GH_ParamAccess.list;
    List<int> pens = new List<int>();
    foreach (Curve c in DemoDrawing(pens)) pc.PersistentData.Append(new GH_Curve(c));
    Place(doc, pc, 60, 40);

    // ---- 1 penIds
    Param_Integer pi = new Param_Integer();
    pi.CreateAttributes();
    pi.NickName = "penIds";
    pi.Access = GH_ParamAccess.list;
    foreach (int p in pens) pi.PersistentData.Append(new GH_Integer(p));
    Place(doc, pi, 60, 90);
    Wire(cs, 1, pi);

    // ---- 2 drawPlane, and placing the artwork in the cell
    //
    // TF-09 takes curves that are ALREADY where the paper is. drawPlane says
    // which way is up off the paper - it is not a transform, and moving it
    // does not drag the artwork with it.
    //
    // So the demo is authored around the origin, the way you would draw it,
    // and one point drives both: it moves the curves onto the paper AND it
    // becomes the paper's origin. They cannot drift apart.
    //
    // It has to be moved at all because the virtual robot stands at the world
    // origin. Artwork left there is underneath the arm, and every target comes
    // back unreachable - which reads like a broken toolpath and is not one.
    GH_NumberSlider paperX = Slider(doc, "paper X", -1200, 1200, 600, 0, 60, 108);
    GH_NumberSlider paperY = Slider(doc, "paper Y", -1200, 1200,   0, 0, 60, 140);
    GH_NumberSlider paperZ = Slider(doc, "paper Z",  -500, 1200, 200, 0, 60, 172);

    IGH_Component paperPt = Comp(doc, G_ConPoint, "Construct Point", 250, 108);
    paperPt.Params.Input[0].AddSource(paperX);
    paperPt.Params.Input[1].AddSource(paperY);
    paperPt.Params.Input[2].AddSource(paperZ);

    // Point -> Motion relies on Grasshopper's own point-to-vector cast.
    IGH_Component mv = Comp(doc, G_Move, "Move", 400, 40);
    Wire(mv, 0, pc);
    Wire(mv, 1, paperPt, 0);
    Wire(cs, 0, mv, 0);

    IGH_Component dp = Comp(doc, G_XYPlane, "XY Plane", 400, 140);
    Wire(dp, 0, paperPt, 0);
    Wire(cs, 2, dp, 0);

    // ---- 3 drawGeo left unwired (flat paper). Wire a surface for a curved sheet.

    // ---- 4 slotPlanes : a placeholder magazine, four slots in a row
    GH_NumberSlider slotX = Slider(doc, "slot X0",  -1000, 1000, 380, 0, 60, 204);
    GH_NumberSlider pitch = Slider(doc, "slot pitch",   20,  200,  80, 0, 60, 236);
    GH_NumberSlider slotY = Slider(doc, "slot Y",   -1000, 1000, -420, 0, 60, 268);
    IGH_Component ser = Comp(doc, G_Series, "Series", 260, 204);
    ser.Params.Input[0].AddSource(slotX);
    ser.Params.Input[1].AddSource(pitch);
    Param_Integer cnt = new Param_Integer();
    cnt.CreateAttributes(); cnt.NickName = "slots";
    cnt.PersistentData.Append(new GH_Integer(4));
    Place(doc, cnt, 60, 300);
    Wire(ser, 2, cnt);

    GH_NumberSlider slotZ = Slider(doc, "slot Z", -500, 1000, 150, 0, 60, 332);

    IGH_Component cp = Comp(doc, G_ConPoint, "Construct Point", 420, 204);
    Wire(cp, 0, ser, 0);
    cp.Params.Input[1].AddSource(slotY);
    cp.Params.Input[2].AddSource(slotZ);

    // Plane Normal, not XY Plane. A slot plane is handed to the robot as a
    // target verbatim, so it has to carry the same convention as every other
    // target: Z runs FROM the tool INTO the work. A world-XY plane has Z
    // pointing up, which asks the arm to approach the magazine from
    // underneath the floor - every magazine trip then comes back unreachable,
    // and the toolpath itself looks blameless.
    Param_Vector down = new Param_Vector();
    down.CreateAttributes();
    down.NickName = "into slot";
    down.PersistentData.Append(new GH_Vector(new Vector3d(0, 0, -1)));
    Place(doc, down, 420, 300);

    IGH_Component sp = Comp(doc, G_PlaneNorm, "Plane Normal", 580, 204);
    Wire(sp, 0, cp, 0);
    sp.Params.Input[1].AddSource(down);
    Wire(cs, 4, sp, 0);

    Note(doc,
      "WHERE THE PAPER IS\r\n\r\n" +
      "paper X/Y/Z do two jobs at once: they move the artwork into the cell " +
      "and they set the paper's origin. One point drives both, so the drawing " +
      "and the plane cannot drift apart.\r\n\r\n" +
      "Using your own curves? Wire them into Move in place of the demo param. " +
      "If they are already positioned in the cell, skip Move and wire them " +
      "straight into curves.",
      620, 108, 330, 170);

    Note(doc,
      "MAGAZINE - PLACEHOLDER COORDINATES\r\n\r\n" +
      "Four slots in a straight row, flat, so the definition solves and you " +
      "can see the swap logic work. Not one of these numbers has touched " +
      "hardware.\r\n\r\n" +
      "Before the robot moves: delete this little chain, put a Plane param " +
      "here instead, and set it from planes you have actually taught on the " +
      "cell. See krl/PENSWAP_README.md.\r\n\r\n" +
      "Whatever you replace it with, keep the convention: a slot plane's Z " +
      "runs FROM the tool INTO the slot. Get that backwards and the arm is " +
      "asked to reach the magazine from below.",
      60, 370, 470, 190);

    // ---- 5 slotTools, 6 homePlane left unwired (documented defaults)

    // ---- sliders 7..11
    float sy = 500;
    GH_NumberSlider leadIn  = Slider(doc, "leadIn",     0,  50,   5, 1, 60, sy);        sy += 40;
    GH_NumberSlider leadOut = Slider(doc, "leadOut",    0,  50,   5, 1, 60, sy);        sy += 40;
    GH_NumberSlider hover   = Slider(doc, "hover",      5, 100,  30, 1, 60, sy);        sy += 40;
    GH_NumberSlider tilt    = Slider(doc, "tiltDeg",  -45,  45,   0, 1, 60, sy);        sy += 40;
    GH_NumberSlider resol   = Slider(doc, "resolution", 0.1, 5, 0.5, 2, 60, sy);        sy += 50;
    Wire(cs,  7, leadIn); Wire(cs,  8, leadOut); Wire(cs, 9, hover);
    Wire(cs, 10, tilt);   Wire(cs, 11, resol);

    // ---- toggles 12,13,15,20
    GH_BooleanToggle opt  = Toggle(doc, "optimize",   true,  60, sy); sy += 40;
    GH_BooleanToggle grp  = Toggle(doc, "groupByPen", true,  60, sy); sy += 40;
    Wire(cs, 12, opt); Wire(cs, 13, grp);

    GH_NumberSlider start = Slider(doc, "startIndex", 0, 200, 0, 0, 60, sy); sy += 40;
    Wire(cs, 14, start);

    GH_BooleanToggle live = Toggle(doc, "liveRun",  false, 60, sy); sy += 40;
    Wire(cs, 15, live);
    Note(doc,
      "liveRun is OFF, so this is a DRY RUN: the pen is held one lift height " +
      "clear of the paper the whole way. Turn it on only when the paper, the " +
      "pen and the base are all real and taught.",
      280, sy - 40, 330, 74);

    sy += 50;
    GH_NumberSlider fDraw = Slider(doc, "feedDraw",     5, 200, 100, 0, 60, sy); sy += 40;
    GH_NumberSlider fRap  = Slider(doc, "feedRapid",   50, 800, 500, 0, 60, sy); sy += 40;
    GH_NumberSlider swapS = Slider(doc, "swapSeconds",  5,  90,  25, 0, 60, sy); sy += 40;
    Wire(cs, 16, fDraw); Wire(cs, 17, fRap); Wire(cs, 18, swapS);

    GH_Panel jobName = Panel(doc, "jobName", "DRAW_JOB", 60, sy, 150, 28, null);
    Wire(cs, 19, jobName); sy += 44;

    GH_BooleanToggle selfT = Toggle(doc, "selfTest", false, 60, sy); sy += 44;
    Wire(cs, 20, selfT);

    GH_NumberSlider press = Slider(doc, "pressDepth",   0, 10, 3, 2, 60, sy); sy += 40;
    GH_NumberSlider bIdx  = Slider(doc, "baseIndex",    1,  4, 1, 0, 60, sy); sy += 40;
    GH_NumberSlider mIdx  = Slider(doc, "magBaseIndex", 1,  4, 1, 0, 60, sy); sy += 40;
    Wire(cs, 21, press); Wire(cs, 22, bIdx); Wire(cs, 23, mIdx);

    Note(doc,
      "THE DEFAULTS ARE THE END-EFFECTOR SPEC\r\n\r\n" +
      "draw 100 mm/s  ·  air 500 mm/s  ·  lift 30 mm  ·  press 3 mm\r\n" +
      "BASE[1] worktable centre  ·  BASE[2] large-format shift\r\n\r\n" +
      "Straight out of the Key parameters table in " +
      "end-effectors/01-drawing/README.md. Unwire any slider and you get the " +
      "documented number back.",
      280, sy - 130, 330, 140);

    // ---- outputs ----
    GH_Panel status = Panel(doc, "Status",   null, 1080,  60, 300,  60, null);
    GH_Panel swaps  = Panel(doc, "SwapLog",  null, 1080, 140, 300, 130, null);
    GH_Panel nSwap  = Panel(doc, "SwapCount",null, 1080, 290, 140,  40, null);
    GH_Panel cycle  = Panel(doc, "CycleTime",null, 1080, 350, 140,  40, null);
    GH_Panel krl    = Panel(doc, "KRL",      null, 1080, 410, 300, 260, null);
    GH_Panel log    = Panel(doc, "Log",      null, 1080, 690, 300, 200, null);
    GH_Panel stest  = Panel(doc, "SelfTest", null, 1080, 910, 300, 160, null);

    status.AddSource(cs.Params.Output[15]);   // Status
    swaps .AddSource(cs.Params.Output[9]);    // SwapLog
    nSwap .AddSource(cs.Params.Output[10]);   // SwapCount
    cycle .AddSource(cs.Params.Output[13]);   // CycleTime
    krl   .AddSource(cs.Params.Output[14]);   // KRL
    log   .AddSource(cs.Params.Output[16]);   // Log
    stest .AddSource(cs.Params.Output[17]);   // SelfTest

    Note(doc,
      "THIS PANEL IS THE ROBOT PROGRAM\r\n\r\n" +
      "Right-click the KRL panel > Stream Contents, and save it as " +
      "DRAW_JOB.src next to PENSWAP.src and PENSWAP.dat.\r\n\r\n" +
      "It is NOT the same file KUKA|prc writes. prc does not know the pen " +
      "magazine exists, so it cannot emit the swap calls. prc is here to " +
      "answer 'can the arm reach this' - this panel is what actually runs.",
      1420, 410, 340, 200);

    // ---- prc chain, fed from Flat (output 3): the whole job in order ----
    PrcChain(doc, cs.Params.Output[3], 1, 150,
      "TOOL PLANE IS A GUESS\r\n\r\n" +
      "150 mm off the flange, TOOL[1] = technical pen. Replace with the " +
      "measured TCP before you trust the reach check.",
      1900, 120);

    Note(doc,
      "TF-09 - PEN-SWITCHING LOOP\r\n\r\n" +
      "Board item TF-09. Also closes D1-01 (draw order) and D1-03 (lead-in / " +
      "lead-out), which are the same code path.\r\n\r\n" +
      "Everything on this canvas is orientation-independent: rotate the paper " +
      "plane anywhere and the job rotates with it. Flip selfTest on to have " +
      "the component prove that to you.",
      640, 1120, 420, 170);

    Save(doc, outDir, "TF09_pen_drawing");
  }

  // =========================================================================
  //  FL-01
  // =========================================================================
  static void BuildFl01(string root, string outDir)
  {
    L("=== FL-01 ===");
    GH_Document doc = new GH_Document();
    // (GH_Document.DisplayName is read-only - the file name is the name.)

    string dir = Path.Combine(root, "01_FL01_mesh_to_planes");
    string usings  = ReadPane(Path.Combine(dir, "FL01_usings.cs"));
    string body    = ReadPane(Path.Combine(dir, "FL01_body.cs"));
    string members = ReadPane(Path.Combine(dir, "FL01_helpers.cs"));

    In[] ins = new In[] {
      new In("geo",          "Mesh",     GH_ParamAccess.item, false),
      new In("axisMode",     "int",      GH_ParamAccess.item, true),
      new In("customAxis",   "Vector3d", GH_ParamAccess.item, true),
      new In("sections",     "int",      GH_ParamAccess.item, true),
      new In("samples",      "int",      GH_ParamAccess.item, true),
      new In("loopMode",     "int",      GH_ParamAccess.item, true),
      new In("normalMode",   "int",      GH_ParamAccess.item, true),
      new In("flipApproach", "bool",     GH_ParamAccess.item, true),
      new In("tiltDeg",      "double",   GH_ParamAccess.item, true),
      new In("rollMode",     "int",      GH_ParamAccess.item, true),
      new In("leadLen",      "double",   GH_ParamAccess.item, true),
      new In("minSpacing",   "double",   GH_ParamAccess.item, true),
      new In("maxTurnDeg",   "double",   GH_ParamAccess.item, true),
      new In("seamGuide",    "Point3d",  GH_ParamAccess.list, true),
      new In("fromFrame",    "Plane",    GH_ParamAccess.item, true),
      new In("toFrame",      "Plane",    GH_ParamAccess.item, true),
      new In("selfTest",     "bool",     GH_ParamAccess.item, true)
    };

    string[] outs = new string[] {
      "Planes","Points","MoveTypes","Sections","SlicePlanes","PartFrame",
      "SliceAxis","Count","MaxTurn","Status","Log","SelfTest"
    };

    IGH_Component cs = CSharp(doc, "FL-01", usings, body, members, ins, outs, 620, 60);

    // ---- 0 geo : demo part internalised
    Param_Mesh pm = new Param_Mesh();
    pm.CreateAttributes();
    pm.NickName = "geo";
    pm.PersistentData.Append(new GH_Mesh(DemoPart()));
    Place(doc, pm, 60, 40);
    Wire(cs, 0, pm);

    Note(doc,
      "THE PART\r\n\r\n" +
      "A demo mesh is baked into this param so the file works the moment you " +
      "open it. To use your own: right-click > Clear values, then right-click " +
      "> Set one Mesh and pick it in Rhino.\r\n\r\n" +
      "Deliberately lopsided. A part that is symmetric about its own long " +
      "axis has no unique seam, and FL-01 will say so rather than guess.",
      60, 90, 400, 160);

    float sy = 280;
    GH_NumberSlider axisMode = Slider(doc, "axisMode",  0,   5,  0, 0, 60, sy); sy += 40;
    GH_NumberSlider sect     = Slider(doc, "sections",  2, 100, 12, 0, 60, sy); sy += 40;
    // 64, not the component's own default of 32. A five-lobed section turns
    // its normal fast at the lobes, and at 32 the wrist has to swing ~56 deg
    // between neighbouring targets - over the limit, and the component says so.
    // Sampling finer is the honest fix; it makes each step smaller.
    GH_NumberSlider samp     = Slider(doc, "samples",   6, 200, 64, 0, 60, sy); sy += 40;
    GH_NumberSlider loopM    = Slider(doc, "loopMode",  0,   1,  0, 0, 60, sy); sy += 40;
    GH_NumberSlider normM    = Slider(doc, "normalMode",0,   1,  0, 0, 60, sy); sy += 40;
    Wire(cs, 1, axisMode); Wire(cs, 3, sect); Wire(cs, 4, samp);
    Wire(cs, 5, loopM);    Wire(cs, 6, normM);

    Note(doc,
      "axisMode  0 long · 1 short · 2 X · 3 Y · 4 Z · 5 custom\r\n" +
      "Leave it on 0. Modes 2-4 lock the job to the world and the component " +
      "warns when you use them.\r\n\r\n" +
      "loopMode    0 largest loop only · 1 every loop\r\n" +
      "normalMode  0 mesh normal · 1 radial from the slice centre",
      280, 280, 330, 150);

    GH_BooleanToggle flip = Toggle(doc, "flipApproach", false, 60, sy); sy += 44;
    Wire(cs, 7, flip);

    GH_NumberSlider tilt  = Slider(doc, "tiltDeg",   -45, 45,  0, 1, 60, sy); sy += 40;
    GH_NumberSlider rollM = Slider(doc, "rollMode",    0,  1,  0, 0, 60, sy); sy += 40;
    GH_NumberSlider lead  = Slider(doc, "leadLen",     0, 100, 0, 1, 60, sy); sy += 40;
    GH_NumberSlider minSp = Slider(doc, "minSpacing",  0,  20, 0, 2, 60, sy); sy += 40;
    GH_NumberSlider maxT  = Slider(doc, "maxTurnDeg",  5,  90, 30, 0, 60, sy); sy += 50;
    Wire(cs, 8, tilt); Wire(cs, 9, rollM); Wire(cs, 10, lead);
    Wire(cs, 11, minSp); Wire(cs, 12, maxT);

    Note(doc,
      "rollMode  0 rigid · 1 free spin\r\n" +
      "A hot wire is a rigid bow and cares which way the wrist is rolled. " +
      "A pen or a router bit is round and does not - free spin lets the " +
      "solver take the gentler wrist path.",
      280, sy - 210, 330, 110);

    // 13 seamGuide, 14 fromFrame, 15 toFrame left unwired
    GH_BooleanToggle selfT = Toggle(doc, "selfTest", false, 60, sy); sy += 44;
    Wire(cs, 16, selfT);

    Note(doc,
      "selfTest - THE ORIENTATION PROOF\r\n\r\n" +
      "Turn it on. The component rotates the whole part eight times by random " +
      "rotations, re-runs everything, rotates the answers back and measures " +
      "the difference. You want RESULT: PASS.\r\n\r\n" +
      "Turn it back off afterwards - it costs eight extra solves.",
      280, sy - 40, 330, 150);

    // ---- outputs ----
    GH_Panel status = Panel(doc, "Status",  null, 1060,  60, 300,  60, null);
    GH_Panel count  = Panel(doc, "Count",   null, 1060, 140, 140,  40, null);
    GH_Panel maxTrn = Panel(doc, "MaxTurn", null, 1060, 200, 140,  40, null);
    GH_Panel log    = Panel(doc, "Log",     null, 1060, 260, 300, 220, null);
    GH_Panel stest  = Panel(doc, "SelfTest",null, 1060, 500, 300, 180, null);
    status.AddSource(cs.Params.Output[10]);  // Status
    count .AddSource(cs.Params.Output[8]);   // Count
    maxTrn.AddSource(cs.Params.Output[9]);   // MaxTurn
    log   .AddSource(cs.Params.Output[11]);  // Log
    stest .AddSource(cs.Params.Output[12]);  // SelfTest

    Note(doc,
      "PREVIEW PartFrame FIRST\r\n\r\n" +
      "Before anything else, look at PartFrame in the Rhino viewport. It is " +
      "the model's own axes worked out from the shape itself. If the red " +
      "arrow runs down the long direction of your part, the pipeline has " +
      "understood it and everything downstream will be right.",
      1060, 700, 340, 160);

    // ---- one slice at a time into prc ----
    GH_NumberSlider pass = Slider(doc, "pass select", 0, 30, 0, 0, 1500, 60);
    IGH_Component br = Comp(doc, G_TreeBr, "Tree Branch", 1660, 60);
    Wire(br, 0, cs, 1);              // Planes
    br.Params.Input[1].AddSource(pass);

    Note(doc,
      "ONE PASS AT A TIME\r\n\r\n" +
      "Planes comes out as a tree, one branch per slice. The robot cuts one " +
      "slice per run, so Tree Branch picks the pass and you export one .src " +
      "per pass - the same index-then-cut workflow as the ruled-wire " +
      "definition.",
      1500, 120, 330, 150);

    PrcChain(doc, br.Params.Output[0], 4, 350,
      "TOOL PLANE IS A GUESS\r\n\r\n" +
      "350 mm off the flange, TOOL[4] = wire midpoint (TOOL[5]/[6] are the " +
      "wire ends). Replace with the measured values from the real frame.",
      1900, 340);

    Note(doc,
      "FL-01 - MESH TO KUKA|prc PLANES\r\n\r\n" +
      "Board item FL-01. A mesh goes in, usable planes come out.\r\n\r\n" +
      "Usable means four things, and all four are measured: every plane is " +
      "orthonormal, every origin sits on the mesh, every tool axis points " +
      "into the material, and the tree structure matches the slices.",
      620, 900, 420, 170);

    Save(doc, outDir, "FL01_mesh_to_planes");
  }

  // =========================================================================
  //  DEMO GEOMETRY
  // =========================================================================

  /// A small multi-pen drawing: two nested rectangles (pen 0), two circles
  /// (pen 1), four hatch lines (pen 2). Interleaved on purpose so that
  /// grouping by pen visibly collapses the swap count to two.
  static List<Curve> DemoDrawing(List<int> pens)
  {
    List<Curve> cs = new List<Curve>();
    pens.Clear();

    cs.Add(Rect(-100, -70, 200, 140)); pens.Add(0);
    cs.Add(new ArcCurve(new Circle(new Plane(new Point3d(-45, 0, 0), Vector3d.ZAxis), 30))); pens.Add(1);
    cs.Add(Rect(-80, -52, 160, 104));  pens.Add(0);
    cs.Add(new ArcCurve(new Circle(new Plane(new Point3d(45, 0, 0), Vector3d.ZAxis), 18)));  pens.Add(1);

    for (int i = 0; i < 4; i++)
    {
      double x = -30 + i * 20;
      cs.Add(new LineCurve(new Point3d(x, -35, 0), new Point3d(x + 14, 35, 0)));
      pens.Add(2);
    }
    return cs;
  }

  static Curve Rect(double x, double y, double w, double h)
  {
    Polyline p = new Polyline();
    p.Add(new Point3d(x, y, 0));
    p.Add(new Point3d(x + w, y, 0));
    p.Add(new Point3d(x + w, y + h, 0));
    p.Add(new Point3d(x, y + h, 0));
    p.Add(new Point3d(x, y, 0));
    return new PolylineCurve(p);
  }

  /// A tapered, twisted, five-lobed bar 260 mm long.
  ///
  /// The lobe count and the extra harmonics are not decoration. A section with
  /// any rotational symmetry about the long axis has two equally correct seam
  /// answers, and FL-01 refuses to choose between them - correctly, but it
  /// makes a confusing first thing to open. This shape has no symmetry at all.
  static Mesh DemoPart()
  {
    int rings = 26, around = 40;
    double len = 260.0;
    Mesh m = new Mesh();

    // Sat 620 mm out and 250 mm up, not on the origin: the virtual robot
    // stands at the origin, and a part built around it is inside the arm.
    double cx = 620.0, cz = 250.0;

    for (int i = 0; i < rings; i++)
    {
      double t = (double)i / (rings - 1);
      double x = -len * 0.5 + len * t;
      double taper = 1.0 - 0.45 * t * t;
      double twist = 0.9 * t;

      for (int j = 0; j < around; j++)
      {
        double a = 2.0 * Math.PI * j / around + twist;
        double r = 42.0 * taper *
                   (1.0 + 0.26 * Math.Sin(5 * a) + 0.11 * Math.Cos(2 * a) + 0.06 * Math.Sin(3 * a));
        m.Vertices.Add(new Point3d(cx + x, r * Math.Cos(a), cz + r * Math.Sin(a)));
      }
    }

    for (int i = 0; i < rings - 1; i++)
      for (int j = 0; j < around; j++)
      {
        int j2 = (j + 1) % around;
        int a = i * around + j, b = i * around + j2;
        int c = (i + 1) * around + j2, d = (i + 1) * around + j;
        m.Faces.AddFace(a, b, c, d);
      }

    // caps
    int c0 = m.Vertices.Add(new Point3d(cx - len * 0.5, 0, cz));
    int c1 = m.Vertices.Add(new Point3d(cx + len * 0.5, 0, cz));
    for (int j = 0; j < around; j++)
    {
      int j2 = (j + 1) % around;
      m.Faces.AddFace(c0, j2, j);
      m.Faces.AddFace(c1, (rings - 1) * around + j, (rings - 1) * around + j2);
    }

    m.Normals.ComputeNormals();
    m.Compact();
    return m;
  }

  // =========================================================================
  //  SAVE + VERIFY
  // =========================================================================
  /// Writes to local disk first and copies in.
  ///
  /// The destination is a Google Drive streaming mount, and writing straight
  /// to it produced a file truncated to one 4 KB cluster once already. A local
  /// write cannot half-succeed, and File.Copy either lands the whole thing or
  /// throws.
  static void Save(GH_Document doc, string outDir, string name)
  {
    string dest = Path.Combine(outDir, name + ".gh");
    string tmp  = Path.Combine(Path.GetTempPath(), name + ".gh");

    GH_IO.Serialization.GH_Archive a = new GH_IO.Serialization.GH_Archive();
    a.AppendObject(doc, "Definition");
    if (!a.WriteToFile(tmp, true, false)) throw new Exception("could not write " + tmp);

    long n = new FileInfo(tmp).Length;
    if (n < 8000) throw new Exception("suspiciously small archive (" + n + " bytes) for " + name);

    File.Copy(tmp, dest, true);
    long m = new FileInfo(dest).Length;
    if (m != n) throw new Exception("copy landed short: " + m + " of " + n + " bytes");
    File.Delete(tmp);

    L("  wrote " + Path.GetFileName(dest) + "  (" + m + " bytes, " +
      doc.ObjectCount + " objects on canvas)");
  }
}

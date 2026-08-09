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
  static readonly Guid G_Orient   = new Guid("378d0690-9da0-4dd1-ab16-1d15246e7c22"); // Geometry / Source / Target

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
  /// toolPlaneSource: wire the TCP in from elsewhere (the hotwire component).
  ///                  null builds the simple "N mm straight off the flange"
  ///                  chain, which is all a pen needs.
  /// toolGeo:         mesh in FLANGE COORDINATES for prc to draw and collide.
  static void PrcChain(GH_Document doc, IGH_Param cmdSource,
                       int toolNumber, double toolZ, string toolNote,
                       float x, float y,
                       IGH_Param toolPlaneSource, Mesh toolGeo)
  {
    List<IGH_DocumentObject> grouped = new List<IGH_DocumentObject>();

    IGH_Component lin = Comp(doc, G_prcLin, "LINear Movement", x, y);
    lin.Params.Input[0].AddSource(cmdSource);
    // Speed (input 1) is deliberately left unwired so KUKA|prc's own default
    // applies. The authoritative feeds live in the generated KRL, not here.

    // ---- tool ----
    GH_NumberSlider tn = Slider(doc, "TOOL id", 0, 16, toolNumber, 0, x - 40, y + 250);

    IGH_Component tool = Comp(doc, G_prcTool, "Custom Tool: Plane", x + 480, y + 190);
    tool.Params.Input[1].AddSource(tn);

    if (toolPlaneSource != null)
    {
      tool.Params.Input[2].AddSource(toolPlaneSource);
    }
    else
    {
      GH_NumberSlider tz = Slider(doc, "toolZ mm", 0, 600, toolZ, 0, x - 40, y + 190);
      IGH_Component tpt = Comp(doc, G_ConPoint, "Construct Point", x + 150, y + 190);
      tpt.Params.Input[2].AddSource(tz);
      IGH_Component tpl = Comp(doc, G_XYPlane, "XY Plane", x + 330, y + 190);
      Wire(tpl, 0, tpt, 0);
      Wire(tool, 2, tpl, 0);
      grouped.AddRange(new IGH_DocumentObject[] { tz, tpt, tpl });
    }

    if (toolGeo != null)
    {
      Param_Mesh tg = new Param_Mesh();
      tg.CreateAttributes();
      tg.NickName = "tool mesh";
      tg.Access = GH_ParamAccess.list;
      tg.PersistentData.Append(new GH_Mesh(toolGeo));
      Place(doc, tg, x + 300, y + 120);
      tool.Params.Input[0].AddSource(tg);
      grouped.Add(tg);
      L("  prc: tool geometry internalised, " + toolGeo.Faces.Count + " faces");
    }

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

    grouped.AddRange(new IGH_DocumentObject[] { lin, tool, rob, core, anal, sim, tn });
    Group(doc, "KUKA|prc  (legacy - never PRC Preview)",
          Color.FromArgb(60, 43, 107, 107), grouped.ToArray());
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
    // TF-09 takes curves that are ALREADY where the paper is, and drawPlane
    // says which way is up off it. The two have to agree, so ONE thing drives
    // both: the PENTOOL component's DrawPlane output becomes the paper's own
    // frame AND the target of the Orient that carries the artwork onto it.
    // They cannot drift apart.
    //
    // Orient, not Move. The board now STANDS UP, so placing the drawing is a
    // rotation as well as a translation - a Move would leave the strokes lying
    // flat on the floor while the sheet they belong to stood vertical.
    //
    // The artwork has to be placed at all because the virtual robot stands at
    // the world origin: strokes left there are underneath the arm, and every
    // target comes back unreachable - which reads like a broken toolpath and
    // is not one.
    IGH_Component pt = BuildPenTool(doc, root);

    IGH_Component src = Comp(doc, G_XYPlane, "XY Plane", 250, 200);   // world XY
    IGH_Component ori = Comp(doc, G_Orient, "Orient", 420, 40);
    Wire(ori, 0, pc);                       // the artwork, authored flat
    Wire(ori, 1, src, 0);                   // Source: world XY
    Wire(ori, 2, pt, 1);                    // Target: the board
    Wire(cs, 0, ori, 0);

    Wire(cs, 2, pt, 1);                     // drawPlane <- the same board

    // ---- 3 drawGeo left unwired (flat sheet). Wire a surface for a curved one.

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
      "WHERE THE ARTWORK IS\r\n\r\n" +
      "The demo is authored FLAT around the origin, the way you would draw it " +
      "on paper, and Orient carries it onto whatever board the PENTOOL " +
      "component builds. The same plane feeds drawPlane, so the strokes and " +
      "the sheet cannot drift apart - move the board and the drawing goes " +
      "with it, standing up included.\r\n\r\n" +
      "It has to be placed at all because the virtual robot stands at the " +
      "world origin. Artwork left there is underneath the arm and every " +
      "target comes back unreachable, which reads like a broken toolpath and " +
      "is not one.\r\n\r\n" +
      "Using your own curves? Wire them into Orient in place of the demo " +
      "param, authored flat about the origin. If they are already positioned " +
      "in the cell, skip Orient and wire them straight into curves - but then " +
      "drawPlane has to match them by hand.",
      620, 108, 340, 260);

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
      "asked to reach the magazine from below.\r\n\r\n" +
      "THE MAGAZINE DOES NOT MOVE WITH THE BOARD, and that is correct - a " +
      "magazine is bolted to the cell. But it does mean that if you swing the " +
      "board round with the board X/Y sliders, you have to bring these three " +
      "with it or the swap trips are left reaching across the cell.\r\n\r\n" +
      "Measured: board to the LEFT at 0 / 900 is unreachable with the slots " +
      "left here, and solves exactly as well as the front once they are put " +
      "at the matching 90 deg image - slot X0 420, slot Y 380. Rotate (x, y) " +
      "to (-y, x) and it comes back.",
      60, 370, 470, 330);

    // ---- 5 slotTools, 6 homePlane left unwired (documented defaults)

    // ---- sliders 7..11
    float sy = 500;
    GH_NumberSlider leadIn  = Slider(doc, "leadIn",     0,  50,   5, 1, 60, sy);        sy += 40;
    GH_NumberSlider leadOut = Slider(doc, "leadOut",    0,  50,   5, 1, 60, sy);        sy += 40;
    GH_NumberSlider hover   = Slider(doc, "hover",      5, 100,  30, 1, 60, sy);        sy += 40;
    GH_NumberSlider resol   = Slider(doc, "resolution", 0.1, 5, 0.5, 2, 60, sy);        sy += 50;
    Wire(cs,  7, leadIn); Wire(cs,  8, leadOut); Wire(cs, 9, hover);
    Wire(cs, 11, resol);

    // tiltDeg has no slider of its own any more - it comes from PENTOOL's
    // penLeanDeg. One component then owns the whole orientation story and can
    // check its own advice, which matters here because the wrong lean makes
    // the job unreachable for a reason that is invisible in the geometry.
    Wire(cs, 10, pt, 4);

    Note(doc,
      "THE PEN LEAN COMES FROM PENTOOL\r\n\r\n" +
      "tiltDeg is wired from the PENTOOL component's penLeanDeg, not from a " +
      "slider here, so the component that decides how the board is hung is " +
      "also the one that decides how far the pen leans - and can warn when " +
      "the two do not go together.\r\n\r\n" +
      "They do not go together at 0. A sheet standing square-on to the robot " +
      "aims the pen straight back down the arm's own reach line, the wrist " +
      "goes flat, and prc refuses the job even though every target is well " +
      "inside the reach ring. 20 deg of lean is the shipped answer.",
      280, sy - 130, 340, 210);

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
    //
    // The TCP now comes from the PENTOOL component rather than a slider, and
    // the tool geometry is the lab's own pen tool in flange coordinates - the
    // same arrangement the FL-01 file uses for the hotwire.
    PrcChain(doc, cs.Params.Output[3], 1, CellBuild.PenToolZ,
      "TOOL[1] = THE PEN NIB\r\n\r\n" +
      "The TCP comes from the PENTOOL component, not from a slider here, and " +
      "the tool mesh is the lab's Pen_Tool Rev 008 rebuilt in flange " +
      "coordinates. Nib " + CellBuild.PenToolZ + " mm off the flange face.",
      1900, 120, pt.Params.Output[2], CellBuild.PenToolMesh());

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
  //  THE PEN TOOL AND THE BOARD SWITCH
  //
  //  Upstream of TF-09, unlike the hotwire component which is downstream of
  //  FL-01. The reason is in PN_body.cs: TF-09 needs a draw plane before it
  //  can build a target, so deriving the board from TF-09's own output would
  //  be a cycle and Grasshopper will not run one.
  // =========================================================================
  static IGH_Component BuildPenTool(GH_Document doc, string root)
  {
    string dir = Path.Combine(root, "07_pen_tool");

    In[] ins = new In[] {
      new In("toolX",       "double",  GH_ParamAccess.item, true),
      new In("toolY",       "double",  GH_ParamAccess.item, true),
      new In("toolZ",       "double",  GH_ParamAccess.item, true),
      new In("toolA",       "double",  GH_ParamAccess.item, false),
      new In("toolB",       "double",  GH_ParamAccess.item, false),
      new In("toolC",       "double",  GH_ParamAccess.item, false),
      new In("penAxis",     "int",     GH_ParamAccess.item, false),
      new In("boardOrigin", "Point3d", GH_ParamAccess.item, true),
      new In("boardW",      "double",  GH_ParamAccess.item, true),
      new In("boardH",      "double",  GH_ParamAccess.item, true),
      new In("boardOrient", "int",     GH_ParamAccess.item, false),
      new In("cardinal",    "int",     GH_ParamAccess.item, false),
      new In("leanDeg",     "double",  GH_ParamAccess.item, true),
      new In("spinDeg",     "double",  GH_ParamAccess.item, true),
      new In("flipBoardZ",  "bool",    GH_ParamAccess.item, true),
      new In("zToRobot",    "bool",    GH_ParamAccess.item, true),
      new In("robotBase",   "Plane",   GH_ParamAccess.item, true),
      new In("reachMax",    "double",  GH_ParamAccess.item, true),
      new In("reachMin",    "double",  GH_ParamAccess.item, true),
      new In("grid",        "int",     GH_ParamAccess.item, true),
      new In("penLeanDeg",  "double",  GH_ParamAccess.item, true)
    };
    string[] outs = new string[] {
      "DrawPlane","ToolPlane","ToolAbc","PenLean","Board","Corners","FlangePts",
      "PenLines","Approach","Status","Log"
    };

    IGH_Component pt = CSharp(doc, "PENTOOL",
      ReadPane(Path.Combine(dir, "PN_usings.cs")),
      ReadPane(Path.Combine(dir, "PN_body.cs")),
      ReadPane(Path.Combine(dir, "PN_helpers.cs")),
      ins, outs, 250, 620);

    // ---- the four numbers from the pendant's CUSTOM TOOL dialog ----
    float y = 620;
    GH_NumberSlider tZ = Slider(doc, "Tool Z", 0, 600, CellBuild.PenToolZ, 1, 60, y); y += 34;
    GH_NumberSlider tA = Slider(doc, "Tool A", -180, 180, 0, 0, 60, y); y += 34;
    GH_NumberSlider tB = Slider(doc, "Tool B", -180, 180, 0, 0, 60, y); y += 34;
    GH_NumberSlider tC = Slider(doc, "Tool C", -180, 180, 0, 0, 60, y); y += 34;
    GH_NumberSlider pA = Slider(doc, "penAxis", 0, 2, 2, 0, 60, y); y += 44;
    Wire(pt, 2, tZ); Wire(pt, 3, tA); Wire(pt, 4, tB); Wire(pt, 5, tC); Wire(pt, 6, pA);

    // ---- where the sheet hangs ----
    GH_NumberSlider bX = Slider(doc, "board X", -1600, 1600, CellBuild.BoardX, 0, 60, y); y += 34;
    GH_NumberSlider bY = Slider(doc, "board Y", -1600, 1600, CellBuild.BoardY, 0, 60, y); y += 34;
    GH_NumberSlider bZ = Slider(doc, "board Z",  -400, 1400, CellBuild.BoardZ, 0, 60, y); y += 34;
    IGH_Component bp = Comp(doc, G_ConPoint, "Construct Point", 60, y); y += 60;
    bp.Params.Input[0].AddSource(bX);
    bp.Params.Input[1].AddSource(bY);
    bp.Params.Input[2].AddSource(bZ);
    Wire(pt, 7, bp, 0);

    GH_NumberSlider bW = Slider(doc, "board W", 50, 900, CellBuild.BoardW, 0, 60, y); y += 34;
    GH_NumberSlider bH = Slider(doc, "board H", 50, 900, CellBuild.BoardH, 0, 60, y); y += 44;
    Wire(pt, 8, bW); Wire(pt, 9, bH);

    // ---- THE BOARD SWITCH ----
    // 0 VERTICAL: the sheet stands up and its own Z faces the robot. That is
    // the arrangement the cell uses and the one the brief asked for.
    GH_NumberSlider bO = Slider(doc, "boardOrient", 0, 3, 0, 0, 60, y); y += 34;
    GH_NumberSlider cD = Slider(doc, "cardinal",    0, 4, 0, 0, 60, y); y += 34;
    GH_NumberSlider lD = Slider(doc, "leanDeg",     0, 90, 45, 0, 60, y); y += 34;
    GH_NumberSlider sD = Slider(doc, "spinDeg", -180, 180, 0, 0, 60, y); y += 34;
    Wire(pt, 10, bO); Wire(pt, 11, cD); Wire(pt, 12, lD); Wire(pt, 13, sD);

    GH_BooleanToggle fB = Toggle(doc, "flipBoardZ", false, 60, y); y += 34;
    GH_BooleanToggle zR = Toggle(doc, "zToRobot",   true,  60, y); y += 44;
    Wire(pt, 14, fB); Wire(pt, 15, zR);

    GH_NumberSlider rX = Slider(doc, "reachMax", 200, 2000, 1101, 0, 60, y); y += 34;
    GH_NumberSlider rN = Slider(doc, "reachMin",   0, 1000,  460, 0, 60, y); y += 34;
    // 20, not 0. A sheet standing square-on to the robot puts the arm's own
    // reach line straight down the pen, and the wrist has to go flat to manage
    // it - a singular pose that prc refuses even though every target is well
    // inside the ring. Measured band on this cell: 15..30 solves, 0..10 does
    // not, 40 is too far the other way. Drag it to 0 and watch prc go red.
    GH_NumberSlider pL = Slider(doc, "penLeanDeg", -45, 45, 20, 0, 60, y); y += 44;
    Wire(pt, 17, rX); Wire(pt, 18, rN); Wire(pt, 20, pL);
    // 16 robotBase and 19 grid left unwired - the virtual robot stands at the
    // world origin and 5 x 5 samples is plenty for a flat sheet.

    GH_Panel abc = Panel(doc, "ToolAbc", null, 620, 620, 330, 46, null);
    GH_Panel st  = Panel(doc, "Status",  null, 620, 680, 330, 60, null);
    GH_Panel lg  = Panel(doc, "Log",     null, 620, 755, 330, 300, null);
    abc.AddSource(pt.Params.Output[3]);
    st .AddSource(pt.Params.Output[10]);
    lg .AddSource(pt.Params.Output[11]);

    Note(doc,
      "THE BOARD SWITCH  -  Z FACES THE ROBOT\r\n\r\n" +
      "boardOrient  0 VERTICAL  1 FLAT  2 TILTED  3 AWAY\r\n" +
      "cardinal     0 AUTO  1 +X  2 -X  3 +Y  4 -Y\r\n\r\n" +
      "With a hotwire the switch acted on the TOOL, because a wire is a line " +
      "and how you lay it decides whether it cuts. A pen draws with a POINT, " +
      "so the tool has only one sensible attitude - straight into the paper - " +
      "and the interesting question moves to the WORK.\r\n\r\n" +
      "  0 VERTICAL  the sheet stands on an easel and faces the arm. Its own " +
      "blue Z runs back at the robot.  <-- shipped\r\n" +
      "  1 FLAT      lying on a table. Its normal points at the ceiling, so " +
      "nothing about it faces the robot and zToRobot says so.\r\n" +
      "  2 TILTED    a drafting table. leanDeg 0 is FLAT, 90 is VERTICAL - " +
      "the continuum the other two are the ends of.\r\n" +
      "  3 AWAY      the sheet turned round. Kept so you can see wrong.\r\n\r\n" +
      "MEASURED at the shipped pen lean of 20 deg: only 0 and 3 solve. 1 and " +
      "2 want a different lean, because the lean and the board attitude are " +
      "one question and not two - which is exactly why they live on the same " +
      "component. Change boardOrient and expect to re-tune penLeanDeg.\r\n\r\n" +
      "cardinal AUTO reads where the board sits: in front is +X, behind is " +
      "-X, either side is +/-Y. Drag board X/Y/Z and the sheet turns to keep " +
      "facing the arm.\r\n\r\n" +
      "WHICH Z IS WHICH, because this is the thing that confuses everyone:\r\n" +
      "  the BOARD's Z points OUT of the sheet, back at the robot\r\n" +
      "  the TARGET's Z points INTO the sheet, down the pen\r\n" +
      "They are opposite by definition, and they have to be - the pen reaches " +
      CellBuild.PenToolZ + " mm ahead of the wrist, so the flange has to sit " +
      "between the robot and the paper or the arm cannot reach past its own " +
      "tool. Preview FlangePts to see exactly that.\r\n\r\n" +
      "AND THE ONE THAT ACTUALLY BIT: penLeanDeg.\r\n" +
      "Standing the sheet up square-on aims the pen back down the arm's reach " +
      "line, so the wrist goes flat and the pose is singular - prc refuses the " +
      "whole job with every target well inside the ring. Measured here:\r\n" +
      "   lean 0, 5, 10        UNREACHABLE\r\n" +
      "   lean 15, 20, 25, 30  clean\r\n" +
      "   lean 40              UNREACHABLE, too far the other way\r\n" +
      "Drag penLeanDeg to 0 and watch prc go red. That is the switch proving " +
      "the point rather than the note asking you to take it on trust.",
      420, 620, 340, 620);

    Note(doc,
      "THE PEN TOOL\r\n\r\n" +
      "Tool Z / A / B / C are the four numbers from the pendant's CUSTOM TOOL " +
      "dialog. Type what the pendant says and the simulation matches the cell.\r\n\r\n" +
      "The defaults are the real tool, measured in the lab's own " +
      "Pen_Tool Rev 008.3dm: nib " + CellBuild.PenToolZ + " mm off the flange " +
      "face, and A / B / C all ZERO because a pen runs straight out along the " +
      "flange axis and has nothing to twist. That is the whole difference from " +
      "the hotwire, whose crossbar needs A -90 / B -90 / C 0 to describe it.\r\n\r\n" +
      "Rev 008 is a bench layout rather than a flange-referenced model, so the " +
      "tool is REBUILT from its dimensions instead of lifted whole. Which face " +
      "is the flange face is a definition, not a measurement - see " +
      "07_pen_tool/PN_README.md.",
      420, 1110, 340, 260);

    return pt;
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

    // ---- 0 geo : demo part, standing vertical, on position sliders
    //
    // Internalised at the ORIGIN and moved by the sliders, rather than baked
    // in place. That is what makes zMode = AUTO worth having: drag the part
    // round the robot and the approach direction follows it.
    Param_Mesh pm = new Param_Mesh();
    pm.CreateAttributes();
    pm.NickName = "geo";
    pm.PersistentData.Append(new GH_Mesh(DemoPart()));
    Place(doc, pm, 60, 40);

    // 1273 / -20 / 302 is where the part sits in HoitWire_V1.3dm, kept because
    // it is the real setup and because it measures clean - see the note.
    GH_NumberSlider partX = Slider(doc, "part X", -1600, 1600, 1273, 0, 60, 92);
    GH_NumberSlider partY = Slider(doc, "part Y", -1600, 1600,  -20, 0, 60, 124);
    GH_NumberSlider partZ = Slider(doc, "part Z",  -600, 1400,  302, 0, 60, 156);
    IGH_Component partPt = Comp(doc, G_ConPoint, "Construct Point", 250, 92);
    partPt.Params.Input[0].AddSource(partX);
    partPt.Params.Input[1].AddSource(partY);
    partPt.Params.Input[2].AddSource(partZ);

    IGH_Component pmv = Comp(doc, G_Move, "Move", 420, 40);
    Wire(pmv, 0, pm);
    Wire(pmv, 1, partPt, 0);
    Wire(cs, 0, pmv, 0);

    Note(doc,
      "THE PART - STANDING VERTICAL\r\n\r\n" +
      "Baked in at the origin and moved here, so dragging part X/Y/Z walks it " +
      "round the cell and cardinal = AUTO re-picks the approach to match.\r\n\r\n" +
      "Default 1273 / -20 / 302 - where the part sits in HoitWire_V1.3dm.\r\n\r\n" +
      "THE REACHABLE ZONE IS A RING, NOT A MAXIMUM. The flange ends up 422 mm " +
      "back from the cut, so a part that is too CLOSE is just as impossible as " +
      "one too far - the arm would have to fold inside itself. Measured against " +
      "prc with this tool:\r\n" +
      "   part X below ~900    flange under ~460   FAILS, too close\r\n" +
      "   part X 900 .. 1450   flange 460..1050    clean\r\n" +
      "This is why moving the part nearer is not automatically the fix.\r\n\r\n" +
      "Your own part: right-click geo > Clear values, then > Set one Mesh.\r\n\r\n" +
      "The demo shape is deliberately lopsided - a part symmetric about its " +
      "own long axis has no unique seam, and FL-01 says so rather than guess.",
      620, 92, 360, 250);

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

    // ---- the hotwire tool, and the flip toggles ----------------------------
    string hwDir = Path.Combine(root, "06_hotwire_tool");
    In[] hwIns = new In[] {
      new In("toolX",        "double", GH_ParamAccess.item, true),
      new In("toolY",        "double", GH_ParamAccess.item, true),
      new In("toolZ",        "double", GH_ParamAccess.item, true),
      new In("toolA",        "double", GH_ParamAccess.item, false),
      new In("toolB",        "double", GH_ParamAccess.item, false),
      new In("toolC",        "double", GH_ParamAccess.item, false),
      new In("wireSpan",     "double", GH_ParamAccess.item, true),
      new In("wireAxis",     "int",    GH_ParamAccess.item, false),
      new In("flipToolZ",    "bool",   GH_ParamAccess.item, true),
      new In("flipToolSpin", "bool",   GH_ParamAccess.item, true),
      new In("targets",      "Plane",  GH_ParamAccess.list, true),
      new In("flipZ",        "bool",   GH_ParamAccess.item, true),
      new In("flipX",        "bool",   GH_ParamAccess.item, true),
      new In("tiltDeg",      "double", GH_ParamAccess.item, true),
      new In("spinDeg",      "double", GH_ParamAccess.item, true),
      new In("frameMode",    "int",    GH_ParamAccess.item, false),
      new In("cardinal",     "int",    GH_ParamAccess.item, false),
      new In("robotBase",    "Plane",  GH_ParamAccess.item, true),
      new In("reachMax",     "double", GH_ParamAccess.item, true),
      new In("reachMin",     "double", GH_ParamAccess.item, true),
      new In("cutOrient",    "int",    GH_ParamAccess.item, false),
      new In("zToRobot",     "bool",   GH_ParamAccess.item, true)
    };
    string[] hwOuts = new string[] {
      "ToolPlane","ToolAbc","Targets","WireLines","WireEndA","WireEndB",
      "FlangePts","Approach","Status","Log"
    };

    IGH_Component hw = CSharp(doc, "HOTWIRE",
      ReadPane(Path.Combine(hwDir, "HW_usings.cs")),
      ReadPane(Path.Combine(hwDir, "HW_body.cs")),
      ReadPane(Path.Combine(hwDir, "HW_helpers.cs")),
      hwIns, hwOuts, 1900, 60);

    // The four numbers from the CUSTOM TOOL dialog on the pendant.
    float hy = 60;
    GH_NumberSlider tZ = Slider(doc, "Tool Z",  0, 800, 422, 0, 1560, hy); hy += 34;
    GH_NumberSlider tA = Slider(doc, "Tool A", -180, 180, -90, 0, 1560, hy); hy += 34;
    GH_NumberSlider tB = Slider(doc, "Tool B", -180, 180, -90, 0, 1560, hy); hy += 34;
    GH_NumberSlider tC = Slider(doc, "Tool C", -180, 180,   0, 0, 1560, hy); hy += 34;
    GH_NumberSlider wS = Slider(doc, "wireSpan", 50, 800, 415.8, 1, 1560, hy); hy += 34;
    GH_NumberSlider wA = Slider(doc, "wireAxis",  0,   2, 2, 0, 1560, hy); hy += 40;
    Wire(hw, 2, tZ); Wire(hw, 3, tA); Wire(hw, 4, tB); Wire(hw, 5, tC);
    Wire(hw, 6, wS); Wire(hw, 7, wA);

    GH_BooleanToggle fTZ = Toggle(doc, "flipToolZ",    false, 1560, hy); hy += 34;
    GH_BooleanToggle fTS = Toggle(doc, "flipToolSpin", false, 1560, hy); hy += 34;
    Wire(hw, 8, fTZ); Wire(hw, 9, fTS);

    Wire(hw, 10, br, 0);                       // the chosen pass

    GH_BooleanToggle fZ = Toggle(doc, "flipZ",  false, 1560, hy); hy += 34;
    GH_BooleanToggle fX = Toggle(doc, "flipX",  false, 1560, hy); hy += 34;
    // 90, not 0. FL-01 hands out planes whose Z points INTO the material,
    // which is right for a point tool and wrong for a wire - it would put the
    // wire in end-on. 90 lays it across the travel. Drag it to 0 and watch the
    // HOTWIRE component start complaining; that is the toggle doing its job.
    // tiltDeg only bites when zMode is 0. Left at 0 because zMode ships on.
    GH_NumberSlider  tl = Slider(doc, "tiltDeg", -180, 180,  0, 0, 1560, hy); hy += 34;
    GH_NumberSlider  sp = Slider(doc, "spinDeg", -180, 180,  0, 0, 1560, hy); hy += 40;
    Wire(hw, 11, fZ); Wire(hw, 12, fX); Wire(hw, 13, tl); Wire(hw, 14, sp);

    // ---- THE APPROACH SWITCH ----
    GH_NumberSlider zM = Slider(doc, "frameMode", 0, 2, 1, 0, 1560, hy); hy += 34;
    GH_NumberSlider xM = Slider(doc, "cardinal",  0, 4, 0, 0, 1560, hy); hy += 34;
    GH_NumberSlider rM = Slider(doc, "reachMax", 200, 2000, 1101, 0, 1560, hy); hy += 34;
    GH_NumberSlider rN = Slider(doc, "reachMin",   0, 1000,  460, 0, 1560, hy); hy += 40;
    // 0 VERTICAL - the wire stands up and spans the height of an upright part,
    // which removes material far better than nibbling at it side-on.
    GH_NumberSlider cO = Slider(doc, "cutOrient", 0, 3, 0, 0, 1560, hy); hy += 34;
    GH_BooleanToggle zR = Toggle(doc, "zToRobot", false, 1560, hy); hy += 40;
    Wire(hw, 15, zM); Wire(hw, 16, xM); Wire(hw, 18, rM); Wire(hw, 19, rN);
    Wire(hw, 20, cO); Wire(hw, 21, zR);
    // 17 robotBase left unwired - the virtual robot stands at the world origin

    GH_Panel abc  = Panel(doc, "ToolAbc", null, 2300,  60, 330,  46, null);
    GH_Panel hSt  = Panel(doc, "Status",  null, 2300, 120, 330,  60, null);
    GH_Panel hLog = Panel(doc, "Log",     null, 2300, 195, 330, 250, null);
    abc .AddSource(hw.Params.Output[2]);
    hSt .AddSource(hw.Params.Output[9]);
    hLog.AddSource(hw.Params.Output[10]);

    Note(doc,
      "THE APPROACH SWITCH\r\n\r\n" +
      "cardinal   0 AUTO  1 +X  2 -X  3 +Y  4 -Y\r\n" +
      "frameMode  0 keep  1 CUT  2 wire-on-cardinal\r\n\r\n" +
      "FL-01's Z is RADIAL - it swings right round the loop, so somewhere in " +
      "every pass it points back at the robot and asks the arm to reach " +
      "through the part. That was the problem.\r\n\r\n" +
      "cardinal AUTO reads where the part sits: in front is +X, behind is -X, " +
      "either side is +/-Y. Drag the part X/Y/Z sliders and the approach " +
      "re-picks itself.\r\n\r\n" +
      "frameMode says what that direction drives, and the two are NOT the same:\r\n" +
      "  1 CUT   drives the ARM. The wire is laid across the travel, tangent " +
      "to the surface. Both reach and cutting satisfied.  <-- shipped\r\n" +
      "  2 WIRE  drives the WIRE, literally. For this tool that points the " +
      "wire end-on into the foam. Try it and read the warning.\r\n\r\n" +
      "Both can be right at once because reach acts on X (the flange sits " +
      "422 mm back along it) and cutting acts on Z (the wire lies on it).\r\n\r\n" +
      "cutOrient says what the WIRE does, and it is the one to change per part:\r\n" +
      "  0 VERTICAL  up and down - spans the height of an upright part  <-- shipped\r\n" +
      "  1 ACROSS    across the travel, tangent - follows a tilted part\r\n" +
      "  2 ALONG     along the travel - slides down its own kerf\r\n" +
      "  3 CARDINAL  along the approach - goes in end-on\r\n" +
      "2 and 3 are the wrong answers, kept so you can see wrong.\r\n\r\n" +
      "zToRobot turns frame Z back towards the robot. It will REFUSE while the " +
      "wire is on tool Z and vertical, because then Z is the wire and can only " +
      "point up or down. Read the Log - it explains and tells you the tool " +
      "definition that separates them.",
      1560, hy + 44, 330, 470);

    Note(doc,
      "THE HOTWIRE\r\n\r\n" +
      "Tool Z / A / B / C are the four numbers from the pendant's CUSTOM TOOL " +
      "dialog. Type what the pendant says and the simulation matches the cell. " +
      "The defaults are the real tool: Z 422, A -90, B -90, C 0.\r\n\r\n" +
      "wireSpan 415.8 is the span MODELLED in Hotwire_2.1.3dm. The usable " +
      "cutting span is shorter and still has to be measured.\r\n\r\n" +
      "flipZ approaches from the other side. flipX reverses travel. spinDeg " +
      "rolls about the approach. tiltDeg only bites when zMode = 0.\r\n\r\n" +
      "Preview WireLines to see where the wire is, and FlangePts to see where " +
      "the WRIST has to be - that is the one the reach check measures.",
      1560, hy + 526, 330, 270);

    Note(doc,
      "A -90 / B -90 / C 0 IS GIMBAL LOCK\r\n\r\n" +
      "B = +/-90 is the pose where only A+C is determined, so the pendant may " +
      "read back A 0 / B -90 / C -90 for the very same orientation. Nothing is " +
      "wrong when that happens.",
      2300, 400, 330, 130);

    PrcChain(doc, hw.Params.Output[3], 4, 422,
      "TOOL[4] = WIRE MIDPOINT\r\n\r\n" +
      "The TCP comes from the HOTWIRE component, not from a slider here, and " +
      "the tool mesh is the lab's own Hotwire Rev2.1 in flange coordinates.",
      1900, 620, hw.Params.Output[1], CellBuild.ReducedTool());

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
  /// Public so CellBuild can put the SAME curves in the Rhino model. If these
  /// two ever drift apart, the .3dm stops describing the .gh.
  public static List<Curve> DemoDrawing(List<int> pens)
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
  public static Mesh DemoPart()
  {
    int rings = 26, around = 40;
    double len = 260.0;
    Mesh m = new Mesh();

    // Built STANDING VERTICAL and centred on the origin. Vertical because that
    // is how the part sits in the cell; on the origin because the canvas moves
    // it, and a mesh that is already somewhere cannot be moved somewhere else
    // without the two offsets fighting.
    //
    // The long axis runs up Z, so the ring coordinate below is a height.
    double cx = 0.0, cz = 0.0;

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
        // x is the position along the part's long axis, which is now Z.
        m.Vertices.Add(new Point3d(cx + r * Math.Cos(a), r * Math.Sin(a), cz + x));
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
    int c0 = m.Vertices.Add(new Point3d(cx, 0, cz - len * 0.5));
    int c1 = m.Vertices.Add(new Point3d(cx, 0, cz + len * 0.5));
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

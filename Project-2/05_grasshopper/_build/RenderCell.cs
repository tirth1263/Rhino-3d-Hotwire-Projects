// ---------------------------------------------------------------------------
// RenderCell - draws the hotwire cell and the approach switch to PNG.
//
// These are RENDERS, not screen captures. Rhino runs headless here, so there
// is no window to photograph; every image is drawn from the same geometry the
// .gh file computes, by asking Rhino to display it off-screen. Each one is
// captioned as a render on the image itself so it cannot be mistaken for a
// screenshot of the Grasshopper canvas.
//
// Tooling, not a deliverable. C# only, no Python.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;

using Rhino;
using Rhino.Display;
using Rhino.Geometry;

public static class RenderCell
{
  const int W = 1600, H = 1000;

  static StringBuilder _log;
  static void L(string s) { _log.AppendLine(s); }

  public class Scene
  {
    public Mesh Part;
    public Mesh Tool;                       // already placed in the cell
    public List<Plane> Targets = new List<Plane>();
    public List<Line> Wires = new List<Line>();
    public List<Point3d> Flange = new List<Point3d>();
    public Vector3d Approach = Vector3d.Unset;
    public string Title = "", Sub = "", Verdict = "";
    public bool Bad = false;

    // ---- the pen job needs three things the hotwire one did not ----
    //
    // Added rather than forked so both jobs are drawn by the same code and
    // cannot drift into looking different for reasons that are not real.
    public List<Curve> Curves = new List<Curve>();  // the strokes, or the sheet
    public string HeroLabel = "wire";               // what the bold line is
    public string Legend = null;                    // null keeps the hotwire one
  }

  // =========================================================================
  public static string Run(string outDir, List<Scene> scenes)
  {
    _log = new StringBuilder();
    Directory.CreateDirectory(outDir);

    int i = 1;
    foreach (Scene s in scenes)
    {
      string name = "R" + i.ToString("00") + "_" +
                    s.Title.ToLower().Replace(" ", "_").Replace(",", "")
                           .Replace("(", "").Replace(")", "").Replace("/", "-") + ".png";
      Draw(s, Path.Combine(outDir, name));
      L("  " + name);
      i++;
    }
    return _log.ToString();
  }

  // =========================================================================
  //  A painter's-algorithm 3D view. Rhino's own view capture needs a document
  //  and a viewport, neither of which exists in a headless build, so the scene
  //  is projected and drawn directly - which also keeps the images identical
  //  from run to run.
  // =========================================================================
  static void Draw(Scene s, string path)
  {
    // ---- camera: fit whatever is in the scene, so the part never ends up a
    //      speck in the corner just because it moved ----
    BoundingBox bb = new BoundingBox(new Point3d(-300, -300, 0), new Point3d(300, 300, 400));
    if (s.Part != null) bb.Union(s.Part.GetBoundingBox(true));
    if (s.Tool != null) bb.Union(s.Tool.GetBoundingBox(true));
    foreach (Point3d f in s.Flange) bb.Union(f);
    foreach (Line w in s.Wires) { bb.Union(w.From); bb.Union(w.To); }
    foreach (Curve c in s.Curves) if (c != null) bb.Union(c.GetBoundingBox(true));

    Point3d target = bb.Center;
    Vector3d dir = new Vector3d(-0.60, -0.68, -0.42);   // down over the shoulder
    dir.Unitize();

    Vector3d fwd = dir;
    Vector3d right = Vector3d.CrossProduct(fwd, Vector3d.ZAxis); right.Unitize();
    Vector3d up = Vector3d.CrossProduct(right, fwd); up.Unitize();
    Point3d eye = target - dir * 6000.0;

    // widest extent as seen by the camera, with a margin
    double halfW = 0.0, halfH = 0.0;
    foreach (Point3d c in bb.GetCorners())
    {
      Vector3d v = c - target;
      halfW = Math.Max(halfW, Math.Abs(v * right));
      halfH = Math.Max(halfH, Math.Abs(v * up));
    }
    double scale = Math.Min((W * 0.44) / Math.Max(halfW, 1.0),
                            ((H - 150) * 0.44) / Math.Max(halfH, 1.0));

    using (Bitmap bmp = new Bitmap(W, H))
    using (Graphics g = Graphics.FromImage(bmp))
    {
      g.SmoothingMode = SmoothingMode.AntiAlias;
      g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
      g.Clear(Color.FromArgb(246, 246, 244));

      Func<Point3d, PointF> P = delegate(Point3d p)
      {
        Vector3d v = p - target;
        double x = v * right, y = v * up;
        return new PointF((float)(W * 0.5 + x * scale), (float)(H * 0.56 - y * scale));
      };

      // ---- ground grid ----
      using (Pen gp = new Pen(Color.FromArgb(224, 224, 220), 1f))
        for (int k = -2000; k <= 2400; k += 200)
        {
          g.DrawLine(gp, P(new Point3d(k, -2000, 0)), P(new Point3d(k, 2000, 0)));
          g.DrawLine(gp, P(new Point3d(-2000, k, 0)), P(new Point3d(2400, k, 0)));
        }

      // ---- robot base footprint + reach ring ----
      DrawCircle(g, P, Point3d.Origin, 160, Color.FromArgb(120, 120, 120), 2f);
      DrawCircle(g, P, Point3d.Origin, 1101, Color.FromArgb(200, 170, 170), 1.5f);
      DrawCircle(g, P, Point3d.Origin, 460, Color.FromArgb(200, 170, 170), 1.5f);
      Label(g, P(new Point3d(1101, 0, 0)), "flange reach 1101", Color.FromArgb(170, 120, 120), 11f);
      Label(g, P(new Point3d(460, 0, 0)), "inner 460", Color.FromArgb(170, 120, 120), 11f);

      // ---- world axes at the robot ----
      Arrow(g, P, Point3d.Origin, new Point3d(420, 0, 0), Color.FromArgb(200, 60, 60), 2.5f, "+X");
      Arrow(g, P, Point3d.Origin, new Point3d(0, 420, 0), Color.FromArgb(60, 150, 60), 2.5f, "+Y");
      Arrow(g, P, Point3d.Origin, new Point3d(0, 0, 420), Color.FromArgb(60, 90, 210), 2.5f, "+Z");

      // ---- part ----
      if (s.Part != null) Shade(g, P, s.Part, eye, Color.FromArgb(226, 200, 96), Color.FromArgb(150, 130, 50));

      // ---- a stick arm, base to flange to cut point, so it is obvious how
      //      far the robot is being asked to stretch ----
      if (s.Flange.Count > 0 && s.Targets.Count > 0)
      {
        int mid = s.Flange.Count / 2;
        Point3d shoulder = new Point3d(0, 0, 400);
        using (Pen ap = new Pen(Color.FromArgb(90, 60, 60, 60), 7f))
        {
          ap.StartCap = LineCap.Round; ap.EndCap = LineCap.Round;
          g.DrawLine(ap, P(Point3d.Origin), P(shoulder));
          g.DrawLine(ap, P(shoulder), P(s.Flange[mid]));
        }
        using (Pen tp = new Pen(Color.FromArgb(140, 60, 60, 60), 3f))
          g.DrawLine(tp, P(s.Flange[mid]), P(s.Targets[mid].Origin));
      }

      // ---- curves: the artwork and the sheet outline ----
      //
      // Drawn as polylines rather than through Rhino's display pipeline,
      // because there is no viewport here. 2 mm of chord tolerance is finer
      // than one pixel at any scale these images use.
      using (Pen cp = new Pen(Color.FromArgb(230, 30, 30, 30), 2.2f))
        foreach (Curve c in s.Curves)
        {
          if (c == null) continue;
          Polyline pl;
          PolylineCurve pc = c.ToPolyline(2.0, 0.02, 1.0, 0.0);
          if (pc == null || !pc.TryGetPolyline(out pl) || pl.Count < 2) continue;
          PointF[] q = new PointF[pl.Count];
          for (int k = 0; k < pl.Count; k++) q[k] = P(pl[k]);
          g.DrawLines(cp, q);
        }

      // ---- wires ----
      using (Pen wp = new Pen(Color.FromArgb(90, 225, 70, 40), 1.2f))
        foreach (Line w in s.Wires) g.DrawLine(wp, P(w.From), P(w.To));

      // ---- flange points ----
      using (SolidBrush fb = new SolidBrush(Color.FromArgb(170, 40, 90, 180)))
        foreach (Point3d f in s.Flange)
        { PointF q = P(f); g.FillEllipse(fb, q.X - 3f, q.Y - 3f, 6f, 6f); }

      // ---- tool, drawn at one representative target ----
      if (s.Tool != null) Shade(g, P, s.Tool, eye, Color.FromArgb(150, 168, 190), Color.FromArgb(90, 105, 125));

      // ---- target frames, thinned hard so the part stays visible ----
      int step = Math.Max(1, s.Targets.Count / 12);
      for (int i = 0; i < s.Targets.Count; i += step)
      {
        Plane t = s.Targets[i];
        Seg(g, P, t.Origin, t.Origin + t.XAxis * 130, Color.FromArgb(215, 200, 60, 60), 2.2f);
        Seg(g, P, t.Origin, t.Origin + t.ZAxis * 110, Color.FromArgb(215, 60, 90, 210), 2.2f);
      }

      // one wire drawn boldly, so its attitude is unmistakable
      if (s.Wires.Count > 0)
      {
        Line w = s.Wires[s.Wires.Count / 2];
        using (Pen wp = new Pen(Color.FromArgb(255, 225, 70, 40), 4f))
        { wp.StartCap = LineCap.Round; wp.EndCap = LineCap.Round; g.DrawLine(wp, P(w.From), P(w.To)); }
        Label(g, P(w.To), s.HeroLabel, Color.FromArgb(200, 60, 30), 12f);
      }

      // ---- caption ----
      Caption(g, s);
      bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
  }

  // =========================================================================
  static void Shade(Graphics g, Func<Point3d, PointF> P, Mesh m, Point3d eye,
                    Color face, Color edge)
  {
    if (m.FaceNormals.Count != m.Faces.Count) m.FaceNormals.ComputeFaceNormals();

    // painter's algorithm: far faces first
    int n = m.Faces.Count;
    double[] depth = new double[n];
    int[] order = new int[n];
    for (int i = 0; i < n; i++)
    {
      MeshFace f = m.Faces[i];
      // Mesh vertices are Point3f - single precision - so they have to be
      // widened before any Point3d arithmetic will compile.
      Point3d c = (V(m, f.A) + V(m, f.B) + V(m, f.C)) / 3.0;
      depth[i] = -c.DistanceTo(eye);
      order[i] = i;
    }
    Array.Sort(depth, order);

    Vector3d lamp = new Vector3d(-0.4, -0.5, 0.76); lamp.Unitize();

    foreach (int i in order)
    {
      MeshFace f = m.Faces[i];
      Vector3d nrm = new Vector3d(m.FaceNormals[i]);
      Point3d a = V(m, f.A), b = V(m, f.B), c = V(m, f.C);

      // back-face cull
      Point3d ctr = (a + b + c) / 3.0;
      if (nrm * (ctr - eye) > 0) continue;

      double lit = 0.35 + 0.65 * Math.Max(0.0, nrm * lamp);
      Color col = Color.FromArgb(
        (int)Math.Min(255, face.R * lit), (int)Math.Min(255, face.G * lit),
        (int)Math.Min(255, face.B * lit));

      PointF[] pts = f.IsQuad
        ? new PointF[] { P(a), P(b), P(c), P(V(m, f.D)) }
        : new PointF[] { P(a), P(b), P(c) };

      using (SolidBrush br = new SolidBrush(col)) g.FillPolygon(br, pts);
    }
  }

  static Point3d V(Mesh m, int i)
  {
    Point3f p = m.Vertices[i];
    return new Point3d(p.X, p.Y, p.Z);
  }

  static void DrawCircle(Graphics g, Func<Point3d, PointF> P, Point3d c, double r, Color col, float w)
  {
    PointF[] pts = new PointF[73];
    for (int i = 0; i <= 72; i++)
    {
      double a = i * Math.PI / 36.0;
      pts[i] = P(new Point3d(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a), c.Z));
    }
    using (Pen p = new Pen(col, w)) g.DrawLines(p, pts);
  }

  static void Seg(Graphics g, Func<Point3d, PointF> P, Point3d a, Point3d b, Color c, float w)
  { using (Pen p = new Pen(c, w)) g.DrawLine(p, P(a), P(b)); }

  static void Arrow(Graphics g, Func<Point3d, PointF> P, Point3d a, Point3d b, Color c, float w, string tag)
  {
    using (Pen p = new Pen(c, w))
    {
      p.EndCap = LineCap.ArrowAnchor;
      g.DrawLine(p, P(a), P(b));
    }
    Label(g, P(b), tag, c, 12f);
  }

  static void Label(Graphics g, PointF at, string s, Color c, float size)
  {
    using (Font f = new Font("Segoe UI", size, FontStyle.Bold))
    using (SolidBrush b = new SolidBrush(c))
      g.DrawString(s, f, b, at.X + 4, at.Y - 8);
  }

  static void Caption(Graphics g, Scene s)
  {
    using (SolidBrush bg = new SolidBrush(Color.FromArgb(238, 255, 255, 255)))
      g.FillRectangle(bg, 0, 0, W, 96);
    using (Pen line = new Pen(Color.FromArgb(210, 210, 205), 1f))
      g.DrawLine(line, 0, 96, W, 96);

    using (Font t = new Font("Segoe UI", 19f, FontStyle.Bold))
    using (SolidBrush b = new SolidBrush(Color.FromArgb(30, 30, 30)))
      g.DrawString(s.Title, t, b, 24, 12);

    // Wrapped in a box rather than drawn on one line. The captions explain a
    // measurement and are long on purpose, and a single line ran under the
    // verdict in the top right.
    using (Font t = new Font("Segoe UI", 12f))
    using (SolidBrush b = new SolidBrush(Color.FromArgb(90, 90, 90)))
      g.DrawString(s.Sub, t, b, new RectangleF(24, 44, W - 430, 50));

    if (!string.IsNullOrEmpty(s.Verdict))
    {
      Color vc = s.Bad ? Color.FromArgb(190, 50, 40) : Color.FromArgb(30, 130, 70);
      using (Font t = new Font("Segoe UI", 14f, FontStyle.Bold))
      using (SolidBrush b = new SolidBrush(vc))
      {
        SizeF sz = g.MeasureString(s.Verdict, t);
        g.DrawString(s.Verdict, t, b, W - sz.Width - 24, 34);
      }
    }

    string legend = s.Legend ?? ("red = frame X (arm direction)   blue = frame Z (the wire)   " +
                                 "blue dots = where the flange must be");
    using (Font t = new Font("Segoe UI", 10f, FontStyle.Italic))
    using (SolidBrush b = new SolidBrush(Color.FromArgb(150, 150, 150)))
      g.DrawString("RENDER of the real computed geometry - not a screen capture.  " + legend,
                   t, b, 24, H - 26);
  }
}

using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace EpicChimeMuter
{
    static class Art
    {
        public static readonly Color Bg = Color.FromArgb(16, 16, 20);
        public static readonly Color Surface = Color.FromArgb(25, 25, 31);
        public static readonly Color SurfaceHover = Color.FromArgb(36, 36, 45);
        public static readonly Color Border = Color.FromArgb(44, 44, 54);
        public static readonly Color Text = Color.FromArgb(245, 245, 247);
        public static readonly Color SubText = Color.FromArgb(160, 160, 172);
        public static readonly Color DimText = Color.FromArgb(108, 108, 120);
        public static readonly Color Accent = Color.FromArgb(0, 120, 242);
        public static readonly Color AccentLight = Color.FromArgb(56, 150, 255);
        public static readonly Color Amber = Color.FromArgb(245, 166, 35);
        public static readonly Color TrackOff = Color.FromArgb(56, 56, 68);
        public static readonly Color TrackOffHover = Color.FromArgb(70, 70, 84);
        public static readonly Color Danger = Color.FromArgb(232, 17, 35);

        public static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Alpha(Color c, int alpha)
        {
            return Color.FromArgb(alpha, c);
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
            if (d <= 0f)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, RectangleF r, float radius, Color c)
        {
            using (var path = RoundRect(r, radius))
            using (var b = new SolidBrush(c))
                g.FillPath(b, path);
        }

        public static void StrokeRound(Graphics g, RectangleF r, float radius, Color c)
        {
            using (var path = RoundRect(r, radius))
            using (var p = new Pen(c))
                g.DrawPath(p, path);
        }

        // A notification bell, optionally with a "muted" slash cut through it.
        public static void DrawBell(Graphics g, RectangleF r, Color color, bool slashed)
        {
            Func<float, float, PointF> P = (x, y) => new PointF(r.X + x * r.Width, r.Y + y * r.Height);

            GraphicsPath slashCut = null;
            float stroke = r.Width * 0.085f;
            PointF s1 = P(0.13f, 0.10f), s2 = P(0.87f, 0.90f);
            if (slashed)
            {
                slashCut = new GraphicsPath();
                slashCut.AddLine(s1, s2);
                using (var wide = new Pen(Color.Black, stroke * 2.8f))
                {
                    wide.StartCap = wide.EndCap = LineCap.Round;
                    slashCut.Widen(wide);
                }
            }

            GraphicsState state = g.Save();
            if (slashCut != null) g.SetClip(slashCut, CombineMode.Exclude);
            using (var body = new GraphicsPath())
            using (var b = new SolidBrush(color))
            {
                body.AddBezier(P(0.50f, 0.14f), P(0.31f, 0.14f), P(0.25f, 0.29f), P(0.25f, 0.47f));
                body.AddLine(P(0.25f, 0.47f), P(0.24f, 0.62f));
                body.AddBezier(P(0.24f, 0.62f), P(0.23f, 0.69f), P(0.16f, 0.71f), P(0.14f, 0.76f));
                body.AddLine(P(0.14f, 0.76f), P(0.86f, 0.76f));
                body.AddBezier(P(0.86f, 0.76f), P(0.84f, 0.71f), P(0.77f, 0.69f), P(0.76f, 0.62f));
                body.AddLine(P(0.76f, 0.62f), P(0.75f, 0.47f));
                body.AddBezier(P(0.75f, 0.47f), P(0.75f, 0.29f), P(0.69f, 0.14f), P(0.50f, 0.14f));
                body.CloseFigure();
                g.FillPath(b, body);
                g.FillPie(b, r.X + 0.40f * r.Width, r.Y + 0.70f * r.Height, 0.20f * r.Width, 0.18f * r.Height, 0, 180);
                g.FillEllipse(b, r.X + 0.45f * r.Width, r.Y + 0.07f * r.Height, 0.10f * r.Width, 0.10f * r.Height);
            }
            g.Restore(state);

            if (slashCut != null)
            {
                slashCut.Dispose();
                using (var pen = new Pen(color, stroke))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, s1, s2);
                }
            }
        }

        // App logo: blue rounded tile with a muted bell.
        public static void DrawLogo(Graphics g, RectangleF r)
        {
            float radius = r.Width * 0.23f;
            using (var path = RoundRect(r, radius))
            using (var grad = new LinearGradientBrush(r, Color.FromArgb(40, 150, 255), Color.FromArgb(0, 84, 200), 45f))
            {
                g.FillPath(grad, path);
                if (r.Width >= 32)
                {
                    // soft top highlight
                    RectangleF top = new RectangleF(r.X, r.Y, r.Width, r.Height * 0.5f);
                    using (var hl = new LinearGradientBrush(new RectangleF(top.X, top.Y - 1, top.Width, top.Height + 2),
                               Color.FromArgb(50, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    {
                        GraphicsState st = g.Save();
                        g.SetClip(path);
                        g.FillRectangle(hl, top);
                        g.Restore(st);
                    }
                }
            }
            float inset = r.Width * (r.Width <= 20 ? 0.12f : 0.17f);
            DrawBell(g, RectangleF.Inflate(r, -inset, -inset), Color.White, true);
        }
    }
}

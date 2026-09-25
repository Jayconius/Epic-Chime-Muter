// Build-time helper: renders the app icon and the README graphics using the app's own drawing code.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;

namespace EpicChimeMuter
{
    static class MakeAssets
    {
        static readonly string[] ChimeNames =
        {
            "PartyJoinabilityChanged.wav", "PartyMessageReceived.wav", "PartyUserJoined.wav",
            "PartyUserLeft.wav", "PushToTalkActivated.wav", "PushToTalkDeactivated.wav",
        };
        const string DemoFolder = @"C:\Program Files\Epic Games\Launcher\Portal\Extras\SocialChimes";

        [STAThread]
        static int Main(string[] args)
        {
            string root = args.Length > 0 ? args[0] : ".";
            string assets = Path.Combine(root, "assets");
            string docs = Path.Combine(root, "docs");
            Directory.CreateDirectory(assets);
            Directory.CreateDirectory(docs);

            WriteIcon(Path.Combine(assets, "icon.ico"));
            using (Bitmap logo = RenderLogo(256)) logo.Save(Path.Combine(docs, "logo.png"), ImageFormat.Png);

            bool[] none = { false, false, false, false, false, false }, all = { true, true, true, true, true, true };
            // Custom mix: party sounds muted, push-to-talk sounds kept.
            bool[] custom = { true, true, true, true, false, false };
            Screenshot(Path.Combine(docs, "screenshot-on.png"), Demo(none, none), false);
            Screenshot(Path.Combine(docs, "screenshot-muted.png"), Demo(all, all), false);
            Screenshot(Path.Combine(docs, "screenshot-custom.png"), Demo(custom, custom), false);
            // An Epic update put two muted chimes back.
            Screenshot(Path.Combine(docs, "screenshot-mixed.png"), Demo(new[] { true, false, true, false, true, true }, all), false);
            Screenshot(Path.Combine(docs, "screenshot-settings.png"), Demo(all, all), true);

            Banner(Path.Combine(docs, "banner.png"));
            HowItWorks(Path.Combine(docs, "how-it-works.png"));
            Console.WriteLine("Assets written.");
            return 0;
        }

        // muted: what's on disk. wanted: what the user asked to be muted.
        static ChimeManager Demo(bool[] muted, bool[] wanted)
        {
            var files = new List<ChimeFile>();
            var wantedNames = new List<string>();
            for (int i = 0; i < ChimeNames.Length; i++)
            {
                files.Add(new ChimeFile(ChimeNames[i], ChimeNames[i] + (muted[i] ? ".off" : ""), muted[i]));
                if (wanted[i]) wantedNames.Add(ChimeNames[i]);
            }
            return new ChimeManager(DemoFolder, files, "Default location", wantedNames);
        }

        static Bitmap RenderLogo(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                Art.DrawLogo(g, new RectangleF(0, 0, size, size));
            }
            return bmp;
        }

        // Multi-resolution .ico with PNG-compressed frames.
        static void WriteIcon(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var frames = new List<byte[]>();
            foreach (int s in sizes)
                using (Bitmap b = RenderLogo(s))
                using (var ms = new MemoryStream())
                {
                    b.Save(ms, ImageFormat.Png);
                    frames.Add(ms.ToArray());
                }

            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0);
                w.Write((short)1);
                w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((short)1);
                    w.Write((short)32);
                    w.Write(frames[i].Length);
                    w.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (byte[] f in frames) w.Write(f);
            }
        }

        // App window rendered at 2x with a soft drop shadow on a transparent background.
        static void Screenshot(string path, ChimeManager mgr, bool settings)
        {
            const float s = 2f;
            const int pad = 48;
            using (var form = new MainForm(mgr))
            {
                form.SettingsOpen = settings;
                int w = (int)(MainForm.DesignWidth * s), h = (int)(form.DesignHeight * s);
                using (var app = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                using (var outBmp = new Bitmap(w + pad * 2, h + pad * 2, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(app)) form.RenderTo(g, s);
                    using (Graphics g = Graphics.FromImage(outBmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);
                        for (int i = 24; i >= 1; i--)
                        {
                            var r = new RectangleF(pad - i, pad - i + 10, w + i * 2, h + i * 2);
                            Art.FillRound(g, r, 8 + i, Color.FromArgb(3, 0, 0, 0));
                        }
                        g.DrawImage(app, pad, pad, w, h);
                    }
                    outBmp.Save(path, ImageFormat.Png);
                }
            }
        }

        static Graphics Prep(Bitmap bmp)
        {
            Graphics g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            return g;
        }

        static void Banner(string path)
        {
            const int W = 1280, H = 400;
            using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            using (Graphics g = Prep(bmp))
            {
                using (var path0 = Art.RoundRect(new RectangleF(0, 0, W, H), 28))
                using (var bg = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(14, 14, 19), Color.FromArgb(10, 28, 58), 20f))
                {
                    g.Clear(Color.Transparent);
                    g.FillPath(bg, path0);
                    g.SetClip(path0);
                }

                // glow behind logo
                using (var gp = new GraphicsPath())
                {
                    gp.AddEllipse(-60, -40, 560, 480);
                    using (var pb = new PathGradientBrush(gp))
                    {
                        pb.CenterColor = Color.FromArgb(70, Art.Accent);
                        pb.SurroundColors = new[] { Color.FromArgb(0, Art.Accent) };
                        g.FillPath(pb, gp);
                    }
                }

                // decorative sound wave that flattens out (the chimes going quiet)
                using (var pen = new Pen(Color.FromArgb(40, Art.AccentLight), 6f))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    var rnd = new Random(7);
                    for (int i = 0; i < 15; i++)
                    {
                        float x = 1062 + i * 14;
                        float amp = (float)(Math.Max(0.0, 1.0 - i / 11.0) * (30 + rnd.Next(0, 80)));
                        amp = Math.Max(amp, 3f);
                        g.DrawLine(pen, x, 200 - amp, x, 200 + amp);
                    }
                }

                using (Bitmap logo = RenderLogo(180)) g.DrawImage(logo, 92, 110, 180, 180);

                using (var fTitle = new Font("Segoe UI Semibold", 76f, GraphicsUnit.Pixel))
                using (var fTag = new Font("Segoe UI", 28f, GraphicsUnit.Pixel))
                using (var fPill = new Font("Segoe UI Semibold", 18f, GraphicsUnit.Pixel))
                using (var white = new SolidBrush(Color.White))
                using (var sub = new SolidBrush(Color.FromArgb(185, 190, 205)))
                {
                    g.DrawString("Epic Chime Muter", fTitle, white, 310, 102);
                    g.DrawString("Silence the Epic Games Launcher's party & friend chimes.\nOne click. Fully reversible.",
                        fTag, sub, new RectangleF(316, 200, 900, 90));

                    float x = 318;
                    foreach (string p in new[] { "Windows 10 / 11", "No install", "Open source" })
                    {
                        SizeF sz = g.MeasureString(p, fPill);
                        var r = new RectangleF(x, 298, sz.Width + 26, 38);
                        Art.FillRound(g, r, 19, Color.FromArgb(40, Art.AccentLight));
                        using (var tb = new SolidBrush(Color.FromArgb(150, 200, 255)))
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            g.DrawString(p, fPill, tb, r, sf);
                        x = r.Right + 12;
                    }
                }
                bmp.Save(path, ImageFormat.Png);
            }
        }

        static void HowItWorks(string path)
        {
            const int W = 1280, H = 470;
            using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            using (Graphics g = Prep(bmp))
            using (var fHead = new Font("Segoe UI Semibold", 26f, GraphicsUnit.Pixel))
            using (var fFile = new Font("Consolas", 21f, GraphicsUnit.Pixel))
            using (var fCap = new Font("Segoe UI", 20f, GraphicsUnit.Pixel))
            {
                g.Clear(Color.Transparent);
                Art.FillRound(g, new RectangleF(0, 0, W, H), 28, Color.FromArgb(16, 16, 20));

                DrawFileCard(g, new RectangleF(50, 40, 500, 390), "Chimes ON", Art.Amber, false, fHead, fFile, fCap);
                DrawFileCard(g, new RectangleF(730, 40, 500, 390), "Chimes MUTED", Art.AccentLight, true, fHead, fFile, fCap);

                // arrows
                using (var pen = new Pen(Art.AccentLight, 5f))
                {
                    pen.EndCap = LineCap.ArrowAnchor;
                    g.DrawLine(pen, 590, 200, 690, 200);
                }
                using (var pen = new Pen(Art.Amber, 5f))
                {
                    pen.EndCap = LineCap.ArrowAnchor;
                    g.DrawLine(pen, 690, 270, 590, 270);
                }
                using (var sub = new SolidBrush(Art.SubText))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                {
                    g.DrawString("mute", fCap, sub, new RectangleF(560, 160, 160, 30), sf);
                    g.DrawString("unmute", fCap, sub, new RectangleF(560, 280, 160, 30), sf);
                }
                bmp.Save(path, ImageFormat.Png);
            }
        }

        static void DrawFileCard(Graphics g, RectangleF r, string title, Color accent, bool muted, Font fHead, Font fFile, Font fCap)
        {
            Art.FillRound(g, r, 18, Art.Surface);
            Art.StrokeRound(g, r, 18, Art.Border);
            Art.DrawBell(g, new RectangleF(r.X + 28, r.Y + 24, 34, 34), accent, muted);
            using (var b = new SolidBrush(accent)) g.DrawString(title, fHead, b, r.X + 72, r.Y + 24);

            using (var name = new SolidBrush(muted ? Art.SubText : Art.Text))
            using (var ext = new SolidBrush(accent))
            {
                for (int i = 0; i < ChimeNames.Length; i++)
                {
                    float y = r.Y + 88 + i * 46;
                    string n = ChimeNames[i];
                    g.DrawString(n, fFile, name, r.X + 30, y);
                    if (muted)
                    {
                        SizeF sz = g.MeasureString(n, fFile, PointF.Empty, StringFormat.GenericTypographic);
                        g.DrawString(".off", fFile, ext, r.X + 30 + sz.Width + 3, y);
                    }
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace EpicChimeMuter
{
    public class MainForm : Form
    {
        public const float DesignWidth = 440f;
        const float TitleH = 40f;
        const float CardTop = 272f;
        const float RowsTop = 312f;
        const float RowH = 32f;

        readonly ChimeManager mgr;
        readonly List<KeyValuePair<string, RectangleF>> hits = new List<KeyValuePair<string, RectangleF>>();
        readonly Font fTitle, fHead, fSub, fBody, fSmall, fLabel, fBadge;
        readonly Timer anim, toastTimer;
        float scale = 1f;
        float knob, knobTarget;
        string hover;
        string toast;
        bool shownOnce;
        SoundPlayer player;

        [DllImport("user32.dll")]
        static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public MainForm(ChimeManager manager)
        {
            mgr = manager;
            Text = "Epic Chime Muter";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Art.Bg;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            fTitle = new Font("Segoe UI Semibold", 13f, GraphicsUnit.Pixel);
            fHead = new Font("Segoe UI Semibold", 25f, GraphicsUnit.Pixel);
            fSub = new Font("Segoe UI", 13.5f, GraphicsUnit.Pixel);
            fBody = new Font("Segoe UI", 13.5f, GraphicsUnit.Pixel);
            fSmall = new Font("Segoe UI", 12f, GraphicsUnit.Pixel);
            fLabel = new Font("Segoe UI Semibold", 11f, GraphicsUnit.Pixel);
            fBadge = new Font("Segoe UI Semibold", 10.5f, GraphicsUnit.Pixel);

            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            anim = new Timer();
            anim.Interval = 15;
            anim.Tick += OnAnimTick;
            toastTimer = new Timer();
            toastTimer.Interval = 2600;
            toastTimer.Tick += delegate { toastTimer.Stop(); toast = null; Invalidate(); };

            mgr.Scan();
            knob = knobTarget = mgr.State == ChimeState.AllMuted ? 1f : 0f;
            UpdateSize();
        }

        public float DesignHeight { get { return CardBottom + 56f; } }

        float CardBottom { get { return RowsTop + Math.Max(6, mgr.Files.Count) * RowH + 12f; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x20000;       // WS_MINIMIZEBOX: allow minimize/restore from the taskbar
                cp.ClassStyle |= 0x20000;  // CS_DROPSHADOW
                return cp;
            }
        }

        void UpdateSize()
        {
            ClientSize = new Size((int)Math.Ceiling(DesignWidth * scale), (int)Math.Ceiling(DesignHeight * scale));
        }

        // ------------------------------------------------------------------ actions

        void Rescan()
        {
            mgr.Scan();
            knobTarget = mgr.State == ChimeState.AllMuted ? 1f : 0f;
            if (knob != knobTarget) anim.Start();
            UpdateSize();
            Invalidate();
        }

        void Toggle()
        {
            ChimeState st = mgr.State;
            if (st == ChimeState.NotFound || st == ChimeState.Empty) return;
            try
            {
                if (st == ChimeState.AllMuted)
                {
                    int n = mgr.UnmuteAll();
                    ShowToast("Chimes restored  â€¢  " + n + " file" + (n == 1 ? "" : "s"));
                }
                else
                {
                    int n = mgr.MuteAll();
                    ShowToast("Chimes muted  â€¢  " + n + " file" + (n == 1 ? "" : "s") + " renamed");
                }
            }
            catch (Exception ex)
            {
                Rescan();
                MessageBox.Show(this,
                    "Some chime files couldn't be renamed:\n\n" + ex.Message +
                    "\n\nTry closing the Epic Games Launcher and toggling again.",
                    "Epic Chime Muter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Rescan();
        }

        void Preview(int index)
        {
            if (index < 0 || index >= mgr.Files.Count) return;
            ChimeFile f = mgr.Files[index];
            try
            {
                if (player != null) { player.Stop(); player.Dispose(); }
                player = new SoundPlayer(new MemoryStream(File.ReadAllBytes(f.FullPath)));
                player.Play();
            }
            catch (Exception ex)
            {
                ShowToast("Couldn't play " + f.Name + ": " + ex.Message);
            }
        }

        void Browse()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select Epic's SocialChimes folder (usually Epic Games\\Launcher\\Portal\\Extras\\SocialChimes)";
                dlg.ShowNewFolderButton = false;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                mgr.SetFolder(dlg.SelectedPath);
                Rescan();
                if (mgr.State == ChimeState.Empty)
                    ShowToast("No .wav chime files in that folder");
            }
        }

        void OpenFolder()
        {
            if (mgr.Folder == null) return;
            try { Process.Start("explorer.exe", "\"" + mgr.Folder + "\""); }
            catch { }
        }

        void ShowToast(string message)
        {
            toast = message;
            toastTimer.Stop();
            toastTimer.Start();
            Invalidate();
        }

        void OnAnimTick(object sender, EventArgs e)
        {
            float d = knobTarget - knob;
            if (Math.Abs(d) < 0.01f)
            {
                knob = knobTarget;
                anim.Stop();
            }
            else
            {
                knob += d * 0.22f;
            }
            Invalidate();
        }

        // ------------------------------------------------------------------ input

        string HitTest(Point p)
        {
            var q = new PointF(p.X / scale, p.Y / scale);
            for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Value.Contains(q)) return hits[i].Key;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string h = HitTest(e.Location);
            if (h != hover)
            {
                hover = h;
                Cursor = h != null ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != null) { hover = null; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y / scale < TitleH && HitTest(e.Location) == null)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            string id = HitTest(e.Location);
            if (id == null) return;
            if (id == "close") Close();
            else if (id == "min") WindowState = FormWindowState.Minimized;
            else if (id == "toggle") Toggle();
            else if (id == "browse") Browse();
            else if (id == "open") OpenFolder();
            else if (id == "refresh") { Rescan(); ShowToast("Refreshed"); }
            else if (id.StartsWith("row:")) Preview(int.Parse(id.Substring(4)));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) Toggle();
            else if (e.KeyCode == Keys.Escape) Close();
            else if (e.KeyCode == Keys.F5) Rescan();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            // Re-check when the window regains focus, in case Epic restored the files meanwhile.
            if (shownOnce) Rescan();
            shownOnce = true;
        }

        // ------------------------------------------------------------------ painting

        protected override void OnPaint(PaintEventArgs e)
        {
            RenderTo(e.Graphics, scale);
        }

        public void RenderTo(Graphics g, float s)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Art.Bg);
            g.ScaleTransform(s, s);
            hits.Clear();

            ChimeState st = mgr.State;
            float H = DesignHeight;

            DrawGlow(g, st);
            DrawTitleBar(g);
            DrawToggle(g, st);
            DrawStatus(g, st);
            DrawCard(g, st);
            DrawFooter(g, H);
            DrawToast(g, H);

            using (var p = new Pen(Art.Border, 1f))
                g.DrawRectangle(p, 0.5f, 0.5f, DesignWidth - 1f, H - 1f);
        }

        void DrawGlow(Graphics g, ChimeState st)
        {
            Color glow = (st == ChimeState.NotFound || st == ChimeState.Empty)
                ? Art.DimText
                : Art.Lerp(Art.Amber, Art.Accent, knob);
            using (var gp = new GraphicsPath())
            {
                gp.AddEllipse(DesignWidth / 2f - 210f, 20f, 420f, 250f);
                using (var pb = new PathGradientBrush(gp))
                {
                    pb.CenterColor = Art.Alpha(glow, 50);
                    pb.SurroundColors = new[] { Art.Alpha(glow, 0) };
                    g.FillPath(pb, gp);
                }
            }
        }

        void DrawTitleBar(Graphics g)
        {
            Art.DrawLogo(g, new RectangleF(14f, 11f, 18f, 18f));
            using (var b = new SolidBrush(Art.SubText))
                g.DrawString("Epic Chime Muter", fTitle, b, 38f, 11f);

            var rMin = new RectangleF(DesignWidth - 92f, 1f, 46f, TitleH - 6f);
            var rClose = new RectangleF(DesignWidth - 46f, 1f, 45f, TitleH - 6f);
            if (hover == "min") Art.FillRound(g, rMin, 0f, Art.SurfaceHover);
            if (hover == "close") Art.FillRound(g, rClose, 0f, Art.Danger);

            using (var p = new Pen(hover == "min" ? Art.Text : Art.SubText, 1.2f))
            {
                float cx = rMin.X + rMin.Width / 2f, cy = rMin.Y + rMin.Height / 2f;
                g.DrawLine(p, cx - 5f, cy, cx + 5f, cy);
            }
            using (var p = new Pen(hover == "close" ? Color.White : Art.SubText, 1.2f))
            {
                float cx = rClose.X + rClose.Width / 2f, cy = rClose.Y + rClose.Height / 2f;
                g.DrawLine(p, cx - 5f, cy - 5f, cx + 5f, cy + 5f);
                g.DrawLine(p, cx - 5f, cy + 5f, cx + 5f, cy - 5f);
            }
            AddHit("min", rMin);
            AddHit("close", rClose);
        }

        void DrawToggle(Graphics g, ChimeState st)
        {
            bool enabled = st != ChimeState.NotFound && st != ChimeState.Empty;
            bool hot = enabled && hover == "toggle";
            var tr = new RectangleF(DesignWidth / 2f - 80f, 84f, 160f, 76f);

            Color track;
            if (!enabled) track = Color.FromArgb(36, 36, 44);
            else track = Art.Lerp(hot ? Art.TrackOffHover : Art.TrackOff, hot ? Art.AccentLight : Art.Accent, knob);

            // outer ring
            Art.FillRound(g, RectangleF.Inflate(tr, 5f, 5f), 43f, Art.Alpha(track, 45));
            Art.FillRound(g, tr, 38f, track);

            // side labels inside the track
            using (var b = new SolidBrush(Art.Alpha(Color.White, enabled ? (int)(200 * knob) : 0)))
                DrawCentered(g, "MUTED", fLabel, b, new RectangleF(tr.X + 8f, tr.Y, 70f, tr.Height));
            using (var b = new SolidBrush(Art.Alpha(Art.SubText, enabled ? (int)(220 * (1f - knob)) : 0)))
                DrawCentered(g, "ON", fLabel, b, new RectangleF(tr.Right - 78f, tr.Y, 70f, tr.Height));

            const float kd = 64f;
            float kx = tr.X + 6f + knob * (tr.Width - 12f - kd);
            var kr = new RectangleF(kx, tr.Y + 6f, kd, kd);
            using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                g.FillEllipse(shadow, kr.X, kr.Y + 2.5f, kd, kd);
            using (var kb = new SolidBrush(enabled ? Color.White : Color.FromArgb(90, 90, 100)))
                g.FillEllipse(kb, kr);

            Color icon = enabled ? Art.Lerp(Color.FromArgb(84, 84, 98), Art.Accent, knob) : Color.FromArgb(60, 60, 70);
            Art.DrawBell(g, RectangleF.Inflate(kr, -15f, -15f), icon, knob > 0.5f);

            if (enabled) AddHit("toggle", RectangleF.Inflate(tr, 5f, 5f));
        }

        void DrawStatus(Graphics g, ChimeState st)
        {
            string head, sub;
            Color headColor = Art.Text;
            int muted = mgr.MutedCount, total = mgr.Files.Count;
            switch (st)
            {
                case ChimeState.AllMuted:
                    head = "Chimes are muted";
                    sub = "Epic's party & friend sounds won't play.\nFlip the switch any time to bring them back.";
                    break;
                case ChimeState.AllOn:
                    head = "Chimes are on";
                    sub = "Flip the switch to silence the Epic Games\nLauncher's party & friend sounds.";
                    break;
                case ChimeState.Mixed:
                    head = "Some chimes are back";
                    headColor = Art.Amber;
                    sub = (total - muted) + " of " + total + " sounds are active again, probably from an\nEpic update. Flip the switch to mute them all.";
                    break;
                case ChimeState.Empty:
                    head = "No chime files found";
                    sub = "The SocialChimes folder has no .wav files.\nEpic may have moved or removed them.";
                    break;
                default:
                    head = "Epic Launcher not found";
                    headColor = Art.SubText;
                    sub = "Couldn't find the SocialChimes folder.\nUse Browse below to pick it manually.";
                    break;
            }
            using (var b = new SolidBrush(headColor))
                DrawCentered(g, head, fHead, b, new RectangleF(20f, 176f, DesignWidth - 40f, 36f));
            using (var b = new SolidBrush(Art.SubText))
            using (var sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                g.DrawString(sub, fSub, b, new RectangleF(24f, 214f, DesignWidth - 48f, 44f), sf);
            }
        }

        void DrawCard(Graphics g, ChimeState st)
        {
            var card = new RectangleF(20f, CardTop, DesignWidth - 40f, CardBottom - CardTop);
            Art.FillRound(g, card, 12f, Art.Surface);
            Art.StrokeRound(g, card, 12f, Art.Border);

            using (var b = new SolidBrush(Art.DimText))
            {
                g.DrawString("SOUND FILES", fLabel, b, card.X + 16f, card.Y + 13f);
                if (mgr.Files.Count > 0)
                {
                    using (var sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Far;
                        g.DrawString("click to preview", fSmall, b, new RectangleF(card.X, card.Y + 12f, card.Width - 16f, 20f), sf);
                    }
                }
            }

            if (mgr.Files.Count == 0)
            {
                Art.DrawBell(g, new RectangleF(DesignWidth / 2f - 26f, card.Y + 58f, 52f, 52f), Art.Border, true);
                using (var b = new SolidBrush(Art.SubText))
                    DrawCentered(g, st == ChimeState.NotFound
                        ? "Looked in Program Files and Program Files (x86)."
                        : "Nothing to mute in this folder.", fSmall, b,
                        new RectangleF(card.X, card.Y + 122f, card.Width, 22f));

                var btn = new RectangleF(DesignWidth / 2f - 70f, card.Y + 158f, 140f, 36f);
                Art.FillRound(g, btn, 18f, hover == "browse" ? Art.AccentLight : Art.Accent);
                using (var b = new SolidBrush(Color.White))
                    DrawCentered(g, "Browseâ€¦", fTitle, b, btn);
                AddHit("browse", btn);
                return;
            }

            for (int i = 0; i < mgr.Files.Count; i++)
            {
                ChimeFile f = mgr.Files[i];
                string id = "row:" + i;
                bool hot = hover == id;
                var row = new RectangleF(card.X + 8f, RowsTop + i * RowH, card.Width - 16f, RowH - 2f);
                if (hot) Art.FillRound(g, row, 8f, Art.SurfaceHover);

                // play button
                var pc = new RectangleF(row.X + 8f, row.Y + 4f, 22f, 22f);
                using (var b = new SolidBrush(hot ? Art.Accent : Art.Alpha(Color.White, 20)))
                    g.FillEllipse(b, pc);
                using (var b = new SolidBrush(hot ? Color.White : Art.SubText))
                {
                    float cx = pc.X + pc.Width / 2f + 1f, cy = pc.Y + pc.Height / 2f;
                    g.FillPolygon(b, new[] { new PointF(cx - 3.5f, cy - 5f), new PointF(cx - 3.5f, cy + 5f), new PointF(cx + 5f, cy) });
                }

                using (var b = new SolidBrush(f.Muted ? Art.SubText : Art.Text))
                    g.DrawString(f.FriendlyName, fBody, b, row.X + 40f, row.Y + 5f);

                string badge = f.Muted ? "MUTED" : "ACTIVE";
                Color bc = f.Muted ? Art.AccentLight : Art.Amber;
                SizeF bs = g.MeasureString(badge, fBadge);
                var br = new RectangleF(row.Right - 10f - (bs.Width + 12f), row.Y + 5f, bs.Width + 12f, 20f);
                Art.FillRound(g, br, 10f, Art.Alpha(bc, 38));
                using (var b = new SolidBrush(bc))
                    DrawCentered(g, badge, fBadge, b, br);

                AddHit(id, row);
            }
        }

        void DrawFooter(Graphics g, float H)
        {
            float y = H - 38f;
            float x = DesignWidth - 22f;

            string[] ids = { "refresh", "open" };
            string[] labels = { "Refresh", "Open folder" };
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == "open" && mgr.Folder == null) continue;
                SizeF sz = g.MeasureString(labels[i], fSmall);
                var r = new RectangleF(x - sz.Width, y, sz.Width, 20f);
                using (var b = new SolidBrush(hover == ids[i] ? Art.AccentLight : Art.SubText))
                    g.DrawString(labels[i], fSmall, b, r.X, r.Y + 1f);
                AddHit(ids[i], RectangleF.Inflate(r, 4f, 4f));
                x = r.X - 14f;
            }

            using (var b = new SolidBrush(Art.DimText))
                g.DrawString(ShortenPath(g, mgr.Folder ?? "Folder not found", x - 30f), fSmall, b, 22f, y + 1f);
        }

        // Drops leading folders ("...\Extras\SocialChimes") until the path fits.
        string ShortenPath(Graphics g, string path, float maxWidth)
        {
            if (g.MeasureString(path, fSmall).Width <= maxWidth) return path;
            string[] parts = path.Split('\\');
            for (int skip = 1; skip < parts.Length; skip++)
            {
                string s = "…\\" + string.Join("\\", parts, skip, parts.Length - skip);
                if (g.MeasureString(s, fSmall).Width <= maxWidth) return s;
            }
            return parts[parts.Length - 1];
        }

        void DrawToast(Graphics g, float H)
        {
            if (toast == null) return;
            SizeF sz = g.MeasureString(toast, fSmall);
            float w = Math.Min(DesignWidth - 40f, sz.Width + 32f);
            var r = new RectangleF(DesignWidth / 2f - w / 2f, H - 44f, w, 30f);
            Art.FillRound(g, RectangleF.Inflate(r, 0f, 0f), 15f, Color.FromArgb(50, 50, 62));
            Art.StrokeRound(g, r, 15f, Color.FromArgb(72, 72, 88));
            using (var b = new SolidBrush(Art.Text))
                DrawCentered(g, toast, fSmall, b, r);
        }

        void DrawCentered(Graphics g, string text, Font font, Brush brush, RectangleF r)
        {
            using (var sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                g.DrawString(text, font, brush, r, sf);
            }
        }

        void AddHit(string id, RectangleF r)
        {
            hits.Add(new KeyValuePair<string, RectangleF>(id, r));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                anim.Dispose();
                toastTimer.Dispose();
                if (player != null) player.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

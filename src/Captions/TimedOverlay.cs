using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BilibiliPotPlayer
{
    internal static class DanmakuSettings
    {
        internal static readonly string[] Fonts = { "SimHei", "SimSun", "NSimSun", "FangSong", "Microsoft YaHei", "Microsoft YaHei UI", "Noto Sans CJK SC" };
        internal static readonly int[] Regions = { 10, 25, 50, 75, 100 }, Speeds = { 40, 70, 100, 130, 160 };
        internal static int Snap(int value, int[] steps)
        { int index = 0; for (int i = 1; i < steps.Length; i++) if (Math.Abs((long)value - steps[i]) < Math.Abs((long)value - steps[index])) index = i; return steps[index]; }
        internal static int Index(int value, int[] steps) { return Array.IndexOf(steps, Snap(value, steps)); }
        internal static int At(float ratio, int[] steps)
        { return steps[Math.Max(0, Math.Min(steps.Length - 1, (int)Math.Floor(ratio * (steps.Length - 1) + 0.5f)))]; }
        internal static Font FontFor(float size, CaptionOptions options) { return TextFont(size, options, true); }
        internal static Font TextFont(float size, CaptionOptions options, bool danmaku)
        {
            using (Font regular = new Font(danmaku ? options.DanmakuFont : options.SubtitleFont, size, GraphicsUnit.Pixel))
            {
                FontStyle style = FontStyle.Regular;
                if ((danmaku ? options.DanmakuBold : options.SubtitleBold) && regular.FontFamily.IsStyleAvailable(FontStyle.Bold)) style |= FontStyle.Bold;
                if ((danmaku ? options.DanmakuItalic : options.SubtitleItalic) && regular.FontFamily.IsStyleAvailable(FontStyle.Italic)) style |= FontStyle.Italic;
                if (!regular.FontFamily.IsStyleAvailable(style)) style = FontStyle.Regular;
                return new Font(regular.FontFamily, size, style, GraphicsUnit.Pixel);
            }
        }
        internal static int ColorFor(TextCue cue, CaptionOptions options) { return options.UseDanmakuColor ? options.DanmakuUniformColor ? options.DanmakuTextColor : cue.Color : 0xFFFFFF; }
        internal static float ScreenScale(int height,CaptionOptions options,float dpi)
        {
            if(!options.ScaleWithScreen)return 1;
            float logical=height/dpi;
            float size=logical<=314?16:logical<=499?.0385f*logical+3.29f:logical<=608?.041f*logical:
                logical<=700?.022f*logical+12:logical<=816?27:logical<=980?.0365f*logical-2.5f:
                logical<=1300?.022f*logical+18.5f:40;
            return size/25;
        }
        internal static float SizeFor(TextCue cue,int height,CaptionOptions options,float dpi)
        { return Math.Max(8*dpi,Math.Min(512,(float)(25*dpi*cue.Size/Math.Max(1,cue.DanmakuBaseSize))*ScreenScale(height,options,dpi)*options.Size/100f)); }

    }
    internal sealed class DanmakuPlacement
    {
        internal TextCue Cue;
        internal float X, Y, Size, Width, Height, FromX, ToX;
        internal double Start, Life;
        internal bool Timed;
        internal CaptionOptions Appearance;
        internal int CanvasWidth, CanvasHeight, ClipHeight, Group;
        internal float DpiScale = 1;
    }
    internal static class DanmakuLayout
    {
        internal static bool UsesLayout(TextSource source)
        { return source.Category == TextCategory.Danmaku && source.Enabled && !source.Bitmap && !source.IntrinsicAdvanced && !source.ProtocolAdvanced; }
        internal static void Collect(CaptionCatalog catalog, List<TextCue> timeline)
        {
            timeline.Clear();
            foreach (TextSource source in catalog.Sources) if (UsesLayout(source)) {
                double baseline=25;
                if(source.AssRenderable) {
                    var counts=new Dictionary<int,int>(); int most=0;
                    foreach(TextCue cue in source.Cues) if(cue.Size>0) { int count; counts.TryGetValue(cue.Size,out count); counts[cue.Size]=++count; if(count>most) {most=count;baseline=cue.Size;} }
                }
                foreach (TextCue cue in source.Cues) if (cue.Mode < 7 && (cue.Effects & (CueEffects.Animation | CueEffects.Drawing | CueEffects.Protocol)) == 0) { cue.DanmakuBaseSize=baseline; timeline.Add(cue); }
            }
            TimedText.Sort(timeline);
        }
        internal static int AreaHeight(int height, CaptionOptions options, Rectangle? pictureBounds=null, bool subtitlesPresent=true)
        {
            // Subtitle avoidance is a temporary compositing mask, never a smaller
            // admission area or a change to existing comment trajectories.
            return Math.Max(0,Math.Min(height,height*options.Region/100));
        }
        internal static int LowerBound(List<TextCue> cues, double time)
        { int low = 0, high = cues.Count; while (low < high) { int middle = (low + high) / 2; if (cues[middle].Start < time) low = middle + 1; else high = middle; } return low; }
        internal static List<TextCue> Active(List<TextCue> cues, double position)
        {
            List<TextCue> active = new List<TextCue>();
            int end = LowerBound(cues, position + 0.00001);
            // Subtitle cues may be much longer than danmaku; don't assume a short lookback.
            for (int i = end - 1; i >= 0; i--) if (cues[i].End > position) { active.Add(cues[i]); if (active.Count == 4) break; }
            active.Reverse(); return active;
        }
    }
    internal sealed class TimedOverlay : LayeredSurface
    {
        readonly CaptionCatalog catalog;
        readonly List<TextCue> timeline = new List<TextCue>();
        readonly PlaybackClock clock = new PlaybackClock();
        readonly Stopwatch wallClock=Stopwatch.StartNew();
        readonly DanmakuPlayback playback = new DanmakuPlayback();
        sealed class Measurement { internal float Size, Width; }
        readonly Dictionary<TextCue, Measurement> measurements = new Dictionary<TextCue, Measurement>();
        string mediaPath;
        int revision = -1;
        long nextFrame, nextSample;
        long paintedTime = -1;
        int paintedRevision = -1;
        int paintedTranslationRevision = -1;
        Bitmap bitmap;
        AssScene scene;
        internal IntPtr MenuWindow;
        internal TimedOverlay(CaptionCatalog catalog) : base("字幕和弹幕画面", true) { this.catalog = catalog; Cursor = Cursors.Default; }
        protected override CreateParams CreateParams
        { get { CreateParams p = base.CreateParams; p.ExStyle |= 0x80020; return p; } }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x84) { message.Result = (IntPtr)(-1); return; }
            base.WndProc(ref message);
        }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        internal void Update(IntPtr player)
        {
            long now=wallClock.ElapsedMilliseconds;
            if (now < nextFrame) return; nextFrame = now + 10;
            if (mediaPath != catalog.MediaPath) { mediaPath = catalog.MediaPath; clock.Reset(); playback.Reset(); nextSample = 0; }
            if (Native.IsIconic(player) || !Native.IsWindowVisible(player)) { Dismiss(); return; }
            IntPtr video = Native.VideoSurface(player); Rectangle bounds = Native.ClientBounds(video);
            if (bounds.IsEmpty || !PlaceInPlayer(player, bounds)) { Dismiss(); return; }
            if (now >= nextSample)
            {
                clock.Sample(command=>{long value;return Native.Get(player,command,out value)?(long?)value:null;},()=>wallClock.ElapsedMilliseconds);
                nextSample=wallClock.ElapsedMilliseconds+30;
            }
            if (!clock.Valid) { Dismiss(); return; }
            now=wallClock.ElapsedMilliseconds;
            long time = (long)Math.Round(clock.Position(now));
            if (catalog.NativeTranslation != null) catalog.NativeTranslation.Update(time / 1000.0, clock.Epoch);
            if (catalog.Translation != null) catalog.Translation.Update(catalog.MediaPath, catalog.Find(catalog.Slots.Main), catalog.Options, time / 1000.0, clock.Epoch);
            int translationRevision = catalog.Translation == null ? 0 : catalog.Translation.Revision;
            Rectangle picture=PictureBounds(new Rectangle(0,0,Width,Height),catalog.DisplayRatio);
            if (revision != catalog.Revision)
            {
                revision = catalog.Revision; DanmakuLayout.Collect(catalog, timeline); playback.SetEnabledCues(timeline); measurements.Clear();
            }
            // PotPlayer raises or recreates its video child when changing fullscreen/renderers.
            // Restore our sibling order even when a paused frame needs no repaint.
            RevealBelowMenus();
            if (Visible && bitmap != null && bitmap.Width == Width && bitmap.Height == Height && paintedTime == time && paintedRevision == catalog.Revision && paintedTranslationRevision == translationRevision) return;
            if (bitmap == null || bitmap.Width != Width || bitmap.Height != Height) { if (bitmap != null) bitmap.Dispose(); bitmap = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), PixelFormat.Format32bppPArgb); measurements.Clear(); }
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent); graphics.SmoothingMode = SmoothingMode.AntiAlias;
                double position = time / 1000.0;
                List<DanmakuPlacement> comments = playback.Frame(timeline, position, Width, Height, catalog.Options, delegate(TextCue cue, float size, CaptionOptions appearance)
                {
                    Measurement measured;
                    if (!measurements.TryGetValue(cue, out measured) || measured.Size != size)
                    {
                        using (Font font = DanmakuSettings.FontFor(size, appearance)) measured = new Measurement { Size = size, Width = graphics.MeasureString(cue.Text.Replace('\n', ' '), font).Width };
                        measurements[cue] = measured;
                    }
                    return measured.Width;
                }, clock.AnimationTime(position), clock.Epoch, clock.Rate, picture,Math.Max(96,Native.GetDpiForWindow(player))/96f,
                    catalog.Find(catalog.Slots.Main)!=null || catalog.Find(catalog.Slots.Secondary)!=null);
                // libass composites all text into the same premultiplied surface.
                // The native renderer is called after releasing GDI's bitmap lock.
                pendingComments = comments;
            }
            if (scene == null) scene = new AssScene();
            scene.Render(bitmap,catalog,time,pendingComments,clock.AnimationTime(time / 1000.0),clock.Epoch,picture,Math.Max(96,Native.GetDpiForWindow(player))/96f);
            if (!Visible) Reveal(false); Present(Handle, bitmap); paintedTime = time; paintedRevision = catalog.Revision; paintedTranslationRevision = translationRevision;
        }
        List<DanmakuPlacement> pendingComments;
        internal static Rectangle PictureBounds(Rectangle viewport,double ratio)
        {
            if(ratio<=0 || Double.IsNaN(ratio) || Double.IsInfinity(ratio)) return viewport;
            int width=viewport.Width, height=(int)Math.Round(width/ratio);
            if(height>viewport.Height) { height=viewport.Height; width=(int)Math.Round(height*ratio); }
            return new Rectangle(viewport.Left+(viewport.Width-width)/2,viewport.Top+(viewport.Height-height)/2,width,height);
        }
        void RevealBelowMenus()
        {
            Reveal(false);
            // Keep the text plane immediately above the video, below all visible interactive
            // surfaces. Raising it to HWND_TOP every frame otherwise covers paused menus.
            if (MenuWindow != IntPtr.Zero && Native.IsWindowVisible(MenuWindow)) Native.SetWindowPos(Handle, MenuWindow, 0, 0, 0, 0, 0x0013);
        }
        [StructLayout(LayoutKind.Sequential)] struct Size2 { internal int Width, Height; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct Blend { internal byte Op, Flags, Alpha, Format; }
        internal static void Present(IntPtr handle, Bitmap image)
        {
            IntPtr screen = GetDC(IntPtr.Zero), memory = CreateCompatibleDC(screen), bitmapHandle = image.GetHbitmap(Color.FromArgb(0)), previous = SelectObject(memory, bitmapHandle);
            try
            {
                Size2 size = new Size2 { Width = image.Width, Height = image.Height }; Native.Point origin = new Native.Point(); Blend blend = new Blend { Alpha = 255, Format = 1 };
                UpdateLayeredWindow(handle, screen, IntPtr.Zero, ref size, memory, ref origin, 0, ref blend, 2);
            }
            finally { SelectObject(memory, previous); DeleteObject(bitmapHandle); DeleteDC(memory); ReleaseDC(IntPtr.Zero, screen); }
        }
        protected override void Dispose(bool disposing) { if (disposing) { if(bitmap != null) bitmap.Dispose(); if(scene != null) scene.Dispose(); } base.Dispose(disposing); }
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr window, IntPtr dst, IntPtr position, ref Size2 size, IntPtr src, ref Native.Point origin, uint key, ref Blend blend, uint flags);
    }
}

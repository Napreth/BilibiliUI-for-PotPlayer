using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace BilibiliPotPlayer
{
    internal static class CaptionPages
    {
        internal static int Count(int items, int size) { return Math.Max(1, (items + size - 1) / size); }
        internal static int Clamp(int page, int items, int size) { return Math.Max(0, Math.Min(page, Count(items, size) - 1)); }
        internal static int First(int page, int items, int size) { return Clamp(page, items, size) * size; }
        internal static int WindowStart(int page, int pages) { return Math.Max(0, Math.Min(pages - 5, page - 3)); }
    }
    internal static class CaptionDropAreas
    {
        internal const int RowHeight = 28, MainHeight = 556;
        internal static int HeightFor(CaptionOptions options) { return MainHeight+(options.Region==100?36:0); }
        internal static RectangleF Bounds(TextCategory category)
        { return new RectangleF(category == TextCategory.Subtitle || category == TextCategory.Uncertain ? 16 : 326, category == TextCategory.Subtitle || category == TextCategory.Danmaku ? 39 : 180, 274, 100); }
        internal static TextCategory? At(float x, float y)
        { foreach (TextCategory category in (TextCategory[])Enum.GetValues(typeof(TextCategory))) if (Bounds(category).Contains(x, y)) return category; return null; }
    }
    internal sealed class SubtitleButton : LayeredSurface
    {
        internal SubtitleButton() : base("字幕和弹幕", true) { BackColor = Color.FromArgb(30, 30, 30); }
        internal void UpdatePlayer(IntPtr player)
        { if (!Native.ControlBarVisible(player) || !PlaceInControlBar(player, Native.SubtitleButtonBounds(player))) { Dismiss(); return; } Reveal(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Font font = new Font("Microsoft YaHei UI", 12 * Height / 40f, GraphicsUnit.Pixel))
                TextRenderer.DrawText(e.Graphics, UiLanguage.Read().Subtitles, font, ClientRectangle, Color.FromArgb(218, 218, 218), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
    }
    internal sealed class SubtitlePopup : LayeredSurface
    {
        sealed class Hit { internal RectangleF Rect; internal Action Action; internal TextSource Source; internal Action<float> Slide; internal string Tooltip; }
        sealed class PlayerOwner : IWin32Window { readonly IntPtr handle; internal PlayerOwner(IntPtr handle) { this.handle = handle; } public IntPtr Handle { get { return handle; } } }
        readonly SubtitleButton button;
        readonly CaptionCatalog catalog;
        readonly List<Hit> hits = new List<Hit>();
        readonly int[] listPages = new int[4];
        readonly ToolTip tip = new ToolTip();
        readonly SvgGlyph[] filterIcons = { new SvgGlyph("BilibiliPotPlayer.Filter.scroll.svg"), new SvgGlyph("BilibiliPotPlayer.Filter.fixed.svg"), new SvgGlyph("BilibiliPotPlayer.Filter.color.svg"), new SvgGlyph("BilibiliPotPlayer.Filter.advanced.svg") };
        long hoverSince, lastInside, nextLanguageRead;
        IntPtr hovering;
        float scale = 1;
        int page, hovered = -1, down = -1;
        PointF start, dragPoint;
        bool dragging, dialog, english, releasingMouse;
        TextSource dragSource;
        Hit slider, pressed;
        int picker;
        readonly Color accent = Color.FromArgb(251, 114, 153), selected = Color.FromArgb(0, 174, 236), muted = Color.FromArgb(129, 131, 140);
        internal SubtitlePopup(SubtitleButton button, CaptionCatalog catalog) : base("字幕和弹幕菜单", true) { this.button = button; this.catalog = catalog; }
        internal bool IsHoverTarget(IntPtr player, Native.Point point)
        { return button.Player == player && button.IsOver(point) || Player == player && IsOver(point); }
        internal void UpdateHover(IntPtr player, long now)
        {
            Native.Point pointer;
            if (dialog) return;
            if (player == IntPtr.Zero || !button.Visible || !Native.GetCursorPos(out pointer)) { Dismiss(); hovering = IntPtr.Zero; return; }
            bool overButton = button.Player == player && button.IsOver(pointer), overPanel = Player == player && IsOver(pointer);
            if (overButton) { if (hovering != player) { hovering = player; hoverSince = now; } lastInside = now; }
            else if (!overPanel) hovering = IntPtr.Zero;
            if (overPanel || Capture) lastInside = now;
            if (Visible && !overButton && !overPanel && !Capture && now - lastInside >= 450) { Dismiss(); page = picker = 0; return; }
            if (!Visible && (!overButton || now - hoverSince < 120)) return;
            Player = player; Rectangle client = Native.ClientBounds(player);
            int logicalHeight = page == 0 ? CaptionDropAreas.HeightFor(catalog.Options) : 656;
            if(slider==null || !Capture) scale = Math.Min(Math.Max(96, Native.GetDpiForWindow(player)) / 96f, Math.Min(client.Width / 620f, Math.Max(1, client.Height - 50) / (float)logicalHeight));
            if (now >= nextLanguageRead) { english = UiLanguage.Read().Subtitles == "Subtitles & danmaku"; nextLanguageRead = now + 1000; }
            int width = (int)(620 * scale), height = (int)(logicalHeight * scale); Rectangle screen = Screen.FromHandle(player).WorkingArea, anchor = button.ScreenBounds;
            Rectangle rect = new Rectangle(Math.Max(screen.Left, Math.Min(screen.Right - width, anchor.Right - width)), Math.Max(screen.Top, anchor.Top - height), width, height);
            rect.X = Math.Max(client.Left, Math.Min(client.Right - width, rect.X)); rect.Y = Math.Max(client.Top, Math.Min(client.Bottom - height, rect.Y));
            if (!PlaceInPlayer(player, rect)) { Dismiss(); return; } Reveal(); Invalidate();
        }
        string L(string chinese, string translated) { return english ? translated : chinese; }
        RectangleF R(float x, float y, float width, float height) { return new RectangleF(x * scale, y * scale, width * scale, height * scale); }
        void Label(Graphics graphics, string text, RectangleF rect, Color color, bool bold = false, float size = 12, bool center = false, bool fileName = false)
        {
            using (Font font = new Font("Microsoft YaHei UI", size * scale, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
            {
                if (fileName) text = CaptionFileNames.Fit(graphics, text, font, Rectangle.Round(rect).Width);
                TextRenderer.DrawText(graphics, text, font, Rectangle.Round(rect), color, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | (center ? TextFormatFlags.HorizontalCenter : 0));
            }
        }
        void Line(Graphics graphics, float x1, float y1, float x2, float y2)
        { using (Pen pen = new Pen(Color.FromArgb(61, 62, 68), scale)) graphics.DrawLine(pen, x1 * scale, y1 * scale, x2 * scale, y2 * scale); }
        void Register(RectangleF rect, Action action, TextSource source = null, Action<float> slide = null,string tooltip=null)
        { hits.Add(new Hit { Rect = rect, Action = action, Source = source, Slide = slide, Tooltip=tooltip }); }
        void Switch(Graphics graphics, float x, float y, bool on, bool enabled = true)
        {
            RectangleF rect = R(x, y + 9, 30, 16);
            using (Brush back = new SolidBrush(on && enabled ? accent : Color.FromArgb(61, 62, 69)))
            using (Brush thumb = new SolidBrush(enabled ? Color.White : muted))
            {
                graphics.FillEllipse(back, rect.X, rect.Y, rect.Height, rect.Height); graphics.FillEllipse(back, rect.Right - rect.Height, rect.Y, rect.Height, rect.Height); graphics.FillRectangle(back, rect.X + rect.Height / 2, rect.Y, rect.Width - rect.Height, rect.Height);
                graphics.FillEllipse(thumb, rect.X + (on ? 17 : 3) * scale, rect.Y + 3 * scale, 10 * scale, 10 * scale);
            }
        }
        void Rows(Graphics graphics, TextCategory category, int list, float x, float y, int count, float width)
        {
            if (dragging && dragSource != null && dragSource.Category != category) { DropArea(graphics, category); return; }
            List<TextSource> sources = catalog.Sources.FindAll(s => s.Category == category);
            listPages[list] = CaptionPages.Clamp(listPages[list], sources.Count, count);
            int first = CaptionPages.First(listPages[list], sources.Count, count);
            if (sources.Count == 0) Label(graphics, catalog.Discovering && category != TextCategory.Advanced ? L("正在读取…", "Loading…") : L("暂无文件", "No files"), R(x + 8, y, width - 16, 34), muted);
            for (int i = first; i < sources.Count && i < first + count; i++)
            {
                TextSource source = sources[i]; float rowY = y + (i - first) * CaptionDropAreas.RowHeight; RectangleF rect = R(x, rowY, width, 27);
                // Keep the source slot empty throughout the drag; its neighbours do not move.
                if (dragging && source == dragSource) continue;
                PaintRow(graphics, source, x, rowY, width, hits.Count == hovered);
                Register(rect, delegate { if (!CaptionCatalog.CanUse(source)) return; if (category == TextCategory.Subtitle) catalog.Select(source); else if (category == TextCategory.Danmaku || category == TextCategory.Advanced) catalog.Toggle(source); }, source);
            }
            int pages = CaptionPages.Count(sources.Count, count), visible = Math.Min(5, pages), firstDot = CaptionPages.WindowStart(listPages[list], pages);
            if (pages <= 1) return;
            bool previous = firstDot > 0, next = firstDot + visible < pages;
            int indicators = visible + (previous ? 1 : 0) + (next ? 1 : 0);
            float left = x + (width - indicators * 22) / 2, centerY = y + count * CaptionDropAreas.RowHeight + 8;
            if (previous) { PageArrow(graphics, list, left, centerY, -1); left += 22; }
            for (int i = 0; i < visible; i++)
            {
                int target = firstDot + i; float centerX = left + i * 22 + 11;
                if (pages > 5 && target == listPages[list]) Label(graphics, (target + 1).ToString(), R(centerX - 11, centerY - 9, 22, 18), Color.White, true, 12, true);
                else using (Brush dot = new SolidBrush(target == listPages[list] ? Color.White : muted)) graphics.FillEllipse(dot, R(centerX - 3, centerY - 3, 6, 6));
                Register(R(centerX - 11, centerY - 9, 22, 18), delegate { listPages[list] = target; Invalidate(); });
            }
            if (next) PageArrow(graphics, list, left + visible * 22, centerY, 1);
        }
        void PageArrow(Graphics graphics, int list, float x, float centerY, int direction)
        {
            RectangleF rect = R(x, centerY - 9, 22, 18);
            Label(graphics, direction < 0 ? "‹" : "›", rect, Color.White, false, 18, true);
            Register(rect, delegate { listPages[list] += direction; Invalidate(); });
        }
        void PaintRow(Graphics graphics, TextSource source, float x, float y, float width, bool hover)
        {
            TextCategory category = source.Category;
            bool main = catalog.Slots.Main == source.Id, secondary = catalog.Slots.Secondary == source.Id;
            bool isSelected = category == TextCategory.Subtitle ? main || secondary : source.Enabled, enabled = CaptionCatalog.CanUse(source);
            if (hover) using (Brush brush = new SolidBrush(Color.FromArgb(43, 45, 51))) graphics.FillRectangle(brush, R(x, y, width, 27));
            if (category == TextCategory.Subtitle)
            {
                if (catalog.Slots.Multi && isSelected) Label(graphics, main ? L("主", "P") : L("副", "S"), R(x + 3, y, 24, 27), selected, true, 13, true);
                else using (Pen circle = new Pen(isSelected ? selected : muted, 1.5f * scale))
                { graphics.DrawEllipse(circle, R(x + 8, y + 8, 12, 12)); if (isSelected) using (Brush dot = new SolidBrush(selected)) graphics.FillEllipse(dot, R(x + 11, y + 11, 6, 6)); }
            }
            else if (category == TextCategory.Danmaku || category == TextCategory.Advanced) Switch(graphics, x + 1, y - 4, source.Enabled, enabled);
            float inset = category == TextCategory.Uncertain ? 8 : 40;
            Label(graphics, source.Title, R(x + inset, y, width - inset - 8, 27), enabled ? isSelected ? selected : Color.White : muted, isSelected, 12, false, source.Stream < 0);
        }
        void DraggedRow(Graphics graphics)
        {
            if (!dragging || dragSource == null || pressed == null) return;
            // Preserve the grab offset rather than snapping the row centre to the cursor.
            RectangleF rect = pressed.Rect; rect.Offset(dragPoint.X - start.X, dragPoint.Y - start.Y);
            using (Brush shadow = new SolidBrush(Color.FromArgb(95, 0, 0, 0))) graphics.FillRectangle(shadow, rect.X + 3 * scale, rect.Y + 3 * scale, rect.Width, rect.Height);
            using (Brush fill = new SolidBrush(Color.FromArgb(48, 50, 57))) graphics.FillRectangle(fill, rect);
            using (Pen border = new Pen(Color.FromArgb(129, 131, 140), scale)) graphics.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
            PaintRow(graphics, dragSource, rect.X / scale, rect.Y / scale, rect.Width / scale, false);
        }
        void DropArea(Graphics graphics, TextCategory category)
        {
            RectangleF logical = CaptionDropAreas.Bounds(category), rect = R(logical.X + 2, logical.Y + 2, logical.Width - 4, logical.Height - 4);
            float diameter = 16 * scale; bool over = logical.Contains(dragPoint.X / scale, dragPoint.Y / scale);
            using (GraphicsPath path = new GraphicsPath())
            using (Pen border = new Pen(over ? Color.White : muted, 1.5f * scale) { DashStyle = DashStyle.Dash })
            {
                path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90); path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
                path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90); path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure();
                graphics.DrawPath(border, path);
            }
            Label(graphics, L("拖动到此处", "Drop here"), rect, Color.White, false, 12, true);
        }
        void Link(Graphics graphics, string text, float x, float y, float width, Action action, bool enabled = true)
        {
            RectangleF rect = R(x, y, width, 28);
            if (hits.Count == hovered && enabled) using (Brush fill = new SolidBrush(Color.FromArgb(43, 45, 51))) graphics.FillRectangle(fill, rect);
            Label(graphics, text, rect, enabled ? Color.White : muted); Register(rect, enabled ? action : null);
        }
        void MainPage(Graphics graphics)
        {
            Line(graphics, 307, 10, 307, CaptionDropAreas.HeightFor(catalog.Options) - 10);
            Label(graphics, L("字幕", "Subtitles"), R(18, 9, 260, 28), Color.White, true, 14); Label(graphics, L("弹幕", "Danmaku"), R(328, 9, 260, 28), Color.White, true, 14);
            Rows(graphics, TextCategory.Subtitle, 0, 16, 39, 3, 274); Rows(graphics, TextCategory.Danmaku, 1, 326, 39, 3, 274);
            Line(graphics, 16, 147, 290, 147); Line(graphics, 326, 147, 603, 147);
            Label(graphics, L("分类存疑（请拖动到对应分类）", "Uncertain · drag to a category"), R(18, 154, 270, 24), Color.White, true, 11);
            Rows(graphics, TextCategory.Uncertain, 2, 16, 180, 3, 274);
            Label(graphics, L("高级弹幕", "Advanced danmaku"), R(328, 154, 270, 24), Color.White, true); Rows(graphics, TextCategory.Advanced, 3, 326, 180, 3, 274);
            Line(graphics, 16, 284, 290, 284); Line(graphics, 326, 284, 603, 284);
            Switch(graphics, 24, 294, catalog.Slots.Multi); Label(graphics, L("多语言字幕", "Bilingual subtitles"), R(66, 294, 210, 34), Color.White); Register(R(20, 293, 266, 35), delegate { catalog.SetMulti(!catalog.Slots.Multi); });
            Link(graphics, L("实时字幕翻译 · 未接入", "Subtitle translation · unavailable"), 66, 328, 222, delegate { }, false);
            Link(graphics, L("在线字幕搜索", "Online subtitle search"), 66, 356, 180, delegate { Dismiss(); Native.SetCommand(Player, 10863); });
            Link(graphics, L("语音生成字幕 · 未接入", "Speech subtitles · unavailable"), 66, 384, 183, delegate { }, false);
            Link(graphics, L("从文件加载字幕/弹幕", "Load subtitles / danmaku"), 66, 412, 190, Import);
            DanmakuMain(graphics);
            Nut(graphics, 267, 446, delegate { page = 1; picker = 0; }); Nut(graphics, 576, 520+(catalog.Options.Region==100?36:0), delegate { page = 2; picker = 0; });
            if (catalog.Status.Length > 0) Label(graphics, catalog.Status, R(18, 480, 274, 56), accent, false, 11);
        }
        void Nut(Graphics graphics, float x, float y, Action action)
        {
            RectangleF hit = R(x, y, 32, 28);
            PointF[] points = new PointF[6];
            for (int i = 0; i < 6; i++) { double angle = (i * 60 - 90) * Math.PI / 180; points[i] = new PointF((x + 16 + (float)Math.Cos(angle) * 7.5f) * scale, (y + 14 + (float)Math.Sin(angle) * 7.5f) * scale); }
            using (Pen pen = new Pen(Color.White, 1.3f * scale)) { graphics.DrawPolygon(pen, points); graphics.DrawEllipse(pen, R(x + 13, y + 11, 6, 6)); }
            Register(hit, action);
        }
        void FilterIcon(Graphics graphics, int index, float x, bool allowed, Action action)
        {
            bool blocked = !allowed; Color color = blocked ? accent : Color.FromArgb(220, 220, 220);
            filterIcons[index].Draw(graphics, (x + 7) * scale, 327 * scale, 28 * scale, color);
            if (blocked)
            {
                RectangleF badge = R(x + 32, 345, 13, 13);
                using (Brush back = new SolidBrush(BackColor)) graphics.FillEllipse(back, R(x + 30, 343, 17, 17));
                using (Pen pen = new Pen(color, 2.2f * scale)) { graphics.DrawEllipse(pen, badge); graphics.DrawLine(pen, (x + 35) * scale, 348 * scale, (x + 42) * scale, 355 * scale); }
            }
            string[] labels = { L("滚动", "Scroll"), L("固定", "Fixed"), L("彩色", "Color"), L("高级", "Advanced") };
            Label(graphics, labels[index], R(x, 358, 54, 22), color, false, 11, true);
            Register(R(x, 324, 54, 57), action);
        }
        void DanmakuMain(Graphics graphics)
        {
            CaptionOptions options = catalog.Options;
            Label(graphics, L("按类型过滤", "Filter by type"), R(330, 294, 268, 27), Color.White, false, 13);
            FilterIcon(graphics, 0, 327, options.Scrolling, delegate { options.Scrolling = !options.Scrolling; Changed(); });
            FilterIcon(graphics, 1, 395, options.Fixed, delegate { options.Fixed = !options.Fixed; Changed(); });
            FilterIcon(graphics, 2, 463, options.Colored, delegate { options.Colored = !options.Colored; Changed(); });
            FilterIcon(graphics, 3, 531, options.AssEffectsEnabled, delegate { options.AssEffectsEnabled = !options.AssEffectsEnabled; Changed(); });
            Checkbox(graphics, L("弹幕随屏幕缩放", "Scale with screen"), 330, 382, 166, options.ScaleWithScreen, delegate { options.ScaleWithScreen = !options.ScaleWithScreen; Changed(); });
            Checkbox(graphics, L("防挡字幕", "Avoid subtitles"), 500, 382, 106, options.AvoidSubtitles, delegate { options.AvoidSubtitles = !options.AvoidSubtitles; Changed(); });
            CompactSlider(graphics, L("显示区域", "Area"), 417, options.Region, 10, 100, options.Region + "%", value => options.Region = value, DanmakuSettings.Regions);
            int densityOffset=options.Region==100?36:0;
            if(densityOffset>0) Density(graphics);
            CompactSlider(graphics, L("不透明度", "Opacity"), 451+densityOffset, options.Opacity, 10, 100, options.Opacity + "%", value => options.Opacity = value);

            string[] speedLabels = english ? new[] { "Slowest", "Slower", "Normal", "Faster", "Fastest" } : new[] { "极慢", "较慢", "适中", "较快", "极快" };
            CompactSlider(graphics, L("弹幕速度", "Speed"), 485+densityOffset, options.Speed, 40, 160, speedLabels[DanmakuSettings.Index(options.Speed, DanmakuSettings.Speeds)], value => options.Speed = value, DanmakuSettings.Speeds);


        }
        void Density(Graphics graphics)
        {
            using(Brush fill=new SolidBrush(Color.FromArgb(64,65,71))) graphics.FillRectangle(fill,R(326,451,277,28));
            Label(graphics,L("弹幕密度","Density"),R(330,451,69,28),Color.White,false,11);
            string[] labels={L("正常","Normal"),L("较多","More"),L("重叠","Overlap")};
            string[] tips={L("绝对权重至少为 3，同类型弹幕避免重叠。无权重的来源保留全部弹幕。","Absolute weight at least 3, avoiding overlap within each type. Unscored sources keep every comment."),L("绝对权重至少为 2，同类型弹幕避免重叠。无权重时与正常档筛选相同。","Absolute weight at least 2, avoiding overlap within each type. Unscored sources match Normal."),L("沿用较多的筛选，滚动和固定弹幕均不受碰撞限制，可直接交叠。","Uses More filtering; scrolling and fixed comments may overlap without collision rejection.")};
            for(int i=0;i<3;i++) {
                int value=i+1; RectangleF rect=R(399+i*68,451,68,28);
                Label(graphics,labels[i],rect,catalog.Options.Density==value?accent:Color.White,false,11,true);
                if(i>0) Line(graphics,399+i*68,457,399+i*68,473);
                Register(rect,delegate {catalog.Options.Density=value;Changed();},null,null,tips[i]);
            }
        }
        void CompactSlider(Graphics graphics, string text, float y, int value, int min, int max, string display, Action<int> apply, int[] steps = null)
        {
            float left = 390, right = 550;
            float ratio = steps == null ? (value - min) / (float)(max - min) : DanmakuSettings.Index(value, steps) / (float)(steps.Length - 1);
            Label(graphics, text, R(330, y, 56, 28), Color.White, false, 12);
            using (Pen rail = new Pen(Color.FromArgb(76, 77, 84), 4 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round }) graphics.DrawLine(rail, left * scale, (y + 14) * scale, right * scale, (y + 14) * scale);
            using (Pen fill = new Pen(accent, 4 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round }) graphics.DrawLine(fill, left * scale, (y + 14) * scale, (left + (right - left) * ratio) * scale, (y + 14) * scale);
            if (steps != null) for (int i = 0; i < steps.Length; i++) using (Brush dot = new SolidBrush(Color.White)) graphics.FillEllipse(dot, R(left + (right - left) * i / (steps.Length - 1) - 2, y + 12, 4, 4));
            using (Brush knob = new SolidBrush(Color.White)) graphics.FillEllipse(knob, R(left + (right - left) * ratio - 7, y + 7, 14, 14));
            Label(graphics, display, R(560, y, 42, 28), Color.White, false, english && steps == DanmakuSettings.Speeds ? 9 : 12, true);
            Register(R(left - 8, y - 2, right - left + 16, 32), null, null, delegate(float mouseX)
            {
                float position = Math.Max(0, Math.Min(1, (mouseX / scale - left) / (right - left)));
                apply(steps == null ? (int)Math.Round(min + (max - min) * position) : DanmakuSettings.At(position, steps));
                catalog.Options.Clamp(); catalog.Revision++; Invalidate();
            });
        }
        void Checkbox(Graphics graphics, string text, float x, float y, float width, bool value, Action action, bool enabled = true)
        {
            RectangleF check = R(x, y + 7, 13, 13);
            if (value)
            {
                using (Brush fill = new SolidBrush(enabled ? accent : muted)) graphics.FillRectangle(fill, check);
                using (Pen tick = new Pen(BackColor, 1.7f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    graphics.DrawLines(tick, new[] { new PointF((x + 3) * scale, (y + 13) * scale), new PointF((x + 5.5f) * scale, (y + 15.5f) * scale), new PointF((x + 10) * scale, (y + 10) * scale) });
            }
            else using (Pen pen = new Pen(enabled ? Color.White : muted, scale)) graphics.DrawRectangle(pen, check.X, check.Y, check.Width, check.Height);
            Label(graphics, text, R(x + 23, y, width - 23, 28), enabled ? Color.White : muted, false, page == 0 ? 10 : 12); Register(R(x, y, width, 28), enabled ? action : null);
        }
        void Slider(Graphics graphics, string text, float y, int value, int minimum, int maximum, string suffix, Action<int> apply)
        {
            Label(graphics, text, R(24, y, 90, 26), Color.White, false, 13); float left = 120, right = 510, ratio = (value - minimum) / (float)(maximum - minimum);
            using (Pen track = new Pen(Color.FromArgb(66, 67, 75), 5 * scale)) graphics.DrawLine(track, left * scale, (y + 14) * scale, right * scale, (y + 14) * scale);
            using (Pen fill = new Pen(accent, 5 * scale)) graphics.DrawLine(fill, left * scale, (y + 14) * scale, (left + (right - left) * ratio) * scale, (y + 14) * scale);
            using (Brush dot = new SolidBrush(Color.White)) graphics.FillEllipse(dot, R(left + (right - left) * ratio - 7, y + 7, 14, 14));
            Label(graphics, value + suffix, R(526, y, 70, 28), Color.White, false, 13);
            Register(R(left - 8, y - 1, right - left + 16, 29), null, null, delegate(float mouseX) { int number = Math.Max(minimum, Math.Min(maximum, (int)Math.Round(minimum + (maximum - minimum) * (mouseX / scale - left) / (right - left)))); apply(number); catalog.Options.Clamp(); catalog.Revision++; Invalidate(); });
        }
        void Field(Graphics g, string text, float x, float y, float width, bool active, Action action, int color = -1)
        {
            RectangleF rect = R(x, y, width, 34);
            using (Brush fill = new SolidBrush(active ? Color.FromArgb(56, 47, 55) : Color.FromArgb(39, 40, 45))) g.FillRectangle(fill, rect);
            using (Pen border = new Pen(active ? accent : Color.FromArgb(73, 74, 81), scale)) g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
            if (color >= 0) { using (Brush swatch = new SolidBrush(Color.FromArgb(color | unchecked((int)0xFF000000)))) g.FillRectangle(swatch, R(x + 10, y + 10, 14, 14)); }
            Label(g, text, R(x + (color >= 0 ? 34 : 10), y, width - (color >= 0 ? 44 : 20), 34), Color.White, active, 12, color < 0 && width < 80);
            Register(rect, action);
        }
        void Section(Graphics g, string title, float y)
        { Label(g, title, R(24, y, 568, 24), muted, true, 11); }
        void SettingsPage(Graphics graphics)
        {
            bool dm = page == 2; CaptionOptions o = catalog.Options;
            Link(graphics, L("‹ 返回", "‹ Back"), 20, 12, 82, delegate { page = 0; picker = 0; });
            Label(graphics, dm ? L("弹幕设置", "Danmaku settings") : L("字幕设置", "Subtitle settings"), R(122, 12, 466, 30), Color.White, true, 16);
            Line(graphics, 24, 54, 596, 54);
            Section(graphics, L("字体与外观", "Font & appearance"), 66);
            string font = dm ? o.DanmakuFont : o.SubtitleFont;
            Field(graphics, FontLabel(font) + "  ▾", 24, 98, 332, picker == 1, delegate { picker = picker == 1 ? 0 : 1; });
            Field(graphics, "B", 374, 98, 42, dm ? o.DanmakuBold : o.SubtitleBold, delegate { if (dm) o.DanmakuBold = !o.DanmakuBold; else o.SubtitleBold = !o.SubtitleBold; Changed(); });
            Field(graphics, "I", 426, 98, 42, dm ? o.DanmakuItalic : o.SubtitleItalic, delegate { if (dm) o.DanmakuItalic = !o.DanmakuItalic; else o.SubtitleItalic = !o.SubtitleItalic; Changed(); });
            Slider(graphics, L("字号", "Font size"), 150, dm ? o.Size : o.SubtitleSize, dm ? 50 : 16, dm ? 170 : 64, dm ? "%" : "px", value => { if (dm) o.Size = value; else o.SubtitleSize = value; });
            Field(graphics, L("字体颜色  ▾", "Text color  ▾"), 24, 198, 172, picker == 2, delegate { picker = picker == 2 ? 0 : 2; }, dm ? o.DanmakuTextColor : o.SubtitleColor);
            Field(graphics, (dm ? o.DanmakuBackground : o.SubtitleBackground) ? L("背景色  ▾", "Background  ▾") : L("无填充  ▾", "No fill  ▾"), 210, 198, 172, picker == 3, delegate { picker = picker == 3 ? 0 : 3; }, dm ? o.DanmakuBackgroundColor : o.SubtitleBackgroundColor);
            Field(graphics, L("描边色  ▾", "Outline color  ▾"), 396, 198, 200, picker == 4, delegate { picker = picker == 4 ? 0 : 4; }, dm ? o.DanmakuOutlineColor : o.SubtitleOutlineColor);
            Section(graphics, L("文字效果", "Text effects"), 254);
            string[] outlines = { L("重墨", "Heavy"), L("描边", "Outline"), L("投影", "Shadow"), L("无", "None") };
            for (int i = 0; i < outlines.Length; i++) { int mode = i; Field(graphics, outlines[i], 24 + i * 146, 285, 134, (dm ? o.DanmakuOutline : o.SubtitleOutline) == mode, delegate { if (dm) o.DanmakuOutline = mode; else o.SubtitleOutline = mode; Changed(); }); }
            Line(graphics, 24, 342, 596, 342);
            Section(graphics, L("播放与布局", "Playback & layout"), 354);
            if (dm)
            {
                Checkbox(graphics, L("启用彩色弹幕", "Enable colored danmaku"), 24, 388, 270, o.UseDanmakuColor, delegate { o.UseDanmakuColor = !o.UseDanmakuColor; Changed(); });
                Checkbox(graphics, L("保留弹幕原色", "Keep original colors"), 316, 388, 280, !o.DanmakuUniformColor, delegate { o.DanmakuUniformColor = !o.DanmakuUniformColor; Changed(); });
                Checkbox(graphics, L("速度同步播放倍数", "Sync speed with playback"), 24, 431, 560, o.DanmakuSpeedSync, delegate { o.DanmakuSpeedSync = !o.DanmakuSpeedSync; Changed(); });
                Switch(graphics, 24, 476, o.DanmakuEnabled); Label(graphics, L("弹幕总开关（D）", "Danmaku toggle (D)"), R(67, 476, 510, 33), Color.White); Register(R(24, 474, 570, 37), ToggleDanmaku);
            }
            else
            {
                Slider(graphics, L("不透明度", "Opacity"), 388, o.SubtitleOpacity, 10, 100, "%", value => o.SubtitleOpacity = value);
                Slider(graphics, L("距底部", "Bottom margin"), 437, o.SubtitleBottom, 2, 30, "%", value => o.SubtitleBottom = value);
                Checkbox(graphics, L("多语言字幕（主／副）", "Bilingual subtitles"), 24, 484, 570, catalog.Slots.Multi, delegate { catalog.SetMulti(!catalog.Slots.Multi); });
            }
            Line(graphics, 24, 548, 596, 548);
            // Preview uses the same family, weight and colors as the actual renderer.
            RectangleF preview = R(24, 566, 572, 42);
            AssPreview(graphics,L("字幕和弹幕 · 文字预览", "Subtitles & danmaku · Preview"),preview,o,dm);
            if (dm) Link(graphics, L("恢复默认设置", "Restore defaults"), 24, 618, 180, delegate { o.ResetDanmaku(); picker = 0; Changed(); });
            else Label(graphics, L("图像字幕沿用原生渲染", "Bitmap subtitles use native rendering"), R(24, 618, 570, 26), muted, false, 11);
        }
        LibassRenderer previewRenderer;
        void AssPreview(Graphics graphics,string text,RectangleF bounds,CaptionOptions options,bool dm)
        {
            if(previewRenderer==null) previewRenderer=new LibassRenderer();
            using(Bitmap bitmap=new Bitmap(Math.Max(1,(int)bounds.Width),Math.Max(1,(int)bounds.Height),System.Drawing.Imaging.PixelFormat.Format32bppPArgb)) {
                float size=dm?25*scale*options.Size/100f:18*scale;
                string script=AssScene.Header(bitmap.Width,bitmap.Height,options,scale)+"Dialogue: 0,0:00:00.00,1:00:00.00,"+(dm?"Danmaku":"Subtitle")+",,0,0,0,,{\\an5\\pos("+(bitmap.Width/2)+","+(bitmap.Height/2)+")\\fs"+size.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}"+AssScene.Escape(text)+"\n";
                previewRenderer.SetTrack("preview",script); previewRenderer.Render(bitmap,"preview",0); graphics.DrawImageUnscaled(bitmap,(int)bounds.Left,(int)bounds.Top);
            }
        }
        void Picker(Graphics g)
        {
            if (picker == 0) return;
            hits.Clear(); bool dm = page == 2; CaptionOptions o = catalog.Options;
            float x = picker == 1 ? 24 : picker == 2 ? 24 : picker == 3 ? 210 : 316, y = picker == 1 ? 136 : 236;
            float width = picker == 1 ? 332 : 280, height = picker == 1 ? DanmakuSettings.Fonts.Length * 36 + 8 : 190;
            RectangleF rect = R(x, y, width, height);
            using (Brush shadow = new SolidBrush(Color.FromArgb(18, 18, 20))) g.FillRectangle(shadow, R(x + 4, y + 4, width, height));
            using (Brush fill = new SolidBrush(Color.FromArgb(39, 40, 45))) g.FillRectangle(fill, rect);
            using (Pen border = new Pen(Color.FromArgb(89, 91, 100), scale)) g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
            if (picker == 1)
            {
                for (int i = 0; i < DanmakuSettings.Fonts.Length; i++)
                {
                    string font = DanmakuSettings.Fonts[i]; RectangleF item = R(x + 4, y + 4 + i * 36, width - 8, 36); bool active = font == (dm ? o.DanmakuFont : o.SubtitleFont);
                    if (active || item.Contains(dragPoint)) using (Brush fill = new SolidBrush(Color.FromArgb(55, 57, 65))) g.FillRectangle(fill, item);
                    Label(g, active ? "✓" : "", R(x + 12, y + 4 + i * 36, 22, 36), accent);
                    using (Font preview = new Font(font, 14 * scale, GraphicsUnit.Pixel)) TextRenderer.DrawText(g, FontLabel(font), preview, Rectangle.Round(R(x + 42, y + 4 + i * 36, width - 56, 36)), Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    Register(item, delegate { if (dm) o.DanmakuFont = font; else o.SubtitleFont = font; picker = 0; Changed(); });
                }
            }
            else
            {
                int kind = picker; int[] colors = { 0xFFFFFF, 0xE7E6E6, 0xA6A6A6, 0x595959, 0x000000, 0x4472C4, 0xED7D31, 0xFFC000, 0x70AD47, 0x5B9BD5, 0xC00000, 0xFF0000, 0xFFFF00, 0x92D050, 0x00B0F0, 0x7030A0, 0xFB7299, 0x80D8FF };
                Label(g, L("主题颜色", "Theme colors"), R(x + 12, y + 6, width - 24, 24), muted, false, 11);
                for (int i = 0; i < colors.Length; i++)
                {
                    int color = colors[i]; RectangleF cell = R(x + 14 + i % 6 * 43, y + 36 + i / 6 * 34, 32, 24);
                    using (Brush fill = new SolidBrush(Color.FromArgb(color | unchecked((int)0xFF000000)))) g.FillRectangle(fill, cell);
                    Register(cell, delegate { if (dm) { if (kind == 2) { o.DanmakuTextColor = color; o.DanmakuUniformColor = true; o.UseDanmakuColor = true; } else if (kind == 3) { o.DanmakuBackgroundColor = color; o.DanmakuBackground = true; } else o.DanmakuOutlineColor = color; } else { if (kind == 2) o.SubtitleColor = color; else if (kind == 3) { o.SubtitleBackgroundColor = color; o.SubtitleBackground = true; } else o.SubtitleOutlineColor = color; } picker = 0; Changed(); });
                }
                if (kind == 3) Link(g, L("无填充", "No fill"), x + 14, y + 146, width - 28, delegate { if (dm) o.DanmakuBackground = false; else o.SubtitleBackground = false; picker = 0; Changed(); });
                else Link(g, L("关闭调色板", "Close palette"), x + 14, y + 146, width - 28, delegate { picker = 0; });
            }
        }
        string FontLabel(string font)
        {
            if (english) return font;
            string[] labels = { "黑体", "宋体", "新宋体", "仿宋", "微软雅黑", "微软雅黑 UI", "Noto Sans CJK" };
            int index = Array.IndexOf(DanmakuSettings.Fonts, font); return index < 0 ? font : labels[index];
        }
        internal void ToggleDanmaku() { catalog.Options.DanmakuEnabled = !catalog.Options.DanmakuEnabled; Changed(); }
        void Changed() { catalog.Options.Save(); catalog.Revision++; Invalidate(); }
        void Import()
        {
            dialog = true; Dismiss();
            try { using (OpenFileDialog file = new OpenFileDialog { Title = L("加载字幕或弹幕", "Load subtitles or danmaku"), Filter = "字幕和弹幕|*.srt;*.ass;*.ssa;*.vtt;*.lrc;*.xml;*.json;*.ttml;*.dfxp|所有文件|*.*", Multiselect = true, CheckFileExists = true }) { if (catalog.Sources.Count > 0 && catalog.Sources[0].Path != null) file.InitialDirectory = Path.GetDirectoryName(catalog.Sources[0].Path); if (file.ShowDialog(new PlayerOwner(Player)) == DialogResult.OK) foreach (string path in file.FileNames) catalog.Import(path); } }
            finally { dialog = false; hovering = IntPtr.Zero; }
        }
        int At(Point point) { return hits.FindIndex(hit => hit.Rect.Contains(point)); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); int index = At(e.Location); if (index != hovered) { hovered = index; tip.SetToolTip(this, index >= 0 ? hits[index].Source != null ? hits[index].Source.Title + "\n" + hits[index].Source.Detail + "\n" + hits[index].Source.Error : hits[index].Tooltip??"" : ""); Invalidate(); }
            dragPoint = e.Location;
            if (slider != null) slider.Slide(e.X); if (dragSource != null && Math.Abs(e.X - start.X) + Math.Abs(e.Y - start.Y) > 8 * scale) { dragging = true; tip.Hide(this); Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = -1; tip.Hide(this); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        { base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return; if (picker != 0 && At(e.Location) < 0) { picker = 0; Invalidate(); return; } down = At(e.Location); pressed = down < 0 ? null : hits[down]; start = dragPoint = e.Location; dragging = false; if (pressed != null) { slider = pressed.Slide == null ? null : pressed; if (slider != null) { Capture = true; slider.Slide(e.X); } else if (pressed.Source != null) { dragSource = pressed.Source; Capture = true; } } }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); if (e.Button != MouseButtons.Left) return;
            if (slider != null) { slider.Slide(e.X); catalog.Options.Save(); }
            else if (dragging && dragSource != null) { TextCategory? target = CaptionDropAreas.At(e.X / scale, e.Y / scale); if (target.HasValue && target.Value != dragSource.Category) catalog.Classify(dragSource, target.Value); }
            else { int index = At(e.Location); if (pressed != null && index >= 0 && hits[index].Source == pressed.Source && hits[index].Rect == pressed.Rect && pressed.Action != null) pressed.Action(); }
            Capture = false; slider = pressed = null; dragSource = null; dragging = false; down = -1; Invalidate();
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (page != 0 || dragging) return;
            TextCategory? category = CaptionDropAreas.At(e.X / scale, e.Y / scale);
            if (!category.HasValue) return;
            int list = category.Value == TextCategory.Subtitle ? 0 : category.Value == TextCategory.Danmaku ? 1 : category.Value == TextCategory.Uncertain ? 2 : 3;
            listPages[list] = Math.Max(0, listPages[list] - Math.Sign(e.Delta)); Invalidate();
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        { base.OnMouseCaptureChanged(e); if (!Capture && !releasingMouse) { if (slider != null) catalog.Options.Save(); slider = pressed = null; dragSource = null; dragging = false; down = -1; Invalidate(); } }
        protected override void WndProc(ref Message message)
        {
            // Consume list scrolling here; DefWindowProc would bubble it to PotPlayer's volume handler.
            if (message.Msg == 0x20A)
            { long point = message.LParam.ToInt64(); Point local = PointToClient(new Point(unchecked((short)point), unchecked((short)(point >> 16)))); OnMouseWheel(new MouseEventArgs(MouseButtons.None, 0, local.X, local.Y, unchecked((short)(message.WParam.ToInt64() >> 16)))); message.Result = IntPtr.Zero; return; }
            // WinForms releases capture before raising MouseUp. Keep the pressed row until
            // its release is handled, while a capture stolen by another window still cancels.
            if (message.Msg == 0x202)
            { releasingMouse = true; try { base.WndProc(ref message); } finally { releasingMouse = false; } return; }
            base.WndProc(ref message);
        }
        protected override void OnPaint(PaintEventArgs e)
        { base.OnPaint(e); hits.Clear(); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; if (page == 0) MainPage(e.Graphics); else { SettingsPage(e.Graphics); Picker(e.Graphics); } using (Pen border = new Pen(Color.FromArgb(62, 63, 70))) e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1); if (page == 0) DraggedRow(e.Graphics); }
        protected override void Dispose(bool disposing) { if (disposing) { tip.Dispose(); if(previewRenderer!=null) previewRenderer.Dispose(); } base.Dispose(disposing); }
    }
}

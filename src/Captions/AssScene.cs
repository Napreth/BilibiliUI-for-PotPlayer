using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BilibiliPotPlayer
{
    internal sealed class AssScene : IDisposable
    {
        LibassRenderer renderer;
        string mediaPath;
        readonly List<string> keys = new List<string>();
        readonly List<string> subtitleKeys=new List<string>();
        readonly AdvancedDanmakuPlayback advanced = new AdvancedDanmakuPlayback();
        int revision = -1;
        internal static string Escape(string text)
        { return text.Replace("\\","＼").Replace("{","｛").Replace("}","｝").Replace("\r","").Replace("\n","\\N"); }
        static string N(double value) { return value.ToString("0.###",CultureInfo.InvariantCulture); }
        static string Time(double seconds)
        { long cs=(long)Math.Round(Math.Max(0,seconds)*100); return (cs/360000).ToString(CultureInfo.InvariantCulture)+":"+(cs/6000%60).ToString("00")+":"+(cs/100%60).ToString("00")+"."+(cs%100).ToString("00"); }
        static string Color(int rgb,int opacity)
        { int bgr=(rgb & 255)<<16 | rgb & 0xFF00 | (rgb>>16 & 255); return "&H"+((uint)(255-opacity*255/100)<<24 | (uint)bgr).ToString("X8",CultureInfo.InvariantCulture); }
        internal static string Header(int width,int height,CaptionOptions options,float dpi=1)
        {
            return "[Script Info]\nScriptType: v4.00+\nPlayResX: "+width+"\nPlayResY: "+height+"\nScaledBorderAndShadow: yes\nWrapStyle: 0\n[V4+ Styles]\nFormat: Name,Fontname,Fontsize,PrimaryColour,SecondaryColour,OutlineColour,BackColour,Bold,Italic,Underline,StrikeOut,ScaleX,ScaleY,Spacing,Angle,BorderStyle,Outline,Shadow,Alignment,MarginL,MarginR,MarginV,Encoding\n"+
                Style("Subtitle",options.SubtitleFont,options.SubtitleSize*height/1080.0,options.SubtitleColor,options.SubtitleOpacity,options.SubtitleBold,options.SubtitleItalic,options.SubtitleOutline,options.SubtitleOutlineColor,options.SubtitleBackground,options.SubtitleBackgroundColor)+
                Style("Danmaku",options.DanmakuFont,25,options.DanmakuTextColor,options.Opacity,options.DanmakuBold,options.DanmakuItalic,options.DanmakuOutline,options.DanmakuOutlineColor,options.DanmakuBackground,options.DanmakuBackgroundColor,true,dpi)+
                "[Events]\nFormat: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text\n";
        }
        static string Style(string name,string font,double size,int color,int opacity,bool bold,bool italic,int outline,int outlineColor,bool background,int backgroundColor,bool danmaku=false,float dpi=1)
        {
            double border=outline==3 || outline==2 ? 0 : danmaku && outline==1 ? 1 : Math.Max(1.5,size/(outline==0?8:12));
            if(danmaku) border*=dpi;
                return "Style: "+name+","+font+","+N(size)+","+Color(color,opacity)+","+Color(color,opacity)+","+Color(background?backgroundColor:outlineColor,opacity)+","+Color(background?backgroundColor:outlineColor,background?opacity*55/100:opacity)+","+(bold?-1:0)+","+(italic?-1:0)+",0,0,100,100,0,0,"+(background?3:1)+","+N(background?Math.Max(2,border):border)+","+N(outline==2?Math.Max(1,size/12)*(danmaku?dpi:1):0)+",2,0,0,0,1\n";
        }
        void Comments(Bitmap bitmap,CaptionCatalog catalog,long time,List<DanmakuPlacement> comments,double animation,bool synchronized,bool fixedComments)
        {
            double drawingTime=synchronized?time/1000.0:animation, origin=Math.Floor((drawingTime-60)/60)*60;
            var styles=new Dictionary<CaptionOptions,string>(); StringBuilder definitions=new StringBuilder(), events=new StringBuilder();
            foreach(DanmakuPlacement p in comments) {
                if(!catalog.Options.DanmakuEnabled || p.Cue.Mode>=7 || (p.Cue.Effects & CueEffects.Protocol)!=0) continue;
                if((p.Cue.Mode==4 || p.Cue.Mode==5)!=fixedComments)continue;
                if((p.Appearance!=null && p.Appearance.DanmakuSpeedSync)!=synchronized) continue;
                CaptionOptions appearance=p.Appearance??catalog.Options; string name="Danmaku";
                if(p.Appearance!=null && !styles.TryGetValue(appearance,out name)) {
                    name="Live"+styles.Count; styles.Add(appearance,name);
                    definitions.Append(Style(name,appearance.DanmakuFont,25,appearance.DanmakuTextColor,appearance.Opacity,appearance.DanmakuBold,appearance.DanmakuItalic,appearance.DanmakuOutline,appearance.DanmakuOutlineColor,appearance.DanmakuBackground,appearance.DanmakuBackgroundColor,true,p.DpiScale));
                }
                events.Append("Dialogue: 0,").Append(p.Timed?Time(p.Start-origin):"0:00:00.00").Append(',').Append(p.Timed?Time(p.Start+p.Life-origin):"1:00:00.00").Append(',').Append(name).Append(",,0,0,0,,{\\an7\\q2\\clip(0,0,").Append(bitmap.Width).Append(',').Append(p.Appearance!=null?p.ClipHeight:DanmakuLayout.AreaHeight(bitmap.Height,appearance)).Append(')');
                if(p.Timed && p.FromX!=p.ToX) events.Append("\\move(").Append(N(p.FromX)).Append(',').Append(N(p.Y)).Append(',').Append(N(p.ToX)).Append(',').Append(N(p.Y)).Append(')');
                else events.Append("\\pos(").Append(N(p.X)).Append(',').Append(N(p.Y)).Append(')');
                events.Append("\\fs").Append(N(p.Size)).Append("\\1c").Append(Color(DanmakuSettings.ColorFor(p.Cue,appearance),appearance.Opacity)).Append('}').Append(Escape(p.Cue.Text.Replace('\n',' '))).Append('\n');
            }
            string key="comments-"+(fixedComments?"fixed-":"scroll-")+(synchronized?"media":"animation");
            string header=Header(bitmap.Width,bitmap.Height,catalog.Options).Replace("[Events]\n",definitions+"[Events]\n");
            renderer.SetTrack(key,header+events); renderer.Render(bitmap,key,(long)Math.Round((drawingTime-origin)*1000));
        }
        internal static string Script(TextSource source,IEnumerable<TextCue> events)
        {
            StringBuilder script=new StringBuilder(source.AssHeader);
            foreach(TextCue cue in events.OrderBy(c=>c.AssOrder)) {
                if((cue.Effects & CueEffects.Protocol)!=0 || cue.AssFields.Length==0) continue;
                int comma=cue.AssFields.IndexOf(','); if(comma<0) continue;
                script.Append("Dialogue: ").Append(cue.AssFields.Substring(0,comma)).Append(',').Append(Time(cue.Start)).Append(',').Append(Time(cue.End)).Append(',').Append(cue.AssFields.Substring(comma+1)).Append('\n');
            }
            return script.ToString();
        }
        internal static bool Enabled(TextSource source,CaptionCatalog catalog)
        {
            if(source.Bitmap || !source.AssRenderable) return false;
            if(source.Category==TextCategory.Subtitle) return catalog.Options.SubtitlesEnabled && (source.Id==catalog.Slots.Main || source.Id==catalog.Slots.Secondary);
            // Ordinary ASS comments use the same layout and style settings as XML/JSON.
            if(source.Category==TextCategory.Danmaku) return false;
            return source.Category==TextCategory.Advanced && source.Enabled && catalog.Options.AssEffectsEnabled;
        }
        internal static bool Filter(TextCue cue,CaptionOptions options)
        { return (cue.Mode==4 || cue.Mode==5 ? options.Fixed : options.Scrolling) && (cue.Color==0xFFFFFF || options.Colored); }
        void Update(CaptionCatalog catalog,long time,int? clockEpoch)
        {
            if(renderer==null || mediaPath!=catalog.MediaPath) { if(renderer!=null) renderer.Dispose(); renderer=new LibassRenderer(); mediaPath=catalog.MediaPath; advanced.Reset(); revision=-1; }
            bool changed=clockEpoch.HasValue && advanced.Advance(catalog,time/1000.0,clockEpoch.Value);
            if(revision==catalog.Revision && !changed) return;
            revision=catalog.Revision; renderer.Clear(); keys.Clear(); subtitleKeys.Clear();
            foreach(TextSource source in catalog.Sources) if(DanmakuLayout.UsesLayout(source)) renderer.AddFonts(source.Fonts);
            var sources=clockEpoch.HasValue ? catalog.Sources.Where(s=>s.Category==TextCategory.Subtitle && Enabled(s,catalog)).Concat(advanced.Active.Select(e=>e.Source)).Distinct() : catalog.Sources.Where(s=>Enabled(s,catalog));
            var groups=sources.GroupBy(s=>(s.Category==TextCategory.Subtitle?"subtitle:":"danmaku:")+(s.Path??s.Title)+":"+s.Stream+":"+s.AssHeader);
            foreach(var group in groups) {
                foreach(TextSource source in group) renderer.AddFonts(source.Fonts);
                List<TextCue> events=new List<TextCue>();
                foreach(TextSource source in group) events.AddRange(clockEpoch.HasValue && advanced.Active.Any(e=>e.Source==source) ? advanced.Active.Where(e=>e.Source==source).Select(e=>e.Cue) : source.Events);
                string key="source-"+(keys.Count+subtitleKeys.Count);
                try { renderer.SetTrack(key,Script(group.First(),events)); (group.First().Category==TextCategory.Subtitle?subtitleKeys:keys).Add(key); }
                catch(InvalidOperationException error) { catalog.Status=error.Message; }
            }
        }
        static void AddBackgrounds(List<Rectangle> ink,List<Rectangle> backgrounds,Rectangle viewport,float dpi,float lineHeight)
        {
            var blocks=new List<Rectangle>();
            foreach(Rectangle region in ink) {
                Rectangle block=region;
                for(int i=0;i<blocks.Count;) {
                    if(block.IntersectsWith(blocks[i])) {block=Rectangle.Union(block,blocks[i]);blocks.RemoveAt(i);i=0;} else i++;
                }
                blocks.Add(block);
            }
            foreach(Rectangle block in blocks) {
                Rectangle box=block;
                box.Inflate((int)Math.Ceiling(12*dpi),(int)Math.Ceiling(Math.Max(2*dpi,(lineHeight-block.Height)/2+2*dpi)));
                box=Rectangle.Intersect(viewport,box);
                if(!box.IsEmpty)backgrounds.Add(box);
            }
        }
        static void RoundedBackground(Graphics graphics,Brush brush,Rectangle box,float dpi)
        {
            float diameter=Math.Min(8*dpi,Math.Min(box.Width,box.Height));
            using(var path=new System.Drawing.Drawing2D.GraphicsPath()) {
                path.AddArc(box.Left,box.Top,diameter,diameter,180,90);
                path.AddArc(box.Right-diameter,box.Top,diameter,diameter,270,90);
                path.AddArc(box.Right-diameter,box.Bottom-diameter,diameter,diameter,0,90);
                path.AddArc(box.Left,box.Bottom-diameter,diameter,diameter,90,90);
                path.CloseFigure();graphics.FillPath(brush,path);
            }
        }
        internal bool Render(Bitmap bitmap,CaptionCatalog catalog,long time,List<DanmakuPlacement> comments,double? commentTime=null,int? clockEpoch=null,Rectangle? pictureBounds=null,float dpiScale=1)
        {
            using(Graphics clear=Graphics.FromImage(bitmap)) clear.Clear(System.Drawing.Color.Transparent);
            Update(catalog,time,clockEpoch);
            Rectangle picture=Rectangle.Intersect(new Rectangle(0,0,bitmap.Width,bitmap.Height),pictureBounds??new Rectangle(0,0,bitmap.Width,bitmap.Height));
            if(picture.Width<=0 || picture.Height<=0) picture=Rectangle.Empty;
            StringBuilder frame=new StringBuilder(Header(picture.Width,picture.Height,catalog.Options));
            TextSource main=catalog.Find(catalog.Slots.Main), second=catalog.Find(catalog.Slots.Secondary);
            double bottom=picture.Height*(1-catalog.Options.SubtitleBottom/100.0);
            if(catalog.Options.SubtitlesEnabled && !picture.IsEmpty) {
                bottom=PlainCaption(frame,second,time/1000.0,picture.Width,picture.Height,bottom,.9,catalog.Options);
                bottom=PlainCaption(frame,main,time/1000.0,picture.Width,picture.Height,bottom,1,catalog.Options);
                if(catalog.Options.TranslationEnabled && catalog.Translation!=null && catalog.Translation.Track.Cues.Count>0) {
                    // Authored ASS coordinates stay intact. Place the extra
                    // row in a clear band, including both rows' gray padding.
                    renderer.SetTrack("frame",frame.ToString());
                    var ink=renderer.Regions("frame",0,picture);
                    foreach(string key in subtitleKeys) ink.AddRange(renderer.Regions(key,time,picture));
                    double size=catalog.Options.SubtitleSize*picture.Height/1080.0;
                    double translationHeight=size*1.5*Math.Max(1,catalog.Translation.Track.Cues.Sum(c=>c.Text.Split('\n').Length));
                    foreach(Rectangle block in ink.OrderByDescending(r=>r.Top))
                        if(block.Top-picture.Top-size*.65-8*dpiScale<bottom && block.Bottom-picture.Top+size*.65+8*dpiScale>bottom-translationHeight)
                            bottom=Math.Min(bottom,block.Top-picture.Top-size*.65-8*dpiScale);
                    PlainCaption(frame,catalog.Translation.Track,time/1000.0,picture.Width,picture.Height,bottom,.9,catalog.Options);
                }
            }
            if(!picture.IsEmpty)renderer.SetTrack("frame",frame.ToString());
            foreach(string key in keys) renderer.Render(bitmap,key,time,100,picture);
            // Preserve type ordering across both independent animation clocks.
            // ASS Layer alone cannot order events in separate rendered tracks.
            Comments(bitmap,catalog,time,comments,commentTime??time/1000.0,false,false);
            Comments(bitmap,catalog,time,comments,commentTime??time/1000.0,true,false);
            Comments(bitmap,catalog,time,comments,commentTime??time/1000.0,false,true);
            Comments(bitmap,catalog,time,comments,commentTime??time/1000.0,true,true);
            if(catalog.Options.AvoidSubtitles && catalog.Options.SubtitlesEnabled && !picture.IsEmpty) {
                var boxes=new List<Rectangle>();
                Rectangle viewport=new Rectangle(0,0,bitmap.Width,bitmap.Height);
                AddBackgrounds(renderer.Regions("frame",0,picture),boxes,viewport,dpiScale,catalog.Options.SubtitleSize*picture.Height/1080f*1.5f);
                foreach(string key in subtitleKeys)AddBackgrounds(renderer.Regions(key,time,picture),boxes,viewport,dpiScale,0);
                using(Graphics g=Graphics.FromImage(bitmap))using(var gray=new SolidBrush(System.Drawing.Color.FromArgb(160,48,48,48))) {
                    // Replace only the subtitle background's pixels. The comment
                    // instance keeps moving underneath and reappears beyond it.
                    g.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    foreach(Rectangle box in boxes)RoundedBackground(g,gray,box,dpiScale);
                }
            }
            foreach(string key in subtitleKeys)renderer.Render(bitmap,key,time,100,picture);
            if(!picture.IsEmpty)renderer.Render(bitmap,"frame",0,100,picture);
            return keys.Count>0 || subtitleKeys.Count>0 || comments.Count>0 || main!=null || second!=null;
        }
        static double PlainCaption(StringBuilder frame,TextSource source,double time,int width,int height,double bottom,double ratio,CaptionOptions options)
        {
            if(source==null || source.Bitmap || source.AssRenderable) return bottom;
            string text=String.Join("\n",DanmakuLayout.Active(source.Cues,time).Select(c=>c.Text)); if(text.Length==0) return bottom;
            double size=Math.Max(12,options.SubtitleSize*height/1080.0*ratio);
            int margin=(int)(width*.05);
            frame.Append("Dialogue: 1,0:00:00.00,1:00:00.00,Subtitle,,").Append(margin).Append(',').Append(margin).Append(",0,,{\\an2\\pos(").Append(width/2).Append(',').Append(N(bottom)).Append(")\\fs").Append(N(size)).Append('}').Append(Escape(text)).Append('\n');
            return bottom-size*1.5*text.Split('\n').Length-4;
        }
        public void Dispose() { if(renderer!=null) renderer.Dispose(); }
    }
}

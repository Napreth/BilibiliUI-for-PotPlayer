using System;
using System.Collections.Generic;
using System.Drawing;

namespace BilibiliPotPlayer
{
    // Admission and appearance belong to a live comment, not to a reconstructed
    // historical frame. Rejected or newly enabled past events are never backfilled.
    internal sealed class DanmakuPlayback
    {
        readonly List<DanmakuPlacement> active = new List<DanmakuPlacement>();
        bool initialized;
        double previous;
        int epoch;
        uint randomState = 0x9E3779B9;
        HashSet<TextCue> enabledCues;
        internal const int MaxActive = 512;
        internal void Reset() { active.Clear(); initialized = false; randomState=0x9E3779B9; enabledCues=null; }
        internal void SetEnabledCues(IEnumerable<TextCue> cues) { enabledCues=new HashSet<TextCue>(cues); }
        int Choose(int count)
        {
            // A repeatable spatial shuffle; admission order must not determine a
            // top-to-bottom staircase. This never assigns or filters cue weights.
            randomState ^= randomState << 13; randomState ^= randomState >> 17; randomState ^= randomState << 5;
            return (int)(randomState % (uint)count);
        }
        static bool Fixed(TextCue cue) { return cue.Mode == 4 || cue.Mode == 5; }
        static bool SamePool(TextCue a,TextCue b)
        { return Fixed(a) || Fixed(b) ? a.Mode==b.Mode : true; }
        static double Age(DanmakuPlacement p, double media, double animation)
        { return (p.Appearance.DanmakuSpeedSync ? media : animation) - p.Start; }
        static void Position(DanmakuPlacement p, double media, double animation)
        { p.X = p.FromX + (p.ToX - p.FromX) * (float)Math.Max(0, Math.Min(1, Age(p, media, animation) / p.Life)); }
        static void Geometry(DanmakuPlacement p, int width, int height, float dpi, Func<TextCue,float,CaptionOptions,float> measure)
        {
            if (p.CanvasWidth != width || p.CanvasHeight != height || p.DpiScale != dpi)
            {
                p.Size *= p.Appearance.ScaleWithScreen ? height / (float)p.CanvasHeight : dpi / p.DpiScale;
                p.Width = measure(p.Cue, p.Size, p.Appearance);
                p.Y *= height / (float)p.CanvasHeight;
                p.ClipHeight = (int)Math.Round(p.ClipHeight * height / (double)p.CanvasHeight);
                p.CanvasWidth = width; p.CanvasHeight = height;
                p.DpiScale = dpi;
            }
            p.FromX = Fixed(p.Cue) ? (width - p.Width) / 2 : p.Cue.Mode == 6 ? -p.Width : width;
            p.ToX = Fixed(p.Cue) ? p.FromX : p.Cue.Mode == 6 ? width : -p.Width;
        }
        bool Free(DanmakuPlacement next, double media, double animation, double rate)
        {
            foreach (DanmakuPlacement old in active)
            {
                if(!SamePool(next.Cue,old.Cue)) continue;
                if (next.Y >= old.Y + old.Size * 1.125f || old.Y >= next.Y + next.Size * 1.125f) continue;
                if (Fixed(next.Cue) || Fixed(old.Cue) || next.Cue.Mode != old.Cue.Mode) return false;
                double gap = next.Cue.Mode == 6 ? old.X - 12 : next.CanvasWidth - old.X - old.Width - 12;
                if (gap < 0) return false;
                double oldRate = old.Appearance.DanmakuSpeedSync ? rate : 1, newRate = next.Appearance.DanmakuSpeedSync ? rate : 1;
                double oldVelocity = (old.CanvasWidth + old.Width) / old.Life * oldRate;
                double newVelocity = (next.CanvasWidth + next.Width) / next.Life * newRate;
                double remaining = (old.Life - Age(old, media, animation)) / oldRate;
                if ((newVelocity - oldVelocity) * remaining >= gap) return false;
            }
            return true;
        }
        bool Place(DanmakuPlacement next,int area,float line,double media,double animation,double rate)
        {
            int rows=Math.Max(1,Math.Min(100,(int)Math.Floor((area-next.Size*1.125f)/line)+1));
            bool fixedCue=Fixed(next.Cue), overlap=DanmakuDensity.Overlap(next.Appearance);
            if(overlap) { int row=Choose(rows); next.Y=fixedCue && next.Cue.Mode==4?Math.Max(0,area-next.Size*1.125f-row*line):row*line; return true; }
            int bestLoad=Int32.MaxValue,ties=0; float bestY=0;
            for(int row=0;row<rows;row++)
            {
                next.Y=fixedCue && next.Cue.Mode==4?Math.Max(0,area-next.Size*1.125f-row*line):row*line;
                if(!Free(next,media,animation,rate)) continue;
                if(fixedCue) return true;
                int load=0;
                foreach(DanmakuPlacement old in active)
                    if(SamePool(next.Cue,old.Cue) && next.Y<old.Y+old.Size*1.125f && old.Y<next.Y+next.Size*1.125f) load++;
                // Spread traffic across the available height, choosing among
                // equally occupied safe lanes without correlating row with time.
                if(load<bestLoad) { bestLoad=load; bestY=next.Y; ties=1; }
                else if(load==bestLoad && Choose(++ties)==0) bestY=next.Y;
            }
            if(bestLoad<Int32.MaxValue) { next.Y=bestY; return true; }
            return false;
        }
        internal List<DanmakuPlacement> Frame(List<TextCue> timeline, double media, int width, int height, CaptionOptions options,
            Func<TextCue,float,CaptionOptions,float> measure, double animation, int clockEpoch = 0, double rate = 1, Rectangle? pictureBounds=null,float dpiScale=1,bool subtitlesPresent=true)
        {
            if (width <= 0 || height <= 0) return new List<DanmakuPlacement>();
            dpiScale=Math.Max(.5f,Math.Min(4,dpiScale));
            bool first = !initialized || epoch != clockEpoch;
            if (first) { active.Clear(); previous = media; initialized = true; epoch = clockEpoch; randomState=0x9E3779B9; }
            for (int i = active.Count - 1; i >= 0; i--)
            {
                DanmakuPlacement p = active[i];
                if (!options.DanmakuEnabled || !AssScene.Filter(p.Cue,options) || enabledCues!=null && !enabledCues.Contains(p.Cue) || Age(p, media, animation) >= p.Life) { active.RemoveAt(i); continue; }
                Geometry(p, width, height,dpiScale, measure); Position(p, media, animation);
            }
            int start = DanmakuLayout.LowerBound(timeline, first ? media : previous + .0000001);
            if (options.DanmakuEnabled)
            {
                CaptionOptions appearance = null;
                for (int i = start; i < timeline.Count && timeline[i].Start <= media; i++)
                {
                    TextCue cue = timeline[i];
                    if (!AssScene.Filter(cue, options) || !DanmakuDensity.Allows(cue,options) || cue.Mode >= 7 || (cue.Effects & (CueEffects.Animation | CueEffects.Drawing | CueEffects.Protocol)) != 0 || active.Count >= MaxActive) continue;
                    int area = DanmakuLayout.AreaHeight(height, options,pictureBounds,subtitlesPresent); if (area <= 0) continue;
                    if (appearance == null) appearance = options.Snapshot();
                    float size = DanmakuSettings.SizeFor(cue,height,appearance,dpiScale);
                    double life = 8.0 * 100 / appearance.Speed; if (Fixed(cue)) life = Math.Min(5, life);
                    DanmakuPlacement p = new DanmakuPlacement { Cue=cue, Appearance=appearance, Size=size, Width=measure(cue,size,appearance),
                        CanvasWidth=width, CanvasHeight=height, DpiScale=dpiScale,ClipHeight=area, Start=appearance.DanmakuSpeedSync?media:animation, Life=life, Timed=true };
                    Geometry(p, width, height,dpiScale, measure); Position(p, media, animation);
                    float line = Math.Max(1,30*dpiScale*DanmakuSettings.ScreenScale(height,appearance,dpiScale)*appearance.Size/100f);
                    if(Place(p,area,line,media,animation,rate)) active.Add(p);
                }
            }
            previous = Math.Max(previous, media);
            return new List<DanmakuPlacement>(active);
        }
    }
}

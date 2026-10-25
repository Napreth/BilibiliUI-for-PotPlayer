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
                p.Size *= dpi / p.DpiScale * DanmakuSettings.ScreenScale(height,p.Appearance,dpi) / DanmakuSettings.ScreenScale(p.CanvasHeight,p.Appearance,p.DpiScale);
                p.Width = measure(p.Cue, p.Size, p.Appearance);
                p.Height = DanmakuTiming.Height(p.Cue,p.Size);
                p.Y *= height / (float)p.CanvasHeight;
                p.ClipHeight = (int)Math.Round(p.ClipHeight * height / (double)p.CanvasHeight);
                p.CanvasWidth = width; p.CanvasHeight = height;
                p.DpiScale = dpi;
            }
            p.FromX = Fixed(p.Cue) ? (width - p.Width) / 2 : p.Cue.Mode == 6 ? -p.Width : width;
            p.ToX = Fixed(p.Cue) ? p.FromX : p.Cue.Mode == 6 ? width : -p.Width;
        }
        bool Free(DanmakuPlacement next, double media, double animation, double rate,out int load)
        {
            load=0; float padding=next.DpiScale;
            foreach (DanmakuPlacement old in active)
            {
                if(old.Group!=next.Group || !SamePool(next.Cue,old.Cue)) continue;
                if (next.Y+.001f >= old.Y + old.Height+padding || old.Y+.001f >= next.Y + next.Height+padding) continue;
                load++;
                if (Fixed(next.Cue) || Fixed(old.Cue) || next.Cue.Mode != old.Cue.Mode) return false;
                double gap = next.Cue.Mode == 6 ? old.X - 12*padding : next.CanvasWidth - old.X - old.Width - 12*padding;
                if (gap < 0) return false;
                double oldRate = old.Appearance.DanmakuSpeedSync ? rate : 1, newRate = next.Appearance.DanmakuSpeedSync ? rate : 1;
                double oldVelocity = (old.CanvasWidth + old.Width) / old.Life * oldRate;
                double newVelocity = (next.CanvasWidth + next.Width) / next.Life * newRate;
                double remaining = (old.Life - Age(old, media, animation)) / oldRate;
                if ((newVelocity - oldVelocity) * remaining >= gap) return false;
            }
            return true;
        }
        bool Place(DanmakuPlacement next,int area,double media,double animation,double rate)
        {
            bool fixedCue=Fixed(next.Cue);
            int groups=DanmakuDensity.Overlap(next.Appearance)?3:1;
            float bottom=Math.Max(0,area-next.Height),step=Math.Max(1,next.Height+next.DpiScale);
            for(int group=0;group<groups;group++)
            {
                next.Group=group;
                var positions=new List<float>{0,bottom};
                for(float y=step;y<bottom;y+=step) positions.Add(y);
                // Add actual box edges as well as evenly distributed candidates.
                // A smaller incoming font can use gaps between larger comments.
                foreach(DanmakuPlacement old in active)
                    if(old.Group==group && SamePool(next.Cue,old.Cue)) {
                        float below=old.Y+old.Height+next.DpiScale, above=old.Y-next.Height-next.DpiScale;
                        if(below>=0 && below<=bottom)positions.Add(below);
                        if(above>=0 && above<=bottom)positions.Add(above);
                    }
                positions.Sort(); if(fixedCue && next.Cue.Mode==4)positions.Reverse();
                int bestLoad=Int32.MaxValue,ties=0;float bestY=0,last=Single.NaN;
                foreach(float y in positions) {
                    if(Math.Abs(y-last)<.001f)continue; last=y; next.Y=y;
                    int load;if(!Free(next,media,animation,rate,out load))continue;
                    if(fixedCue)return true;
                    if(load<bestLoad){bestLoad=load;bestY=y;ties=1;}
                    else if(load==bestLoad && Choose(++ties)==0)bestY=y;
                }
                // Exhaust a collision-safe group before adding an overlapping one.
                if(bestLoad<Int32.MaxValue){next.Y=bestY;return true;}
            }
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
                    float textWidth=measure(cue,size,appearance);
                    double life = Fixed(cue)?4.5:DanmakuTiming.BaseDuration(width,textWidth,dpiScale)*100/appearance.Speed;
                    DanmakuPlacement p = new DanmakuPlacement { Cue=cue, Appearance=appearance, Size=size, Width=textWidth,Height=DanmakuTiming.Height(cue,size),
                        CanvasWidth=width, CanvasHeight=height, DpiScale=dpiScale,ClipHeight=area, Start=appearance.DanmakuSpeedSync?media:animation, Life=life, Timed=true };
                    Geometry(p, width, height,dpiScale, measure); Position(p, media, animation);
                    if(Place(p,area,media,animation,rate)) active.Add(p);
                }
            }
            previous = Math.Max(previous, media);
            return new List<DanmakuPlacement>(active);
        }
    }
}

using System;
using System.Collections.Generic;
using BilibiliPotPlayer;

internal static class DanmakuWebPlaybackChecks
{
    static TextCue Cue(double start=0,int size=25,int mode=1,string text="same")
    { return new TextCue {Start=start,End=start+8,Size=size,Mode=mode,Text=text}; }
    static List<DanmakuPlacement> Frame(DanmakuPlayback playback,List<TextCue> cues,double time,CaptionOptions options,int width=800,int height=180,float dpi=1)
    { return playback.Frame(cues,time,width,height,options,(cue,size,style)=>cue.Text.Length*size,time,0,1,null,dpi); }
    static bool Touch(DanmakuPlacement a,DanmakuPlacement b)
    { return a.X<b.X+b.Width && b.X<a.X+a.Width && a.Y<b.Y+b.Height && b.Y<a.Y+a.Height; }
    internal static void Run(Action<bool,bool,string> equal)
    {
        Action<bool,string> check=(v,label)=>equal(true,v,label);
        check(Math.Abs(DanmakuTiming.BaseDuration(800,200,1)-7.100988100920287)<.000001,"web duration curve matches the 800 CSS pixel reference");
        check(Math.Abs(DanmakuTiming.BaseDuration(1280,400,1)-10.102228978938278)<.000001,"web duration curve matches the 1280 CSS pixel reference");
        check(Math.Abs(DanmakuTiming.BaseDuration(2560,400,1)-14.0991432122978)<.000001 && Math.Abs(DanmakuTiming.BaseDuration(3000,600,1)-16.1)<.000001,"large viewport durations preserve the web boundary branches");
        check(Math.Abs(DanmakuTiming.BaseDuration(3560,600,1)-18.099383151402407)<.000001,"viewports above 2560 pixels extend the duration range in 500 pixel increments");
        check(DanmakuTiming.BaseDuration(557,100,1)==4.5 && DanmakuTiming.BaseDuration(800,0,1)==4.5,"small or unmeasurable viewports use the configured web fallback duration");
        check(DanmakuTiming.BaseDuration(1200,300,1.5f)==DanmakuTiming.BaseDuration(800,200,1),"DPI conversion preserves duration for the same logical viewport and text widths");
        check(DanmakuTiming.BaseDuration(1280,4000,1)>DanmakuTiming.BaseDuration(1280,100,1),"text width participates in the duration curve instead of a single constant lifetime");
        var options=new CaptionOptions{Region=100,AvoidSubtitles=false,Density=3};
        var burst=new List<TextCue>();for(int i=0;i<30;i++)burst.Add(Cue());
        var live=new DanmakuPlayback();var frame=Frame(live,burst,0,options,320,90);
        check(frame.Count==9 && frame.FindAll(p=>p.Group==0).Count==3 && frame.FindAll(p=>p.Group==1).Count==3 && frame.FindAll(p=>p.Group==2).Count==3,"a saturated viewport admits three finite groups and rejects overflow");
        frame=Frame(live,burst,1,options,320,90);bool overlap=false,safe=true;
        for(int i=0;i<frame.Count;i++)for(int j=0;j<i;j++)if(Touch(frame[i],frame[j])){overlap=true;if(frame[i].Group==frame[j].Group)safe=false;}
        check(overlap && safe,"real overlap occurs across groups while every individual group remains collision-safe");
        var varied=new List<TextCue>();for(int i=0;i<160;i++)varied.Add(Cue(i*.035,12+i%4*8,i%5==0?6:1,i%7==0?"longer text\nsecond line":"word"));
        live.Reset();safe=true;
        for(int tick=0;tick<600;tick++) {
            frame=Frame(live,varied,tick*.016,options);
            for(int i=0;i<frame.Count;i++)for(int j=0;j<i;j++)
                if(frame[i].Group==frame[j].Group && Touch(frame[i],frame[j]))safe=false;
        }
        check(safe,"mixed widths, font sizes, multiline and opposing motion remain collision-safe within all three groups");
        options.Density=1;live.Reset();
        var boxes=new List<TextCue>{Cue(0,50,5),Cue(0,25,5),Cue(0,12,5)};
        frame=Frame(live,boxes,0,options,800,110);
        check(frame.Count==3 && Math.Abs(frame[1].Y-57.25)<.001 && Math.Abs(frame[2].Y-86.375)<.001,"smaller fixed text packs against larger box edges rather than inheriting a fixed-height grid");
        live.Reset();boxes=new List<TextCue>{Cue(0,25,5,"one\ntwo"),Cue(0,12,5),Cue(0,25,5)};
        frame=Frame(live,boxes,0,options,800,80);
        check(frame.Count==2 && frame[0].Height==56.25f && frame[1].Y>=57.25,"multiline footprints reserve their actual line count and do not overlap following text");
        live.Reset();var timed=new List<TextCue>{Cue(0),Cue(.05),Cue(.6)};
        Frame(live,timed,0,options);frame=Frame(live,timed,.05,options);
        check(frame.Count==2 && frame[0].Start==0 && frame[1].Start==.05 && timed[2].Start==.6,"similar text keeps its original timing instead of acquiring an invented aggregation window");
        double lifetime=frame[0].Life;options.Speed=160;options.Density=3;
        frame=Frame(live,timed,.05,options,1280);
        check(frame[0].Life==lifetime && frame[0].Group==0,"viewport and density edits preserve the lifetime and group of an admitted comment");
        frame=Frame(live,timed,.6,options,1280);var incoming=frame.Find(p=>p.Cue==timed[2]);
        check(incoming!=null && incoming.X==1280 && incoming.Life>6 && incoming.Life<7,"new entries capture their own viewport duration and user speed at the edge");
        live.Reset();options.Speed=40;frame=Frame(live,new List<TextCue>{Cue(0,25,5)},0,options);
        check(frame[0].Life==4.5,"fixed comments use their own web display duration independent of the scrolling speed control");
        options.ScaleWithScreen=true;options.Size=100;
        check(Math.Abs(DanmakuSettings.SizeFor(Cue(),900,options,1)-30.35)<.001 && Math.Abs(DanmakuSettings.SizeFor(Cue(),1350,options,1.5f)-45.525)<.001,"piecewise screen scaling uses logical height and preserves the same appearance across DPI");
        options.ScaleWithScreen=false;
        check(DanmakuSettings.SizeFor(Cue(),1350,options,1.5f)==37.5f,"disabled screen scaling retains the existing 25 logical pixel default");
    }
}

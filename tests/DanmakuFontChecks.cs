using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using BilibiliPotPlayer;

internal static class DanmakuFontChecks
{
    static Rectangle Ink(Bitmap bitmap)
    {
        int left=bitmap.Width,top=bitmap.Height,right=-1,bottom=-1;
        for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)if(bitmap.GetPixel(x,y).A>120)
        { left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y); }
        return right<0?Rectangle.Empty:Rectangle.FromLTRB(left,top,right+1,bottom+1);
    }
    internal static void Probe()
    {
        var options=new CaptionOptions { Region=100,AvoidSubtitles=false,Opacity=100,DanmakuOutline=3,DanmakuBold=false };
        using(var catalog=new CaptionCatalog(IntPtr.Zero,null,options,null,delegate {}))using(var scene=new AssScene())using(var bitmap=new Bitmap(400,160,PixelFormat.Format32bppPArgb))
        foreach(float size in new float[]{25,28,30,37.5f,42}) {
            var cue=new TextCue { Text="看见电影了" };
            scene.Render(bitmap,catalog,1000,new List<DanmakuPlacement>{new DanmakuPlacement{Cue=cue,X=10,Y=10,Size=size,Width=300}});
            Console.WriteLine(size+" ASS units: "+Ink(bitmap));
        }
    }
    internal static void Run(Action<bool,bool,string> equal)
    {
        Action<bool,string> check=(v,label)=>equal(true,v,label);
        var options=new CaptionOptions { Region=100,AvoidSubtitles=false,Opacity=100,DanmakuOutline=3 };
        var sources=new List<TextSource>();
        foreach(int authoredSize in new[]{25,36,64}) {
            string header=AssScene.Header(1280,720,options);
            TextSource source=TimedText.Parse(header+"Dialogue: 0,0:00:00.00,0:00:08.00,Danmaku,,0,0,0,,{\\an8\\pos(640,20)\\fs"+authoredSize+"}看见电影了\n","sized.ass",".ass")[0];
            source.Category=TextCategory.Danmaku; source.Enabled=true; sources.Add(source);
        }
        sources.Add(TimedText.Parse("<i><d p='0,5,25,16777215'>看见电影了</d></i>","web.xml",".xml")[0]);
        sources.Add(TimedText.Parse("[{\"time\":0,\"text\":\"看见电影了\",\"mode\":5,\"size\":25}]","web.json",".json")[0]);
        foreach(TextSource source in sources)
        using(var catalog=new CaptionCatalog(IntPtr.Zero,null,options,null,delegate {}))
        {
            source.Enabled=true; catalog.Sources.Add(source); var timeline=new List<TextCue>(); DanmakuLayout.Collect(catalog,timeline);
            var playback=new DanmakuPlayback(); var frame=playback.Frame(timeline,0,1280,720,options,(cue,size,style)=>size*5,0);
            check(frame.Count==1 && Math.Abs(frame[0].Size-25)<.001,"100 percent normalizes "+source.Title+" to the 25 logical pixel web baseline");
        }
        var ordinary=new TextSource {Category=TextCategory.Danmaku,Enabled=true,AssHeader="[Script Info]\n"};
        ordinary.Cues.Add(new TextCue{Start=0,End=8,Text="regular",Mode=5,Size=36});
        ordinary.Cues.Add(new TextCue{Start=0,End=8,Text="regular",Mode=5,Size=36});
        ordinary.Cues.Add(new TextCue{Start=0,End=8,Text="larger",Mode=5,Size=72});
        using(var catalog=new CaptionCatalog(IntPtr.Zero,null,options,null,delegate {}))
        {
            catalog.Sources.Add(ordinary); var timeline=new List<TextCue>(); DanmakuLayout.Collect(catalog,timeline);
            check(DanmakuSettings.SizeFor(ordinary.Cues[0],1080,options,1)==25 && DanmakuSettings.SizeFor(ordinary.Cues[2],1080,options,1)==50,"ASS normalization retains relative size differences without inheriting an oversized canvas baseline");
        }
        var cueText=new TextCue {Start=0,End=8,Text="看见电影了",Mode=5,Size=25};
        var cues=new List<TextCue>{cueText};
        using(var catalog=new CaptionCatalog(IntPtr.Zero,null,options,null,delegate {}))using(var scene=new AssScene())using(var bitmap=new Bitmap(400,180,PixelFormat.Format32bppPArgb))
        {
            bool simHei=Array.Exists(FontFamily.Families,f=>f.Name=="SimHei");
            Rectangle firstInk=Rectangle.Empty,secondInk=Rectangle.Empty;
            foreach(float dpi in new[]{1f,1.5f}) {
                var playback=new DanmakuPlayback(); var frame=playback.Frame(cues,0,400,180,options,(cue,size,style)=>size*5,0,0,1,null,dpi);
                scene.Render(bitmap,catalog,1000,frame,1); Rectangle ink=Ink(bitmap);
                bool reference=!simHei || ink.Width/dpi>117 && ink.Width/dpi<126 && ink.Height/dpi>21 && ink.Height/dpi<25;
                check(Math.Abs(frame[0].Size-25*dpi)<.001 && !ink.IsEmpty && reference,"native 100 percent glyphs use 25 logical pixels and match Firefox metrics when SimHei is installed at DPI scale "+dpi);
                if(dpi==1) firstInk=ink; else secondInk=ink;
            }
            check(Math.Abs(secondInk.Width/(double)firstInk.Width-1.5)<.06 && Math.Abs(secondInk.Height/(double)firstInk.Height-1.5)<.08,"native glyph dimensions track DPI even when the OS supplies a fallback font");
        }
        var live=new DanmakuPlayback(); var initial=live.Frame(cues,0,1280,720,options,(cue,size,style)=>size*5,0);
        var resized=live.Frame(cues,1,1920,1440,options,(cue,size,style)=>size*5,1);
        check(resized[0].Size==25,"default font size stays at 25 logical pixels when the viewport grows");
        foreach(float dpi in new[]{1f,1.5f}) {
            var rows=new DanmakuPlayback(); var simultaneous=new List<TextCue>{new TextCue{Start=0,Text="one",Mode=5},new TextCue{Start=0,Text="two",Mode=5},new TextCue{Start=0,Text="three",Mode=5}};
            var frame=rows.Frame(simultaneous,0,800,600,options,(cue,size,style)=>size*5,0,0,1,null,dpi);
            check(frame.Count==3 && Math.Abs(frame[1].Y-29.125f*dpi)<.01 && Math.Abs(frame[2].Y-58.25f*dpi)<.01,"ordinary spacing follows measured text height with logical pixel padding at DPI scale "+dpi);
        }
        var scaled=live.Frame(cues,1,1920,1440,options,(cue,size,style)=>size*5,1,0,1,null,1.5f);
        check(scaled[0].Size==37.5f,"moving an unsynchronized live comment to a different DPI preserves its logical size");
        options.ScaleWithScreen=true; live.Reset(); initial=live.Frame(cues,0,1280,720,options,(cue,size,style)=>size*5,0);
        resized=live.Frame(cues,1,1920,1440,options,(cue,size,style)=>size*5,1);
        check(initial[0].Appearance.ScaleWithScreen && Math.Abs(DanmakuSettings.SizeFor(cueText,720,options,1)-27)<.01 && Math.Abs(resized[0].Size-40)<.01,"explicit screen scaling follows the web's piecewise height curve during resize");
        string path=Path.Combine(Path.GetTempPath(),"font-baseline-"+Guid.NewGuid().ToString("N")+".json");
        try {
            File.WriteAllText(path,"{\"SettingsVersion\":3,\"Size\":85,\"ScaleWithScreen\":true,\"DanmakuBold\":false,\"DanmakuOutline\":1}");
            var loaded=CaptionOptions.Load(path);
            check(loaded.Size==100 && !loaded.ScaleWithScreen && !loaded.DanmakuBold && loaded.DanmakuOutline==1,"the temporary 85 percent preset migrates to the corrected 100 percent baseline");
        } finally { File.Delete(path); }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using BilibiliPotPlayer;

internal static class SubtitleMaskChecks
{
    static bool Gray(Color c) {return c.A==160 && Math.Abs(c.R-48)<2 && Math.Abs(c.G-48)<2 && Math.Abs(c.B-48)<2;}
    static Rectangle Background(Bitmap image)
    {
        int left=image.Width,top=image.Height,right=-1,bottom=-1;
        for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)if(Gray(image.GetPixel(x,y))) {left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);}
        return right<0?Rectangle.Empty:Rectangle.FromLTRB(left,top,right+1,bottom+1);
    }
    internal static void Run(Action<bool,bool,string> equal)
    {
        Action<bool,string> check=(value,label)=>equal(true,value,label);
        var options=new CaptionOptions {Region=100,AvoidSubtitles=true,Opacity=100,SubtitleSize=48,SubtitleBottom=7};
        using(var catalog=new CaptionCatalog(IntPtr.Zero,null,options,null,delegate {}))
        using(var scene=new AssScene())using(var baseline=new Bitmap(640,360,PixelFormat.Format32bppPArgb))using(var image=new Bitmap(640,360,PixelFormat.Format32bppPArgb))
        {
            var subtitle=new TextSource {Id="subtitle",Category=TextCategory.Subtitle,Loaded=true};
            subtitle.Cues.Add(new TextCue {Start=0,End=10,Text="SUBTITLE"});catalog.Sources.Add(subtitle);catalog.Slots.Main=subtitle.Id;catalog.Revision++;
            scene.Render(baseline,catalog,1000,new List<DanmakuPlacement>());Rectangle box=Background(baseline);
            check(!box.IsEmpty && box.Height<40 && box.Width<200,"subtitle avoidance draws a compact semi-transparent gray background around the actual one-line text");
            var cue=new TextCue {Text="MMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMM",Mode=1};
            var placement=new DanmakuPlacement {Cue=cue,X=box.Left-100,Y=box.Top+2,Size=25,Width=600};
            var comments=new List<DanmakuPlacement>{placement};scene.Render(image,catalog,1000,comments);
            bool hidden=true,outside=false;
            for(int y=box.Top;y<box.Bottom;y++)for(int x=0;x<640;x++) {
                if(box.Contains(x,y) && baseline.GetPixel(x,y).A>=150 && image.GetPixel(x,y)!=baseline.GetPixel(x,y))hidden=false;
                if(!box.Contains(x,y) && image.GetPixel(x,y).A>0)outside=true;
            }
            check(hidden && outside && comments.Count==1,"only comment pixels inside the subtitle background disappear; the same live instance remains visible on both sides");
            options.AvoidSubtitles=false;scene.Render(image,catalog,1000,comments);
            check(Background(image).IsEmpty,"disabling subtitle avoidance removes its gray background and temporary mask");
            options.AvoidSubtitles=true;scene.Render(image,catalog,11000,comments);
            check(Background(image).IsEmpty && image.GetPixel(box.Left+4,box.Top+12).A>0,"comments immediately reappear in the old background area when the subtitle ends");
            options.SubtitlesEnabled=false;scene.Render(image,catalog,1000,comments);
            check(Background(image).IsEmpty,"the subtitle switch removes the mask without deleting danmaku");
            options.SubtitlesEnabled=true;
            placement.Timed=true;placement.Start=0;placement.Life=8;placement.FromX=640;placement.ToX=-600;placement.Appearance=options.Snapshot();placement.ClipHeight=360;
            scene.Render(image,catalog,4000,comments,4);
            scene.Render(baseline,catalog,4000,new List<DanmakuPlacement>(),4);
            hidden=true;for(int y=box.Top;y<box.Bottom;y++)for(int x=box.Left;x<box.Right;x++)if(baseline.GetPixel(x,y).A>=150 && image.GetPixel(x,y)!=baseline.GetPixel(x,y))hidden=false;
            check(hidden && placement.FromX==640 && placement.ToX==-600 && placement.Start==0 && comments.Count==1,"a moving instance keeps its original trajectory and age while the gray background occludes it");
            catalog.Sources.Clear();catalog.Slots.Main="ass";
            string script=AssScene.Header(640,360,options)+"Dialogue: 0,0:00:00.00,0:00:10.00,Subtitle,,0,0,0,,{\\an8\\pos(320,50)}TOP\nDialogue: 0,0:00:00.00,0:00:10.00,Subtitle,,0,0,0,,{\\an2\\pos(320,310)}BOTTOM\n";
            var authored=TimedText.Parse(script,"captions.ass",".ass")[0];authored.Id="ass";authored.Category=TextCategory.Subtitle;catalog.Sources.Add(authored);catalog.Revision++;
            scene.Render(image,catalog,1000,new List<DanmakuPlacement>());
            bool top=false,bottom=false,middle=false;
            for(int y=0;y<360;y++)for(int x=0;x<640;x++)if(Gray(image.GetPixel(x,y))){if(y<100)top=true;else if(y>250)bottom=true;else middle=true;}
            check(top && bottom && !middle,"authored ASS captions produce separate local backgrounds rather than clearing the empty space between distant lines");
        }
    }
}

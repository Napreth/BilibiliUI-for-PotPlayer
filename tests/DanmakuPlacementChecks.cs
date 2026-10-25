using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Web.Script.Serialization;
using BilibiliPotPlayer;

internal static class DanmakuPlacementChecks
{
    static List<TextCue> Traffic(int count,double interval,bool weighted=false)
    {
        var cues=new List<TextCue>();
        for(int i=0;i<count;i++) cues.Add(new TextCue {Start=i*interval,End=i*interval+8,Text="comment "+i,Mode=1,Size=25,Weight=weighted?(int?)(i%3==0?2:3):null});
        return cues;
    }
    static List<DanmakuPlacement> Frame(DanmakuPlayback live,List<TextCue> cues,double time,CaptionOptions options,int height=600)
    { return live.Frame(cues,time,800,height,options,(cue,size,style)=>120+(int)(cue.Start*13)%7*28,time); }
    static bool Touch(DanmakuPlacement a,DanmakuPlacement b)
    { return a.X<b.X+b.Width && b.X<a.X+a.Width && a.Y<b.Y+b.Size*1.125f && b.Y<a.Y+a.Size*1.125f && a.X<800 && b.X<800 && a.X+a.Width>0 && b.X+b.Width>0; }
    static bool Collision(List<DanmakuPlacement> frame)
    { for(int i=0;i<frame.Count;i++) for(int j=0;j<i;j++) if(Touch(frame[i],frame[j]))return true; return false; }
    internal static void Preview(string directory)
    {
        Directory.CreateDirectory(directory); var cues=Traffic(2000,.0625,true);
        string[] words={"看见电影了！","666666","这是什么画质","太厉害了","手机也能看","？？？","前方高能","好清楚","来了来了","真的假的"};
        for(int i=0;i<cues.Count;i++) {cues[i].Text=words[i%words.Length]+" "+i;cues[i].Color=i%9==0?0xFF6688:i%11==0?0x66CCFF:0xFFFFFF;}
        var reports=new List<object>();
        foreach(int density in new[]{1,2,3}) {
            var options=new CaptionOptions{Region=100,AvoidSubtitles=false,Density=density}; var live=new DanmakuPlayback();
            using(var bitmap=new Bitmap(800,600,PixelFormat.Format32bppPArgb))
            using(var graphics=Graphics.FromImage(bitmap))
            using(var catalog=new CaptionCatalog(IntPtr.Zero,null,options,null,delegate {}))
            using(var scene=new AssScene()) {
                List<DanmakuPlacement> frame=null;
                for(int tick=0;tick<=500;tick++) frame=live.Frame(cues,tick*.016,800,600,options,(cue,size,style)=>{
                    using(var font=DanmakuSettings.FontFor(size,style))return graphics.MeasureString(cue.Text,font).Width;
                },tick*.016);
                scene.Render(bitmap,catalog,8000,frame,8);
                int pairs=0;for(int i=0;i<frame.Count;i++)for(int j=0;j<i;j++)if(Touch(frame[i],frame[j]))pairs++;
                bitmap.Save(Path.Combine(directory,"density-"+density+".png"),ImageFormat.Png);
                reports.Add(new {Density=density,Comments=frame.Count,IntersectingPairs=pairs,Width=800,Height=600,MediaSeconds=8});
            }
        }
        string json=new JavaScriptSerializer().Serialize(reports);File.WriteAllText(Path.Combine(directory,"comparison.json"),json);Console.WriteLine(json);
    }
    internal static void PreviewSource(string sourcePath,string subtitlePath,string directory)
    {
        Directory.CreateDirectory(directory);var reports=new List<object>();
        foreach(int density in new[]{1,2,3}) {
            var options=new CaptionOptions{Region=100,AvoidSubtitles=true,Density=density};var live=new DanmakuPlayback();
            using(var catalog=new CaptionCatalog(IntPtr.Zero,null,options,null,delegate {}))using(var scene=new AssScene())
            using(var bitmap=new Bitmap(2560,1600,PixelFormat.Format32bppPArgb))using(var graphics=Graphics.FromImage(bitmap)) {
                int id=0;foreach(var source in TimedText.Read(sourcePath)){source.Id="source-"+id++;source.Enabled=true;catalog.Sources.Add(source);}
                foreach(var source in TimedText.Read(subtitlePath)){source.Id="subtitle-"+id++;source.Category=TextCategory.Subtitle;catalog.Sources.Add(source);if(catalog.Slots.Main==null)catalog.Slots.Main=source.Id;}
                catalog.Revision++;var cues=new List<TextCue>();DanmakuLayout.Collect(catalog,cues);live.SetEnabledCues(cues);
                var widths=new Dictionary<TextCue,float>();Func<TextCue,float,CaptionOptions,float> measure=(cue,size,style)=>{
                    float width;if(!widths.TryGetValue(cue,out width)){using(var font=DanmakuSettings.FontFor(size,style))width=graphics.MeasureString(cue.Text,font).Width;widths[cue]=width;}return width;
                };
                var picture=new Rectangle(0,80,2560,1440);
                for(int tick=0;tick<=2240;tick++) {
                    double time=tick*.025;var frame=live.Frame(cues,time,2560,1600,options,measure,time,0,1,picture,1.5f,true);
                    if(tick==960 || tick==1600 || tick==2240) {
                        scene.Render(bitmap,catalog,(long)(time*1000),frame,time,0,picture,1.5f);
                        bitmap.Save(Path.Combine(directory,"density-"+density+"-at-"+time+".png"),ImageFormat.Png);
                        int fixedCount=0;float lowest=0;foreach(var p in frame){if(p.Cue.Mode==4 || p.Cue.Mode==5)fixedCount++;lowest=Math.Max(lowest,p.Y+p.Size*1.125f);}
                        reports.Add(new{Density=density,Time=time,Comments=frame.Count,Fixed=fixedCount,Scrolling=frame.Count-fixedCount,AreaHeight=DanmakuLayout.AreaHeight(1600,options,picture,true),Lowest=lowest});
                    }
                }
            }
        }
        string json=new JavaScriptSerializer().Serialize(reports);File.WriteAllText(Path.Combine(directory,"comparison.json"),json);Console.WriteLine(json);
    }
    internal static void Run(Action<bool,bool,string> equal)
    {
        Action<bool,string> check=(value,label)=>equal(true,value,label);
        var options=new CaptionOptions {Region=100,AvoidSubtitles=false};
        var cues=Traffic(48,.25); var live=new DanmakuPlayback();
        var visited=new HashSet<float>(); var order=new List<float>();
        int ascending=0,descending=0;
        for(int i=0;i<20;i++) {
            var frame=Frame(live,cues,i*.25,options); var entry=frame.Find(p=>p.Cue==cues[i]);
            if(entry!=null) { visited.Add(entry.Y); order.Add(entry.Y); }
        }
        for(int i=1;i<order.Count;i++) {if(order[i]>order[i-1])ascending++;if(order[i]<order[i-1])descending++;}
        check(visited.Count>=17 && order.Exists(y=>y>=540),"moderate traffic uses the full height before repeatedly occupying the upper screen");
        check(ascending>=5 && descending>=5,"successive scrolling entries are spatially mixed instead of forming a monotone diagonal");
        bool safe=true; int maxFrame=0;
        foreach(int density in new[]{1,2}) {
            options.Density=density; live.Reset(); var crowded=Traffic(300,.04,true);
            for(int tick=0;tick<500;tick++) {
                var frame=Frame(live,crowded,tick*.032,options,180);
                if(Collision(frame)) safe=false;
                maxFrame=Math.Max(maxFrame,frame.Count);
            }
        }
        check(safe && maxFrame>6,"Normal and More prevent actual rectangle intersections over time, including wider faster comments");
        var sparse=Traffic(160,.25); bool sparseSafe=true;
        options.Density=3; live.Reset();
        for(int tick=0;tick<1000;tick++) {
            var frame=Frame(live,sparse,tick*.032,options,1200);
            if(Collision(frame) || frame.Exists(p=>p.Group!=0)) sparseSafe=false;
        }
        check(sparseSafe,"Overlap retains one collision-safe group while the full-height area still has room");
        options.Region=75;live.Reset();safe=true;
        for(int tick=0;tick<300;tick++) if(Collision(Frame(live,sparse,tick*.032,options,240)))safe=false;
        check(safe,"a saved Overlap choice below full display area still enforces collision avoidance");
        options.Region=100; options.Density=1; var normalLive=new DanmakuPlayback();
        var moreLive=new DanmakuPlayback(); bool same=true;
        for(int i=0;i<40;i++) {
            options.Density=1;var normal=Frame(normalLive,sparse,i*.25,options);
            options.Density=2;var more=Frame(moreLive,sparse,i*.25,options);
            if(normal.Count!=more.Count) {same=false;break;}
            for(int j=0;j<normal.Count;j++) if(normal[j].Cue!=more[j].Cue || normal[j].X!=more[j].X || normal[j].Y!=more[j].Y || normal[j].Size!=more[j].Size || normal[j].Life!=more[j].Life)same=false;
        }
        check(same,"unscored Normal and More use identical geometry rather than manufacturing different diagonal spacing");
        options.Density=1; live.Reset(); var replay=Frame(live,cues,0,options);float originalY=replay[0].Y;
        live.Reset(); replay=Frame(live,cues,0,options);
        check(replay[0].Y==originalY,"spatial mixing is repeatable for the same playback sequence");
        var mixed=new List<TextCue>();for(int i=0;i<12;i++)mixed.Add(new TextCue{Start=0,End=8,Text="fixed "+i,Mode=i<6?5:4});
        mixed.Add(new TextCue{Start=0,End=8,Text="scroll",Mode=1});
        options.Density=1;live.Reset();var separated=Frame(live,mixed,0,options,180);
        check(separated.Count==13,"scrolling, top and bottom comments have separate collision pools instead of excluding each other's entire rows");
        options.Density=3;live.Reset();var fixedBurst=new List<TextCue>();for(int i=0;i<12;i++)fixedBurst.Add(new TextCue{Start=0,End=8,Text="fixed "+i,Mode=5});
        var fixedOverlap=Frame(live,fixedBurst,0,options,600);var fixedRows=new HashSet<float>();foreach(var p in fixedOverlap)fixedRows.Add(p.Y);
        check(fixedOverlap.Count==12 && fixedRows.Count==12 && fixedOverlap.TrueForAll(p=>p.Group==0),"fixed comments also fill their first safe group before overlapping");
    }
}

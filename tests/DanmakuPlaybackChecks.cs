using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using BilibiliPotPlayer;

internal static class DanmakuPlaybackChecks
{
    static byte[] Pixels(Bitmap bitmap)
    {
        BitmapData data=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
        try { byte[] pixels=new byte[Math.Abs(data.Stride)*data.Height]; Marshal.Copy(data.Scan0,pixels,0,pixels.Length); return pixels; }
        finally { bitmap.UnlockBits(data); }
    }
    static bool Same(byte[] a,byte[] b) { if(a.Length!=b.Length)return false; for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false; return true; }
    static TextCue Cue(double start,string text,int mode=1)
    { return new TextCue { Start=start,End=start+8,Text=text,Mode=mode,Size=20 }; }
    internal static void Run(Action<bool,bool,string> equal)
    {
        Action<bool,string> check=(value,label)=>equal(true,value,label);
        Func<TextCue,float,CaptionOptions,float> measure=(cue,size,style)=>80*size/20;
        var options=new CaptionOptions { Region=10,ScaleWithScreen=false,AvoidSubtitles=false,Opacity=100,Size=100 };
        var live=new DanmakuPlayback(); var first=Cue(0,"first"); var dropped=Cue(.1,"dropped"); var future=Cue(1,"future");
        var cues=new List<TextCue> { first,dropped,future };
        var frame=live.Frame(cues,0,320,180,options,measure,0);
        check(frame.Count==1 && frame[0].X==320,"ordinary scrolling admission starts beyond the right edge");
        frame=live.Frame(cues,.1,320,180,options,measure,.1);
        check(frame.Count==1 && frame[0].Cue==first,"occupied lanes reject a new event at admission");
        options.Region=100;
        frame=live.Frame(cues,.9,320,180,options,measure,.9);
        check(frame.Count==1 && frame[0].Cue==first,"expanding the region cannot revive a rejected comment in the middle");
        frame=live.Frame(cues,1.3,320,180,options,measure,1.3);
        DanmakuPlacement added=frame.Find(p=>p.Cue==future);
        check(added!=null && added.X==320 && added.Start==1.3,"a delayed frame admits a due event at its actual edge-entry time");
        frame=live.Frame(cues,1.316,320,180,options,measure,1.316);
        check(frame.Find(p=>p.Cue==future).X<320 && frame.Find(p=>p.Cue==future).X>318,"movement advances smoothly after edge admission");
        live.SetEnabledCues(new List<TextCue>());
        frame=live.Frame(new List<TextCue>(),1.4,320,180,options,measure,1.4);
        check(frame.Count==0,"disabling an ordinary source immediately removes its already admitted instances");
        live.Reset(); frame=live.Frame(cues,3,320,180,options,measure,3);
        check(frame.Count==0,"opening or loading a source cannot reconstruct historical comments mid-screen");
        live.Frame(new List<TextCue>(),4,320,180,options,measure,4);
        check(live.Frame(cues,5,320,180,options,measure,5).Count==0,"enabling a source cannot backfill past events");
        live.Reset(); cues=new List<TextCue> { Cue(0,"reverse",6) }; frame=live.Frame(cues,0,320,180,options,measure,0);
        check(frame.Count==1 && frame[0].X==-frame[0].Width,"reverse scrolling enters from beyond the left edge");
        frame=live.Frame(cues,.016,320,180,options,measure,.016);
        check(frame[0].X>-frame[0].Width,"reverse scrolling moves inward after edge admission");

        options=new CaptionOptions { Region=100,ScaleWithScreen=false,AvoidSubtitles=false,Opacity=100,Size=100 };
        using(var catalog=new CaptionCatalog(IntPtr.Zero,null,options,null,delegate {}))
        using(var scene=new AssScene()) using(var bitmap=new Bitmap(320,180,PixelFormat.Format32bppPArgb))
        {
            live.Reset(); cues=new List<TextCue> { Cue(0,"ONE"),Cue(0,"TWO"),Cue(0,"THREE") };
            live.Frame(cues,0,320,180,options,measure,0);
            frame=live.Frame(cues,1,320,180,options,measure,1);
            scene.Render(bitmap,catalog,1000,frame,1); byte[] before=Pixels(bitmap);
            float x=frame[0].X,y=frame[2].Y,size=frame[0].Size; double life=frame[0].Life;
            options.Region=10; options.Opacity=10; options.Size=170; options.Speed=160; options.DanmakuSpeedSync=true;
            options.DanmakuFont="SimSun"; options.DanmakuBold=false; options.DanmakuItalic=true; options.DanmakuUniformColor=true;
            options.DanmakuTextColor=0x0000FF; options.DanmakuOutline=2; options.DanmakuOutlineColor=0xFF0000;
            options.DanmakuBackground=true; options.DanmakuBackgroundColor=0x00FF00; options.ScaleWithScreen=true;
            options.AvoidSubtitles=true; catalog.Revision++;
            frame=live.Frame(cues,1,320,180,options,measure,1);
            scene.Render(bitmap,catalog,1000,frame,1);
            check(frame.Count==3 && !Same(before,new byte[320*180*4]) && Same(before,Pixels(bitmap)),"style, opacity, region and scaling edits leave existing ordinary glyphs pixel-identical");
            check(frame[0].X==x && frame[2].Y==y && frame[0].Size==size && frame[0].Life==life && frame[0].ClipHeight==180,"existing comments retain position, size, clipping region and lifetime");
            frame=live.Frame(cues,2,320,180,options,measure,1.5);
            check(frame.Count==3 && Math.Abs(frame[0].X-(x-25))<.01,"speed and speed-sync edits do not change an existing comment's movement clock");
            options.Scrolling=options.Colored=true; options.Region=100; options.AvoidSubtitles=false; options.ScaleWithScreen=false;
            cues.Add(Cue(3,"NEW STYLE"));
            frame=live.Frame(cues,3,320,180,options,measure,2);
            added=frame.Find(p=>p.Cue.Start==3);
            check(added!=null && added.X==320 && added.Appearance.Opacity==10 && added.Appearance.DanmakuFont=="SimSun" && added.Size==34 && added.Life==5,"new admissions capture edited style and speed while old comments retain their snapshots");
            scene.Render(bitmap,catalog,3000,frame,2);
            frame=live.Frame(cues,3.1,320,180,options,measure,2.05);
            check(Math.Abs(frame.Find(p=>p.Cue.Start==3).X-(320-(320+136)*.1/5))<.01,"mixed speed-sync policies advance on their captured clocks");
            options.DanmakuEnabled=false; catalog.Revision++;
            frame=live.Frame(cues,3.1,320,180,options,measure,2.05); scene.Render(bitmap,catalog,3100,frame,2.05);
            check(frame.Count==0 && Same(Pixels(bitmap),new byte[320*180*4]),"the master switch immediately clears all live ordinary pixels");
            options.DanmakuEnabled=true;
            check(live.Frame(cues,3.2,320,180,options,measure,2.1).Count==0,"re-enabling the master switch cannot resurrect cleared comments");
        }
        options=new CaptionOptions {Region=100,AvoidSubtitles=false}; live.Reset();
        var white=Cue(0,"white"); var colored=Cue(0,"color"); colored.Color=0xFF0000;
        var fixedCue=Cue(0,"fixed",5); var secondSource=Cue(0,"other source");
        cues=new List<TextCue>{white,colored,fixedCue,secondSource};
        live.SetEnabledCues(cues); frame=live.Frame(cues,0,640,360,options,measure,0);
        options.Colored=false; frame=live.Frame(cues,.1,640,360,options,measure,.1);
        check(frame.Count==3 && !frame.Exists(p=>p.Cue==colored),"the color filter immediately removes only source-colored live comments");
        options.Fixed=false; frame=live.Frame(cues,.2,640,360,options,measure,.2);
        check(frame.Count==2 && !frame.Exists(p=>p.Cue==fixedCue),"the fixed filter immediately removes live fixed comments");
        live.SetEnabledCues(new[]{white}); frame=live.Frame(cues,.3,640,360,options,measure,.3);
        check(frame.Count==1 && frame[0].Cue==white,"a source switch removes only instances belonging to the disabled stream");
        options.Scrolling=false; frame=live.Frame(cues,.4,640,360,options,measure,.4);
        check(frame.Count==0,"the scrolling filter immediately removes live scrolling comments");
        options.Scrolling=options.Fixed=options.Colored=true; live.SetEnabledCues(cues);
        check(live.Frame(cues,.5,640,360,options,measure,.5).Count==0,"re-enabling source and type switches never resurrects cleared instances");
        options=new CaptionOptions {Region=100,AvoidSubtitles=false}; live.Reset(); cues.Clear();
        for(int i=0;i<32;i++)cues.Add(Cue(i*.25,"short "+i));
        float lowest=0; bool steady=true;
        for(int i=0;i<32;i++) {
            frame=live.Frame(cues,i*.25,640,600,options,measure,i*.25);
            foreach(var p in frame) lowest=Math.Max(lowest,p.Y);
            if(frame.Count==0)steady=false;
        }
        check(steady && lowest>=540,"moderate scrolling traffic reaches the bottom of a full region instead of recycling only its upper lanes");
        check(DanmakuLayout.AreaHeight(600,new CaptionOptions{Region=100},null,false)==600,"subtitle avoidance reserves no empty band when no subtitle stream is selected");
        options=new CaptionOptions { Region=100,ScaleWithScreen=false,AvoidSubtitles=false };
        live.Reset(); cues=new List<TextCue> { Cue(0,"old"),Cue(10,"seek target"),Cue(11,"next") };
        live.Frame(cues,0,320,180,options,measure,0,1);
        frame=live.Frame(cues,10.5,320,180,options,measure,10.5,2);
        check(frame.Count==0,"seeking clears live state without inserting comments from before the target");
        frame=live.Frame(cues,11,320,180,options,measure,11,2);
        check(frame.Count==1 && frame[0].Cue.Start==11 && frame[0].X==320,"the next event after a seek enters from the edge");
        PlaybackClock clock=new PlaybackClock(); clock.Observe(1000,2,1,0); clock.Position(140);
        int epoch=clock.Epoch; clock.Observe(1000,1,1,140);
        check(clock.Position(1000)==1140 && clock.Epoch==epoch,"pausing on a stale coarse sample freezes the displayed frame without restarting admission");

        const string header="[Script Info]\nScriptType: v4.00+\nPlayResX: 320\nPlayResY: 180\n[V4+ Styles]\nFormat: Name,Fontname,Fontsize,PrimaryColour,SecondaryColour,OutlineColour,BackColour,Bold,Italic,Underline,StrikeOut,ScaleX,ScaleY,Spacing,Angle,BorderStyle,Outline,Shadow,Alignment,MarginL,MarginR,MarginV,Encoding\nStyle: Default,Arial,20,&H00FFFFFF,&H00FFFFFF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,7,0,0,0,1\n[Events]\nFormat: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text\n";
        using(var catalog=new CaptionCatalog(IntPtr.Zero,null,new CaptionOptions(),null,delegate {}))
        using(var scene=new AssScene()) using(var bitmap=new Bitmap(320,180,PixelFormat.Format32bppPArgb))
        {
            var source=TimedText.Parse(header+"Dialogue: 0,0:00:00.00,0:00:04.00,Default,,0,0,0,,{\\an7\\p1\\pos(200,100)}m 0 0 l 40 0 40 20 0 20\n","effect.ass",".ass")[0];
            source.Enabled=true; catalog.Sources.Add(source); catalog.Revision++;
            scene.Render(bitmap,catalog,0,new List<DanmakuPlacement>(),0,1); byte[] before=Pixels(bitmap);
            source.Enabled=false; catalog.Revision++;
            scene.Render(bitmap,catalog,1000,new List<DanmakuPlacement>(),1,1);
            check(Same(Pixels(bitmap),new byte[320*180*4]) && !Same(before,new byte[320*180*4]),"a source switch immediately removes an admitted authored effect");
            scene.Render(bitmap,catalog,4100,new List<DanmakuPlacement>(),4.1,1);
            check(Same(Pixels(bitmap),new byte[320*180*4]),"disabled advanced effects remain absent at their authored end time");
            source.Enabled=true; catalog.Options.AssEffectsEnabled=true; catalog.Revision++;
            scene.Render(bitmap,catalog,1000,new List<DanmakuPlacement>(),1,2);
            check(Same(Pixels(bitmap),new byte[320*180*4]),"re-enabling an advanced source after a seek cannot revive a historical effect");
            scene.Render(bitmap,catalog,0,new List<DanmakuPlacement>(),0,3);
            catalog.Options.AssEffectsEnabled=false; catalog.Revision++;
            scene.Render(bitmap,catalog,1000,new List<DanmakuPlacement>(),1,3);
            check(Same(Pixels(bitmap),new byte[320*180*4]),"the advanced-type filter independently removes admitted effects immediately");
            catalog.Options.AssEffectsEnabled=true; catalog.Revision++;
            scene.Render(bitmap,catalog,1000,new List<DanmakuPlacement>(),1,3);
            check(Same(Pixels(bitmap),new byte[320*180*4]),"re-enabling the advanced-type filter cannot resurrect a removed effect");
            scene.Render(bitmap,catalog,0,new List<DanmakuPlacement>(),0,4);
            catalog.Options.DanmakuEnabled=false; catalog.Revision++;
            scene.Render(bitmap,catalog,1000,new List<DanmakuPlacement>(),1,4);
            check(Same(Pixels(bitmap),new byte[320*180*4]),"the master danmaku switch immediately clears authored advanced effects too");
        }
    }
}

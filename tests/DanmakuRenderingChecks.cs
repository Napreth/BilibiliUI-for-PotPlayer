using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using BilibiliPotPlayer;

internal static class DanmakuRenderingChecks
{
    static byte[] Pixels(Bitmap bitmap)
    {
        BitmapData data = bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
        try { byte[] result = new byte[data.Stride * data.Height]; Marshal.Copy(data.Scan0,result,0,result.Length); return result; }
        finally { bitmap.UnlockBits(data); }
    }
    static Rectangle Ink(Bitmap bitmap)
    {
        byte[] pixels = Pixels(bitmap); int left=bitmap.Width, top=bitmap.Height, right=-1, bottom=-1;
        for(int y=0;y<bitmap.Height;y++) for(int x=0;x<bitmap.Width;x++) if(pixels[(y*bitmap.Width+x)*4+3]>4)
        { left=Math.Min(left,x); right=Math.Max(right,x); top=Math.Min(top,y); bottom=Math.Max(bottom,y); }
        return right<0 ? Rectangle.Empty : Rectangle.FromLTRB(left,top,right+1,bottom+1);
    }
    static int Alpha(Bitmap bitmap) { int maximum=0; byte[] pixels=Pixels(bitmap); for(int i=3;i<pixels.Length;i+=4) maximum=Math.Max(maximum,pixels[i]); return maximum; }
    static bool ColorInk(Bitmap bitmap, int channel)
    { byte[] pixels=Pixels(bitmap); for(int i=0;i<pixels.Length;i+=4) if(pixels[i+3]>30 && pixels[i+channel]>pixels[i+3]*.9) return true; return false; }
    internal static void Run(Action<bool,bool,string> equal)
    {
        Action<bool,string> check=(v,m)=>equal(true,v,m);
        CaptionOptions options=new CaptionOptions { Region=100, AvoidSubtitles=false, Opacity=100, DanmakuOutline=3, DanmakuTextColor=0xFF0000 };
        string header=AssScene.Header(320,180,options).Replace("[Script Info]","[Script Info]\nTitle: danmaku export");
        StringBuilder ass=new StringBuilder(header), xml=new StringBuilder("<i>"), json=new StringBuilder("{\"danmaku\":[");
        for(int i=0;i<12;i++)
        {
            ass.Append("Dialogue: 0,0:00:00.00,0:00:08.00,Danmaku,,0,0,0,,{\\an8\\pos(160,160)}MMMM ").Append(i).Append('\n');
            xml.Append("<d p='0,5,25,16711680'>MMMM ").Append(i).Append("</d>");
            if(i>0) json.Append(','); json.Append("{\"time\":0,\"mode\":5,\"color\":16711680,\"text\":\"MMMM ").Append(i).Append("\"}");
        }
        xml.Append("</i>"); json.Append("]}");
        string[] bodies={ass.ToString(),xml.ToString(),json.ToString()}, names={"comments.ass","comments.xml","comments.json"};
        for(int format=0;format<bodies.Length;format++)
        {
            TextSource source=TimedText.Parse(bodies[format],names[format],System.IO.Path.GetExtension(names[format]))[0];
            source.Id="ordinary"; source.Enabled=true;
            using(var catalog=new CaptionCatalog(IntPtr.Zero,null,options,null,delegate {})) using(var scene=new AssScene()) using(var bitmap=new Bitmap(320,180,PixelFormat.Format32bppPArgb))
            {
                catalog.Sources.Add(source); catalog.Revision++;
                var cues=new List<TextCue>(); DanmakuLayout.Collect(catalog,cues);
                check(source.Category==TextCategory.Danmaku && cues.Count==12,"ordinary "+names[format]+" reaches the configurable layout");
                options.Region=10; options.Opacity=100; options.Colored=options.Fixed=options.Scrolling=options.UseDanmakuColor=true; options.DanmakuUniformColor=false; options.ScaleWithScreen=false; options.Size=options.Speed=100;
                List<DanmakuPlacement> small=DanmakuTestFrames.At(cues,1,320,180,options,(cue,size)=>80);
                scene.Render(bitmap,catalog,1000,small); Rectangle narrow=Ink(bitmap);
                check(!narrow.IsEmpty && narrow.Bottom<=18,"ten-percent region clips actual "+names[format]+" glyphs to the selected band");
                options.Region=100; catalog.Revision++;
                List<DanmakuPlacement> full=DanmakuTestFrames.At(cues,1,320,180,options,(cue,size)=>80);
                scene.Render(bitmap,catalog,1000,full);
                check(full.Count>small.Count && Ink(bitmap).Bottom>narrow.Bottom+40,"expanding region reallocates visible "+names[format]+" rows");
                int opaque=Alpha(bitmap); options.Opacity=20; catalog.Revision++; full=DanmakuTestFrames.At(cues,1,320,180,options,(cue,size)=>80); scene.Render(bitmap,catalog,1000,full);
                check(opaque>240 && Alpha(bitmap)>30 && Alpha(bitmap)<60,"opacity changes actual "+names[format]+" pixel alpha without retaining authored opaque text");
                options.Opacity=100; options.DanmakuUniformColor=true; options.DanmakuTextColor=0x0000FF; catalog.Revision++; full=DanmakuTestFrames.At(cues,1,320,180,options,(cue,size)=>80); scene.Render(bitmap,catalog,1000,full);
                check(ColorInk(bitmap,0) && !ColorInk(bitmap,2),"uniform color reaches "+names[format]+" glyphs");
                options.UseDanmakuColor=false; catalog.Revision++; full=DanmakuTestFrames.At(cues,1,320,180,options,(cue,size)=>80); scene.Render(bitmap,catalog,1000,full);
                check(ColorInk(bitmap,0) && ColorInk(bitmap,1) && ColorInk(bitmap,2),"white-only style reaches "+names[format]+" glyphs");
                options.Colored=false;
                List<DanmakuPlacement> hidden=DanmakuTestFrames.At(cues,1,320,180,options,(cue,size)=>80); scene.Render(bitmap,catalog,1000,hidden);
                check(hidden.Count==0 && Ink(bitmap).IsEmpty,"source color filter still clears white-rendered "+names[format]+" comments");
                options.Colored=true; options.Fixed=false; catalog.Revision++; hidden=DanmakuTestFrames.At(cues,1,320,180,options,(cue,size)=>80); scene.Render(bitmap,catalog,1000,hidden);
                check(hidden.Count==0 && Ink(bitmap).IsEmpty,"fixed filter cannot leak a raw "+names[format]+" track behind the generated scene");
                options.Fixed=true; options.ScaleWithScreen=false; options.Size=50;
                float smaller=DanmakuTestFrames.At(cues,1,320,180,options,(cue,size)=>80)[0].Size;
                options.Size=170;
                float larger=DanmakuTestFrames.At(cues,1,320,180,options,(cue,size)=>80)[0].Size;
                check(larger>smaller*2,"font-size setting affects "+names[format]+" layout");
                options.ScaleWithScreen=true;
                check(DanmakuTestFrames.At(cues,1,320,90,options,(cue,size)=>80)[0].Size<larger,"scale-with-screen setting affects "+names[format]+" layout");
                if(format==0)
                {
                    options.Size=100; options.ScaleWithScreen=false;
                    check(DanmakuTestFrames.At(cues,1,320,180,options,(cue,size)=>80)[0].Size==25,"ordinary ASS uses the web-size baseline independently of the author's canvas height");
                    options.Size=170;
                }
                options.Region=10; options.ScaleWithScreen=false;
                var large=DanmakuTestFrames.At(cues,1,320,180,options,(cue,size)=>80); catalog.Revision++; scene.Render(bitmap,catalog,1000,large);
                check(large.Count>0 && !Ink(bitmap).IsEmpty && Ink(bitmap).Bottom<=18,"large font and a small region do not silently disable "+names[format]+" comments");
                source.Enabled=false; catalog.Revision++; DanmakuLayout.Collect(catalog,cues); scene.Render(bitmap,catalog,1000,new List<DanmakuPlacement>());
                check(cues.Count==0 && Ink(bitmap).IsEmpty,"source toggle clears both paths for "+names[format]);
            }
        }
        using(var catalog=new CaptionCatalog(IntPtr.Zero,null,new CaptionOptions { Region=100, AvoidSubtitles=false, ScaleWithScreen=false, Opacity=100 },null,delegate {})) using(var scene=new AssScene()) using(var bitmap=new Bitmap(320,180,PixelFormat.Format32bppPArgb))
        {
            var cues=new List<TextCue> { new TextCue { Start=0,End=8,Mode=1,Text="SMOOTH",Size=25 } };
            byte[] previous=null; bool moving=true;
            for(int frame=0;frame<20;frame++)
            {
                long now=2000+frame*16;
                var placements=DanmakuTestFrames.At(cues,now/1000.0,320,180,catalog.Options,(cue,size)=>100);
                scene.Render(bitmap,catalog,now,placements); byte[] current=Pixels(bitmap);
                bool different=previous==null; if(previous!=null) for(int i=0;i<current.Length;i++) if(current[i]!=previous[i]) { different=true; break; }
                moving &= different && !Ink(bitmap).IsEmpty; previous=current;
            }
            check(moving,"ordinary libass move animation changes actual glyph pixels on every 16 ms frame");
            var same=DanmakuTestFrames.At(cues,2.304,320,180,catalog.Options,(cue,size)=>100);
            scene.Render(bitmap,catalog,2304,same); byte[] frozen=Pixels(bitmap); scene.Render(bitmap,catalog,2304,same); byte[] repeat=Pixels(bitmap);
            bool stable=true; for(int i=0;i<repeat.Length;i++) if(repeat[i]!=frozen[i]) { stable=false; break; }
            check(stable,"cached ordinary motion track freezes exactly at a paused timestamp");
            cues[0].Mode=6;
            var reverse1=DanmakuTestFrames.At(cues,2,320,180,catalog.Options,(cue,size)=>100); scene.Render(bitmap,catalog,2000,reverse1); int x1=Ink(bitmap).Left;
            var reverse2=DanmakuTestFrames.At(cues,2.5,320,180,catalog.Options,(cue,size)=>100); scene.Render(bitmap,catalog,2500,reverse2);
            check(Ink(bitmap).Left>x1+10,"reverse ordinary comments move smoothly toward the right");
        }
        using(var catalog=new CaptionCatalog(IntPtr.Zero,null,new CaptionOptions { Region=10, Opacity=10 },null,delegate {})) using(var scene=new AssScene()) using(var bitmap=new Bitmap(320,180,PixelFormat.Format32bppPArgb))
        {
            TextSource advanced=TimedText.Parse(header+"Dialogue: 0,0:00:00.00,0:00:08.00,Danmaku,,0,0,0,,{\\an7\\p1\\pos(200,100)}m 0 0 l 40 0 40 20 0 20\n","effect.ass",".ass")[0];
            advanced.Id="effect"; advanced.Enabled=true; catalog.Sources.Add(advanced); catalog.Revision++;
            var cues=new List<TextCue>(); DanmakuLayout.Collect(catalog,cues); scene.Render(bitmap,catalog,1000,new List<DanmakuPlacement>());
            check(cues.Count==0 && Ink(bitmap).Left==200 && Ink(bitmap).Top==100,"advanced ASS stays on its authored coordinate path instead of becoming ordinary text");
        }
    }
}

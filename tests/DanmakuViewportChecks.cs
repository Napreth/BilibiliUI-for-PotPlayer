using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using BilibiliPotPlayer;

internal static class DanmakuViewportChecks
{
    static byte[] Pixels(Bitmap bitmap,Rectangle bounds)
    {
        BitmapData data=bitmap.LockBits(bounds,ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
        try {
            byte[] pixels=new byte[bounds.Width*bounds.Height*4];
            for(int row=0;row<bounds.Height;row++) Marshal.Copy(IntPtr.Add(data.Scan0,row*data.Stride),pixels,row*bounds.Width*4,bounds.Width*4);
            return pixels;
        } finally { bitmap.UnlockBits(data); }
    }
    static bool Same(byte[] a,byte[] b) { if(a.Length!=b.Length)return false; for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false; return true; }
    static int Ink(Bitmap bitmap,Rectangle bounds,bool dark=false)
    { int count=0; byte[] pixels=Pixels(bitmap,bounds); for(int i=0;i<pixels.Length;i+=4)if(pixels[i+3]>32 && (!dark || pixels[i]<50 && pixels[i+1]<50 && pixels[i+2]<50))count++; return count; }
    internal static void Run(Action<bool,bool,string> equal)
    {
        Action<bool,string> check=(value,label)=>equal(true,value,label);
        CaptionOptions options=new CaptionOptions();
        check(options.Size==100 && !options.ScaleWithScreen && !options.DanmakuBold && options.DanmakuOutline==1,"new danmaku defaults use the web-size regular-weight outline preset");
        string path=Path.Combine(Path.GetTempPath(),"danmaku-style-"+Guid.NewGuid().ToString("N")+".json");
        try {
            File.WriteAllText(path,"{\"SettingsVersion\":2,\"Size\":100,\"DanmakuBold\":true,\"DanmakuOutline\":0,\"DanmakuFont\":\"SimHei\",\"Region\":75,\"Opacity\":63}");
            options=CaptionOptions.Load(path);
            check(options.Size==100 && !options.ScaleWithScreen && !options.DanmakuBold && options.DanmakuOutline==1 && options.Region==75 && options.Opacity==63,"old default-style presets adopt the web-size default without resetting unrelated options");
            File.WriteAllText(path,"{\"SettingsVersion\":2,\"Size\":130,\"DanmakuBold\":true,\"DanmakuOutline\":0}"); options=CaptionOptions.Load(path);
            check(options.Size==130 && options.DanmakuBold && options.DanmakuOutline==0,"a customized legacy style remains unchanged");
            File.WriteAllText(path,"{\"SettingsVersion\":3,\"Size\":100,\"DanmakuBold\":true,\"DanmakuOutline\":0}"); options=CaptionOptions.Load(path);
            check(options.Size==100 && options.DanmakuBold && options.DanmakuOutline==0,"an explicitly selected heavy preset in the current schema is retained");
        } finally { File.Delete(path); }
        options=new CaptionOptions { Region=100,AvoidSubtitles=false,Opacity=100 };
        var cue=new TextCue { Start=0,End=8,Text="OUTLINE",Size=36,CanvasHeight=720,DanmakuBaseSize=36 };
        var live=new DanmakuPlayback();
        var fresh=live.Frame(new List<TextCue>{cue},0,1280,720,options,(c,size,style)=>size*5,0);
        check(Math.Abs(fresh[0].Size-25)<.01,"one hundred percent uses 25 logical pixels instead of the author's ASS canvas scale");
        using(var catalog=new CaptionCatalog(IntPtr.Zero,null,options,null,delegate {})) using(var scene=new AssScene()) using(var bitmap=new Bitmap(320,180,PixelFormat.Format32bppPArgb)) {
            var comments=new List<DanmakuPlacement> { new DanmakuPlacement { Cue=cue,X=30,Y=30,Size=30,Width=180 } };
            Rectangle all=new Rectangle(0,0,320,180);
            scene.Render(bitmap,catalog,1000,comments); int light=Ink(bitmap,all,true);
            options.DanmakuOutline=0; catalog.Revision++; scene.Render(bitmap,catalog,1000,comments); int heavy=Ink(bitmap,all,true);
            options.DanmakuOutline=3; catalog.Revision++; scene.Render(bitmap,catalog,1000,comments); int none=Ink(bitmap,all,true);
            check(light>none+20 && heavy>light*1.5,"the regular outline retains a visible edge with substantially fewer dark pixels than heavy ink");
        }
        const string header="[Script Info]\nScriptType: v4.00+\nPlayResX: 320\nPlayResY: 180\n[V4+ Styles]\nFormat: Name,Fontname,Fontsize,PrimaryColour,SecondaryColour,OutlineColour,BackColour,Bold,Italic,Underline,StrikeOut,ScaleX,ScaleY,Spacing,Angle,BorderStyle,Outline,Shadow,Alignment,MarginL,MarginR,MarginV,Encoding\nStyle: Default,Arial,20,&H000000FF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,7,0,0,0,1\n[Events]\nFormat: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text\n";
        options=new CaptionOptions { Region=100,AvoidSubtitles=false,Opacity=100,SubtitleSize=64 };
        using(var catalog=new CaptionCatalog(IntPtr.Zero,null,options,null,delegate {})) using(var scene=new AssScene())
        using(var bitmap=new Bitmap(320,300,PixelFormat.Format32bppPArgb)) using(var reference=new Bitmap(320,180,PixelFormat.Format32bppPArgb)) {
            TextSource sub=TimedText.Parse(header+"Dialogue: 0,0:00:00.00,0:00:08.00,Default,,0,0,0,,{\\an7\\p1\\pos(10,140)}m 0 0 l 40 0 40 20 0 20\n","sub.ass",".ass")[0];
            sub.Id="sub"; sub.Category=TextCategory.Subtitle; catalog.Sources.Add(sub); catalog.Slots.Main=sub.Id; catalog.Revision++;
            Rectangle picture=new Rectangle(0,60,320,180);
            var comments=new List<DanmakuPlacement> { new DanmakuPlacement { Cue=cue,X=10,Y=2,Size=30,Width=180 },new DanmakuPlacement { Cue=cue,X=10,Y=265,Size=25,Width=150 } };
            scene.Render(bitmap,catalog,1000,comments,null,null,picture);
            check(Ink(bitmap,new Rectangle(0,0,320,60))>100 && Ink(bitmap,new Rectangle(0,240,320,60))>100,"ordinary danmaku can render in both top and bottom letterbox bars");
            check(bitmap.GetPixel(20,210).R>240 && bitmap.GetPixel(20,210).G<10 && bitmap.GetPixel(20,150).A==0,"authored ASS subtitle coordinates remain relative to the fitted video picture");
            scene.Render(reference,catalog,1000,new List<DanmakuPlacement>());
            check(Same(Pixels(reference,new Rectangle(0,0,320,180)),Pixels(bitmap,picture)),"partial-surface ASS composition preserves the exact subtitle pixels and adds only the picture offset");
            catalog.Slots.Main=null;
            var plain=new TextSource { Id="plain",Category=TextCategory.Subtitle,Loaded=true }; plain.Cues.Add(new TextCue {Start=0,End=8,Text="SUBTITLE"}); catalog.Sources.Add(plain); catalog.Slots.Main=plain.Id; catalog.Revision++;
            scene.Render(bitmap,catalog,1000,new List<DanmakuPlacement>(),null,null,picture); scene.Render(reference,catalog,1000,new List<DanmakuPlacement>());
            check(Ink(bitmap,picture)>0 && Same(Pixels(reference,new Rectangle(0,0,320,180)),Pixels(bitmap,picture)) && Ink(bitmap,new Rectangle(0,240,320,60))==0,"plain subtitles retain their existing video-relative scale and bottom margin");
        }
        using(var catalog=new CaptionCatalog(IntPtr.Zero,null,options,null,delegate {})) using(var scene=new AssScene()) using(var bitmap=new Bitmap(480,180,PixelFormat.Format32bppPArgb)) {
            var comments=new List<DanmakuPlacement> { new DanmakuPlacement { Cue=cue,X=3,Y=30,Size=20,Width=130 },new DanmakuPlacement { Cue=cue,X=425,Y=70,Size=20,Width=130 } };
            scene.Render(bitmap,catalog,1000,comments,null,null,new Rectangle(80,0,320,180));
            check(Ink(bitmap,new Rectangle(0,0,80,180))>40 && Ink(bitmap,new Rectangle(400,0,80,180))>40,"ordinary danmaku can cross both side pillarbox bars");
        }
        var avoid=new CaptionOptions {Region=100,SubtitleSize=32,SubtitleBottom=7};
        check(DanmakuLayout.AreaHeight(900,avoid,new Rectangle(0,150,1440,600))==900,"subtitle avoidance never shrinks the selected danmaku admission area");
    }
}

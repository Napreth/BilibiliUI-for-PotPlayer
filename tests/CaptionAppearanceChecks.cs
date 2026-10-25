using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using BilibiliPotPlayer;

internal static class CaptionAppearanceChecks
{
    internal static void Run(Action<bool,bool,string> equal)
    {
        Action<bool,string> check=(v,label)=>equal(true,v,label);
        var options=new CaptionOptions {Region=100,AvoidSubtitles=false,Opacity=100,DanmakuOutline=3};
        using(var catalog=new CaptionCatalog(IntPtr.Zero,null,options,null,delegate {}))using(var scene=new AssScene())
        using(var reference=new Bitmap(320,180,PixelFormat.Format32bppPArgb))using(var image=new Bitmap(320,180,PixelFormat.Format32bppPArgb))
        foreach(int mode in new[]{4,5})foreach(bool fixedSync in new[]{false,true})foreach(bool scrollSync in new[]{false,true})foreach(bool fixedFirst in new[]{false,true}) {
            CaptionOptions fixedStyle=options.Snapshot(),scrollStyle=options.Snapshot();fixedStyle.DanmakuSpeedSync=fixedSync;scrollStyle.DanmakuSpeedSync=scrollSync;
            var fixedComment=new DanmakuPlacement {Cue=new TextCue{Text="MMMM",Mode=mode,Color=0xFF0000},X=30,Y=30,Size=30,Width=120,Appearance=fixedStyle,ClipHeight=180};
            var scrolling=new DanmakuPlacement {Cue=new TextCue{Text="MMMM",Mode=1,Color=0x0000FF},X=30,Y=30,Size=30,Width=120,Appearance=scrollStyle,ClipHeight=180};
            scene.Render(reference,catalog,1000,new List<DanmakuPlacement>{fixedComment},.5);
            scene.Render(image,catalog,1000,fixedFirst?new List<DanmakuPlacement>{fixedComment,scrolling}:new List<DanmakuPlacement>{scrolling,fixedComment},.5);
            int core=0;bool above=true;
            for(int y=0;y<180;y++)for(int x=0;x<320;x++) {
                Color expected=reference.GetPixel(x,y);if(expected.A<250 || expected.R<240 || expected.B>10)continue;
                core++;Color actual=image.GetPixel(x,y);if(actual.R<240 || actual.B>10)above=false;
            }
            check(above && core>30,"fixed mode "+mode+" remains above scrolling for clocks "+fixedSync+"/"+scrollSync+" and source order "+fixedFirst);
        }
        var defaults=new CaptionOptions();
        check(defaults.SubtitleFont=="Microsoft YaHei" && defaults.SubtitleSize==48 && !defaults.SubtitleBold && defaults.SubtitleOutline==3 && defaults.DanmakuFont=="SimHei" && defaults.Size==100,"default subtitles use larger regular YaHei text without the danmaku outline preset");
        defaults.AvoidSubtitles=false;
        using(var catalog=new CaptionCatalog(IntPtr.Zero,null,defaults,null,delegate {}))using(var scene=new AssScene())using(var image=new Bitmap(640,1080,PixelFormat.Format32bppPArgb)) {
            var subtitle=new TextSource{Id="subtitle",Category=TextCategory.Subtitle,Loaded=true};subtitle.Cues.Add(new TextCue{Start=0,End=2,Text="字幕 Subtitle"});catalog.Sources.Add(subtitle);catalog.Slots.Main=subtitle.Id;catalog.Revision++;
            scene.Render(image,catalog,1000,new List<DanmakuPlacement>{new DanmakuPlacement{Cue=new TextCue{Mode=1,Text="字幕 Subtitle",Color=0xFF0000},X=20,Y=100,Size=25,Width=300}});
            int whiteTop=1080,whiteBottom=-1,redTop=1080,redBottom=-1;
            for(int y=0;y<1080;y++)for(int x=0;x<640;x++) {Color c=image.GetPixel(x,y);if(c.A<160)continue;if(c.R>220 && c.G>220 && c.B>220){whiteTop=Math.Min(whiteTop,y);whiteBottom=Math.Max(whiteBottom,y);}else if(c.R>220 && c.G<30 && c.B<30){redTop=Math.Min(redTop,y);redBottom=Math.Max(redBottom,y);}}
            check(redBottom>redTop && whiteBottom-whiteTop>(redBottom-redTop)*1.45,"actual native subtitle glyphs are visibly larger than ordinary danmaku at the 1080-pixel reference height");
        }
        string path=Path.Combine(Path.GetTempPath(),"caption-appearance-"+Guid.NewGuid().ToString("N")+".json");
        try {
            File.WriteAllText(path,"{\"SettingsVersion\":4,\"SubtitleSize\":32,\"SubtitleFont\":\"Microsoft YaHei UI\",\"SubtitleOutline\":1,\"Region\":75,\"Opacity\":63}");
            var migrated=CaptionOptions.Load(path);
            check(migrated.SubtitleSize==48 && migrated.SubtitleFont=="Microsoft YaHei" && migrated.SubtitleOutline==3 && migrated.Region==75 && migrated.Opacity==63,"legacy untouched subtitle defaults adopt the web preset while other settings survive");
            File.WriteAllText(path,"{\"SettingsVersion\":2,\"SubtitleSize\":32}");migrated=CaptionOptions.Load(path);
            check(migrated.SubtitleSize==48 && migrated.SubtitleOutline==3,"legacy files that omitted the old default font and outline also migrate");
            File.WriteAllText(path,"{\"SubtitleSize\":40,\"SubtitleFont\":\"SimSun\",\"SubtitleOutline\":2,\"SubtitleBold\":true}");migrated=CaptionOptions.Load(path);
            check(migrated.SubtitleSize==40 && migrated.SubtitleFont=="SimSun" && migrated.SubtitleOutline==2 && migrated.SubtitleBold,"custom subtitle typography remains unchanged");
            migrated.SubtitleSize=32;migrated.SubtitleFont="Microsoft YaHei UI";migrated.SubtitleOutline=1;migrated.SubtitleBold=false;migrated.Save(path);migrated=CaptionOptions.Load(path);
            check(migrated.SubtitleSize==32 && migrated.SubtitleFont=="Microsoft YaHei UI" && migrated.SubtitleOutline==1,"choosing the former appearance explicitly is not repeatedly migrated on later launches");
        } finally {File.Delete(path);}
    }
}

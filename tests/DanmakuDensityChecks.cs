using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using BilibiliPotPlayer;

internal static class DanmakuDensityChecks
{
    static TextCue Cue(string text,int? weight=null,double start=0)
    { return new TextCue {Text=text,Start=start,End=start+8,Mode=5,Weight=weight}; }
    static byte[] Pixels(Bitmap image)
    {
        var data=image.LockBits(new Rectangle(0,0,image.Width,image.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
        try { byte[] pixels=new byte[data.Stride*data.Height];Marshal.Copy(data.Scan0,pixels,0,pixels.Length);return pixels; }
        finally {image.UnlockBits(data);}
    }
    static bool Same(byte[] a,byte[] b) { for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true; }
    static List<DanmakuPlacement> Frame(DanmakuPlayback live,List<TextCue> cues,double time,CaptionOptions options,int height=90)
    { return live.Frame(cues,time,320,height,options,(cue,size,style)=>100,time); }
    internal static void Run(Action<bool,bool,string> equal)
    {
        Action<bool,string> check=(v,label)=>equal(true,v,label);
        var options=new CaptionOptions {Region=100,AvoidSubtitles=false,Opacity=100};
        var weighted=new List<TextCue>{Cue("low",1),Cue("more",2),Cue("normal",3),Cue("unscored")};
        foreach(int density in new[]{1,2,3}) {
            options.Density=density; var frame=Frame(new DanmakuPlayback(),weighted,0,options,180);
            check(frame.Count==(density==1?2:3) && !frame.Exists(p=>p.Cue.Text=="low") && frame.Exists(p=>p.Cue.Text=="unscored"),"density "+density+" applies the Bilibili weight gate and retains unscored comments");
        }
        check(DanmakuDensity.Allows(Cue("negative",-3),new CaptionOptions {Region=100,Density=1}) && !DanmakuDensity.Allows(Cue("negative",-2),new CaptionOptions {Region=100,Density=1}),"weight filtering uses absolute scores like the player");
        options.Density=3; options.Region=75;
        check(!DanmakuDensity.Overlap(options) && !DanmakuDensity.Allows(Cue("weight two",2),options),"a partial display region always uses the normal non-overlap policy");
        options.Region=100;
        var burst=new List<TextCue>(); for(int i=0;i<18;i++)burst.Add(Cue("burst "+i));
        options.Density=1; var normal=Frame(new DanmakuPlayback(),burst,0,options);
        options.Density=2; var more=Frame(new DanmakuPlayback(),burst,0,options);
        options.Density=3; var overlap=Frame(new DanmakuPlayback(),burst,0,options);
        check(normal.Count==3 && more.Count==normal.Count,"unscored sources are not randomly filtered to manufacture a difference between Normal and More");
        check(overlap.Count==burst.Count && overlap.Exists(p=>p.Y==0) && overlap.Exists(p=>p.Y==30) && overlap.Exists(p=>p.Y==60),"Overlap admits crowded events and distributes overflow across the available rows");
        options.Region=75; check(Frame(new DanmakuPlayback(),burst,0,options,120).Count==3,"a remembered Overlap choice is inactive below one hundred percent region");
        options.Region=100; options.Density=1; var live=new DanmakuPlayback(); Frame(live,burst,0,options);
        using(var catalog=new CaptionCatalog(IntPtr.Zero,null,options,null,delegate {}))using(var scene=new AssScene())using(var bitmap=new Bitmap(320,90,PixelFormat.Format32bppPArgb)) {
            var frame=Frame(live,burst,.5,options);scene.Render(bitmap,catalog,500,frame,.5);byte[] before=Pixels(bitmap);
            options.Density=3;catalog.Revision++;frame=Frame(live,burst,.5,options);scene.Render(bitmap,catalog,500,frame,.5);
            check(frame.Count==3 && Same(before,Pixels(bitmap)) && frame.TrueForAll(p=>p.Appearance.Density==1),"density changes leave paused in-flight comment pixels and appearance snapshots unchanged");
            for(int i=0;i<9;i++)burst.Add(Cue("new "+i,null,1));
            frame=Frame(live,burst,1,options);
            check(frame.Count==12 && frame.FindAll(p=>p.Cue.Start==1).TrueForAll(p=>p.Appearance.Density==3),"only new admissions use the newly selected overlap policy");
            options.Density=1;frame=Frame(live,burst,1,options);
            check(frame.Count==12,"returning to Normal lets already overlapping instances finish");
            check(Frame(live,burst,6.1,options).Count==0,"density changes never reconstruct old rejected or expired events");
        }
        options.Density=3;live.Reset();burst.Clear();for(int i=0;i<DanmakuPlayback.MaxActive+20;i++)burst.Add(Cue("bounded "+i));
        check(Frame(live,burst,0,options).Count==DanmakuPlayback.MaxActive,"overlap mode bounds native scene and live-state size for extreme bursts");
        var xml=TimedText.Parse("<i><d p='0,5,25,16777215' weight='2'>weighted</d><d p='1,5,25,16777215'>unknown</d></i>","weights.xml",".xml")[0];
        check(xml.Cues[0].Weight==2 && !xml.Cues[1].Weight.HasValue,"XML imports preserve optional weight attributes without inventing missing scores");
        var json=TimedText.Parse("{\"danmaku\":[{\"time\":0,\"mode\":5,\"text\":\"weighted\",\"weight\":-3},{\"time\":1,\"mode\":5,\"text\":\"invalid\",\"weight\":\"bad\"}]}","weights.json",".json")[0];
        check(json.Cues[0].Weight==-3 && !json.Cues[1].Weight.HasValue,"JSON imports preserve signed weights and keep invalid scores unscored");
        using(var catalog=new CaptionCatalog(IntPtr.Zero,null,new CaptionOptions{Region=100},null,delegate {})) {
            var source=new TextSource{Category=TextCategory.Advanced,Enabled=true,AssHeader="[Script Info]\n"};
            source.Events.Add(Cue("advanced early",2,0));source.Events.Add(Cue("advanced next",2,1));catalog.Sources.Add(source);catalog.Revision++;
            var advanced=new AdvancedDanmakuPlayback();advanced.Advance(catalog,0,1);
            check(advanced.Active.Count==0,"Normal also screens scored authored events before admission");
            catalog.Options.Density=2;catalog.Revision++;advanced.Advance(catalog,1,1);
            check(advanced.Active.Count==1 && advanced.Active[0].Cue.Start==1,"More admits newly due scored effects without reviving earlier rejected events");
            catalog.Options.Density=1;advanced.Advance(catalog,1,1);
            check(advanced.Active.Count==1,"density edits let already admitted authored effects finish");
        }
        string path=Path.Combine(Path.GetTempPath(),"density-"+Guid.NewGuid().ToString("N")+".json");
        try {
            options.Region=75; options.Density=3; options.Save(path);var loaded=CaptionOptions.Load(path);
            check(loaded.Density==3 && loaded.Region==75 && DanmakuDensity.Effective(loaded)==1,"density selection persists independently of display region");
            loaded.Region=100;check(DanmakuDensity.Overlap(loaded),"restoring the full region restores the saved density choice");
            loaded.ResetDanmaku();check(loaded.Density==1,"restoring danmaku defaults resets density to Normal");
            File.WriteAllText(path,"{\"SettingsVersion\":4}");check(CaptionOptions.Load(path).Density==1,"existing configurations without density default to Normal");
            options.Density=999;options.Clamp();check(options.Density==3,"out-of-range density values are clamped");
        } finally {File.Delete(path);}
        using(var catalog=new CaptionCatalog(IntPtr.Zero,null,new CaptionOptions(),null,delegate {}))using(var button=new SubtitleButton())using(var popup=new SubtitlePopup(button,catalog)) {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic; var paint=typeof(SubtitlePopup).GetMethod("OnPaint",flags);
            foreach(int region in new[]{75,100})using(var image=new Bitmap(620,CaptionDropAreas.HeightFor(new CaptionOptions{Region=region})))using(var graphics=Graphics.FromImage(image)) {
                catalog.Options.Region=region;popup.Size=image.Size;paint.Invoke(popup,new object[]{new PaintEventArgs(graphics,new Rectangle(Point.Empty,image.Size))});
                int choices=0;foreach(object hit in (IEnumerable)typeof(SubtitlePopup).GetField("hits",flags).GetValue(popup))if(hit.GetType().GetField("Tooltip",flags).GetValue(hit)!=null)choices++;
                check(choices==(region==100?3:0),"the rendered menu shows exactly three density hit targets only at region "+region);
            }
        }
    }
}

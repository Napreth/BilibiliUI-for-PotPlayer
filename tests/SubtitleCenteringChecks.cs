using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Web.Script.Serialization;
using BilibiliPotPlayer;

internal static class SubtitleCenteringChecks
{
    static Rectangle Bounds(Bitmap image,bool background)
    {
        int left=image.Width,top=image.Height,right=-1,bottom=-1;
        for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++) {
            Color c=image.GetPixel(x,y);
            bool found=background?c.A==160 && Math.Abs(c.R-48)<2 && Math.Abs(c.G-48)<2 && Math.Abs(c.B-48)<2:
                c.A>180 && c.R>190 && c.G>190 && c.B>190;
            if(found){left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);}
        }
        return right<0?Rectangle.Empty:Rectangle.FromLTRB(left,top,right+1,bottom+1);
    }
    static void Check(Action<bool,bool,string> equal,string directory)
    {
        if(directory!=null)Directory.CreateDirectory(directory);
        var reports=new List<object>();int index=0;
        foreach(string text in new[]{"曾经经过的街巷","Subtitle Agyp","第一行字幕\nSubtitle Agyp"})
        foreach(float dpi in new[]{1f,1.5f}) {
            var options=new CaptionOptions{AvoidSubtitles=true,SubtitleFont="Microsoft YaHei",SubtitleSize=48};
            using(var catalog=new CaptionCatalog(IntPtr.Zero,null,options,null,delegate {}))using(var scene=new AssScene())
            using(var image=new Bitmap((int)(640*dpi),(int)(360*dpi),PixelFormat.Format32bppPArgb)) {
                var source=new TextSource{Id="subtitle",Category=TextCategory.Subtitle,Loaded=true};
                source.Cues.Add(new TextCue{Start=0,End=5,Text=text});catalog.Sources.Add(source);catalog.Slots.Main=source.Id;catalog.Revision++;
                scene.Render(image,catalog,1000,new List<DanmakuPlacement>(),null,null,null,dpi);
                Rectangle box=Bounds(image,true),ink=Bounds(image,false);
                int above=ink.Top-box.Top,below=box.Bottom-ink.Bottom;
                if(equal!=null)equal(true,!box.IsEmpty && !ink.IsEmpty && above>=0 && below>=0 && Math.Abs(above-below)<=1,
                    "subtitle visible glyphs are vertically centered for "+text.Replace('\n','/')+" at DPI "+dpi+" (top "+above+", bottom "+below+")");
                if(directory!=null)image.Save(Path.Combine(directory,"caption-"+index+".png"),ImageFormat.Png);
                reports.Add(new{Text=text,Dpi=dpi,Box=box,Ink=ink,Top=above,Bottom=below});index++;
            }
        }
        if(directory!=null){string json=new JavaScriptSerializer().Serialize(reports);File.WriteAllText(Path.Combine(directory,"centering.json"),json);Console.WriteLine(json);}
    }
    internal static void Run(Action<bool,bool,string> equal){Check(equal,null);}
    internal static void Preview(string directory){Check(null,directory);}
}

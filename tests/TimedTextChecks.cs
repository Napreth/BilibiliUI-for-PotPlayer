using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using BilibiliPotPlayer;

internal static class TimedTextChecks
{
    internal static void Run(Action<bool, bool, string> equal)
    {
        Action<bool, string> check = (value, message) => equal(true, value, message);
        string srt = "1\n00:00:01,000 --> 00:00:05,200\n你好，世界\n\n2\n00:00:10,000 --> 00:00:15,000\nfunction is just subtitle text\n";
        TextSource source = TimedText.Parse(srt, "renamed.xml", ".xml")[0];
        check(source.Category == TextCategory.Uncertain && source.Cues.Count == 2, "content signature overrides misleading suffix but two unlabelled cues remain uncertain");
        check(source.Cues[0].Start == 1 && source.Cues[0].End == 5.2, "SRT timestamps retain millisecond precision");
        check(source.Cues[1].Text.StartsWith("function"), "spoken words are not treated as executable code");
        check(TimedText.Parse(srt.Replace("你好，世界", "&lt;hello&gt; &amp; world"), "x.srt", ".srt")[0].Cues[0].Text == "<hello> & world", "subtitle entities decode after markup removal");
        check(TimedText.Time("99:99:00") == -1 && TimedText.Time("00:00:99") == -1, "invalid clock fields rejected");
        check(TimedText.Time("NaN") == -1 && TimedText.Time("Infinity") == -1 && TimedText.Time("-1") == -1, "nonfinite or negative time rejected");
        check(TimedText.Time("1250ms") == 1.25 && TimedText.Time("10.5s") == 10.5, "TTML offset timestamps");
        string mixed = "<i><d p='0,1,25,16777215'>滚动</d><d p='1,4,25,16711680'>底部</d><d p='2,5,25,16777215'>顶部</d><d p='3,6,25,16777215'>反向</d><d p='4,7,25,1'>[animation]</d><d p='5,8,25,1'>script()</d><d p='6,9,25,1'>BAS</d></i>";
        List<TextSource> split = TimedText.Parse(mixed, "mixed.srt", ".srt");
        check(split.Count == 2, "mixed XML splits ordinary and advanced groups");
        check(split[0].Cues.Count == 4 && split[0].Category == TextCategory.Danmaku, "adjacent XML elements all retained");
        check(split[1].IntrinsicAdvanced && split[1].Cues.Count == 0 && split[1].Detail.StartsWith("3"), "advanced, script and BAS are counted without interpreting payload");
        check(split[0].Cues[1].Mode == 4 && split[0].Cues[1].Color == 0xFF0000, "basic positioning and color retained");
        bool dtdRejected = false;
        try { TimedText.Parse("<!DOCTYPE i [<!ENTITY x SYSTEM 'file:///secret'>]><i><d p='1,1,25,1'>&x;</d></i>", "x.xml", ".xml"); } catch (XmlException) { dtdRejected = true; }
        check(dtdRejected, "XML external entities are prohibited");
        check(TimedText.Parse("<i><d p='NaN,1,25,1'>bad</d><d p='2,1,25,1'>good</d></i>", "x.xml", ".xml")[0].Cues.Count == 1, "malformed danmaku time does not poison following entry");
        string ass = "[Script Info]\nTitle: anime\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:01.00,0:00:08.00,Default,,0,0,0,,{\\b1}a,b\\Nc\n";
        TextSource assSource = TimedText.Parse(ass, "x.ass", ".ass")[0];
        check(assSource.Category == TextCategory.Uncertain && assSource.Cues[0].Text == "a,b\nc", "one styled ASS event preserves text and remains uncertain");
        check(TimedText.Parse(ass.Replace("{\\b1}", "{\\p1}"), "x.ass", ".ass")[0].Category == TextCategory.Advanced, "drawing ASS is advanced even without provenance");
        string moving = ass.Replace("{\\b1}", "{\\move(640,20,-100,20)}");
        check(TimedText.Parse(moving + moving.Substring(moving.IndexOf("Dialogue:")) + moving.Substring(moving.IndexOf("Dialogue:")), "x.ass", ".ass")[0].Category == TextCategory.Uncertain, "moving ASS without provenance needs user classification");
        check(TimedText.Parse(ass.Replace("Title: anime", "Title: danmaku export"), "x.ass", ".ass")[0].Category == TextCategory.Danmaku, "declared simple danmaku ASS detected");
        check(TimedText.Parse(ass.Replace("Title: anime", "Title: danmaku export").Replace("{\\b1}", "{\\p1}"), "x.ass", ".ass")[0].IntrinsicAdvanced, "declared vector danmaku stays advanced");
        List<TextSource> json = TimedText.Parse("{\"danmaku\":[{\"time\":1,\"text\":\"normal\",\"mode\":1},{\"time\":2,\"text\":\"code\",\"mode\":8}]}", "x.json", ".json");
        check(json.Count == 2 && json[0].Cues.Count == 1 && json[1].IntrinsicAdvanced, "declared JSON separates scripts from simple comments");
        check(TimedText.Parse("[{\"time\":1,\"text\":\"ambiguous\"}]", "x.json", ".json")[0].Category == TextCategory.Uncertain, "generic time/text JSON is ambiguous");
        var biliSubtitle=TimedText.Parse("[{\"from\":7.211009,\"to\":9.95,\"location\":2,\"content\":\"subtitle\"}]","bili.json",".json")[0];
        check(biliSubtitle.Category==TextCategory.Subtitle && biliSubtitle.Cues.Count==1 && biliSubtitle.Cues[0].Start==7.211009 && biliSubtitle.Cues[0].End==9.95 && biliSubtitle.Cues[0].Text=="subtitle","Bilibili subtitle arrays preserve from/to timing and content instead of importing an empty uncertain source");
        var biliBody=TimedText.Parse("{\"body\":[{\"from\":1,\"to\":3,\"content\":\"first\"},{\"from\":4,\"to\":6,\"content\":\"second\"}]}","bili.json",".json")[0];
        check(biliBody.Category==TextCategory.Subtitle && biliBody.Cues.Count==2 && biliBody.Cues[1].Start==4,"Bilibili subtitle body wrappers retain adjacent timed entries");
        check(TimedText.Parse("[00:01.00]one\n[00:05.00]two", "x.lrc", ".lrc")[0].Cues[0].End == 5, "lyrics end at next timestamp");
        check(TimedText.Parse("<tt><body><div><p begin='1s' end='3s'>one</p><p begin='4s' dur='2s'>two</p></div></body></tt>", "x.ttml", ".ttml")[0].Cues.Count == 2, "adjacent TTML cues retained");
        SubtitleSlots slots = new SubtitleSlots(); slots.Select("a"); check(slots.Main == "a" && slots.Secondary == null, "single subtitle selects main");
        slots.Select("b"); check(slots.Main == "b" && slots.Secondary == null, "single mode replaces selection");
        slots.SetMulti(true); slots.Select("a"); check(slots.Main == "b" && slots.Secondary == "a", "bilingual fills secondary");
        slots.Select("c"); check(slots.Main == "b" && slots.Secondary == "c", "third subtitle replaces secondary only");
        slots.Select("b"); check(slots.Main == "c" && slots.Secondary == null, "clearing main promotes secondary");
        slots.Select("c"); check(slots.Main == null && slots.Secondary == null, "selected radio can close subtitles");
        slots.Select("a"); slots.Select("b"); slots.SetMulti(false); check(slots.Main == "a" && slots.Secondary == null, "leaving bilingual preserves main only");
        CaptionOptions options = new CaptionOptions { Region = -10, Size = 9999, Speed = 0, Opacity = 500, SubtitleSize = 1 }; options.Clamp();
        check(options.Region == 10 && options.Size == 170 && options.Speed == 40 && options.Opacity == 100 && options.SubtitleSize == 16, "persisted settings clamped before rendering");
        options = new CaptionOptions(); List<TextCue> timeline = new List<TextCue> { new TextCue { Start = 1, End = 9, Text = "one" } };
        List<DanmakuPlacement> frame = DanmakuTestFrames.At(timeline, 3, 1000, 1000, options, (cue, size) => 100);
        check(frame.Count == 1 && frame[0].X < 1000 && frame[0].X > 0, "ordinary comments move across viewport");
        check(DanmakuTestFrames.At(timeline, 0, 1000, 1000, options, (cue, size) => 100).Count == 0, "seek before comment hides it");
        check(DanmakuTestFrames.At(timeline, 10, 1000, 1000, options, (cue, size) => 100).Count == 0, "expired comments disappear after seek");
        check(DanmakuTestFrames.At(timeline, 3, 1000, 1000, options, (cue, size) => 100)[0].X == frame[0].X, "paused position gives stable comment placement");
        options.DanmakuEnabled = false; check(DanmakuTestFrames.At(timeline, 3, 1000, 1000, options, (cue, size) => 100).Count == 0, "global D toggle clears all ordinary comments");
        options.DanmakuEnabled = true; timeline[0].Mode = 8; check(DanmakuTestFrames.At(timeline, 3, 1000, 1000, options, (cue, size) => 100).Count == 0, "renderer refuses script modes even if misrouted");
        timeline[0].Mode = 5; options.Fixed = false; check(DanmakuTestFrames.At(timeline, 3, 1000, 1000, options, (cue, size) => 100).Count == 0, "fixed comment filter");
        options.Fixed = true; timeline[0].Color = 0xFF0000; options.Colored = false; check(DanmakuTestFrames.At(timeline, 3, 1000, 1000, options, (cue, size) => 100).Count == 0, "colored comment filter");
        check(DanmakuLayout.Active(source.Cues, 2).Count == 1 && DanmakuLayout.Active(source.Cues, 6).Count == 0, "subtitle start and end visibility");
        options = new CaptionOptions { Region = 100, AvoidSubtitles = false };
        timeline = new List<TextCue> { new TextCue { Start = 1, End = 9, Text = "fixed", Mode = 5 }, new TextCue { Start = 1, End = 9, Text = "scroll", Mode = 1, Size = 64 } };
        frame = DanmakuTestFrames.At(timeline, 3, 1000, 1000, options, (cue, size) => 100);
        check(frame.Count == 2 && frame[0].Y != frame[1].Y, "fixed and scrolling comments share nonoverlapping lanes");
        check(new CaptionOptions().Region == 10 && new CaptionOptions().UseDanmakuColor, "new danmaku defaults use ten percent area and original colors");
        check(DanmakuSettings.At(.124f, DanmakuSettings.Regions) == 10 && DanmakuSettings.At(.126f, DanmakuSettings.Regions) == 25, "region slider snaps at the midpoint between equal-spaced ticks");
        check(DanmakuSettings.At(.5f, DanmakuSettings.Regions) == 50 && DanmakuSettings.At(.99f, DanmakuSettings.Regions) == 100, "region slider has five exact values");
        check(DanmakuSettings.Snap(61, DanmakuSettings.Speeds) == 70 && DanmakuSettings.At(1, DanmakuSettings.Speeds) == 160, "speed settings use Bilibili's five factors");
        options = new CaptionOptions { Region = 100, Speed = 40 }; timeline = new List<TextCue> { new TextCue { Start = 1, Text = "colored", Color = 0xFF0000 } };
        check(DanmakuTestFrames.At(timeline, 10, 1000, 1000, options, (cue, size) => 100).Count == 1, "slowest speed keeps a scrolling comment for twenty media seconds at 1x");
        options.Speed = 160;
        check(DanmakuTestFrames.At(timeline, 10, 1000, 1000, options, (cue, size) => 100).Count == 0, "fastest speed expires the same comment after five seconds");
        options.Speed = 100; options.UseDanmakuColor = false;
        check(DanmakuSettings.ColorFor(timeline[0], options) == 0xFFFFFF && timeline[0].Color == 0xFF0000, "white-only rendering preserves the source color");
        check(DanmakuTestFrames.At(timeline, 3, 1000, 1000, options, (cue, size) => 100).Count == 1, "white-only mode keeps the colored comment visible");
        options.Colored = false;
        check(DanmakuTestFrames.At(timeline, 3, 1000, 1000, options, (cue, size) => 100).Count == 0, "color-type blocking remains independent of white-only rendering");
        options.Colored = true; float normalX = DanmakuTestFrames.At(timeline, 3, 1000, 1000, options, (cue, size) => 100)[0].X;
        float twiceX = DanmakuTestFrames.At(timeline, 3, 1000, 1000, options, (cue, size) => 100, 2)[0].X;
        check(twiceX > normalX, "without speed sync a 2x video does not double wall-time comment movement");
        options.DanmakuSpeedSync = true;
        check(DanmakuTestFrames.At(timeline, 3, 1000, 1000, options, (cue, size) => 100, 2)[0].X == normalX, "speed sync follows media time at 2x");
        options.SubtitleSize = 48; options.Classes["keep"] = 2; options.ResetDanmaku();
        check(options.Region == 10 && options.Opacity == 80 && options.Size == 100 && options.Speed == 100 && options.UseDanmakuColor && !options.DanmakuSpeedSync && !options.ScaleWithScreen && !options.DanmakuBold && options.DanmakuOutline==1, "restore defaults selects the web-size regular-weight outline preset without screen scaling");
        check(options.SubtitleSize == 48 && options.Classes["keep"] == 2, "restoring danmaku defaults preserves subtitles and classifications");
        foreach (string font in DanmakuSettings.Fonts)
        {
            options.DanmakuFont = font;
            using (System.Drawing.Font renderedFont = DanmakuSettings.FontFor(25, options)) check(renderedFont.Size == 25, "font selection is renderable, with a safe fallback if absent: " + font);
        }
        string settingFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Bilibili-caption-check-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            options.Region = 75; options.Opacity = 63; options.Size = 170; options.Speed = 130; options.UseDanmakuColor = false; options.DanmakuFont = "SimSun"; options.DanmakuBold = false; options.DanmakuOutline = 2; options.DanmakuSpeedSync = true; options.Fixed = false; options.Save(settingFile);
            CaptionOptions loaded = CaptionOptions.Load(settingFile);
            check(loaded.Region == 75 && loaded.Opacity == 63 && loaded.Size == 170 && loaded.Speed == 130, "all four slider settings survive a new load");
            check(!loaded.UseDanmakuColor && loaded.DanmakuFont == "SimSun" && !loaded.DanmakuBold && loaded.DanmakuOutline == 2 && loaded.DanmakuSpeedSync && !loaded.Fixed, "advanced settings and filters survive a new load");
            options.DanmakuUniformColor = true; options.DanmakuTextColor = 0x4472C4; options.DanmakuBackground = true; options.DanmakuBackgroundColor = 0x595959; options.DanmakuOutlineColor = 0xED7D31; options.DanmakuItalic = true;
            options.SubtitleFont = "SimSun"; options.SubtitleBold = true; options.SubtitleItalic = true; options.SubtitleBackgroundColor = 0x123456; options.SubtitleOutlineColor = 0xABCDEF; options.SubtitleOutline = 3; options.Save(settingFile);
            loaded = CaptionOptions.Load(settingFile);
            check(loaded.DanmakuUniformColor && loaded.DanmakuTextColor == 0x4472C4 && loaded.DanmakuBackground && loaded.DanmakuBackgroundColor == 0x595959 && loaded.DanmakuOutlineColor == 0xED7D31 && loaded.DanmakuItalic, "danmaku text background outline and italic survive reload");
            check(loaded.SubtitleFont == "SimSun" && loaded.SubtitleBold && loaded.SubtitleItalic && loaded.SubtitleOutline == 3 && loaded.SubtitleBackgroundColor == 0x123456 && loaded.SubtitleOutlineColor == 0xABCDEF, "subtitle typography and effects survive reload independently");
            loaded.UseDanmakuColor = true;
            check(DanmakuSettings.ColorFor(timeline[0], loaded) == 0x4472C4, "uniform text color overrides cue color when enabled");
            loaded.UseDanmakuColor = false;
            check(DanmakuSettings.ColorFor(timeline[0], loaded) == 0xFFFFFF, "disabling colored danmaku forces white even with a custom text color");
            options.ResetDanmaku();
            check(!options.DanmakuItalic && !options.DanmakuBackground && !options.DanmakuUniformColor && options.DanmakuTextColor == 0xFFFFFF && options.SubtitleOutlineColor == 0xABCDEF, "danmaku style reset keeps subtitle formatting");
            System.IO.File.WriteAllText(settingFile, "{\"Region\":50,\"Size\":200,\"Speed\":200,\"Classes\":{\"keep\":2}}");
            loaded = CaptionOptions.Load(settingFile);
            check(loaded.Region == 10 && loaded.Size == 170 && loaded.Speed == 160 && loaded.Classes["keep"] == 2 && loaded.UseDanmakuColor, "legacy defaults migrate with range limits and classifications intact");
        }
        finally { if (System.IO.File.Exists(settingFile)) System.IO.File.Delete(settingFile); }
    }
}

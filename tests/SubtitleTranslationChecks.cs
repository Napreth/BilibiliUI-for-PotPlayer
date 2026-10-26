using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using BilibiliPotPlayer;

internal static class SubtitleTranslationChecks
{
    sealed class Translator : ISubtitleTranslator
    {
        internal int Calls;
        internal string LastText, LastSource, LastTarget;
        internal Func<string, CancellationToken, string> Run;
        public string Translate(string text, string source, string target, CancellationToken cancellation)
        {
            LastText = text; LastSource = source; LastTarget = target; Interlocked.Increment(ref Calls);
            return Run == null ? "译文 " + text : Run(text, cancellation);
        }
    }
    static TextSource Source(string id, double start, double end, string text)
    {
        var source = new TextSource { Id = id, Title = id, Category = TextCategory.Subtitle, Loaded = true };
        source.Cues.Add(new TextCue { Start = start, End = end, Text = text }); return source;
    }
    internal static SubtitleTranslation Preview() { return new SubtitleTranslation(new Translator()); }
    static void Wait(Action tick, Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        do { tick(); if (done()) return; Thread.Sleep(2); } while (watch.ElapsedMilliseconds < 3000);
        throw new Exception("Translation test worker timed out");
    }
    internal static void Run(Action<bool, bool, string> equal)
    {
        Action<bool, string> check = (value, message) => equal(true, value, message);
        var options = new CaptionOptions();
        check(!options.TranslationEnabled && options.TranslationSource == "auto" && options.TranslationTarget == "zh-CN", "translation starts disabled with automatic detection and Simplified Chinese");
        string path = Path.Combine(Path.GetTempPath(), "translation-options-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{\"TranslationEnabled\":true,\"TranslationSource\":null,\"TranslationTarget\":\"auto\",\"Opacity\":67}");
            options = CaptionOptions.Load(path);
            check(options.TranslationEnabled && options.TranslationSource == "auto" && options.TranslationTarget == "zh-CN" && options.Opacity == 67, "invalid translation languages normalize without resetting unrelated settings");
            options.TranslationSource = "ja"; options.TranslationTarget = "en"; options.Save(path); options = CaptionOptions.Load(path);
            check(options.TranslationSource == "ja" && options.TranslationTarget == "en", "translation preferences round trip with caption settings");
        }
        finally { File.Delete(path); }
        options = new CaptionOptions();
        TextSource primary = Source("primary", 1, 4, "Hello");
        var translator = new Translator();
        using (var session = new SubtitleTranslation(translator))
        {
            session.Update("video", primary, options, 2, 0);
            check(translator.Calls == 0 && session.Track.Cues.Count == 0, "disabled translation never invokes the provider");
            options.TranslationEnabled = true;
            session.Update("video", null, options, 2, 0);
            primary.Bitmap = true; session.Update("video", primary, options, 2, 0);
            primary.Bitmap = false; primary.Loaded = false; session.Update("video", primary, options, 2, 0);
            check(translator.Calls == 0, "missing, bitmap and still-loading subtitles do not invoke translation");
            primary.Loaded = true;
            Wait(() => session.Update("video", primary, options, 2, 0), () => session.Track.Cues.Count > 0);
            check(translator.Calls == 1 && session.Track.Cues[0].Text == "译文 Hello" && translator.LastSource == "auto" && translator.LastTarget == "zh-CN", "active primary text is translated asynchronously using the selected language pair");
            check(session.Track.Cues[0].Start == 1 && session.Track.Cues[0].End == 4 && primary.Cues[0].Text == "Hello", "translation retains exact timing without modifying the original cue");
            int revision = session.Revision;
            session.Update("video", primary, options, 2, 0);
            check(translator.Calls == 1 && session.Revision == revision, "paused-frame updates reuse translations without repeated requests or renderer revisions");
            session.Update("video", primary, options, 4, 0);
            check(session.Track.Cues.Count == 0, "translation disappears exactly at the original cue end");
            session.Update("video", primary, options, 2, 1);
            check(translator.Calls == 1 && session.Track.Cues.Count == 1, "backward seeks reuse cached text at the original timestamp");
            options.SubtitlesEnabled = false; session.Update("video", primary, options, 2, 1);
            check(session.Track.Cues.Count == 0, "the subtitle master switch immediately clears translated text");
        }
        options.SubtitlesEnabled = true;
        using (var started = new ManualResetEvent(false)) using (var release = new ManualResetEvent(false)) using (var finished = new ManualResetEvent(false))
        {
            translator = new Translator { Run = (text, token) => { started.Set(); release.WaitOne(3000); finished.Set(); return "迟到的译文"; } };
            using (var session = new SubtitleTranslation(translator))
            {
                var watch = Stopwatch.StartNew(); session.Update("video", primary, options, 2, 0);
                check(watch.ElapsedMilliseconds < 100 && started.WaitOne(1000), "a blocked provider does not block the playback thread");
                session.Update("video", primary, options, 5, 1); release.Set();
                check(finished.WaitOne(1000), "the superseded provider can finish independently");
                session.Update("video", primary, options, 5, 1);
                check(session.Track.Cues.Count == 0, "a late result from before a seek cannot extend expired subtitles");
            }
        }
        using (var started = new ManualResetEvent(false)) using (var release = new ManualResetEvent(false))
        {
            translator = new Translator { Run = (text, token) => { if (text == "Hello") { started.Set(); release.WaitOne(3000); return "旧字幕"; } return "新字幕"; } };
            using (var session = new SubtitleTranslation(translator))
            {
                session.Update("one", primary, options, 2, 0); check(started.WaitOne(1000), "source-switch test starts the old request");
                var other = Source("other", 1, 4, "World"); session.Update("two", other, options, 2, 0); release.Set();
                Wait(() => session.Update("two", other, options, 2, 0), () => session.Track.Cues.Count == 1);
                check(session.Track.Cues[0].Text == "新字幕", "switching media and subtitle tracks rejects stale replies even if a provider ignores cancellation");
                options.TranslationTarget = "en";
                Wait(() => session.Update("two", other, options, 2, 0), () => session.Track.Cues.Count == 1);
                check(translator.LastTarget == "en" && translator.Calls >= 3, "changing the target language invalidates prior cached translations");
            }
        }
        options.TranslationTarget = "zh-CN";
        translator = new Translator();
        using (var session = new SubtitleTranslation(translator))
        {
            var future = Source("future", 10, 12, "Soon"); future.Cues.Add(new TextCue { Start = 40, End = 42, Text = "Too far" });
            Wait(() => session.Update("video", future, options, 0, 0), () => translator.Calls > 0);
            Wait(() => session.Update("video", future, options, 0, 0), () => session.Status == "翻译已就绪" || session.Status == "等待字幕");
            check(translator.Calls == 1 && translator.LastText == "Soon" && session.Track.Cues.Count == 0, "lookahead translates nearby text without rendering it early or uploading distant cues");
            session.Update("video", future, options, 10, 0);
            check(session.Track.Cues.Count == 1 && translator.Calls == 1, "prefetched translation appears at the cue start without a new request");
        }
        translator = new Translator { Run = (text, token) => { throw new TranslationException("翻译请求受限"); } };
        using (var session = new SubtitleTranslation(translator))
        {
            var source = Source("error", 1, 4, "Hello"); source.Cues.Add(new TextCue { Start = 5, End = 8, Text = "Next" });
            Wait(() => session.Update("video", source, options, 2, 0), () => session.Status == "翻译请求受限");
            for (int i = 0; i < 100; i++) session.Update("video", source, options, 2, 0);
            check(translator.Calls == 1 && session.Track.Cues.Count == 0 && source.Cues[0].Text == "Hello", "provider failure keeps original text and applies a global cooldown instead of hammering nearby cues");
            session.Retry(); Wait(() => session.Update("video", source, options, 2, 0), () => translator.Calls == 2);
            check(translator.Calls == 2, "explicit retry clears the failure cooldown");
        }
        foreach (bool ass in new[] { false, true }) Render(check, options, ass);
    }
    static void Render(Action<bool, string> check, CaptionOptions options, bool ass)
    {
        using (var catalog = new CaptionCatalog(IntPtr.Zero, null, options, null, delegate { }))
        using (var scene = new AssScene()) using (var image = new Bitmap(800, 600, PixelFormat.Format32bppPArgb))
        using (var original = new Bitmap(800, 600, PixelFormat.Format32bppPArgb))
        {
            TextSource source = Source("subtitle", 1, 4, "Original subtitle"); catalog.Sources.Add(source); catalog.Slots.Main = source.Id;
            if (ass)
            {
                source.AssHeader = CaptionClassificationChecks.Header;
                source.Events.Add(new TextCue { Start = 1, End = 4, Text = "Original subtitle", AssFields = "0,Default,,0,0,0,,{\\pos(960,1000)}Original subtitle" });
            }
            catalog.Translation = new SubtitleTranslation(new Translator { Run = (text, token) => "实时翻译" });
            Wait(() => catalog.Translation.Update("video", source, options, 2, 0), () => catalog.Translation.Track.Cues.Count == 1);
            options.TranslationEnabled = false; scene.Render(original, catalog, 2000, new List<DanmakuPlacement>());
            options.TranslationEnabled = true; scene.Render(image, catalog, 2000, new List<DanmakuPlacement>());
            int added = 0, changedOriginal = 0;
            for (int y = 0; y < 600; y++) for (int x = 0; x < 800; x++)
            {
                Color before = original.GetPixel(x, y), after = image.GetPixel(x, y);
                if (before.A == 0 && after.A != 0) added++;
                if (before.A != 0 && before != after) changedOriginal++;
            }
            check(added > 200 && changedOriginal == 0, "libass renders a translated row without moving or repainting " + (ass ? "authored ASS" : "plain") + " subtitle pixels");
            catalog.Translation.Update("video", source, options, 4, 0); scene.Render(image, catalog, 4000, new List<DanmakuPlacement>());
            bool empty = true;
            for (int y = 0; y < 600; y++) for (int x = 0; x < 800; x++) if (image.GetPixel(x, y).A != 0) empty = false;
            check(empty, "both original and translated " + (ass ? "ASS" : "plain") + " subtitle pixels clear at their original end time");
        }
    }
}

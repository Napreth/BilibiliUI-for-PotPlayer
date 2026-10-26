using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using BilibiliPotPlayer;

internal static class CaptionDiscoveryChecks
{
    sealed class Media : IMediaContext
    {
        internal string Path;
        public string SourcePath { get { return Path; } }
        public object Revision { get { return this; } }
        public IList<ContainerCaption> CaptionTracks { get { return new List<ContainerCaption>(); } }
        public bool CameraCandidate { get { return false; } }
        public double DisplayRatio { get { return 0; } }
    }
    static void Wait(CaptionCatalog catalog, Func<bool> pending)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (pending() && watch.ElapsedMilliseconds < 5000) { catalog.DrainCompleted(); Thread.Sleep(2); }
        catalog.DrainCompleted();
        if (pending()) throw new Exception("Caption discovery worker timed out");
    }
    internal static void Run(Action<bool, bool, string> equal)
    {
        Action<bool, string> check = (value, message) => equal(true, value, message);
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Bilibili-caption-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            const string xml = "<?xml version='1.0' encoding='UTF-8'?><i><d p='1,1,25,16777215'>scroll</d><d p='2,5,25,16777215'>fixed</d></i>";
            const string subtitle = "[{\"from\":1,\"to\":3,\"content\":\"lyric\"}]";
            const string conversation = "{\"title\":\"ChatGPT history\",\"mapping\":{\"a\":{\"message\":{\"content\":{\"parts\":[\"hello\"]}}}}}";
            string video = System.IO.Path.Combine(folder, "episode.01.mp4");
            File.WriteAllText(video, "");
            File.WriteAllText(System.IO.Path.Combine(folder, "EPISODE.01.XML"), xml);
            File.WriteAllText(System.IO.Path.Combine(folder, "episode.01.json"), subtitle);
            File.WriteAllText(System.IO.Path.Combine(folder, "episode.01.ass"), CaptionClassificationChecks.Header.Replace("[Script Info]", "[Script Info]\nTitle: danmaku export") + "Dialogue: 0,0:00:01.00,0:00:05.00,Default,,0,0,0,,comment\n");
            File.WriteAllText(System.IO.Path.Combine(folder, "episode.01.srt"), "invalid text");
            File.WriteAllText(System.IO.Path.Combine(folder, "episode.01.vtt"), "WEBVTT\n");
            string history = System.IO.Path.Combine(folder, "ChatGPT-history.json");
            File.WriteAllText(history, conversation);
            string other = System.IO.Path.Combine(folder, "other.xml");
            File.WriteAllText(other, xml);
            File.WriteAllText(System.IO.Path.Combine(folder, "episode.01.extra.xml"), xml);
            File.WriteAllText(System.IO.Path.Combine(folder, "episode.010.xml"), xml);
            // Unrelated files must not consume the scan budget ahead of matching sidecars.
            for (int i = 0; i < 260; i++) File.WriteAllText(System.IO.Path.Combine(folder, "unrelated-" + i + ".json"), conversation);
            var media = new Media { Path = video };
            using (var catalog = new CaptionCatalog(IntPtr.Zero, media, new CaptionOptions(), null, delegate { }))
            {
                catalog.Tick(0); Wait(catalog, () => catalog.Discovering);
                check(catalog.Sources.Count == 3, "automatic discovery includes only valid exact-name sidecars, excluding prefixes and unrelated JSON");
                check(catalog.Sources.TrueForAll(s => System.IO.Path.GetFileNameWithoutExtension(s.Path).Equals("episode.01", StringComparison.OrdinalIgnoreCase)), "automatic sidecars match the entire dotted video stem");
                TextSource comments = catalog.Sources.Find(s => s.Format == "Bilibili XML");
                check(comments != null && comments.Category == TextCategory.Danmaku && comments.Enabled && comments.Cues.Count == 2, "same-name XML is discovered and enabled alongside ASS and subtitle JSON");
                check(catalog.Sources.Exists(s => s.Title == "episode.01.ass" && s.Category == TextCategory.Danmaku && s.Enabled), "same-name ASS comments remain enabled");
                TextSource lyrics = catalog.Find(catalog.Slots.Main);
                check(lyrics != null && lyrics.Category == TextCategory.Subtitle && lyrics.Title == "episode.01.json", "same-name Bilibili subtitle JSON is selected as subtitles");
                catalog.Tick(3000); Wait(catalog, () => catalog.Discovering);
                check(catalog.Sources.Count == 3, "periodic discovery does not duplicate sources or add empty files");

                catalog.Import(other); Wait(catalog, () => catalog.Status == "正在读取文件…");
                check(catalog.Sources.Count == 4 && catalog.Sources.Exists(s => s.Path == other && s.Enabled), "manual import accepts a differently named valid XML");
                catalog.Import(history); Wait(catalog, () => catalog.Status == "正在读取文件…");
                check(catalog.Sources.Count == 4 && catalog.Status.StartsWith("无法读取："), "manual import rejects conversation JSON with a clear status and no uncertain source");

                File.WriteAllText(System.IO.Path.Combine(folder, "episode.02.mp4"), "");
                File.WriteAllText(System.IO.Path.Combine(folder, "episode.02.json"), conversation);
                File.WriteAllText(System.IO.Path.Combine(folder, "episode.02.xml"), "<i><d p='bad'>invalid</d></i>");
                media.Path = System.IO.Path.Combine(folder, "episode.02.mp4");
                catalog.Tick(6000); Wait(catalog, () => catalog.Discovering);
                check(catalog.Sources.Count == 0 && catalog.Slots.Main == null, "media changes clear imports and even exact-name JSON/XML without valid entries stay out of the menu");

                File.WriteAllText(System.IO.Path.Combine(folder, "episode.02.json"), xml);
                catalog.Tick(9000); Wait(catalog, () => catalog.Discovering);
                check(catalog.Sources.Count == 1 && catalog.Sources[0].Format == "Bilibili XML" && catalog.Sources[0].Enabled, "content identifies XML with a JSON extension and corrected files can be rediscovered");
            }
            string ambiguous = System.IO.Path.Combine(folder, "ambiguous.json");
            File.WriteAllText(ambiguous, "[{\"time\":1,\"text\":\"unlabelled timed text\"}]");
            TextSource uncertain = CaptionCatalog.ReadFile(ambiguous)[0];
            check(uncertain.Category == TextCategory.Uncertain && uncertain.Cues.Count == 1, "valid timed text with insufficient classification evidence remains available for manual classification");
            string advanced = System.IO.Path.Combine(folder, "advanced.xml");
            File.WriteAllText(advanced, "<i><d p='1,8,25,16777215'>script payload</d></i>");
            TextSource blocked = CaptionCatalog.ReadFile(advanced)[0];
            check(blocked.Category == TextCategory.Advanced && blocked.Events.Count == 1 && !CaptionCatalog.CanUse(blocked), "event validation retains unsupported advanced protocol sources without allowing playback");
        }
        finally { Directory.Delete(folder, true); }
    }
}

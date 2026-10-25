using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace BilibiliPotPlayer
{
    internal sealed class CaptionOptions
    {
        public bool Multilingual, DanmakuEnabled = true, Scrolling = true, Fixed = true, Colored = true, AvoidSubtitles = true, ScaleWithScreen, UseDanmakuColor = true;
        public bool AssEffectsEnabled = true;
        public int SettingsVersion = 4;
        public int Region = 10, Opacity = 80, Size = 100, Speed = 100;
        public int Density = 1;
        public int SubtitleSize = 48, SubtitleOpacity = 100, SubtitleColor = 0xFFFFFF, SubtitleBottom = 7;
        public int SubtitleStyleVersion = 1;
        public bool SubtitleBackground;
        public bool SubtitlesEnabled = true;
        public bool DanmakuSpeedSync, DanmakuBold;
        public string DanmakuFont = "SimHei";
        public int DanmakuOutline = 1;
        public string SubtitleFont = "Microsoft YaHei";
        public bool SubtitleBold, SubtitleItalic, DanmakuItalic, DanmakuUniformColor, DanmakuBackground;
        public int SubtitleOutline = 3, SubtitleOutlineColor, SubtitleBackgroundColor;
        public int DanmakuTextColor = 0xFFFFFF, DanmakuOutlineColor, DanmakuBackgroundColor;
        internal CaptionOptions Snapshot() { return (CaptionOptions)MemberwiseClone(); }
        internal void ResetDanmaku()
        { DanmakuEnabled = AssEffectsEnabled = Scrolling = Fixed = Colored = AvoidSubtitles = UseDanmakuColor = true; Region = 10; Density = 1; Opacity = 80; Size = Speed = 100; ScaleWithScreen = DanmakuSpeedSync = DanmakuBold = false; DanmakuFont = "SimHei"; DanmakuOutline = 1; DanmakuItalic = DanmakuUniformColor = DanmakuBackground = false; DanmakuTextColor = 0xFFFFFF; DanmakuOutlineColor = DanmakuBackgroundColor = 0; }

        public Dictionary<string, int> Classes = new Dictionary<string, int>();
        internal static CaptionOptions Load() { return Load(SettingsPath); }
        internal static CaptionOptions Load(string path)
        {
            try { if (File.Exists(path)) { string json = File.ReadAllText(path); JavaScriptSerializer serializer = new JavaScriptSerializer(); CaptionOptions value = serializer.Deserialize<CaptionOptions>(json); if (value != null) { Dictionary<string, object> fields = serializer.Deserialize<Dictionary<string, object>>(json); if (!fields.ContainsKey("SettingsVersion") && value.Region == 50) value.Region = 10;
                if ((!fields.ContainsKey("SettingsVersion") || value.SettingsVersion < 3) && value.Size == 100 && value.DanmakuBold && value.DanmakuOutline == 0 && value.DanmakuFont == "SimHei") { value.DanmakuBold = false; value.DanmakuOutline = 1; value.ScaleWithScreen = false; }
                if (value.SettingsVersion < 4 && (value.Size==85 || value.Size==100) && !value.DanmakuBold && value.DanmakuOutline==1 && value.DanmakuFont=="SimHei") { value.Size=100; value.ScaleWithScreen=false; }
                if(!fields.ContainsKey("SubtitleStyleVersion") && (!fields.ContainsKey("SubtitleSize") || value.SubtitleSize==32) && (!fields.ContainsKey("SubtitleFont") || value.SubtitleFont=="Microsoft YaHei UI") && (!fields.ContainsKey("SubtitleOutline") || value.SubtitleOutline==1) && value.SubtitleOutlineColor==0 && value.SubtitleColor==0xFFFFFF && value.SubtitleOpacity==100 && !value.SubtitleBold && !value.SubtitleItalic && !value.SubtitleBackground) {
                    value.SubtitleSize=48;value.SubtitleFont="Microsoft YaHei";value.SubtitleOutline=3;
                }
                value.Clamp(); return value; } } }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { } catch (InvalidOperationException) { }
            return new CaptionOptions();
        }
        internal void Clamp()
        {
            SettingsVersion = 4; Region = DanmakuSettings.Snap(Region, DanmakuSettings.Regions); Opacity = Math.Max(10, Math.Min(100, Opacity)); Size = Math.Max(50, Math.Min(170, Size)); Speed = DanmakuSettings.Snap(Speed, DanmakuSettings.Speeds);
            Density = Math.Max(1,Math.Min(3,Density));
            SubtitleSize = Math.Max(16, Math.Min(64, SubtitleSize)); SubtitleOpacity = Math.Max(10, Math.Min(100, SubtitleOpacity)); SubtitleBottom = Math.Max(2, Math.Min(30, SubtitleBottom)); SubtitleColor &= 0xFFFFFF;
            DanmakuOutline = Math.Max(0, Math.Min(3, DanmakuOutline)); if (Array.IndexOf(DanmakuSettings.Fonts, DanmakuFont) < 0) DanmakuFont = "SimHei";
            SubtitleOutline = Math.Max(0, Math.Min(3, SubtitleOutline));
            SubtitleOutlineColor &= 0xFFFFFF; SubtitleBackgroundColor &= 0xFFFFFF; DanmakuTextColor &= 0xFFFFFF; DanmakuOutlineColor &= 0xFFFFFF; DanmakuBackgroundColor &= 0xFFFFFF;
            if (Array.IndexOf(DanmakuSettings.Fonts, SubtitleFont) < 0) SubtitleFont = "Microsoft YaHei";
            if (Classes == null) Classes = new Dictionary<string, int>();
        }
        internal static string SettingsPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BilibiliPotPlayer", "caption-options.json"); } }
        internal void Save() { Save(SettingsPath); }
        internal void Save(string path)
        {
            Clamp();
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); string temporary = path + ".tmp"; File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false)); if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    internal sealed class CaptionCatalog : IDisposable
    {
        readonly IntPtr player;
        readonly IMediaContext streams;
        readonly Func<TextSource, List<TextCue>> load;
        readonly Action saveOptions;
        readonly object sync = new object();
        readonly Queue<Action> completed = new Queue<Action>();
        int generation;
        string path = "";
        object media;
        long nextScan;
        bool nativeCaptured, nativeWasVisible, nativeVisible, initialSelectionDone, captionControlActive;
        internal bool Discovering;
        internal string Status = "";
        internal readonly List<TextSource> Sources = new List<TextSource>();
        internal readonly SubtitleSlots Slots = new SubtitleSlots();
        internal readonly CaptionOptions Options;
        internal int Revision;
        internal double DisplayRatio { get { return streams == null || streams.CameraCandidate ? 0 : streams.DisplayRatio; } }
        internal string MediaPath { get { return path; } }
        internal CaptionCatalog(IntPtr player, IMediaContext streams) : this(player, streams, CaptionOptions.Load(), null, null) { }
        internal CaptionCatalog(IntPtr player, IMediaContext streams, CaptionOptions options, Func<TextSource, List<TextCue>> load, Action save)
        { this.player = player; this.streams = streams; Options = options; this.load = load; saveOptions = save ?? Options.Save; Slots.Multi = Options.Multilingual; }
        internal void DrainCompleted() { lock (sync) while (completed.Count > 0) completed.Dequeue()(); }
        static List<TextSource> LoadContainer(TextSource source)
        { return NativeTimedText.Read(source.Path,source.Stream,source.Title); }
        internal static bool CanUse(TextSource source)
        { return !source.Bitmap && ((!source.IntrinsicAdvanced && source.Category != TextCategory.Advanced) || source.AssRenderable) && (source.Error.Length == 0 || source.Stream >= 0); }
        internal void Tick(long now)
        {
            DrainCompleted();
            if (path != streams.SourcePath)
            {
                RestoreNative(); path = streams.SourcePath; media = null; generation++; Discovering = false; initialSelectionDone = captionControlActive = false; Sources.Clear(); Slots.Clear(); Status = ""; Revision++; nextScan = 0;
            }
            if (path.Length == 0 || !File.Exists(path)) return;
            if (media != streams.Revision)
            {
                media = streams.Revision;
                foreach (ContainerCaption track in streams.CaptionTracks)
                {
                    string id = Key(path) + ":stream:" + track.Ordinal;
                    if (Sources.Exists(s => s.Id == id)) continue;
                    Sources.Add(new TextSource { Id = id, Title = track.Title.Length > 0 ? track.Title : track.Language.Length > 0 ? track.Language : "字幕 " + (track.Ordinal + 1), Detail = track.Codec + (track.Language.Length > 0 ? " · " + track.Language : ""), Format = track.Codec, Path = path, Stream = track.Ordinal, Bitmap = track.Bitmap, Error = track.Bitmap ? "图像字幕沿用 PotPlayer 原生渲染，暂不在此切换" : "", Category = track.Bitmap ? TextCategory.Subtitle : TextCategory.Uncertain });
                    Revision++;
                }
                ApplyClasses(Sources);
                ChooseInitial();
            }
            if (now >= nextScan) { nextScan = now + 3000; Discover(); }
            if (!Sources.Exists(s => s.Stream >= 0 && s.Loading)) {
                TextSource pending = Sources.Find(s => s.Stream >= 0 && !s.Bitmap && !s.Loaded && s.Error.Length == 0);
                EnsureLoaded(pending);
            }
            TextSource main = Find(Slots.Main), secondary = Find(Slots.Secondary);
            EnsureLoaded(main); EnsureLoaded(secondary);
            if (Sources.Exists(s => !s.Converted && s.AssRenderable && s.Enabled && (s.Category == TextCategory.Advanced && Options.AssEffectsEnabled || s.Category == TextCategory.Danmaku && Options.DanmakuEnabled))) captionControlActive = true;
            if (captionControlActive || !Options.SubtitlesEnabled) SetNativeVisible(false);
        }
        void Discover()
        {
            if (Discovering) return;
            Discovering = true; string current = path; int version = generation;
            HashSet<string> existing = new HashSet<string>(); foreach (TextSource source in Sources) if (source.Stream < 0 && source.Path != null) existing.Add(source.Path);
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<TextSource> found = new List<TextSource>();
                try
                {
                    int count = 0;
                    foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(current)))
                    {
                        string extension = Path.GetExtension(file).ToLowerInvariant();
                        if (!SupportedExtension(extension) || existing.Contains(file)) continue;
                        if (++count > 256) break;
                        try { List<TextSource> parsed = ReadFile(file); string stem = Path.GetFileNameWithoutExtension(current); foreach (TextSource source in parsed) source.Enabled = (source.Category == TextCategory.Danmaku || source.Category == TextCategory.Advanced && source.AssRenderable) && (Path.GetFileNameWithoutExtension(file).Equals(stem,StringComparison.OrdinalIgnoreCase) || Path.GetFileName(file).StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase)); found.AddRange(parsed); }
                        catch (Exception error) { if (!(error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is System.Xml.XmlException || error is ArgumentException || error is InvalidOperationException || error is System.Text.RegularExpressions.RegexMatchTimeoutException)) throw; found.Add(new TextSource { Id = Key(file), Path = file, Title = Path.GetFileName(file), Category = TextCategory.Uncertain, Loaded = true, Error = error.Message, Detail = "读取失败" }); }
                    }
                }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
                lock (sync) completed.Enqueue(delegate { if (version != generation) return; Discovering = false; if(found.Count>0) { Sources.AddRange(found); ApplyClasses(found); ChooseInitial(); Revision++; } });
            });
        }
        internal static bool SupportedExtension(string extension)
        { return new HashSet<string> { ".srt", ".ass", ".ssa", ".vtt", ".lrc", ".xml", ".json", ".ttml", ".dfxp" }.Contains(extension.ToLowerInvariant()); }
        internal static List<TextSource> ReadFile(string file)
        {
            List<TextSource> sources = DanmakuFactory.Adapt(TimedText.Read(file));
            for (int i = 0; i < sources.Count; i++) { TextSource source = sources[i]; source.Path = file; source.Id = Key(file) + ":" + i; source.Enabled = source.Category == TextCategory.Danmaku && source.Cues.Count > 0 || source.Category == TextCategory.Advanced && source.AssRenderable; }
            return sources;
        }
        internal void Import(string file)
        {
            int version = generation; Status = "正在读取文件…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<TextSource> result = null; string error = "";
                try { result = ReadFile(file); } catch (Exception ex) { error = ex.Message; }
                lock (sync) completed.Enqueue(delegate
                {
                    if (version != generation) return;
                    if (result == null) { Status = "无法读取：" + error; return; }
                    foreach (TextSource source in result)
                    { TextSource existing = Find(source.Id); if (existing == null) Sources.Add(source); else if (existing.Error.Length>0 || source.Converted) Sources[Sources.IndexOf(existing)]=source; else if (existing.Category == TextCategory.Danmaku || existing.Category == TextCategory.Advanced && existing.AssRenderable) existing.Enabled = true; }
                    ApplyClasses(result); Status = "";
                    TextSource subtitle = result.Find(s => s.Category == TextCategory.Subtitle && s.Error.Length == 0); if (subtitle != null) { initialSelectionDone = captionControlActive = true; if (Slots.Main != subtitle.Id && Slots.Secondary != subtitle.Id) Slots.Select(subtitle.Id); EnsureLoaded(Find(subtitle.Id)); }
                    Revision++;
                });
            });
        }
        void ApplyClasses(List<TextSource> sources)
        { foreach (TextSource source in sources) { int category; if (!source.IntrinsicAdvanced && Options.Classes.TryGetValue(source.Id, out category) && category >= 0 && category <= 3) source.Category = (TextCategory)category; } }
        void ChooseInitial()
        {
            if (initialSelectionDone || Slots.Main != null || Sources.Count == 0) return;
            // Initial discovery only: closing a selected track does not call this path again.
            string stem = Path.GetFileNameWithoutExtension(path);
            TextSource selected = Sources.Find(s => s.Category == TextCategory.Subtitle && s.Error.Length == 0 && s.Path != null && s.Stream < 0 && (Path.GetFileNameWithoutExtension(s.Path).Equals(stem, StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(s.Path).StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase)));
            if (selected == null && media != null && new List<ContainerCaption>(streams.CaptionTracks).Exists(t => t.Default && t.Bitmap)) { initialSelectionDone = true; return; }
            if (selected == null) selected = Sources.Find(s => s.Category == TextCategory.Subtitle && !s.Bitmap && s.Stream >= 0 && media != null && new List<ContainerCaption>(streams.CaptionTracks).Exists(t => t.Ordinal == s.Stream && t.Default));
            if (selected == null) selected = Sources.Find(s => s.Category == TextCategory.Subtitle && !s.Bitmap && s.Stream >= 0);
            if (selected != null) { initialSelectionDone = captionControlActive = true; Slots.Main = selected.Id; EnsureLoaded(selected); }
        }
        internal TextSource Find(string id) { return id == null ? null : Sources.Find(s => s.Id == id); }
        internal void Select(TextSource source)
        {
            if (source.Category != TextCategory.Subtitle || !CanUse(source)) return;
            initialSelectionDone = captionControlActive = true;
            Options.SubtitlesEnabled = true; saveOptions();
            Slots.Select(source.Id);
            EnsureLoaded(Find(Slots.Main), true); EnsureLoaded(Find(Slots.Secondary), true); Revision++;
        }
        internal void SetMulti(bool value) { Slots.SetMulti(value); Options.Multilingual = value; saveOptions(); Revision++; }
        internal void ToggleSubtitles()
        {
            Options.SubtitlesEnabled = !Options.SubtitlesEnabled;
            if (!captionControlActive) SetNativeVisible(Options.SubtitlesEnabled);
            saveOptions(); Revision++;
        }
        internal void Classify(TextSource source, TextCategory category)
        {
            if (source.IntrinsicAdvanced && category != TextCategory.Advanced) { Status = source.AssRenderable ? "该 ASS 含有特效，保留高级分类" : "特殊协议 / 代码弹幕暂不支持播放"; return; }
            if (Slots.Main == source.Id || Slots.Secondary == source.Id) Slots.Select(source.Id);
            source.Category = category; source.Enabled = (category == TextCategory.Danmaku || category == TextCategory.Advanced) && CanUse(source);
            if (source.Enabled) EnsureLoaded(source, true);
            source.Detail = category == TextCategory.Uncertain ? "用途待确认，请拖动分类" : (source.AssRenderable ? source.Events.Count : source.Cues.Count) + (category == TextCategory.Subtitle ? " 条字幕" : category == TextCategory.Danmaku ? " 条普通弹幕" : source.AssRenderable ? " 条 ASS 特效" : " 条 · 暂不播放") + " · 手动分类";
            Options.Classes[source.Id] = (int)category; saveOptions(); Status = ""; Revision++;
        }
        internal void Toggle(TextSource source)
        {
            if ((source.Category != TextCategory.Danmaku && source.Category != TextCategory.Advanced) || !CanUse(source)) return;
            source.Enabled = !source.Enabled;
            if (source.Enabled) EnsureLoaded(source, true);
            Revision++;
        }
        void EnsureLoaded(TextSource source, bool retry = false)
        {
            if (source == null || source.Loaded || source.Loading || source.Bitmap || source.Stream < 0 || source.Error.Length > 0 && !retry) return;
            source.Loading = true; source.Error = ""; Status = ""; int version = generation;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<TextCue> cues = null; List<TextSource> tracks = null; string error = "";
                try { if (load != null) cues = load(source); else { tracks = LoadContainer(source); cues = tracks[0].Cues; } }
                catch (Exception ex) { error = ex.Message; }
                lock (sync) completed.Enqueue(delegate
                {
                    if (version != generation) return; source.Loading = false; source.Loaded = cues != null; source.Error = error;
                    if (cues != null) {
                        source.Cues.Clear(); source.Cues.AddRange(cues);
                        if (tracks != null) ApplyContainer(source,tracks);
                    }
                    else { source.Enabled = false; Status = error; }
                    if (cues == null && (Slots.Main == source.Id || Slots.Secondary == source.Id))
                    { Slots.Select(source.Id); if (Slots.Main == null) { captionControlActive = false; RestoreNative(); } }
                    ChooseInitial(); Revision++;
                });
            });
        }
        internal void ApplyContainer(TextSource source, List<TextSource> tracks)
        {
            bool wasEnabled = source.Enabled;
            for (int i = 0; i < tracks.Count; i++) {
                TextSource parsed = tracks[i], target;
                if (i == 0) target = source;
                else {
                    string id = source.Id+":part:"+parsed.AutoCategory;
                    target = Find(id);
                    if (target == null) { target = new TextSource { Id = id, Title = source.Title, Path = source.Path, Stream = source.Stream }; Sources.Add(target); }
                    target.Cues.Clear(); target.Cues.AddRange(parsed.Cues);
                }
                target.Events.Clear(); target.Events.AddRange(parsed.Events); target.Analysis = parsed.Analysis; target.AutoCategory = parsed.AutoCategory;
                target.AssHeader = parsed.AssHeader;
                target.ProtocolAdvanced = parsed.ProtocolAdvanced;
                target.Fonts.Clear(); target.Fonts.AddRange(parsed.Fonts);
                target.IntrinsicAdvanced = parsed.IntrinsicAdvanced; target.Category = parsed.Category; target.Detail = parsed.Detail; target.Loaded = true; target.Error = "";
                int manual;
                if (!target.IntrinsicAdvanced && Options.Classes.TryGetValue(target.Id,out manual) && manual >= 0 && manual <= 3) target.Category = (TextCategory)manual;
                // Completion may change the automatic class, but cannot reverse a user's
                // explicit off switch while decoding was in progress.
                target.Enabled = (target.Category == TextCategory.Danmaku && !target.IntrinsicAdvanced || target.Category == TextCategory.Advanced && target.AssRenderable) && (i == 0 && Options.Classes.ContainsKey(target.Id) ? wasEnabled : true);
                if (target.Category != TextCategory.Subtitle && (Slots.Main == target.Id || Slots.Secondary == target.Id)) Slots.Select(target.Id);
            }
            if (Slots.Main == null && captionControlActive) { captionControlActive = false; RestoreNative(); }
        }
        internal static string Extract(string file, int ordinal)
        { return NativeTimedText.Extract(file, ordinal); }
        void SetNativeVisible(bool value)
        {
            if (nativeCaptured && nativeVisible == value) return;
            List<NativeMenuSnapshot.Item> items = NativeCaptionAccess.Read(player); NativeMenuSnapshot.Item visible = items.Find(i => i.Command == 10126);
            if (visible == null) return;
            if (!nativeCaptured) { nativeWasVisible = visible.Checked; nativeCaptured = true; }
            if (visible.Checked != value) Native.SetCommand(player, 10126);
            nativeVisible = value;
        }
        void RestoreNative()
        {
            if (nativeCaptured && Native.IsWindow(player))
            { NativeMenuSnapshot.Item visible = NativeCaptionAccess.Read(player).Find(i => i.Command == 10126); if (visible != null && visible.Checked != nativeWasVisible) Native.SetCommand(player, 10126); }
            // PotPlayer saves CaptionVisible before destroying its window. Restore the user's
            // original preference as well when that window has already gone away.
            if (nativeCaptured)
                try { using (RegistryKey key = Registry.CurrentUser.CreateSubKey("Software\\DAUM\\PotPlayerMini64\\Settings")) { if (nativeWasVisible) key.DeleteValue("CaptionVisible", false); else key.SetValue("CaptionVisible", 0, RegistryValueKind.DWord); } }
                catch (UnauthorizedAccessException) { } catch (System.Security.SecurityException) { }
            nativeCaptured = false;
        }
        internal static string Key(string path)
        {
            using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(path).ToLowerInvariant()))).Replace("-", "");
        }
        public void Dispose() { generation++; RestoreNative(); }
    }
}

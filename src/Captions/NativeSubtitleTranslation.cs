using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BilibiliPotPlayer
{
    internal sealed class NativeSubtitleTranslation : NativeWindow, ISubtitleTranslator, IDisposable
    {
        const string ClientProperty = "BilibiliTranslation.Client", TokenProperty = "BilibiliTranslation.Token", SettingsProperty = "BilibiliTranslation.Settings";
        [StructLayout(LayoutKind.Sequential)] struct CopyData { internal IntPtr Kind; internal int Size; internal IntPtr Data; }
        readonly IntPtr player;
        readonly CaptionCatalog catalog;
        readonly object sync = new object();
        readonly Dictionary<string, string> results = new Dictionary<string, string>();
        readonly HashSet<string> allowed = new HashSet<string>();
        readonly Stopwatch watch = Stopwatch.StartNew();
        readonly string folder;
        readonly uint playerPid;
        string media, sourceId, currentInput = "", fingerprint = "", engine = "";
        int settingsVersion, epoch = -1;
        long nextExport, nextSettingsRead, configurationOpened;
        ulong token;
        bool disposed, configuring, warmed, warming, warmupBlocked, inputAttached;
        string restoreFile;
        long warmupStarted;
        NativeMenuSnapshot.Item restoreSource;
        internal string Status = "已关闭";
        internal string Engine { get { return engine.Length == 0 ? "原生默认引擎" : Path.GetFileNameWithoutExtension(engine).Replace("SubtitleTranslate - ", ""); } }
        internal string Languages = "沿用原生设置";
        internal NativeSubtitleTranslation(IntPtr player, CaptionCatalog catalog)
        {
            this.player = player; this.catalog = catalog;
            Native.GetWindowThreadProcessId(player, out playerPid);
            folder = Path.Combine(Path.GetTempPath(), "BilibiliNativeTranslation-" + Guid.NewGuid().ToString("N"));
            CreateHandle(new CreateParams { Caption = "Bilibili native translation receiver", Parent = new IntPtr(-3) });
        }
        static string Normalize(string value) { return value.Replace("\r", "").Trim(); }
        internal static string Export(IEnumerable<TextCue> cues, ulong context = 0)
        {
            var text = new StringBuilder(); int index = 0;
            foreach (TextCue cue in cues)
            {
                if (String.IsNullOrWhiteSpace(cue.Text) || cue.Text.Length > 2000 || Double.IsNaN(cue.Start) || Double.IsInfinity(cue.Start) || Double.IsNaN(cue.End) || Double.IsInfinity(cue.End) || cue.Start < 0 || cue.End <= cue.Start || cue.End > 360000) continue;
                text.Append(++index).Append('\n').Append(Time(cue.Start)).Append(" --> ").Append(Time(cue.End)).Append('\n');
                // Give the native text cache a generation key. The adapted
                // extension removes it before invoking the original engine.
                if (context != 0) text.Append('\u2063').Append(context.ToString("X16", CultureInfo.InvariantCulture)).Append(':').Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(cue.Text))).Append('\u2063');
                text.Append(SecurityElement.Escape(cue.Text).Replace("\r", "")).Append("\n\n");
                if (index == 8) break;
            }
            return text.ToString();
        }
        static string Time(double value)
        { long ms = (long)Math.Round(value * 1000); return (ms / 3600000).ToString("00", CultureInfo.InvariantCulture) + ":" + (ms / 60000 % 60).ToString("00") + ":" + (ms / 1000 % 60).ToString("00") + "," + (ms % 1000).ToString("000"); }
        void ReadSettings()
        {
            if (watch.ElapsedMilliseconds < nextSettingsRead) return; nextSettingsRead = watch.ElapsedMilliseconds + 2000;
            string[] names = { "CaptionTranslateEngine2", "CaptionTranslateCond", "CaptionTranslateOrder" }; var values = new List<string>();
            try
            {
                string root; using (Process process = Process.GetProcessById((int)playerPid)) root = Path.GetDirectoryName(process.MainModule.FileName);
                string ini = Path.Combine(root, "PotPlayerMini64.ini");
                if (File.Exists(ini)) foreach (string name in names) { var value = new StringBuilder(2048); GetPrivateProfileString("Settings", name, "", value, value.Capacity, ini); values.Add(value.ToString()); }
                else using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\DAUM\\PotPlayerMini64\\Settings")) foreach (string name in names) values.Add(key == null ? "" : Convert.ToString(key.GetValue(name, ""), CultureInfo.InvariantCulture));
            }
            catch (System.ComponentModel.Win32Exception) { } catch (UnauthorizedAccessException) { } catch (InvalidOperationException) { }
            while (values.Count < names.Length) values.Add("");
            string next = String.Join("\n", values);
            if (fingerprint != next) { engine = values[0]; fingerprint = next; Invalidate(); }
        }
        void Invalidate()
        {
            if (GetProp(player, TokenProperty).ToInt64() == unchecked((long)token)) RemoveProp(player, TokenProperty);
            token = 0; currentInput = ""; nextExport = 0;
            warmupBlocked = false;
            Languages = "沿用原生设置";
            lock (sync) { results.Clear(); allowed.Clear(); }
            if (catalog.Translation != null) catalog.Translation.Invalidate();
        }
        internal void Update(double position, int clockEpoch)
        {
            if (disposed) return;
            ReadSettings();
            int settings = unchecked((int)GetProp(player, SettingsProperty).ToInt64());
            if (settings != settingsVersion) { settingsVersion = settings; warmed = warming = false; Invalidate(); }
            if (!Native.IsWindowEnabled(player))
            {
                if (!configuring) { Invalidate(); configuring = true; configurationOpened = watch.ElapsedMilliseconds; }
                Status = "正在配置原生翻译…"; return;
            }
            if (configuring)
            {
                if (watch.ElapsedMilliseconds - configurationOpened < 500 || !Native.IsWindowEnabled(player)) { Status = "正在配置原生翻译…"; return; }
                configuring = false; nextSettingsRead = 0; ReadSettings(); Invalidate();
            }
            TextSource source = catalog.Find(catalog.Slots.Main);
            bool active = catalog.Options.TranslationEnabled && catalog.Options.SubtitlesEnabled && source != null && source.Loaded && !source.Bitmap && source.Category == TextCategory.Subtitle && source.Error.Length == 0;
            if (media != catalog.MediaPath) { Stop(false); media = catalog.MediaPath; sourceId = null; }
            if (!active)
            {
                Stop(true); sourceId = null;
                Status = !catalog.Options.TranslationEnabled ? "已关闭" : !catalog.Options.SubtitlesEnabled ? "字幕已关闭" : source == null ? "请先选择主字幕" : source.Bitmap ? "不支持图像字幕" : "正在读取主字幕…"; return;
            }
            if (sourceId != source.Id) { Invalidate(); sourceId = source.Id; }
            if (token == 0)
            {
                if (restoreSource == null && restoreFile == null)
                {
                    restoreSource = NativeCaptionAccess.ReadSources(player).Find(i => i.Checked && i.Command >= 29000 && i.Command < 29199);
                    restoreFile = source.Stream < 0 && !String.IsNullOrEmpty(source.Path) && File.Exists(source.Path) ? source.Path : "";
                }
                token = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0) & 0x7fffffffffffffffUL; if (token == 0) token = 1;
                if (!SetProp(player, ClientProperty, Handle) || !SetProp(player, TokenProperty, (IntPtr)(long)token)) { Status = "无法连接原生翻译回传"; token = 0; return; }
            }
            if (!warmed)
            {
                if (warmupBlocked) { Status = "原生翻译尚未启用或扩展不兼容，请检查原生设置"; return; }
                if (!warming)
                {
                    try { inputAttached = OpenSubtitle(player, BlankSubtitle()) || inputAttached; }
                    catch (IOException) { warmupBlocked = true; Status = "无法写入翻译临时字幕"; return; }
                    catch (UnauthorizedAccessException) { warmupBlocked = true; Status = "无法写入翻译临时字幕"; return; }
                    var visible = NativeCaptionAccess.Read(player).Find(i => i.Command == 10126);
                    if (visible != null && !visible.Checked) Native.SetCommand(player, 10126);
                    warming = true; warmupStarted = watch.ElapsedMilliseconds;
                }
                if (GetProp(player, "BilibiliTranslation.Ready").ToInt64() == unchecked((long)token)) warmed = true;
                if (warmed || watch.ElapsedMilliseconds - warmupStarted > 3000)
                {
                    var visible = NativeCaptionAccess.Read(player).Find(i => i.Command == 10126);
                    if (visible != null && visible.Checked) Native.SetCommand(player, 10126);
                    warming = false; warmupBlocked = !warmed;
                }
                if (!warmed) { Status = "正在初始化原生翻译，请确认原生翻译已启用"; return; }
            }
            if (epoch != clockEpoch) { epoch = clockEpoch; nextExport = 0; }
            if (watch.ElapsedMilliseconds < nextExport) return; nextExport = watch.ElapsedMilliseconds + 2000;
            var cues = source.Cues.Where(c => c.End > position && c.Start <= position + 15 && !String.IsNullOrWhiteSpace(c.Text) && c.Text.Length <= 2000 && (c.Effects & (CueEffects.Drawing | CueEffects.Protocol)) == 0).OrderBy(c => c.Start).Take(8).ToList();
            lock (sync) { if (allowed.Count > 2048) allowed.Clear(); foreach (TextCue cue in cues) allowed.Add(Normalize(cue.Text)); }
            string input = Export(cues, token);
            if (input.Length == 0 || currentInput == input) return;
            try
            {
                Directory.CreateDirectory(folder);
                string file = Path.Combine(folder, "translation-" + token.ToString("X16", CultureInfo.InvariantCulture) + ".srt");
                File.WriteAllText(file, input, new UTF8Encoding(true));
                if (currentInput.Length > 0) Native.SetCommand(player, 10488);
                else if (!OpenSubtitle(player, file)) { Status = "原生播放器未接受字幕输入"; return; }
                inputAttached = true; currentInput = input; Status = "等待原生译文 · 引擎和语言沿用原生设置";
            }
            catch (IOException) { Status = "无法写入翻译临时字幕"; } catch (UnauthorizedAccessException) { Status = "无法写入翻译临时字幕"; }
        }
        internal static bool OpenSubtitle(IntPtr player, string file)
        {
            if (file.IndexOf('"') >= 0 || file.IndexOf('\0') >= 0 || !Path.IsPathRooted(file)) return false;
            string command = "/current \"" + file + "\""; IntPtr memory = Marshal.StringToHGlobalUni(command);
            try { var data = new CopyData { Kind = (IntPtr)1000, Size = (command.Length + 1) * 2, Data = memory }; IntPtr result; return SendMessageTimeout(player, 0x4a, IntPtr.Zero, ref data, 2, 500, out result) != IntPtr.Zero; }
            finally { Marshal.FreeHGlobal(memory); }
        }
        internal void Configure()
        { Invalidate(); configuring = true; configurationOpened = watch.ElapsedMilliseconds; PostMessage(player, 0x111, (IntPtr)12400, IntPtr.Zero); }
        internal void Retry() { Invalidate(); }
        string BlankSubtitle()
        {
            Directory.CreateDirectory(folder); string file = Path.Combine(folder, "initialize.srt");
            var invisible = new StringBuilder("\u2063\u2063\u2063");
            for (int bit = 0; bit < 64; bit++) invisible.Append((token & (1UL << bit)) == 0 ? '\u200B' : '\u200C');
            File.WriteAllText(file, "1\n00:00:00,000 --> 99:59:59,000\n" + invisible + "\n\n", new UTF8Encoding(true)); return file;
        }
        internal bool Accept(IntPtr sender, byte[] bytes)
        {
            NativeTranslationPacket packet = NativeTranslationPacket.Read(bytes);
            if (disposed || sender != player || packet == null || packet.Player != playerPid || token == 0 || packet.Token != token || packet.Original.Length > 2000 || packet.Translated.Length > 8000 || !catalog.Options.TranslationEnabled || !catalog.Options.SubtitlesEnabled) return false;
            string original = Normalize(packet.Original);
            lock (sync)
            {
                if (!allowed.Contains(original)) return false;
                if (results.Count >= 2048) results.Clear(); results[original] = packet.Translated;
            }
            Languages = (packet.Source.Length == 0 ? "自动检测" : packet.Source) + " → " + packet.Target;
            engine = packet.Engine;
            Status = "原生译文已接入"; return true;
        }
        public string Translate(string text, string source, string target, CancellationToken cancellation)
        {
            long started = watch.ElapsedMilliseconds;
            while (watch.ElapsedMilliseconds - started < 8000)
            {
                cancellation.ThrowIfCancellationRequested();
                lock (sync) { string translated; if (disposed) throw new OperationCanceledException(); if (results.TryGetValue(Normalize(text), out translated)) return translated; }
                cancellation.WaitHandle.WaitOne(40);
            }
            throw new TranslationException("尚未收到原生译文，请检查原生翻译设置");
        }
        void Stop(bool restore)
        {
            bool attached = inputAttached;
            if (warming)
            {
                var visible = NativeCaptionAccess.Read(player).Find(i => i.Command == 10126);
                if (visible != null && visible.Checked) Native.SetCommand(player, 10126);
                warming = false;
            }
            if (attached) Invalidate();
            if (GetProp(player, ClientProperty) == Handle) { RemoveProp(player, TokenProperty); RemoveProp(player, ClientProperty); }
            if (restore && attached && Native.IsWindow(player))
            {
                var matches = restoreSource == null ? new List<NativeMenuSnapshot.Item>() : NativeCaptionAccess.ReadSources(player).FindAll(i => i.Text == restoreSource.Text && i.Command >= 29000 && i.Command < 29199);
                if (matches.Count == 1) Native.SetCommand(player, matches[0].Command);
                else try { OpenSubtitle(player, !String.IsNullOrEmpty(restoreFile) && File.Exists(restoreFile) ? restoreFile : BlankSubtitle()); }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            restoreSource = null; restoreFile = null; inputAttached = false;
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x4a && message.LParam != IntPtr.Zero)
            {
                var data = (CopyData)Marshal.PtrToStructure(message.LParam, typeof(CopyData));
                if (data.Kind.ToInt64() == NativeTranslationPacket.Magic && data.Size >= 32 && data.Size <= NativeTranslationPacket.Maximum && data.Data != IntPtr.Zero)
                { byte[] bytes = new byte[data.Size]; Marshal.Copy(data.Data, bytes, 0, bytes.Length); message.Result = Accept(message.WParam, bytes) ? (IntPtr)1 : IntPtr.Zero; return; }
            }
            base.WndProc(ref message);
        }
        public void Dispose()
        {
            if (disposed) return; Stop(true); lock (sync) { disposed = true; results.Clear(); allowed.Clear(); }
            DestroyHandle();
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetProp(IntPtr window, string name, IntPtr value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetProp(IntPtr window, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr RemoveProp(IntPtr window, string name);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wp, ref CopyData data, uint flags, uint timeout, out IntPtr result);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetPrivateProfileString(string section, string key, string fallback, StringBuilder value, int size, string file);
    }
}

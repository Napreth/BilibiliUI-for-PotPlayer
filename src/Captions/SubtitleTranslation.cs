using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace BilibiliPotPlayer
{
    internal interface ISubtitleTranslator
    {
        string Translate(string text, string source, string target, CancellationToken cancellation);
    }
    // Owned by the player UI thread. Workers only publish immutable replies; a
    // cancelled generation never mutates the currently selected subtitle track.
    internal sealed class SubtitleTranslation : IDisposable
    {
        sealed class Reply { internal int Generation; internal string Text, Result, Error; }
        readonly ISubtitleTranslator translator;
        readonly object sync = new object();
        readonly Queue<Reply> replies = new Queue<Reply>();
        readonly Dictionary<string, string> cache = new Dictionary<string, string>();
        readonly Dictionary<string, long> failed = new Dictionary<string, long>();
        readonly Stopwatch watch = Stopwatch.StartNew();
        readonly TextSource translated = new TextSource { Id = "translation", Title = "实时字幕翻译", Category = TextCategory.Subtitle, Loaded = true };
        CancellationTokenSource cancellation;
        TextSource selected;
        List<TextCue> cues = new List<TextCue>();
        double[] maximumEnd = new double[0];
        string media, sourceLanguage, targetLanguage;
        int generation, epoch, scan, pending;
        long nextRequest;
        bool enabled, disposed;
        internal int Revision { get; private set; }
        internal string Status { get; private set; }
        internal TextSource Track { get { return translated; } }
        internal SubtitleTranslation(ISubtitleTranslator translator) { this.translator = translator; Status = "已关闭"; }

        internal void Update(string mediaPath, TextSource main, CaptionOptions options, double position, int clockEpoch)
        {
            if (disposed) return;
            bool active = options.TranslationEnabled && options.SubtitlesEnabled && main != null && !main.Bitmap && main.Category == TextCategory.Subtitle && main.Loaded && main.Error.Length == 0;
            bool changed = main != selected || media != mediaPath || sourceLanguage != options.TranslationSource || targetLanguage != options.TranslationTarget || active != enabled;
            bool seek = clockEpoch != epoch;
            if (changed || seek)
            {
                Cancel(); generation++; pending = 0;
                if (changed) { cache.Clear(); failed.Clear(); }
                selected = main; media = mediaPath; sourceLanguage = options.TranslationSource; targetLanguage = options.TranslationTarget; enabled = active; epoch = clockEpoch;
                cues = active ? main.Cues.Where(c => c.Text.Trim().Length > 0 && c.Text.Length <= 2000 && (c.Effects & (CueEffects.Drawing | CueEffects.Protocol)) == 0).OrderBy(c => c.Start).ToList() : new List<TextCue>();
                maximumEnd = new double[cues.Count];
                for (int i = 0; i < cues.Count; i++) maximumEnd[i] = Math.Max(i == 0 ? 0 : maximumEnd[i - 1], cues[i].End);
                scan = Math.Max(0, DanmakuLayout.LowerBound(cues, position) - 1);
                SetTrack(position);
                Status = !options.TranslationEnabled ? "已关闭" : !options.SubtitlesEnabled ? "字幕已关闭" : main == null ? "请先选择主字幕" : main.Bitmap ? "不支持图像字幕" : !active ? "正在读取主字幕…" : "等待字幕";
            }
            Drain();
            if (!active) return;
            // Publish only current cues, keeping original timestamps. A late
            // translation must never linger into the next sentence.
            SetTrack(position);
            if (pending != 0 || watch.ElapsedMilliseconds < nextRequest) return;
            while (scan < cues.Count && cues[scan].End <= position) scan++;
            var candidates = Active(position);
            // Bounded lookahead: no whole-file upload, even while paused.
            for (int i = scan; i < cues.Count && cues[i].Start <= position + 15 && candidates.Count < 8; i++)
                if (cues[i].End > position && !candidates.Contains(cues[i])) candidates.Add(cues[i]);
            foreach (TextCue cue in candidates)
            {
                long retry;
                if (cache.ContainsKey(cue.Text) || failed.TryGetValue(cue.Text, out retry) && watch.ElapsedMilliseconds < retry) continue;
                Start(cue.Text); return;
            }
            if (translated.Cues.Count > 0) Status = "翻译已就绪";
            else if (failed.Count == 0) Status = "等待字幕";
        }
        void Start(string text)
        {
            cancellation = new CancellationTokenSource();
            CancellationTokenSource owner = cancellation;
            CancellationToken token = owner.Token;
            int version = generation; string from = sourceLanguage, to = targetLanguage;
            pending = 1; Status = "正在翻译…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                var reply = new Reply { Generation = version, Text = text };
                try
                {
                    reply.Result = translator.Translate(text, from, to, token);
                    if (String.IsNullOrWhiteSpace(reply.Result) || reply.Result.Length > 8000) reply.Error = "翻译服务返回了无效内容";
                }
                catch (OperationCanceledException) { reply.Error = "翻译已取消，请重试"; }
                catch (TranslationException error) { reply.Error = error.Message; }
                catch (Exception) { reply.Error = "翻译失败，请稍后重试"; }
                lock (sync) { if (!disposed && !token.IsCancellationRequested) replies.Enqueue(reply); }
                owner.Dispose();
            });
        }
        void Drain()
        {
            lock (sync) while (replies.Count > 0)
            {
                Reply reply = replies.Dequeue(); if (reply.Generation != generation) continue;
                pending = 0; cancellation = null;
                if (reply.Error != null)
                {
                    failed[reply.Text] = watch.ElapsedMilliseconds + 30000;
                    nextRequest = watch.ElapsedMilliseconds + 30000;
                    Status = reply.Error;
                }
                else if (reply.Result != null)
                {
                    // Cache is scoped to this source/language pair and session.
                    // Cap it independently from subtitle-file size.
                    if (cache.Count >= 2048) cache.Clear();
                    cache[reply.Text] = reply.Result; failed.Remove(reply.Text); nextRequest = watch.ElapsedMilliseconds + 250; Status = "翻译已就绪";
                }
            }
        }
        void SetTrack(double position)
        {
            var next = new List<TextCue>();
            if (enabled) foreach (TextCue cue in Active(position))
            {
                string text;
                if (cache.TryGetValue(cue.Text, out text) && text.Trim() != cue.Text.Trim()) next.Add(new TextCue { Start = cue.Start, End = cue.End, Text = text });
            }
            if (next.Count == translated.Cues.Count && next.Zip(translated.Cues, (a, b) => a.Start == b.Start && a.End == b.End && a.Text == b.Text).All(v => v)) return;
            translated.Cues.Clear(); translated.Cues.AddRange(next); Revision++;
        }
        List<TextCue> Active(double position)
        {
            var active = new List<TextCue>();
            for (int i = DanmakuLayout.LowerBound(cues, position + .00001) - 1; i >= 0 && maximumEnd[i] > position; i--)
                if (cues[i].End > position) { active.Add(cues[i]); if (active.Count == 4) break; }
            active.Reverse(); return active;
        }
        void Cancel()
        {
            // A finished worker disposes its own CTS. Cancellation may race it.
            if (cancellation != null) { try { cancellation.Cancel(); } catch (ObjectDisposedException) { } cancellation = null; }
        }
        internal void Retry() { failed.Clear(); nextRequest = 0; Status = "等待字幕"; }
        internal void Invalidate()
        {
            Cancel(); generation++; pending = 0; selected = null; media = null;
            cache.Clear(); failed.Clear(); nextRequest = 0;
            if (translated.Cues.Count > 0) { translated.Cues.Clear(); Revision++; }
        }
        public void Dispose()
        {
            lock (sync) { disposed = true; replies.Clear(); }
            Cancel(); cache.Clear(); failed.Clear(); translated.Cues.Clear();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BilibiliPotPlayer
{
    internal static class DanAnyAss
    {
        static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);
        internal static bool KnownHeader(string header)
        { return Regex.IsMatch(header, @"(?im)^\s*Original Script:[^\r\n]*(?:@dan-uni/dan-any-ext-ass\(https://github\.com/ani-uni/dan-any-ext-ass(?:#readme)?\)|https://github\.com/ani-uni/danuni/tree/master/packages/dan-any#readme|https://github\.com/justorez/biliy(?=\s|$))", RegexOptions.None, Timeout); }
        sealed class Evidence
        {
            internal TextCue Original;
            internal int Mode;
            internal int? Weight;
            internal bool Conflict;
        }
        static string Text(string value)
        {
            string text = TimedText.Plain(value.Replace("{", "｛").Replace("}", "｝").Replace("\r", "").Replace("\n", ""));
            return text.Length > 2000 ? text.Substring(0, 2000) : text;
        }
        static string Key(long time, string text) { return time.ToString(CultureInfo.InvariantCulture) + "\0" + text; }
        static int Mode(TextCue cue) { return cue.Mode == 2 || cue.Mode == 3 ? 1 : cue.Mode; }
        static void Index(Dictionary<string, Evidence> index, long time, string text, TextCue cue)
        {
            string key = Key(time, text); Evidence previous;
            if (index.TryGetValue(key, out previous)) {
                if (previous.Mode != Mode(cue) || previous.Original.ConverterCategory != cue.ConverterCategory) previous.Conflict = true;
                if (previous.Weight != cue.Weight) previous.Weight = null;
            } else index.Add(key, new Evidence { Original = cue, Mode = Mode(cue), Weight = cue.Weight });
        }
        static int Bind(List<TextCue> originals, TextSource source, bool signed)
        {
            var index = new Dictionary<string, Evidence>(StringComparer.Ordinal);
            foreach (TextCue original in originals) {
                string text = Text(original.Text);
                double position = original.Start * 100;
                long time = (long)Math.Floor(position), rounded = (long)Math.Floor(position + .0000001);
                Index(index, time, text, original);
                if (rounded != time) Index(index, rounded, text, original);
                // Verified v2 output writes one-digit centiseconds without zero padding.
                if (signed) {
                    long seconds = (long)Math.Floor(original.Start), fraction = (long)Math.Floor((original.Start - seconds) * 100);
                    if (fraction > 0 && fraction < 10) Index(index, seconds * 100 + fraction * 10, text, original);
                }
            }
            int matched = 0;
            foreach (TextCue cue in source.Events) {
                Evidence match; if (!index.TryGetValue(Key((long)Math.Round(cue.Start * 100), cue.Text), out match)) continue;
                if (match.Conflict) { cue.ConverterCategory = null; cue.Converter = ""; cue.Declared = null; cue.Effects |= CueEffects.Unparsed; continue; }
                cue.ConverterCategory = match.Original.ConverterCategory; cue.Converter = "dan-any Raw"; cue.Declared = null;
                cue.Mode = match.Mode; cue.Weight = match.Weight;
                if (cue.ConverterCategory == TextCategory.Danmaku) cue.Motion = cue.Mode == 4 || cue.Mode == 5 ? CueMotion.Static : CueMotion.Horizontal;
                matched++;
            }
            return matched;
        }
        internal static void Apply(string value, string header, TextSource source)
        {
            bool signed = KnownHeader(header);
            var fields = new Dictionary<string, string>(StringComparer.Ordinal); bool duplicate = false;
            string section = "";
            foreach (string raw in value.Replace("\r", "").Split('\n')) {
                string line = raw.Trim();
                if (AssEvents.IsSectionHeader(line)) { section = line.ToLowerInvariant(); continue; }
                if (section != "[script info]" && section != "[events]" || !line.StartsWith(";Raw", StringComparison.Ordinal)) continue;
                int colon = line.IndexOf(':'); if (colon < 0) continue;
                string key = line.Substring(1, colon - 1);
                if (key != "Raw" && key != "RawConf" && key != "RawPb" && key != "RawCompressType" && key != "RawBaseType") continue;
                if (fields.ContainsKey(key)) duplicate = true; else fields.Add(key, line.Substring(colon + 1).Trim());
            }
            if (fields.Count > 0) {
                try {
                    if (duplicate) throw new InvalidDataException("重复 Raw 字段");
                    List<TextCue> originals = DanAnyRaw.Read(fields);
                    int matched = Bind(originals, source, signed);
                    source.OriginalEvents.AddRange(originals);
                    source.MetadataDetail = "Raw 匹配 " + matched + "/" + source.Events.Count + " 条 ASS 事件";
                } catch (Exception error) {
                    if (!(error is IOException || error is InvalidDataException || error is ArgumentException || error is InvalidOperationException || error is FormatException || error is DecoderFallbackException || error is DllNotFoundException || error is EntryPointNotFoundException || error is BadImageFormatException)) throw;
                    source.MetadataDetail = "Raw 元数据无效或不支持，按 ASS 识别";
                }
            }
            if (!signed) return;
            foreach (TextCue cue in source.Events) {
                if (cue.ConverterCategory.HasValue || (cue.Effects & CueEffects.Unparsed) != 0 || !Regex.IsMatch(cue.Style, @"^F\d+$", RegexOptions.None, Timeout)) continue;
                cue.ConverterCategory = TextCategory.Danmaku; cue.Converter = "dan-any 样式"; cue.Declared = null;
                if (cue.Motion == CueMotion.Static && cue.PositionKnown && cue.Y >= 0 && cue.Y <= 1) cue.Mode = cue.Y < .5 ? 5 : 4;
            }
        }
    }
}

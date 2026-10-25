using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml;

namespace BilibiliPotPlayer
{
    internal enum TextCategory { Subtitle, Danmaku, Advanced, Uncertain }
    internal sealed class TextCue
    {
        internal double Start, End;
        internal string Text = "", RawContent = "", Origin = "", Style = "";
        internal string AssFields = "";
        internal int Layer, Alignment, AssOrder;
        internal double X, Y;
        internal double CanvasHeight = 1080;
        internal double DanmakuBaseSize = 25;
        internal bool PositionKnown, DurationInferred;
        internal CueMotion Motion;
        internal CueEffects Effects;
        internal TextCategory? Declared;
        internal int Mode = 1, Color = 0xFFFFFF, Size = 25;
        internal int? Weight;
    }
    internal sealed class TextSource
    {
        internal string Id, Title, Detail = "", Path, Format = "", Error = "";
        internal string AssHeader = "";
        internal bool ProtocolAdvanced;
        internal bool Converted;
        internal readonly List<TextCue> OriginalEvents = new List<TextCue>();
        internal bool AssRenderable { get { return AssHeader.Length > 0 && !ProtocolAdvanced; } }
        internal readonly List<AssFont> Fonts = new List<AssFont>();
        internal TextCategory Category, AutoCategory;
        internal CaptionAnalysis Analysis;
        internal readonly List<TextCue> Events = new List<TextCue>();
        internal bool IntrinsicAdvanced, Enabled, Loading, Loaded, Bitmap;
        internal int Stream = -1;
        internal readonly List<TextCue> Cues = new List<TextCue>();
    }
    internal static class TimedText
    {
        internal const int MaxBytes = 32 * 1024 * 1024, MaxCues = 100000;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static Match Match(string value, string pattern) { return Regex.Match(value, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200)); }
        static bool Has(string value, string pattern) { return Match(value, pattern).Success; }
        internal static List<TextSource> Read(string path)
        {
            FileInfo file = new FileInfo(path);
            if (!file.Exists || file.Length > MaxBytes) throw new InvalidDataException("文件过大或不存在");
            byte[] bytes = File.ReadAllBytes(path); string value;
            try { value = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
            catch (DecoderFallbackException) { value = Encoding.GetEncoding(936).GetString(bytes); }
            if (bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF))
                value = (bytes[0] == 0xFF ? Encoding.Unicode : Encoding.BigEndianUnicode).GetString(bytes, 2, bytes.Length - 2);
            return Parse(value, Path.GetFileName(path), Path.GetExtension(path));
        }
        internal static List<TextSource> Parse(string value, string name, string extension)
        {
            if (value.Length > MaxBytes) throw new InvalidDataException("文件过大");
            string trimmed = value.TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
            TextSource source = new TextSource { Title = name, Category = TextCategory.Uncertain, Loaded = true };
            if (trimmed.StartsWith("<") && Has(trimmed, @"<d\s+[^>]*p\s*=")) return ParseXml(trimmed, name);
            if (Has(trimmed, @"(?m)^\s*\[Events\]")) AssEvents.Parse(trimmed, source);
            else if (Has(trimmed, @"(?m)^\s*(?:\d+:)?\d{1,2}:\d{2}[,.]\d+\s*-->")) ParseSrt(trimmed, source);
            else if (trimmed.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase)) ParseSrt(trimmed, source);
            else if (Has(trimmed, @"(?m)^\s*\[\d{1,3}:\d{2}(?:[.]\d+)?\]")) ParseLrc(trimmed, source);
            else if (trimmed.StartsWith("{") || trimmed.StartsWith("[")) return ParseJson(trimmed, name);
            else if (trimmed.StartsWith("<") && Has(trimmed, @"<(?:\w+:)?tt\b")) ParseTtml(trimmed, source);
            else source.Error = "无法识别内容结构";
            return CaptionClassifier.Split(source);
        }
        internal static double Time(string value)
        {
            value = value.Trim().Replace(',', '.');
            double seconds;
            if (value.EndsWith("ms") && Double.TryParse(value.Substring(0, value.Length - 2), NumberStyles.Float, Inv, out seconds)) return Valid(seconds / 1000);
            if (value.EndsWith("s") && Double.TryParse(value.Substring(0, value.Length - 1), NumberStyles.Float, Inv, out seconds)) return Valid(seconds);
            string[] parts = value.Split(':'); double total = 0;
            if (parts.Length > 3) return -1;
            for (int i = 0; i < parts.Length; i++) { if (!Double.TryParse(parts[i], NumberStyles.Float, Inv, out seconds) || seconds < 0 || i > 0 && seconds >= 60) return -1; total = total * 60 + seconds; }
            return Valid(total);
        }
        static double Valid(double value) { return Double.IsNaN(value) || Double.IsInfinity(value) || value < 0 || value > 604800 ? -1 : value; }
        internal static string Plain(string value)
        { return System.Net.WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(value, @"\{[^}]*\}", "", RegexOptions.None, TimeSpan.FromMilliseconds(200)), @"<[^>]*>", "", RegexOptions.None, TimeSpan.FromMilliseconds(200))).Replace("\\N", "\n").Replace("\\n", "\n").Replace("\\h", " ").Trim(); }
        internal static void Add(TextSource source, TextCue cue)
        {
            if (source.Events.Count >= MaxCues) throw new InvalidDataException("条目数量超过限制");
            if (cue.Start < 0 || cue.End <= cue.Start || cue.Text.Length == 0) return;
            if (cue.Text.Length > 2000) cue.Text = cue.Text.Substring(0, 2000);
            cue.Size = Math.Max(12, Math.Min(64, cue.Size)); cue.Color &= 0xFFFFFF; source.Events.Add(cue); source.Cues.Add(cue);
        }
        internal static void Sort(List<TextCue> cues) { cues.Sort(delegate(TextCue a, TextCue b) { return a.Start.CompareTo(b.Start); }); }
        static void ParseSrt(string value, TextSource source)
        {
            source.Format = "SRT / WebVTT"; source.Category = TextCategory.Subtitle;
            string[] lines = value.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                Match time = Match(lines[i], @"^\s*((?:\d+:)?\d{1,2}:\d{2}[,.]\d+)\s*-->\s*((?:\d+:)?\d{1,2}:\d{2}[,.]\d+)");
                if (!time.Success) continue;
                string timingLine = lines[i];
                StringBuilder text = new StringBuilder();
                while (i + 1 < lines.Length && lines[i + 1].Trim().Length > 0) { if (text.Length > 2000) break; if (text.Length > 0) text.Append('\n'); text.Append(lines[++i]); }
                TextCue cue = new TextCue { Start = Time(time.Groups[1].Value), End = Time(time.Groups[2].Value), Text = Plain(text.ToString()), RawContent = time.Value+"\n"+text, Origin = "SRT / WebVTT" };
                Match position = Match(timingLine, @"\bposition:(\d+(?:\.\d+)?)%"), vertical = Match(timingLine, @"\bline:(\d+(?:\.\d+)?)%");
                if (position.Success && vertical.Success) { cue.X = Double.Parse(position.Groups[1].Value,Inv)/100; cue.Y = Double.Parse(vertical.Groups[1].Value,Inv)/100; cue.PositionKnown = true; cue.Motion = CueMotion.Static; cue.Alignment = 2; }
                Add(source,cue);
            }
        }
        static void ParseLrc(string value, TextSource source)
        {
            source.Format = "LRC"; source.Category = TextCategory.Subtitle;
            foreach (string line in value.Replace("\r", "").Split('\n'))
            {
                MatchCollection timestamps = Regex.Matches(line, @"\[(\d{1,3}:\d{2}(?:[.]\d+)?)\]", RegexOptions.None, TimeSpan.FromMilliseconds(200));
                string text = Regex.Replace(line, @"\[[^]]*\]", "", RegexOptions.None, TimeSpan.FromMilliseconds(200)).Trim();
                foreach (Match time in timestamps) Add(source, new TextCue { Start = Time(time.Groups[1].Value), End = Time(time.Groups[1].Value) + 8, Text = text, RawContent = line, Origin = "LRC", DurationInferred = true, Declared = TextCategory.Subtitle });
            }
            Sort(source.Cues);
            for (int i = 0; i + 1 < source.Cues.Count; i++) if (source.Cues[i + 1].Start > source.Cues[i].Start) source.Cues[i].End = source.Cues[i + 1].Start;
        }
        static XmlReader Xml(string value)
        { return XmlReader.Create(new StringReader(value), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes }); }
        static List<TextSource> ParseXml(string value, string name)
        {
            TextSource ordinary = new TextSource { Title = name, Category = TextCategory.Danmaku, Format = "Bilibili XML", Loaded = true };
            TextSource advanced = new TextSource { Title = name, Category = TextCategory.Advanced, Format = "高级 / 代码 / BAS", Loaded = true, IntrinsicAdvanced = true };
            int advancedCount = 0, total = 0;
            using (XmlReader reader = Xml(value))
                while (!reader.EOF)
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "d") { reader.Read(); continue; }
                    if (++total > MaxCues) throw new InvalidDataException("条目数量超过限制");
                    string[] p = (reader.GetAttribute("p") ?? "").Split(','); int? weight=ParseWeight(reader.GetAttribute("weight")); string text = reader.ReadElementContentAsString();
                    int mode, size = 25, color = 0xFFFFFF; double time;
                    if (p.Length < 4 || !Double.TryParse(p[0], NumberStyles.Float, Inv, out time) || Valid(time) < 0 || !Int32.TryParse(p[1], out mode)) continue;
                    if (!Int32.TryParse(p[2], out size)) size = 25; if (!Int32.TryParse(p[3], out color)) color = 0xFFFFFF;
                    if (mode >= 7 && mode <= 9) { advancedCount++; advanced.Events.Add(new TextCue { Start = time, End = time + 8, Mode = mode, Size = size, Color = color, Weight=weight, RawContent = text, Origin = "Bilibili XML mode " + mode, Effects = CueEffects.Protocol, DurationInferred = true }); continue; }
                    if (mode < 1 || mode > 6) continue;
                    Add(ordinary, new TextCue { Start = time, End = time + 8, Text = text.Replace("/n", "\n"), Mode = mode, Size = size, Color = color, Weight=weight, RawContent = text, Origin = "Bilibili XML mode " + mode, DurationInferred = true, Declared = TextCategory.Danmaku, Motion = mode == 4 || mode == 5 ? CueMotion.Static : CueMotion.Horizontal });
                }
            ordinary.Analysis = CaptionClassifier.Analyze(ordinary.Events); ordinary.AutoCategory = TextCategory.Danmaku;
            advanced.Analysis = CaptionClassifier.Analyze(advanced.Events); advanced.AutoCategory = TextCategory.Advanced;
            ordinary.Analysis.Confidence = advanced.Analysis.Confidence = 1;
            ordinary.Analysis.Reason = "Bilibili 普通弹幕协议"; advanced.Analysis.Reason = "Bilibili 高级／代码／BAS 协议";
            List<TextSource> result = new List<TextSource>();
            if (ordinary.Cues.Count > 0) { ordinary.Detail = ordinary.Cues.Count + " 条普通弹幕"; Sort(ordinary.Cues); result.Add(ordinary); }
            if (advancedCount > 0) { advanced.Detail = advancedCount + " 条 · 暂不播放"; result.Add(advanced); }
            if (result.Count == 0) { ordinary.Detail = "没有有效条目"; result.Add(ordinary); }
            return result;
        }
        static List<TextSource> ParseJson(string value, string name)
        {
            JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = MaxBytes, RecursionLimit = 32 };
            object parsed = json.DeserializeObject(value);
            Dictionary<string, object> document = parsed as Dictionary<string, object>;
            bool declared = document != null && document.ContainsKey("danmaku");
            object[] entries = (declared ? document["danmaku"] : document!=null && document.ContainsKey("body") ? document["body"] : parsed) as object[];
            TextSource source = new TextSource { Title = name, Format = "JSON", Loaded = true };
            if (entries != null) {
                if (entries.Length > MaxCues) throw new InvalidDataException("条目数量超过限制");
                foreach (object entry in entries) {
                    Dictionary<string, object> item = entry as Dictionary<string, object>; if(item==null)continue;
                    if(!declared && !item.ContainsKey("mode") && !item.ContainsKey("time") && item.ContainsKey("from") && item.ContainsKey("to") && item.ContainsKey("content")) {
                        Add(source,new TextCue{Start=Time(Convert.ToString(item["from"],Inv)),End=Time(Convert.ToString(item["to"],Inv)),Text=Convert.ToString(item["content"],Inv),RawContent=json.Serialize(item),Origin="BilibiliSubtitleJSON",Declared=TextCategory.Subtitle,Motion=CueMotion.Static});
                        continue;
                    }
                    if(!item.ContainsKey("time"))continue;
                    double time = Time(Convert.ToString(item["time"], Inv)); int mode = 0, color = 0xFFFFFF, size = 25;
                    if (item.ContainsKey("mode")) Int32.TryParse(Convert.ToString(item["mode"], Inv),out mode);
                    string type = item.ContainsKey("type") ? Convert.ToString(item["type"],Inv).ToLowerInvariant() : "";
                    TextCue cue = new TextCue { Start = time, End = item.ContainsKey("end") ? Time(Convert.ToString(item["end"],Inv)) : time + 8,
                        Text = item.ContainsKey("text") ? Convert.ToString(item["text"],Inv) : "", RawContent = json.Serialize(item), Origin = "JSON", DurationInferred = !item.ContainsKey("end"), Mode = mode >= 1 ? mode : 1 };
                    if(item.ContainsKey("weight")) cue.Weight=ParseWeight(Convert.ToString(item["weight"],Inv));
                    if (mode >= 7 && mode <= 9 || type == "script" || type == "bas" || type == "advanced" || type == "animation") {
                        cue.Effects = CueEffects.Protocol; if (cue.Text.Length == 0) cue.Text = "[advanced]";
                    } else if (mode > 9 || item.ContainsKey("animation") || item.ContainsKey("motion") || item.ContainsKey("script")) cue.Effects = CueEffects.Unparsed;
                    if (declared || mode >= 1 && mode <= 6) { cue.Declared = TextCategory.Danmaku; cue.Motion = mode == 4 || mode == 5 ? CueMotion.Static : CueMotion.Horizontal; }
                    if (item.ContainsKey("color")) { string c = Convert.ToString(item["color"],Inv); if (c.StartsWith("#")) Int32.TryParse(c.Substring(1),NumberStyles.HexNumber,Inv,out color); else Int32.TryParse(c,out color); }
                    if (item.ContainsKey("size")) Int32.TryParse(Convert.ToString(item["size"],Inv),out size);
                    cue.Color = color; cue.Size = size; Add(source,cue);
                }
            }
            return CaptionClassifier.Split(source);
        }
        static int? ParseWeight(string text)
        { int weight; return Int32.TryParse(text,NumberStyles.Integer,Inv,out weight)?weight:(int?)null; }
        static void ParseTtml(string value, TextSource source)
        {
            source.Category = TextCategory.Subtitle; source.Format = "TTML";
            using (XmlReader reader = Xml(value))
                while (!reader.EOF)
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "p") { reader.Read(); continue; }
                    double start = Time(reader.GetAttribute("begin") ?? ""), end = Time(reader.GetAttribute("end") ?? "");
                    if (end < 0) { double duration = Time(reader.GetAttribute("dur") ?? ""); if (duration >= 0) end = start + duration; }
                    string body = reader.ReadInnerXml();
                    Add(source, new TextCue { Start = start, End = end, Text = Plain(body), RawContent = body, Origin = "TTML", Effects = Has(body,@"<(?:\w+:)?(?:set|animate)\b") ? CueEffects.Animation : CueEffects.None });
                }
        }
    }
    internal sealed class SubtitleSlots
    {
        internal bool Multi;
        internal string Main, Secondary;
        internal void Select(string id)
        {
            if (id == Main) { Main = Secondary; Secondary = null; return; }
            if (id == Secondary) { Secondary = null; return; }
            if (!Multi || Main == null) { Main = id; Secondary = null; }
            else Secondary = id;
        }
        internal void SetMulti(bool value) { Multi = value; if (!value) Secondary = null; }
        internal void Clear() { Main = Secondary = null; }
    }
}

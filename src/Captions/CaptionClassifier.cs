using System;
using System.Collections.Generic;
using System.Linq;

namespace BilibiliPotPlayer
{
    internal enum CueMotion { Unknown, Static, Horizontal }
    [Flags] internal enum CueEffects { None = 0, Animation = 1, Drawing = 2, Protocol = 4, Unparsed = 8 }
    internal sealed class CaptionAnalysis
    {
        public double O2, O3, B3, Q, M, Hy, Bottom, DurationReliability, Danmaku, Subtitle, Confidence;
        public int Samples, Features;
        public string Reason = "";
    }
    internal static class CaptionClassifier
    {
        sealed class Edge { internal double Time; internal int Delta; }
        internal static CaptionAnalysis Analyze(List<TextCue> events)
        {
            CaptionAnalysis a = new CaptionAnalysis { Samples = events.Count };
            if (events.Count == 0) { a.Reason = "没有有效事件"; return a; }
            List<TextCue> sorted = events.OrderBy(c => c.Start).ThenBy(c => c.End).ToList();
            List<TextCue> timed = sorted.Where(c => !c.DurationInferred).ToList();
            a.DurationReliability = timed.Count / (double)sorted.Count;
            List<Edge> edges = new List<Edge>();
            foreach (TextCue c in timed) { edges.Add(new Edge { Time = c.Start, Delta = 1 }); edges.Add(new Edge { Time = c.End, Delta = -1 }); }
            edges.Sort((x,y) => x.Time.CompareTo(y.Time));
            double active = 0, two = 0, three = 0, last = 0; int concurrent = 0;
            for (int i = 0; i < edges.Count; ) {
                double t = edges[i].Time, dt = t - last;
                if (concurrent > 0) active += dt; if (concurrent >= 2) two += dt; if (concurrent >= 3) three += dt;
                do { concurrent += edges[i++].Delta; } while (i < edges.Count && edges[i].Time == t);
                last = t;
            }
            if (active > 0) { a.O2 = two / active; a.O3 = three / active; }
            int left = 0, right = 0, clustered = 0, pairs = 0, sequential = 0;
            for (int i = 0; i < sorted.Count; i++) {
                while (sorted[left].Start < sorted[i].Start - .5) left++;
                while (right < sorted.Count && sorted[right].Start <= sorted[i].Start + .5) right++;
                if (right - left >= 3) clustered++;
                if (i > 0 && !sorted[i-1].DurationInferred && !sorted[i].DurationInferred) { pairs++; if (sorted[i].Start >= sorted[i-1].End - .05) sequential++; }
            }
            a.B3 = clustered / (double)sorted.Count; a.Q = pairs == 0 ? 0 : sequential / (double)pairs;
            int motion = 0, scroll = 0, positioned = 0, stationary = 0, bottom = 0; int[] bins = new int[12];
            foreach (TextCue c in sorted) {
                if (c.Motion != CueMotion.Unknown) { motion++; if (c.Motion == CueMotion.Horizontal) scroll++; }
                if (!c.PositionKnown) continue;
                positioned++; bins[Math.Max(0,Math.Min(11,(int)(c.Y * 12)))]++;
                if (c.Motion == CueMotion.Static) { stationary++; if (c.Y >= .75 && c.X >= .25 && c.X <= .75 && c.Alignment % 3 == 2) bottom++; }
            }
            a.M = motion == 0 ? 0 : scroll / (double)motion;
            foreach (int bin in bins) if (bin > 0) { double p = bin / (double)positioned; a.Hy -= p * Math.Log(p) / Math.Log(12); }
            a.Bottom = stationary == 0 ? 0 : bottom / (double)stationary;
            double dw = 0, sw = 0, d = 0, s = 0;
            Evidence(ref d, ref dw, a.M, 3, motion / (double)sorted.Count, a);
            Evidence(ref d, ref dw, a.O3, 2.5, a.DurationReliability, a);
            Evidence(ref d, ref dw, a.B3, 2, 1, a);
            Evidence(ref d, ref dw, a.Hy, 1, positioned / (double)sorted.Count, a);
            // Sequential starts do not imply dialogue when the source explicitly scrolls.
            Evidence(ref s, ref sw, a.Q, 1.5, pairs / (double)Math.Max(1, sorted.Count - 1) * (1 - a.M), a);
            Evidence(ref s, ref sw, a.Bottom, 1.5, stationary / (double)sorted.Count, a);
            a.Danmaku = dw == 0 ? 0 : d / dw; a.Subtitle = sw == 0 ? 0 : s / sw;
            a.Confidence = Math.Min(1, sorted.Count / 20.0) * Math.Min(1,Math.Abs(a.Danmaku - a.Subtitle)/.35);
            return a;
        }
        static void Evidence(ref double total, ref double weight, double value, double w, double reliability, CaptionAnalysis a)
        { if (reliability <= 0) return; total += value * w * reliability; weight += w * reliability; a.Features++; }
        internal static TextCategory Decide(List<TextCue> events, CaptionAnalysis a)
        {
            TextCategory? declared = events.Count == 0 ? null : events[0].Declared;
            if (declared.HasValue && events.TrueForAll(c => c.Declared == declared)) { a.Confidence = 1; a.Reason = "来源明确声明用途"; return declared.Value; }
            if (a.Samples < 8 || a.Features < 2) { a.Reason = "样本或有效特征不足"; return TextCategory.Uncertain; }
            if (a.Danmaku >= .65 && a.Subtitle >= .65) { a.Reason = "字幕与弹幕证据冲突"; return TextCategory.Uncertain; }
            double r = a.Danmaku - a.Subtitle;
            if (r >= .35) { a.Reason = "水平滚动、并发、时间聚集或多轨道分布"; return TextCategory.Danmaku; }
            double span = events.Max(c => c.End) - events.Min(c => c.Start);
            double coverage = events.Where(c => !c.DurationInferred).Sum(c => c.End - c.Start) / Math.Max(.001, span);
            if (r <= -.35 && a.DurationReliability >= .8 && coverage >= .12) { a.Reason = "顺序显示与固定位置的字幕证据"; return TextCategory.Subtitle; }
            a.Reason = "证据不足或分类差值未达阈值"; return TextCategory.Uncertain;
        }
        internal static List<TextSource> Split(TextSource source)
        {
            List<TextCue> advanced = new List<TextCue>(), uncertain = new List<TextCue>(), normal = new List<TextCue>(), convertedOrdinary = new List<TextCue>();
            foreach (TextCue c in source.Events) {
                if (c.ConverterCategory == TextCategory.Advanced || (c.Effects & ~CueEffects.Unparsed) != 0) advanced.Add(c);
                else if ((c.Effects & CueEffects.Unparsed) != 0) uncertain.Add(c);
                else if (c.ConverterCategory == TextCategory.Danmaku) convertedOrdinary.Add(c);
                else normal.Add(c);
            }
            List<TextSource> result = new List<TextSource>();
            if (normal.Count > 0) {
                // Split only stable, independently supported opposite classes; bilingual
                // static styles alone are never sufficient to manufacture a danmaku track.
                var candidates = normal.GroupBy(c => (c.Motion == CueMotion.Horizontal ? "scroll" : "static") + ":" + c.Style + ":" + c.Layer).ToList();
                var groups = new List<TextSource>();
                if (candidates.Count > 1 && candidates.Count <= 64) foreach (var group in candidates) groups.Add(Track(source, group.ToList(), null));
                if (groups.Count > 1 && groups.TrueForAll(g => g.Analysis.Samples >= 8 && g.Category != TextCategory.Uncertain) && groups.Exists(g => g.Category == TextCategory.Subtitle) && groups.Exists(g => g.Category == TextCategory.Danmaku)) {
                    foreach (TextCategory category in new[] { TextCategory.Subtitle, TextCategory.Danmaku }) result.Add(Track(source, groups.Where(g => g.Category == category).SelectMany(g => g.Events).ToList(), category));
                } else result.Add(Track(source, normal, null));
            }
            if (convertedOrdinary.Count > 0) {
                int ordinary = result.FindIndex(s => s.Category == TextCategory.Danmaku);
                if (ordinary >= 0) {
                    convertedOrdinary.AddRange(result[ordinary].Events);
                    result[ordinary] = Track(source, convertedOrdinary, TextCategory.Danmaku);
                } else result.Add(Track(source, convertedOrdinary, TextCategory.Danmaku));
            }
            if (advanced.Count > 0) result.Add(Track(source, advanced, TextCategory.Advanced));
            if (uncertain.Count > 0) result.Add(Track(source, uncertain, TextCategory.Uncertain));
            if (result.Count == 0) result.Add(Track(source, normal, TextCategory.Uncertain));
            return result;
        }
        static TextSource Track(TextSource original, List<TextCue> events, TextCategory? forced)
        {
            TextSource source = new TextSource { Title = original.Title, Format = original.Format, Error = original.Error, Loaded = true, AssHeader = original.AssHeader, MetadataDetail = original.MetadataDetail };
            source.Fonts.AddRange(original.Fonts);
            source.OriginalEvents.AddRange(original.OriginalEvents);
            source.Events.AddRange(events); source.Analysis = Analyze(events);
            source.ProtocolAdvanced=events.Exists(c=>(c.Effects & CueEffects.Protocol)!=0);
            source.Category = forced ?? Decide(events, source.Analysis); source.AutoCategory = source.Category;
            source.IntrinsicAdvanced = source.Category == TextCategory.Advanced;
            if (!source.IntrinsicAdvanced) source.Cues.AddRange(events);
            TimedText.Sort(source.Cues);
            if (forced.HasValue) source.Analysis.Reason = forced == TextCategory.Advanced ? "高级协议、有效动画或特殊绘图" : forced == TextCategory.Uncertain ? "关键表现信息无法可靠解析" : "稳定的混合事件子集合";
            if (forced.HasValue && events.Count > 0 && events[0].Converter.Length > 0 && events.TrueForAll(c => c.ConverterCategory == forced && c.Converter == events[0].Converter)) { source.Analysis.Reason = events[0].Converter + (forced == TextCategory.Advanced ? " 高级表现" : " 普通弹幕"); source.Analysis.Confidence = 1; }
            if (forced == TextCategory.Advanced) source.Analysis.Confidence = 1;
            if (source.Category == TextCategory.Uncertain) source.Analysis.Confidence = 0;
            source.Detail = events.Count + (source.IntrinsicAdvanced ? source.AssRenderable ? " 条 ASS 特效" : " 条 · 暂不播放" : source.Category == TextCategory.Uncertain ? " 条 · 用途待确认" : source.Category == TextCategory.Subtitle ? " 条字幕" : " 条普通弹幕");
            if (source.MetadataDetail.Length > 0) source.Detail += " · " + source.MetadataDetail;
            return source;
        }
    }
}

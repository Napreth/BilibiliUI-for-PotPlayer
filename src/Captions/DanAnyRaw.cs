using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace BilibiliPotPlayer
{
    // Independent reader of the documented DanUni wire fields. Does not load
    // dan-any's database, adapters, scripts, identifiers or a JavaScript engine.
    internal static class DanAnyRaw
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = TimedText.MaxBytes, RecursionLimit = 32 }; }
        static Dictionary<string, object> Object(object value) { return value as Dictionary<string, object>; }
        static object Get(Dictionary<string, object> item, string name) { object value; return item != null && item.TryGetValue(name, out value) ? value : null; }
        static double Number(object value, double fallback)
        { double n; return value != null && Double.TryParse(Convert.ToString(value, Inv), NumberStyles.Float, Inv, out n) && !Double.IsNaN(n) && !Double.IsInfinity(n) ? n : fallback; }
        static string String(object value) { return value as string ?? ""; }
        static TextCue Cue(double seconds, int mode, int size, int color, int? weight, string content, object extraValue, string platform = "")
        {
            Dictionary<string, object> extra = Object(extraValue);
            if (extraValue is string && ((string)extraValue).Length > 0) extra = Object(Json().DeserializeObject((string)extraValue));
            var bili = Object(Get(extra, "bili")); string payload = content;
            int original = (int)Number(Get(bili, "mode"), 0);
            if (original >= 1 && original <= 9) mode = original;
            if (Get(bili, "adv") is string) { mode = 7; payload = String(Get(bili, "adv")); }
            else if (Get(bili, "code") is string) { mode = 8; payload = String(Get(bili, "code")); }
            else if (Get(bili, "bas") is string) { mode = 9; payload = String(Get(bili, "bas")); }
            if (seconds < 0 || seconds > 604800 || content.Length == 0 || content.Length > 16384 || payload.Length > 16384) return null;
            if (platform.Length > 128) platform = platform.Substring(0, 128);
            return new TextCue { Start = seconds, End = seconds + 8, Text = content, RawContent = payload, Mode = mode, Size = size, Color = color & 0xFFFFFF, Weight = weight, DurationInferred = true, Origin = "dan-any Raw" + (platform.Length > 0 ? " / " + platform : ""), ConverterCategory = mode >= 7 ? TextCategory.Advanced : TextCategory.Danmaku };
        }
        static int UniMode(object value)
        {
            string name = String(value); int mode;
            if (name == "Normal") return 1; if (name == "Bottom") return 4; if (name == "Top") return 5; if (name == "Reverse") return 6; if (name == "Ext") return 100;
            if (!Int32.TryParse(Convert.ToString(value ?? 0, Inv), NumberStyles.Integer, Inv, out mode)) throw new InvalidDataException("Raw 模式无效");
            switch (mode) { case 0: return 1; case 1: return 4; case 2: return 5; case 3: return 6; case 4: return 100; default: throw new InvalidDataException("Raw 模式无效"); }
        }
        static void Add(List<TextCue> result, TextCue cue)
        { if (cue != null) { if (result.Count >= TimedText.MaxCues) throw new InvalidDataException("Raw 条目超限"); result.Add(cue); } }
        static List<TextCue> Legacy(byte[] bytes)
        {
            var root = Object(Json().DeserializeObject(Utf8.GetString(bytes)));
            var list = Get(root, "list") as object[];
            if (list == null || list.Length > TimedText.MaxCues || Object(Get(root, "config")) == null || Object(Get(root, "context")) == null) throw new InvalidDataException("Raw JSON 结构无效");
            var result = new List<TextCue>();
            foreach (object entry in list) {
                var item = Object(entry); if (item == null) continue;
                var original = Object(Get(item, "extra"));
                if (original != null) {
                    double time = Number(Get(original, "progress"), 0);
                    int? weight = Get(original, "weight") == null ? (int?)null : (int)Number(Get(original, "weight"), 0);
                    Add(result, Cue(time, UniMode(Get(original, "mode")), (int)Number(Get(original, "fontsize"), 25), (int)Number(Get(original, "color"), 0xFFFFFF), weight, String(Get(original, "content")), Get(original, "extra") ?? Get(original, "extraStr"), String(Get(original, "platform"))));
                } else {
                    int type = (int)Number(Get(item, "type"), 1), sizeType = (int)Number(Get(item, "fontSizeType"), 1);
                    if (type < 1 || type > 3) continue;
                    var rgb = Object(Get(item, "color"));
                    int color = ((int)Number(Get(rgb, "r"), 255) & 255) << 16 | ((int)Number(Get(rgb, "g"), 255) & 255) << 8 | ((int)Number(Get(rgb, "b"), 255) & 255);
                    Add(result, Cue(Number(Get(item, "time"), -1), type == 2 ? 4 : type == 3 ? 5 : 1, sizeType == 2 ? 36 : 25, color, null, String(Get(item, "content")), null, "bili"));
                }
            }
            return result;
        }
        sealed class Wire
        {
            readonly byte[] data; readonly int end; int offset;
            internal Wire(byte[] bytes, int start, int length) { data = bytes; offset = start; end = start + length; }
            internal bool More { get { return offset < end; } }
            internal ulong Varint()
            {
                ulong value = 0;
                for (int shift = 0; shift < 70; shift += 7) {
                    if (offset >= end) throw new InvalidDataException("Raw Protobuf 被截断");
                    byte b = data[offset++]; if (shift == 63 && b > 1) throw new InvalidDataException("Raw Protobuf 数值溢出");
                    value |= (ulong)(b & 127) << shift; if (b < 128) return value;
                }
                throw new InvalidDataException("Raw Protobuf 数值过长");
            }
            internal int Tag()
            { ulong tag = Varint(); if (tag < 8 || tag > Int32.MaxValue) throw new InvalidDataException("Raw Protobuf 字段无效"); return (int)tag; }
            internal Wire Message()
            { ulong length = Varint(); if (length > (ulong)(end - offset)) throw new InvalidDataException("Raw Protobuf 长度无效"); var result = new Wire(data, offset, (int)length); offset += (int)length; return result; }
            internal string Text() { Wire part = Message(); return Utf8.GetString(data, part.offset, part.end - part.offset); }
            internal void Skip(int wire)
            {
                if (wire == 0) { Varint(); return; } if (wire == 2) { Message(); return; }
                int count = wire == 1 ? 8 : wire == 5 ? 4 : -1;
                if (count < 0 || count > end - offset) throw new InvalidDataException("Raw Protobuf 类型无效"); offset += count;
            }
        }
        static List<TextCue> Protobuf(byte[] bytes)
        {
            var result = new List<TextCue>(); var document = new Wire(bytes, 0, bytes.Length); int total = 0;
            while (document.More) {
                int tag = document.Tag(); if (tag != 10) { document.Skip(tag & 7); continue; }
                if (++total > TimedText.MaxCues) throw new InvalidDataException("Raw 条目超限");
                Wire item = document.Message(); int progress = 0, mode = 0, size = 25, color = 0; int? weight = null; string content = "", extra = "", oldExtra = "", platform = "";
                while (item.More) {
                    int field = item.Tag(), number = field >> 3, wire = field & 7;
                    if ((number == 3 || number == 4 || number == 5 || number == 6 || number == 10) && wire != 0 || (number == 8 || number == 13 || number == 14 || number == 15) && wire != 2) throw new InvalidDataException("Raw Protobuf 字段类型无效");
                    if (wire == 0 && (number == 3 || number == 4 || number == 5 || number == 6 || number == 10)) {
                        int n = unchecked((int)item.Varint());
                        if (number == 3) progress = n; else if (number == 4) mode = n; else if (number == 5) size = n; else if (number == 6) color = n; else weight = n;
                    } else if (wire == 2 && (number == 8 || number == 13 || number == 14 || number == 15)) {
                        string text = item.Text(); if (text.Length > 16384) throw new InvalidDataException("Raw 文本超限");
                        if (number == 8) content = text; else if (number == 13) platform = text; else if (number == 14) oldExtra = text; else extra = text;
                    } else item.Skip(wire);
                }
                Add(result, Cue(progress / 1000.0, UniMode(mode), size, color, weight, content, extra.Length > 0 ? extra : oldExtra, platform));
            }
            return result;
        }
        internal static List<TextCue> Read(Dictionary<string, string> fields)
        {
            string codec, encoding, legacy, config, protobuf;
            if (!fields.TryGetValue("RawCompressType", out codec)) codec = "gzip";
            if (!fields.TryGetValue("RawBaseType", out encoding)) encoding = "base64";
            if (fields.TryGetValue("Raw", out legacy)) {
                if (fields.ContainsKey("RawPb") || fields.ContainsKey("RawConf")) throw new InvalidDataException("Raw 版本字段冲突");
                return Legacy(RawCompression.Decompress(RawCompression.DecodeBase(legacy, encoding), codec, TimedText.MaxBytes));
            }
            if (!fields.TryGetValue("RawConf", out config) || !fields.TryGetValue("RawPb", out protobuf)) throw new InvalidDataException("Raw 元数据不完整");
            var conf = Object(Json().DeserializeObject(Utf8.GetString(RawCompression.Decompress(RawCompression.DecodeBase(config, encoding), codec, 1024 * 1024))));
            if (Object(Get(conf, "config")) == null || Object(Get(conf, "context")) == null) throw new InvalidDataException("Raw 配置结构无效");
            return Protobuf(RawCompression.Decompress(RawCompression.DecodeBase(protobuf, encoding), codec, TimedText.MaxBytes));
        }
    }
}

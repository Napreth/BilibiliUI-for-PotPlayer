using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using BilibiliPotPlayer;

internal static class DanAnyRawChecks
{
    static string Folder { get { return Path.Combine(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..")), "tests", "fixtures", "dan-any"); } }
    static string Fixture(string name) { return File.ReadAllText(Path.Combine(Folder, name), Encoding.UTF8); }
    static List<TextSource> Parse(string text) { return TimedText.Parse(text, "dan-any.ass", ".ass"); }
    static byte[] Gzip(byte[] data)
    {
        using (var output = new MemoryStream()) {
            using (var gzip = new GZipStream(output, CompressionMode.Compress, true)) gzip.Write(data, 0, data.Length);
            return output.ToArray();
        }
    }
    static string Pb(byte[] bytes)
    { return Regex.Replace(Fixture("v2-gzip-base64.ass"), @"(?m)^;RawPb:[^\r\n]*", ";RawPb: " + Convert.ToBase64String(Gzip(bytes))); }
    static bool Rejected(Action action)
    { try { action(); return false; } catch (InvalidDataException) { return true; } catch (FormatException) { return true; } }
    static bool Fallback(string text)
    {
        var sources = Parse(text);
        return sources.Count == 1 && sources[0].Category == TextCategory.Danmaku && sources[0].Events.Count == 7 && sources[0].Error.Length == 0 && sources[0].MetadataDetail.Contains("按 ASS 识别");
    }
    static byte[] Pixels(Bitmap bitmap)
    {
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try { byte[] bytes = new byte[data.Stride * bitmap.Height]; Marshal.Copy(data.Scan0, bytes, 0, bytes.Length); return bytes; }
        finally { bitmap.UnlockBits(data); }
    }
    internal static void Run(Action<bool, bool, string> equal)
    {
        Action<bool, string> check = (v, m) => equal(true, v, m);
        string path = Environment.GetEnvironmentVariable("PATH");
        try {
            Environment.SetEnvironmentVariable("PATH", "");
            foreach (string name in new[] { "v2-zstd-base16384.ass", "v2-gzip-base64.ass", "v2-brotli-base64.ass", "v1-gzip-base64.ass" }) {
                var sources = Parse(Fixture(name));
                check(sources.Count == 2 && sources.Single(s => s.Category == TextCategory.Danmaku).Events.Count == 4 && sources.Single(s => s.Category == TextCategory.Advanced).Events.Count == 3, "real upstream Raw fixture splits four ordinary and three advanced events: " + name);
                TextSource ordinary = sources.Single(s => s.Category == TextCategory.Danmaku), advanced = sources.Single(s => s.Category == TextCategory.Advanced);
                check(ordinary.Cues.Single(c => c.Text == "bottom").Mode == 4 && ordinary.Cues.Single(c => c.Text == "reverse").Mode == 6, "Raw restores original bottom/reverse modes independently of rendered geometry: " + name);
                check(advanced.AssRenderable && !advanced.ProtocolAdvanced && advanced.OriginalEvents.Count == 7 && advanced.Events.Select(c => c.Mode).SequenceEqual(new[] { 7, 8, 9 }), "Raw provenance preserves original advanced types while the existing ASS remains renderable: " + name);
                check(advanced.OriginalEvents.TrueForAll(c => c.Origin == "dan-any Raw / bili"), "Raw retains platform provenance without reading user identifiers: " + name);
                check(advanced.Events[0].Start == 5.4 && Math.Abs(advanced.OriginalEvents.Single(c => c.Mode == 7).Start - 5.043) < .000001, "one-digit upstream centiseconds match without rewriting authored ASS timing: " + name);
                var original = new TextSource(); AssEvents.Parse(Fixture("no-raw.ass"), original);
                check(sources.SelectMany(s => s.Events).OrderBy(c => c.AssOrder).Select(c => c.AssFields).SequenceEqual(original.Events.Select(c => c.AssFields)), "Raw classification preserves original tags, styles, layers and composition order: " + name);
            }
        } finally { Environment.SetEnvironmentVariable("PATH", path); }

        using (var catalog = new CaptionCatalog(IntPtr.Zero, null, new CaptionOptions(), null, delegate { }))
        using (var scene = new AssScene())
        using (var direct = new LibassRenderer())
        using (var image = new Bitmap(480, 270, PixelFormat.Format32bppPArgb))
        using (var referenceImage = new Bitmap(480, 270, PixelFormat.Format32bppPArgb)) {
            TextSource advanced = Parse(Fixture("v2-zstd-base16384.ass")).Single(s => s.Category == TextCategory.Advanced);
            advanced.Enabled = true; catalog.Sources.Add(advanced); catalog.Revision++;
            scene.Render(image, catalog, 6500, new List<DanmakuPlacement>());
            direct.SetTrack("reference", AssScene.Script(advanced, advanced.Events)); direct.Render(referenceImage, "reference", 6500);
            byte[] bytes = Pixels(image);
            check(bytes.Where((v, i) => i % 4 == 3).Any(v => v > 0) && bytes.SequenceEqual(Pixels(referenceImage)), "Raw-classified advanced ASS matches direct libass pixels at authored positions and timing");
            catalog.Options.AssEffectsEnabled = false; catalog.Revision++;
            scene.Render(image, catalog, 6500, new List<DanmakuPlacement>());
            check(!Pixels(image).Where((v, i) => i % 4 == 3).Any(v => v > 0), "advanced source filtering clears Raw-classified ASS pixels");
        }

        var noRaw = Parse(Fixture("no-raw.ass"));
        check(noRaw.Count == 1 && noRaw[0].Category == TextCategory.Danmaku && noRaw[0].Events.Count == 7 && noRaw[0].OriginalEvents.Count == 0, "known dan-any F styles use the converter fallback when Raw is absent");
        var removed = Parse(Fixture("removed-event.ass"));
        check(removed.Sum(s => s.Events.Count) == 6 && removed[0].OriginalEvents.Count == 7 && !removed.SelectMany(s => s.Events).Any(c => c.Text == "normal ｛中文｝"), "Raw records omitted from the ASS body are retained as provenance without being replayed");
        var old = Parse(Fixture("biliy-gzip-base64.ass"));
        check(old.Count == 1 && old[0].Category == TextCategory.Danmaku && old[0].Cues.Single().Mode == 4 && old[0].OriginalEvents.Count == 1, "legacy biliy RGB/time/type Raw is recovered with gzip/base64 defaults");
        string noSignature = Regex.Replace(Fixture("v2-gzip-base64.ass"), @"(?m)^Original Script:[^\r\n]*", "Original Script: unspecified");
        check(Parse(noSignature).Single(s => s.Category == TextCategory.Advanced).Events.Count == 2, "validated Raw can classify without a signature but does not assume the converter's timestamp bug");
        string generic = Regex.Replace(Fixture("no-raw.ass"), @"(?m)^Original Script:[^\r\n]*", "Original Script: unspecified");
        check(Parse(generic)[0].Category == TextCategory.Uncertain, "F1 alone does not fingerprint an unknown ASS source");
        check(Parse(Fixture("no-raw.ass").Replace(",F1,", ",Novel,"))[0].Category == TextCategory.Uncertain, "unknown styles in a signed file fall back to generic evidence");

        check(Fallback(Fixture("v2-gzip-base64.ass").Replace(";RawCompressType: gzip", ";RawCompressType: unsupported")), "unsupported Raw compression falls back without disabling ASS playback");
        check(Fallback(Regex.Replace(Fixture("v2-gzip-base64.ass"), @"(?m)^;RawPb:[^\r\n]*", ";RawPb: !!!")), "invalid encoded Raw falls back without creating a file error");
        check(Fallback(Regex.Replace(Fixture("v2-gzip-base64.ass"), @"(?m)^;RawConf:[^\r\n]*", "")), "partial Raw fields do not prevent ASS playback");
        check(Fallback(Fixture("v2-gzip-base64.ass") + ";RawBaseType: base64\n"), "duplicate Raw fields are not silently trusted");
        check(Fallback(Pb(new byte[] { 10, 128 })), "truncated Protobuf lengths fall back atomically");
        check(Fallback(Pb(new byte[] { 10, 6, 26, 1, 0, 66, 1, 120 })), "wrong Protobuf field wire types are rejected");
        check(Fallback(Pb(Enumerable.Repeat((byte)255, 11).ToArray())), "overflowing Protobuf varints cannot escape the Raw fallback");
        byte[] reference = File.ReadAllBytes(Path.Combine(Folder, "reference.pb"));
        check(Parse(Pb(reference.Concat(new byte[] { 248, 1, 123 }).ToArray())).Single(s => s.Category == TextCategory.Advanced).Events.Count == 3, "unknown valid Protobuf fields are skipped for forward compatibility");

        var serializer = new JavaScriptSerializer();
        var manifest = (Dictionary<string, object>)serializer.DeserializeObject(File.ReadAllText(Path.Combine(Folder, "manifest.json")));
        foreach (var entry in (object[])manifest["fixtures"]) {
            var vector = (Dictionary<string, object>)entry;
            if (vector.ContainsKey("base16384")) {
                byte[] decoded = RawCompression.DecodeBase((string)vector["base16384"], "base18384");
                check(BitConverter.ToString(decoded).Replace("-", "").ToLowerInvariant() == (string)vector["hex"], "Base16384 matches upstream vector across complete and partial blocks: " + decoded.Length);
            } else {
                using (SHA256 hash = SHA256.Create()) check(BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(Path.Combine(Folder, (string)vector["name"])))).Replace("-", "").ToLowerInvariant() == (string)vector["sha256"], "checked-in upstream fixture hash: " + vector["name"]);
            }
        }
        check(Rejected(() => RawCompression.DecodeBase("一㴆", "base18384")), "invalid Base16384 length markers are rejected");
        check(Rejected(() => RawCompression.DecodeBase("丁㴁", "base18384")), "nonzero Base16384 padding is rejected");
        check(Rejected(() => RawCompression.Decompress(Gzip(reference), "gzip", 8)), "gzip output is bounded before accumulation");
        byte[] badGzip = Gzip(reference); badGzip[badGzip.Length - 8] ^= 1;
        check(Rejected(() => RawCompression.Decompress(badGzip, "gzip", 4096)), "gzip checksum corruption is rejected");
        check(Rejected(() => RawCompression.Decompress(Gzip(reference).Take(16).ToArray(), "gzip", 4096)), "truncated gzip is rejected");
        string compressed = Regex.Match(Fixture("v2-brotli-base64.ass"), @"(?m)^;RawPb: ([^\r\n]*)").Groups[1].Value;
        check(Rejected(() => RawCompression.Decompress(Convert.FromBase64String(compressed), "brotli", 8)), "native Brotli output is bounded");
        compressed = Regex.Match(Fixture("v2-zstd-base16384.ass"), @"(?m)^;RawPb: ([^\r\n]*)").Groups[1].Value;
        check(Rejected(() => RawCompression.Decompress(RawCompression.DecodeBase(compressed, "base18384"), "zstd", 8)), "native Zstandard output is bounded");
        check(Rejected(() => RawCompression.Decompress(new byte[] { 1, 2, 3 }, "zstd", 1024)), "invalid native compressed input is rejected");
        check(RawCompression.DecodeBase("一㴁", "base16384").SequenceEqual(new byte[] { 0 }), "correctly spelled Base16384 label is accepted as an alias");

        string legacyConflict = serializer.Serialize(new { config = new { }, context = new { }, list = new[] {
            new { extra = new { progress = 1.234, mode = 0, content = "normal {中文}" } },
            new { extra = new { progress = 1.234, mode = 4, content = "normal {中文}" } }
        } });
        var conflict = Parse(Fixture("no-raw.ass") + ";Raw: " + Convert.ToBase64String(Gzip(Encoding.UTF8.GetBytes(legacyConflict))) + "\n");
        check(conflict.Single(s => s.Category == TextCategory.Uncertain).Events.Count == 1 && conflict.Sum(s => s.Events.Count) == 7, "conflicting Raw types for identical text/time remain uncertain instead of choosing an arbitrary record");
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace BilibiliPotPlayer
{
    // Adapt installed scripts in place so native engine names, account storage
    // and language preferences continue to refer to their original filenames.
    internal static class NativeTranslationScripts
    {
        internal const string Marker = "// Bilibili native translation bridge v1\n";
        const string Begin = "namespace BilibiliOriginalEngine\n{\n", End = "\n}\n// Bilibili native translation bridge callbacks\n";
        internal sealed class Entry { public string Name, OriginalHash, WrappedHash, PreviousHash, Backup; }
        static bool Owned(Entry entry, string hash) { return hash == entry.WrappedHash || hash == entry.PreviousHash; }
        static readonly string[] Methods = { "GetTitle", "GetVersion", "GetDesc", "GetLoginTitle", "GetLoginDesc", "GetUserText", "GetPasswordText", "ServerLogin", "ServerLogout", "GetWebAccountUrl", "GetWebAccountDomain", "GetSrcLangs", "GetDstLangs", "OnInitialize", "OnFinalize" };
        internal static string Hash(byte[] bytes) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", ""); }
        internal static string Wrap(string original, string name)
        {
            if (original.StartsWith(Marker, StringComparison.Ordinal)) return original;
            if (original.Length > 1024 * 1024 || Regex.IsMatch(original, @"(?m)^\s*#\s*include\b")) throw new InvalidDataException("翻译扩展含有暂不支持的 include 指令或内容过大");
            // Mask comments and strings before inspecting exported declarations.
            string code = Regex.Replace(original, "//[^\r\n]*|/\\*[\\s\\S]*?\\*/|\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'", m => new string(' ', m.Length), RegexOptions.None, TimeSpan.FromSeconds(1));
            if (!Regex.IsMatch(code, @"\bstring\s+Translate\s*\(\s*string\s+\w+\s*,\s*string\s*&in\s+\w+\s*,\s*string\s*&in\s+\w+\s*\)")) throw new InvalidDataException("不是受支持的原生翻译扩展接口");
            // Some native lookup paths compare declarations without namespace
            // qualifiers. Keep exactly one exported Translate declaration.
            var implementation = new StringBuilder(original);
            MatchCollection identifiers = Regex.Matches(code, @"\bTranslate\b", RegexOptions.None, TimeSpan.FromSeconds(1));
            for (int i = identifiers.Count - 1; i >= 0; i--) implementation.Remove(identifiers[i].Index, identifiers[i].Length).Insert(identifiers[i].Index, "BilibiliTranslateOriginal");
            var result = new StringBuilder(Marker).Append(Begin).Append(implementation).Append(End);
            foreach (string method in Methods)
            {
                Match match = Regex.Match(code, @"\b(?<type>void|string|array\s*<\s*string\s*>)\s+" + method + @"\s*\((?<args>[^()]*)\)\s*\{", RegexOptions.None, TimeSpan.FromSeconds(1));
                if (!match.Success) continue;
                string type = match.Groups["type"].Value, args = match.Groups["args"].Value;
                var names = new List<string>();
                if (args.Trim().Length > 0) foreach (string arg in args.Split(','))
                {
                    Match parameter = Regex.Match(arg, @"(\w+)\s*(?:=[\s\S]*)?$", RegexOptions.None, TimeSpan.FromSeconds(1));
                    if (!parameter.Success) throw new InvalidDataException("无法转发翻译扩展参数"); names.Add(parameter.Groups[1].Value);
                }
                result.Append(type).Append(' ').Append(method).Append('(').Append(args).Append(") { ").Append(type == "void" ? "" : "return ").Append("BilibiliOriginalEngine::").Append(method).Append('(').Append(String.Join(",", names)).Append("); }\n");
            }
            string safeName = name.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "");
            result.Append(@"
uintptr BilibiliTranslationLibrary = 0;
class BilibiliTranslationCached
{
    string key, text, source, target;
}
array<BilibiliTranslationCached@> BilibiliTranslationCache;
uint64 BilibiliTranslationContext()
{
    if (BilibiliTranslationLibrary == 0)
    {
        string folder = HostGetScriptFolder();
        if (folder.length() > 0 && folder.substr(folder.length() - 1, 1) != ""\\"" && folder.substr(folder.length() - 1, 1) != ""/"") folder += ""\\"";
        BilibiliTranslationLibrary = HostLoadLibrary(folder + ""BilibiliTranslation.dll"");
    }
    if (BilibiliTranslationLibrary == 0) return 0;
    uintptr proc = HostGetProcAddress(BilibiliTranslationLibrary, ""bili_translation_context"");
    return proc == 0 ? 0 : HostCallProcUIntPtr(proc, ""I"", 0);
}
string Translate(string Text, string &in SrcLang, string &in DstLang)
{
    uint64 context = BilibiliTranslationContext();
    if (Text.length() >= 9 && Text.substr(0, 9) == ""\xe2\x81\xa3\xe2\x81\xa3\xe2\x81\xa3"")
    {
        if (context != 0)
        {
            uintptr ready = HostGetProcAddress(BilibiliTranslationLibrary, ""bili_translation_ready"");
            if (ready != 0) HostCallProcInt(ready, ""Q"", context);
        }
        return ""\xe2\x81\xa3"";
    }
    // This marker belongs only to our bounded temporary SRT. Native caches
    // distinguish generations; the selected engine receives the original text.
    if (Text.length() >= 24 && Text.substr(0, 3) == ""\xe2\x81\xa3"" && Text.substr(19, 1) == "":"")
    {
        int boundary = Text.findFirst(""\xe2\x81\xa3"", 20);
        if (boundary >= 20 && boundary <= 11000)
        {
            if (context == 0 || parseUInt(Text.substr(3, 16), 16) != context) return """";
            Text = HostBase64Dec(Text.substr(20, boundary - 20));
        }
    }
    string source = SrcLang, target = DstLang;
    string key = formatUInt(context) + ""\x1f"" + source + ""\x1f"" + target + ""\x1f"" + Text;
    string result;
    bool cached = false;
    for (uint i = 0; i < BilibiliTranslationCache.length(); i++)
        if (BilibiliTranslationCache[i].key == key)
        {
            result = BilibiliTranslationCache[i].text;
            SrcLang = BilibiliTranslationCache[i].source; DstLang = BilibiliTranslationCache[i].target;
            cached = true; break;
        }
    if (!cached)
    {
        result = BilibiliOriginalEngine::BilibiliTranslateOriginal(Text, SrcLang, DstLang);
        if (context != 0 && result.length() > 0 && result.length() <= 32768)
        {
            BilibiliTranslationCached@ item = BilibiliTranslationCached();
            item.key = key; item.text = result; item.source = SrcLang; item.target = DstLang;
            if (BilibiliTranslationCache.length() >= 2048) BilibiliTranslationCache.removeAt(0);
            BilibiliTranslationCache.insertLast(item);
        }
    }
    if (context != 0 && Text.length() > 0 && Text.length() <= 8192 && result.length() > 0 && result.length() <= 32768)
    {
        uintptr proc = HostGetProcAddress(BilibiliTranslationLibrary, ""bili_translation_result"");
        string metadata = """).Append(safeName).Append(@"\n"" + source + ""\n"" + target;
        if (proc != 0 && metadata.length() <= 4096)
            HostCallProcInt(proc, ""QPPPII"", context, HostString2UIntPtr(Text), HostString2UIntPtr(result), HostString2UIntPtr(metadata), uint(Text.length()) | (uint(result.length()) << 16), metadata.length());
    }
    return result;
}
");
            return result.ToString();
        }
        static string Folder(string playerRoot) { return Path.Combine(Path.GetFullPath(playerRoot), "Extension", "Subtitle", "Translate"); }
        internal static int Prepare(string playerRoot, string stateRoot)
        {
            string folder = Folder(playerRoot), state = Path.Combine(Path.GetFullPath(stateRoot), "native-translation");
            if (!Directory.Exists(folder)) return 0;
            Directory.CreateDirectory(state); string manifest = Path.Combine(state, "scripts.json");
            var json = new JavaScriptSerializer();
            var entries = File.Exists(manifest) ? json.Deserialize<List<Entry>>(File.ReadAllText(manifest)) : new List<Entry>();
            if (entries == null) entries = new List<Entry>();
            int count = 0;
            foreach (string file in Directory.GetFiles(folder, "*.as"))
            {
                byte[] bytes = File.ReadAllBytes(file); string source;
                try { source = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); } catch (DecoderFallbackException) { continue; }
                string name = Path.GetFileName(file); Entry existing = entries.Find(e => e.Name == name);
                if (source.StartsWith(Marker, StringComparison.Ordinal))
                {
                    if (existing == null || !Owned(existing, Hash(bytes)) || Path.GetFileName(existing.Backup) != existing.Backup) continue;
                    byte[] saved = File.ReadAllBytes(Path.Combine(state, existing.Backup));
                    if (Hash(saved) != existing.OriginalHash) throw new InvalidDataException("翻译扩展原始备份校验失败");
                    byte[] replacement = new UTF8Encoding(false).GetBytes(Wrap(new UTF8Encoding(false, true).GetString(saved).TrimStart('\uFEFF'), name));
                    if (Hash(replacement) != existing.WrappedHash)
                    {
                        existing.PreviousHash = Hash(bytes); existing.WrappedHash = Hash(replacement); File.WriteAllText(manifest, json.Serialize(entries), new UTF8Encoding(false));
                        string updateFile = file + ".bilibili.tmp"; File.WriteAllBytes(updateFile, replacement); File.Replace(updateFile, file, null, true);
                    }
                    count++; continue;
                }
                string wrapped;
                try { wrapped = Wrap(source, name); } catch (InvalidDataException) { continue; }
                var entry = new Entry { Name = name, OriginalHash = Hash(bytes), Backup = Hash(Encoding.UTF8.GetBytes(name)) + ".as" };
                File.WriteAllBytes(Path.Combine(state, entry.Backup), bytes);
                byte[] output = new UTF8Encoding(false).GetBytes(wrapped); entry.WrappedHash = Hash(output);
                entries.RemoveAll(e => e.Name == name); entries.Add(entry);
                // Persist the recovery record before modifying the installed script.
                File.WriteAllText(manifest, json.Serialize(entries), new UTF8Encoding(false));
                string temporary = file + ".bilibili.tmp";
                File.WriteAllBytes(temporary, output); File.Replace(temporary, file, null, true); count++;
            }
            if (count > 0) PrepareLibrary(folder, state);
            return count;
        }
        static void PrepareLibrary(string folder, string state)
        {
            const string name = "BilibiliTranslation.dll";
            string source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name), file = Path.Combine(folder, name), manifest = Path.Combine(state, "library.json");
            byte[] output = File.ReadAllBytes(source); string hash = Hash(output);
            var json = new JavaScriptSerializer();
            Entry entry = File.Exists(manifest) ? json.Deserialize<Entry>(File.ReadAllText(manifest)) : null;
            byte[] current = File.Exists(file) ? File.ReadAllBytes(file) : null;
            if (entry == null || current == null || !Owned(entry, Hash(current)))
            {
                entry = new Entry { Name = name, OriginalHash = current == null ? "" : Hash(current), Backup = "original-bridge.dll" };
                if (current != null) File.WriteAllBytes(Path.Combine(state, entry.Backup), current);
            }
            entry.PreviousHash = current == null ? null : Hash(current); entry.WrappedHash = hash;
            File.WriteAllText(manifest, json.Serialize(entry), new UTF8Encoding(false));
            if (current != null && Hash(current) == hash) return;
            string temporary = file + ".bilibili.tmp"; File.WriteAllBytes(temporary, output);
            if (current == null) File.Move(temporary, file); else File.Replace(temporary, file, null, true);
        }
        internal static void Restore(string playerRoot, string stateRoot)
        {
            string folder = Folder(playerRoot), state = Path.Combine(Path.GetFullPath(stateRoot), "native-translation"), manifest = Path.Combine(state, "scripts.json");
            if (!File.Exists(manifest)) return;
            var entries = new JavaScriptSerializer().Deserialize<List<Entry>>(File.ReadAllText(manifest));
            if (entries == null) return;
            foreach (Entry entry in entries)
            {
                if (Path.GetFileName(entry.Name) != entry.Name || Path.GetFileName(entry.Backup) != entry.Backup) throw new InvalidDataException("翻译扩展恢复记录路径无效");
                string file = Path.Combine(folder, entry.Name), backup = Path.Combine(state, entry.Backup);
                if (File.Exists(file) && Owned(entry, Hash(File.ReadAllBytes(file))))
                {
                    if (!File.Exists(backup) || Hash(File.ReadAllBytes(backup)) != entry.OriginalHash) throw new InvalidDataException("翻译扩展原始备份缺失或校验失败");
                    File.WriteAllBytes(file, File.ReadAllBytes(backup));
                }
            }
            string libraryManifest = Path.Combine(state, "library.json");
            if (!File.Exists(libraryManifest)) return;
            Entry library = new JavaScriptSerializer().Deserialize<Entry>(File.ReadAllText(libraryManifest));
            if (library == null || library.Name != "BilibiliTranslation.dll" || library.Backup != "original-bridge.dll") throw new InvalidDataException("翻译桥接恢复记录无效");
            string installed = Path.Combine(folder, library.Name), original = Path.Combine(state, library.Backup);
            if (!File.Exists(installed) || !Owned(library, Hash(File.ReadAllBytes(installed)))) return;
            if (library.OriginalHash != "" && (!File.Exists(original) || Hash(File.ReadAllBytes(original)) != library.OriginalHash)) throw new InvalidDataException("翻译桥接原始备份缺失或校验失败");
            // The process can signal exit slightly before Windows releases its
            // last image mapping. Give that normal shutdown a bounded retry.
            for (int attempt = 0; ; attempt++) try
            {
                if (library.OriginalHash == "") File.Delete(installed);
                else if (File.Exists(original) && Hash(File.ReadAllBytes(original)) == library.OriginalHash) File.WriteAllBytes(installed, File.ReadAllBytes(original));
                break;
            }
            catch (IOException) { if (attempt == 19) throw; Thread.Sleep(100); }
            catch (UnauthorizedAccessException) { if (attempt == 19) throw; Thread.Sleep(100); }
        }
    }
}

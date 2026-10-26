using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using BilibiliPotPlayer;

internal static class NativeTranslationChecks
{
    internal const string Script = "string GetTitle(){return \"Offline test\";}\nstring GetVersion(){return \"1\";}\nstring GetDesc(){return \"Offline only\";}\nstring GetLoginTitle(){return \"Native login\";}\nstring ServerLogin(string User,string Pass){return \"200 ok\";}\nvoid ServerLogout(){}\narray<string> GetSrcLangs(){return {\"\",\"en\"};}\narray<string> GetDstLangs(){return {\"zh-CN\",\"en\"};}\nstring Translate(string Text,string &in SrcLang,string &in DstLang){string value=DstLang+\": \"+Text;SrcLang=\"UTF8\";DstLang=\"UTF8\";return value;}\n";
    internal static byte[] Packet(ulong token, uint pid, string original, string translated, string metadata)
    {
        byte[] a=Encoding.UTF8.GetBytes(original),b=Encoding.UTF8.GetBytes(translated),c=Encoding.UTF8.GetBytes(metadata);
        using(var output=new MemoryStream())using(var writer=new BinaryWriter(output))
        {writer.Write(NativeTranslationPacket.Magic);writer.Write(1);writer.Write(token);writer.Write(pid);writer.Write(a.Length);writer.Write(b.Length);writer.Write(c.Length);writer.Write(a);writer.Write(b);writer.Write(c);return output.ToArray();}
    }
    internal static void Run(Action<bool,bool,string> equal)
    {
        Action<bool,string> check=(value,message)=>equal(true,value,message);
        byte[] bytes=Packet(123,456,"原文\nline","译文\ntranslation","SubtitleTranslate - Offline.as\n\nzh-CN");
        NativeTranslationPacket packet=NativeTranslationPacket.Read(bytes);
        check(packet!=null && packet.Token==123 && packet.Player==456 && packet.Original=="原文\nline" && packet.Translated=="译文\ntranslation" && packet.Target=="zh-CN" && packet.Source=="", "native packet preserves Unicode, multiline text and native language metadata");
        byte[] corrupt=(byte[])bytes.Clone();corrupt[4]=2;
        check(NativeTranslationPacket.Read(corrupt)==null,"native packet rejects unknown protocol versions");
        corrupt=(byte[])bytes.Clone();corrupt[20]=255;corrupt[21]=255;
        check(NativeTranslationPacket.Read(corrupt)==null,"native packet rejects oversized or inconsistent text lengths");
        corrupt=(byte[])bytes.Clone();corrupt[32]=255;
        check(NativeTranslationPacket.Read(corrupt)==null,"native packet rejects malformed UTF-8");
        check(NativeTranslationPacket.Read(Packet(0,1,"a","b","engine.as\nen\nzh-CN"))==null && NativeTranslationPacket.Read(Packet(1,1,"a","b","../engine.as\nen\nzh-CN"))==null,"native packet rejects missing context and path-bearing engine identifiers");
        check(NativeTranslationPacket.Read(Packet(1,1,"a","b","engine.as\nen\nzh-CN\nextra"))==null && NativeTranslationPacket.Read(new byte[31])==null,"native packet rejects extra metadata and truncated headers");
        string exported=NativeSubtitleTranslation.Export(new[]{new TextCue{Start=1.234,End=3.456,Text="<font>literal</font> & text"},new TextCue{Start=Double.NaN,End=9,Text="bad"}});
        check(exported.Contains("00:00:01,234 --> 00:00:03,456") && exported.Contains("&lt;font&gt;literal&lt;/font&gt; &amp; text") && !exported.Contains("bad"),"native SRT input retains source milliseconds, escapes literal markup and rejects invalid timing");
        var cues=new System.Collections.Generic.List<TextCue>();for(int i=0;i<20;i++)cues.Add(new TextCue{Start=i,End=i+1,Text="line"+i});
        exported=NativeSubtitleTranslation.Export(cues);
        check(exported.Contains("line7") && !exported.Contains("line8"),"native input is limited to eight subtitle cues per window");
        exported=NativeSubtitleTranslation.Export(cues,123);
        check(exported.Contains("\u2063000000000000007B:"+Convert.ToBase64String(Encoding.UTF8.GetBytes("line0"))+"\u2063line0") && !exported.Contains("line8"),"native cache generation marker preserves original bytes and the bounded cue input");
        string wrapped=NativeTranslationScripts.Wrap(Script,"SubtitleTranslate - Offline.as");
        check(wrapped.StartsWith(NativeTranslationScripts.Marker) && wrapped.Contains(Script.Replace("Translate(","BilibiliTranslateOriginal(")) && wrapped.Contains("return BilibiliOriginalEngine::ServerLogin(User,Pass)") && wrapped.Contains("BilibiliOriginalEngine::ServerLogout()"),"script adaptation retains the native implementation under a unique internal entry and forwards login APIs");
        check(wrapped.Contains("uint64 context = BilibiliTranslationContext()") && wrapped.Contains("bili_translation_result") && !wrapped.Contains("HostFreeLibrary"),"script captures a generation before translation and keeps the native bridge loaded across worker callbacks");
        check(NativeTranslationScripts.Wrap(wrapped,"SubtitleTranslate - Offline.as")==wrapped,"script preparation never nests its own wrapper");
        bool rejected=false;try{NativeTranslationScripts.Wrap("// string Translate(string x,string &in y,string &in z){}\nstring GetTitle(){return \"fake\";}","fake.as");}catch(InvalidDataException){rejected=true;}
        check(rejected,"commented-out translation signatures cannot enable script adaptation");
        string root=Path.Combine(Path.GetTempPath(),"native-translation-check-"+Guid.NewGuid().ToString("N")),folder=Path.Combine(root,"player","Extension","Subtitle","Translate"),state=Path.Combine(root,"state");
        Directory.CreateDirectory(folder);string file=Path.Combine(folder,"SubtitleTranslate - Offline.as");byte[] original=new UTF8Encoding(true).GetPreamble();byte[] body=Encoding.UTF8.GetBytes(Script);var originalBytes=new byte[original.Length+body.Length];Buffer.BlockCopy(original,0,originalBytes,0,original.Length);Buffer.BlockCopy(body,0,originalBytes,original.Length,body.Length);File.WriteAllBytes(file,originalBytes);
        try
        {
            check(NativeTranslationScripts.Prepare(Path.Combine(root,"player"),state)==1 && File.ReadAllText(file).StartsWith(NativeTranslationScripts.Marker),"installation prepares the existing native script under its original filename");
            string library=Path.Combine(folder,"BilibiliTranslation.dll");
            check(File.Exists(library) && NativeTranslationScripts.Hash(File.ReadAllBytes(library))==NativeTranslationScripts.Hash(File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BilibiliTranslation.dll"))),"installation puts the verified result bridge beside the native translator scripts");
            string first=NativeTranslationScripts.Hash(File.ReadAllBytes(file));NativeTranslationScripts.Prepare(Path.Combine(root,"player"),state);
            check(NativeTranslationScripts.Hash(File.ReadAllBytes(file))==first,"repeated installation is idempotent and retains the recovery copy");
            NativeTranslationScripts.Restore(Path.Combine(root,"player"),state);
            check(NativeTranslationScripts.Hash(File.ReadAllBytes(file))==NativeTranslationScripts.Hash(originalBytes),"uninstall restores original script bytes including their BOM");
            check(!File.Exists(library),"uninstall removes a bridge that was originally absent");
            File.WriteAllText(library,"preexisting library");NativeTranslationScripts.Prepare(Path.Combine(root,"player"),state);NativeTranslationScripts.Prepare(Path.Combine(root,"player"),state);NativeTranslationScripts.Restore(Path.Combine(root,"player"),state);
            check(File.ReadAllText(library)=="preexisting library","repeated upgrades preserve and restore a preexisting companion library");
            NativeTranslationScripts.Prepare(Path.Combine(root,"player"),state);File.WriteAllText(library,"user library update");NativeTranslationScripts.Restore(Path.Combine(root,"player"),state);
            check(File.ReadAllText(library)=="user library update","uninstall retains companion libraries subsequently updated by the user");
            NativeTranslationScripts.Prepare(Path.Combine(root,"player"),state);File.WriteAllText(file,Script+"// user update\n");NativeTranslationScripts.Restore(Path.Combine(root,"player"),state);
            check(File.ReadAllText(file).EndsWith("// user update\n"),"restoration preserves scripts updated by the user or PotPlayer after installation");
            NativeTranslationScripts.Prepare(Path.Combine(root,"player"),state);NativeTranslationScripts.Restore(Path.Combine(root,"player"),state);
            check(File.ReadAllText(file).EndsWith("// user update\n"),"upgrading an updated native script captures its new original for recovery");
            NativeTranslationScripts.Prepare(Path.Combine(root,"player"),state);
            var records=new JavaScriptSerializer().Deserialize<System.Collections.Generic.List<NativeTranslationScripts.Entry>>(File.ReadAllText(Path.Combine(state,"native-translation","scripts.json")));
            File.WriteAllText(Path.Combine(state,"native-translation",records[0].Backup),"corrupt backup");bool restoreFailed=false;
            try{NativeTranslationScripts.Restore(Path.Combine(root,"player"),state);}catch(InvalidDataException){restoreFailed=true;}
            check(restoreFailed && File.ReadAllText(file).StartsWith(NativeTranslationScripts.Marker),"missing or damaged recovery bytes abort restoration without overwriting an installed translator");
        }
        finally{Directory.Delete(root,true);}
    }
}

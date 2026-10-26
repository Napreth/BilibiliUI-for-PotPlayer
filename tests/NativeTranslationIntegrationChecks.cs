using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using BilibiliPotPlayer;

internal static class NativeTranslationIntegrationChecks
{
    sealed class Media : IMediaContext
    {
        internal string Path;
        public string SourcePath { get { return Path; } }
        public object Revision { get { return this; } }
        public IList<ContainerCaption> CaptionTracks { get { return new List<ContainerCaption>(); } }
        public bool CameraCandidate { get { return false; } }
        public double DisplayRatio { get { return 16.0/9; } }
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Startup {public int Size;public string Reserved,Desktop,Title;public int X,Y,Width,Height,CharsX,CharsY,Fill,Flags;public short Show,ReservedSize;public IntPtr ReservedData,Input,Output,Error;}
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo {public IntPtr Process,Thread;public uint Pid,Tid;}
    delegate bool EnumProc(IntPtr window,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateDesktop(string name,IntPtr device,IntPtr mode,int flags,uint access,IntPtr security);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll",SetLastError=true)] static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll")] static extern bool EnumDesktopWindows(IntPtr desktop,EnumProc callback,IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr window,EnumProc callback,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int size);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcess(string app,StringBuilder command,IntPtr ps,IntPtr ts,bool inherit,uint flags,IntPtr env,string cwd,ref Startup startup,out ProcessInfo info);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetProp(IntPtr window,string name);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window,uint message,IntPtr wp,IntPtr lp);
    [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr window);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wp,StringBuilder text);
    static IntPtr Window(IntPtr desktop,uint pid)
    {
        IntPtr found=IntPtr.Zero;
        EnumDesktopWindows(desktop,delegate(IntPtr window,IntPtr unused){uint owner;Native.GetWindowThreadProcessId(window,out owner);var name=new StringBuilder(128);Native.GetClassName(window,name,name.Capacity);if(owner==pid && name.ToString()=="PotPlayer64"){found=window;return false;}return true;},IntPtr.Zero);
        return found;
    }
    static TextSource Source(string id,string text)
    {
        var source=new TextSource{Id=id,Title=id,Category=TextCategory.Subtitle,Loaded=true};
        source.Cues.Add(new TextCue{Start=0,End=170,Text=text});
        source.Cues.Add(new TextCue{Start=180,End=239,Text="FUTURE "+text});return source;
    }
    static void Until(Action tick,Func<bool> done,string label,int timeout=12000)
    {
        var watch=Stopwatch.StartNew();
        do {Application.DoEvents();tick();Application.DoEvents();if(done())return;Thread.Sleep(15);}while(watch.ElapsedMilliseconds<timeout);
        throw new Exception("Native translation integration timed out: "+label);
    }
    internal static void Run(string sourcePlayer,string video,string output)
    {
        Exception failure=null;
        var worker=new Thread(delegate(){try{RunCore(sourcePlayer,video,output);}catch(Exception error){failure=error;}});
        worker.SetApartmentState(ApartmentState.MTA);worker.Start();worker.Join();
        if(failure!=null)throw new Exception("Isolated native integration failed: "+failure.Message,failure);
    }
    static void RunCore(string sourcePlayer,string video,string output)
    {
        output=Path.GetFullPath(output);string playerRoot=Path.Combine(output,"player");
        if(Directory.Exists(playerRoot))throw new Exception("Use a fresh integration output folder");
        Directory.CreateDirectory(playerRoot);
        foreach(string file in new[]{"PotPlayerMini64.exe","PotPlayer64.dll","ffcodec64.dll","MediaInfo.sdb","ATextOut64.dll","d3dcompiler_47.dll","KakaoUtil64.dll"})File.Copy(Path.Combine(sourcePlayer,file),Path.Combine(playerRoot,file));
        Directory.CreateDirectory(Path.Combine(playerRoot,"Skins"));File.Copy(Path.Combine(sourcePlayer,"Skins","Default.dsf"),Path.Combine(playerRoot,"Skins","Default.dsf"));
        Directory.CreateDirectory(Path.Combine(playerRoot,"Language"));File.Copy(Path.Combine(sourcePlayer,"Language","English.ini"),Path.Combine(playerRoot,"Language","English.ini"));
        string extension=Path.Combine(playerRoot,"Extension","Subtitle","Translate"),runtime=Path.Combine(playerRoot,"Extension","BilibiliBehaviors");Directory.CreateDirectory(extension);Directory.CreateDirectory(runtime);
        File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"PlayerLayout.dll"),Path.Combine(runtime,"PlayerLayout.dll"));
        File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BilibiliTranslation.dll"),Path.Combine(runtime,"BilibiliTranslation.dll"));
        const string scriptName="SubtitleTranslate - OfflineTest.as";
        string fixture=NativeTranslationChecks.Script.Replace("string GetLoginTitle(){return \"Native login\";}\n", "").Replace("string ServerLogin(string User,string Pass){return \"200 ok\";}\n", "").Replace("void ServerLogout(){}\n", "");
        fixture=fixture.Replace("return {\"zh-CN\",\"en\"};", "return {\"zh-CN\",\"ja\"};");
        string traceSource=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","..","tests","fixtures","native-translation-trace.c"));
        var compile=new ProcessStartInfo("gcc","-shared -O2 -s -static-libgcc -o \""+Path.Combine(extension,"NativeTestTrace.dll")+"\" \""+traceSource+"\""){UseShellExecute=false,CreateNoWindow=true};
        using(var compiler=Process.Start(compile)){compiler.WaitForExit();if(compiler.ExitCode!=0)throw new Exception("Trace helper compile failed");}
        fixture=fixture.Replace("string GetTitle(){return", "string GetTitle(){TestTrace(\"TITLE\");return").Replace("{string value=DstLang", "{TestTrace(\"CALL \"+Text);string value=DstLang");
        fixture+="\nuintptr TraceLibrary=0;\nvoid TestTrace(string value){if(TraceLibrary==0)TraceLibrary=HostLoadLibrary(HostGetScriptFolder()+\"\\\\NativeTestTrace.dll\");if(TraceLibrary!=0){uintptr proc=HostGetProcAddress(TraceLibrary,\"native_test_trace\");if(proc!=0)HostCallProcInt(proc,\"P\",HostString2UIntPtr(value));}}\nvoid OnInitialize(){TestTrace(\"INIT \"+HostGetExecuteFolder());}\n";
        File.WriteAllText(Path.Combine(extension,scriptName),fixture,new UTF8Encoding(false));
        if(NativeTranslationScripts.Prepare(playerRoot,Path.Combine(output,"state"))!=1)throw new Exception("Offline script adaptation failed");
        File.Copy(video,Path.Combine(playerRoot,"integration.mkv"));
        File.WriteAllText(Path.Combine(playerRoot,"integration.srt"),"1\n00:00:00,000 --> 00:03:59,000\nNATIVE BASELINE\n\n",new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(playerRoot,"PotPlayerMini64.ini"),"[Settings]\r\nLanguageIni=English.ini\r\nAudioMute=1\r\nCaptionVisible=1\r\nCaptionTranslateCond=1\r\nCaptionTranslateEngine2="+scriptName+"\r\nCaptionTranslateOrder=1\r\n[ExtensionInfoList]\r\n0="+scriptName+"\r\n0D=zh-CN\r\n0S=\r\n1=\r\n",Encoding.Unicode);
        string desktopName="BilibiliNativeTranslationTest-"+Guid.NewGuid().ToString("N");IntPtr desktop=CreateDesktop(desktopName,IntPtr.Zero,IntPtr.Zero,0,0x10000000,IntPtr.Zero);
        if(desktop==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
        // The receiver, menu handles and UI-thread hook must belong to the
        // same desktop as this test player. This never switches the input desktop.
        if(!SetThreadDesktop(desktop))throw new System.ComponentModel.Win32Exception();
        ProcessInfo info=new ProcessInfo();Process process=null;var evidence=new Dictionary<string,object>();
        try
        {
            string exe=Path.Combine(playerRoot,"PotPlayerMini64.exe");var startup=new Startup{Size=Marshal.SizeOf(typeof(Startup)),Desktop=desktopName};
            if(!CreateProcess(exe,new StringBuilder("\""+exe+"\" /new \""+Path.Combine(playerRoot,"integration.mkv")+"\""),IntPtr.Zero,IntPtr.Zero,false,0,IntPtr.Zero,playerRoot,ref startup,out info))throw new System.ComponentModel.Win32Exception();
            process=Process.GetProcessById((int)info.Pid);IntPtr player=IntPtr.Zero;
            Until(delegate{player=Window(desktop,info.Pid);},()=>player!=IntPtr.Zero,"player window");
            Until(delegate{},()=>{long duration;return Native.Get(player,Native.GetDuration,out duration) && duration>200000;},"video ready");
            // Configure only this offline test player through native commands;
            // production never chooses an engine or changes these preferences.
            foreach(int command in new[]{11125,12001,12200,11115,11121})Native.SetCommand(player,command);
            var media=new Media{Path=Path.Combine(playerRoot,"integration.mkv")};var options=new CaptionOptions{TranslationEnabled=true};
            using(var catalog=new CaptionCatalog(player,media,options,null,delegate{}))using(var layout=new WindowLayout(player))
            {
                catalog.Tick(0);catalog.DrainCompleted();var first=Source("first","FIRST subtitle");var second=Source("second","SECOND subtitle");catalog.Sources.Add(first);catalog.Sources.Add(second);catalog.Slots.Main=first.Id;
                catalog.NativeTranslation=new NativeSubtitleTranslation(player,catalog);catalog.Translation=new SubtitleTranslation(catalog.NativeTranslation);
                double position=2;int epoch=0;
                Action tick=delegate{layout.Update(0);catalog.NativeTranslation.Update(position,epoch);catalog.Translation.Update(catalog.MediaPath,catalog.Find(catalog.Slots.Main),options,position,epoch);evidence["LastTick"]=new{NativeStatus=catalog.NativeTranslation.Status,ManagedStatus=catalog.Translation.Status,Token=GetProp(player,"BilibiliTranslation.Token").ToInt64(),Client=GetProp(player,"BilibiliTranslation.Client").ToInt64(),ContextCalls=GetProp(player,"BilibiliTranslation.ContextCalls").ToInt64(),ResultCalls=GetProp(player,"BilibiliTranslation.ResultCalls").ToInt64(),catalog.MediaPath,Layout=layout.Available};};
                evidence["OriginalNativeSelection"]=NativeCaptionAccess.ReadSources(player).Where(i=>i.Checked).Select(i=>new{i.Command,i.Text}).ToArray();
                var translationItems=NativeCaptionAccess.ReadSources(player);
                evidence["NativeTranslationMenu"]=translationItems.Select(i=>new{i.Command,i.Text,i.Checked,i.Enabled}).ToArray();
                var offline=translationItems.Find(i=>i.Command>=11125 && i.Command<=11149 && i.Text.Contains("Offline test"));
                if(offline!=null)Native.SetCommand(player,offline.Command);
                var nativeVisible=NativeCaptionAccess.Read(player).Find(i=>i.Command==10126);
                evidence["NativeVisibleBefore"]=nativeVisible==null?(object)"unavailable":nativeVisible.Checked;
                if(nativeVisible!=null && nativeVisible.Checked)Native.SetCommand(player,10126);
                Until(tick,()=>catalog.Translation.Track.Cues.Count==1,"first live callback");
                string expected="zh-CN: "+first.Cues[0].Text;
                if(catalog.Translation.Track.Cues[0].Text!=expected)throw new Exception("Native languages or result not preserved: "+catalog.Translation.Track.Cues[0].Text);
                evidence["FirstTranslation"]=expected;evidence["NativeLanguages"]=catalog.NativeTranslation.Languages;
                nativeVisible=NativeCaptionAccess.Read(player).Find(i=>i.Command==10126);
                evidence["NativeVisibleAtFirstReply"]=nativeVisible==null?(object)"unavailable":nativeVisible.Checked;
                if(nativeVisible==null || nativeVisible.Checked)throw new Exception("Native subtitles must stay hidden from startup through the first reply");
                if(nativeVisible!=null && nativeVisible.Checked)Native.SetCommand(player,10126);
                using(var scene=new AssScene())using(var before=new Bitmap(800,600,PixelFormat.Format32bppPArgb))using(var after=new Bitmap(800,600,PixelFormat.Format32bppPArgb))
                {
                    options.TranslationEnabled=false;scene.Render(before,catalog,2000,new List<DanmakuPlacement>());options.TranslationEnabled=true;scene.Render(after,catalog,2000,new List<DanmakuPlacement>());
                    int changed=0,added=0;for(int y=0;y<600;y++)for(int x=0;x<800;x++){Color a=before.GetPixel(x,y),b=after.GetPixel(x,y);if(a.A!=0 && a!=b)changed++;if(a.A==0 && b.A!=0)added++;}
                    if(changed!=0 || added<200)throw new Exception("Native translation changed original subtitle pixels");
                    after.Save(Path.Combine(output,"live-native-translation.png"));evidence["OriginalPixelsChanged"]=changed;evidence["AddedTranslationPixels"]=added;
                }
                catalog.Slots.Main=second.Id;catalog.Revision++;
                Until(tick,()=>catalog.Translation.Track.Cues.Count==1 && catalog.Translation.Track.Cues[0].Text=="zh-CN: "+second.Cues[0].Text,"source switch");evidence["SourceSwitch"]=true;
                ulong token=unchecked((ulong)GetProp(player,"BilibiliTranslation.Token").ToInt64());
                bool bad=catalog.NativeTranslation.Accept(IntPtr.Zero,NativeTranslationChecks.Packet(token,info.Pid,second.Cues[0].Text,"wrong","engine.as\nen\nzh-CN"));
                bad|=catalog.NativeTranslation.Accept(player,NativeTranslationChecks.Packet(token+1,info.Pid,second.Cues[0].Text,"wrong","engine.as\nen\nzh-CN"));
                bad|=catalog.NativeTranslation.Accept(player,NativeTranslationChecks.Packet(token,info.Pid,"not submitted","wrong","engine.as\nen\nzh-CN"));
                if(bad)throw new Exception("Invalid native callback was accepted");evidence["StaleAndForeignRepliesRejected"]=true;
                if(!layout.Available)throw new Exception("Native setting-change hook unavailable");
                catalog.NativeTranslation.Configure();IntPtr settingsDialog=IntPtr.Zero;
                Until(delegate{EnumDesktopWindows(desktop,delegate(IntPtr window,IntPtr unused){uint owner;Native.GetWindowThreadProcessId(window,out owner);var name=new StringBuilder(128);Native.GetClassName(window,name,name.Capacity);if(owner==info.Pid && name.ToString()=="#32770")settingsDialog=window;return true;},IntPtr.Zero);},()=>settingsDialog!=IntPtr.Zero,"native settings dialog");
                IntPtr targetCombo=IntPtr.Zero;
                Until(delegate{EnumChildWindows(settingsDialog,delegate(IntPtr child,IntPtr unused){if(GetDlgCtrlID(child)==3005)targetCombo=child;return true;},IntPtr.Zero);},()=>targetCombo!=IntPtr.Zero,"native settings controls");
                var controls=new List<object>();
                EnumChildWindows(settingsDialog,delegate(IntPtr control,IntPtr unused){var name=new StringBuilder(128);var title=new StringBuilder(4096);Native.GetClassName(control,name,name.Capacity);GetWindowText(control,title,title.Capacity);var values=new List<string>();long count;if(name.ToString()=="ComboBox" && Native.TrySend(control,0x146,0,0,out count))for(int i=0;i<count && i<200;i++){var text=new StringBuilder(4096);SendMessage(control,0x148,(IntPtr)i,text);values.Add(text.ToString());}controls.Add(new{Id=GetDlgCtrlID(control),Class=name.ToString(),Text=title.ToString(),Values=values});if(GetDlgCtrlID(control)==3005)targetCombo=control;return true;},IntPtr.Zero);
                evidence["NativeDialogControls"]=controls;
                if(targetCombo==IntPtr.Zero)throw new Exception("Native target combo unavailable");
                long comboReply;Native.TrySend(targetCombo,0x14e,1,0,out comboReply);Native.TrySend(Native.GetParent(targetCombo),0x111,GetDlgCtrlID(targetCombo)|(1<<16),targetCombo.ToInt64(),out comboReply);PostMessage(settingsDialog,0x111,(IntPtr)1,IntPtr.Zero);
                Until(tick,()=>catalog.Translation.Track.Cues.Count==1 && catalog.Translation.Track.Cues[0].Text=="ja: "+second.Cues[0].Text,"native target language change");evidence["NativeLanguageChange"]=true;
                position=181;epoch++;Native.Set(player,Native.SetPosition,181000);
                Until(tick,()=>catalog.Translation.Track.Cues.Count==1 && catalog.Translation.Track.Cues[0].Text=="ja: "+second.Cues[1].Text,"seek");evidence["Seek"]=true;
                var unicode=Source("unicode","中日 Unicode & <literal>\n第二行");catalog.Sources.Add(unicode);catalog.Slots.Main=unicode.Id;position=2;epoch++;Native.Set(player,Native.SetPosition,2000);
                Until(tick,()=>catalog.Translation.Track.Cues.Count==1 && catalog.Translation.Track.Cues[0].Text=="ja: "+unicode.Cues[0].Text,"Unicode and literal markup");evidence["UnicodeAndLiteralMarkup"]=true;
                options.TranslationEnabled=false;tick();
                evidence["RestoredNativeSources"]=NativeCaptionAccess.ReadSources(player).Where(i=>i.Checked && i.Command>=29000 && i.Command<29199).Select(i=>i.Text).ToArray();
                if(catalog.Translation.Track.Cues.Count!=0 || GetProp(player,"BilibiliTranslation.Token")!=IntPtr.Zero || GetProp(player,"BilibiliTranslation.Client")!=IntPtr.Zero)throw new Exception("Disabling did not revoke the native callback");evidence["DisabledContextRevoked"]=true;
            }
            Native.TrySend(player,0x10,0,0,out exitResult);
            Until(delegate{},()=>process.HasExited,"player close",5000);evidence["PlayerExited"]=true;
            NativeTranslationScripts.Restore(playerRoot,Path.Combine(output,"state"));
            if(File.ReadAllText(Path.Combine(extension,scriptName))!=fixture)throw new Exception("Native script not restored");evidence["OriginalScriptRestored"]=true;
            evidence["OfflineOnly"]=true;
        }
        catch(Exception error){evidence["Error"]=error.Message;string trace=Path.Combine(extension,"native-test-trace.txt");if(File.Exists(trace))evidence["NativeTrace"]=File.ReadAllText(trace);var windows=new List<object>();EnumDesktopWindows(desktop,delegate(IntPtr window,IntPtr unused){uint pid;Native.GetWindowThreadProcessId(window,out pid);if(pid!=info.Pid)return true;var name=new StringBuilder(128);var title=new StringBuilder(4096);Native.GetClassName(window,name,name.Capacity);GetWindowText(window,title,title.Capacity);windows.Add(new{Class=name.ToString(),Title=title.ToString()});if(name.ToString()=="#32770")EnumChildWindows(window,delegate(IntPtr child,IntPtr ignored){var text=new StringBuilder(4096);GetWindowText(child,text,text.Capacity);windows.Add(new{Text=text.ToString()});return true;},IntPtr.Zero);return true;},IntPtr.Zero);evidence["Windows"]=windows;throw;}
        finally
        {
            File.WriteAllText(Path.Combine(output,"integration.json"),new JavaScriptSerializer().Serialize(evidence));
            if(process!=null){try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}process.Dispose();}
            if(info.Thread!=IntPtr.Zero)CloseHandle(info.Thread);if(info.Process!=IntPtr.Zero)CloseHandle(info.Process);CloseDesktop(desktop);
        }
        Console.WriteLine("PASS: live native subtitle translation integration: "+output);
    }
    static long exitResult;
}

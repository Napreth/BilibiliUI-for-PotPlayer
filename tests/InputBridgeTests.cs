using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using BilibiliPotPlayer;

internal static class BridgeTests
{
    static int checks;
    static void Equal<T>(T expected, T actual, string label)
    {
        checks++;
        if (!Object.Equals(expected, actual)) throw new Exception(label + ": expected " + expected + ", got " + actual);
    }
    [STAThread] static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + (args.Length > 0 && args[0] == "--native-translation-e2e" ? error.ToString() : error.Message)); return 1; }
    }
    static int Run(string[] args)
    {
        Native.SetProcessDpiAwarenessContext((IntPtr)(-4));
        if (args.Length == 4 && args[0] == "--native-translation-e2e") { NativeTranslationIntegrationChecks.Run(args[1], args[2], args[3]); return 0; }
        if (args.Length > 0 && args[0] == "--window-check") { WindowModeChecks.Run(); return 0; }
        if (args.Length > 0 && args[0] == "--aspect-check") { WindowModeChecks.CheckAspect(); return 0; }
        if (args.Length > 0 && args[0] == "--aspect-gesture-check") { WindowModeChecks.CheckAspectGestures(); return 0; }
        if (args.Length > 0 && args[0] == "--chrome-check") { WindowModeChecks.CheckChromeGestures(); return 0; }
        if (args.Length > 0 && args[0] == "--sidebar-pixel-check") { WindowModeChecks.CheckSidebarPixels(); return 0; }
        if (args.Length > 0 && args[0] == "--sidebar-resize-check") { WindowModeChecks.CheckSidebarResize(); return 0; }
        if (args.Length > 0 && args[0] == "--startup-layout-check") { WindowModeChecks.CheckStartupLayout(args.Length > 1 ? args[1] : ""); return 0; }
        if (args.Length > 0 && args[0] == "--sidebar-tab-check") { WindowModeChecks.CheckSidebarTabs(); return 0; }
        if (args.Length > 0 && args[0] == "--layout-check") { WindowModeChecks.Run(); return 0; }
        if (args.Length > 0 && args[0] == "--status") { Status(); return 0; }
        if (args.Length > 1 && args[0] == "--timing-probe") { PlaybackClockChecks.Sample(args[1],args.Length>2?Int32.Parse(args[2]):4000); return 0; }
        if (args.Length > 0 && args[0] == "--ui-status") { UiStatus(); return 0; }
        if (args.Length > 0 && args[0] == "--font-probe") { DanmakuFontChecks.Probe(); return 0; }
        if (args.Length > 1 && args[0] == "--placement-preview") { DanmakuPlacementChecks.Preview(args[1]); return 0; }
        if (args.Length > 1 && args[0] == "--subtitle-centering-preview") { SubtitleCenteringChecks.Preview(args[1]); return 0; }
        if (args.Length > 3 && args[0] == "--source-preview") { DanmakuPlacementChecks.PreviewSource(args[1],args[2],args[3]); return 0; }
        if (args.Length > 1 && args[0] == "--caption-preview") { CaptionPreview(args[1], args.Length > 2 ? Single.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 1); return 0; }
        if (args.Length > 1 && args[0] == "--classify-caption") {
            foreach (TextSource source in TimedText.Read(args[1])) Console.WriteLine(new JavaScriptSerializer().Serialize(new { source.Title, Category=source.Category.ToString(), Events=source.Events.Count, Playable=source.Cues.Count, source.Analysis }));
            return 0;
        }
        if (args.Length > 0 && args[0] == "--caption-menu") { foreach (IntPtr player in Native.PlayerWindows()) { Console.WriteLine("Player " + player); foreach (NativeMenuSnapshot.Item item in NativeCaptionAccess.Read(player)) if (item.Command == 10126 || item.Command >= 29000 && item.Command < 29400) Console.WriteLine(item.Command + " " + item.Checked + " " + item.Enabled + " " + item.Text); } return 0; }
        if (args.Length > 0 && (args[0] == "--metadata" || args[0] == "--media-path")) { Metadata(args[0] == "--media-path"); return 0; }
        if (args.Length > 1 && args[0] == "--inspect") { PrintCatalog(MediaCatalog.Read(System.IO.Path.GetFullPath(args[1]))); return 0; }
        if (args.Length > 1 && args[0] == "--caption-inspect")
        {
            string captionPath = System.IO.Path.GetFullPath(args[1]); MediaCatalog catalog = MediaCatalog.Read(captionPath);
            foreach (ContainerCaption track in catalog.Text)
            { foreach (TextSource parsed in NativeTimedText.Read(captionPath, track.Ordinal, track.Title)) Console.WriteLine(new JavaScriptSerializer().Serialize(new { track.Ordinal, track.Title, track.Language, track.Codec, track.Default, Category=parsed.Category.ToString(), Events=parsed.Events.Count, Cues = parsed.Cues.Count, parsed.Analysis })); }
            return 0;
        }
        RightGesture r = new RightGesture(300);
        Equal(RightAction.None, r.Up(0), "stray release");
        r.Down(1000); r.Down(1100);
        Equal(RightAction.None, r.Tick(1299), "hold threshold");
        Equal(RightAction.SeekForward, r.Up(1299), "tap seeks once despite repeats");
        Equal(RightAction.None, r.Up(1300), "duplicate release does not seek");
        r.Down(2000);
        Equal(RightAction.StartSpeed, r.Tick(2300), "hold starts 2x");
        Equal(RightAction.None, r.Tick(3000), "hold does not repeatedly change speed");
        Equal(RightAction.RestoreSpeed, r.Up(3001), "release restores saved speed");
        Equal(RightAction.None, r.Tick(4000), "released hold remains stopped");
        r.Down(5000); r.Tick(5300);
        Equal(RightAction.RestoreSpeed, r.Cancel(), "focus loss restores speed");
        Equal(RightAction.None, r.Up(5500), "focus loss never seeks");
        r.Down(6000);
        Equal(RightAction.None, r.Cancel(), "cancel a tap without seeking");
        Equal(RightAction.None, r.Up(6100), "release after cancellation");
        r.Down(7000);
        Equal(RightAction.None, r.Up(7300), "delayed timer cannot turn hold into seek");
        RewindGesture l = new RewindGesture(300, 100);
        Equal(true, l.Down(0), "left starts with one seek");
        Equal(false, l.Down(1), "OS repeat does not add a second seek");
        Equal(false, l.Tick(299), "left repeat threshold");
        Equal(true, l.Tick(300), "left hold rewinds");
        Equal(false, l.Tick(399), "left repeat spacing");
        Equal(true, l.Tick(400), "continuous rewind");
        Equal(true, l.Tick(2000), "one seek after a busy UI");
        Equal(false, l.Tick(2001), "no queued rewind burst");
        l.Cancel(); Equal(false, l.Tick(3000), "release/focus loss stops rewind");
        Equal(true, l.Down(4000), "next left press works");
        l.Cancel(); Equal(false, l.Tick(4500), "rapid left tap never sticks");
        VideoClickGesture click = new VideoClickGesture(500, 6);
        click.Down(100, 100); Equal(ClickAction.None, click.Up(100, 100, 0), "single click waits for double click");
        Equal(ClickAction.None, click.Tick(499), "single click cannot fire within double-click interval");
        Equal(ClickAction.Single, click.Tick(500), "single click fires once");
        Equal(ClickAction.None, click.Tick(501), "single click does not repeat");
        click.Down(100, 100); click.Up(100, 100, 1000); click.Down(102, 100);
        Equal(ClickAction.Double, click.Up(102, 100, 1300), "double click selects fullscreen");
        Equal(ClickAction.None, click.Tick(1600), "double click does not toggle play");
        click.Down(100, 100); click.Move(150, 100); click.Up(100, 100, 2000);
        Equal(ClickAction.None, click.Tick(2500), "drag returning to start is not a click");
        click.Down(100, 100); click.Up(100, 100, 3000); click.Cancel();
        Equal(ClickAction.None, click.Tick(3500), "focus loss cancels pending click");
        click.Down(100, 100); click.Up(100, 100, 4000);
        Equal(ClickAction.Single, click.ResolvePending(), "middle press resolves a preceding real left click once");
        Equal(ClickAction.None, click.Tick(4500), "resolved real click cannot fire again during camera gesture");
        click.Down(100, 100); Equal(ClickAction.None, click.ResolvePending(), "unfinished left drag cannot become a pause");
        CameraPlaybackGuard playback = new CameraPlaybackGuard();
        playback.Arm(2, 0, 600);
        Equal(2, playback.Restore(1, 15), "camera mouse down cannot pause a playing video");
        Equal(2, playback.Restore(1, 500), "delayed native single or double click remains isolated");
        Equal(0, playback.Restore(2, 501), "unchanged playback requires no command");
        Equal(0, playback.Restore(1, 601), "expired protection cannot undo later playback changes");
        playback.Arm(1, 1000, 600); Equal(1, playback.Restore(2, 1015), "camera mouse up cannot resume a paused video");
        playback.Cancel(); Equal(0, playback.Restore(2, 1016), "explicit space or real left click cancels protection");
        playback.Arm(2, 2000, 600); Equal(0, playback.Restore(0, 2010), "stopping or closing media never restarts it");
        Equal(0, playback.Restore(1, 2011), "stopped media clears playback protection");
        playback.Arm(0, 3000, 600); Equal(0, playback.Restore(1, 3015), "stopped media cannot arm camera playback protection");
        Equal(1, CaptionPages.Count(0, 3), "empty subtitle list has no extra page");
        Equal(1, CaptionPages.Count(3, 3), "three tracks fit one page");
        Equal(2, CaptionPages.Count(4, 3), "fourth track starts the next page");
        Equal(3, CaptionPages.First(1, 4, 3), "page two starts after three tracks");
        Equal(6, CaptionPages.First(99, 9, 3), "list shrink clamps the last page");
        Equal(0, CaptionPages.First(-1, 5, 3), "scrolling above page one stays at the beginning");
        Equal(4, CaptionPages.Count(10, 3), "the supplied ten-track MKV needs four pages");
        Equal(0, CaptionPages.WindowStart(3, 9), "page four keeps the first five indicators and a next arrow");
        Equal(1, CaptionPages.WindowStart(4, 9), "page five shifts the five indicators with both arrows");
        Equal(4, CaptionPages.WindowStart(8, 9), "last page window ends at the last page");
        Equal(0, CaptionPages.WindowStart(2, 4), "short lists keep all their page dots");
        foreach (TextCategory category in (TextCategory[])Enum.GetValues(typeof(TextCategory)))
        {
            RectangleF area = CaptionDropAreas.Bounds(category);
            Equal(100f, area.Height, category + " has equal space for three tracks and pagination");
            Equal((TextCategory?)category, CaptionDropAreas.At(area.X + 10, area.Bottom - 1), category + " third-row drop remains inside its own area");
        }
        Equal((TextCategory?)null, CaptionDropAreas.At(308, 100), "divider is not a drop target");
        Equal((TextCategory?)null, CaptionDropAreas.At(100, 355), "settings are not a drop target");
        Equal((TextCategory?)null, CaptionDropAreas.At(100, 20), "category header is not a drop target");
        Equal(PlayerStage.Normal, ResponsivePolicy.Stage(1100, 700, 1), "large window retains sidebar and stream controls");
        Equal(PlayerStage.Normal, ResponsivePolicy.Stage(900, 650, 1), "narrow first stage closes the sidebar");
        Equal(PlayerStage.Normal, ResponsivePolicy.Stage(1200, 580, 1), "short first stage also closes the sidebar");
        Equal(PlayerStage.Small, ResponsivePolicy.Stage(700, 650, 1), "narrow second stage uses essential controls");
        Equal(PlayerStage.Small, ResponsivePolicy.Stage(1200, 400, 1), "short second stage uses essential controls");
        Equal(PlayerStage.Normal, ResponsivePolicy.Stage(2200, 1400, 2), "responsive breakpoints use logical pixels at high DPI");
        Equal(PlayerStage.Normal, ResponsivePolicy.Stage(1024, 600, 1), "first transition returns full controls at its boundary");
        Equal(PlayerStage.Normal, ResponsivePolicy.Stage(800, 480, 1), "second transition restores full control row at its boundary");
        ChromeActivity chrome = new ChromeActivity();
        Equal(true, chrome.Visible(0, 10, 20, true, true, false), "fullscreen controls start visible");
        Equal(true, chrome.Visible(2999, 10, 20, true, true, false), "fullscreen controls remain until the idle deadline");
        Equal(false, chrome.Visible(3000, 10, 20, true, true, false), "stationary fullscreen controls hide after three seconds");
        Equal(true, chrome.Visible(3100, 11, 20, true, true, false), "pointer movement reveals fullscreen controls");
        Equal(true, chrome.Visible(7000, 11, 20, true, true, true), "open menus keep fullscreen controls visible");
        Equal(false, chrome.Visible(7100, 11, 20, false, true, false), "leaving the player hides controls immediately");
        Equal(true, chrome.Visible(7200, 11, 20, true, true, false), "reentering the player resets the idle deadline");
        Equal(false, chrome.Visible(20000, 11, 20, true, true, false), "normal window controls also time out");
        Equal(true, chrome.Visible(21000, 12, 20, true, true, false), "movement reveals normal window controls");
        Equal(false, chrome.Visible(24000, 12, 20, true, true, false), "normal window controls hide after three idle seconds");
        Equal(true, chrome.Visible(25000, 12, 20, true, false, false), "entering small mode reveals its hover controls");
        Equal(true, chrome.Visible(50000, 12, 20, true, false, false), "small window hover does not time out");
        Equal(false, chrome.Visible(50100, 12, 20, false, false, false), "small window controls hide when the pointer leaves");
        CaptionLoadingChecks();
        CameraGesture camera = new CameraGesture();
        camera.Begin(100, 100); camera.Move(105, 102); camera.End(105, 102);
        Equal(false, camera.Moved, "middle click tolerates pointer jitter");
        camera.Begin(100, 100); camera.Move(120, 100); camera.Move(100, 100); camera.End(100, 100);
        Equal(true, camera.Moved, "middle drag returning to origin does not recenter");
        camera.Cancel(); Equal(false, camera.Down, "camera cancellation releases drag state");
        Equal(false, CameraPolicy.Enabled(false, true, false), "automatic mode does not activate on ordinary video");
        Equal(true, CameraPolicy.Enabled(false, true, true), "automatic mode permits equirectangular camera");
        Equal(true, CameraPolicy.Enabled(true, false, false), "forced native mode permits other projections");
        Equal(false, CameraPolicy.Enabled(false, false, true), "disabled native VR mode is respected");
        Equal(false, SphericalVideoProbe.Candidate("ordinary-film.mp4", 3840, 1920, false), "2:1 ordinary film does not enable camera gestures");
        Equal(true, SphericalVideoProbe.Candidate("【360° VR】sample.mp4", 3840, 1920, false), "explicitly marked repacked spherical file");
        Equal(false, SphericalVideoProbe.Candidate("movie 360p.mp4", 3840, 1920, false), "360p is not a spherical marker");
        Equal(true, SphericalVideoProbe.Candidate("cube.mp4", 3000, 2000, true), "tagged cubemap does not require a 2:1 canvas");
        Equal(false, SphericalVideoProbe.Read(new System.IO.MemoryStream(new byte[7])), "truncated spherical metadata is rejected");
        for (int key = 0x70; key <= 0x7B; key++)
            Equal(key == 0x74 ? KeyRoute.Blocked : KeyRoute.Native, InputPolicy.Route(key, false, false, false, false), "function key " + key);
        Equal(KeyRoute.Native, InputPolicy.Route(0x75, true, true, false, false), "modified F6 retains native behavior");
        Equal(KeyRoute.Native, InputPolicy.Route(0x5B, false, false, false, false), "left Windows key reaches the system before its state changes");
        Equal(KeyRoute.Native, InputPolicy.Route(0x5C, false, false, false, false), "right Windows key reaches the system before its state changes");
        Equal(KeyRoute.Native, InputPolicy.Route(0x52, false, false, false, true), "Windows R opens the system Run dialog");
        Equal(KeyRoute.Blocked, InputPolicy.Route(0x74, true, true, false, false), "modified F5 remains reserved");
        Equal(KeyRoute.Blocked, InputPolicy.Route(0x0D, false, false, true, false), "Alt Enter cannot invoke default fullscreen");
        Equal(KeyRoute.Blocked, InputPolicy.Route(0x43, false, true, false, false), "unlisted native shortcut is disabled");
        Equal(KeyRoute.Blocked, InputPolicy.Route(0x59, false, false, false, false), "unlisted letter is disabled");
        Equal(KeyRoute.Mapped, InputPolicy.Route(0x58, false, false, false, false), "X opens playlist");
        Equal(KeyRoute.Mapped, InputPolicy.Route(0x43, false, false, false, false), "C toggles subtitles");
        Equal(KeyRoute.Mapped, InputPolicy.Route(0x53, false, false, false, false), "S toggles small-window mode");
        Equal(true, ResponsivePolicy.SidebarAllowed(1024, 540, 1), "sidebar allowed at its lower height boundary");
        Equal(false, ResponsivePolicy.SidebarAllowed(1024, 539, 1), "sidebar suppressed below its height boundary");
        Equal(true, ResponsivePolicy.SidebarAllowed(1536, 810, 1.5), "sidebar height boundary scales with DPI");
        Equal(false, ResponsivePolicy.SidebarAllowed(1536, 809, 1.5), "scaled height below the boundary suppresses the sidebar");
        Equal(false, ResponsivePolicy.SidebarAllowed(1023, 600, 1), "sidebar suppressed below width budget");
        Equal(true, ResponsivePolicy.SidebarAllowed(1024, 599, 1), "sidebar remains available below the former height boundary");
        Equal(new Size(640, 360), ResponsivePolicy.AspectSize(640, 0, 16.0 / 9, false), "width resize preserves widescreen ratio");
        Equal(new Size(320, 240), ResponsivePolicy.AspectSize(0, 240, 4.0 / 3, true), "height resize preserves classic ratio");
        Equal(new Size(300, 400), ResponsivePolicy.AspectSize(300, 0, .75, false), "portrait ratio remains portrait");
        Equal(KeyRoute.Blocked, InputPolicy.Route(0x58, false, true, false, false), "Ctrl X remains unlisted");
        Equal(KeyRoute.Mapped, InputPolicy.Route(0x31, true, false, false, false), "Shift 1 still mapped");
        Equal(KeyRoute.Mapped, InputPolicy.Route(0x27, false, false, false, false), "right gesture still mapped");
        Equal(KeyRoute.Native, InputPolicy.Route(0x73, false, false, true, false), "system Alt F4 retained");
        Equal(KeyRoute.Native, InputPolicy.Route(0x09, false, false, true, false), "system Alt Tab retained");
        Equal(KeyRoute.Native, InputPolicy.Route(0x25, false, false, false, true), "system window snapping retained");
        Equal(100, VolumeScale.PercentAt(54, 54, 134), "volume top is 100 percent");
        Equal(0, VolumeScale.PercentAt(134, 54, 134), "volume bottom is zero");
        Equal(50, VolumeScale.PercentAt(94, 54, 134), "volume midpoint is 50 percent");
        Equal(100, VolumeScale.PercentAt(0, 54, 134), "drag above rail clamps to 100");
        Equal(0, VolumeScale.PercentAt(200, 54, 134), "drag below rail clamps to zero");
        Equal(81f, VolumeScale.PositionAt(100, 81, 201), "DPI scaled full-volume thumb is at top");
        Equal(201f, VolumeScale.PositionAt(0, 81, 201), "DPI scaled zero-volume thumb is at bottom");
        Rectangle row = ControlLayout.Row(new Rectangle(100, 200, 1280, 720), 1, false);
        Equal(new Rectangle(103, 877, 1274, 40), row, "normal row excludes the skin bottom border");
        Rectangle volume = ControlLayout.Volume(row, 1), speed = ControlLayout.Speed(volume, 1);
        Equal(new Rectangle(1295, 877, 41, 40), volume, "native volume dock coordinates");
        Equal(897, speed.Top + speed.Height / 2, "speed and native buttons share a vertical center");
        row = ControlLayout.Row(new Rectangle(0, 0, 2560, 1528), 1.5, false);
        Equal(1523, row.Bottom, "150 percent DPI uses a five-pixel skin border");
        Equal(267, 2560 - ControlLayout.Speed(ControlLayout.Volume(row, 1.5), 1.5).Right, "DPI speed position includes subtitles and native buttons");
        Equal(ControlLayout.Volume(row, 1.5).Left, ControlLayout.Subtitle(ControlLayout.Volume(row, 1.5), 1.5).Right, "subtitle button is immediately left of volume");
        Equal(ControlLayout.Subtitle(ControlLayout.Volume(row, 1.5), 1.5).Left, ControlLayout.Speed(ControlLayout.Volume(row, 1.5), 1.5).Right, "speed is immediately left of subtitles");
        row = ControlLayout.Row(new Rectangle(0, 0, 1920, 1080), 1, true);
        Equal(new Rectangle(0, 1037, 1920, 40), row, "fullscreen OSC retains bottom border without side borders");
        Equal(-85, ControlLayout.Volume(ControlLayout.Row(new Rectangle(-1920, 0, 1920, 1080), 1, false), 1).Left, "secondary monitor coordinates remain negative");
        Equal(new Rectangle(300, 0, 100, 60), ControlLayout.RelativeTo(new Rectangle(-1000, 1500, 100, 60), new Rectangle(-1300, 1500, 1280, 60)), "embedded controls use parent coordinates on a negative monitor");
        Equal(new Rectangle(40, 10, 100, 60), ControlLayout.RelativeTo(new Rectangle(140, 210, 100, 60), new Rectangle(100, 200, 1280, 80)), "embedded controls preserve offsets from a moved player");
        MediaChecks();
        DolbyVisionProbeChecks.Run(Equal<bool>);
        SphericalVideoProbeChecks.Run(Equal<bool>);
        TimedTextChecks.Run(Equal<bool>);
        CaptionDiscoveryChecks.Run(Equal<bool>);
        CaptionClassificationChecks.Run(Equal<bool>);
        AssConverterChecks.Run(Equal<bool>);
        DanAnyRawChecks.Run(Equal<bool>);
        ModuleChecks.Run(Equal<bool>);
        NativeTimedTextChecks.Run(Equal<bool>);
        AssRenderingChecks.Run(Equal<bool>);
        PlaybackClockChecks.Run(Equal<bool>);
        DanmakuRenderingChecks.Run(Equal<bool>);
        DanmakuPlaybackChecks.Run(Equal<bool>);
        DanmakuViewportChecks.Run(Equal<bool>);
        DanmakuFontChecks.Run(Equal<bool>);
        DanmakuDensityChecks.Run(Equal<bool>);
        DanmakuPlacementChecks.Run(Equal<bool>);
        DanmakuWebPlaybackChecks.Run(Equal<bool>);
        SubtitleMaskChecks.Run(Equal<bool>);
        SubtitleCenteringChecks.Run(Equal<bool>);
        CaptionAppearanceChecks.Run(Equal<bool>);
        SubtitleTranslationChecks.Run(Equal<bool>);
        NativeTranslationChecks.Run(Equal<bool>);
        DanmakuFactoryChecks.Run(Equal<bool>);
        Console.WriteLine("PASS: " + checks + " input and metadata checks");
        return 0;
    }
    static void MediaChecks()
    {
        UiLanguage zh = new UiLanguage("Chinese(Simplified).ini"), en = new UiLanguage("English.ini"), tw = new UiLanguage("Chinese(Traditional).ini");
        MediaTrack track = new MediaTrack { Codec = "FLAC", Lossless = true, Bits = 24, SampleRate = 96000, Channels = "2.0", Bitrate = 2400000 };
        Equal(true, track.HighResolution, "24-bit 96 kHz lossless qualifies");
        Equal("Hi-Res 立体声2.0", track.Summary(zh), "compact audio keeps Hi-Res and names channels without codec");
        Equal("FLAC 立体声2.0 96kHz/24bit 2400 kbps", track.Details(zh), "audio menu retains codec and measurements without quality badge");
        track.SampleRate = 48000; Equal(false, track.HighResolution, "24-bit 48 kHz below configured threshold");
        Equal("48kHz/24bit", track.AudioSpecification, "sample rate and depth use slash notation");
        track.SampleRate = 44100; track.Bits = 16;
        Equal("44.1kHz/16bit", track.AudioSpecification, "fractional sample rate retains precision");
        track.SampleRate = 48000; track.Bits = 0;
        Equal("48kHz", track.AudioSpecification, "unknown bit depth is not invented");
        track.SampleRate = 0; track.Bits = 24;
        Equal("24bit", track.AudioSpecification, "missing sample rate does not leave a slash");
        track.SampleRate = 96000; track.Bits = 16; Equal(false, track.HighResolution, "16-bit high-rate source is not 24-bit");
        track.Bits = 24; track.Lossless = false; Equal(false, track.HighResolution, "lossy decoder precision cannot imply Hi-Res");
        Equal("Atmos", MediaTrack.SpatialName("JOC", "", ""), "E-AC-3 JOC identifies Atmos");
        Equal("", MediaTrack.SpatialName("", "", "Dolby Digital Plus"), "Dolby codec alone is not Atmos");
        Equal("DTS:X", MediaTrack.SpatialName("XLL X", "", ""), "DTS object extension");
        Equal("", MediaTrack.SpatialName("", "MA", "DTS-HD Master Audio"), "DTS-HD alone is not DTS:X");
        Equal("", MediaTrack.HdrName("", "", "BT.709"), "SDR has no HDR label");
        Equal("Dolby Vision", MediaTrack.HdrName("Dolby Vision", "HDR10", "PQ"), "Dolby Vision takes precedence");
        Equal("HDR10+", MediaTrack.HdrName("SMPTE ST 2094-40", "HDR10", "PQ"), "dynamic HDR format");
        Equal("HDR (PQ)", MediaTrack.HdrName("", "", "PQ"), "PQ without HDR10 metadata is not called HDR10");
        Equal("HLG", MediaTrack.HdrName("", "", "HLG"), "HLG transfer");
        Equal("杜比全景声", zh.Spatial("Atmos"), "simplified Atmos name");
        Equal("Dolby Atmos", en.Spatial("Atmos"), "English Atmos name");
        Equal("杜比全景聲", tw.Spatial("Atmos"), "traditional Atmos name");
        Equal("杜比视界", zh.Hdr("Dolby Vision"), "simplified Dolby Vision name");
        Equal("Dolby Vision", en.Hdr("Dolby Vision"), "English Dolby Vision name");
        Equal("5.1", MediaTrack.ChannelName("L R C LFE Ls Rs", 6), "5.1 bed");
        Equal("5.1.2", MediaTrack.ChannelName("L R C LFE Ls Rs Tfl Tfr", 8), "height layout is counted from source channels");
        Equal("2.0", MediaTrack.ChannelName("L R", 2), "stereo");
        Equal("H.264", MediaTrack.CodecName("AVC", "High"), "AVC display name");
        Equal("TrueHD", MediaTrack.CodecName("MLP FBA", ""), "TrueHD display name");
        Equal("立体声2.0", zh.ChannelLabel("2.0"), "stereo channel label");
        Equal("立体声2.1", zh.ChannelLabel("2.1"), "stereo plus subwoofer label");
        Equal("环绕声5.1", zh.ChannelLabel("5.1"), "5.1 surround label");
        Equal("环绕声7.1", zh.ChannelLabel("7.1"), "7.1 surround label");
        Equal("沉浸式9.1.6", zh.ChannelLabel("9.1.6"), "height layout does not imply a particular codec");
        Equal("单声道1.0", zh.ChannelLabel("1.0"), "mono label");
        Equal("Immersive 7.1.4", en.ChannelLabel("7.1.4"), "English immersive label");
        Equal("環繞聲5.1", tw.ChannelLabel("5.1"), "traditional surround label");
        Equal("", zh.ChannelLabel(""), "unknown channels are not invented");
        track.Lossless = true; track.Spatial = "Atmos"; track.Channels = "5.1.2";
        Equal("Hi-Res 杜比全景声 沉浸式5.1.2", track.Summary(zh), "verified Atmos and layout remain separate from codec");
        Equal("1080P", MediaTrack.ResolutionName(1920, 1080), "standard full HD shorthand");
        Equal("4K", MediaTrack.ResolutionName(3840, 2160), "UHD shorthand");
        Equal("4K", MediaTrack.ResolutionName(4096, 2160), "DCI 4K shorthand");
        Equal("1080P", MediaTrack.ResolutionName(1080, 1920), "portrait full HD shorthand");
        Equal("1920×800", MediaTrack.ResolutionName(1920, 800), "cropped source retains exact dimensions");
        MediaTrack video = new MediaTrack { Video = true, Codec = "H.265", Hdr = "Dolby Vision", Width = 3840, Height = 2160, Fps = 59.94, Bits = 10 };
        Equal("杜比视界 4K@59.94fps", video.Summary(zh), "video summary omits codec");
        Equal("H.265 杜比视界 3840×2160@59.94fps 10bit", video.Details(zh), "video menu retains codec and exact dimensions");
        video.Width = 1920; video.Height = 1080; video.Interlaced = true;
        Equal("1080I@59.94fps", video.Resolution, "interlaced source is not labeled progressive");
        var tracks = new List<MediaTrack> { new MediaTrack { Codec = "FLAC", Label = "原声" }, new MediaTrack { Codec = "AAC", Label = "解说" } };
        var items = new List<NativeTrackAccess.Entry> {
            new NativeTrackAccess.Entry { Command = 10020, Text = "Cycle stream" },
            new NativeTrackAccess.Entry { Command = -1, Text = "" },
            new NativeTrackAccess.Entry { Command = 45000, Text = "原声 FLAC", Selected = false },
            new NativeTrackAccess.Entry { Command = 45001, Text = "解说 AAC", Selected = true }
        };
        Equal(45001, NativeTrackAccess.Match(items, tracks)[1].Command, "real checked item survives mapping");
        items[2].Selected = true; Equal(null, NativeTrackAccess.Match(items, tracks), "reject ambiguous native checked state");
        items[2].Selected = false;
        items.Add(new NativeTrackAccess.Entry { Command = 45002, Text = "extra AAC" });
        Equal(null, NativeTrackAccess.Match(items, tracks), "reject external-track count mismatch");
        items.RemoveAt(4); items[2].Text = "unrelated"; items[3].Text = "unrelated";
        Equal(null, NativeTrackAccess.Match(items, tracks), "unrelated native menu cannot become a stream selector");
    }
    static void WaitCaption(CaptionCatalog captions, TextSource source)
    {
        Stopwatch wait = Stopwatch.StartNew();
        while (source.Loading && wait.ElapsedMilliseconds < 3000) { captions.DrainCompleted(); Thread.Sleep(2); }
        captions.DrainCompleted(); Equal(false, source.Loading, "caption worker finishes without blocking the UI");
    }
    static void CaptionLoadingChecks()
    {
        int attempts = 0;
        Func<TextSource, List<TextCue>> loader = delegate(TextSource source)
        { if (++attempts == 1) throw new System.IO.IOException("temporary decoder failure"); return new List<TextCue> { new TextCue { Start = 0, End = 5, Text = source.Title } }; };
        using (CaptionCatalog captions = new CaptionCatalog(IntPtr.Zero, null, new CaptionOptions(), loader, delegate { }))
        {
            TextSource a = new TextSource { Id = "a", Title = "Chinese", Category = TextCategory.Subtitle, Stream = 0 };
            TextSource b = new TextSource { Id = "b", Title = "English", Category = TextCategory.Subtitle, Stream = 1 };
            captions.Sources.Add(a); captions.Sources.Add(b);
            captions.Select(a); WaitCaption(captions, a);
            Equal(false, a.Loaded, "temporary decoder failure cannot become a permanently loaded error");
            Equal(true, CaptionCatalog.CanUse(a), "failed container track remains clickable for retry");
            Equal(null, captions.Slots.Main, "failed selection does not occupy a subtitle slot");
            captions.DrainCompleted(); Equal(1, attempts, "idle completion processing does not retry in a loop");
            captions.Select(a); WaitCaption(captions, a);
            Equal("a", captions.Slots.Main, "clicking a failed subtitle retries and selects it");
            Equal("", a.Error, "successful retry clears the stale error");
            Equal(1, a.Cues.Count, "retry has one copy of its cues");
            captions.Select(b); WaitCaption(captions, b);
            Equal("b", captions.Slots.Main, "single-language click replaces the selected subtitle");
            captions.SetMulti(true); captions.Select(a);
            Equal("b", captions.Slots.Main, "bilingual mode preserves the primary subtitle");
            Equal("a", captions.Slots.Secondary, "bilingual mode fills the secondary subtitle");
            captions.ToggleSubtitles();
            Equal(false, captions.Options.SubtitlesEnabled, "subtitle master switch turns captions off");
            Equal("b", captions.Slots.Main, "subtitle master switch retains primary selection");
            Equal("a", captions.Slots.Secondary, "subtitle master switch retains secondary selection");
            Equal(true, captions.Options.DanmakuEnabled, "subtitle master switch does not affect danmaku");
            captions.ToggleSubtitles(); Equal(true, captions.Options.SubtitlesEnabled, "subtitle master switch restores both selected tracks");
            TextSource c = new TextSource { Id = "c", Title = "Retry as danmaku", Category = TextCategory.Subtitle, Stream = 2, Error = "old failure" };
            captions.Sources.Add(c); captions.Classify(c, TextCategory.Danmaku); WaitCaption(captions, c);
            Equal(true, c.Enabled, "reclassifying a failed container subtitle loads and enables ordinary danmaku");
            Equal(1, c.Cues.Count, "reclassified container retains decoded timing and text");
            captions.Toggle(c); Equal(false, c.Enabled, "ordinary danmaku can be turned off");
            captions.Toggle(c); Equal(true, c.Enabled, "ordinary danmaku can be turned back on");
            Equal(4, attempts, "already loaded subtitles and danmaku do not decode again");
            TextSource bitmap = new TextSource { Category = TextCategory.Subtitle, Stream = 3, Bitmap = true };
            Equal(false, CaptionCatalog.CanUse(bitmap), "bitmap streams remain in native rendering");
            TextSource advanced = new TextSource { Category = TextCategory.Advanced, IntrinsicAdvanced = true };
            captions.Classify(advanced, TextCategory.Danmaku);
            Equal(TextCategory.Advanced, advanced.Category, "advanced scripts cannot become executable ordinary danmaku");
        }
        using (ManualResetEvent release = new ManualResetEvent(false))
        using (CaptionCatalog captions = new CaptionCatalog(IntPtr.Zero, null, new CaptionOptions(), delegate(TextSource source) { release.WaitOne(2000); return new List<TextCue> { new TextCue { Text = "late result" } }; }, delegate { }))
        {
            TextSource a = new TextSource { Id = "pending", Title = "Pending", Category = TextCategory.Subtitle, Stream = 0 };
            captions.Sources.Add(a); captions.Select(a); captions.Classify(a, TextCategory.Danmaku); captions.Toggle(a); release.Set(); WaitCaption(captions, a);
            Equal(TextCategory.Danmaku, a.Category, "late decoder completion respects reclassification");
            Equal(false, a.Enabled, "late decoder completion cannot undo the user's off switch");
            Equal(null, captions.Slots.Main, "reclassified pending track no longer occupies a subtitle slot");
        }
    }
    static void CaptionPreview(string folder, float scale)
    {
        folder = System.IO.Path.GetFullPath(folder); System.IO.Directory.CreateDirectory(folder);
        using (StreamControls streams = new StreamControls())
        using (CaptionCatalog captions = new CaptionCatalog(IntPtr.Zero, streams))
        using (SubtitleButton button = new SubtitleButton())
        using (SubtitlePopup popup = new SubtitlePopup(button, captions))
        {
            for (int i = 0; i < 24; i++) captions.Sources.Add(new TextSource { Id = "subtitle-" + i, Title = i < 2 ? i == 0 ? "中文软字幕" : "English subtitle" : "外挂字幕 " + (i + 1) + ".srt", Category = TextCategory.Subtitle, Loaded = true });
            foreach (TextCategory category in new[] { TextCategory.Danmaku, TextCategory.Uncertain, TextCategory.Advanced })
                for (int i = 0; i < 3; i++) captions.Sources.Add(new TextSource { Id = category + "-" + i, Title = (category == TextCategory.Danmaku ? "普通弹幕" : category == TextCategory.Uncertain ? "用途待定" : "高级弹幕") + (i + 1) + ".xml", Category = category, Enabled = category == TextCategory.Danmaku, IntrinsicAdvanced = category == TextCategory.Advanced, Loaded = true });
            captions.Slots.Main = "subtitle-0"; popup.Size = new Size(620, CaptionDropAreas.MainHeight);
            Type type = typeof(SubtitlePopup); var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            type.GetField("scale", flags).SetValue(popup, scale); popup.Size = new Size((int)(620 * scale), (int)(CaptionDropAreas.MainHeight * scale));
            var paint = type.GetMethod("OnPaint", flags);
            foreach (string view in new string[] { "normal", "page4", "page5", "drag-subtitle", "drag-uncertain", "blocked-filters", "advanced-settings", "font-picker", "color-picker", "subtitle-settings", "long-files", "long-file-drag", "translation-settings", "translation-settings-en" })
            {
                bool translation = view.StartsWith("translation-");
                popup.Size = new Size((int)(620 * scale), (int)((translation ? 656 : CaptionDropAreas.MainHeight) * scale));
                if (translation && captions.Translation == null) captions.Translation = SubtitleTranslationChecks.Preview();
                type.GetField("english", flags).SetValue(popup, view.EndsWith("-en"));
                if (view.StartsWith("long-file")) {
                    string title = "【超原画级 4K】某科学的超电磁炮系列OP ED全合集 (AI画质提升版)";
                    captions.Find("subtitle-0").Title = title + ".json";
                    captions.Find("Danmaku-0").Title = title + ".xml";
                    captions.Find("Advanced-0").Title = title + ".ass";
                    captions.Find("Uncertain-0").Title = title + ".srt";
                }
                type.GetField("page", flags).SetValue(popup, translation ? 3 : view == "subtitle-settings" ? 1 : view == "advanced-settings" || view.EndsWith("picker") ? 2 : 0); captions.Options.Scrolling = view != "blocked-filters"; captions.Options.Fixed = view != "blocked-filters"; captions.Options.Colored = view != "blocked-filters";
                type.GetField("picker", flags).SetValue(popup, view == "font-picker" ? 1 : view == "color-picker" ? 2 : 0);
                bool drag = view.StartsWith("drag-") || view == "long-file-drag"; TextSource source = captions.Find(view == "drag-uncertain" ? "Uncertain-0" : "subtitle-0");
                int[] pages = (int[])type.GetField("listPages", flags).GetValue(popup); pages[0] = view == "page4" ? 3 : view == "page5" ? 4 : 0;
                type.GetField("dragging", flags).SetValue(popup, drag);
                type.GetField("dragSource", flags).SetValue(popup, source);
                type.GetField("start", flags).SetValue(popup, new PointF(70 * scale, (source.Category == TextCategory.Subtitle ? 60 : 229) * scale));
                type.GetField("dragPoint", flags).SetValue(popup, new PointF((view == "long-file-drag" ? 360 : 420) * scale, 115 * scale));
                Type hitType = type.GetNestedType("Hit", System.Reflection.BindingFlags.NonPublic); object hit = Activator.CreateInstance(hitType, true);
                RectangleF bounds = CaptionDropAreas.Bounds(source.Category); bounds.Height = 27;
                bounds = new RectangleF(bounds.X * scale, bounds.Y * scale, bounds.Width * scale, bounds.Height * scale);
                hitType.GetField("Rect", flags).SetValue(hit, bounds); type.GetField("pressed", flags).SetValue(popup, hit);
                using (Bitmap bitmap = new Bitmap(popup.Width, popup.Height))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                { graphics.Clear(Color.FromArgb(28, 29, 32)); paint.Invoke(popup, new object[] { new PaintEventArgs(graphics, new Rectangle(Point.Empty, bitmap.Size)) }); bitmap.Save(System.IO.Path.Combine(folder, view + ".png"), System.Drawing.Imaging.ImageFormat.Png); }
            }
        }
        Console.WriteLine("Caption previews: " + folder);
    }
    static void Metadata(bool pathOnly)
    {
        IntPtr player = IntPtr.Zero;
        Native.EnumWindows(delegate(IntPtr hwnd, IntPtr data) { if (Native.IsPlayer(hwnd) && Native.IsWindowVisible(hwnd)) { player = hwnd; return false; } return true; }, IntPtr.Zero);
        if (player == IntPtr.Zero) throw new Exception("No player window");
        using (MetadataMonitor monitor = new MetadataMonitor())
        {
            Stopwatch timer = Stopwatch.StartNew();
            do { monitor.UpdatePlayer(player, timer.ElapsedMilliseconds); Application.DoEvents(); Thread.Sleep(15); }
            while (timer.ElapsedMilliseconds < 5000 && (monitor.SourcePath.Length == 0 || !pathOnly && monitor.Catalog.Audio.Count + monitor.Catalog.Video.Count == 0));
            if (pathOnly) Console.WriteLine(monitor.SourcePath); else PrintCatalog(monitor.Catalog);
        }
    }
    static void PrintCatalog(MediaCatalog catalog)
    {
        UiLanguage language = UiLanguage.Read(); List<object> audio = new List<object>(), video = new List<object>();
        foreach (MediaTrack track in catalog.Audio) audio.Add(new { Label = track.Label, Summary = track.Summary(language), Details = track.Details(language) });
        foreach (MediaTrack track in catalog.Video) video.Add(new { Label = track.Label, Summary = track.Summary(language), Details = track.Details(language) });
        List<object> text = new List<object>(); foreach (ContainerCaption track in catalog.Text) text.Add(new { track.Ordinal, track.Title, track.Language, track.Codec, track.Default, track.Bitmap });
        Console.WriteLine(new JavaScriptSerializer().Serialize(new { Audio = audio, Video = video, Text = text }));
    }
    static void LayoutCheck()
    {
        IntPtr player = Native.PlayerWindows().Find(p => Native.IsWindowVisible(p)); if (player == IntPtr.Zero) throw new Exception("No player window");
        Native.Rect original; Native.GetWindowRect(player, out original); bool maximized = (Native.GetWindowLong(player, -16) & 0x01000000) != 0;
        bool originallyOpen = PlaylistLayout.IsOpen(player); PlaylistLayout layout = new PlaylistLayout(player);
        Action<int,int> resize = (width,height) => { Native.SetWindowPos(player, IntPtr.Zero, 110, 90, width, height, 0x0014); Thread.Sleep(350); };
        Action<string> snapshot = label => { Native.Rect r; Native.GetWindowRect(player, out r); Console.WriteLine(label + " outer=" + r.left + "," + r.top + "," + r.right + "," + r.bottom + " video=" + Native.ClientBounds(Native.VideoSurface(player)) + " sidebar=" + PlaylistLayout.IsOpen(player)); };
        try
        {
            ShowWindow(player, 9); resize(1180,720);
            if (PlaylistLayout.IsOpen(player)) { layout.Toggle(); Thread.Sleep(350); }
            Rectangle closed = Native.ClientBounds(Native.VideoSurface(player)); Native.Rect closedBounds; Native.GetWindowRect(player, out closedBounds); snapshot("closed");
            layout.Toggle(); Thread.Sleep(350); snapshot("opened");
            Equal(true, PlaylistLayout.IsOpen(player), "playlist opens inside the window");
            Native.Rect openBounds; Native.GetWindowRect(player, out openBounds);
            Equal(closedBounds.right-closedBounds.left, openBounds.right-openBounds.left, "opening playlist preserves outer width");
            Equal(closedBounds.bottom-closedBounds.top, openBounds.bottom-openBounds.top, "opening playlist preserves outer height");
            Equal(true, Native.ClientBounds(Native.VideoSurface(player)).Width < closed.Width, "opening playlist reduces video width");
            layout.Toggle(); Thread.Sleep(350); snapshot("closed-again");
            Equal(false, PlaylistLayout.IsOpen(player), "playlist closes");
            Equal(closed.Width, Native.ClientBounds(Native.VideoSurface(player)).Width, "closing playlist restores video width");
            // Native F6 / skin command must receive the same inward layout correction.
            Native.SetCommand(player,10011); Thread.Sleep(500); snapshot("native-open");
            Native.GetWindowRect(player, out openBounds); Equal(closedBounds.right-closedBounds.left, openBounds.right-openBounds.left, "native playlist action also preserves outer width");
            resize(900,650); snapshot("compact"); Equal(false, PlaylistLayout.IsOpen(player), "compact width automatically closes playlist");
            layout.Toggle(); Thread.Sleep(200); Equal(false, PlaylistLayout.IsOpen(player), "compact window prevents opening an unusable sidebar");
            resize(1180,530); snapshot("compact-height"); Equal(false, PlaylistLayout.IsOpen(player), "short window also keeps sidebar closed");
            resize(1180,400); snapshot("minimal-height"); Equal(true, HasSurface(player,"简洁播放控件"), "short height exposes essential controls independently of width");
            resize(700,400); snapshot("minimal");
            Equal(true, HasSurface(player,"简洁播放控件"), "minimal window exposes essential controls");
            Equal(false, HasSurface(player,"字幕和弹幕"), "minimal window hides large caption menu entry");
            resize(1180,720); snapshot("restored");
            Equal(false, HasSurface(player,"简洁播放控件"), "growing window restores normal controls");
            Equal(true, HasSurface(player,"字幕和弹幕"), "caption menu returns after growing window");
            Console.WriteLine("PASS: " + checks + " live layout checks");
        }
        finally
        {
            resize(1180,720); if (PlaylistLayout.IsOpen(player) != originallyOpen) { layout.Toggle(); Thread.Sleep(200); }
            Native.SetWindowPos(player,IntPtr.Zero,original.left,original.top,original.right-original.left,original.bottom-original.top,0x0014);
            if (maximized) ShowWindow(player,3);
        }
    }
    static bool HasSurface(IntPtr player, string expected)
    {
        bool found = false; Native.EnumChildWindows(player, delegate(IntPtr child, IntPtr unused) { if (Native.IsWindowVisible(child)) { System.Text.StringBuilder title = new System.Text.StringBuilder(128); GetWindowText(child,title,title.Capacity); if (title.ToString() == expected) found = true; } return true; },IntPtr.Zero); return found;
    }
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int show);
    static void Status()
    {
        List<object> players = new List<object>();
        Native.EnumWindows(delegate(IntPtr hwnd, IntPtr data)
        {
            if (Native.IsPlayer(hwnd) && Native.IsWindowVisible(hwnd))
            {
                long speed, volume, position, duration, paused, mute;
                Native.Get(hwnd, Native.GetSpeed, out speed); Native.Get(hwnd, Native.GetVolume, out volume);
                Native.Get(hwnd, Native.GetPosition, out position); Native.Get(hwnd, Native.GetDuration, out duration);
                Native.Get(hwnd, Native.GetPlayStatus, out paused); Native.Get(hwnd, Native.GetMute, out mute);
                Native.Rect rect; Native.GetWindowRect(hwnd, out rect);
                players.Add(new { Window = hwnd.ToInt64(), Speed = speed, Volume = volume,
                    Position = position, Duration = duration, PlayStatus = paused, Mute = mute,
                    Fullscreen = Native.IsFullscreen(hwnd), Style = Native.GetWindowLong(hwnd, -16),
                    Menu = Native.GetMenu(hwnd).ToInt64(),
                    Client = Native.ClientBounds(hwnd), Video = Native.ClientBounds(Native.VideoSurface(hwnd)), Sidebar = PlaylistLayout.IsOpen(hwnd), Stage = ResponsivePolicy.Stage(Native.ClientBounds(hwnd).Width, Native.ClientBounds(hwnd).Height, Math.Max(96, Native.GetDpiForWindow(hwnd)) / 96.0).ToString(), VolumeButton = Native.VolumeButtonBounds(hwnd), SpeedButton = Native.SpeedButtonBounds(hwnd),
                    Bounds = new int[] { rect.left, rect.top, rect.right, rect.bottom } });
            }
            return true;
        }, IntPtr.Zero);
        Console.WriteLine(new JavaScriptSerializer().Serialize(players));
    }
    static void UiStatus()
    { Console.WriteLine(UiSnapshot()); }
    static string UiSnapshot()
    {
        List<object> surfaces = new List<object>();
        List<object> nativeFrames = new List<object>();
        HashSet<uint> playerPids = new HashSet<uint>();
        int z = 0;
        List<object> probes = new List<object>();
        foreach (IntPtr player in Native.PlayerWindows())
        {
            uint pid; Native.GetWindowThreadProcessId(player, out pid); playerPids.Add(pid);
            Rectangle client = Native.ClientBounds(player), volume = Native.VolumeButtonBounds(player);
            Native.Point body = new Native.Point { x = client.Left + client.Width / 2, y = client.Top + client.Height / 2 };
            Native.Point control = new Native.Point { x = volume.Left + volume.Width / 2, y = volume.Top + volume.Height / 2 };
            probes.Add(new { Player = player.ToInt64(), Body = Native.WindowFromPoint(body).ToInt64(), Control = Native.WindowFromPoint(control).ToInt64(), BarVisible = Native.ControlBarVisible(player) });
            Native.EnumChildWindows(player, delegate(IntPtr child, IntPtr data)
            {
                uint childPid; Native.GetWindowThreadProcessId(child, out childPid);
                System.Text.StringBuilder name = new System.Text.StringBuilder(128); Native.GetClassName(child, name, name.Capacity);
                Native.Rect bounds; Native.GetWindowRect(child, out bounds);
                if (bounds.right > bounds.left && bounds.bottom > bounds.top)
                    nativeFrames.Add(new { Window = child.ToInt64(), Parent = Native.GetParent(child).ToInt64(), Visible = Native.IsWindowVisible(child), Class = name.ToString(), Bounds = new int[] { bounds.left, bounds.top, bounds.right, bounds.bottom } });
                if (childPid != pid)
                {
                    System.Text.StringBuilder title = new System.Text.StringBuilder(128); GetWindowText(child, title, title.Capacity);
                    if (title.ToString() == "播放控制栏" || title.ToString() == "倍速按钮" || title.ToString() == "音频参数" || title.ToString() == "视频参数" || title.ToString() == "字幕和弹幕画面" || title.ToString() == "字幕和弹幕菜单" || title.ToString() == "字幕和弹幕")
                        surfaces.Add(new { Title = title.ToString(), Window = child.ToInt64(), Parent = Native.GetParent(child).ToInt64(),
                            Root = Native.GetAncestor(child, 2).ToInt64(), Embedded = (Native.GetWindowLong(child, -16) & 0x40000000) != 0,
                            Dpi = Native.GetDpiForWindow(child), DpiContext = Native.GetWindowDpiAwarenessContext(child).ToInt64(),
                            ParentDpiContext = Native.GetWindowDpiAwarenessContext(Native.GetParent(child)).ToInt64(),
                            Visible = Native.IsWindowVisible(child), Bounds = new int[] { bounds.left, bounds.top, bounds.right, bounds.bottom } });
                }
                return true;
            }, IntPtr.Zero);
        }
        Native.EnumWindows(delegate(IntPtr window, IntPtr data)
        {
            int order = z++;
            uint pid; Native.GetWindowThreadProcessId(window, out pid);
            if (playerPids.Contains(pid) && Native.IsWindowVisible(window))
            {
                System.Text.StringBuilder name = new System.Text.StringBuilder(128); Native.GetClassName(window, name, name.Capacity);
                Native.Rect bounds; Native.GetWindowRect(window, out bounds);
                nativeFrames.Add(new { Window = window.ToInt64(), Class = name.ToString(), Z = order, ExStyle = Native.GetWindowLong(window, -20), Bounds = new int[] { bounds.left, bounds.top, bounds.right, bounds.bottom } });
            }
            try
            {
                using (Process process = Process.GetProcessById((int)pid))
                    if (process.ProcessName != "BilibiliBehaviors") return true;
                System.Text.StringBuilder title = new System.Text.StringBuilder(128);
                GetWindowText(window, title, title.Capacity);
                if (title.Length == 0) return true;
                Native.Rect rect; Native.GetWindowRect(window, out rect);
                surfaces.Add(new { Title = title.ToString(), Window = window.ToInt64(), Owner = GetWindow(window, 4).ToInt64(), Z = order,
                    Visible = Native.IsWindowVisible(window), ExStyle = Native.GetWindowLong(window, -20),
                    Bounds = new int[] { rect.left, rect.top, rect.right, rect.bottom } });
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return true;
        }, IntPtr.Zero);
        Native.Point pointer; Native.GetCursorPos(out pointer);
        return new JavaScriptSerializer().Serialize(new { Foreground = Native.GetForegroundWindow().ToInt64(), Pointer = pointer, Probes = probes, Surfaces = surfaces, NativeFrames = nativeFrames });
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int length);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
}

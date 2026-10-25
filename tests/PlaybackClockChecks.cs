using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using BilibiliPotPlayer;

internal static class PlaybackClockChecks
{
    internal static void Run(Action<bool,bool,string> equal)
    {
        Action<bool,string> check = (v,m) => equal(true,v,m);
        PlaybackClock clock = new PlaybackClock();
        double previous = 0; bool smooth = true, synchronized = true;
        for (int now = 0; now <= 3000; now += 16)
        {
            if (now % 48 == 0) clock.Observe(1000 + now / 100 * 100, 2, 1, now);
            double position = clock.Position(now);
            if (now > 0 && (position - previous < 10 || position - previous > 21)) smooth = false;
            if (Math.Abs(position - (1000 + now)) > 80) synchronized = false;
            previous = position;
        }
        check(smooth && synchronized, "quantized 100 ms IPC samples produce continuous 16 ms frames without clock drift");
        PlaybackClock coarse = new PlaybackClock(); previous=0; smooth=true;
        for(int now=0;now<=4000;now+=16)
        {
            if(now%48==0) coarse.Observe(1000+now/500*500,2,1,now);
            double position=coarse.Position(now);
            if(now>0 && (position-previous<10 || position-previous>21)) smooth=false;
            previous=position;
        }
        check(smooth,"real PotPlayer half-second position samples never cause periodic interpolation freezes");
        clock.Observe(4000, 1, 1, 3000); double paused = clock.Position(3000);
        check(paused == 4000 && clock.Position(9000) == paused, "pause freezes at the player's actual timestamp");
        clock.Observe(4000, 2, 1, 9000);
        check(clock.Position(9016) == 4016, "resume advances from the paused timestamp");
        clock.Observe(20000, 2, 1, 9020); check(clock.Position(9020) == 20000, "forward seek immediately replaces the interpolated clock");
        clock.Observe(2000, 2, 1, 9040); check(clock.Position(9040) == 2000, "backward seek immediately clears the old timeline");
        clock.Observe(2000, 1, 1, 9050); clock.Observe(10000, 1, 1, 9060);
        check(clock.Position(10000) == 10000, "seeking while paused displays only the target frame");
        clock.Observe(10000, 0, 1, 10000); clock.Observe(10000,0,1,10150);
        check(!clock.Valid && clock.Position(20000) == 0, "a confirmed stop resets the interpolation state");
        clock.Observe(1000, 2, 1, 0);
        for (int now = 50; now <= 1000; now += 50) clock.Observe(1000, 2, 1, now);
        check(clock.Position(1000) == 1000+PlaybackClock.ExtrapolationLimit && clock.Position(2000) == 1000+PlaybackClock.ExtrapolationLimit, "a stalled playing clock cannot drift beyond the extrapolation limit");
        clock.Observe(10000, 2, 1, 2020); check(clock.Position(2020) == 10000, "clock resynchronizes when playback advances after a stall");
        clock.Reset(); clock.Observe(0, 2, 1, 0); clock.Observe(100, 2, 1, 100);
        double before = clock.AnimationTime(.1); clock.Observe(100, 2, 2, 100);
        check(Math.Abs(clock.AnimationTime(.1) - before) < .0001 && Math.Abs(clock.Position(116) - 132) < .001, "a speed change is continuous and advances media at the new rate");
        clock.Observe(300, 2, 2, 200);
        check(Math.Abs(clock.AnimationTime(.3) - .2) < .0001 && Math.Abs(clock.MediaTime(.2) - .3) < .0001, "danmaku time integrates earlier playback rates and supports inverse lookup");
        List<TextCue> cues = new List<TextCue> { new TextCue { Start = 0, End = 10, Text = "steady" } };
        CaptionOptions options = new CaptionOptions { Region = 100, AvoidSubtitles = false };
        clock.Reset(); clock.Observe(0, 2, 1, 0); clock.Observe(100, 2, 1, 100);
        float x1 = DanmakuTestFrames.At(cues, .1, 320, 180, options, (cue,size) => 60, 1, clock)[0].X;
        clock.Observe(100, 2, 2, 100);
        float x2 = DanmakuTestFrames.At(cues, .1, 320, 180, options, (cue,size) => 60, 2, clock)[0].X;
        float x3 = DanmakuTestFrames.At(cues, .132, 320, 180, options, (cue,size) => 60, 2, clock)[0].X;
        check(x1 == x2 && Math.Abs(x2 - x3 - .76) < .01, "ordinary comments do not jump when temporarily entering 2x playback");
        options.DanmakuSpeedSync = true;
        float x4 = DanmakuTestFrames.At(cues, .132, 320, 180, options, (cue,size) => 60, 2, clock)[0].X;
        check(x4 < x3, "enabling speed sync still makes ordinary comments follow media time");
        clock.Observe(300, 2, .5, 200);
        check(Math.Abs(clock.AnimationTime(.35) - .3) < .0001, "slow playback preserves the wall-time comment clock across rate segments");
        clock.Reset(); check(!clock.Valid && clock.AnimationTime(20) == 20, "media replacement discards speed history");
        var jitter = new PlaybackClock();
        jitter.Observe(1000,2,1,0); jitter.Observe(1500,2,1,30);
        int stableEpoch=jitter.Epoch;
        jitter.Position(400); jitter.Observe(1500,2,1,410);
        check(jitter.Epoch==stableEpoch,"a repeated coarse sample after an early first tick cannot be mistaken for a seek");
        jitter.Observe(2000,2,1,530); jitter.Position(1100);
        jitter.Observe(2000,2,1,1110);
        check(jitter.Epoch==stableEpoch,"late unchanged player reports never clear live comments");
        jitter.Observe(1990,2,1,1140);
        check(jitter.Epoch==stableEpoch,"small backward sampling jitter retains the live timeline");
        jitter.Reset(); jitter.Observe(1000,2,1,0); jitter.Observe(1500,2,1,500); jitter.Observe(1900,2,1,1000);
        stableEpoch=jitter.Epoch; jitter.Observe(1900,2,1,1660); jitter.Observe(1900,2,1,1800);
        check(jitter.Epoch==stableEpoch,"a late coarse tick followed by buffering freezes without repeatedly resetting admission");
        var transient=new PlaybackClock(); transient.Observe(1000,2,1,0);
        stableEpoch=transient.Epoch; transient.Observe(1000,0,1,30); transient.Observe(1000,2,1,60);
        check(transient.Valid && transient.Epoch==stableEpoch,"a transient stopped report does not clear and recreate a running scene");
        transient.Observe(1000,0,1,90); transient.Observe(1000,0,1,300);
        check(!transient.Valid,"a confirmed stop still clears the playback clock");
    }
    // Run inside the player's interactive session; no seeking or input injection.
    internal static void Sample(string path,int duration=4000)
    {
        List<IntPtr> players = Native.PlayerWindows(); if (players.Count == 0) throw new InvalidOperationException("No player");
        Stopwatch timer = Stopwatch.StartNew(); PlaybackClock clock = new PlaybackClock();
        List<string> rows = new List<string> { "wall,raw,interpolated,status,rate,epoch,sampleOk" }; long next = 0, sample = 0, status = 0, speed = 1000; bool sampleOk=true;
        while (timer.ElapsedMilliseconds < Math.Max(1000,Math.Min(120000,duration)))
        {
            long now = timer.ElapsedMilliseconds;
            if (now >= next) { next = now + 30; sampleOk=Native.Get(players[0], Native.GetPosition, out sample) && Native.Get(players[0], Native.GetPlayStatus, out status); if(sampleOk) { Native.Get(players[0], Native.GetSpeed, out speed); clock.Observe(sample,(int)status,speed / 1000.0,now); } }
            rows.Add(String.Join(",", now, sample, clock.Position(now).ToString("0.###",CultureInfo.InvariantCulture), status, speed,clock.Epoch,sampleOk)); Thread.Sleep(8);
        }
        File.WriteAllLines(path,rows);
    }
}

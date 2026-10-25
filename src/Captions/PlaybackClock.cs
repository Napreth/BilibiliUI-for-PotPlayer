using System;
using System.Collections.Generic;

namespace BilibiliPotPlayer
{
    // Interpolate quantized player samples against the host's monotonic clock. Never
    // run indefinitely through buffering, or smooth over an actual seek.
    internal sealed class PlaybackClock
    {
        sealed class Segment { internal double Media, Animation, Rate; }
        readonly List<Segment> segments = new List<Segment>();
        double anchor, rate = 1, rendered;
        long wallAnchor, lastAdvance, lastRaw, lastObservation;
        long pendingStop = -1;
        bool valid, playing, advanced;
        int extrapolation = ExtrapolationLimit;
        internal bool Valid { get { return valid; } }
        internal double Rate { get { return rate; } }
        internal int Epoch { get; private set; }
        internal const int ExtrapolationLimit = 750;
        internal void Reset() { valid = playing = false; pendingStop = -1; segments.Clear(); rendered = 0; Epoch++; }
        void Anchor(long media, long now, bool running, double speed)
        {
            valid = true; advanced = false; extrapolation = ExtrapolationLimit; playing = running; rate = speed; anchor = rendered = lastRaw = media;
            wallAnchor = lastAdvance = lastObservation = now; segments.Clear();
            segments.Add(new Segment { Media = media / 1000.0, Animation = media / 1000.0, Rate = speed });
        }
        internal void Observe(long media, int status, double speed, long now)
        {
            // IPC may briefly report no state while the player recreates its video
            // surface. Confirm a stop before discarding live instances.
            if (status == 0)
            {
                if (!valid) return;
                if (pendingStop < 0) { anchor = rendered = Position(now); wallAnchor = now; playing = false; pendingStop = now; }
                if (now - pendingStop >= 150) Reset();
                return;
            }
            if (media < 0) return;
            pendingStop = -1;
            if (speed <= 0 || Double.IsNaN(speed) || Double.IsInfinity(speed)) speed = rate;
            speed = Math.Max(.1, Math.Min(12, speed));
            bool running = status == 2;
            if (!valid) { Anchor(media, now, running, speed); return; }
            double predicted = Position(now), error = media - predicted;
            // A stale report behind the interpolated frame is not a backward seek.
            // Detect backward jumps against the last raw sample instead, and never
            // interpret an unchanged sample during buffering as a new timeline.
            if (now < lastObservation || media < lastRaw - 250 || media > lastRaw && error > Math.Max(750, extrapolation * rate))
            { Epoch++; Anchor(media, now, running, speed); return; }
            if (playing != running)
            {
                // A paused position report can still be the previous coarse sample.
                // Freeze the frame already shown instead of rewinding live comments.
                anchor = rendered = Math.Max(predicted, media); wallAnchor = lastAdvance = now; playing = running;
            }
            else if (!running) { anchor = rendered = Math.Max(rendered, media); wallAnchor = lastAdvance = now; }
            else if (media > lastRaw)
            {
                int cadence = (int)Math.Max(ExtrapolationLimit, Math.Min(3000, now - lastAdvance + 150));
                // The first advancing sample locates the player's coarse clock phase.
                // Its typical 500 ms cadence must not be mistaken for buffering.
                if (!advanced && error > 40) { Anchor(media, now, running, speed); advanced = true; extrapolation = cadence; return; }
                advanced = true; extrapolation = cadence;
                // Small bounded phase corrections avoid turning every IPC sample into
                // a visible step backwards. Quantization error within 40 ms is expected.
                double correction = Math.Abs(error) > 40 ? Math.Max(-2, Math.Min(2, error * .1)) : 0;
                anchor = Math.Max(rendered, predicted + correction); wallAnchor = now; lastAdvance = now;
            }
            if (speed != rate)
            {
                double position = Position(now) / 1000.0;
                while (segments.Count > 1 && segments[segments.Count - 1].Media >= position) segments.RemoveAt(segments.Count - 1);
                double animation = AnimationTime(position);
                segments.Add(new Segment { Media = position, Animation = animation, Rate = speed });
                anchor = rendered = position * 1000; wallAnchor = now; rate = speed;
            }
            lastRaw = Math.Max(lastRaw,media); lastObservation = now;
            double currentAnimation = AnimationTime(Position(now) / 1000.0);
            while (segments.Count > 2 && currentAnimation - segments[1].Animation > 120) segments.RemoveAt(0);
        }
        internal double Position(long now)
        {
            if (!valid) return 0;
            long end = playing ? Math.Min(now, lastAdvance + extrapolation) : wallAnchor;
            double value = anchor + Math.Max(0, end - wallAnchor) * (playing ? rate : 0);
            rendered = Math.Max(rendered, value); return rendered;
        }
        internal double AnimationTime(double media)
        {
            if (segments.Count == 0) return media;
            int i = segments.Count - 1; while (i > 0 && media < segments[i].Media) i--;
            Segment s = segments[i]; return s.Animation + (media - s.Media) / s.Rate;
        }
        internal double MediaTime(double animation)
        {
            if (segments.Count == 0) return animation;
            int i = segments.Count - 1; while (i > 0 && animation < segments[i].Animation) i--;
            Segment s = segments[i]; return s.Media + (animation - s.Animation) * s.Rate;
        }
    }
}

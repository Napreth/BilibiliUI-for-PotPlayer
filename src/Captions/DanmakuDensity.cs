using System;

namespace BilibiliPotPlayer
{
    internal static class DanmakuDensity
    {
        internal static int Effective(CaptionOptions options) { return options.Region==100?options.Density:1; }
        internal static bool Overlap(CaptionOptions options) { return Effective(options)==3; }
        // Match the player's quality-weight gates. Missing cloud weights are not
        // synthesized: local ASS/text must not be randomly discarded.
        internal static bool Allows(TextCue cue,CaptionOptions options)
        { return !cue.Weight.HasValue || Math.Abs((double)cue.Weight.Value)>=(Effective(options)==1?3:2); }
    }
}

using System;
using System.Collections.Generic;

namespace BilibiliPotPlayer
{
    // Authored ASS retains its own coordinates and duration. Source/type switches
    // immediately gate visibility without changing the surviving authored effects.
    internal sealed class AdvancedDanmakuPlayback
    {
        internal sealed class Entry { internal TextSource Source; internal TextCue Cue; }
        readonly List<Entry> timeline = new List<Entry>();
        internal readonly List<Entry> Active = new List<Entry>();
        int revision = -1, epoch;
        bool initialized;
        double previous;
        internal void Reset() { Active.Clear(); timeline.Clear(); initialized = false; revision = -1; }
        internal bool Advance(CaptionCatalog catalog, double media, int clockEpoch)
        {
            bool changed = false, first = !initialized || epoch != clockEpoch;
            if (first) { changed = Active.Count > 0; Active.Clear(); previous = media; epoch = clockEpoch; initialized = true; }
            if (revision != catalog.Revision)
            {
                revision = catalog.Revision; timeline.Clear();
                foreach (TextSource source in catalog.Sources)
                    if (source.Category == TextCategory.Advanced && source.AssRenderable && !source.Bitmap)
                        foreach (TextCue cue in source.Events) if ((cue.Effects & CueEffects.Protocol) == 0) timeline.Add(new Entry { Source=source, Cue=cue });
                timeline.Sort((a,b) => a.Cue.Start.CompareTo(b.Cue.Start));
            }
            for (int i = Active.Count - 1; i >= 0; i--)
                if (Active[i].Cue.End <= media || !Active[i].Source.Enabled || Active[i].Source.Category!=TextCategory.Advanced || !catalog.Options.AssEffectsEnabled || !catalog.Sources.Contains(Active[i].Source)) { Active.RemoveAt(i); changed = true; }
            if (!catalog.Options.DanmakuEnabled && Active.Count>0) { Active.Clear(); changed=true; }
            double cutoff = first ? media : previous + .0000001;
            int low=0, high=timeline.Count;
            while(low<high) { int middle=(low+high)/2; if(timeline[middle].Cue.Start<cutoff) low=middle+1; else high=middle; }
            for(int i=low;i<timeline.Count && timeline[i].Cue.Start<=media;i++)
                if(catalog.Options.DanmakuEnabled && catalog.Options.AssEffectsEnabled && DanmakuDensity.Allows(timeline[i].Cue,catalog.Options) && timeline[i].Source.Enabled && timeline[i].Cue.End>media) { Active.Add(timeline[i]); changed=true; }
            previous=Math.Max(previous,media); return changed;
        }
    }
}

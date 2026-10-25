using System;

namespace BilibiliPotPlayer
{
    internal static class DanmakuTiming
    {
        static readonly double[] Boundaries = { 558, 888, 1082, 1246, 1452, 1744, 2314, 2560 };
        static readonly double[] Minimum = { 7, 8, 9, 10, 11, 13, 14, 16 }, Maximum = { 13, 14, 14, 15, 16, 18, 20, 22 };
        // The web curve operates in CSS pixels. Convert both widths together so
        // changing monitor DPI cannot change the speed of the same logical view.
        internal static double BaseDuration(double viewport, double text, float dpi)
        {
            viewport /= dpi; text /= dpi;
            if (viewport < Boundaries[0] || text <= 0 || Double.IsNaN(viewport) || Double.IsNaN(text) || Double.IsInfinity(viewport) || Double.IsInfinity(text)) return 4.5;
            int index=0; while(index<Boundaries.Length-1 && viewport>Boundaries[index+1]) index++;
            double extra=0;
            if(viewport>Boundaries[Boundaries.Length-1]) { index=Boundaries.Length-1; extra=Math.Floor((viewport-Boundaries[index])/500); }
            double low=Minimum[index]+extra, high=Maximum[index]+extra;
            double center=Math.Log((high-low)/.1-1)/.2+.2;
            double exponent=Math.Max(-700,Math.Min(700,-.2*(text/viewport-center)));
            return low+(high-low)/(1+Math.Exp(exponent));
        }
        internal static float Height(TextCue cue,float size)
        {
            int lines=1; foreach(char c in cue.Text) if(c=='\n')lines++;
            return size*1.125f*lines;
        }
    }
}

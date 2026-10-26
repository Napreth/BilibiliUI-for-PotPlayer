using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace BilibiliPotPlayer
{
    internal static class CaptionFileNames
    {
        const TextFormatFlags Flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        static int Width(Graphics graphics, string text, Font font)
        { return TextRenderer.MeasureText(graphics, text, font, new Size(Int32.MaxValue, Int32.MaxValue), Flags).Width; }
        internal static string Fit(Graphics graphics, string name, Font font, int width)
        {
            if (String.IsNullOrEmpty(name) || Width(graphics, name, font) <= width) return name;
            int dot = name.LastIndexOf('.');
            if (dot < 0 || !CaptionCatalog.SupportedExtension(name.Substring(dot))) return name;
            string stem = name.Substring(0, dot), extension = name.Substring(dot), suffix = "… " + extension;
            if (Width(graphics, suffix, font) > width) return Width(graphics, extension, font) <= width ? extension : "";
            // Search only whole text elements so emoji and combining marks are
            // never cut in half when measuring a truncated filename.
            int[] elements = StringInfo.ParseCombiningCharacters(stem);
            Func<int, string> prefix = count => stem.Substring(0, count == elements.Length ? stem.Length : elements[count]).TrimEnd() + suffix;
            int low = 0, high = elements.Length;
            while (low < high) {
                int middle = (low + high + 1) / 2;
                if (Width(graphics, prefix(middle), font) <= width) low = middle;
                else high = middle - 1;
            }
            return prefix(low);
        }
    }
}

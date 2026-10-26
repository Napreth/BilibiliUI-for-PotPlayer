using System;

namespace BilibiliPotPlayer
{
    internal sealed class TranslationException : Exception
    { internal TranslationException(string message) : base(message) { } }

    internal static class TranslationLanguages
    {
        internal static readonly string[] Codes = { "auto", "zh-CN", "zh-TW", "en", "ja", "ko", "fr", "de", "es", "ru", "pt", "ar" };
        internal static readonly string[] Names = { "自动检测", "简体中文", "繁體中文", "English", "日本語", "한국어", "Français", "Deutsch", "Español", "Русский", "Português", "العربية" };
        internal static string Normalize(string code, bool source)
        { int index = Array.IndexOf(Codes, code); return index < (source ? 0 : 1) ? source ? "auto" : "zh-CN" : code; }
        internal static string Name(string code) { int index = Array.IndexOf(Codes, code); return index < 0 ? code : Names[index]; }
    }
}

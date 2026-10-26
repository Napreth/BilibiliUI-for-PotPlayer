using System;
using System.IO;
using System.Text;

namespace BilibiliPotPlayer
{
    internal sealed class NativeTranslationPacket
    {
        internal const int Magic = 0x42545231, Maximum = 32 + 8192 + 32768 + 4096;
        internal ulong Token;
        internal uint Player;
        internal string Original, Translated, Engine, Source, Target;
        internal static NativeTranslationPacket Read(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 32 || bytes.Length > Maximum) return null;
            try
            {
                using (var input = new BinaryReader(new MemoryStream(bytes), new UTF8Encoding(false, true)))
                {
                    if (input.ReadInt32() != Magic || input.ReadInt32() != 1) return null;
                    var packet = new NativeTranslationPacket { Token = input.ReadUInt64(), Player = input.ReadUInt32() };
                    uint original = input.ReadUInt32(), translated = input.ReadUInt32(), meta = input.ReadUInt32();
                    if (packet.Token == 0 || original == 0 || original > 8192 || translated == 0 || translated > 32768 || meta > 4096 || 32L + original + translated + meta != bytes.Length) return null;
                    var utf8 = new UTF8Encoding(false, true);
                    packet.Original = utf8.GetString(input.ReadBytes((int)original));
                    packet.Translated = utf8.GetString(input.ReadBytes((int)translated));
                    string[] metadata = utf8.GetString(input.ReadBytes((int)meta)).Split('\n');
                    if (metadata.Length != 3 || Path.GetFileName(metadata[0]) != metadata[0] || metadata[0].Length == 0 || metadata[1].Length > 64 || metadata[2].Length > 64 || packet.Original.IndexOf('\0') >= 0 || packet.Translated.IndexOf('\0') >= 0) return null;
                    packet.Engine = metadata[0]; packet.Source = metadata[1]; packet.Target = metadata[2];
                    return packet;
                }
            }
            catch (IOException) { return null; } catch (ArgumentException) { return null; }
        }
    }
}

using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace BilibiliPotPlayer
{
    internal static class RawCompression
    {
        static readonly uint[] CrcTable = MakeCrcTable();
        static uint[] MakeCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++) { uint crc = i; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320U : 0); table[i] = crc; }
            return table;
        }
        static uint Little32(byte[] bytes, int at)
        { return (uint)bytes[at] | (uint)bytes[at + 1] << 8 | (uint)bytes[at + 2] << 16 | (uint)bytes[at + 3] << 24; }
        [DllImport("BilibiliRawCompression.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int bili_raw_decompress(int codec, byte[] input, int length, byte[] output, int capacity);
        internal static byte[] Decompress(byte[] input, string codec, int limit)
        {
            if (input.Length == 0 || input.Length > TimedText.MaxBytes || limit < 1 || limit > TimedText.MaxBytes) throw new InvalidDataException("Raw 大小超限");
            if (codec == "gzip") {
                if (input.Length < 18 || input[0] != 31 || input[1] != 139 || input[2] != 8) throw new InvalidDataException("Raw gzip 头部无效");
                using (var source = new MemoryStream(input, false))
                using (var gzip = new GZipStream(source, CompressionMode.Decompress))
                using (var output = new MemoryStream()) {
                    byte[] buffer = new byte[8192]; int count;
                    while ((count = gzip.Read(buffer, 0, buffer.Length)) > 0) {
                        if (output.Length + count > limit) throw new InvalidDataException("Raw 解压大小超限");
                        output.Write(buffer, 0, count);
                    }
                    byte[] decoded = output.ToArray(); uint crc = 0xFFFFFFFFU;
                    foreach (byte value in decoded) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
                    // Framework GZipStream can return partial output without
                    // rejecting a missing trailer. Validate this single-member
                    // format's trailer explicitly before trusting its metadata.
                    if (Little32(input, input.Length - 4) != decoded.Length || Little32(input, input.Length - 8) != (crc ^ 0xFFFFFFFFU)) throw new InvalidDataException("Raw gzip 校验失败或被截断");
                    return decoded;
                }
            }
            int kind = codec == "zstd" ? 1 : codec == "brotli" ? 2 : 0;
            if (kind == 0) throw new InvalidDataException("不支持的 Raw 压缩格式");
            byte[] result = new byte[limit];
            int size = bili_raw_decompress(kind, input, input.Length, result, limit);
            if (size < 0 || size > limit) throw new InvalidDataException("Raw 解压失败或大小超限");
            Array.Resize(ref result, size); return result;
        }
        // Compatible with base16384@1.0.0's always-present U+3D00..U+3D06
        // length marker, including the marker for a complete seven-byte block.
        internal static byte[] DecodeBase(string text, string encoding)
        {
            if (text.Length > TimedText.MaxBytes) throw new InvalidDataException("Raw 编码大小超限");
            if (encoding == "base64") return Convert.FromBase64String(text);
            if (encoding != "base18384" && encoding != "base16384") throw new InvalidDataException("不支持的 Raw 字符编码");
            if (text.Length < 1) throw new InvalidDataException("缺少 Base16384 长度标记");
            int chars = text.Length - 1, rest = text[chars] - 0x3D00;
            if (rest < 0 || rest > 6 || chars % 4 != (rest * 4 + 6) / 7 % 4 || chars == 0 && rest != 0) throw new InvalidDataException("Base16384 长度标记无效");
            int length = chars == 0 ? 0 : (chars - 1) / 4 * 7 + (rest == 0 ? 7 : rest);
            if (length > TimedText.MaxBytes) throw new InvalidDataException("Raw 大小超限");
            byte[] bytes = new byte[length]; int bits = 0, index = 0; uint value = 0;
            for (int i = 0; i < chars; i++) {
                int code = text[i] - 0x4E00;
                if (code < 0 || code >= 16384) throw new InvalidDataException("Base16384 字符无效");
                value = (value << 14) | (uint)code; bits += 14;
                while (bits >= 8 && index < length) { bits -= 8; bytes[index++] = (byte)(value >> bits); }
                value &= (1U << bits) - 1;
            }
            if (index != length || value != 0) throw new InvalidDataException("Base16384 填充无效");
            return bytes;
        }
    }
}

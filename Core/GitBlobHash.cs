#nullable enable

using System;
using System.IO;
using System.Text;

namespace MVZ2CloudSaves;

/// <summary>
/// Git blob SHA-1（"blob &lt;len&gt;\0&lt;content&gt;" 的 SHA-1）。
/// 纯托管实现，避免 Android BepInEx 下 System.Security.Cryptography 的 JIT 风险。
/// </summary>
internal static class GitBlobHash
{
    public static string Compute(byte[] data)
    {
        var header = Encoding.UTF8.GetBytes("blob " + data.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\0");
        var sha = new Sha1();
        sha.Update(header, 0, header.Length);
        sha.Update(data, 0, data.Length);
        return sha.FinalHex();
    }

    public static string Compute(string text) => Compute(Encoding.UTF8.GetBytes(text));

    public static string ComputeFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var len = fs.Length;
        var header = Encoding.UTF8.GetBytes("blob " + len.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\0");
        var sha = new Sha1();
        sha.Update(header, 0, header.Length);
        var buffer = new byte[81920];
        int read;
        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
            sha.Update(buffer, 0, read);
        return sha.FinalHex();
    }

    private sealed class Sha1
    {
        private readonly uint[] h = { 0x67452301, 0xEFCDAB89, 0x98BADCFE, 0x10325476, 0xC3D2E1F0 };
        private readonly byte[] block = new byte[64];
        private int blockLen;
        private ulong totalLen;

        public void Update(byte[] data, int offset, int count)
        {
            totalLen += (ulong)count;
            while (count > 0)
            {
                int take = Math.Min(64 - blockLen, count);
                Buffer.BlockCopy(data, offset, block, blockLen, take);
                blockLen += take;
                offset += take;
                count -= take;
                if (blockLen == 64)
                {
                    ProcessBlock(block, 0);
                    blockLen = 0;
                }
            }
        }

        public string FinalHex()
        {
            ulong bitLen = totalLen * 8;
            int pad = blockLen < 56 ? 56 - blockLen : 120 - blockLen;
            var padding = new byte[pad + 8];
            padding[0] = 0x80;
            for (int i = 0; i < 8; i++)
                padding[pad + i] = (byte)(bitLen >> (56 - i * 8));
            Update(padding, 0, padding.Length);

            var sb = new StringBuilder(40);
            for (int i = 0; i < 5; i++)
                sb.Append(h[i].ToString("x8", System.Globalization.CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private void ProcessBlock(byte[] data, int offset)
        {
            var w = new uint[80];
            for (int i = 0; i < 16; i++)
            {
                w[i] = (uint)(data[offset + i * 4] << 24 | data[offset + i * 4 + 1] << 16 | data[offset + i * 4 + 2] << 8 | data[offset + i * 4 + 3]);
            }
            for (int i = 16; i < 80; i++)
                w[i] = RotateLeft(w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16], 1);

            uint a = h[0], b = h[1], c = h[2], d = h[3], e = h[4];
            for (int i = 0; i < 80; i++)
            {
                uint f, k;
                if (i < 20) { f = (b & c) | (~b & d); k = 0x5A827999; }
                else if (i < 40) { f = b ^ c ^ d; k = 0x6ED9EBA1; }
                else if (i < 60) { f = (b & c) | (b & d) | (c & d); k = 0x8F1BBCDC; }
                else { f = b ^ c ^ d; k = 0xCA62C1D6; }
                uint temp = RotateLeft(a, 5) + f + e + k + w[i];
                e = d;
                d = c;
                c = RotateLeft(b, 30);
                b = a;
                a = temp;
            }
            h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e;
        }

        private static uint RotateLeft(uint x, int n) => (x << n) | (x >> (32 - n));
    }
}

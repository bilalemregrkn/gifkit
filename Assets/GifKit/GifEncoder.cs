using System;
using System.Collections.Generic;
using System.IO;

namespace GifKit
{
    public sealed class GifEncoder
    {
        private readonly int _width;
        private readonly int _height;
        private readonly int _loops;
        private readonly List<(byte[] rgb24, int delay)> _frames = new();

        public GifEncoder(int width, int height, int loops = 0)
        {
            _width = width;
            _height = height;
            _loops = loops;
        }

        public void AddFrame(byte[] rgb24Pixels, int delayHundredths = 7)
        {
            _frames.Add((rgb24Pixels, delayHundredths));
        }

        public byte[] Encode()
        {
            if (_frames.Count == 0) return Array.Empty<byte>();

            var quant = new MedianCutQuantizer(_frames[0].rgb24, _width * _height);
            byte[] palette = quant.BuildPalette();

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            bw.Write(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' });

            bw.Write((short)_width);
            bw.Write((short)_height);
            bw.Write((byte)0xF7);
            bw.Write((byte)0x00);
            bw.Write((byte)0x00);

            bw.Write(palette);

            // NETSCAPE loop extension
            bw.Write((byte)0x21); bw.Write((byte)0xFF); bw.Write((byte)11);
            bw.Write(new byte[] { (byte)'N',(byte)'E',(byte)'T',(byte)'S',(byte)'C',(byte)'A',(byte)'P',(byte)'E' });
            bw.Write(new byte[] { (byte)'2',(byte)'.',(byte)'0' });
            bw.Write((byte)3); bw.Write((byte)1);
            bw.Write((short)_loops);
            bw.Write((byte)0);

            foreach (var (rgb24, delay) in _frames)
                WriteFrame(bw, rgb24, delay, quant);

            bw.Write((byte)0x3B);
            return ms.ToArray();
        }

        private void WriteFrame(BinaryWriter bw, byte[] rgb24, int delay, MedianCutQuantizer quant)
        {
            byte[] indices = quant.Map(rgb24);

            bw.Write((byte)0x21); bw.Write((byte)0xF9); bw.Write((byte)4);
            bw.Write((byte)0x00);
            bw.Write((short)delay);
            bw.Write((byte)0x00); bw.Write((byte)0x00);

            bw.Write((byte)0x2C);
            bw.Write((short)0); bw.Write((short)0);
            bw.Write((short)_width); bw.Write((short)_height);
            bw.Write((byte)0x00);

            bw.Write((byte)8);
            LzwEncoder.Encode(indices, 8, bw);
            bw.Write((byte)0);
        }
    }

    // Median-cut quantizer using flat int arrays — zero per-pixel heap allocation.
    internal sealed class MedianCutQuantizer
    {
        private const int PaletteSize = 256;

        private readonly byte[] _pixels;
        private readonly int _pixelCount;

        // palette[i*3+0]=B, [+1]=G, [+2]=R  (matches BGR pixel input)
        private readonly int[] _palette = new int[PaletteSize * 3];
        // 32x32x32 LUT indexed by (r5 << 10 | g5 << 5 | b5)
        private readonly byte[] _lut = new byte[32 * 32 * 32];

        public MedianCutQuantizer(byte[] bgr24, int pixelCount)
        {
            _pixels    = bgr24;
            _pixelCount = pixelCount;
        }

        public byte[] BuildPalette()
        {
            BuildPaletteInternal();
            BuildLut();

            byte[] result = new byte[PaletteSize * 3];
            for (int i = 0; i < PaletteSize; i++)
            {
                result[i * 3 + 0] = (byte)_palette[i * 3 + 2]; // R
                result[i * 3 + 1] = (byte)_palette[i * 3 + 1]; // G
                result[i * 3 + 2] = (byte)_palette[i * 3 + 0]; // B
            }
            return result;
        }

        private void BuildPaletteInternal()
        {
            // Flat sample array: [b0,g0,r0, b1,g1,r1, ...]
            int sampleStep = Math.Max(1, _pixelCount / 10000);
            int sampleCount = 0;
            for (int i = 0; i < _pixelCount; i += sampleStep) sampleCount++;
            int[] samples = new int[sampleCount * 3];
            int si = 0;
            for (int i = 0; i < _pixelCount; i += sampleStep)
            {
                samples[si++] = _pixels[i * 3 + 0];
                samples[si++] = _pixels[i * 3 + 1];
                samples[si++] = _pixels[i * 3 + 2];
            }

            // Each bucket = (startIndex, count) into samples[]
            var buckets = new List<(int start, int count)> { (0, sampleCount) };

            while (buckets.Count < PaletteSize)
            {
                int largest = -1;
                int largestRange = -1;
                for (int b = 0; b < buckets.Count; b++)
                {
                    int range = GetRange(samples, buckets[b].start, buckets[b].count);
                    if (range > largestRange) { largestRange = range; largest = b; }
                }
                if (largestRange == 0) break;

                var (start, count) = buckets[largest];
                int ch = GetWidestChannel(samples, start, count);
                SortByChannel(samples, start, count, ch);
                int mid = count / 2;
                buckets[largest] = (start, mid);
                buckets.Add((start + mid, count - mid));
            }

            for (int b = 0; b < PaletteSize; b++)
            {
                if (b < buckets.Count)
                {
                    var (start, count) = buckets[b];
                    long sb = 0, sg = 0, sr = 0;
                    for (int i = 0; i < count; i++)
                    {
                        sb += samples[(start + i) * 3 + 0];
                        sg += samples[(start + i) * 3 + 1];
                        sr += samples[(start + i) * 3 + 2];
                    }
                    _palette[b * 3 + 0] = (int)(sb / count);
                    _palette[b * 3 + 1] = (int)(sg / count);
                    _palette[b * 3 + 2] = (int)(sr / count);
                }
                // else stays 0,0,0
            }
        }

        private static int GetRange(int[] samples, int start, int count)
        {
            if (count == 0) return 0;
            int minB = 255, maxB = 0, minG = 255, maxG = 0, minR = 255, maxR = 0;
            for (int i = 0; i < count; i++)
            {
                int b = samples[(start + i) * 3 + 0];
                int g = samples[(start + i) * 3 + 1];
                int r = samples[(start + i) * 3 + 2];
                if (b < minB) minB = b; if (b > maxB) maxB = b;
                if (g < minG) minG = g; if (g > maxG) maxG = g;
                if (r < minR) minR = r; if (r > maxR) maxR = r;
            }
            return Math.Max(maxB - minB, Math.Max(maxG - minG, maxR - minR));
        }

        private static int GetWidestChannel(int[] samples, int start, int count)
        {
            int minB = 255, maxB = 0, minG = 255, maxG = 0, minR = 255, maxR = 0;
            for (int i = 0; i < count; i++)
            {
                int b = samples[(start + i) * 3 + 0];
                int g = samples[(start + i) * 3 + 1];
                int r = samples[(start + i) * 3 + 2];
                if (b < minB) minB = b; if (b > maxB) maxB = b;
                if (g < minG) minG = g; if (g > maxG) maxG = g;
                if (r < minR) minR = r; if (r > maxR) maxR = r;
            }
            int bRange = maxB - minB, gRange = maxG - minG, rRange = maxR - minR;
            if (bRange >= gRange && bRange >= rRange) return 0;
            if (gRange >= rRange) return 1;
            return 2;
        }

        // In-place insertion sort on a slice of the flat samples array by channel ch.
        // Insertion sort is fast enough for the small bucket sizes median-cut produces.
        private static void SortByChannel(int[] samples, int start, int count, int ch)
        {
            for (int i = 1; i < count; i++)
            {
                int keyB = samples[(start + i) * 3 + 0];
                int keyG = samples[(start + i) * 3 + 1];
                int keyR = samples[(start + i) * 3 + 2];
                int keyVal = samples[(start + i) * 3 + ch];
                int j = i - 1;
                while (j >= 0 && samples[(start + j) * 3 + ch] > keyVal)
                {
                    samples[(start + j + 1) * 3 + 0] = samples[(start + j) * 3 + 0];
                    samples[(start + j + 1) * 3 + 1] = samples[(start + j) * 3 + 1];
                    samples[(start + j + 1) * 3 + 2] = samples[(start + j) * 3 + 2];
                    j--;
                }
                samples[(start + j + 1) * 3 + 0] = keyB;
                samples[(start + j + 1) * 3 + 1] = keyG;
                samples[(start + j + 1) * 3 + 2] = keyR;
            }
        }

        private void BuildLut()
        {
            for (int r = 0; r < 32; r++)
            for (int g = 0; g < 32; g++)
            for (int b = 0; b < 32; b++)
            {
                int rb = r * 8, gb = g * 8, bb = b * 8;
                int best = 0; int bestDist = int.MaxValue;
                for (int i = 0; i < PaletteSize; i++)
                {
                    int db = bb - _palette[i * 3 + 0];
                    int dg = gb - _palette[i * 3 + 1];
                    int dr = rb - _palette[i * 3 + 2];
                    int dist = db * db + dg * dg + dr * dr;
                    if (dist < bestDist) { bestDist = dist; best = i; }
                }
                _lut[(r << 10) | (g << 5) | b] = (byte)best;
            }
        }

        public byte[] Map(byte[] bgr24)
        {
            byte[] result = new byte[_pixelCount];
            for (int i = 0; i < _pixelCount; i++)
            {
                int b = bgr24[i * 3 + 0] >> 3;
                int g = bgr24[i * 3 + 1] >> 3;
                int r = bgr24[i * 3 + 2] >> 3;
                result[i] = _lut[(r << 10) | (g << 5) | b];
            }
            return result;
        }
    }

    internal static class LzwEncoder
    {
        private const int NoEntry = -1;

        public static void Encode(byte[] indices, int minCodeSize, BinaryWriter bw)
        {
            int clearCode = 1 << minCodeSize;
            int eofCode   = clearCode + 1;
            int codeSize  = minCodeSize + 1;
            int maxCode   = 1 << codeSize;

            // Flat array replaces Dictionary — index = prefix * 256 + suffix, value = assigned code.
            // 4096 possible codes * 256 suffixes. Reset to NoEntry on table clear.
            int tableSize = 4096 * 256;
            int[] codeTable = new int[tableSize];
            Array.Fill(codeTable, NoEntry);
            int nextCode = eofCode + 1;

            int bits = 0, bitCount = 0;
            var block = new List<byte>(255);

            void Flush255()
            {
                bw.Write((byte)255);
                foreach (var b in block) bw.Write(b);
                block.Clear();
            }

            void WriteCode(int code)
            {
                bits     |= code << bitCount;
                bitCount += codeSize;
                while (bitCount >= 8)
                {
                    block.Add((byte)(bits & 0xFF));
                    bits     >>= 8;
                    bitCount  -= 8;
                    if (block.Count == 255) Flush255();
                }
            }

            void ResetTable()
            {
                codeSize = 12;
                WriteCode(clearCode);
                codeSize  = minCodeSize + 1;
                maxCode   = 1 << codeSize;
                nextCode  = eofCode + 1;
                Array.Fill(codeTable, NoEntry);
            }

            WriteCode(clearCode);

            int prefix = -1;
            foreach (byte pixel in indices)
            {
                if (prefix == -1) { prefix = pixel; continue; }

                int key = prefix * 256 + pixel;
                int code = codeTable[key];
                if (code != NoEntry)
                {
                    prefix = code;
                }
                else
                {
                    WriteCode(prefix);
                    if (nextCode < tableSize)
                        codeTable[key] = nextCode++;
                    prefix = pixel;

                    if (nextCode > maxCode)
                    {
                        if (codeSize < 12) { codeSize++; maxCode = 1 << codeSize; }
                        else ResetTable();
                    }
                }
            }

            if (prefix != -1) WriteCode(prefix);
            WriteCode(eofCode);

            while (bitCount > 0)
            {
                block.Add((byte)(bits & 0xFF));
                bits     >>= 8;
                bitCount  -= 8;
            }
            if (block.Count > 0)
            {
                bw.Write((byte)block.Count);
                foreach (var b in block) bw.Write(b);
            }
        }
    }
}

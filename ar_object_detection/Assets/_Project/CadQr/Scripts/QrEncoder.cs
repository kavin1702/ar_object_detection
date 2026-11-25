using System;
using System.Text;

namespace CADInspection.CadQr
{
    /// <summary>
    /// A small, dependency-free QR code encoder (ISO/IEC 18004).
    ///
    /// Scope on purpose: byte mode, error-correction level L, versions 1 to 5.
    /// That covers payloads up to 106 bytes, which is plenty for an id such as
    /// "CADQR|v1|l_bracket". Versions 1 to 5 at level L all use a single
    /// Reed-Solomon block, so no block interleaving is needed.
    ///
    /// Output: a square bool grid where true = dark module. No quiet zone is
    /// included; <see cref="QrTextureFactory"/> adds it when drawing the texture.
    /// </summary>
    public static class QrEncoder
    {
        // Index = version (1..5). Data codewords and error-correction codewords at level L.
        static readonly int[] DataCodewords = { 0, 19, 34, 55, 80, 108 };
        static readonly int[] EccCodewords  = { 0, 7, 10, 15, 20, 26 };
        // Centre of the single alignment pattern (version 2+). 0 = none.
        static readonly int[] AlignmentCentre = { 0, 0, 18, 22, 26, 30 };

        public const int MaxPayloadBytes = 106;

        /// <summary>Encodes text as UTF-8 and returns the module grid (true = dark).</summary>
        public static bool[,] Encode(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text ?? string.Empty);

            // 1. Pick the smallest version that fits: 4 bits mode + 8 bits length + 8 bits per byte.
            int version = 0;
            for (int v = 1; v <= 5; v++)
            {
                if (4 + 8 + data.Length * 8 <= DataCodewords[v] * 8) { version = v; break; }
            }
            if (version == 0)
                throw new ArgumentException($"QR payload is {data.Length} bytes; this encoder supports at most {MaxPayloadBytes}.");

            byte[] codewords = BuildCodewords(data, version);
            int size = 17 + 4 * version;

            var modules = new bool[size, size];
            var isFunction = new bool[size, size];
            DrawFunctionPatterns(modules, isFunction, version);
            PlaceData(modules, isFunction, codewords);

            // 2. Try all 8 masks and keep the one with the lowest penalty score.
            int bestMask = 0;
            int bestPenalty = int.MaxValue;
            for (int mask = 0; mask < 8; mask++)
            {
                ApplyMask(modules, isFunction, mask);
                DrawFormatBits(modules, isFunction, mask);
                int penalty = Penalty(modules);
                if (penalty < bestPenalty) { bestPenalty = penalty; bestMask = mask; }
                ApplyMask(modules, isFunction, mask); // XOR again = undo
            }
            ApplyMask(modules, isFunction, bestMask);
            DrawFormatBits(modules, isFunction, bestMask);
            return modules;
        }

        // ---------- Data + error correction ----------

        static byte[] BuildCodewords(byte[] data, int version)
        {
            int capacityBits = DataCodewords[version] * 8;
            var bits = new BitBuffer();
            bits.Append(0b0100, 4);           // byte mode
            bits.Append(data.Length, 8);      // character count (8 bits for versions 1-9)
            foreach (byte b in data) bits.Append(b, 8);

            bits.Append(0, Math.Min(4, capacityBits - bits.Length)); // terminator
            bits.Append(0, (8 - bits.Length % 8) % 8);               // pad to a byte
            for (byte pad = 0xEC; bits.Length < capacityBits; pad ^= 0xEC ^ 0x11)
                bits.Append(pad, 8);

            byte[] dataCodewords = bits.ToBytes();
            byte[] ecc = ReedSolomon(dataCodewords, EccCodewords[version]);

            var all = new byte[dataCodewords.Length + ecc.Length];
            Array.Copy(dataCodewords, all, dataCodewords.Length);
            Array.Copy(ecc, 0, all, dataCodewords.Length, ecc.Length);
            return all;
        }

        static byte[] ReedSolomon(byte[] data, int degree)
        {
            // Generator polynomial = product of (x - a^i), i = 0..degree-1, in GF(256) with 0x11D.
            var gen = new byte[degree];
            gen[degree - 1] = 1;
            byte root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    gen[j] = GfMultiply(gen[j], root);
                    if (j + 1 < degree) gen[j] ^= gen[j + 1];
                }
                root = GfMultiply(root, 0x02);
            }

            var result = new byte[degree];
            foreach (byte b in data)
            {
                byte factor = (byte)(b ^ result[0]);
                Array.Copy(result, 1, result, 0, degree - 1);
                result[degree - 1] = 0;
                for (int i = 0; i < degree; i++)
                    result[i] ^= GfMultiply(gen[i], factor);
            }
            return result;
        }

        static byte GfMultiply(byte x, byte y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return (byte)z;
        }

        // ---------- Fixed patterns ----------

        static void DrawFunctionPatterns(bool[,] m, bool[,] f, int version)
        {
            int size = m.GetLength(0);

            // Timing patterns (row 6 and column 6).
            for (int i = 0; i < size; i++)
            {
                Set(m, f, 6, i, i % 2 == 0);
                Set(m, f, i, 6, i % 2 == 0);
            }

            // Three finder patterns with their white separators.
            DrawFinder(m, f, 3, 3);
            DrawFinder(m, f, size - 4, 3);
            DrawFinder(m, f, 3, size - 4);

            // Alignment pattern (only one for versions 2-5).
            int a = AlignmentCentre[version];
            if (a != 0)
            {
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        Set(m, f, a + dx, a + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }

            // Reserve the format-information areas (real bits are drawn per mask).
            DrawFormatBits(m, f, 0);
        }

        static void DrawFinder(bool[,] m, bool[,] f, int cx, int cy)
        {
            int size = m.GetLength(0);
            for (int dy = -4; dy <= 4; dy++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= size || y >= size) continue;
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    Set(m, f, x, y, dist != 2 && dist != 4);
                }
            }
        }

        static void DrawFormatBits(bool[,] m, bool[,] f, int mask)
        {
            int size = m.GetLength(0);
            // Level L = 01, followed by the 3 mask bits, then a BCH(15,5) remainder.
            int data = (0b01 << 3) | mask;
            int rem = data;
            for (int i = 0; i < 10; i++)
                rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = ((data << 10) | rem) ^ 0x5412;

            // Copy 1: around the top-left finder.
            for (int i = 0; i <= 5; i++) Set(m, f, 8, i, Bit(bits, i));
            Set(m, f, 8, 7, Bit(bits, 6));
            Set(m, f, 8, 8, Bit(bits, 7));
            Set(m, f, 7, 8, Bit(bits, 8));
            for (int i = 9; i < 15; i++) Set(m, f, 14 - i, 8, Bit(bits, i));

            // Copy 2: split between the other two finders.
            for (int i = 0; i < 8; i++) Set(m, f, size - 1 - i, 8, Bit(bits, i));
            for (int i = 8; i < 15; i++) Set(m, f, 8, size - 15 + i, Bit(bits, i));
            Set(m, f, 8, size - 8, true); // the "dark module", always dark
        }

        // ---------- Data placement + masking ----------

        static void PlaceData(bool[,] m, bool[,] f, byte[] codewords)
        {
            int size = m.GetLength(0);
            int bitIndex = 0;
            int totalBits = codewords.Length * 8;

            // Zig-zag in two-column strips from the bottom-right, skipping the timing column.
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < size; vert++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? size - 1 - vert : vert;
                        if (f[x, y]) continue;
                        if (bitIndex < totalBits)
                        {
                            m[x, y] = ((codewords[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1) != 0;
                            bitIndex++;
                        }
                        // Remainder bits stay light (false).
                    }
                }
            }
        }

        static void ApplyMask(bool[,] m, bool[,] f, int mask)
        {
            int size = m.GetLength(0);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (f[x, y]) continue;
                    bool invert;
                    switch (mask)
                    {
                        case 0: invert = (x + y) % 2 == 0; break;
                        case 1: invert = y % 2 == 0; break;
                        case 2: invert = x % 3 == 0; break;
                        case 3: invert = (x + y) % 3 == 0; break;
                        case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                        case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (invert) m[x, y] = !m[x, y];
                }
            }
        }

        // The four penalty rules from the standard. Lower = easier to scan.
        static int Penalty(bool[,] m)
        {
            int size = m.GetLength(0);
            int result = 0;

            // Rule 1: runs of 5+ same-colour modules in a row or column.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int a = 0; a < size; a++)
                {
                    int run = 1;
                    for (int b = 1; b < size; b++)
                    {
                        bool cur = pass == 0 ? m[b, a] : m[a, b];
                        bool prev = pass == 0 ? m[b - 1, a] : m[a, b - 1];
                        if (cur == prev) run++;
                        else { if (run >= 5) result += run - 2; run = 1; }
                    }
                    if (run >= 5) result += run - 2;
                }
            }

            // Rule 2: 2x2 blocks of one colour.
            for (int y = 0; y < size - 1; y++)
                for (int x = 0; x < size - 1; x++)
                {
                    bool c = m[x, y];
                    if (c == m[x + 1, y] && c == m[x, y + 1] && c == m[x + 1, y + 1]) result += 3;
                }

            // Rule 3: finder-like 1:1:3:1:1 patterns with 4 light modules on one side.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int a = 0; a < size; a++)
                {
                    for (int b = 0; b + 10 < size; b++)
                    {
                        int pattern = 0;
                        for (int k = 0; k < 11; k++)
                        {
                            bool dark = pass == 0 ? m[b + k, a] : m[a, b + k];
                            pattern = (pattern << 1) | (dark ? 1 : 0);
                        }
                        if (pattern == 0b10111010000 || pattern == 0b00001011101) result += 40;
                    }
                }
            }

            // Rule 4: overall dark/light balance.
            int darkCount = 0;
            foreach (bool c in m) if (c) darkCount++;
            int total = size * size;
            int k4 = (Math.Abs(darkCount * 20 - total * 10) + total - 1) / total - 1;
            result += Math.Max(0, k4) * 10;
            return result;
        }

        // ---------- Helpers ----------

        static void Set(bool[,] m, bool[,] f, int x, int y, bool dark)
        {
            m[x, y] = dark;
            f[x, y] = true;
        }

        static bool Bit(int value, int index) => ((value >> index) & 1) != 0;

        sealed class BitBuffer
        {
            readonly System.Collections.Generic.List<bool> m_Bits = new System.Collections.Generic.List<bool>();
            public int Length => m_Bits.Count;

            public void Append(int value, int count)
            {
                for (int i = count - 1; i >= 0; i--) m_Bits.Add(((value >> i) & 1) != 0);
            }

            public byte[] ToBytes()
            {
                var bytes = new byte[m_Bits.Count / 8];
                for (int i = 0; i < m_Bits.Count; i++)
                    if (m_Bits[i]) bytes[i >> 3] |= (byte)(0x80 >> (i & 7));
                return bytes;
            }
        }
    }
}

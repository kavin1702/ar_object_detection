using System;

namespace CADInspection.CadQr
{
    /// <summary>
    /// Paints a square "marker card" as a greyscale pixel buffer (0 = black, 255 = white).
    ///
    /// Layout (outside to inside):
    ///   1. A thick black frame (clear edge for the tracker).
    ///   2. A ring of random shapes. The random seed comes from the model id, so every model
    ///      gets its own pattern and the same id always gives the same pattern.
    ///      ARCore needs this texture: a bare QR code is a poor tracking image because its
    ///      squares repeat (see Google's Augmented Images guidelines).
    ///   3. A black triangle in the top border that shows which way is "up".
    ///   4. The QR code itself, centred, with its white quiet zone. Any phone QR scanner can
    ///      read it and see which CAD model the card belongs to.
    ///
    /// This class has no Unity dependency, so it can be unit-tested outside Unity.
    /// <see cref="QrTextureFactory"/> turns the buffer into a Texture2D.
    /// </summary>
    public static class MarkerCardPainter
    {
        /// <summary>Returns size*size bytes. Row 0 is the TOP row of the card.</summary>
        public static byte[] Paint(string qrPayload, string seedText, int size = 1024)
        {
            if (size < 300) throw new ArgumentOutOfRangeException(nameof(size), "ARCore needs at least 300 x 300 px.");

            var px = new byte[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = 255;

            int frame = Math.Max(4, size * 3 / 100);          // 3% black frame
            FillRect(px, size, 0, 0, size, frame, 0);
            FillRect(px, size, 0, size - frame, size, frame, 0);
            FillRect(px, size, 0, 0, frame, size, 0);
            FillRect(px, size, size - frame, 0, frame, size, 0);

            // QR block in the centre: 52% of the card, including a 4-module quiet zone.
            bool[,] qr = QrEncoder.Encode(qrPayload);
            int modules = qr.GetLength(0) + 8;
            int module = Math.Max(1, size * 52 / 100 / modules);
            int qrBlock = module * modules;
            int qrX = (size - qrBlock) / 2;
            int qrY = (size - qrBlock) / 2;

            // Random shapes in the ring between the frame and the QR block.
            var rng = new Random(StableHash(seedText));
            int inner = frame + size / 100;
            int keepOutPad = size / 50;
            for (int n = 0; n < 500; n++)
            {
                int w = rng.Next(size / 40, size / 9);
                int h = rng.Next(size / 40, size / 9);
                int x = rng.Next(inner, size - inner - w);
                int y = rng.Next(inner, size - inner - h);
                if (Overlaps(x, y, w, h, qrX - keepOutPad, qrY - keepOutPad, qrBlock + 2 * keepOutPad, qrBlock + 2 * keepOutPad))
                    continue;
                if (Overlaps(x, y, w, h, size / 2 - size / 12, frame, size / 6, size / 10)) // keep the "up" arrow clear
                    continue;
                byte shade = (byte)rng.Next(0, 200);
                if (rng.Next(3) == 0) FillCircle(px, size, x + w / 2, y + h / 2, Math.Min(w, h) / 2, shade);
                else FillRect(px, size, x, y, w, h, shade);
            }

            // "Up" arrow: a black triangle pointing at the top edge.
            int triH = size / 14;
            int cx = size / 2;
            int tipY = frame + size / 80;
            for (int row = 0; row < triH; row++)
            {
                int half = row * (size / 12) / triH;
                FillRect(px, size, cx - half, tipY + row, 2 * half + 1, 1, 0);
            }

            // The QR block (white quiet zone first, then the dark modules).
            FillRect(px, size, qrX, qrY, qrBlock, qrBlock, 255);
            int count = qr.GetLength(0);
            for (int my = 0; my < count; my++)
                for (int mx = 0; mx < count; mx++)
                    if (qr[mx, my])
                        FillRect(px, size, qrX + (mx + 4) * module, qrY + (my + 4) * module, module, module, 0);

            return px;
        }

        /// <summary>FNV-1a hash. Unlike string.GetHashCode it is the same on every device and run.</summary>
        public static int StableHash(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in text ?? string.Empty)
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return (int)(hash & 0x7FFFFFFF);
            }
        }

        static bool Overlaps(int x, int y, int w, int h, int ox, int oy, int ow, int oh)
            => x < ox + ow && x + w > ox && y < oy + oh && y + h > oy;

        static void FillRect(byte[] px, int size, int x, int y, int w, int h, byte value)
        {
            int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
            int x1 = Math.Min(size, x + w), y1 = Math.Min(size, y + h);
            for (int yy = y0; yy < y1; yy++)
                for (int xx = x0; xx < x1; xx++)
                    px[yy * size + xx] = value;
        }

        static void FillCircle(byte[] px, int size, int cx, int cy, int r, byte value)
        {
            for (int yy = Math.Max(0, cy - r); yy < Math.Min(size, cy + r + 1); yy++)
                for (int xx = Math.Max(0, cx - r); xx < Math.Min(size, cx + r + 1); xx++)
                    if ((xx - cx) * (xx - cx) + (yy - cy) * (yy - cy) <= r * r)
                        px[yy * size + xx] = value;
        }
    }
}

using UnityEngine;

namespace CADInspection.CadQr
{
    /// <summary>
    /// Turns the greyscale buffer from <see cref="MarkerCardPainter"/> into a Unity Texture2D.
    /// The texture is CPU-readable (needed by ARCore when it is added to the image library)
    /// and can be saved as a PNG for printing.
    /// </summary>
    public static class QrTextureFactory
    {
        public const string PayloadPrefix = "CADQR|v1|";

        /// <summary>The text stored inside the QR code for a model id.</summary>
        public static string PayloadFor(string modelId) => PayloadPrefix + modelId;

        public static Texture2D CreateMarkerCard(string modelId, int size = 1024)
        {
            byte[] grey = MarkerCardPainter.Paint(PayloadFor(modelId), modelId, size);

            // Unity textures start at the BOTTOM row, the painter starts at the TOP row: flip.
            var colors = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                int src = (size - 1 - y) * size;
                int dst = y * size;
                for (int x = 0; x < size; x++)
                {
                    byte g = grey[src + x];
                    colors[dst + x] = new Color32(g, g, g, 255);
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "QR_" + modelId,
                filterMode = FilterMode.Point, // keep the QR squares sharp on screen
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(colors);
            texture.Apply(false, false); // false = keep it readable on the CPU
            return texture;
        }
    }
}

using SkiaSharp;

namespace MercuryManager.Server;

public static class PerlinNoiseGradientGenerator
{
    private static readonly byte[] Table = new byte[]
    {
        0xff, 0xff, 0x88, 0x00, 0xff, 0x88, 0xff, 0x00, 0x88, 0xff, 0xff, 0x00, 0x00, 0xff, 0x88, 0x00,
        0x00, 0x88, 0xff, 0x00, 0x88, 0x00, 0xff, 0x00, 0xff, 0x00, 0x88, 0x00, 0xff, 0x88, 0x00, 0x00,
        0x88, 0xff, 0x00, 0x00, 0x00, 0x00, 0x88, 0x00, 0x00, 0x88, 0x00, 0x00, 0x88, 0x00, 0x00, 0x00
    };

    private static byte[]? cachedPng;
    private static readonly object LockObj = new();

    public static byte[] GeneratePng()
    {
        lock (LockObj)
        {
            if (cachedPng != null) return cachedPng;

            const int width = 128;
            const int height = 128;
            var pixels = new byte[width * height * 4];

            uint seed = 12345;
            for (int i = 0; i < width * height; i++)
            {
                seed = unchecked(196314165u * seed + 907633515u);
                uint bits = (seed & 0x7FFFFFu) | 0x3F800000u;
                float val = BitConverter.UInt32BitsToSingle(bits) - 1.0f;
                int entry = (int)(val * 12.0f);
                if (entry < 0) entry = 0;
                else if (entry >= 12) entry = 11;

                int tableOffset = entry * 4;
                pixels[i * 4 + 0] = Table[tableOffset + 0]; // R
                pixels[i * 4 + 1] = Table[tableOffset + 1]; // G
                pixels[i * 4 + 2] = Table[tableOffset + 2]; // B
                pixels[i * 4 + 3] = Table[tableOffset + 3]; // A
            }

            using var bitmap = new SKBitmap();
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            var gcHandle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                bitmap.InstallPixels(info, gcHandle.AddrOfPinnedObject());
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                cachedPng = data.ToArray();
                return cachedPng;
            }
            finally
            {
                gcHandle.Free();
            }
        }
    }
}

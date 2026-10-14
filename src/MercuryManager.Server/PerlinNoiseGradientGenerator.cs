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

    // Native PF_B8G8R8A8, linear, one mip. Upload the returned RGBA bytes
    // directly; HTML image/canvas paths can discard RGB when alpha is zero.
    public static byte[] GenerateRgba()
    {
        var pixels = new byte[128 * 128 * 4];
        uint seed = 12345;
        for (var i = 0; i < 128 * 128; i++)
        {
            seed = unchecked(196314165u * seed + 907633515u);
            var value = BitConverter.UInt32BitsToSingle((seed & 0x7FFFFFu) | 0x3F800000u) - 1.0f;
            var offset = (int)(value * 12.0f) * 4;
            pixels[i * 4] = Table[offset + 2];
            pixels[i * 4 + 1] = Table[offset + 1];
            pixels[i * 4 + 2] = Table[offset];
            pixels[i * 4 + 3] = Table[offset + 3];
        }
        return pixels;
    }

    private static byte[]? cachedPng;
    private static readonly object LockObj = new();

    public static byte[] GeneratePng()
    {
        lock (LockObj)
        {
            if (cachedPng != null) return cachedPng;

            const int width = 128;
            const int height = 128;
            var pixels = GenerateRgba();

            using var bitmap = new SKBitmap();
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
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

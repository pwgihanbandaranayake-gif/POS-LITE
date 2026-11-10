using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PosSystem.Printing;

/// <summary>
/// Shop logo: loaded from a file in Settings, stored as a base64 PNG in AppSettings ("Shop.Logo"),
/// printed at the top of the bill.
/// </summary>
public static class LogoHelper
{
    /// <summary>Reads an image file, shrinks it to maxWidthPx, puts it on a white background and returns base64 PNG.</summary>
    public static string LoadFromFile(string path, int maxWidthPx = 400)
    {
        if (new FileInfo(path).Length > 5 * 1024 * 1024) throw new AppException("Logo file is too big. Use an image under 5 MB.");

        var original = Load(File.ReadAllBytes(path), 0);
        var decodeWidth = original.PixelWidth > maxWidthPx ? maxWidthPx : 0;
        var img = decodeWidth > 0 ? Load(File.ReadAllBytes(path), decodeWidth) : original;

        // flatten transparency onto white (thermal printers print transparent areas as black)
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, img.PixelWidth, img.PixelHeight));
            dc.DrawImage(img, new Rect(0, 0, img.PixelWidth, img.PixelHeight));
        }
        var rtb = new RenderTargetBitmap(img.PixelWidth, img.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>base64 PNG -> image for WPF (null when empty or invalid).</summary>
    public static BitmapSource? ToBitmap(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;
        try { return Load(Convert.FromBase64String(base64), 0); }
        catch { return null; }
    }

    private static BitmapImage Load(byte[] bytes, int decodeWidth)
    {
        var bmp = new BitmapImage();
        using var ms = new MemoryStream(bytes);
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>
    /// ESC/POS raster image (GS v 0), centred, for the Direct text / ESC-POS print method.
    /// widthDots: about 8 dots per mm on a 203 dpi printer.
    /// </summary>
    public static byte[] EscPos(string base64, int widthDots)
    {
        var src = ToBitmap(base64);
        if (src == null) return Array.Empty<byte>();

        double scale = Math.Min(1.0, (double)widthDots / src.PixelWidth);
        BitmapSource bmp = scale < 1 ? new TransformedBitmap(src, new ScaleTransform(scale, scale)) : src;
        var gray = new FormatConvertedBitmap(bmp, PixelFormats.Gray8, null, 0);

        int w = gray.PixelWidth, h = gray.PixelHeight;
        var pixels = new byte[w * h];
        gray.CopyPixels(pixels, w, 0);

        int bytesPerRow = (w + 7) / 8;
        var data = new byte[bytesPerRow * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (pixels[y * w + x] < 128)                       // dark pixel -> print dot
                    data[y * bytesPerRow + x / 8] |= (byte)(0x80 >> (x % 8));

        var b = new List<byte>();
        b.AddRange(new byte[] { 0x1B, 0x61, 0x01 });                                       // centre
        b.AddRange(new byte[] { 0x1D, 0x76, 0x30, 0x00,
            (byte)(bytesPerRow & 0xFF), (byte)(bytesPerRow >> 8), (byte)(h & 0xFF), (byte)(h >> 8) });
        b.AddRange(data);
        b.AddRange(new byte[] { 0x0A, 0x1B, 0x61, 0x00 });
        return b.ToArray();
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PosSystem.Printing;

/// <summary>
/// Code 128 (set B) barcode for the invoice number at the bottom of the bill.
/// Drawn with WPF rectangles for the Windows-driver mode, or sent as a GS k command for ESC/POS mode.
/// </summary>
public static class Barcode128
{
    // bar/space widths for symbols 0..106 (106 = stop)
    private static readonly string[] Patterns =
    {
        "212222","222122","222221","121223","121322","131222","122213","122312","132212","221213",
        "221312","231212","112232","122132","122231","113222","123122","123221","223211","221132",
        "221231","213212","223112","312131","311222","321122","321221","312212","322112","322211",
        "212123","212321","232121","111323","131123","131321","112313","132113","132311","211313",
        "231113","231311","112133","112331","132131","113123","113321","133121","313121","211331",
        "231131","213113","213311","213131","311123","311321","331121","312113","312311","332111",
        "314111","221411","431111","111224","111422","121124","121421","141122","141221","112214",
        "112412","122114","122411","142112","142211","241211","221114","413111","241112","134111",
        "111242","121142","121241","114212","124112","124211","411212","421112","421211","212141",
        "214121","412121","111143","111341","131141","114113","114311","411113","411311","113141",
        "114131","311141","411131","211412","211214","211232","2331112"
    };

    private const int StartB = 104, Stop = 106;

    /// <summary>Only printable ASCII is allowed in set B.</summary>
    public static string Clean(string text) => new(text.Where(c => c >= 32 && c <= 126).ToArray());

    /// <summary>Module widths: bar, space, bar, space ... (start, data, checksum, stop).</summary>
    public static List<int> Encode(string text)
    {
        text = Clean(text);
        var codes = new List<int> { StartB };
        int checksum = StartB;
        for (int i = 0; i < text.Length; i++)
        {
            int v = text[i] - 32;
            codes.Add(v);
            checksum += v * (i + 1);
        }
        codes.Add(checksum % 103);
        codes.Add(Stop);
        return codes.SelectMany(c => Patterns[c].Select(ch => ch - '0')).ToList();
    }

    /// <summary>Barcode as a WPF element that fits inside maxWidth.</summary>
    public static FrameworkElement CreateVisual(string text, double maxWidth, double height)
    {
        var widths = Encode(text);
        int modules = widths.Sum() + 20;                       // + quiet zone (10 each side)
        double m = Math.Min(2.0, maxWidth / modules);           // module width in DIP
        var canvas = new Canvas { Width = modules * m, Height = height, Background = Brushes.White, SnapsToDevicePixels = true };
        double x = 10 * m;
        for (int i = 0; i < widths.Count; i++)
        {
            double w = widths[i] * m;
            if (i % 2 == 0) // even index = bar
            {
                var r = new Rectangle { Width = w, Height = height, Fill = Brushes.Black };
                Canvas.SetLeft(r, x);
                canvas.Children.Add(r);
            }
            x += w;
        }
        return canvas;
    }

    /// <summary>ESC/POS: centred CODE128 barcode, no text under it.</summary>
    public static byte[] EscPos(string text)
    {
        var data = System.Text.Encoding.ASCII.GetBytes("{B" + Clean(text));
        var b = new List<byte>();
        b.AddRange(new byte[] { 0x1B, 0x61, 0x01 });                 // centre
        b.AddRange(new byte[] { 0x1D, 0x68, 60 });                   // height 60 dots
        b.AddRange(new byte[] { 0x1D, 0x77, 2 });                    // module width 2
        b.AddRange(new byte[] { 0x1D, 0x48, 0 });                    // no human readable text
        b.AddRange(new byte[] { 0x1D, 0x6B, 73, (byte)data.Length }); // GS k m=73 (CODE128)
        b.AddRange(data);
        b.AddRange(new byte[] { 0x0A, 0x1B, 0x61, 0x00 });           // new line, left align
        return b.ToArray();
    }
}

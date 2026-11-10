using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Xps;

namespace PosSystem.Printing;

/// <summary>One sticker to print.</summary>
public sealed class LabelData
{
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Weight { get; set; }
    public decimal Price { get; set; }
    public string? PriceUnit { get; set; }     // e.g. "kg" for loose items -> "Rs. 260.00 / kg"
    public DateTime? MfdDate { get; set; }
    public DateTime? ExpDate { get; set; }
}

/// <summary>
/// Barcode stickers. Default sticker = 2 x 1.5 inch landscape.
/// A page holds Columns x Rows stickers; the page size is calculated from the sticker size,
/// margins and gaps, so label-roll printers and A4 sticker sheets both work.
///
///   ┌──────────────────────────────┐
///   │        MY SUPERMARKET        │  shop name (optional)
///   │  CEMENT 50KG                 │  description (2 lines max)
///   │  Wt: 50kg        Rs.1,950.00 │  weight + unit price
///   │  ║║│║║│║│║║│║║│║║│║│║║│║║   │  barcode = item code
///   │            2050              │  code number
///   └──────────────────────────────┘
/// </summary>
public static class LabelPrinter
{
    public static double InchToDip(decimal inch) => (double)inch * 96.0;

    /// <summary>Sticker width / height in DIP, after applying landscape / portrait.</summary>
    public static (double W, double H) LabelSize(PosSettings s)
    {
        double a = InchToDip(s.LabelWidthIn), b = InchToDip(s.LabelHeightIn);
        double longSide = Math.Max(a, b), shortSide = Math.Min(a, b);
        return s.LabelLandscape ? (longSide, shortSide) : (shortSide, longSide);
    }

    public static (double W, double H) PageSize(PosSettings s)
    {
        var (w, h) = LabelSize(s);
        int cols = Math.Max(1, s.LabelColumns), rows = Math.Max(1, s.LabelRows);
        double pw = Mm.ToDip((double)s.LabelMarginLeftMm) * 2 + cols * w + (cols - 1) * Mm.ToDip((double)s.LabelGapHMm);
        double ph = Mm.ToDip((double)s.LabelMarginTopMm) * 2 + rows * h + (rows - 1) * Mm.ToDip((double)s.LabelGapVMm);
        return (pw, ph);
    }

    /// <summary>Builds all pages. skip = number of sticker positions already used on the first sheet.</summary>
    public static FixedDocument BuildDocument(IReadOnlyList<LabelData> labels, PosSettings s, string shopName, string currency, int skip = 0)
    {
        var (lw, lh) = LabelSize(s);
        var (pw, ph) = PageSize(s);
        int cols = Math.Max(1, s.LabelColumns), rows = Math.Max(1, s.LabelRows), perPage = cols * rows;
        double gapH = Mm.ToDip((double)s.LabelGapHMm), gapV = Mm.ToDip((double)s.LabelGapVMm);
        double left = Mm.ToDip((double)s.LabelMarginLeftMm), top = Mm.ToDip((double)s.LabelMarginTopMm);

        var slots = new List<LabelData?>();
        for (int i = 0; i < Math.Max(0, skip); i++) slots.Add(null);
        slots.AddRange(labels);

        var doc = new FixedDocument();
        double outW = s.LabelRotate ? ph : pw, outH = s.LabelRotate ? pw : ph;
        doc.DocumentPaginator.PageSize = new Size(outW, outH);

        for (int start = 0; start < slots.Count; start += perPage)
        {
            var sheet = new Canvas { Width = pw, Height = ph, Background = Brushes.White };
            for (int k = 0; k < perPage && start + k < slots.Count; k++)
            {
                var data = slots[start + k];
                if (data == null) continue;
                int r = k / cols, c = k % cols;
                var label = BuildLabel(data, lw, lh, s, shopName, currency);
                Canvas.SetLeft(label, left + c * (lw + gapH));
                Canvas.SetTop(label, top + r * (lh + gapV));
                sheet.Children.Add(label);
            }

            var page = new FixedPage { Width = outW, Height = outH, Background = Brushes.White };
            if (s.LabelRotate)
            {
                sheet.RenderTransform = new TransformGroup
                {
                    Children = { new RotateTransform(90), new TranslateTransform(ph, 0) }
                };
            }
            page.Children.Add(sheet);
            var content = new PageContent();
            ((System.Windows.Markup.IAddChild)content).AddChild(page);
            doc.Pages.Add(content);
        }
        return doc;
    }

    /// <summary>One sticker.</summary>
    public static FrameworkElement BuildLabel(LabelData d, double w, double h, PosSettings s, string shopName, string currency)
    {
        double pad = Mm.ToDip(1.5);
        double fs = Math.Clamp(h / 13.5, 7, 13);                 // 1.5" tall -> about 10.7
        var font = new FontFamily("Arial");
        TextBlock T(string text, double size, bool bold = false, TextAlignment a = TextAlignment.Left) => new()
        {
            Text = text, FontFamily = font, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            TextAlignment = a, Foreground = Brushes.Black, TextTrimming = TextTrimming.CharacterEllipsis
        };

        var grid = new Grid { Width = w, Height = h, Background = Brushes.White, ClipToBounds = true };
        var stack = new DockPanel { Margin = new Thickness(pad), LastChildFill = true };
        grid.Children.Add(stack);

        if (s.LabelShowShopName && !string.IsNullOrWhiteSpace(shopName))
        {
            var shop = T(shopName, fs * 0.85, true, TextAlignment.Center);
            DockPanel.SetDock(shop, Dock.Top);
            stack.Children.Add(shop);
        }

        var name = T(d.Description, fs, true);
        name.TextWrapping = TextWrapping.Wrap;
        name.MaxHeight = fs * 2.6;                                   // 2 lines max
        DockPanel.SetDock(name, Dock.Top);
        stack.Children.Add(name);

        // weight (left) | price (right)
        if (s.LabelShowWeight || s.LabelShowPrice)
        {
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
            if (s.LabelShowPrice)
            {
                var price = T($"{currency} {d.Price:N2}" + (string.IsNullOrEmpty(d.PriceUnit) ? "" : " / " + d.PriceUnit), fs * 1.15, true, TextAlignment.Right);
                DockPanel.SetDock(price, Dock.Right);
                row.Children.Add(price);
            }
            if (s.LabelShowWeight && !string.IsNullOrWhiteSpace(d.Weight))
                row.Children.Add(T("Wt: " + d.Weight, fs * 0.95));
            DockPanel.SetDock(row, Dock.Top);
            stack.Children.Add(row);
        }

        if (s.LabelShowDates && (d.MfdDate.HasValue || d.ExpDate.HasValue))
        {
            var dates = T((d.MfdDate.HasValue ? "MFD " + d.MfdDate.Value.ToString("dd/MM/yy") + "   " : "")
                          + (d.ExpDate.HasValue ? "EXP " + d.ExpDate.Value.ToString("dd/MM/yy") : ""), fs * 0.8);
            DockPanel.SetDock(dates, Dock.Bottom);
            stack.Children.Add(dates);
        }

        if (s.LabelShowCode)
        {
            var code = T(d.Code, fs * 0.95, true, TextAlignment.Center);
            DockPanel.SetDock(code, Dock.Bottom);
            stack.Children.Add(code);
        }

        // barcode fills the remaining space
        var holder = new Grid();
        double barH = Math.Max(Mm.ToDip(5), h * 0.30);
        var bc = Barcode128.CreateVisual(d.Code, w - pad * 2, barH);
        bc.HorizontalAlignment = HorizontalAlignment.Center;
        bc.VerticalAlignment = VerticalAlignment.Bottom;
        holder.Children.Add(bc);
        stack.Children.Add(holder);

        return grid;
    }

    public static void Preview(IReadOnlyList<LabelData> labels, PosSettings s, string shopName, string currency, int skip)
    {
        var doc = BuildDocument(labels, s, shopName, currency, skip);
        new Window
        {
            Title = Lang.T("Sticker preview"), Width = 900, Height = 700, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive),
            Content = new DocumentViewer { Document = doc }
        }.ShowDialog();
    }

    public static void Print(IReadOnlyList<LabelData> labels, PosSettings s, string shopName, string currency, int skip)
    {
        var doc = BuildDocument(labels, s, shopName, currency, skip);
        var queue = ReceiptPrinter.GetQueue(string.IsNullOrWhiteSpace(s.LabelPrinter) ? null : s.LabelPrinter);
        var ticket = queue.DefaultPrintTicket?.Clone() ?? new PrintTicket();
        var size = doc.DocumentPaginator.PageSize;
        ticket.PageMediaSize = new PageMediaSize(size.Width, size.Height);
        ticket.PageOrientation = PageOrientation.Portrait;   // page size already has the right shape
        XpsDocumentWriter writer = PrintQueue.CreateXpsDocumentWriter(queue);
        writer.Write(doc, ticket);
    }
}

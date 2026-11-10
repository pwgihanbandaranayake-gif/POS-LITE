using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Xps;

namespace PosSystem.Printing;

/// <summary>
/// Prints bills on the printer set in Settings > This till and printers.
///   Thermal / dot-matrix + "Windows driver"        -> RollReceipt (WPF, logo, barcode, any font)
///   Thermal / dot-matrix + "Direct text / ESC-POS" -> TextReceipt (raw, fastest; logo + barcode on thermal)
///   A5 half sheet                                  -> A5Invoice
/// </summary>
public static class ReceiptPrinter
{
    /// <summary>Blank paper after the bill so the cutter does not cut through the footer (XP-80T needs ~18 mm).</summary>
    public const double BottomFeedMm = 18;

    public static IReadOnlyList<string> GetInstalledPrinters()
    {
        try
        {
            using var server = new LocalPrintServer();
            return server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections })
                         .Select(q => q.FullName).OrderBy(n => n).ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    public static void Print(ReceiptData r, Terminal t, ReceiptFormat? formatOverride = null)
    {
        var format = formatOverride ?? (ReceiptFormat)t.ReceiptFormat;
        OnUiThread(() =>
        {
            if (format == ReceiptFormat.A5HalfSheet)
            {
                var printer = string.IsNullOrWhiteSpace(t.A5Printer) ? t.ReceiptPrinter : t.A5Printer;
                PrintDocument(A5Invoice.Build(r, t.A5Landscape), printer, t.A5Landscape);
            }
            else if ((PrintEngine)t.PrintEngine == PrintEngine.RawText)
            {
                var lines = TextReceipt.Build(r, t.CharsPerLine);
                var bytes = TextReceipt.ToBytes(lines, format == ReceiptFormat.Thermal, r.LogoBase64, (int)(t.PaperWidthMm * 8));
                RawPrinter.Send(PrinterNameOrDefault(t.ReceiptPrinter), bytes, r.DocumentNo);
            }
            else
            {
                PrintRoll(RollReceipt.Build(r, (double)t.PaperWidthMm), t.ReceiptPrinter, r.DocumentNo);
            }
        });
    }

    public static void Preview(ReceiptData r, Terminal t, ReceiptFormat? formatOverride = null)
    {
        var format = formatOverride ?? (ReceiptFormat)t.ReceiptFormat;
        OnUiThread(() =>
        {
            FrameworkElement content;
            if (format == ReceiptFormat.A5HalfSheet)
                content = new FlowDocumentPageViewer { Document = A5Invoice.Build(r, t.A5Landscape) };
            else if ((PrintEngine)t.PrintEngine == PrintEngine.RawText)
                content = PaperPreview(new TextBlock
                {
                    Text = TextReceipt.ToPreviewText(TextReceipt.Build(r, t.CharsPerLine), t.CharsPerLine),
                    FontFamily = new FontFamily("Consolas"), FontSize = 12
                });
            else
                content = PaperPreview(RollReceipt.Build(r, (double)t.PaperWidthMm));

            new Window
            {
                Title = "Print preview - " + r.DocumentNo, Width = format == ReceiptFormat.A5HalfSheet ? 1000 : 480, Height = 820,
                Background = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)), Content = content,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = System.Windows.Application.Current?.MainWindow
            }.ShowDialog();
        });
    }

    public static void OpenCashDrawer(Terminal t)
    {
        if (!t.OpenCashDrawer || string.IsNullOrWhiteSpace(t.ReceiptPrinter)) return;
        try { RawPrinter.Send(t.ReceiptPrinter!, TextReceipt.DrawerKick, "Drawer"); }
        catch { /* never stop billing because the drawer is not connected */ }
    }

    // --------------------------------------------------------------------------

    private static ScrollViewer PaperPreview(UIElement paper) => new()
    {
        Content = new Border { Background = Brushes.White, Padding = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Center, Child = paper }
    };

    /// <summary>Roll paper: the page length is set to the receipt length so the printer does not feed blank paper.</summary>
    private static void PrintRoll(FrameworkElement receipt, string? printerName, string jobName)
    {
        receipt.Measure(new Size(receipt.Width, double.PositiveInfinity));
        receipt.Arrange(new Rect(0, 0, receipt.Width, receipt.DesiredSize.Height));
        receipt.UpdateLayout();

        var queue = GetQueue(printerName);
        var dlg = new PrintDialog { PrintQueue = queue };
        var ticket = queue.DefaultPrintTicket?.Clone() ?? new PrintTicket();
        ticket.PageMediaSize = new PageMediaSize(receipt.Width, receipt.ActualHeight + Mm.ToDip(4));
        dlg.PrintTicket = ticket;
        dlg.PrintVisual(receipt, jobName);
    }

    private static void PrintDocument(FlowDocument doc, string? printerName, bool landscape)
    {
        var queue = GetQueue(printerName);
        var ticket = queue.DefaultPrintTicket?.Clone() ?? new PrintTicket();
        ticket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA5);
        ticket.PageOrientation = landscape ? PageOrientation.Landscape : PageOrientation.Portrait;
        XpsDocumentWriter writer = PrintQueue.CreateXpsDocumentWriter(queue);
        writer.Write(((IDocumentPaginatorSource)doc).DocumentPaginator, ticket);
    }

    public static PrintQueue GetQueue(string? printerName)
    {
        var server = new LocalPrintServer();
        if (string.IsNullOrWhiteSpace(printerName)) return server.DefaultPrintQueue;
        var q = server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections })
                      .FirstOrDefault(p => string.Equals(p.FullName, printerName, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(p.Name, printerName, StringComparison.OrdinalIgnoreCase));
        return q ?? throw new AppException($"Printer '{printerName}' is not installed on this computer. Check Settings > This till and printers.");
    }

    private static string PrinterNameOrDefault(string? name) =>
        string.IsNullOrWhiteSpace(name) ? new LocalPrintServer().DefaultPrintQueue.FullName : name!;

    private static void OnUiThread(Action a)
    {
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess()) a(); else d.Invoke(a);
    }
}

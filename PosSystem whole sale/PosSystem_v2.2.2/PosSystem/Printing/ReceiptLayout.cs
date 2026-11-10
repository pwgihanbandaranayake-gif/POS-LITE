using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PosSystem.Printing;

public static class Mm
{
    /// <summary>millimetres -> WPF units (1/96 inch)</summary>
    public static double ToDip(double mm) => mm * 96.0 / 25.4;
}

/// <summary>
/// Roll receipt (thermal / dot-matrix with Windows driver), laid out like the shop bill:
///
///               [ LOGO ]
///        INDUNIL HARDWARE AND MOTORS
///             address lines
///         Mob : 0717857772 Tel : ...
///   _________________________________
///   Invoice No..            MAIN-0001
///   Cashier:                   NIMAL
///   Date:       16/10/2026 01:11:18 PM
///   ██ PRODUCT   PRICE   DISC  AMOUNT ██
///   W/B FLOOR PAINT OPEL 500ML
///   1.000      1000.00  100.00  900.00
///   Net Total                3,575.00
///   Paid Amount              5,000.00
///   Change                   1,425.00
///               You Saved
///               Rs.375.00
///   - - - - - - - - - - - - - - - - -
///   Items Count                     3
///             |||||||||||||
///              Thank You !
/// </summary>
public static class RollReceipt
{
    public static FrameworkElement Build(ReceiptData r, double widthMm)
    {
        double width = Mm.ToDip(widthMm);
        double fs = widthMm >= 70 ? 12.5 : widthMm >= 60 ? 11.5 : 10.5;   // base font size by paper width
        var font = new FontFamily("Arial, Iskoola Pota, Nirmala UI");   // Sinhala / Tamil fallback for item names
        string M(decimal v) => v.ToString("0.00");
        string N(decimal v) => v.ToString("N2");

        var root = new StackPanel { Width = width, Background = Brushes.White };
        void Add(UIElement e) => root.Children.Add(e);

        TextBlock Text(string s, double size, bool bold = false, TextAlignment align = TextAlignment.Left, Brush? color = null) => new()
        {
            Text = s, FontSize = size, FontFamily = font, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            TextAlignment = align, TextWrapping = TextWrapping.Wrap, Foreground = color ?? Brushes.Black
        };
        void Center(string s, double size, bool bold = false) => Add(Text(s, size, bold, TextAlignment.Center));
        void SolidLine() => Add(new Border { BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 4, 0, 4) });
        void DashLine() => Add(new Line { X1 = 0, X2 = width, Stroke = Brushes.Black, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 }, Margin = new Thickness(0, 5, 0, 5) });
        void Row(string left, string right, bool bold = false, double? size = null)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = Text(left, size ?? fs, bold);
            var rt = Text(right, size ?? fs, bold, TextAlignment.Right);
            Grid.SetColumn(rt, 1);
            g.Children.Add(l); g.Children.Add(rt);
            Add(g);
        }
        // 4 columns used by both the black header bar and the item rows, so they always line up
        Grid Columns()
        {
            var g = new Grid();
            foreach (var star in new[] { 1.15, 1.1, 0.95, 1.15 })
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(star, GridUnitType.Star) });
            return g;
        }
        Grid ItemRow(string a, string b, string c, string d, double size, bool bold, Brush color)
        {
            var g = Columns();
            var cells = new[] { a, b, c, d };
            for (int i = 0; i < 4; i++)
            {
                var t = Text(cells[i], size, bold, i == 0 ? TextAlignment.Left : TextAlignment.Right, color);
                t.TextWrapping = TextWrapping.NoWrap;
                Grid.SetColumn(t, i);
                g.Children.Add(t);
            }
            return g;
        }

        // ---------- logo + shop details ----------
        var logo = LogoHelper.ToBitmap(r.LogoBase64);
        if (logo != null)
            Add(new Image { Source = logo, MaxWidth = width * 0.7, MaxHeight = Mm.ToDip(26), Stretch = Stretch.Uniform,
                            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) });
        Center(r.ShopName, fs + 5, true);
        foreach (var h in r.HeaderLines) Center(h, fs);
        if (!string.IsNullOrEmpty(r.TaxRegNo)) Center("VAT Reg No : " + r.TaxRegNo, fs - 1);
        if (!string.IsNullOrEmpty(r.HeaderNote)) Center(r.HeaderNote!, fs - 1);
        SolidLine();

        // ---------- document info ----------
        if (r.Title != "INVOICE") Center(r.Title, fs + 2, true);
        if (r.IsReprint) Center("*** DUPLICATE COPY ***", fs, true);
        Row(r.Title == "INVOICE" ? "Invoice No.." : "No..", r.DocumentNo, true);
        Row("Cashier:", r.Cashier);
        Row("Date:", r.Date.ToString("dd/MM/yyyy hh:mm:ss tt"));
        if (!string.IsNullOrEmpty(r.Customer)) Row("Customer:", r.Customer!);
        if (!string.IsNullOrEmpty(r.ReferenceNo)) Row("Ref:", r.ReferenceNo!);

        // ---------- items ----------
        if (r.Lines.Count > 0)
        {
            var bar = new Border { Background = Brushes.Black, Padding = new Thickness(3, 3, 3, 3), Margin = new Thickness(0, 4, 0, 4) };
            bar.Child = ItemRow("PRODUCT", "PRICE", "DISC", "AMOUNT", fs - 1, true, Brushes.White);
            Add(bar);

            foreach (var l in r.Lines)
            {
                var name = Text(l.Name.ToUpperInvariant(), fs);
                name.Margin = new Thickness(0, 3, 0, 0);
                Add(name);
                Add(ItemRow(l.Qty.ToString("0.000"), M(l.Price), M(l.Discount), M(l.Amount), fs, false, Brushes.Black));
            }
            SolidLine();

            // ---------- totals ----------
            if (r.BillDiscount != 0 || r.Tax != 0 || r.RoundOff != 0)
            {
                Row("Sub Total", N(r.GrossTotal - r.LineDiscount));
                if (r.BillDiscount != 0) Row(r.BillDiscountLabel ?? "Bill Discount", "-" + N(r.BillDiscount));
                if (r.Tax != 0) Row(r.TaxInclusive ? "Tax (included)" : "Tax", N(r.Tax));
                if (r.RoundOff != 0) Row("Round Off", N(r.RoundOff));
            }
            Row("Net Total", N(r.NetTotal), true, fs + 2);
            Row("Paid Amount", N(r.PaidAmount), true, fs + 2);
            if (r.Payments.Count > 1 || r.Payments.Any(p => p.Name != "Cash"))
                foreach (var p in r.Payments)
                    Row("    " + p.Name + (string.IsNullOrEmpty(p.Ref) ? "" : " (" + TextReceipt.Mask(p.Ref) + ")"), N(p.Amount), false, fs - 1);
            Row("Change", N(r.Change), false, fs + 1);

            var saved = r.LineDiscount + r.BillDiscount;
            if (saved > 0)
            {
                var t = Text("You Saved", fs, true, TextAlignment.Center);
                t.Margin = new Thickness(0, 6, 0, 0);
                Add(t);
                Center($"{r.Currency}{N(saved)}", fs + 3, true);
            }
            DashLine();
            Row("Items Count", r.ItemCount.ToString());
        }

        // ---------- extra lines (Z report, return slip, paid-out voucher) ----------
        if (r.ExtraLines.Count > 0)
        {
            DashLine();
            foreach (var (label, value) in r.ExtraLines)
                if (label == "--") DashLine(); else Row(label, value, label == label.ToUpperInvariant());
            DashLine();
        }

        // ---------- barcode + footer ----------
        if (r.ShowBarcode && !string.IsNullOrEmpty(r.DocumentNo))
        {
            var bc = Barcode128.CreateVisual(r.DocumentNo, width * 0.8, Mm.ToDip(10));
            bc.HorizontalAlignment = HorizontalAlignment.Center;
            bc.Margin = new Thickness(0, 6, 0, 2);
            Add(bc);
        }
        if (!string.IsNullOrEmpty(r.Footer)) Center(r.Footer!, fs + 1);
        if (!string.IsNullOrEmpty(r.PoweredBy))
        {
            DashLine();
            var p = Text(r.PoweredBy!, fs - 3, false, TextAlignment.Center);
            p.FontStyle = FontStyles.Italic;
            Add(p);
        }
        Add(new Border { Height = Mm.ToDip(ReceiptPrinter.BottomFeedMm) });   // space for the cutter
        return root;
    }
}

/// <summary>Half sheet invoice (A5 portrait, or half of A4 landscape) as a paginated document.</summary>
public static class A5Invoice
{
    public static FlowDocument Build(ReceiptData r, bool landscape)
    {
        double w = Mm.ToDip(landscape ? 210 : 148), h = Mm.ToDip(landscape ? 148 : 210);
        var doc = new FlowDocument
        {
            PageWidth = w, PageHeight = h, PagePadding = new Thickness(Mm.ToDip(8)), ColumnWidth = w,
            FontFamily = new FontFamily("Segoe UI, Iskoola Pota, Nirmala UI"), FontSize = 10.5, Foreground = Brushes.Black, Background = Brushes.White
        };
        string N(decimal v) => v.ToString("N2");

        // ---- header: logo + shop (left) | document (right) ----
        var head = new Table { CellSpacing = 0 };
        head.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
        head.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
        var hg = new TableRowGroup(); head.RowGroups.Add(hg);
        var hr = new TableRow(); hg.Rows.Add(hr);

        var shopCell = new TableCell();
        var logo = LogoHelper.ToBitmap(r.LogoBase64);
        if (logo != null)
            shopCell.Blocks.Add(new BlockUIContainer(new Image { Source = logo, MaxHeight = Mm.ToDip(18), MaxWidth = Mm.ToDip(60),
                Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left }));
        var shop = new Paragraph(new Run(r.ShopName) { FontSize = 17, FontWeight = FontWeights.Bold }) { Margin = new Thickness(0) };
        foreach (var l in r.HeaderLines) { shop.Inlines.Add(new LineBreak()); shop.Inlines.Add(new Run(l)); }
        if (!string.IsNullOrEmpty(r.TaxRegNo)) { shop.Inlines.Add(new LineBreak()); shop.Inlines.Add(new Run("VAT Reg No: " + r.TaxRegNo)); }
        shopCell.Blocks.Add(shop);
        hr.Cells.Add(shopCell);

        var info = new Paragraph { TextAlignment = TextAlignment.Right, Margin = new Thickness(0) };
        info.Inlines.Add(new Run(r.Title + (r.IsReprint ? " (COPY)" : "")) { FontSize = 15, FontWeight = FontWeights.Bold });
        info.Inlines.Add(new LineBreak()); info.Inlines.Add(new Run("No: " + r.DocumentNo) { FontWeight = FontWeights.SemiBold });
        info.Inlines.Add(new LineBreak()); info.Inlines.Add(new Run("Date: " + r.Date.ToString("dd/MM/yyyy hh:mm tt")));
        info.Inlines.Add(new LineBreak()); info.Inlines.Add(new Run("Cashier: " + r.Cashier));
        hr.Cells.Add(new TableCell(info));
        doc.Blocks.Add(head);

        if (!string.IsNullOrEmpty(r.Customer))
            doc.Blocks.Add(new Paragraph(new Run("Customer: " + r.Customer) { FontWeight = FontWeights.SemiBold }) { Margin = new Thickness(0, 6, 0, 0) });

        // ---- items ----
        if (r.Lines.Count > 0)
        {
            var t = new Table { CellSpacing = 0, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var c in new[] { 0.4, 1.2, 4.0, 0.9, 1.2, 1.1, 1.4 }) t.Columns.Add(new TableColumn { Width = new GridLength(c, GridUnitType.Star) });
            var g = new TableRowGroup(); t.RowGroups.Add(g);
            var header = new TableRow { Background = Brushes.Black };
            foreach (var (txt, right) in new[] { ("#", false), ("CODE", false), ("PRODUCT", false), ("QTY", true), ("PRICE", true), ("DISC", true), ("AMOUNT", true) })
            {
                var cell = Cell(txt, right, true);
                cell.Foreground = Brushes.White;
                header.Cells.Add(cell);
            }
            g.Rows.Add(header);
            int i = 0;
            foreach (var l in r.Lines)
            {
                var row = new TableRow();
                row.Cells.Add(Cell((++i).ToString(), false));
                row.Cells.Add(Cell(l.Code, false));
                row.Cells.Add(Cell(l.Name, false));
                row.Cells.Add(Cell(l.Qty.ToString("0.000"), true));
                row.Cells.Add(Cell(l.Price.ToString("0.00"), true));
                row.Cells.Add(Cell(l.Discount.ToString("0.00"), true));
                row.Cells.Add(Cell(l.Amount.ToString("0.00"), true));
                g.Rows.Add(row);
            }
            doc.Blocks.Add(t);

            var tot = new Table { CellSpacing = 0, Margin = new Thickness(0, 6, 0, 0) };
            tot.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
            tot.Columns.Add(new TableColumn { Width = new GridLength(1.4, GridUnitType.Star) });
            tot.Columns.Add(new TableColumn { Width = new GridLength(1.2, GridUnitType.Star) });
            var tg = new TableRowGroup(); tot.RowGroups.Add(tg);
            void TR(string a, string b, bool bold = false, double size = 10.5)
            {
                var row = new TableRow();
                row.Cells.Add(new TableCell(new Paragraph()));
                row.Cells.Add(Cell(a, false, bold, size));
                row.Cells.Add(Cell(b, true, bold, size));
                tg.Rows.Add(row);
            }
            TR("Items Count", r.ItemCount.ToString());
            if (r.BillDiscount != 0 || r.Tax != 0 || r.RoundOff != 0)
            {
                TR("Sub Total", N(r.GrossTotal - r.LineDiscount));
                if (r.BillDiscount != 0) TR(r.BillDiscountLabel ?? "Bill Discount", "-" + N(r.BillDiscount));
                if (r.Tax != 0) TR(r.TaxInclusive ? "Tax (included)" : "Tax", N(r.Tax));
                if (r.RoundOff != 0) TR("Round Off", N(r.RoundOff));
            }
            TR("Net Total", r.Currency + " " + N(r.NetTotal), true, 13);
            TR("Paid Amount", N(r.PaidAmount), true);
            foreach (var p in r.Payments) TR("   " + p.Name, N(p.Amount));
            TR("Change", N(r.Change));
            var saved = r.LineDiscount + r.BillDiscount;
            if (saved > 0) TR("You Saved", r.Currency + N(saved), true);
            doc.Blocks.Add(tot);
        }

        if (r.ExtraLines.Count > 0)
        {
            var p = new Paragraph { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var (a, b) in r.ExtraLines) { if (a == "--") continue; p.Inlines.Add(new Run($"{a}: {b}")); p.Inlines.Add(new LineBreak()); }
            doc.Blocks.Add(p);
        }

        // ---- signatures + footer ----
        var foot = new Table { CellSpacing = 0, Margin = new Thickness(0, 22, 0, 0) };
        foot.Columns.Add(new TableColumn()); foot.Columns.Add(new TableColumn()); foot.Columns.Add(new TableColumn());
        var fg = new TableRowGroup(); foot.RowGroups.Add(fg);
        var fr = new TableRow(); fg.Rows.Add(fr);
        fr.Cells.Add(Cell("..............................\nPrepared by", false));
        fr.Cells.Add(new TableCell(new Paragraph(new Run(r.Footer ?? "")) { TextAlignment = TextAlignment.Center, FontStyle = FontStyles.Italic }));
        fr.Cells.Add(Cell("..............................\nCustomer signature", true));
        doc.Blocks.Add(foot);
        if (!string.IsNullOrEmpty(r.PoweredBy))
            doc.Blocks.Add(new Paragraph(new Run(r.PoweredBy)) { FontSize = 8, TextAlignment = TextAlignment.Center, Foreground = Brushes.Gray });
        return doc;
    }

    private static TableCell Cell(string text, bool right, bool bold = false, double size = 10.5) =>
        new(new Paragraph(new Run(text)) { Margin = new Thickness(0), TextAlignment = right ? TextAlignment.Right : TextAlignment.Left })
        {
            Padding = new Thickness(3, 2, 3, 2), FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, FontSize = size,
            BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 0, 0, 0.5)
        };
}

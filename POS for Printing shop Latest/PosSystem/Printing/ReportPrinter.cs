using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace PosSystem.Printing;

/// <summary>Prints any report DataTable as an A4 landscape document with the shop header.</summary>
public static class ReportPrinter
{
    public static void Print(string title, string subTitle, System.Data.DataTable table, string shopName)
    {
        var dlg = new PrintDialog();
        dlg.PrintTicket.PageOrientation = PageOrientation.Landscape;
        if (dlg.ShowDialog() != true) return;

        var doc = new FlowDocument
        {
            PageWidth = dlg.PrintableAreaWidth, PageHeight = dlg.PrintableAreaHeight, ColumnWidth = dlg.PrintableAreaWidth,
            PagePadding = new Thickness(36), FontFamily = new FontFamily("Segoe UI"), FontSize = table.Columns.Count > 9 ? 8.5 : 10
        };
        doc.Blocks.Add(new Paragraph(new Run(shopName) { FontSize = 16, FontWeight = FontWeights.Bold }) { Margin = new Thickness(0) });
        doc.Blocks.Add(new Paragraph(new Run(title) { FontSize = 14, FontWeight = FontWeights.SemiBold }) { Margin = new Thickness(0, 2, 0, 0) });
        doc.Blocks.Add(new Paragraph(new Run(subTitle + "   |   Printed " + DateTime.Now.ToString("yyyy-MM-dd HH:mm")) { Foreground = Brushes.DimGray }) { Margin = new Thickness(0, 0, 0, 8) });

        var t = new Table { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };
        foreach (System.Data.DataColumn _ in table.Columns) t.Columns.Add(new TableColumn());
        var g = new TableRowGroup(); t.RowGroups.Add(g);
        var hr = new TableRow { Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x76, 0x6E)) };
        foreach (System.Data.DataColumn c in table.Columns)
            hr.Cells.Add(new TableCell(new Paragraph(new Run(c.ColumnName)) { Margin = new Thickness(0) })
            { Foreground = Brushes.White, FontWeight = FontWeights.Bold, Padding = new Thickness(3) });
        g.Rows.Add(hr);
        int n = 0;
        foreach (System.Data.DataRow row in table.Rows)
        {
            var tr = new TableRow { Background = n++ % 2 == 0 ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)) };
            foreach (System.Data.DataColumn c in table.Columns)
            {
                var v = row[c];
                bool num = v is decimal or double or int or long or float;
                tr.Cells.Add(new TableCell(new Paragraph(new Run(Format(v))) { Margin = new Thickness(0), TextAlignment = num ? TextAlignment.Right : TextAlignment.Left })
                { Padding = new Thickness(3, 1, 3, 1), FontWeight = row == table.Rows[table.Rows.Count - 1] && Equals(row[0], "TOTAL") ? FontWeights.Bold : FontWeights.Normal });
            }
            g.Rows.Add(tr);
        }
        doc.Blocks.Add(t);
        dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, title);
    }

    private static string Format(object v) => v switch
    {
        DBNull => "",
        decimal d => d.ToString("N2"),
        double d => d.ToString("N2"),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm"),
        _ => v?.ToString() ?? ""
    };
}

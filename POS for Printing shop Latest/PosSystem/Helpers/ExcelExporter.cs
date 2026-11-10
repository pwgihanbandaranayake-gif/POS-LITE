using System.Data;
using ClosedXML.Excel;

namespace PosSystem.Helpers;

/// <summary>Saves a report DataTable as an Excel file.</summary>
public static class ExcelExporter
{
    public static void Export(DataTable table, string title, string filePath)
    {
        using var wb = new XLWorkbook();
        var name = new string(title.Where(c => !"[]*?/\\:".Contains(c)).ToArray());
        var ws = wb.Worksheets.Add(name.Length > 31 ? name[..31] : name);
        ws.Cell(1, 1).Value = title;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = "Generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        var t = ws.Cell(4, 1).InsertTable(table, "ReportData", true);
        t.Theme = XLTableTheme.TableStyleMedium2;
        foreach (var col in t.DataRange.Columns())
        {
            var first = table.Columns[col.ColumnNumber() - 1];
            if (first.DataType == typeof(decimal) || first.DataType == typeof(double))
                col.Style.NumberFormat.Format = "#,##0.00";
        }
        ws.Columns().AdjustToContents();
        wb.SaveAs(filePath);
    }
}

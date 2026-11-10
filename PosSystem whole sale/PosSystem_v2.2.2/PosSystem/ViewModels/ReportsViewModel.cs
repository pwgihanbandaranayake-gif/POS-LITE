using System.Collections.ObjectModel;
using System.Data;
using Microsoft.Win32;

namespace PosSystem.ViewModels;

public sealed record ReportGroup(string Name, IReadOnlyList<ReportDefinition> Reports);

public sealed partial class ReportsViewModel : ViewModelBase
{
    public ReportsViewModel()
    {
        foreach (var g in ReportCatalog.All.Where(r => Session.Has(r.Permission)).GroupBy(r => r.Group))
            Groups.Add(new ReportGroup(g.Key, g.ToList()));
    }

    private static readonly LookupItem All = new(0, "", "(All)");

    public ObservableCollection<ReportGroup> Groups { get; } = new();
    public IReadOnlyList<LookupItem> Locations { get; private set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Users { get; private set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Categories { get; private set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Suppliers { get; private set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Customers { get; private set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Employees { get; private set; } = Array.Empty<LookupItem>();
    [ObservableProperty] private LookupItem? _employee;
    public bool ShowEmployee => SelectedReport?.Has(ReportParams.Employee) == true;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowDates), nameof(ShowAsOf), nameof(ShowLocation), nameof(ShowUser),
        nameof(ShowCategory), nameof(ShowSupplier), nameof(ShowCustomer), nameof(ShowItem), nameof(ShowDays), nameof(ShowTopN), nameof(ShowEmployee), nameof(HasReport))]
    private ReportDefinition? _selectedReport;
    [ObservableProperty] private DateTime _fromDate = DateTime.Today;
    [ObservableProperty] private DateTime _toDate = DateTime.Today;
    [ObservableProperty] private LookupItem? _location;
    [ObservableProperty] private LookupItem? _user;
    [ObservableProperty] private LookupItem? _category;
    [ObservableProperty] private LookupItem? _supplier;
    [ObservableProperty] private LookupItem? _customer;
    [ObservableProperty] private string _itemCode = "";
    [ObservableProperty] private string _days = "30";
    [ObservableProperty] private string _topN = "50";
    [ObservableProperty] private DataView? _result;
    [ObservableProperty] private string _resultInfo = "Choose a report on the left, set the filters and press Run.";
    private DataTable? _table;

    public bool HasReport => SelectedReport != null;
    public bool ShowDates => SelectedReport?.Has(ReportParams.DateRange) == true;
    public bool ShowAsOf => SelectedReport?.Has(ReportParams.AsOfDate) == true;
    public bool ShowLocation => SelectedReport?.Has(ReportParams.Location) == true;
    public bool ShowUser => SelectedReport?.Has(ReportParams.User) == true;
    public bool ShowCategory => SelectedReport?.Has(ReportParams.Category) == true;
    public bool ShowSupplier => SelectedReport?.Has(ReportParams.Supplier) == true;
    public bool ShowCustomer => SelectedReport?.Has(ReportParams.Customer) == true;
    public bool ShowItem => SelectedReport?.Has(ReportParams.Item) == true;
    public bool ShowDays => SelectedReport?.Has(ReportParams.Days) == true;
    public bool ShowTopN => SelectedReport?.Has(ReportParams.TopN) == true;

    partial void OnSelectedReportChanged(ReportDefinition? value) { Result = null; _table = null; ResultInfo = value?.Description ?? ""; }

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            Locations = new[] { All }.Concat(Session.Locations.Select(l => new LookupItem(l.LocationID, l.LocationCode, l.LocationName))).ToList();
            Users = new[] { All }.Concat(await LookupRepository.GetUsersAsync()).ToList();
            Categories = new[] { All }.Concat(await LookupRepository.GetCategoriesAsync()).ToList();
            Suppliers = new[] { All }.Concat(await LookupRepository.GetSuppliersAsync()).ToList();
            Customers = new[] { All }.Concat((await LookupRepository.SearchCustomersAsync(null, 2000)).Select(c => new LookupItem(c.CustomerID, c.Code ?? "", c.Name ?? ""))).ToList();
            Location = Locations.FirstOrDefault(l => l.Id == Session.LocationId) ?? All;
            Employees = new[] { All }.Concat(await LookupRepository.GetEmployeesAsync()).ToList();
            User = Category = Supplier = Customer = Employee = All;
            foreach (var n in new[] { nameof(Locations), nameof(Users), nameof(Categories), nameof(Suppliers), nameof(Customers), nameof(Employees) }) OnPropertyChanged(n);
        });
    }

    [RelayCommand] private void SelectReport(ReportDefinition r) => SelectedReport = r;

    [RelayCommand]
    private void QuickRange(string range)
    {
        var t = DateTime.Today;
        (FromDate, ToDate) = range switch
        {
            "yesterday" => (t.AddDays(-1), t.AddDays(-1)),
            "week" => (t.AddDays(-(((int)t.DayOfWeek + 6) % 7)), t),
            "month" => (new DateTime(t.Year, t.Month, 1), t),
            "lastmonth" => (new DateTime(t.Year, t.Month, 1).AddMonths(-1), new DateTime(t.Year, t.Month, 1).AddDays(-1)),
            _ => (t, t)
        };
    }

    private static object? Id(LookupItem? l) => l == null || l.Id == 0 ? null : l.Id;

    [RelayCommand]
    private async Task Run()
    {
        var r = SelectedReport;
        if (r == null) return;
        if (r.Has(ReportParams.DateRange) && FromDate > ToDate) { Dialogs.Error("From date is after To date."); return; }

        await RunAsync(async () =>
        {
            var p = new Dictionary<string, object?>();
            if (r.Has(ReportParams.DateRange)) { p["FromDate"] = FromDate.Date; p["ToDate"] = ToDate.Date; }
            if (r.Has(ReportParams.AsOfDate)) p["ToDate"] = ToDate.Date;
            if (r.Has(ReportParams.Location)) p["LocationID"] = Id(Location);
            if (r.Has(ReportParams.User)) p["UserID"] = Id(User);
            if (r.Has(ReportParams.Category)) p["CategoryID"] = Id(Category);
            if (r.Has(ReportParams.Supplier)) p["SupplierID"] = Id(Supplier);
            if (r.Has(ReportParams.Customer)) p["CustomerID"] = Id(Customer);
            if (r.Has(ReportParams.Employee)) p["EmployeeID"] = Id(Employee);
            if (r.Has(ReportParams.Days)) p["Days"] = int.TryParse(Days, out var d) ? d : 30;
            if (r.Has(ReportParams.TopN)) p["TopN"] = int.TryParse(TopN, out var n) ? n : 50;
            if (r.Has(ReportParams.Item))
            {
                int? itemId = null;
                if (!string.IsNullOrWhiteSpace(ItemCode))
                {
                    var item = await ItemRepository.FindByCodeAsync(ItemCode, Session.LocationId);
                    if (item == null) { Dialogs.Error("Item code not found."); return; }
                    itemId = item.ItemID;
                }
                p["ItemID"] = itemId;
            }

            var table = await ReportRepository.RunAsync(r.Procedure, p);
            var rows = table.Rows.Count;
            AddTotals(table);
            _table = table;
            Result = table.DefaultView;
            ResultInfo = rows == 0 ? "No records for the selected filters." : $"{rows} rows  |  {Subtitle()}";
        });
    }

    private string Subtitle()
    {
        var r = SelectedReport!;
        var parts = new List<string>();
        if (r.Has(ReportParams.DateRange)) parts.Add(FromDate == ToDate ? FromDate.ToString("yyyy-MM-dd") : $"{FromDate:yyyy-MM-dd} to {ToDate:yyyy-MM-dd}");
        if (r.Has(ReportParams.AsOfDate)) parts.Add("As at " + ToDate.ToString("yyyy-MM-dd"));
        if (r.Has(ReportParams.Location)) parts.Add("Location: " + (Location?.Name ?? "All"));
        if (r.Has(ReportParams.User) && Id(User) != null) parts.Add("User: " + User!.Name);
        if (r.Has(ReportParams.Category) && Id(Category) != null) parts.Add("Category: " + Category!.Name);
        if (r.Has(ReportParams.Supplier) && Id(Supplier) != null) parts.Add("Supplier: " + Supplier!.Name);
        if (r.Has(ReportParams.Employee) && Id(Employee) != null) parts.Add("Employee: " + Employee!.Name);
        return string.Join("   ", parts);
    }

    /// <summary>Adds a TOTAL row for money/quantity columns (not for rates, prices, averages or percentages).</summary>
    private static void AddTotals(DataTable t)
    {
        if (t.Rows.Count < 2) return;
        string[] skip = { "%", "Avg", "Rate", "Price", "Running", "Limit", "Days", "Session" };
        var labelCol = t.Columns.Cast<DataColumn>().FirstOrDefault(c => c.DataType == typeof(string));
        var sumCols = t.Columns.Cast<DataColumn>()
            .Where(c => (c.DataType == typeof(decimal) || c.DataType == typeof(int) || c.DataType == typeof(double))
                        && !skip.Any(s => c.ColumnName.Contains(s, StringComparison.OrdinalIgnoreCase))
                        && c.ColumnName is not "Cost" and not "Selling")
            .ToList();
        if (sumCols.Count == 0) return;
        t.Constraints.Clear();
        foreach (DataColumn c in t.Columns) { c.ReadOnly = false; c.AllowDBNull = true; }
        var row = t.NewRow();
        if (labelCol != null) row[labelCol] = "TOTAL";
        foreach (var c in sumCols)
        {
            var sum = t.Rows.Cast<DataRow>().Where(x => x[c] != DBNull.Value).Sum(x => Convert.ToDecimal(x[c]));
            row[c] = Convert.ChangeType(sum, c.DataType);
        }
        t.Rows.Add(row);
    }

    [RelayCommand]
    private void Print()
    {
        if (_table == null || SelectedReport == null) return;
        try { ReportPrinter.Print(SelectedReport.Title, Subtitle(), _table, Session.Settings.ShopName); }
        catch (Exception ex) { Dialogs.Error(ex.Message, "Print"); }
    }

    [RelayCommand]
    private void Export()
    {
        if (_table == null || SelectedReport == null) return;
        var dlg = new SaveFileDialog
        {
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            FileName = $"{SelectedReport.Title} {DateTime.Now:yyyy-MM-dd}.xlsx".Replace("/", "-")
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            ExcelExporter.Export(_table, SelectedReport.Title, dlg.FileName);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (Exception ex) { Dialogs.Error(ex.Message, "Export"); }
    }
}

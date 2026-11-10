using System.Collections.ObjectModel;
using System.Data;
using System.Windows.Threading;

namespace PosSystem.ViewModels;

public sealed partial class LoginViewModel : ViewModelBase
{
    public event Action? LoggedIn;
    [ObservableProperty] private string _userName = "";
    public string Password { get; set; } = "";
    [ObservableProperty] private string? _error;
    public string MachineName => Environment.MachineName;

    [RelayCommand]
    private async Task Login()
    {
        Error = null;
        LoginResult? r = null;
        if (!await RunAsync(async () => r = await AuthService.LoginAsync(UserName, Password)) || r == null) return;
        if (!r.Success) { Error = r.Error; return; }

        if (r.NeedsTerminalRegistration)
        {
            if (!Session.Has(Perm.SettingsTerminal))
            {
                Error = $"This computer ({MachineName}) is not registered. An administrator must log in here once to register it.";
                AuthService.Logout();
                return;
            }
            var vm = new TerminalSetupViewModel();
            if (Dialogs.Show(vm) != true) { AuthService.Logout(); return; }
        }
        Password = "";
        LoggedIn?.Invoke();
    }
}

public sealed record NavItem(string Title, string Icon, string? Permission, Func<ViewModelBase>? Factory, string? Action = null);
public sealed record NavGroup(string Title, IReadOnlyList<NavItem> Items);

public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly DispatcherTimer _clock;

    public event Action? OpenPosRequested;
    public event Action? LogoutRequested;

    public ShellViewModel()
    {
        ShopName = Session.Settings.ShopName;
        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _clock.Tick += (_, _) => Now = DateTime.Now;
        _clock.Start();
        BuildMenu();
        Lang.ApplyRequested += OnLanguageApplied;
    }

    /// <summary>Settings > Language > Apply: log out and reopen every screen in the new language.</summary>
    private void OnLanguageApplied()
    {
        Lang.ApplyRequested -= OnLanguageApplied;
        _clock.Stop();
        AuthService.Logout();
        LogoutRequested?.Invoke();
    }

    public string ShopName { get; }
    public string UserFullName => Session.FullName;
    public string LocationName => Session.LocationName;
    public string TerminalName => Session.TerminalName;
    [ObservableProperty] private DateTime _now = DateTime.Now;
    [ObservableProperty] private ViewModelBase? _currentPage;
    [ObservableProperty] private string _currentTitle = "Dashboard";
    public ObservableCollection<NavGroup> Menu { get; } = new();

    private void BuildMenu()
    {
        var groups = new List<NavGroup>
        {
            new("Billing", new[]
            {
                new NavItem("Open billing (POS)", "\uE7BF", Perm.PosAccess, null, "pos"),
                new NavItem("Invoices & re-print", "\uE8A5", Perm.PosReprint, () => new InvoicesViewModel()),
                new NavItem("Sales returns", "\uE7A7", Perm.ReturnsSales, () => new SalesReturnViewModel()),
            }),
            new("Inventory", new[]
            {
                new NavItem("Items", "\uE7B8", Perm.MasterItems, () => new ItemMasterViewModel()),
                new NavItem("Goods received (GRN)", "\uE896", Perm.StockGrn, () => StockDoc(StockDocKind.Grn)),
                new NavItem("Return to supplier", "\uE72B", Perm.ReturnsPurchase, () => StockDoc(StockDocKind.PurchaseReturn)),
                new NavItem("Stock transfer", "\uE8AB", Perm.StockTransfer, () => StockDoc(StockDocKind.Transfer)),
                new NavItem("Stock adjustment", "\uE9D5", Perm.StockAdjustment, () => StockDoc(StockDocKind.Adjustment)),
                new NavItem("Barcode stickers", "\uE8A1", Perm.StockBarcode, () => new BarcodePrintViewModel()),
            }),
            new("Cash", new[]
            {
                new NavItem("Paid out / expense", "\uE8C7", Perm.CashPaidOut, () => Cash(true)),
                new NavItem("Receive payment", "\uE8C8", Perm.CashReceived, () => Cash(false)),
                new NavItem("Supplier payment", "\uE8C7", Perm.CashSupplierPayment, () => new SupplierPaymentViewModel()),
                new NavItem("Close my shift", "\uE7E8", Perm.SessionClose, () => new SessionCloseViewModel()),
            }),
            new("Masters", MasterCatalog.All.Select(d => new NavItem(d.Title, d.Icon, d.Permission, () => new MasterListViewModel(d))).ToList()),
            new("Reports", new[] { new NavItem("Reports", "\uE9F9", "Reports.*", () => new ReportsViewModel()) }),
            new("Administration", new[]
            {
                new NavItem("Users", "\uE77B", Perm.SecurityUsers, () => new UsersViewModel()),
                new NavItem("Roles & permissions", "\uE8D7", Perm.SecurityRoles, () => new RolesViewModel()),
                new NavItem("Settings & printers", "\uE713", "Settings.*", () => new SettingsViewModel()),
            }),
        };
        Menu.Clear();
        foreach (var g in groups)
        {
            var visible = g.Items.Where(i => Allowed(i.Permission)).ToList();
            if (visible.Count > 0) Menu.Add(new NavGroup(g.Title, visible));
        }
    }

    private bool Allowed(string? p)
    {
        if (p == null) return true;
        if (p == "Reports.*") return Session.Has(Perm.ReportsSales) || Session.Has(Perm.ReportsStock) || Session.Has(Perm.ReportsPurchase) || Session.Has(Perm.ReportsFinancial);
        if (p == "Settings.*") return Session.Has(Perm.SettingsManage) || Session.Has(Perm.SettingsTerminal);
        return Session.Has(p);
    }

    private ViewModelBase StockDoc(StockDocKind kind) { var vm = new StockDocumentViewModel(); vm.Init(kind); return vm; }
    private ViewModelBase Cash(bool paidOut) { var vm = new CashEntryViewModel(); vm.IsPaidOut = paidOut; return vm; }

    public override async Task LoadAsync() => await ShowDashboard();

    [RelayCommand]
    private async Task ShowDashboard()
    {
        CurrentTitle = "Dashboard";
        CurrentPage = new DashboardViewModel();
        await CurrentPage.LoadAsync();
    }

    [RelayCommand]
    private async Task Navigate(NavItem item)
    {
        if (item.Action == "pos") { OpenPosRequested?.Invoke(); return; }
        if (item.Factory == null) return;
        var page = item.Factory();
        CurrentTitle = item.Title;
        CurrentPage = page;
        await page.LoadAsync();
    }

    [RelayCommand] private void OpenPos() { if (Session.Has(Perm.PosAccess)) OpenPosRequested?.Invoke(); else Dialogs.Error("You do not have access to billing."); }
    [RelayCommand] private void ChangePassword() => Dialogs.Show(new ChangePasswordViewModel());

    [RelayCommand]
    private void Logout()
    {
        if (!Dialogs.Confirm("Log out?")) return;
        Lang.ApplyRequested -= OnLanguageApplied;
        _clock.Stop();
        AuthService.Logout();
        LogoutRequested?.Invoke();
    }
}

public sealed record BarPoint(string Label, decimal Value, double Height, string ValueText);

public sealed partial class DashboardViewModel : ViewModelBase
{
    public string Greeting => Lang.T(DateTime.Now.Hour < 12 ? "Good morning" : DateTime.Now.Hour < 17 ? "Good afternoon" : "Good evening") + ", " + Session.FullName;
    public bool CanSeeSales => Session.Has(Perm.ReportsSales);
    [ObservableProperty] private decimal _todaySales;
    [ObservableProperty] private int _todayBills;
    [ObservableProperty] private decimal _avgBill;
    [ObservableProperty] private decimal _todayDiscount;
    [ObservableProperty] private int _lowStockCount;
    public ObservableCollection<BarPoint> Hourly { get; } = new();
    public ObservableCollection<DataRowView> TopItems { get; } = new();

    public override async Task LoadAsync()
    {
        if (!CanSeeSales) return;
        await RunAsync(async () =>
        {
            var today = DateTime.Today;
            var p = new Dictionary<string, object?> { ["FromDate"] = today, ["ToDate"] = today, ["LocationID"] = Session.LocationId };
            var daily = await ReportRepository.RunAsync("rpt_DailySalesSummary", p);
            if (daily.Rows.Count > 0)
            {
                var r = daily.Rows[0];
                TodaySales = Convert.ToDecimal(r["Net Sales"]); TodayBills = Convert.ToInt32(r["Bills"]);
                AvgBill = r["Avg Bill"] is DBNull ? 0 : Convert.ToDecimal(r["Avg Bill"]); TodayDiscount = Convert.ToDecimal(r["Discount"]);
            }
            var hourly = await ReportRepository.RunAsync("rpt_HourlySales", p);
            var max = hourly.Rows.Cast<DataRow>().Select(x => Convert.ToDecimal(x["Net Sales"])).DefaultIfEmpty(0).Max();
            Hourly.Clear();
            foreach (DataRow h in hourly.Rows)
            {
                var v = Convert.ToDecimal(h["Net Sales"]);
                Hourly.Add(new BarPoint(h["Hour"].ToString()![..2], v, max == 0 ? 0 : (double)(v / max) * 150, v.ToString("N0")));
            }
            var top = await ReportRepository.RunAsync("rpt_FastMovingItems", new Dictionary<string, object?> { ["FromDate"] = today, ["ToDate"] = today, ["LocationID"] = Session.LocationId, ["TopN"] = 8 });
            TopItems.Clear();
            foreach (DataRowView r in top.DefaultView) TopItems.Add(r);
            var rol = await ReportRepository.RunAsync("rpt_ReorderLevel", new Dictionary<string, object?> { ["LocationID"] = Session.LocationId });
            LowStockCount = rol.Rows.Count;
        });
    }
}

/* ---------------- Generic master ---------------- */
public sealed partial class FieldViewModel : ObservableObject
{
    public FieldViewModel(MasterField f, IReadOnlyList<LookupItem>? options) { Field = f; Options = options ?? Array.Empty<LookupItem>(); }
    public MasterField Field { get; }
    public string Label => Lang.T(Field.Label) + (Field.Required ? " *" : "");
    public bool IsText => Field.Kind is FieldKind.Text or FieldKind.Number or FieldKind.Color;
    public bool IsMultiLine => Field.Kind == FieldKind.MultiLine;
    public bool IsBool => Field.Kind == FieldKind.Bool;
    public bool IsLookup => Field.Kind == FieldKind.Lookup;
    public IReadOnlyList<LookupItem> Options { get; }
    [ObservableProperty] private string? _text;
    [ObservableProperty] private bool _flag;
    [ObservableProperty] private LookupItem? _option;

    public void Set(object? v)
    {
        Text = v switch { null => null, decimal d => d.ToString("0.####"), _ => v.ToString() };
        Flag = v is bool b && b;
        Option = v == null ? null : Options.FirstOrDefault(o => o.Id.ToString() == v.ToString());
    }

    public object? Get() => Field.Kind switch
    {
        FieldKind.Bool => Flag,
        FieldKind.Lookup => Option?.Id,
        _ => Text
    };
}

public sealed partial class MasterListViewModel : ViewModelBase
{
    public MasterListViewModel(MasterDefinition def) { Definition = def; }

    public MasterDefinition Definition { get; }
    public ObservableCollection<FieldViewModel> Fields { get; } = new();
    [ObservableProperty] private DataView? _rows;
    [ObservableProperty] private DataRowView? _selectedRow;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private int _editingId;
    public string FormTitle => EditingId == 0
        ? (Lang.Current == "en" ? "New " + Definition.Title.TrimEnd('s').ToLowerInvariant() : Lang.T("New") + " - " + Lang.T(Definition.Title))
        : Lang.T("Edit");

    partial void OnEditingIdChanged(int value) => OnPropertyChanged(nameof(FormTitle));
    partial void OnSearchChanged(string value) => _ = Refresh();
    partial void OnSelectedRowChanged(DataRowView? value) { if (value != null) _ = Edit(Convert.ToInt32(value["Id"])); }

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            Fields.Clear();
            foreach (var f in Definition.Fields)
                Fields.Add(new FieldViewModel(f, f.LookupSql == null ? null : await MasterRepository.LookupAsync(f.LookupSql)));
            New();
            await Refresh();
        });
    }

    private async Task Refresh() => Rows = (await MasterRepository.ListAsync(Definition, Search)).DefaultView;

    private async Task Edit(int id)
    {
        await RunAsync(async () =>
        {
            var data = await MasterRepository.GetAsync(Definition, id);
            foreach (var f in Fields) f.Set(data.TryGetValue(f.Field.Column, out var v) ? v : null);
            EditingId = id;
        });
    }

    [RelayCommand]
    private void New()
    {
        foreach (var f in Fields) f.Set(f.Field.Default);
        EditingId = 0;
        SelectedRow = null;
    }

    [RelayCommand]
    private async Task Save()
    {
        await RunAsync(async () =>
        {
            var values = Fields.ToDictionary(f => f.Field.Column, f => f.Get());
            EditingId = await MasterRepository.SaveAsync(Definition, EditingId, values, Session.UserId);
            await Refresh();
        });
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (EditingId == 0 || !Dialogs.Confirm("Delete this record?")) return;
        await RunAsync(async () => { await MasterRepository.DeleteAsync(Definition, EditingId, Session.UserId); New(); await Refresh(); });
    }
}

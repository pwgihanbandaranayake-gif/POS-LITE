using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.Collections.ObjectModel;

namespace PosSystem.ViewModels;

/* =====================================================================
   ITEM MASTER
   ===================================================================== */
public sealed partial class ItemMasterViewModel : ViewModelBase
{
    public ItemMasterViewModel()
    {
        CanChangePrice = Session.Has(Perm.StockPriceChange);
        CanViewCost = Session.Has(Perm.StockViewCost);
    }

    public bool CanChangePrice { get; }
    public bool CanViewCost { get; }
    public ObservableCollection<Item> Items { get; } = new();
    public ObservableCollection<StockBatch> Batches { get; } = new();
    public IReadOnlyList<LookupItem> Categories { get; private set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Brands { get; private set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Units { get; private set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Taxes { get; private set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> FilterCategories { get; private set; } = Array.Empty<LookupItem>();

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private LookupItem? _filterCategory;
    [ObservableProperty] private Item? _selectedItem;

    // form
    [ObservableProperty] private int _itemId;
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _barcode = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private LookupItem? _category;
    [ObservableProperty] private LookupItem? _brand;
    [ObservableProperty] private LookupItem? _unit;
    [ObservableProperty] private LookupItem? _tax;
    [ObservableProperty] private string _costPrice = "0";
    [ObservableProperty] private string _sellingPrice = "0";
    [ObservableProperty] private string _ourPrice = "0";
    [ObservableProperty] private string _reorderLevel = "0";
    [ObservableProperty] private bool _isService;
    [ObservableProperty] private bool _isWeighted;
    [ObservableProperty] private bool _allowDiscount = true;
    [ObservableProperty] private bool _allowPriceChange;
    [ObservableProperty] private bool _showInTouch;
    [ObservableProperty] private string _touchColor = "";
    [ObservableProperty] private bool _applyPriceToStock = true;
    [ObservableProperty] private decimal _stockOnHand;
    public string FormTitle => ItemId == 0 ? "New item" : "Edit item";
    public string MarginText
    {
        get
        {
            if (!decimal.TryParse(CostPrice, out var c) || !decimal.TryParse(SellingPrice, out var s) || s <= 0) return "";
            return $"Margin {(s - c) / s * 100:0.#}%  ({s - c:N2})";
        }
    }

    partial void OnItemIdChanged(int value) => OnPropertyChanged(nameof(FormTitle));
    partial void OnCostPriceChanged(string value) => OnPropertyChanged(nameof(MarginText));
    partial void OnSellingPriceChanged(string value) => OnPropertyChanged(nameof(MarginText));
    partial void OnSearchChanged(string value) => _ = Refresh();
    partial void OnFilterCategoryChanged(LookupItem? value) => _ = Refresh();
    partial void OnSelectedItemChanged(Item? value) { if (value != null) _ = Edit(value); }

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            Categories = await LookupRepository.GetCategoriesAsync();
            Brands = await LookupRepository.GetBrandsAsync();
            Units = await LookupRepository.GetUnitsAsync();
            Taxes = await LookupRepository.GetTaxesAsync();
            FilterCategories = new[] { new LookupItem(0, "", "All categories") }.Concat(Categories).ToList();
            OnPropertyChanged(nameof(Categories)); OnPropertyChanged(nameof(Brands)); OnPropertyChanged(nameof(Units));
            OnPropertyChanged(nameof(Taxes)); OnPropertyChanged(nameof(FilterCategories));
            _filterCategory = FilterCategories[0]; OnPropertyChanged(nameof(FilterCategory));
            New();
            await Refresh();
        });
    }

    private async Task Refresh()
    {
        var list = await ItemRepository.ListAsync(Search, FilterCategory == null || FilterCategory.Id == 0 ? null : FilterCategory.Id, Session.LocationId);
        Items.Clear();
        foreach (var i in list) Items.Add(i);
    }

    private async Task Edit(Item i)
    {
        ItemId = i.ItemID; Code = i.ItemCode ?? ""; Barcode = i.BarCode ?? ""; Name = i.ItemName ?? i.ItemDescription ?? "";
        Description = i.ItemDescription ?? "";
        Category = Categories.FirstOrDefault(c => c.Id == i.ItemCategoryID);
        Brand = Brands.FirstOrDefault(c => c.Id == i.BrandID);
        Unit = Units.FirstOrDefault(c => c.Id == i.UnitTypeId);
        Tax = Taxes.FirstOrDefault(c => c.Id == i.TaxID);
        CostPrice = i.CostPrice.ToString("0.00"); SellingPrice = i.SellingPrice.ToString("0.00"); OurPrice = i.OurPrice.ToString("0.00");
        ReorderLevel = i.ReorderLevel.ToString("0.###"); IsService = i.IsService; IsWeighted = i.IsWeighted;
        AllowDiscount = i.AllowDiscount; AllowPriceChange = i.AllowPriceChange; ShowInTouch = i.ShowInTouch; TouchColor = i.TouchColor ?? "";
        StockOnHand = i.StockOnHand;
        Batches.Clear();
        foreach (var b in await ItemRepository.GetBatchesAsync(i.ItemID, Session.LocationId, false)) Batches.Add(b);
    }

    [RelayCommand]
    private void New()
    {
        ItemId = 0; Code = ""; Barcode = ""; Name = ""; Description = ""; Category = null; Brand = null;
        Unit = Units.FirstOrDefault(); Tax = null; CostPrice = "0"; SellingPrice = "0"; OurPrice = "0"; ReorderLevel = "0";
        IsService = false; IsWeighted = false; AllowDiscount = true; AllowPriceChange = false; ShowInTouch = false; TouchColor = "";
        StockOnHand = 0; Batches.Clear(); SelectedItem = null;
    }

    [RelayCommand]
    private void GenerateCode()
    {
        var max = Items.Select(i => long.TryParse(i.ItemCode, out var n) ? n : 0).DefaultIfEmpty(0).Max();
        Code = (max + 1).ToString();
    }

    [RelayCommand]
    private async Task Save()
    {
        if (!decimal.TryParse(CostPrice, out var cost) || !decimal.TryParse(SellingPrice, out var sell)
            || !decimal.TryParse(OurPrice, out var our) || !decimal.TryParse(ReorderLevel, out var rol))
        { Dialogs.Error("Prices and re-order level must be numbers."); return; }
        if (cost > sell && sell > 0 && !Dialogs.Confirm("Cost price is higher than the selling price. Save anyway?")) return;

        var item = new Item
        {
            ItemID = ItemId, ItemCode = Code.Trim(), BarCode = Barcode.Trim(), ItemName = Name.Trim(), ItemDescription = Description.Trim(),
            ItemCategoryID = Category?.Id, BrandID = Brand?.Id, UnitTypeId = Unit?.Id, TaxID = Tax?.Id,
            CostPrice = cost, SellingPrice = sell, OurPrice = our, ReorderLevel = rol, IsService = IsService, IsWeighted = IsWeighted,
            AllowDiscount = AllowDiscount, AllowPriceChange = AllowPriceChange, ShowInTouch = ShowInTouch,
            TouchColor = string.IsNullOrWhiteSpace(TouchColor) ? null : TouchColor.Trim()
        };
        await RunAsync(async () =>
        {
            ItemId = await ItemRepository.SaveAsync(item, ApplyPriceToStock, Session.UserId, Session.TerminalId);
            await Refresh();
            Dialogs.Info($"'{item.ItemName}' saved.");
        });
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (ItemId == 0 || !Dialogs.Confirm($"Delete item '{Name}'? It will no longer be available for billing.")) return;
        await RunAsync(async () => { await ItemRepository.DeleteAsync(ItemId, Session.UserId); New(); await Refresh(); });
    }
}

/* =====================================================================
   USERS
   ===================================================================== */
public sealed partial class UsersViewModel : ViewModelBase
{


    public ObservableCollection<UserAccount> Users { get; } = new();
    public IReadOnlyList<LookupItem> Roles { get; private set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<Location> Locations => Session.Locations;
    [ObservableProperty] private UserAccount? _selected;
    [ObservableProperty] private int _userId;
    [ObservableProperty] private string _loginName = "";
    [ObservableProperty] private string _fullName = "";
    [ObservableProperty] private LookupItem? _role;
    [ObservableProperty] private Location? _defaultLocation;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private string _newPassword = "";

    partial void OnSelectedChanged(UserAccount? value)
    {
        if (value == null) return;
        UserId = value.UserProfileId; LoginName = value.userId ?? ""; FullName = value.FullName ?? "";
        Role = Roles.FirstOrDefault(r => r.Id == value.UserGroupID);
        DefaultLocation = Locations.FirstOrDefault(l => l.LocationID == value.DefaultLocationID);
        IsActive = value.IsSystemAccessible; NewPassword = "";
    }

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            Roles = await LookupRepository.GetRolesAsync(); OnPropertyChanged(nameof(Roles));
            await Refresh();
            New();
        });
    }

    private async Task Refresh()
    {
        Users.Clear();
        foreach (var u in await UserRepository.ListAsync()) Users.Add(u);
    }

    [RelayCommand]
    private void New()
    {
        Selected = null; UserId = 0; LoginName = ""; FullName = ""; Role = Roles.FirstOrDefault();
        DefaultLocation = Locations.FirstOrDefault(); IsActive = true; NewPassword = "";
    }

    [RelayCommand]
    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(LoginName) || string.IsNullOrWhiteSpace(FullName) || Role == null)
        { Dialogs.Error("User name, full name and role are required."); return; }
        if (UserId == 0 && NewPassword.Length < 4) { Dialogs.Error("Enter a password of at least 4 characters for the new user."); return; }
        if (UserId == Session.UserId && !IsActive) { Dialogs.Error("You cannot deactivate your own account."); return; }

        var u = new UserAccount
        {
            UserProfileId = UserId, userId = LoginName.Trim(), FullName = FullName.Trim(), UserGroupID = Role.Id,
            DefaultLocationID = DefaultLocation?.LocationID, IsSystemAccessible = IsActive
        };
        await RunAsync(async () =>
        {
            UserId = await UserRepository.SaveAsync(u, string.IsNullOrEmpty(NewPassword) ? null : PasswordHasher.Hash(NewPassword), Session.UserId);
            NewPassword = "";
            await Refresh();
            Dialogs.Info("User saved.");
        });
    }
}

/* =====================================================================
   ROLES & PERMISSIONS
   ===================================================================== */
public sealed partial class PermissionItem : ObservableObject
{
    public Permission Permission { get; init; } = new();
    public string Text => Permission.Description ?? Permission.FormRuleName;
    public string Key => Permission.FormRuleName;
    [ObservableProperty] private bool _isChecked;
}

public sealed record PermissionGroup(string Name, IReadOnlyList<PermissionItem> Items);

public sealed partial class RolesViewModel : ViewModelBase
{
    public ObservableCollection<Role> Roles { get; } = new();
    public ObservableCollection<PermissionGroup> Groups { get; } = new();
    [ObservableProperty] private Role? _selected;
    [ObservableProperty] private int _roleId;
    [ObservableProperty] private string _roleName = "";
    [ObservableProperty] private bool _isSuperUser;
    [ObservableProperty] private string _maxDiscount = "0";

    partial void OnSelectedChanged(Role? value) { if (value != null) _ = LoadRole(value); }

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var perms = await UserRepository.ListPermissionsAsync();
            Groups.Clear();
            foreach (var g in perms.GroupBy(p => p.FormName ?? "Other"))
                Groups.Add(new PermissionGroup(g.Key, g.Select(p => new PermissionItem { Permission = p }).ToList()));
            await RefreshRoles();
            Selected = Roles.FirstOrDefault();
        });
    }

    private async Task RefreshRoles()
    {
        Roles.Clear();
        foreach (var r in await UserRepository.ListRolesAsync()) Roles.Add(r);
    }

    private async Task LoadRole(Role r)
    {
        RoleId = r.UserGroupID; RoleName = r.UserGroupName; IsSuperUser = r.IsSuperUser; MaxDiscount = r.MaxDiscountPercent.ToString("0.##");
        var ids = (await UserRepository.GetRolePermissionIdsAsync(r.UserGroupID)).ToHashSet();
        foreach (var p in Groups.SelectMany(g => g.Items)) p.IsChecked = ids.Contains(p.Permission.FormRuleID);
    }

    [RelayCommand]
    private void New()
    {
        Selected = null; RoleId = 0; RoleName = ""; IsSuperUser = false; MaxDiscount = "0";
        foreach (var p in Groups.SelectMany(g => g.Items)) p.IsChecked = false;
    }

    [RelayCommand]
    private void ToggleGroup(PermissionGroup g)
    {
        var all = g.Items.All(i => i.IsChecked);
        foreach (var i in g.Items) i.IsChecked = !all;
    }

    [RelayCommand]
    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(RoleName)) { Dialogs.Error("Enter the role name."); return; }
        if (!decimal.TryParse(MaxDiscount, out var md) || md < 0 || md > 100) { Dialogs.Error("Max discount must be 0 - 100 (0 = no limit)."); return; }
        await RunAsync(async () =>
        {
            var id = await UserRepository.SaveRoleAsync(new Role { UserGroupID = RoleId, UserGroupName = RoleName.Trim(), IsSuperUser = IsSuperUser, MaxDiscountPercent = md }, Session.UserId);
            await UserRepository.SetRolePermissionsAsync(id, Groups.SelectMany(g => g.Items).Where(i => i.IsChecked).Select(i => i.Permission.FormRuleID), Session.UserId);
            await RefreshRoles();
            Selected = Roles.FirstOrDefault(r => r.UserGroupID == id);
            Dialogs.Info("Role saved. Users get the new permissions at their next login.");
        });
    }
}

/* =====================================================================
   SETTINGS (shop details, billing & tax, terminal & printers)
   ===================================================================== */
public sealed partial class SettingsViewModel : ViewModelBase
{
    public SettingsViewModel()
    {
        CanManageShop = Session.Has(Perm.SettingsManage);
        CanManageTerminal = Session.Has(Perm.SettingsTerminal);
        Shop = Clone(Session.Settings);
        Terminal = CloneTerminal(Session.Terminal);
        _printerLocation = Session.Locations.FirstOrDefault(l => l.LocationID == Terminal.LocationID);
        _logoPreview = LogoHelper.ToBitmap(Shop.LogoBase64);
    }

    public bool CanManageShop { get; }
    public bool CanManageTerminal { get; }
    public PosSettings Shop { get; }
    public Terminal Terminal { get; }
    public IReadOnlyList<Location> Locations => Session.Locations;
    public IReadOnlyList<string> Printers { get; private set; } = Array.Empty<string>();
    [ObservableProperty] private Location? _printerLocation;

    // radio helpers (int-backed)
    public int ReceiptFormat { get => Terminal.ReceiptFormat; set { Terminal.ReceiptFormat = value; OnPropertyChanged(); } }
    public int PrintEngine { get => Terminal.PrintEngine; set { Terminal.PrintEngine = value; OnPropertyChanged(); } }

    // ---------- language (saved per computer) ----------
    public IReadOnlyList<LookupItem> LanguageList { get; } =
        Lang.Languages.Select((l, i) => new LookupItem(i, l.Code, l.Name)).ToList();
    [ObservableProperty] private LookupItem? _selectedLanguage =
        Lang.Languages.Select((l, i) => new LookupItem(i, l.Code, l.Name)).FirstOrDefault(l => l.Code == Lang.Current);

    [RelayCommand]
    private void ApplyLanguage()
    {
        if (SelectedLanguage == null) return;
        if (SelectedLanguage.Code == Lang.Current) { Dialogs.Info("This language is already in use."); return; }
        if (!Dialogs.Confirm("The system will log out and open again in the selected language. Continue?")) return;
        Lang.Set(SelectedLanguage.Code);
        Lang.RequestApply();
    }

    // ---------- shop logo ----------
    [ObservableProperty] private BitmapSource? _logoPreview;

    [RelayCommand]
    private void BrowseLogo()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select shop logo",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            Shop.LogoBase64 = LogoHelper.LoadFromFile(dlg.FileName);
            Shop.ShowLogo = true;
            LogoPreview = LogoHelper.ToBitmap(Shop.LogoBase64);
            OnPropertyChanged(nameof(Shop));
        }
        catch (Exception ex) { Dialogs.Error("Could not load the image. " + ex.Message, "Logo"); }
    }

    [RelayCommand]
    private void RemoveLogo()
    {
        Shop.LogoBase64 = "";
        LogoPreview = null;
    }

    public override Task LoadAsync()
    {
        Printers = ReceiptPrinter.GetInstalledPrinters();
        OnPropertyChanged(nameof(Printers));
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void ApplyPaperPreset(string mm)
    {
        Terminal.PaperWidthMm = decimal.Parse(mm, System.Globalization.CultureInfo.InvariantCulture);
        Terminal.CharsPerLine = mm switch { "58" => 32, "65" => 40, "76" => 40, _ => 48 };
        OnPropertyChanged(nameof(Terminal));
    }

    [RelayCommand]
    private async Task Save()
    {
        await RunAsync(async () =>
        {
            if (CanManageShop) await SettingsService.SaveSettingsAsync(Shop);
            if (CanManageTerminal)
            {
                if (PrinterLocation != null) Terminal.LocationID = PrinterLocation.LocationID;
                await SettingsService.SaveTerminalAsync(Terminal);
            }
            Dialogs.Info("Settings saved.");
        });
    }

    /// <summary>Test bill used by Preview / Test print (same items as a real shop bill).</summary>
    private ReceiptData Sample() => new()
    {
        Title = "INVOICE", DocumentNo = Session.LocationCode + "-" + DateTime.Now.ToString("yyMMdd") + "-0001", Date = DateTime.Now,
        Cashier = Session.FullName, Terminal = Terminal.Description, Location = Session.LocationName,
        Lines = new()
        {
            new ReceiptLine { Code = "1001", Name = "W/B Floor Paint Opel 500ml", Qty = 1, Price = 1000, Discount = 100, Amount = 900 },
            new ReceiptLine { Code = "1001", Name = "W/B Floor Paint Opel 500ml", Qty = 1, Price = 1000, Discount = 250, Amount = 750 },
            new ReceiptLine { Code = "2050", Name = "Cement 50kg", Qty = 1, Price = 1950, Discount = 25, Amount = 1925 },
        },
        GrossTotal = 3950, LineDiscount = 375, NetTotal = 3575,
        Payments = new() { ("Cash", 3575m, null) }, CashTendered = 5000, Change = 1425,
        TotalQty = 3, ItemCount = 3
    };

    private void ApplyToState()
    {
        // preview/test with the values on screen without saving
        if (PrinterLocation != null) Terminal.LocationID = PrinterLocation.LocationID;
    }

    [RelayCommand]
    private void Preview(object format)
    {
        ApplyToState();
        var saved = Session.Terminal; var savedS = Session.Settings;
        Session.Terminal = Terminal; Session.Settings = Shop;
        try { ReceiptService.Preview(Sample(), (Models.ReceiptFormat)int.Parse(format.ToString()!)); }
        catch (Exception ex) { Dialogs.Error(ex.Message); }
        finally { Session.Terminal = saved; Session.Settings = savedS; }
    }

    [RelayCommand]
    private void TestPrint(object format)
    {
        ApplyToState();
        var saved = Session.Terminal; var savedS = Session.Settings;
        Session.Terminal = Terminal; Session.Settings = Shop;
        try { ReceiptService.Print(Sample(), (Models.ReceiptFormat)int.Parse(format.ToString()!)); }
        catch (Exception ex) { Dialogs.Error(ex.Message, "Printer"); }
        finally { Session.Terminal = saved; Session.Settings = savedS; }
    }

    [RelayCommand]
    private void TestDrawer()
    {
        try { ReceiptPrinter.OpenCashDrawer(Terminal); } catch (Exception ex) { Dialogs.Error(ex.Message, "Cash drawer"); }
    }

    private static PosSettings Clone(PosSettings s) => PosSettings.FromDictionary(s.ToDictionary());
    private static Terminal CloneTerminal(Terminal t) => new()
    {
        PcRegistrationID = t.PcRegistrationID, MachineCode = t.MachineCode, Description = t.Description, LocationID = t.LocationID,
        ReceiptPrinter = t.ReceiptPrinter, ReceiptFormat = t.ReceiptFormat, PaperWidthMm = t.PaperWidthMm, CharsPerLine = t.CharsPerLine,
        PrintEngine = t.PrintEngine, A5Printer = t.A5Printer, A5Landscape = t.A5Landscape, AutoPrint = t.AutoPrint,
        PrintCopies = t.PrintCopies, OpenCashDrawer = t.OpenCashDrawer, DefaultTouchMode = t.DefaultTouchMode
    };
}

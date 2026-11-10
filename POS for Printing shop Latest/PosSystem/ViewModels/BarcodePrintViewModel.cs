using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;

namespace PosSystem.ViewModels;

/// <summary>One line in the sticker list (editable, like the old "miscellaneous" barcode form).</summary>
public sealed partial class LabelRowViewModel : ObservableObject
{
    public int ItemId { get; init; }
    public decimal StockOnHand { get; init; }
    public string? PriceUnit { get; init; }
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _weight = "";
    [ObservableProperty] private decimal _price;
    [ObservableProperty] private int _labels = 1;
    [ObservableProperty] private DateTime? _mfdDate;
    [ObservableProperty] private DateTime? _expDate;

    public LabelData ToLabel() => new()
    {
        Code = Code.Trim(), Description = Description, Weight = Weight, Price = Price, PriceUnit = PriceUnit,
        MfdDate = MfdDate, ExpDate = ExpDate
    };
}

/// <summary>
/// Inventory > Barcode stickers.
/// Scan / type an item code (F2 or empty Enter = search), set the number of stickers, then Preview or Print.
/// Sticker size, stickers across / down, margins and printer are saved in Settings (AppSettings table).
/// </summary>
public sealed partial class BarcodePrintViewModel : ViewModelBase
{
    private static readonly HashSet<string> SettingProps = new()
    {
        nameof(WidthIn), nameof(HeightIn), nameof(Landscape), nameof(LabelColumns), nameof(LabelRows), nameof(MarginTopMm),
        nameof(MarginLeftMm), nameof(GapHMm), nameof(GapVMm), nameof(Rotate), nameof(ShowShopName), nameof(ShowWeight),
        nameof(ShowPrice), nameof(ShowCode), nameof(ShowDates), nameof(SelectedRow)
    };

    public BarcodePrintViewModel()
    {
        var s = Session.Settings;
        _widthIn = s.LabelWidthIn; _heightIn = s.LabelHeightIn; _landscape = s.LabelLandscape;
        _labelColumns = s.LabelColumns; _labelRows = s.LabelRows; _marginTopMm = s.LabelMarginTopMm; _marginLeftMm = s.LabelMarginLeftMm;
        _gapHMm = s.LabelGapHMm; _gapVMm = s.LabelGapVMm; _printer = s.LabelPrinter; _rotate = s.LabelRotate;
        _showShopName = s.LabelShowShopName; _showWeight = s.LabelShowWeight; _showPrice = s.LabelShowPrice;
        _showCode = s.LabelShowCode; _showDates = s.LabelShowDates;
        Rows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TotalLabels));
    }

    public ObservableCollection<LabelRowViewModel> Rows { get; } = new();
    public IReadOnlyList<string> Printers { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<LookupItem> Categories { get; private set; } = Array.Empty<LookupItem>();

    [ObservableProperty] private LabelRowViewModel? _selectedRow;
    [ObservableProperty] private string _entryCode = "";
    [ObservableProperty] private LookupItem? _category;
    [ObservableProperty] private int _skipLabels;
    [ObservableProperty] private FrameworkElement? _previewLabel;
    [ObservableProperty] private string _pageInfo = "";

    // ---- sticker settings ----
    [ObservableProperty] private decimal _widthIn;
    [ObservableProperty] private decimal _heightIn;
    [ObservableProperty] private bool _landscape;
    [ObservableProperty] private int _labelColumns;   // stickers across
    [ObservableProperty] private int _labelRows;      // stickers down
    [ObservableProperty] private decimal _marginTopMm;
    [ObservableProperty] private decimal _marginLeftMm;
    [ObservableProperty] private decimal _gapHMm;
    [ObservableProperty] private decimal _gapVMm;
    [ObservableProperty] private string _printer;
    [ObservableProperty] private bool _rotate;
    [ObservableProperty] private bool _showShopName;
    [ObservableProperty] private bool _showWeight;
    [ObservableProperty] private bool _showPrice;
    [ObservableProperty] private bool _showCode;
    [ObservableProperty] private bool _showDates;

    public int TotalLabels => Rows.Sum(r => Math.Max(0, r.Labels));

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName != null && SettingProps.Contains(e.PropertyName)) RefreshPreview();
    }

    /// <summary>Current screen values as settings (used for preview / print / save).</summary>
    private PosSettings CurrentSettings()
    {
        var s = PosSettings.FromDictionary(Session.Settings.ToDictionary());
        s.LabelWidthIn = WidthIn; s.LabelHeightIn = HeightIn; s.LabelLandscape = Landscape;
        s.LabelColumns = Math.Max(1, LabelColumns); s.LabelRows = Math.Max(1, LabelRows);
        s.LabelMarginTopMm = MarginTopMm; s.LabelMarginLeftMm = MarginLeftMm; s.LabelGapHMm = GapHMm; s.LabelGapVMm = GapVMm;
        s.LabelPrinter = Printer ?? ""; s.LabelRotate = Rotate;
        s.LabelShowShopName = ShowShopName; s.LabelShowWeight = ShowWeight; s.LabelShowPrice = ShowPrice;
        s.LabelShowCode = ShowCode; s.LabelShowDates = ShowDates;
        return s;
    }

    private void RefreshPreview()
    {
        if (WidthIn <= 0 || HeightIn <= 0) return;
        var s = CurrentSettings();
        var (w, h) = LabelPrinter.LabelSize(s);
        var (pw, ph) = LabelPrinter.PageSize(s);
        var sample = SelectedRow?.ToLabel() ?? new LabelData { Code = "2050", Description = "CEMENT 50KG", Weight = "50kg", Price = 1950 };
        PreviewLabel = LabelPrinter.BuildLabel(sample, w, h, s, Session.Settings.ShopName, Session.Settings.CurrencySymbol);
        PageInfo = Lang.F("Sticker {0} x {1} mm, page {2} x {3} mm, {4} stickers per page",
            Math.Round(w * 25.4 / 96), Math.Round(h * 25.4 / 96), Math.Round(pw * 25.4 / 96), Math.Round(ph * 25.4 / 96),
            Math.Max(1, LabelColumns) * Math.Max(1, LabelRows));
    }

    public override async Task LoadAsync()
    {
        Printers = ReceiptPrinter.GetInstalledPrinters();
        OnPropertyChanged(nameof(Printers));
        await RunAsync(async () =>
        {
            Categories = await LookupRepository.GetCategoriesAsync();
            OnPropertyChanged(nameof(Categories));
        });
        RefreshPreview();
    }

    // ---------------- list ----------------
    private static readonly Regex WeightInName = new(@"(\d+(?:\.\d+)?)\s*(kg|g|ml|l)\b", RegexOptions.IgnoreCase);

    private LabelRowViewModel MakeRow(Item item, int labels)
    {
        var m = WeightInName.Match(item.DisplayName);
        var row = new LabelRowViewModel
        {
            ItemId = item.ItemID, StockOnHand = item.StockOnHand,
            PriceUnit = item.IsWeighted ? (item.UnitName ?? "kg").ToLowerInvariant() : null,
            Code = item.ItemCode ?? "", Description = item.DisplayName.ToUpperInvariant(),
            Weight = m.Success ? m.Value.Replace(" ", "") : "", Price = item.SellingPrice, Labels = labels
        };
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LabelRowViewModel.Labels)) OnPropertyChanged(nameof(TotalLabels));
            if (row == SelectedRow) RefreshPreview();
        };
        return row;
    }

    [RelayCommand]
    private async Task AddItem()
    {
        var code = EntryCode.Trim();
        EntryCode = "";
        await RunAsync(async () =>
        {
            Item? item = code.Length == 0 ? null : await ItemRepository.FindByCodeAsync(code, Session.LocationId);
            if (item == null)
            {
                var s = new ItemSearchViewModel(code);
                _ = s.LoadAsync();
                if (Dialogs.Show(s) != true || s.Result == null) return;
                item = s.Result;
            }
            var labels = Dialogs.Prompt("Barcode stickers", Lang.F("Number of stickers for {0}", item.DisplayName), "1", true);
            if (labels == null || !int.TryParse(labels, out var n) || n <= 0) return;
            var row = MakeRow(item, n);
            Rows.Add(row);
            SelectedRow = row;
        });
    }

    [RelayCommand]
    private async Task AddCategory()
    {
        if (Category == null) { Dialogs.Error("Select a category."); return; }
        await RunAsync(async () =>
        {
            var items = await ItemRepository.ListAsync(null, Category.Id, Session.LocationId);
            foreach (var i in items.Where(i => !i.IsService)) Rows.Add(MakeRow(i, 1));
            SelectedRow = Rows.LastOrDefault();
        });
    }

    /// <summary>Blank row for a sticker that is not an item (old "miscellaneous" barcode form).</summary>
    [RelayCommand]
    private void AddBlank()
    {
        var row = new LabelRowViewModel { Code = "", Description = "", Labels = 1 };
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LabelRowViewModel.Labels)) OnPropertyChanged(nameof(TotalLabels));
            if (row == SelectedRow) RefreshPreview();
        };
        Rows.Add(row);
        SelectedRow = row;
    }

    [RelayCommand]
    private void LabelsFromStock()
    {
        foreach (var r in Rows.Where(r => r.ItemId > 0)) r.Labels = (int)Math.Max(0, Math.Ceiling(r.StockOnHand));
        OnPropertyChanged(nameof(TotalLabels));
    }

    [RelayCommand]
    private void RemoveRow()
    {
        if (SelectedRow == null) return;
        Rows.Remove(SelectedRow);
        SelectedRow = Rows.LastOrDefault();
    }

    [RelayCommand]
    private void Clear()
    {
        if (Rows.Count > 0 && !Dialogs.Confirm("Clear the sticker list?")) return;
        Rows.Clear();
        SkipLabels = 0;
    }

    // ---------------- print ----------------
    private List<LabelData>? Expand()
    {
        var bad = Rows.FirstOrDefault(r => r.Labels > 0 && (string.IsNullOrWhiteSpace(r.Code) || Barcode128.Clean(r.Code) != r.Code.Trim()));
        if (bad != null) { Dialogs.Error(Lang.F("Line '{0}' needs a valid code (English letters and numbers only).", bad.Description)); return null; }
        var list = Rows.SelectMany(r => Enumerable.Repeat(r.ToLabel(), Math.Max(0, r.Labels))).ToList();
        if (list.Count == 0) { Dialogs.Error("Add items and the number of stickers first."); return null; }
        if (list.Count > 5000 && !Dialogs.Confirm(Lang.F("You are about to print {0} stickers. Continue?", list.Count))) return null;
        return list;
    }

    [RelayCommand]
    private void Preview()
    {
        var list = Expand();
        if (list == null) return;
        try { LabelPrinter.Preview(list, CurrentSettings(), Session.Settings.ShopName, Session.Settings.CurrencySymbol, SkipLabels); }
        catch (Exception ex) { Dialogs.Error(ex.Message, "Preview"); }
    }

    [RelayCommand]
    private void Print()
    {
        var list = Expand();
        if (list == null) return;
        try
        {
            LabelPrinter.Print(list, CurrentSettings(), Session.Settings.ShopName, Session.Settings.CurrencySymbol, SkipLabels);
            Dialogs.Info(Lang.F("{0} stickers sent to the printer.", list.Count));
        }
        catch (Exception ex) { Dialogs.Error(ex.Message, "Printer"); }
    }

    [RelayCommand]
    private async Task SaveSettings()
    {
        if (WidthIn <= 0 || HeightIn <= 0 || LabelColumns <= 0 || LabelRows <= 0)
        { Dialogs.Error("Sticker size, stickers across and stickers down must be greater than zero."); return; }
        await RunAsync(async () =>
        {
            await SettingsService.SaveSettingsAsync(CurrentSettings());
            Dialogs.Info("Sticker settings saved.");
        });
    }

    [RelayCommand]
    private void DefaultSize()
    {
        WidthIn = 2.0m; HeightIn = 1.5m; Landscape = true; Rotate = false;
    }
}

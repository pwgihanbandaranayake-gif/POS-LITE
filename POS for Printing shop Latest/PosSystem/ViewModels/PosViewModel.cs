using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace PosSystem.ViewModels;

/// <summary>
/// Billing screen logic. One view model drives BOTH the keyboard/scanner layout and the touch layout,
/// so every rule (discounts, tax, permissions, hold, payment) behaves exactly the same in both.
/// </summary>
public sealed partial class PosViewModel : ViewModelBase
{
    private readonly DispatcherTimer _clock;
    private DateTime _billStart = DateTime.Now;

    public event Action? FocusScanRequested;
    public event Action? CloseRequested;

    public PosViewModel()
    {
        _isTouchMode = Session.Terminal.DefaultTouchMode;
        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => Now = DateTime.Now;
        _clock.Start();
        Lines.CollectionChanged += (_, _) => { Renumber(); Recalculate(); };
    }

    // ---------------- header info ----------------
    public string ShopName => Session.Settings.ShopName;
    public string Cashier => Session.FullName;
    public string TerminalName => Session.TerminalName;
    public string Currency => Session.Settings.CurrencySymbol;
    [ObservableProperty] private string _locationName = "";
    [ObservableProperty] private DateTime _now = DateTime.Now;
    [ObservableProperty] private int _heldCount;

    // ---------------- cart ----------------
    public ObservableCollection<CartLineViewModel> Lines { get; } = new();
    [ObservableProperty] private CartLineViewModel? _selectedLine;
    [ObservableProperty] private string _scanText = "";
    [ObservableProperty] private CustomerInfoDto? _customer;
    [ObservableProperty] private bool _isTouchMode;
    [ObservableProperty] private DiscountType _billDiscountType;
    [ObservableProperty] private decimal _billDiscountValue;

    [ObservableProperty] private decimal _grossTotal;
    [ObservableProperty] private decimal _lineDiscountTotal;
    [ObservableProperty] private decimal _billDiscount;
    [ObservableProperty] private decimal _taxTotal;
    [ObservableProperty] private decimal _roundOff;
    [ObservableProperty] private decimal _netTotal;
    [ObservableProperty] private decimal _totalQty;
    [ObservableProperty] private int _itemCount;
    public decimal Savings => LineDiscountTotal + BillDiscount;
    public string BillDiscountText => BillDiscountType == DiscountType.Percent ? $"Bill discount {BillDiscountValue:0.##}%" : "Bill discount";
    public bool TaxEnabled => Session.Settings.TaxEnabled;
    public string TaxLabel => Session.Settings.PricesIncludeTax ? "Tax (included)" : "Tax";

    // last action feedback (shown in the display panel)
    [ObservableProperty] private string _lastItemName = "Ready to scan";
    [ObservableProperty] private string _lastItemInfo = "";
    [ObservableProperty] private decimal _lastChange;
    [ObservableProperty] private string? _lastInvoiceNo;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _statusIsWarning;
    private int? _lastInvoiceId;

    // ---------------- touch ----------------
    public ObservableCollection<LookupItem> Categories { get; } = new();
    public ObservableCollection<Item> TouchItems { get; } = new();
    [ObservableProperty] private LookupItem? _selectedCategory;
    [ObservableProperty] private string _numpadText = "";

    public bool HasLines => Lines.Count > 0;

    // =====================================================================
    public override async Task LoadAsync()
    {
        LocationName = Session.LocationName;
        if (!await ShiftService.HasOpenShiftAsync())
        {
            var vm = new OpenSessionViewModel();
            if (Dialogs.Show(vm) != true) { CloseRequested?.Invoke(); return; }
            await RunAsync(() => ShiftService.OpenAsync(vm.Amount));
            if (vm.Amount > 0) ReceiptService.OpenDrawer();
        }
        await RunAsync(async () =>
        {
            Categories.Clear();
            Categories.Add(new LookupItem(0, "#F6C343", "Favourites"));
            foreach (var c in await ItemRepository.GetTouchCategoriesAsync()) Categories.Add(c);
            SelectedCategory = Categories.FirstOrDefault();
            await RefreshHeldCountAsync();
        });
        FocusScanRequested?.Invoke();
    }

    partial void OnSelectedCategoryChanged(LookupItem? value) => _ = LoadTouchItemsAsync();

    [RelayCommand] private void SelectCategory(LookupItem c) => SelectedCategory = c;

    private async Task LoadTouchItemsAsync()
    {
        if (SelectedCategory == null) return;
        var list = await ItemRepository.GetTouchItemsAsync(SelectedCategory.Id == 0 ? null : SelectedCategory.Id, Session.LocationId);
        TouchItems.Clear();
        foreach (var i in list) TouchItems.Add(i);
    }

    private async Task RefreshHeldCountAsync() => HeldCount = (await HoldRepository.ListAsync(Session.LocationId)).Count;

    private void Renumber() { int n = 0; foreach (var l in Lines) l.LineNo = ++n; OnPropertyChanged(nameof(HasLines)); }

    public void Recalculate()
    {
        var s = Session.Settings;
        var t = SaleCalculator.Calculate(Lines.Select(l => l.ToInput()).ToList(), BillDiscountType, BillDiscountValue,
                                         new TaxConfig(s.TaxEnabled, s.PricesIncludeTax), s.RoundTo);
        for (int i = 0; i < Lines.Count; i++) Lines[i].Apply(t.Lines[i]);
        GrossTotal = t.GrossTotal; LineDiscountTotal = t.LineDiscountTotal; BillDiscount = t.BillDiscount;
        TaxTotal = t.TaxTotal; RoundOff = t.RoundOff; NetTotal = t.NetTotal; TotalQty = t.TotalQuantity; ItemCount = Lines.Count;
        OnPropertyChanged(nameof(Savings));
        OnPropertyChanged(nameof(BillDiscountText));
    }

    private void Status(string? msg, bool warning = false) { StatusMessage = msg; StatusIsWarning = warning; }

    // =====================================================================
    //  Adding items
    // =====================================================================
    // Fast scanners fire Enter faster than the database answers: queue the codes and process them in order.
    private readonly Queue<string> _scanQueue = new();
    private bool _processingScans;

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Scan()
    {
        var text = (ScanText ?? "").Trim();
        ScanText = "";
        if (text.Length == 0) return;
        _scanQueue.Enqueue(text);
        if (_processingScans) return;
        _processingScans = true;
        try
        {
            while (_scanQueue.Count > 0) await ProcessScanAsync(_scanQueue.Dequeue());
        }
        finally { _processingScans = false; }
        FocusScanRequested?.Invoke();
    }

    private async Task ProcessScanAsync(string text)
    {
        decimal qty = 1; bool qtyGiven = false;
        var star = text.IndexOf('*');
        if (star > 0 && decimal.TryParse(text[..star], out var q) && q > 0) { qty = q; text = text[(star + 1)..].Trim(); qtyGiven = true; }

        await RunAsync(async () =>
        {
            var item = await ItemRepository.FindByCodeAsync(text, Session.LocationId);
            if (item == null)
            {
                _scanQueue.Clear();
                item = PickFromSearch(text);
                if (item == null) { Status($"'{text}' not found", true); return; }
            }
            await AddItemAsync(item, qty, qtyGiven);
        });
    }

    private Item? PickFromSearch(string? text)
    {
        var vm = new ItemSearchViewModel(text);
        _ = vm.LoadAsync();
        return Dialogs.Show(vm) == true ? vm.Result : null;
    }

    [RelayCommand]
    private async Task SearchItem()
    {
        var item = PickFromSearch(ScanText);
        ScanText = "";
        if (item != null) await RunAsync(() => AddItemAsync(item, 1, false));
        FocusScanRequested?.Invoke();
    }

    [RelayCommand]
    private async Task TouchAdd(Item item)
    {
        decimal qty = 1; bool given = false;
        if (decimal.TryParse(NumpadText, out var q) && q > 0) { qty = q; given = true; }
        NumpadText = "";
        await RunAsync(() => AddItemAsync(item, qty, given));
    }

    private async Task AddItemAsync(Item item, decimal qty, bool qtyGiven)
    {
        LastChange = 0;
        if (Lines.Count == 0) _billStart = DateTime.Now;

        if (item.IsWeighted && !qtyGiven)
        {
            var w = Dialogs.Prompt("Weight / quantity", $"{item.DisplayName} - enter weight ({item.UnitName ?? "kg"})", null, true);
            if (w == null || !decimal.TryParse(w, out qty) || qty <= 0) return;
        }

        // price: one price -> use it; several batch prices -> let cashier pick (MRP batches)
        decimal price = item.SellingPrice; int? stockId = null;
        if (!item.IsService)
        {
            var batches = await ItemRepository.GetBatchesAsync(item.ItemID, Session.LocationId, true);
            var prices = batches.Select(b => b.SellingPrice).Distinct().ToList();
            if (prices.Count == 1) { price = prices[0]; stockId = batches.First().ItemStockID; }
            else if (prices.Count > 1)
            {
                var pp = new PricePickerViewModel(item.DisplayName, batches);
                if (Dialogs.Show(pp) != true || pp.Selected == null) return;
                price = pp.Selected.Price; stockId = pp.Selected.StockId;
            }

            var inCart = Lines.Where(l => l.ItemId == item.ItemID).Sum(l => l.Qty);
            if (item.StockOnHand - inCart - qty < 0)
            {
                if (!Session.Settings.AllowNegativeStock)
                    throw new AppException($"Not enough stock for '{item.DisplayName}'. Available: {item.StockOnHand - inCart:0.###}");
                Status($"Low stock: {item.DisplayName} (system shows {item.StockOnHand:0.###})", true);
            }
            else Status(null);
        }

        var existing = Lines.FirstOrDefault(l => l.ItemId == item.ItemID && l.UnitPrice == price && l.ItemStockId == stockId
                                                && string.IsNullOrWhiteSpace(l.LineNote)      // lines with a note (2x4, 4x4 ...) stay separate
                                                && l.DiscountType == DiscountType.None && !item.IsWeighted);
        if (existing != null)
        {
            existing.Qty += qty;
            SelectedLine = existing;
            Recalculate();
        }
        else
        {
            var line = new CartLineViewModel
            {
                ItemId = item.ItemID, ItemStockId = stockId, Code = item.ItemCode ?? "", Name = item.DisplayName, Unit = item.UnitName,
                TaxId = item.TaxID, TaxRate = item.TaxRate, AllowDiscount = item.AllowDiscount, AllowPriceChange = item.AllowPriceChange,
                IsService = item.IsService, OriginalPrice = price, Qty = qty, UnitPrice = price
            };
            Lines.Add(line);
            SelectedLine = line;
        }
        LastItemName = item.DisplayName;
        LastItemInfo = $"{qty:0.###} x {price:N2}";
    }

    // =====================================================================
    //  Line operations
    // =====================================================================
    [RelayCommand]
    private void MoveSelection(string delta)
    {
        if (Lines.Count == 0) return;
        var i = SelectedLine == null ? Lines.Count - 1 : Lines.IndexOf(SelectedLine) + int.Parse(delta);
        SelectedLine = Lines[Math.Clamp(i, 0, Lines.Count - 1)];
    }

    [RelayCommand]
    private async Task ChangeQty()
    {
        var l = SelectedLine ?? Lines.LastOrDefault();
        if (l == null) return;
        var s = Dialogs.Prompt("Quantity", l.Name, l.Qty.ToString("0.###"), true);
        if (s != null && decimal.TryParse(s, out var q))
        {
            if (q <= 0) await VoidLine();
            else { l.Qty = q; Recalculate(); }
        }
        FocusScanRequested?.Invoke();
    }

    [RelayCommand]
    private async Task IncreaseQty() { var l = SelectedLine; if (l == null) return; l.Qty += 1; Recalculate(); await Task.CompletedTask; }

    [RelayCommand]
    private async Task DecreaseQty()
    {
        var l = SelectedLine; if (l == null) return;
        if (l.Qty - 1 <= 0) { await VoidLine(); return; }
        if (await OverrideService.AuthorizeAsync(Perm.PosVoidLine, $"Reduce quantity of {l.Name}") == null) return;
        l.Qty -= 1; Recalculate();
    }

    [RelayCommand]
    private void SetQtyFromNumpad()
    {
        if (SelectedLine == null || !decimal.TryParse(NumpadText, out var q) || q <= 0) return;
        SelectedLine.Qty = q; NumpadText = ""; Recalculate();
    }

    [RelayCommand]
    private async Task VoidLine()
    {
        var l = SelectedLine ?? Lines.LastOrDefault();
        if (l == null) return;
        var approver = await OverrideService.AuthorizeAsync(Perm.PosVoidLine, $"Void {l.Name}");
        if (approver == null) return;
        if (!Dialogs.Confirm($"Remove '{l.Name}' ({l.Qty:0.###} x {l.UnitPrice:N2}) from the bill?")) return;
        var idx = Lines.IndexOf(l);
        Lines.Remove(l);
        SelectedLine = Lines.Count == 0 ? null : Lines[Math.Min(idx, Lines.Count - 1)];
        await AuthService.AuditAsync("VOID_LINE", null, $"{l.Code} {l.Name} qty {l.Qty} @ {l.UnitPrice}", approver == Session.UserId ? null : approver);
        FocusScanRequested?.Invoke();
    }

    [RelayCommand]
    private async Task LineDiscount()
    {
        var l = SelectedLine ?? Lines.LastOrDefault();
        if (l == null) return;
        if (!l.AllowDiscount) { Dialogs.Error($"Discount is not allowed for '{l.Name}'."); return; }
        if (await OverrideService.AuthorizeAsync(Perm.PosLineDiscount, $"Item discount on {l.Name}") == null) return;

        var vm = new DiscountViewModel("Item discount", $"{l.Name}  ({l.Qty:0.###} x {l.UnitPrice:N2})", l.Gross, l.DiscountType, l.DiscountValue);
        if (Dialogs.Show(vm) != true) { FocusScanRequested?.Invoke(); return; }
        if (!await CheckDiscountLimitAsync(vm.PercentEquivalent, Perm.PosLineDiscount)) return;
        l.DiscountType = vm.Type; l.DiscountValue = vm.Value;
        Recalculate();
        FocusScanRequested?.Invoke();
    }

    [RelayCommand]
    private async Task BillDiscountEdit()
    {
        if (Lines.Count == 0) return;
        if (await OverrideService.AuthorizeAsync(Perm.PosBillDiscount, "Bill discount") == null) return;
        var subTotal = GrossTotal - LineDiscountTotal;
        var vm = new DiscountViewModel("Bill discount", $"Bill sub total after item discounts", subTotal, BillDiscountType, BillDiscountValue);
        if (Dialogs.Show(vm) != true) { FocusScanRequested?.Invoke(); return; }
        if (!await CheckDiscountLimitAsync(vm.PercentEquivalent, Perm.PosBillDiscount)) return;
        BillDiscountType = vm.Type; BillDiscountValue = vm.Value;
        Recalculate();
        FocusScanRequested?.Invoke();
    }

    /// <summary>Role limit (UserGroups.MaxDiscountPercent, 0 = no limit). Above it a supervisor must approve.</summary>
    private async Task<bool> CheckDiscountLimitAsync(decimal percent, string permission)
    {
        var limit = Session.MaxDiscountPercent;
        if (Session.IsSuperUser || limit <= 0 || percent <= limit) return true;
        var vm = new ManagerOverrideViewModel(permission, $"Discount {percent:0.##}% is above your limit of {limit:0.##}%");
        if (Dialogs.Show(vm) != true) return false;
        await AuthService.AuditAsync("DISCOUNT_OVER_LIMIT", null, $"{percent:0.##}%", vm.ApprovedBy);
        return true;
    }

    /// <summary>Ctrl+N / double-click: text added after the item name on the bill, e.g. "2x4".</summary>
    [RelayCommand]
    private void EditLineNote()
    {
        var l = SelectedLine ?? Lines.LastOrDefault();
        if (l == null) return;
        var s = Dialogs.Prompt("Line note", Lang.F("Text printed after '{0}' on the bill, e.g. 2x4", l.Name), l.LineNote ?? "");
        if (s == null) { FocusScanRequested?.Invoke(); return; }
        s = s.Trim();
        if (s.StartsWith('(') && s.EndsWith(')')) s = s[1..^1].Trim();       // "(2x4)" and "2x4" both print as (2x4)
        if (s.Length > 100) s = s[..100];
        l.LineNote = s.Length == 0 ? null : s;
        FocusScanRequested?.Invoke();
    }

    [RelayCommand]
    private async Task PriceChange()
    {
        var l = SelectedLine ?? Lines.LastOrDefault();
        if (l == null) return;
        var approver = l.AllowPriceChange ? Session.UserId : await OverrideService.AuthorizeAsync(Perm.PosPriceOverride, $"Change price of {l.Name}");
        if (approver == null) return;
        var s = Dialogs.Prompt("Change price", $"{l.Name} - current {l.UnitPrice:N2}", l.UnitPrice.ToString("0.00"), true);
        if (s == null || !decimal.TryParse(s, out var p) || p < 0) return;
        await AuthService.AuditAsync("PRICE_CHANGE", null, $"{l.Code} {l.UnitPrice} -> {p}", approver == Session.UserId ? null : approver);
        l.UnitPrice = Money.R(p);
        Recalculate();
        FocusScanRequested?.Invoke();
    }

    // =====================================================================
    //  Bill operations
    // =====================================================================
    [RelayCommand]
    private void SelectCustomer()
    {
        var vm = new CustomerPickerViewModel();
        _ = vm.LoadAsync();
        if (Dialogs.Show(vm) == true)
            Customer = vm.Cleared ? null : vm.Result;
        FocusScanRequested?.Invoke();
    }

    [RelayCommand]
    private async Task CancelBill()
    {
        if (Lines.Count == 0) return;
        var approver = await OverrideService.AuthorizeAsync(Perm.PosCancelBill, "Cancel current bill");
        if (approver == null) return;
        if (!Dialogs.Confirm("Cancel the whole bill?")) return;
        await AuthService.AuditAsync("BILL_CLEAR", null, $"{Lines.Count} lines, total {NetTotal:N2}", approver == Session.UserId ? null : approver);
        ClearBill();
    }

    private void ClearBill()
    {
        Lines.Clear();
        Customer = null;
        BillDiscountType = DiscountType.None; BillDiscountValue = 0;
        Recalculate();
        Status(null);
        FocusScanRequested?.Invoke();
    }

    [RelayCommand]
    private async Task Hold()
    {
        if (Lines.Count == 0) return;
        if (await OverrideService.AuthorizeAsync(Perm.PosHold, "Hold bill") == null) return;
        var note = Dialogs.Prompt("Hold bill", "Note (optional) e.g. customer name or basket colour", Customer?.Name);
        if (note == null) return;
        await RunAsync(async () =>
        {
            var dto = new HeldBillDto
            {
                LocationID = Session.LocationId, CustomerID = Customer?.CustomerID, CustomerName = Customer?.Name,
                BillDiscountType = (int)BillDiscountType, BillDiscountValue = BillDiscountValue, TotalAmount = NetTotal, Note = note,
                Items = Lines.Select(l => new HeldBillItemDto
                {
                    LineNumber = l.LineNo, ItemID = l.ItemId, ItemStockID = l.ItemStockId, Quantity = l.Qty, Rate = l.UnitPrice,
                    DiscountType = (int)l.DiscountType, DiscountValue = l.DiscountValue, LineNote = l.LineNote
                }).ToList()
            };
            var no = await HoldRepository.HoldAsync(dto, Session.UserId, Session.TerminalId);
            ClearBill();
            LastItemName = $"Bill held as {no}";
            LastItemInfo = "Press F7 to recall";
            await RefreshHeldCountAsync();
        });
    }

    [RelayCommand]
    private async Task Recall()
    {
        var vm = new HeldBillsViewModel();
        await vm.LoadAsync();
        if (vm.Bills.Count == 0) { Dialogs.Info("There are no held bills."); return; }
        if (Dialogs.Show(vm) != true || vm.ChosenId == null) { await RefreshHeldCountAsync(); return; }

        if (Lines.Count > 0)
        {
            if (!Dialogs.Confirm("The current bill will be held first. Continue?")) return;
            await Hold();
            if (Lines.Count > 0) return;
        }

        await RunAsync(async () =>
        {
            var h = await HoldRepository.TakeAsync(vm.ChosenId.Value);
            if (h == null) { Dialogs.Error("This bill was already recalled on another counter."); return; }
            foreach (var i in h.Items.OrderBy(x => x.LineNumber))
            {
                var item = await ItemRepository.GetByIdAsync(i.ItemID, Session.LocationId);
                if (item == null) continue;
                Lines.Add(new CartLineViewModel
                {
                    ItemId = item.ItemID, ItemStockId = i.ItemStockID, Code = item.ItemCode ?? "", Name = item.DisplayName, Unit = item.UnitName,
                    TaxId = item.TaxID, TaxRate = item.TaxRate, AllowDiscount = item.AllowDiscount, AllowPriceChange = item.AllowPriceChange,
                    IsService = item.IsService, OriginalPrice = i.Rate, Qty = i.Quantity, UnitPrice = i.Rate,
                    DiscountType = (DiscountType)i.DiscountType, DiscountValue = i.DiscountValue, LineNote = i.LineNote
                });
            }
            BillDiscountType = (DiscountType)h.BillDiscountType; BillDiscountValue = h.BillDiscountValue;
            Customer = h.CustomerID.HasValue ? await LookupRepository.GetCustomerAsync(h.CustomerID.Value) : null;
            Recalculate();
            SelectedLine = Lines.LastOrDefault();
            LastItemName = $"Recalled {h.HoldNo}";
            LastItemInfo = h.Note ?? "";
            await RefreshHeldCountAsync();
        });
        FocusScanRequested?.Invoke();
    }

    // =====================================================================
    //  Payment
    // =====================================================================
    [RelayCommand]
    private async Task Pay()
    {
        if (Lines.Count == 0) { FocusScanRequested?.Invoke(); return; }
        Recalculate();
        var t = Session.Terminal;
        var pay = new PaymentViewModel(NetTotal, Session.PaymentMethods, (ReceiptFormat)t.ReceiptFormat, t.AutoPrint, EnsureCustomer, Currency);
        if (Dialogs.Show(pay) != true) { FocusScanRequested?.Invoke(); return; }

        var req = new SaleRequest
        {
            LocationId = Session.LocationId, TerminalId = Session.TerminalId, UserId = Session.UserId, SessionId = Session.ShiftId,
            CustomerId = Customer?.CustomerID, CustomerName = Customer?.Name, StartTime = _billStart,
            BillDiscountType = BillDiscountType, BillDiscountValue = BillDiscountValue, BillDiscountAmount = BillDiscount,
            GrossTotal = GrossTotal, LineDiscountTotal = LineDiscountTotal, TaxTotal = TaxTotal, RoundOff = RoundOff, NetTotal = NetTotal,
            PricesIncludeTax = Session.Settings.PricesIncludeTax, AllowNegativeStock = Session.Settings.AllowNegativeStock,
            CashTendered = pay.CashTendered, Change = pay.Change, Payments = pay.ToPayments(),
            Lines = Lines.Select(l => new SaleLineDto
            {
                LineNumber = l.LineNo, ItemId = l.ItemId, ItemStockId = l.ItemStockId, ItemCode = l.Code, ItemName = l.Name, LineNote = l.LineNote,
                Quantity = l.Qty, UnitPrice = l.UnitPrice, DiscountType = l.DiscountType, DiscountValue = l.DiscountValue,
                LineDiscount = l.LineDiscount, BillDiscountShare = l.BillShare, TaxId = l.TaxId, TaxRate = l.TaxRate,
                TaxAmount = l.Tax, Gross = l.Gross, Net = l.Net, Total = l.Total
            }).ToList()
        };

        SaleResult? result = null;
        if (!await RunAsync(async () => result = await SalesService.CompleteSaleAsync(req)) || result == null) return;

        if (pay.OpensDrawer) ReceiptService.OpenDrawer();
        _lastInvoiceId = result.InvoiceId;
        LastInvoiceNo = result.InvoiceNo;
        var change = pay.Change;
        ClearBill();
        LastChange = change;
        LastItemName = change > 0 ? $"Change {Currency} {change:N2}" : "Thank you!";
        LastItemInfo = $"Bill {result.InvoiceNo} saved";

        if (pay.PrintMode != 0)
            await RunAsync(() => ReceiptService.PrintInvoiceAsync(result.InvoiceId, false, (ReceiptFormat)pay.PrintMode));
        FocusScanRequested?.Invoke();
    }

    private CustomerInfoDto? EnsureCustomer()
    {
        if (!Session.Has(Perm.PosCreditSale)) { Dialogs.Error("You do not have permission for credit sales."); return null; }
        if (Customer == null) SelectCustomer();
        if (Customer == null) Dialogs.Error("Select a customer for credit sales.");
        return Customer;
    }

    // =====================================================================
    //  Other till functions
    // =====================================================================
    [RelayCommand]
    private async Task Reprint()
    {
        if (await OverrideService.AuthorizeAsync(Perm.PosReprint, "Re-print bill") == null) return;
        var no = Dialogs.Prompt("Re-print", "Invoice number (leave empty for the last bill)", LastInvoiceNo);
        if (no == null) return;
        await RunAsync(async () =>
        {
            int? id = string.IsNullOrWhiteSpace(no) ? _lastInvoiceId ?? await SalesRepository.GetLastInvoiceIdAsync(Session.TerminalId) : await SalesRepository.FindInvoiceIdAsync(no);
            if (id == null) { Dialogs.Error("Invoice not found."); return; }
            await ReceiptService.PrintInvoiceAsync(id.Value, true, null, 1);
            await AuthService.AuditAsync("REPRINT", no, null);
        });
        FocusScanRequested?.Invoke();
    }

    [RelayCommand]
    private async Task OpenDrawer()
    {
        var approver = await OverrideService.AuthorizeAsync(Perm.PosOpenDrawer, "Open cash drawer (no sale)");
        if (approver == null) return;
        ReceiptService.OpenDrawer();
        await AuthService.AuditAsync("NO_SALE", null, "Drawer opened", approver == Session.UserId ? null : approver);
        FocusScanRequested?.Invoke();
    }

    [RelayCommand]
    private async Task ChangeLocation()
    {
        if (Lines.Count > 0) { Dialogs.Error("Finish or hold the current bill before changing location."); return; }
        if (await OverrideService.AuthorizeAsync(Perm.PosChangeLocation, "Change billing location") == null) return;
        var items = Session.Locations.Select(l => new LookupItem(l.LocationID, l.LocationCode, l.LocationName)).ToList();
        var vm = new PickListViewModel("Billing location", items, Session.LocationId);
        if (Dialogs.Show(vm) != true || vm.Selected == null) return;
        Session.SetLocation(vm.Selected.Id, vm.Selected.Code, vm.Selected.Name);
        LocationName = vm.Selected.Name;
        await LoadTouchItemsAsync();
        await RefreshHeldCountAsync();
        Status($"Now billing from {vm.Selected.Name}. Stock will be deducted from this location.");
        FocusScanRequested?.Invoke();
    }

    [RelayCommand]
    private async Task Return()
    {
        if (await OverrideService.AuthorizeAsync(Perm.ReturnsSales, "Sales return") == null) return;
        var vm = new SalesReturnViewModel();
        Dialogs.Show(vm);
        FocusScanRequested?.Invoke();
    }

    [RelayCommand]
    private async Task CashEntry(string kind)
    {
        var paidOut = kind == "out";
        if (await OverrideService.AuthorizeAsync(paidOut ? Perm.CashPaidOut : Perm.CashReceived, paidOut ? "Paid out" : "Receive money") == null) return;
        var vm = new CashEntryViewModel();
        vm.IsPaidOut = paidOut;
        await vm.LoadAsync();
        Dialogs.Show(vm);
        FocusScanRequested?.Invoke();
    }

    [RelayCommand] private void ToggleMode() { IsTouchMode = !IsTouchMode; FocusScanRequested?.Invoke(); }
    [RelayCommand] private void Help() { Dialogs.Show(new ShortcutHelpViewModel()); FocusScanRequested?.Invoke(); }

    [RelayCommand]
    private void Exit()
    {
        if (Lines.Count > 0) { Dialogs.Error("Finish, hold or cancel the current bill first."); return; }
        _clock.Stop();
        CloseRequested?.Invoke();
    }

    // numpad (touch)
    [RelayCommand] private void Numpad(string key)
    {
        if (key == "C") NumpadText = "";
        else if (key == "<") NumpadText = NumpadText.Length > 0 ? NumpadText[..^1] : "";
        else if (key == "." && NumpadText.Contains('.')) return;
        else NumpadText += key;
    }
}

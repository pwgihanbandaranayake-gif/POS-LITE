using System.Collections.ObjectModel;

namespace PosSystem.ViewModels;

/* ---------------- Item search (F2) ---------------- */
public sealed partial class ItemSearchViewModel : DialogViewModel
{
    private CancellationTokenSource? _cts;

    public ItemSearchViewModel(string? initial)
    {
        Title = "Find item"; _searchText = initial ?? "";
    }

    public ObservableCollection<Item> Results { get; } = new();
    [ObservableProperty] private string _searchText;
    [ObservableProperty] private Item? _selected;
    public Item? Result { get; private set; }

    partial void OnSearchTextChanged(string value) => _ = SearchDebouncedAsync();

    public override async Task LoadAsync() => await SearchAsync();

    private async Task SearchDebouncedAsync()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        try { await Task.Delay(250, cts.Token); } catch (TaskCanceledException) { return; }
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        var list = await ItemRepository.SearchAsync(SearchText, Session.LocationId, 150);
        Results.Clear();
        foreach (var i in list) Results.Add(i);
        Selected = Results.FirstOrDefault();
    }

    public void Move(int delta)
    {
        if (Results.Count == 0) return;
        var i = Selected == null ? 0 : Results.IndexOf(Selected) + delta;
        Selected = Results[Math.Clamp(i, 0, Results.Count - 1)];
    }

    [RelayCommand]
    private void Choose()
    {
        if (Selected == null) return;
        Result = Selected;
        Close(true);
    }
}

/* ---------------- Batch price picker ---------------- */
public sealed partial class PricePickerViewModel : DialogViewModel
{
    public PricePickerViewModel(string itemName, IEnumerable<StockBatch> batches)
    {
        Title = "Select price";
        ItemName = itemName;
        Options = batches.GroupBy(b => b.SellingPrice).Select(g => new PriceOption(g.Key, g.Sum(b => b.CurrentQuantity),
            g.Min(b => b.ExpDate), g.OrderBy(b => b.ExpDate ?? DateTime.MaxValue).First().ItemStockID)).OrderBy(o => o.Price).ToList();
        _selected = Options.FirstOrDefault();
    }
    public string ItemName { get; }
    public IReadOnlyList<PriceOption> Options { get; }
    [ObservableProperty] private PriceOption? _selected;
    public sealed record PriceOption(decimal Price, decimal Qty, DateTime? Expiry, int StockId)
    {
        public string ExpiryText => Expiry?.ToString("yyyy-MM-dd") ?? "-";
    }
    [RelayCommand] private void Choose() { if (Selected != null) Close(true); }
}

/* ---------------- Discount (line or bill) ---------------- */
public sealed partial class DiscountViewModel : DialogViewModel
{
    public DiscountViewModel(string title, string subject, decimal baseAmount, DiscountType type, decimal value)
    {
        Title = title; Subject = subject; BaseAmount = baseAmount;
        _isPercent = type != DiscountType.Amount;
        _valueText = value > 0 ? value.ToString("0.##") : "";
    }
    public string Subject { get; }
    public decimal BaseAmount { get; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Discount), nameof(After))] private bool _isPercent;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Discount), nameof(After))] private string _valueText;

    public DiscountType Type => Value <= 0 ? DiscountType.None : IsPercent ? DiscountType.Percent : DiscountType.Amount;
    public decimal Value => decimal.TryParse(ValueText, out var v) ? v : 0;
    public decimal Discount => SaleCalculator.CalcDiscount(BaseAmount, IsPercent ? DiscountType.Percent : DiscountType.Amount, Value);
    public decimal After => BaseAmount - Discount;
    public decimal PercentEquivalent => BaseAmount == 0 ? 0 : Discount * 100m / BaseAmount;

    [RelayCommand] private void Quick(string pct) { IsPercent = true; ValueText = pct; }
    [RelayCommand] private void Remove() { ValueText = ""; Close(true); }

    [RelayCommand]
    private void Apply()
    {
        if (Value < 0) { Dialogs.Error("Discount cannot be negative."); return; }
        if (IsPercent && Value > 100) { Dialogs.Error("Percentage cannot be more than 100."); return; }
        if (!IsPercent && Value > BaseAmount) { Dialogs.Error($"Discount cannot exceed {BaseAmount:N2}."); return; }
        Close(true);
    }
}

/* ---------------- Payment (multi tender) ---------------- */
public sealed partial class TenderViewModel : ObservableObject
{
    public PaymentMethod Method { get; init; } = new();
    public PaymentKind Kind => (PaymentKind)Method.Kind;
    [ObservableProperty] private decimal _amount;
    public string? Reference { get; init; }
    public string? CardType { get; init; }
    public int? CreditNoteId { get; init; }
    public string Display => Method.Name + (string.IsNullOrEmpty(Reference) ? "" : $"  ({Reference})");
}

public sealed partial class PaymentViewModel : DialogViewModel
{
    private readonly Func<CustomerInfoDto?> _ensureCustomer;

    public PaymentViewModel(decimal due, IReadOnlyList<PaymentMethod> methods, ReceiptFormat defaultFormat,
                            bool autoPrint, Func<CustomerInfoDto?> ensureCustomer, string currency)
    {
        Title = "Payment";
        Due = due; Methods = methods; _ensureCustomer = ensureCustomer; Currency = currency;
        _selectedMethod = methods.FirstOrDefault(m => m.Kind == (int)PaymentKind.Cash) ?? methods.FirstOrDefault();
        _printMode = autoPrint ? (int)defaultFormat : 0;
        _amountText = due.ToString("0.00");
        Recalc();
    }

    public decimal Due { get; }
    public string Currency { get; }
    public IReadOnlyList<PaymentMethod> Methods { get; }
    public ObservableCollection<TenderViewModel> Tenders { get; } = new();
    [ObservableProperty] private PaymentMethod? _selectedMethod;
    [ObservableProperty] private TenderViewModel? _selectedTender;
    [ObservableProperty] private string _amountText;
    /// <summary>0 = do not print, 1 = receipt (thermal / dot-matrix roll), 3 = A5 half sheet.</summary>
    [ObservableProperty] private int _printMode;

    [ObservableProperty] private decimal _paid;
    [ObservableProperty] private decimal _balance;
    [ObservableProperty] private decimal _change;
    [ObservableProperty] private bool _isSettled;

    public decimal CashTendered => Tenders.Where(t => t.Kind == PaymentKind.Cash).Sum(t => t.Amount);
    public IEnumerable<decimal> QuickCash
    {
        get
        {
            var notes = new[] { 100m, 500m, 1000m, 2000m, 5000m };
            var bal = Balance > 0 ? Balance : Due;
            return notes.Select(n => Math.Ceiling(bal / n) * n).Where(v => v > bal).Distinct().Take(4);
        }
    }

    private void Recalc()
    {
        var s = PaymentCalculator.Evaluate(Due, Tenders.Select(t => new TenderLine(t.Kind, t.Amount)));
        Paid = s.Paid; Balance = s.Balance; Change = s.Change; IsSettled = s.IsSettled;
        OnPropertyChanged(nameof(QuickCash));
    }

    public void SelectByKey(string key)
    {
        var m = Methods.FirstOrDefault(x => string.Equals(x.ShortcutKey, key, StringComparison.OrdinalIgnoreCase));
        if (m != null) SelectedMethod = m;
    }

    [RelayCommand] private void SelectMethod(PaymentMethod m) { SelectedMethod = m; AmountText = Balance.ToString("0.00"); }
    [RelayCommand] private async Task QuickAmount(decimal amount) { SelectedMethod = Methods.FirstOrDefault(m => m.Kind == 1) ?? SelectedMethod; AmountText = amount.ToString("0.00"); await AddTender(); }
    [RelayCommand] private async Task Exact() { AmountText = Balance.ToString("0.00"); await AddTender(); }

    [RelayCommand]
    private async Task AddTender()
    {
        if (IsSettled && string.IsNullOrWhiteSpace(AmountText)) { Complete(); return; }
        var m = SelectedMethod;
        if (m == null) return;
        decimal amount = string.IsNullOrWhiteSpace(AmountText) ? Balance : decimal.TryParse(AmountText, out var a) ? a : -1;
        if (amount <= 0) { if (IsSettled) Complete(); else Dialogs.Error("Enter a valid amount."); return; }

        var kind = (PaymentKind)m.Kind;
        string? reference = null, cardType = null; int? cnId = null;

        if (kind != PaymentKind.Cash)
        {
            var maxNonCash = Money.R(Math.Max(0, Due - Tenders.Sum(t => t.Amount)));
            if (amount > maxNonCash) amount = maxNonCash;
            if (amount <= 0) { Dialogs.Error("The bill is already fully paid."); return; }
        }

        if (kind == PaymentKind.Credit && _ensureCustomer() == null) return;

        if (kind == PaymentKind.CreditNote)
        {
            reference = Dialogs.Prompt("Credit note", "Scan or type the credit note number");
            if (string.IsNullOrWhiteSpace(reference)) return;
            var note = await ReturnRepository.GetCreditNoteAsync(reference);
            if (note == null || note.IsCancelled) { Dialogs.Error("Credit note not found."); return; }
            if (note.ExpiryDate.HasValue && note.ExpiryDate.Value.Date < DateTime.Today) { Dialogs.Error("This credit note has expired."); return; }
            var already = Tenders.Where(t => t.CreditNoteId == note.CreditNoteID).Sum(t => t.Amount);
            var available = note.Balance - already;
            if (available <= 0) { Dialogs.Error("No balance left on this credit note."); return; }
            amount = Math.Min(amount, available);
            reference = note.CreditNoteNo; cnId = note.CreditNoteID;
        }
        else if (m.RequiresReference)
        {
            var label = kind switch
            {
                PaymentKind.Card => "Card last 4 digits / approval code",
                PaymentKind.Cheque => "Cheque number",
                _ => "Reference / transaction no"
            };
            reference = Dialogs.Prompt(m.Name, label);
            if (reference == null) return;
            if (kind == PaymentKind.Card) cardType = m.Name;
        }

        Tenders.Add(new TenderViewModel { Method = m, Amount = Money.R(amount), Reference = reference, CardType = cardType, CreditNoteId = cnId });
        Recalc();
        AmountText = IsSettled ? "" : Balance.ToString("0.00");
    }

    [RelayCommand]
    private void RemoveTender()
    {
        var t = SelectedTender ?? Tenders.LastOrDefault();
        if (t == null) return;
        Tenders.Remove(t);
        Recalc();
        AmountText = Balance.ToString("0.00");
    }

    [RelayCommand]
    private void Complete()
    {
        if (!IsSettled) { Dialogs.Error($"Balance {Balance:N2} still to be paid."); return; }
        Close(true);
    }

    /// <summary>Applied amounts per method: all cash tenders are merged and reduced by the change.</summary>
    public List<SalePaymentDto> ToPayments()
    {
        var list = new List<SalePaymentDto>();
        var cash = Tenders.Where(t => t.Kind == PaymentKind.Cash).ToList();
        if (cash.Count > 0)
        {
            var applied = cash.Sum(t => t.Amount) - Change;
            if (applied > 0)
                list.Add(new SalePaymentDto { PaymentMethodId = cash[0].Method.PaymentMethodID, Code = cash[0].Method.Code, Name = cash[0].Method.Name, Kind = PaymentKind.Cash, Amount = Money.R(applied) });
        }
        foreach (var t in Tenders.Where(t => t.Kind != PaymentKind.Cash))
            list.Add(new SalePaymentDto
            {
                PaymentMethodId = t.Method.PaymentMethodID, Code = t.Method.Code, Name = t.Method.Name, Kind = t.Kind, Amount = t.Amount,
                Reference = t.Reference, CardType = t.CardType, CreditNoteId = t.CreditNoteId
            });
        return list;
    }

    public bool OpensDrawer => Tenders.Any(t => t.Method.OpensDrawer);
}

/* ---------------- Held bills (F7) ---------------- */
public sealed partial class HeldBillsViewModel : DialogViewModel
{
    public HeldBillsViewModel() { Title = "Held bills"; }
    public ObservableCollection<HeldBillDto> Bills { get; } = new();
    [ObservableProperty] private HeldBillDto? _selected;
    public int? ChosenId { get; private set; }

    public override async Task LoadAsync()
    {
        Bills.Clear();
        foreach (var b in await HoldRepository.ListAsync(Session.LocationId)) Bills.Add(b);
        Selected = Bills.FirstOrDefault();
    }

    [RelayCommand] private void Recall() { if (Selected == null) return; ChosenId = Selected.HeldBillID; Close(true); }

    [RelayCommand]
    private async Task Delete()
    {
        if (Selected == null || !Dialogs.Confirm($"Delete held bill {Selected.HoldNo}?")) return;
        await RunAsync(async () => { await HoldRepository.DeleteAsync(Selected.HeldBillID); await LoadAsync(); });
    }
}

/* ---------------- Customer picker (F8) ---------------- */
public sealed partial class CustomerPickerViewModel : DialogViewModel
{
    public CustomerPickerViewModel() { Title = "Customer"; }
    public ObservableCollection<CustomerInfoDto> Results { get; } = new();
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private CustomerInfoDto? _selected;
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string _newMobile = "";
    public CustomerInfoDto? Result { get; private set; }
    public bool Cleared { get; private set; }

    partial void OnSearchTextChanged(string value) => _ = LoadAsync();

    public override async Task LoadAsync()
    {
        var list = await LookupRepository.SearchCustomersAsync(SearchText, 100);
        Results.Clear();
        foreach (var c in list) Results.Add(c);
        Selected = Results.FirstOrDefault();
    }

    [RelayCommand] private void Choose() { if (Selected == null) return; Result = Selected; Close(true); }
    [RelayCommand] private void ClearCustomer() { Cleared = true; Close(true); }

    [RelayCommand]
    private async Task QuickAdd()
    {
        if (string.IsNullOrWhiteSpace(NewName)) { Dialogs.Error("Enter the customer name."); return; }
        await RunAsync(async () =>
        {
            var id = await LookupRepository.QuickAddCustomerAsync(NewName.Trim(), NewMobile.Trim(), Session.UserId);
            Result = await LookupRepository.GetCustomerAsync(id);
            Close(true);
        });
    }
}

/* ---------------- Simple list picker (location etc.) ---------------- */
public sealed partial class PickListViewModel : DialogViewModel
{
    public PickListViewModel(string title, IReadOnlyList<LookupItem> items, int? selectedId)
    {
        Title = title; Items = items; _selected = items.FirstOrDefault(i => i.Id == selectedId) ?? items.FirstOrDefault();
    }
    public IReadOnlyList<LookupItem> Items { get; }
    [ObservableProperty] private LookupItem? _selected;
    [RelayCommand] private void Choose() { if (Selected != null) Close(true); }
}

/* ---------------- Keyboard help (F1) ---------------- */
public sealed class ShortcutHelpViewModel : DialogViewModel
{
    public ShortcutHelpViewModel() { Title = "Keyboard shortcuts"; }
    public sealed record Shortcut(string Key, string Action);
    public IReadOnlyList<Shortcut> Keys { get; } = new (string, string)[]
    {
        ("Enter", "Add scanned / typed code   (3*CODE = quantity 3)"),
        ("F2", "Find item by name"), ("F3", "Change quantity of selected line"),
        ("F4", "Item discount (% or amount)"), ("F5", "Bill discount (% or amount)"),
        ("F6", "Hold bill"), ("F7", "Recall held bill"), ("F8", "Select / add customer"),
        ("F9 / Delete", "Void selected line"), ("F10 / End", "Payment"),
        ("F11", "Switch keyboard / touch screen"), ("F12", "Cancel the whole bill"),
        ("Up / Down", "Select line"), ("+ / -", "Increase / decrease quantity (empty scan box)"),
        ("Ctrl+E", "Change price of selected line"), ("Ctrl+P", "Re-print last bill"),
        ("Ctrl+R", "Sales return / refund"), ("Ctrl+D", "Open cash drawer"),
        ("Ctrl+L", "Change billing location"), ("Ctrl+O", "Paid out"), ("Ctrl+M", "Receive money"),
        ("Ctrl+N", "Line note (e.g. 2x4) after the item name"),
        ("Esc", "Close billing (when the bill is empty)")
    }.Select(k => new Shortcut(k.Item1, k.Item2)).ToList();
}

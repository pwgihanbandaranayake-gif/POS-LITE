using System.Collections.ObjectModel;

namespace PosSystem.ViewModels;

/* =====================================================================
   SALES RETURN  (page in the back office and dialog from the till - Ctrl+R)
   ===================================================================== */
public sealed partial class ReturnLineViewModel : ObservableObject
{
    public int? SalesInvoiceDetailID { get; init; }
    public int ItemID { get; init; }
    public int? ItemStockID { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public decimal Sold { get; init; }
    public decimal AlreadyReturned { get; init; }
    public decimal Available { get; init; }
    public bool FromInvoice => SalesInvoiceDetailID.HasValue;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Amount))] private decimal _rate;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Amount))] private decimal _returnQty;
    [ObservableProperty] private bool _isDamaged;
    public decimal Amount => Money.R(ReturnQty * Rate);
}

public sealed partial class SalesReturnViewModel : DialogViewModel
{
    public SalesReturnViewModel()
    {
        Title = "Sales return / refund";
    }

    public ObservableCollection<ReturnLineViewModel> Lines { get; } = new();
    [ObservableProperty] private string _invoiceNo = "";
    [ObservableProperty] private InvoiceForReturnDto? _invoice;
    [ObservableProperty] private string _itemCode = "";
    [ObservableProperty] private bool _refundAsCreditNote;
    [ObservableProperty] private string _reason = "";
    [ObservableProperty] private decimal _total;
    public string InvoiceInfo => Invoice == null ? "Return without invoice - type item codes below"
        : $"Invoice {Invoice.InvoiceNo}  |  {Invoice.TransactionDate:yyyy-MM-dd HH:mm}  |  {Invoice.CustomerName ?? "Walk-in"}  |  Total {Invoice.NetTotal:N2}";

    partial void OnInvoiceChanged(InvoiceForReturnDto? value) => OnPropertyChanged(nameof(InvoiceInfo));

    private void Watch(ReturnLineViewModel l) => l.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ReturnLineViewModel.Amount)) Total = Lines.Sum(x => x.Amount); };

    [RelayCommand]
    private async Task LoadInvoice()
    {
        if (string.IsNullOrWhiteSpace(InvoiceNo)) return;
        await RunAsync(async () =>
        {
            var inv = await ReturnRepository.GetInvoiceForReturnAsync(InvoiceNo);
            if (inv == null) { Dialogs.Error("Invoice not found or it was cancelled."); return; }
            Invoice = inv;
            Lines.Clear();
            foreach (var l in inv.Lines)
            {
                var vm = new ReturnLineViewModel
                {
                    SalesInvoiceDetailID = l.SalesInvoiceDetailID, ItemID = l.ItemID, ItemStockID = l.ItemStockID, Code = l.ItemCode, Name = l.ItemName,
                    Sold = l.SoldQty, AlreadyReturned = l.ReturnedQty, Available = l.Available, Rate = l.NetRate
                };
                Watch(vm); Lines.Add(vm);
            }
            Total = 0;
            if (inv.Lines.All(l => l.Available <= 0)) Dialogs.Info("All items of this invoice were already returned.");
        });
    }

    [RelayCommand]
    private void ReturnAll() { foreach (var l in Lines.Where(x => x.FromInvoice)) l.ReturnQty = l.Available; }

    [RelayCommand]
    private async Task AddItem()
    {
        if (string.IsNullOrWhiteSpace(ItemCode)) return;
        await RunAsync(async () =>
        {
            var item = await ItemRepository.FindByCodeAsync(ItemCode, Session.LocationId);
            if (item == null) { Dialogs.Error("Item not found."); return; }
            var vm = new ReturnLineViewModel { ItemID = item.ItemID, Code = item.ItemCode ?? "", Name = item.DisplayName, Available = 999999, Rate = item.SellingPrice, ReturnQty = 1 };
            Watch(vm); Lines.Add(vm);
            Total = Lines.Sum(x => x.Amount);
            ItemCode = "";
        });
    }

    [RelayCommand]
    private void Clear() { Invoice = null; InvoiceNo = ""; Lines.Clear(); Total = 0; Reason = ""; RefundAsCreditNote = false; }

    [RelayCommand]
    private async Task Save()
    {
        var lines = Lines.Where(l => l.ReturnQty > 0).ToList();
        if (lines.Count == 0) { Dialogs.Error("Enter the quantity to return."); return; }
        var over = lines.FirstOrDefault(l => l.ReturnQty > l.Available);
        if (over != null) { Dialogs.Error($"'{over.Name}': only {over.Available:0.###} can be returned."); return; }
        if (string.IsNullOrWhiteSpace(Reason)) { Dialogs.Error("Enter the reason for the return."); return; }

        // no invoice, or older than the allowed days -> supervisor
        bool needsApproval = Invoice == null || (DateTime.Today - Invoice.TransactionDate.Date).TotalDays > Session.Settings.ReturnDaysLimit;
        int? approver = null;
        if (needsApproval)
        {
            var why = Invoice == null ? "Return without invoice" : $"Invoice older than {Session.Settings.ReturnDaysLimit} days";
            var vm = new ManagerOverrideViewModel(Perm.PosCancelInvoice, why);
            if (Dialogs.Show(vm) != true) return;
            approver = vm.ApprovedBy;
        }

        var method = RefundAsCreditNote ? RefundMethod.CreditNote : RefundMethod.Cash;
        if (!Dialogs.Confirm($"Refund {Session.Settings.CurrencySymbol} {Total:N2} as {(RefundAsCreditNote ? "a credit note" : "CASH")}?")) return;

        var req = new SalesReturnRequest
        {
            SalesInvoiceID = Invoice?.SalesInvoiceID, InvoiceNo = Invoice?.InvoiceNo, CustomerID = Invoice?.CustomerID,
            LocationID = Session.LocationId, UserID = Session.UserId, TerminalID = Session.TerminalId, SessionID = Session.ShiftId,
            RefundMethod = method, Reason = Reason.Trim(), CreditNoteValidDays = Session.Settings.CreditNoteValidDays,
            Lines = lines.Select(l => new SalesReturnLineDto
            {
                SalesInvoiceDetailID = l.SalesInvoiceDetailID, ItemID = l.ItemID, ItemStockID = l.ItemStockID, ItemName = l.Name,
                Quantity = l.ReturnQty, Rate = l.Rate, IsDamaged = l.IsDamaged
            }).ToList()
        };

        await RunAsync(async () =>
        {
            var res = await ReturnRepository.SaveSalesReturnAsync(req);
            if (approver.HasValue) await AuthService.AuditAsync("RETURN_APPROVED", res.ReturnNo, Reason, approver);
            if (method == RefundMethod.Cash) ReceiptService.OpenDrawer();

            var r = new ReceiptData
            {
                Title = "SALES RETURN", DocumentNo = res.ReturnNo, Date = DateTime.Now, Cashier = Session.FullName, Terminal = Session.TerminalName,
                Location = Session.LocationName, Customer = Invoice?.CustomerName, ReferenceNo = Invoice?.InvoiceNo,
                Lines = lines.Select(l => new ReceiptLine { Code = l.Code, Name = l.Name + (l.IsDamaged ? " (damaged)" : ""), Qty = l.ReturnQty, Price = l.Rate, Amount = l.Amount }).ToList(),
                GrossTotal = res.Amount, NetTotal = res.Amount, ItemCount = lines.Count, TotalQty = lines.Sum(l => l.ReturnQty),
                Payments = new() { (method == RefundMethod.Cash ? "Cash refund" : "Credit note " + res.CreditNoteNo, res.Amount, null) }
            };
            if (res.CreditNoteNo != null)
            {
                r.ExtraLines.Add(("CREDIT NOTE", res.CreditNoteNo));
                if (Session.Settings.CreditNoteValidDays > 0)
                    r.ExtraLines.Add(("Valid until", DateTime.Today.AddDays(Session.Settings.CreditNoteValidDays).ToString("yyyy-MM-dd")));
                r.ExtraLines.Add(("Use this number as payment", ""));
            }
            r.ExtraLines.Add(("Reason", Reason));
            ReceiptService.Print(r);
            Dialogs.Info(res.CreditNoteNo != null
                ? $"Return {res.ReturnNo} saved.\nCredit note {res.CreditNoteNo} issued for {res.Amount:N2}."
                : $"Return {res.ReturnNo} saved.\nGive the customer {Session.Settings.CurrencySymbol} {res.Amount:N2}.");
            Clear();
        });
    }
}

/* =====================================================================
   STOCK DOCUMENTS: GRN / return to supplier / transfer / adjustment
   ===================================================================== */
public sealed partial class StockLineViewModel : ObservableObject
{
    public int ItemID { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public decimal InStock { get; init; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Amount))] private decimal _qty;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Amount))] private decimal _cost;
    [ObservableProperty] private decimal _selling;
    [ObservableProperty] private string? _batchNo;
    [ObservableProperty] private DateTime? _expDate;
    public decimal Amount => Money.R(Qty * Cost);
}

public sealed partial class StockDocumentViewModel : ViewModelBase
{


    public StockDocKind Kind { get; private set; }
    public void Init(StockDocKind kind) { Kind = kind; OnPropertyChanged(string.Empty); }

    public string Heading => Kind switch
    {
        StockDocKind.Grn => "Goods received note (GRN)",
        StockDocKind.PurchaseReturn => "Return to supplier",
        StockDocKind.Transfer => "Stock transfer",
        _ => "Stock adjustment"
    };
    public string HelpText => Kind switch
    {
        StockDocKind.Grn => "Scan or type item codes. Each line becomes a stock batch with its own cost, selling price, batch no and expiry.",
        StockDocKind.PurchaseReturn => "Stock is taken from the oldest batches first (FIFO).",
        StockDocKind.Transfer => "Moves stock from this location to another location with the same cost and price.",
        _ => "Use a positive quantity to add stock (found / count up) and a negative quantity to remove it (damage, expiry, wastage)."
    };
    public bool IsGrn => Kind == StockDocKind.Grn;
    public bool NeedsSupplier => Kind is StockDocKind.Grn or StockDocKind.PurchaseReturn;
    public bool IsTransfer => Kind == StockDocKind.Transfer;
    public string FromLocation => Session.LocationName;

    public ObservableCollection<StockLineViewModel> Lines { get; } = new();
    public IReadOnlyList<LookupItem> Suppliers { get; private set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<Location> ToLocations => Session.Locations.Where(l => l.LocationID != Session.LocationId).ToList();
    [ObservableProperty] private StockLineViewModel? _selectedLine;
    [ObservableProperty] private LookupItem? _supplier;
    [ObservableProperty] private Location? _toLocation;
    [ObservableProperty] private DateTime _docDate = DateTime.Today;
    [ObservableProperty] private string _supplierInvoiceNo = "";
    [ObservableProperty] private string _remarks = "";
    [ObservableProperty] private string _entryCode = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(NetTotal))] private decimal _total;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(NetTotal))] private string _discount = "0";
    [ObservableProperty] private string _paidAmount = "0";
    [ObservableProperty] private bool _updateItemPrices = true;
    public decimal NetTotal => Total - (decimal.TryParse(Discount, out var d) ? d : 0);

    public override async Task LoadAsync()
    {
        if (NeedsSupplier) await RunAsync(async () => { Suppliers = await LookupRepository.GetSuppliersAsync(); OnPropertyChanged(nameof(Suppliers)); });
    }

    private void Watch(StockLineViewModel l) => l.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(StockLineViewModel.Amount)) Total = Lines.Sum(x => x.Amount); };

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
            var line = new StockLineViewModel
            {
                ItemID = item.ItemID, Code = item.ItemCode ?? "", Name = item.DisplayName, InStock = item.StockOnHand,
                Qty = 1, Cost = item.CostPrice, Selling = item.SellingPrice
            };
            Watch(line); Lines.Add(line); SelectedLine = line;
            Total = Lines.Sum(x => x.Amount);
        });
    }

    [RelayCommand]
    private void RemoveLine()
    {
        if (SelectedLine == null) return;
        Lines.Remove(SelectedLine);
        Total = Lines.Sum(x => x.Amount);
    }

    [RelayCommand]
    private async Task Save()
    {
        if (NeedsSupplier && Supplier == null) { Dialogs.Error("Select the supplier."); return; }
        if (IsTransfer && ToLocation == null) { Dialogs.Error("Select the destination location."); return; }
        if (Kind == StockDocKind.Adjustment && string.IsNullOrWhiteSpace(Remarks)) { Dialogs.Error("Enter the reason for the adjustment."); return; }
        if (IsGrn && Lines.Any(l => l.Selling < l.Cost) && !Dialogs.Confirm("Some items have a selling price below cost. Continue?")) return;
        if (!Dialogs.Confirm($"Save {Heading.ToLowerInvariant()} with {Lines.Count} lines?")) return;

        var req = new StockDocRequest
        {
            Kind = Kind, LocationID = Session.LocationId, ToLocationID = ToLocation?.LocationID, SupplierID = Supplier?.Id,
            SupplierInvoiceNo = SupplierInvoiceNo, DocDate = DocDate, Remarks = Remarks,
            Discount = decimal.TryParse(Discount, out var d) ? d : 0, PaidAmount = decimal.TryParse(PaidAmount, out var p) ? p : 0,
            PayMethod = "CASH", UpdateItemPrices = UpdateItemPrices, UserID = Session.UserId, TerminalID = Session.TerminalId,
            Lines = Lines.Select(l => new StockDocLineDto
            {
                ItemID = l.ItemID, ItemCode = l.Code, ItemName = l.Name, Quantity = l.Qty, CostPrice = l.Cost, SellingPrice = l.Selling,
                BatchNo = l.BatchNo, ExpDate = l.ExpDate
            }).ToList()
        };
        await RunAsync(async () =>
        {
            var res = await StockRepository.SaveAsync(req);
            Dialogs.Info($"{Heading} {res.DocNo} saved.");
            Lines.Clear(); Total = 0; Remarks = ""; SupplierInvoiceNo = ""; Discount = "0"; PaidAmount = "0";
        });
    }
}

/* =====================================================================
   CASH: paid out / receive payment
   ===================================================================== */
public sealed partial class CashEntryViewModel : DialogViewModel
{


    private bool _isPaidOut = true;
    public bool IsPaidOut
    {
        get => _isPaidOut;
        set { _isPaidOut = value; Title = value ? "Paid out / expense" : "Receive payment"; OnPropertyChanged(); OnPropertyChanged(nameof(IsReceive)); OnPropertyChanged(nameof(Title)); }
    }
    public bool IsReceive => !IsPaidOut;
    public IReadOnlyList<string> Categories { get; } = new[] { "Expenses", "Transport", "Utilities", "Staff welfare", "Salary advance", "Cleaning", "Other" };
    public IReadOnlyList<LookupItem> Employees { get; private set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<PaymentMethod> Methods => Session.PaymentMethods.Where(m => m.Kind is 1 or 2 or 4 or 6 or 7).ToList();
    public ObservableCollection<CustomerInfoDto> Customers { get; } = new();
    [ObservableProperty] private string _amount = "";
    [ObservableProperty] private string _category = "Expenses";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private LookupItem? _employee;
    [ObservableProperty] private string _customerSearch = "";
    [ObservableProperty] private CustomerInfoDto? _customer;
    [ObservableProperty] private PaymentMethod? _method;
    [ObservableProperty] private string _chequeNo = "";
    [ObservableProperty] private string _bank = "";

    partial void OnCustomerSearchChanged(string value) => _ = SearchCustomers();

    private async Task SearchCustomers()
    {
        Customers.Clear();
        foreach (var c in await LookupRepository.SearchCustomersAsync(CustomerSearch, 50)) Customers.Add(c);
    }

    public override async Task LoadAsync()
    {
        IsPaidOut = IsPaidOut;
        await RunAsync(async () =>
        {
            Employees = await LookupRepository.GetEmployeesAsync(); OnPropertyChanged(nameof(Employees));
            Method = Methods.FirstOrDefault();
            await SearchCustomers();
        });
    }

    [RelayCommand]
    private async Task Save()
    {
        if (!decimal.TryParse(Amount, out var amt) || amt <= 0) { Dialogs.Error("Enter a valid amount."); return; }
        if (IsPaidOut && string.IsNullOrWhiteSpace(Description)) { Dialogs.Error("Enter what the money was paid for."); return; }
        if (IsReceive && Customer == null && string.IsNullOrWhiteSpace(Description)) { Dialogs.Error("Select a customer or enter a description."); return; }

        var req = new CashEntryRequest
        {
            IsPaidOut = IsPaidOut, Amount = amt, Category = IsPaidOut ? Category : null, Description = Description,
            CustomerID = IsReceive ? Customer?.CustomerID : null, EmployeeID = IsPaidOut ? Employee?.Id : null,
            PaymentMethodID = IsReceive ? Method?.PaymentMethodID : null, ChequeNo = ChequeNo, Bank = Bank,
            LocationID = Session.LocationId, UserID = Session.UserId, TerminalID = Session.TerminalId, SessionID = Session.ShiftId
        };
        await RunAsync(async () =>
        {
            var no = await CashRepository.SaveAsync(req);
            if (IsPaidOut || Method?.Kind == 1) ReceiptService.OpenDrawer();
            var slip = new ReceiptData
            {
                Title = IsPaidOut ? "PAID OUT VOUCHER" : "PAYMENT RECEIPT", DocumentNo = no, Date = DateTime.Now, Cashier = Session.FullName,
                Terminal = Session.TerminalName, Location = Session.LocationName, Customer = IsReceive ? Customer?.Name : Employee?.Name
            };
            if (IsPaidOut) slip.ExtraLines.Add(("Category", Category));
            else slip.ExtraLines.Add(("Paid by", Method?.Name ?? ""));
            if (!string.IsNullOrWhiteSpace(Description)) slip.ExtraLines.Add(("For", Description));
            slip.ExtraLines.Add(("--", ""));
            slip.ExtraLines.Add(("AMOUNT", $"{Session.Settings.CurrencySymbol} {amt:N2}"));
            if (IsReceive && Customer != null) slip.ExtraLines.Add(("Balance due", (Customer.Outstanding - amt).ToString("N2")));
            slip.ExtraLines.Add(("Signature", "................"));
            ReceiptService.Print(slip);
            Dialogs.Info($"{Title} {no} saved.");
            Amount = ""; Description = ""; ChequeNo = ""; Bank = ""; Customer = null;
            Close(true);
        });
    }
}

/* =====================================================================
   SUPPLIER PAYMENT
   ===================================================================== */
public sealed partial class SupplierPaymentViewModel : ViewModelBase
{
    public IReadOnlyList<LookupItem> Suppliers { get; private set; } = Array.Empty<LookupItem>();
    public ObservableCollection<OpenGrnDto> Grns { get; } = new();
    public IReadOnlyList<string> PayTypes { get; } = new[] { "CASH", "CHEQUE", "BANK" };
    [ObservableProperty] private LookupItem? _supplier;
    [ObservableProperty] private OpenGrnDto? _grn;
    [ObservableProperty] private string _amount = "";
    [ObservableProperty] private string _payType = "CASH";
    [ObservableProperty] private string _chequeNo = "";
    [ObservableProperty] private DateTime? _chequeDate = DateTime.Today;
    [ObservableProperty] private string _remarks = "";
    public decimal TotalOutstanding => Grns.Sum(g => g.BalanceAmount);

    partial void OnSupplierChanged(LookupItem? value) => _ = LoadGrns();
    partial void OnGrnChanged(OpenGrnDto? value) { if (value != null) Amount = value.BalanceAmount.ToString("0.00"); }

    public override async Task LoadAsync() =>
        await RunAsync(async () => { Suppliers = await LookupRepository.GetSuppliersAsync(); OnPropertyChanged(nameof(Suppliers)); });

    private async Task LoadGrns()
    {
        Grns.Clear();
        if (Supplier == null) return;
        foreach (var g in await StockRepository.GetOpenGrnsAsync(Supplier.Id)) Grns.Add(g);
        OnPropertyChanged(nameof(TotalOutstanding));
        Grn = Grns.FirstOrDefault();
    }

    [RelayCommand]
    private async Task Save()
    {
        if (Grn == null) { Dialogs.Error("Select the GRN to pay."); return; }
        if (!decimal.TryParse(Amount, out var amt) || amt <= 0 || amt > Grn.BalanceAmount) { Dialogs.Error($"Amount must be between 0 and {Grn.BalanceAmount:N2}."); return; }
        if (PayType == "CHEQUE" && string.IsNullOrWhiteSpace(ChequeNo)) { Dialogs.Error("Enter the cheque number."); return; }
        await RunAsync(async () =>
        {
            var no = await StockRepository.PaySupplierAsync(new SupplierPaymentRequest
            {
                PurchaseInvoiceID = Grn.PurchaseInvoiceID, Amount = amt, PayType = PayType, ChequeNo = ChequeNo,
                ChequeDate = PayType == "CHEQUE" ? ChequeDate : null, Remarks = Remarks, UserID = Session.UserId,
                TerminalID = Session.TerminalId, LocationID = Session.LocationId
            });
            Dialogs.Info($"Payment {no} saved.");
            Remarks = ""; ChequeNo = "";
            await LoadGrns();
        });
    }
}

/* =====================================================================
   SHIFT CLOSE (Z report with cash denomination count)
   ===================================================================== */
public sealed partial class DenominationViewModel : ObservableObject
{
    public decimal Value { get; init; }
    public string Label => Value >= 20 ? Value.ToString("N0") : Value.ToString("0") + " (coin)";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Total))] private string _count = "";
    public decimal Total => int.TryParse(Count, out var c) ? c * Value : 0;
}

public sealed record MethodTotal(string Method, decimal Amount);

public sealed partial class SessionCloseViewModel : ViewModelBase
{
    public SessionCloseViewModel()
    {
        foreach (var v in new[] { 5000m, 2000m, 1000m, 500m, 100m, 50m, 20m, 10m, 5m, 2m, 1m })
        {
            var d = new DenominationViewModel { Value = v };
            d.PropertyChanged += (_, _) => { OnPropertyChanged(nameof(Counted)); OnPropertyChanged(nameof(Difference)); OnPropertyChanged(nameof(DifferenceText)); };
            Denominations.Add(d);
        }
    }

    public ObservableCollection<DenominationViewModel> Denominations { get; } = new();
    public ObservableCollection<MethodTotal> ByMethod { get; } = new();
    [ObservableProperty] private SessionSummaryDto? _summary;
    [ObservableProperty] private bool _hasSession;
    public decimal Counted => Denominations.Sum(d => d.Total);
    public decimal Difference => Counted - (Summary?.ExpectedCash ?? 0);
    public string DifferenceText => Difference == 0 ? "Cash is exact" : Difference > 0 ? $"Excess {Difference:N2}" : $"Shortage {-Difference:N2}";

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            HasSession = await ShiftService.HasOpenShiftAsync();
            if (!HasSession) return;
            Summary = await ShiftService.GetSummaryAsync(Session.ShiftId!.Value);
            ByMethod.Clear();
            foreach (var m in Summary.ByMethod) ByMethod.Add(new MethodTotal(m.Method, m.Amount));
            OnPropertyChanged(nameof(Difference)); OnPropertyChanged(nameof(DifferenceText));
        });
    }

    [RelayCommand]
    private async Task CloseShift()
    {
        if (Summary == null) return;
        if (!Dialogs.Confirm($"Close the shift with counted cash {Counted:N2}?\n{DifferenceText}")) return;
        var denoms = string.Join(";", Denominations.Where(d => d.Total > 0).Select(d => $"{d.Value:0}x{d.Count}"));
        await RunAsync(async () =>
        {
            var id = Summary.SessionId;
            await ShiftService.CloseAsync(id, Counted, denoms);
            var final = await ShiftService.GetSummaryAsync(id);
            ReceiptService.Print(ShiftService.ToReceipt(final, Counted));
            Dialogs.Info("Shift closed and Z report printed.");
            HasSession = false; Summary = null;
        });
    }
}

/* =====================================================================
   INVOICES: search, re-print (roll or A5), cancel
   ===================================================================== */
public sealed partial class InvoicesViewModel : ViewModelBase
{
    public ObservableCollection<InvoiceSummaryDto> Invoices { get; } = new();
    [ObservableProperty] private DateTime _fromDate = DateTime.Today;
    [ObservableProperty] private DateTime _toDate = DateTime.Today;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private InvoiceSummaryDto? _selected;
    [ObservableProperty] private bool _allLocations;
    public decimal Total => Invoices.Where(i => !i.IsCanceled).Sum(i => i.NetTotal);

    public override Task LoadAsync() => Find();

    [RelayCommand]
    private async Task Find()
    {
        await RunAsync(async () =>
        {
            Invoices.Clear();
            foreach (var i in await SalesRepository.ListInvoicesAsync(FromDate, ToDate, AllLocations ? null : Session.LocationId, Search)) Invoices.Add(i);
            OnPropertyChanged(nameof(Total));
        });
    }

    [RelayCommand]
    private async Task Reprint(string format)
    {
        if (Selected == null) return;
        if (await OverrideService.AuthorizeAsync(Perm.PosReprint, "Re-print " + Selected.InvoiceNo) == null) return;
        await RunAsync(() => ReceiptService.PrintInvoiceAsync(Selected.SalesInvoiceID, true, (ReceiptFormat)int.Parse(format), 1));
    }

    [RelayCommand]
    private async Task Preview()
    {
        if (Selected == null) return;
        await RunAsync(async () =>
        {
            var r = await ReceiptService.LoadInvoiceAsync(Selected.SalesInvoiceID, true);
            if (r != null) ReceiptService.Preview(r);
        });
    }

    [RelayCommand]
    private async Task CancelInvoice()
    {
        if (Selected == null || Selected.IsCanceled) return;
        var approver = await OverrideService.AuthorizeAsync(Perm.PosCancelInvoice, "Cancel invoice " + Selected.InvoiceNo);
        if (approver == null) return;
        var reason = Dialogs.Prompt("Cancel invoice", $"Reason for cancelling {Selected.InvoiceNo} ({Selected.NetTotal:N2})");
        if (string.IsNullOrWhiteSpace(reason)) return;
        await RunAsync(async () =>
        {
            await SalesRepository.CancelInvoiceAsync(Selected.SalesInvoiceID, approver.Value, reason, Session.TerminalId);
            Dialogs.Info("Invoice cancelled. Stock has been returned and any credit notes used were restored.");
            await Find();
        });
    }
}

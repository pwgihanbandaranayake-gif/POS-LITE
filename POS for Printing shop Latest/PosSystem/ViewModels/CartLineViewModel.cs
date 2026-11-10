namespace PosSystem.ViewModels;

public sealed partial class CartLineViewModel : ObservableObject
{
    [ObservableProperty] private int _lineNo;
    public int ItemId { get; init; }
    public int? ItemStockId { get; set; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Unit { get; init; }
    public int? TaxId { get; init; }
    public decimal TaxRate { get; init; }
    public bool AllowDiscount { get; init; } = true;
    public bool AllowPriceChange { get; init; }
    public bool IsService { get; init; }
    public decimal OriginalPrice { get; init; }

    /// <summary>Text added after the item name on the bill, e.g. "2x4" -> "FLEX BANNER (2x4)" (Ctrl+N).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DisplayName))] private string? _lineNote;
    /// <summary>Item name + note, shown in the bill grid.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(LineNote) ? Name : $"{Name} ({LineNote})";

    [ObservableProperty] private decimal _qty;
    [ObservableProperty] private decimal _unitPrice;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DiscountText), nameof(HasDiscount))] private DiscountType _discountType;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DiscountText), nameof(HasDiscount))] private decimal _discountValue;

    [ObservableProperty] private decimal _gross;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DiscountText), nameof(HasDiscount))] private decimal _lineDiscount;
    [ObservableProperty] private decimal _billShare;
    [ObservableProperty] private decimal _net;
    [ObservableProperty] private decimal _tax;
    [ObservableProperty] private decimal _total;

    /// <summary>Amount shown on the cart row: after item discount, before bill discount.</summary>
    public decimal RowAmount => Gross - LineDiscount;

    public bool HasDiscount => LineDiscount > 0;
    public string DiscountText => DiscountType switch
    {
        DiscountType.Percent when DiscountValue > 0 => $"{DiscountValue:0.##}% (-{LineDiscount:N2})",
        DiscountType.Amount when DiscountValue > 0 => $"-{LineDiscount:N2}",
        _ => ""
    };

    public LineInput ToInput() => new(Qty, UnitPrice, DiscountType, DiscountValue, TaxRate);

    public void Apply(LineResult r)
    {
        Gross = r.Gross; LineDiscount = r.LineDiscount; BillShare = r.BillDiscountShare; Net = r.Net; Tax = r.Tax; Total = r.Total;
        OnPropertyChanged(nameof(RowAmount));
    }
}

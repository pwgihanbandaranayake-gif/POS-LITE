namespace PosSystem.Models;

public enum DiscountType
{
    None = 0,
    Percent = 1,
    Amount = 2
}

/// <summary>Kind of a payment method. Each PaymentMethods row maps to one kind.</summary>
public enum PaymentKind
{
    Cash = 1,
    Card = 2,
    Credit = 3,
    Cheque = 4,
    CreditNote = 5,
    BankTransfer = 6,
    MobilePay = 7,
    Voucher = 8,
    Other = 9
}

public enum ReceiptFormat
{
    Thermal = 1,
    DotMatrix = 2,
    A5HalfSheet = 3
}

public enum PrintEngine
{
    /// <summary>WPF rendering through the Windows printer driver.</summary>
    Graphic = 1,
    /// <summary>Plain text / ESC-POS sent raw to the printer (fastest, best for dot matrix).</summary>
    RawText = 2
}

public enum RefundMethod
{
    Cash = 1,
    CreditNote = 2
}

public static class StockMovementTypes
{
    public const string Opening = "OPENING";
    public const string Purchase = "GRN";
    public const string Sale = "SALE";
    public const string SaleCancel = "SALE_CANCEL";
    public const string SalesReturn = "SALES_RETURN";
    public const string PurchaseReturn = "PURCHASE_RETURN";
    public const string TransferOut = "TRANSFER_OUT";
    public const string TransferIn = "TRANSFER_IN";
    public const string AdjustmentIn = "ADJ_IN";
    public const string AdjustmentOut = "ADJ_OUT";
}

public static class DocumentTypes
{
    public const string Invoice = "INV";
    public const string SalesReturn = "SRN";
    public const string CreditNote = "CN";
    public const string Grn = "GRN";
    public const string PurchaseReturn = "PRN";
    public const string Transfer = "TRF";
    public const string Adjustment = "ADJ";
    public const string Hold = "HLD";
    public const string PaidOut = "PO";
    public const string Received = "RA";
    public const string SupplierPayment = "SP";
}

namespace PosSystem.Models;

public sealed record LookupItem(int Id, string Code, string Name)
{
    public override string ToString() => string.IsNullOrWhiteSpace(Code) ? Name : $"{Code} - {Name}";
}

/* ---------------- Sales ---------------- */
public sealed class SaleLineDto
{
    public int LineNumber { get; set; }
    public int ItemId { get; set; }
    public int? ItemStockId { get; set; }
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>Text printed after the item name, e.g. "2x4" (saved in SalesInvoiceDetails.LineNote).</summary>
    public string? LineNote { get; set; }
    public DiscountType DiscountType { get; set; }
    public decimal DiscountValue { get; set; }
    public decimal LineDiscount { get; set; }
    public decimal BillDiscountShare { get; set; }
    public int? TaxId { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Gross { get; set; }
    public decimal Net { get; set; }
    public decimal Total { get; set; }
}

public sealed class SalePaymentDto
{
    public int PaymentMethodId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public PaymentKind Kind { get; set; }
    public decimal Amount { get; set; }          // applied amount (for cash: tendered - change)
    public string? Reference { get; set; }       // card last 4 / cheque no / credit note no / txn id
    public string? CardType { get; set; }
    public DateTime? ChequeDate { get; set; }
    public int? CreditNoteId { get; set; }
}

public sealed class SaleRequest
{
    public int LocationId { get; set; }
    public int TerminalId { get; set; }
    public int UserId { get; set; }
    public int? SessionId { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public DateTime StartTime { get; set; }
    public List<SaleLineDto> Lines { get; set; } = new();
    public List<SalePaymentDto> Payments { get; set; } = new();
    public DiscountType BillDiscountType { get; set; }
    public decimal BillDiscountValue { get; set; }
    public decimal BillDiscountAmount { get; set; }
    public decimal GrossTotal { get; set; }
    public decimal LineDiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public bool PricesIncludeTax { get; set; }
    public decimal RoundOff { get; set; }
    public decimal NetTotal { get; set; }
    public decimal CashTendered { get; set; }
    public decimal Change { get; set; }
    public bool AllowNegativeStock { get; set; }
    public string? Remarks { get; set; }
}

public sealed record SaleResult(int InvoiceId, string InvoiceNo);

public sealed class InvoiceSummaryDto
{
    public int SalesInvoiceID { get; set; }
    public string InvoiceNo { get; set; } = "";
    public DateTime TransactionDate { get; set; }
    public string? Cashier { get; set; }
    public string? Customer { get; set; }
    public decimal NetTotal { get; set; }
    public bool IsCanceled { get; set; }
    public string? Payments { get; set; }
}

/* ---------------- Receipt ---------------- */
public sealed class ReceiptLine
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal Price { get; set; }
    public decimal Discount { get; set; }
    public decimal Amount { get; set; }
}

public sealed class ReceiptData
{
    public string Title { get; set; } = "INVOICE";
    public string ShopName { get; set; } = "";
    public List<string> HeaderLines { get; set; } = new();
    public string? TaxRegNo { get; set; }
    public string DocumentNo { get; set; } = "";
    public DateTime Date { get; set; }
    public string Cashier { get; set; } = "";
    public string Terminal { get; set; } = "";
    public string Location { get; set; } = "";
    public string? Customer { get; set; }
    public string? ReferenceNo { get; set; }
    public List<ReceiptLine> Lines { get; set; } = new();
    public decimal GrossTotal { get; set; }
    public decimal LineDiscount { get; set; }
    public decimal BillDiscount { get; set; }
    public string? BillDiscountLabel { get; set; }
    public decimal Tax { get; set; }
    public bool TaxInclusive { get; set; }
    public decimal RoundOff { get; set; }
    public decimal NetTotal { get; set; }
    public List<(string Name, decimal Amount, string? Ref)> Payments { get; set; } = new();
    public decimal CashTendered { get; set; }
    public decimal Change { get; set; }
    public decimal TotalQty { get; set; }
    public int ItemCount { get; set; }
    public string? HeaderNote { get; set; }
    public string? Footer { get; set; }
    public bool IsReprint { get; set; }
    public string Currency { get; set; } = "Rs.";
    public string? LogoBase64 { get; set; }
    public bool ShowBarcode { get; set; } = true;
    public string? PoweredBy { get; set; }
    /// <summary>Total handed over by the customer (all payments + change).</summary>
    public decimal PaidAmount => Payments.Sum(p => p.Amount) + Change;
    public List<(string Label, string Value)> ExtraLines { get; set; } = new();
}

/* ---------------- Hold ---------------- */
public sealed class HeldBillDto
{
    public int HeldBillID { get; set; }
    public string HoldNo { get; set; } = "";
    public int LocationID { get; set; }
    public int? CustomerID { get; set; }
    public string? CustomerName { get; set; }
    public int BillDiscountType { get; set; }
    public decimal BillDiscountValue { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Note { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime DateCreated { get; set; }
    public int ItemCount { get; set; }
    public List<HeldBillItemDto> Items { get; set; } = new();
}

public sealed class HeldBillItemDto
{
    public int LineNumber { get; set; }
    public int ItemID { get; set; }
    public int? ItemStockID { get; set; }
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public int DiscountType { get; set; }
    public decimal DiscountValue { get; set; }
    public string? LineNote { get; set; }
}

/* ---------------- Returns ---------------- */
public sealed class ReturnableLineDto
{
    public int SalesInvoiceDetailID { get; set; }
    public int ItemID { get; set; }
    public int? ItemStockID { get; set; }
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public decimal SoldQty { get; set; }
    public decimal ReturnedQty { get; set; }
    public decimal NetRate { get; set; }   // net unit price actually paid (after all discounts)
    public decimal Available => SoldQty - ReturnedQty;
}

public sealed class InvoiceForReturnDto
{
    public int SalesInvoiceID { get; set; }
    public string InvoiceNo { get; set; } = "";
    public DateTime TransactionDate { get; set; }
    public int? CustomerID { get; set; }
    public string? CustomerName { get; set; }
    public decimal NetTotal { get; set; }
    public int LocationID { get; set; }
    public List<ReturnableLineDto> Lines { get; set; } = new();
}

public sealed class SalesReturnLineDto
{
    public int? SalesInvoiceDetailID { get; set; }
    public int ItemID { get; set; }
    public int? ItemStockID { get; set; }
    public string ItemName { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public bool IsDamaged { get; set; }
    public decimal Amount => Math.Round(Quantity * Rate, 2);
}

public sealed class SalesReturnRequest
{
    public int? SalesInvoiceID { get; set; }
    public string? InvoiceNo { get; set; }
    public int? CustomerID { get; set; }
    public int LocationID { get; set; }
    public int UserID { get; set; }
    public int TerminalID { get; set; }
    public int? SessionID { get; set; }
    public RefundMethod RefundMethod { get; set; }
    public string? Reason { get; set; }
    public int CreditNoteValidDays { get; set; } = 90;
    public List<SalesReturnLineDto> Lines { get; set; } = new();
}

public sealed record SalesReturnResult(int ReturnId, string ReturnNo, decimal Amount, string? CreditNoteNo);

public sealed class CreditNoteDto
{
    public int CreditNoteID { get; set; }
    public string CreditNoteNo { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal Balance { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public bool IsCancelled { get; set; }
}

/* ---------------- Stock documents ---------------- */
public enum StockDocKind { Grn = 1, PurchaseReturn = 2, Transfer = 3, Adjustment = 4 }

public sealed class StockDocLineDto
{
    public int ItemID { get; set; }
    public int? ItemStockID { get; set; }
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public string? BatchNo { get; set; }
    public DateTime? ExpDate { get; set; }
    public decimal Amount => Math.Round(Quantity * CostPrice, 2);
}

public sealed class StockDocRequest
{
    public StockDocKind Kind { get; set; }
    public int LocationID { get; set; }
    public int? ToLocationID { get; set; }
    public int? SupplierID { get; set; }
    public string? SupplierInvoiceNo { get; set; }
    public DateTime DocDate { get; set; }
    public decimal Discount { get; set; }
    public decimal PaidAmount { get; set; }
    public string? PayMethod { get; set; }
    public string? Remarks { get; set; }
    public bool UpdateItemPrices { get; set; } = true;
    public int UserID { get; set; }
    public int TerminalID { get; set; }
    public List<StockDocLineDto> Lines { get; set; } = new();
}

public sealed record StockDocResult(int Id, string DocNo);

/* ---------------- Cash / session ---------------- */
public sealed class CashEntryRequest
{
    public bool IsPaidOut { get; set; }
    public decimal Amount { get; set; }
    public string? Category { get; set; }
    public string? Description { get; set; }
    public int? CustomerID { get; set; }
    public int? EmployeeID { get; set; }
    public int? PaymentMethodID { get; set; }
    public string? ChequeNo { get; set; }
    public string? Bank { get; set; }
    public int LocationID { get; set; }
    public int UserID { get; set; }
    public int? ApprovedBy { get; set; }
    public int TerminalID { get; set; }
    public int? SessionID { get; set; }
}

public sealed class SupplierPaymentRequest
{
    public int PurchaseInvoiceID { get; set; }
    public decimal Amount { get; set; }
    public string PayType { get; set; } = "CASH";
    public string? ChequeNo { get; set; }
    public DateTime? ChequeDate { get; set; }
    public string? Remarks { get; set; }
    public int UserID { get; set; }
    public int TerminalID { get; set; }
    public int LocationID { get; set; }
}

public sealed class OpenGrnDto
{
    public int PurchaseInvoiceID { get; set; }
    public string? RefNo { get; set; }
    public string? InvoiceNo { get; set; }
    public DateTime TransactionDate { get; set; }
    public decimal NetTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal BalanceAmount { get; set; }
    public override string ToString() => $"{RefNo}  ({TransactionDate:yyyy-MM-dd})  Balance {BalanceAmount:N2}";
}

public sealed class SessionSummaryDto
{
    public int SessionId { get; set; }
    public string Cashier { get; set; } = "";
    public string Terminal { get; set; } = "";
    public string Location { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public decimal OpeningBalance { get; set; }
    public int Bills { get; set; }
    public int CancelledBills { get; set; }
    public decimal GrossSales { get; set; }
    public decimal Discounts { get; set; }
    public decimal NetSales { get; set; }
    public decimal CashSales { get; set; }
    public decimal CardSales { get; set; }
    public decimal CreditSales { get; set; }
    public decimal ChequeSales { get; set; }
    public decimal OtherSales { get; set; }
    public decimal CreditNoteRedeemed { get; set; }
    public decimal ReceivedPayments { get; set; }
    public decimal PaidOuts { get; set; }
    public decimal CashRefunds { get; set; }
    public decimal ExpectedCash => OpeningBalance + CashSales + ReceivedPayments - PaidOuts - CashRefunds;
    public List<(string Method, decimal Amount)> ByMethod { get; set; } = new();
}

public sealed class CustomerInfoDto
{
    public int CustomerID { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? Mobile { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal Outstanding { get; set; }
    public override string ToString() => $"{Name}  {Mobile}";
}

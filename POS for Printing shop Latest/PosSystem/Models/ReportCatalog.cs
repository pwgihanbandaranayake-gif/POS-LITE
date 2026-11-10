namespace PosSystem.Models;

[Flags]
public enum ReportParams
{
    None = 0,
    DateRange = 1,
    AsOfDate = 2,
    Location = 4,
    User = 8,
    Category = 16,
    Supplier = 32,
    Customer = 64,
    Item = 128,
    Days = 256,
    TopN = 512,
    Employee = 1024
}

public sealed record ReportDefinition(
    string Key,
    string Group,
    string Title,
    string Description,
    string Procedure,
    ReportParams Params,
    string Permission,
    bool Landscape = true)
{
    public bool Has(ReportParams p) => (Params & p) == p;
}

public static class ReportCatalog
{
    private const ReportParams DL = ReportParams.DateRange | ReportParams.Location;

    public static IReadOnlyList<ReportDefinition> All { get; } = new List<ReportDefinition>
    {
        // ---- Sales
        new("daily", "Sales", "Daily Sales Summary", "Bills, discount, tax and payment split per day", "rpt_DailySalesSummary", DL, Perm.ReportsSales),
        new("invoices", "Sales", "Invoice List", "Every bill with cashier, customer and payment", "rpt_SalesInvoiceList", DL | ReportParams.User | ReportParams.Customer, Perm.ReportsSales),
        new("itemwise", "Sales", "Item-wise Sales", "Quantity and value sold per item", "rpt_ItemWiseSales", DL | ReportParams.Category, Perm.ReportsSales),
        new("categorywise", "Sales", "Category-wise Sales", "Sales and share % per category", "rpt_CategoryWiseSales", DL, Perm.ReportsSales),
        new("hourly", "Sales", "Hourly Sales", "Busy hours analysis", "rpt_HourlySales", DL, Perm.ReportsSales),
        new("cashier", "Sales", "Cashier-wise Sales", "Bills, cancels and discounts per cashier", "rpt_CashierWiseSales", DL, Perm.ReportsSales),
        new("paymethods", "Sales", "Payment Method Summary", "Cash / card / credit / QR totals", "rpt_PaymentMethodSummary", DL, Perm.ReportsSales),
        new("discounts", "Sales", "Discount Report", "Item and bill discounts given", "rpt_DiscountReport", DL | ReportParams.User, Perm.ReportsSales),
        new("tax", "Sales", "Tax Report", "Taxable value and tax collected", "rpt_TaxReport", DL, Perm.ReportsSales),
        new("salesreturns", "Sales", "Sales Returns", "Returned items, refunds and credit notes", "rpt_SalesReturns", DL, Perm.ReportsSales),
        new("cancelled", "Sales", "Cancelled Bills", "Cancelled invoices with reason", "rpt_CancelledBills", DL, Perm.ReportsSales),
        new("fastmoving", "Sales", "Fast Moving Items", "Top selling items by quantity", "rpt_FastMovingItems", DL | ReportParams.TopN, Perm.ReportsSales),

        // ---- Stock
        new("stock", "Stock", "Current Stock", "Stock in hand with cost and sales value", "rpt_CurrentStock", ReportParams.Location | ReportParams.Category, Perm.ReportsStock),
        new("rol", "Stock", "Re-order Level", "Items at or below re-order level", "rpt_ReorderLevel", ReportParams.Location, Perm.ReportsStock),
        new("movement", "Stock", "Stock Movement (Stock Card)", "Every in/out movement with running balance", "rpt_StockMovement", DL | ReportParams.Item, Perm.ReportsStock),
        new("expiry", "Stock", "Expiry Report", "Batches expiring within N days", "rpt_ExpiryReport", ReportParams.Days | ReportParams.Location, Perm.ReportsStock),
        new("nonmoving", "Stock", "Non-moving Items", "Items in stock with no sales in period", "rpt_NonMovingItems", DL, Perm.ReportsStock),
        new("transfers", "Stock", "Stock Transfers", "Transfers between locations", "rpt_StockTransfers", DL, Perm.ReportsStock),
        new("adjustments", "Stock", "Stock Adjustments", "Damages, wastage and stock-take adjustments", "rpt_StockAdjustments", DL, Perm.ReportsStock),
        new("pricechanges", "Stock", "Price Change Log", "Selling price change history", "rpt_PriceChangeLog", ReportParams.DateRange, Perm.ReportsStock),

        // ---- Purchase
        new("purchsummary", "Purchase", "Purchase Summary (GRN)", "GRN totals, paid and balance", "rpt_PurchaseSummary", DL | ReportParams.Supplier, Perm.ReportsPurchase),
        new("purchdetail", "Purchase", "Purchase Detail", "GRN lines with batch and expiry", "rpt_PurchaseDetail", DL | ReportParams.Supplier, Perm.ReportsPurchase),
        new("purchreturns", "Purchase", "Purchase Returns", "Items returned to suppliers", "rpt_PurchaseReturns", DL | ReportParams.Supplier, Perm.ReportsPurchase),
        new("supplieros", "Purchase", "Supplier Outstanding", "Amount payable per supplier", "rpt_SupplierOutstanding", ReportParams.AsOfDate, Perm.ReportsPurchase),

        // ---- Financial
        new("profit", "Financial", "Gross Profit by Item", "Revenue, cost, profit and margin", "rpt_GrossProfit", DL | ReportParams.Category, Perm.ReportsFinancial),
        new("customeros", "Financial", "Customer Outstanding", "Credit customers balance", "rpt_CustomerOutstanding", ReportParams.AsOfDate, Perm.ReportsFinancial),
        new("sessions", "Financial", "Cashier Sessions (Shift)", "Opening, expected, counted, excess/shortage", "rpt_SessionSummary", DL | ReportParams.User, Perm.ReportsFinancial),
        new("cashinout", "Financial", "Paid Outs & Receipts", "Cash going out and coming in", "rpt_CashInOut", DL | ReportParams.Employee, Perm.ReportsFinancial),
        new("audit", "Financial", "Audit Log", "Overrides, cancellations, drawer opens", "rpt_AuditLog", ReportParams.DateRange | ReportParams.User, Perm.ReportsFinancial),
    };
}

namespace PosSystem.Data;

public static class SalesRepository
{

    /// <summary>
    /// The whole bill is written in ONE transaction: number, header, lines, FIFO stock deduction,
    /// stock movements, payments and credit-note redemption. Nothing touches the DB while the cashier
    /// is scanning, so a crash or power-cut can no longer leave half bills or wrong stock.
    /// </summary>
    public static Task<SaleResult> SaveSaleAsync(SaleRequest r) => Db.InTransactionAsync(async (cn, tx) =>
    {
        var invoiceNo = await Db.NextDocNoAsync(cn, tx, DocumentTypes.Invoice, r.LocationId);

        decimal Kind(PaymentKind k) => r.Payments.Where(p => p.Kind == k).Sum(p => p.Amount);
        var cash = Kind(PaymentKind.Cash);
        var card = Kind(PaymentKind.Card);
        var credit = Kind(PaymentKind.Credit);
        var cheque = Kind(PaymentKind.Cheque);
        var payType = r.Payments.Count == 1 ? r.Payments[0].Code : r.Payments.Count == 0 ? "CASH" : "MULTI";

        var invoiceId = await cn.ExecuteScalarAsync<int>(@"
            INSERT dbo.SalesInvoices(CustomerID, CustomerName, InvoiceNo, TransactionDate, StartTime, EndTime,
                TotalAmount, TotalDiscount, TotalGross, RoundupDiscount, NetTotal, CashPaidAmount, ChangeBalanceAmount,
                TotalCashAmount, TotalCreditAmount, TotalCardAmount, TotalChequeAmount, TotalEmployeeCredit, TotalRefundAmount,
                TotalDonationAmount, TotalStaffAmount, TotalStoresAmount, TotalLostAmount, TotalPartnerAmount,
                Remarks, PayType, SalesSessionID, IsCanceled, IsDeleted, CreatedBy, DateCreated, PCCreated,
                IsLoyalityCardDiscount, IsOrder, CreditBalance, HasSync, LocationID, BillDiscountType, BillDiscountValue,
                BillDiscountAmount, LineDiscountAmount, TotalTax, PricesIncludeTax)
            VALUES(@CustomerId, @CustomerName, @invoiceNo, GETDATE(), @StartTime, GETDATE(),
                @GrossTotal, @totalDiscount, @GrossTotal, @RoundOff, @NetTotal, @CashTendered, @Change,
                @cash, @credit, @card, @cheque, 0, 0,
                0, 0, 0, 0, 0,
                @Remarks, @payType, @SessionId, 0, 0, @UserId, GETDATE(), @TerminalId,
                0, 0, @credit, 0, @LocationId, @billType, @BillDiscountValue,
                @BillDiscountAmount, @LineDiscountTotal, @TaxTotal, @PricesIncludeTax);
            SELECT CAST(SCOPE_IDENTITY() AS INT);",
            new
            {
                r.CustomerId, r.CustomerName, invoiceNo, r.StartTime, r.GrossTotal,
                totalDiscount = r.LineDiscountTotal + r.BillDiscountAmount, r.RoundOff, r.NetTotal, r.CashTendered, r.Change,
                cash, credit, card, cheque, r.Remarks, payType, r.SessionId, r.UserId, r.TerminalId, r.LocationId,
                billType = (byte)r.BillDiscountType, r.BillDiscountValue, r.BillDiscountAmount, r.LineDiscountTotal, r.TaxTotal, r.PricesIncludeTax
            }, tx);

        int lineNo = 0;
        foreach (var l in r.Lines)
        {
            lineNo++;
            var info = await cn.QueryFirstAsync<(bool IsService, decimal Cost)>(
                "SELECT CAST(ISNULL(IsService,0) AS BIT), ISNULL(CostPrice,0) FROM dbo.Item WHERE ItemID = @ItemId", new { l.ItemId }, tx);

            var detailId = await cn.ExecuteScalarAsync<int>(@"
                INSERT dbo.SalesInvoiceDetails(SalesInvoiceID, ItemID, Rate, Quantity, Amount, Discount, DiscountVal, NetAmount,
                    IsDeleted, CreatedBy, DateCreated, PCCreated, StockRate, StockID, LineNumber, DiscountType, BillDiscountShare,
                    TaxID, TaxRate, TaxAmount, CostPrice, ReturnedQty, LineNote)
                VALUES(@invoiceId, @ItemId, @UnitPrice, @Quantity, @Gross, @DiscountValue, @LineDiscount, @Net,
                    0, @UserId, GETDATE(), @TerminalId, @UnitPrice, @ItemStockId, @lineNo, @dtype, @BillDiscountShare,
                    @TaxId, @TaxRate, @TaxAmount, @cost, 0, @lineNote);
                SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new
                {
                    invoiceId, l.ItemId, l.UnitPrice, l.Quantity, l.Gross, l.DiscountValue, l.LineDiscount, Net = l.Net,
                    r.UserId, r.TerminalId, l.ItemStockId, lineNo, dtype = (byte)l.DiscountType, l.BillDiscountShare,
                    l.TaxId, l.TaxRate, l.TaxAmount, cost = info.Cost,
                    lineNote = string.IsNullOrWhiteSpace(l.LineNote) ? null : l.LineNote.Trim()
                }, tx);

            if (info.IsService) continue;

            var taken = await StockLedger.DeductAsync(cn, tx, l.ItemId, r.LocationId, l.Quantity, l.ItemStockId, r.AllowNegativeStock,
                l.ItemName, new MovementInfo(StockMovementTypes.Sale, invoiceId, invoiceNo, r.UserId, r.TerminalId, detailId));

            var qty = taken.Sum(t => t.Qty);
            if (qty > 0)
            {
                var avgCost = Money.R(taken.Sum(t => t.Qty * t.Cost) / qty);
                await cn.ExecuteAsync("UPDATE dbo.SalesInvoiceDetails SET CostPrice = @avgCost, StockID = @sid WHERE SalesInvoiceDetailID = @detailId",
                    new { avgCost, sid = taken[0].ItemStockId, detailId }, tx);
            }
        }

        foreach (var p in r.Payments)
        {
            await cn.ExecuteAsync(@"
                INSERT dbo.SalesInvoicePayments(SalesInvoiceID, DatePaid, Amount, PayType, CardType, CardNo, ChequeNo, ChequeDate,
                    IsDeleted, CreatedBy, DateCreated, PCCreated, PaymentMethodID, ReferenceNo, CreditNoteID)
                VALUES(@invoiceId, GETDATE(), @Amount, @Code, @CardType, @cardNo, @chequeNo, @chequeDate,
                    0, @UserId, GETDATE(), @TerminalId, @PaymentMethodId, @Reference, @CreditNoteId)",
                new
                {
                    invoiceId, p.Amount, p.Code, p.CardType,
                    cardNo = p.Kind == PaymentKind.Card ? p.Reference : null,
                    chequeNo = p.Kind == PaymentKind.Cheque ? p.Reference : null,
                    chequeDate = p.ChequeDate?.ToString("yyyy-MM-dd"),
                    r.UserId, r.TerminalId, p.PaymentMethodId, p.Reference, p.CreditNoteId
                }, tx);

            if (p.Kind == PaymentKind.CreditNote && p.CreditNoteId.HasValue)
            {
                var ok = await cn.ExecuteAsync("UPDATE dbo.CreditNotes SET Balance = Balance - @Amount WHERE CreditNoteID = @CreditNoteId AND Balance >= @Amount AND IsCancelled = 0",
                    new { p.Amount, p.CreditNoteId }, tx);
                if (ok == 0) throw new AppException($"Credit note {p.Reference} has insufficient balance.");
            }
        }

        return new SaleResult(invoiceId, invoiceNo);
    });

    public static async Task<ReceiptData?> GetReceiptAsync(int invoiceId)
    {
        await using var cn = await Db.OpenAsync();
        var h = await cn.QueryFirstOrDefaultAsync<HeaderRow>(@"
            SELECT s.InvoiceNo, s.TransactionDate, u.FullName AS Cashier, pc.Description AS Terminal, l.LocationName AS Location,
                   ISNULL(c.Name, s.CustomerName) AS Customer, ISNULL(s.TotalAmount,0) AS Gross,
                   ISNULL(s.LineDiscountAmount, ISNULL(s.TotalDiscount,0)) AS LineDiscount, ISNULL(s.BillDiscountAmount,0) AS BillDiscount,
                   CAST(ISNULL(s.BillDiscountType,0) AS INT) AS BillDiscountType, ISNULL(s.BillDiscountValue,0) AS BillDiscountValue,
                   ISNULL(s.TotalTax,0) AS Tax, CAST(ISNULL(s.PricesIncludeTax,1) AS BIT) AS PricesIncludeTax,
                   ISNULL(s.RoundupDiscount,0) AS RoundOff, ISNULL(s.NetTotal,0) AS NetTotal,
                   ISNULL(s.CashPaidAmount,0) AS CashTendered, ISNULL(s.ChangeBalanceAmount,0) AS Change,
                   CAST(ISNULL(s.IsCanceled,0) AS BIT) AS IsCanceled
            FROM dbo.SalesInvoices s
            LEFT JOIN dbo.UserProfile u ON u.UserProfileId = s.CreatedBy
            LEFT JOIN dbo.PcRegistration pc ON pc.PcRegistrationID = s.PCCreated
            LEFT JOIN dbo.Locations l ON l.LocationID = ISNULL(s.LocationID,1)
            LEFT JOIN dbo.Customers c ON c.CustomerID = s.CustomerID
            WHERE s.SalesInvoiceID = @invoiceId", new { invoiceId });
        if (h == null) return null;

        var lines = (await cn.QueryAsync<ReceiptLine>(@"
            SELECT i.ItemCode AS Code, ISNULL(i.ItemName, i.ItemDescription) + CASE WHEN ISNULL(d.LineNote, '') = '' THEN '' ELSE ' (' + d.LineNote + ')' END AS Name, ISNULL(d.Quantity,0) AS Qty, ISNULL(d.Rate,0) AS Price,
                   ISNULL(d.DiscountVal,0) AS Discount, ISNULL(d.Amount,0) - ISNULL(d.DiscountVal,0) AS Amount
            FROM dbo.SalesInvoiceDetails d JOIN dbo.Item i ON i.ItemID = d.ItemID
            WHERE d.SalesInvoiceID = @invoiceId AND ISNULL(d.IsDeleted,0) = 0
            ORDER BY ISNULL(d.LineNumber, d.SalesInvoiceDetailID)", new { invoiceId })).ToList();

        var pays = (await cn.QueryAsync<(string Name, decimal Amount, string? Ref)>(@"
            SELECT p.MethodName, p.Amount, p.ReferenceNo FROM dbo.vw_SalesPayments p WHERE p.SalesInvoiceID = @invoiceId
            ORDER BY p.SalesInvoicePaymentID", new { invoiceId })).ToList();

        var r = new ReceiptData
        {
            Title = h.IsCanceled ? "INVOICE  (CANCELLED)" : "INVOICE",
            DocumentNo = h.InvoiceNo ?? "", Date = h.TransactionDate, Cashier = h.Cashier ?? "", Terminal = h.Terminal ?? "",
            Location = h.Location ?? "", Customer = h.Customer, Lines = lines, GrossTotal = h.Gross, LineDiscount = h.LineDiscount,
            BillDiscount = h.BillDiscount, Tax = h.Tax, TaxInclusive = h.PricesIncludeTax, RoundOff = h.RoundOff, NetTotal = h.NetTotal,
            Payments = pays, CashTendered = h.CashTendered, Change = h.Change, TotalQty = lines.Sum(l => l.Qty), ItemCount = lines.Count,
            BillDiscountLabel = h.BillDiscountType == (int)DiscountType.Percent ? $"Bill discount {h.BillDiscountValue:0.##}%" : "Bill discount"
        };
        if (pays.Count > 0 && r.LineDiscount == 0 && r.BillDiscount == 0 && h.Gross > 0 && lines.Sum(l => l.Discount) > 0)
            r.LineDiscount = lines.Sum(l => l.Discount);
        return r;
    }

    private sealed class HeaderRow
    {
        public string? InvoiceNo { get; set; }
        public DateTime TransactionDate { get; set; }
        public string? Cashier { get; set; }
        public string? Terminal { get; set; }
        public string? Location { get; set; }
        public string? Customer { get; set; }
        public decimal Gross { get; set; }
        public decimal LineDiscount { get; set; }
        public decimal BillDiscount { get; set; }
        public int BillDiscountType { get; set; }
        public decimal BillDiscountValue { get; set; }
        public decimal Tax { get; set; }
        public bool PricesIncludeTax { get; set; }
        public decimal RoundOff { get; set; }
        public decimal NetTotal { get; set; }
        public decimal CashTendered { get; set; }
        public decimal Change { get; set; }
        public bool IsCanceled { get; set; }
    }

    public static async Task<int?> FindInvoiceIdAsync(string invoiceNo)
    {
        await using var cn = await Db.OpenAsync();
        return await cn.ExecuteScalarAsync<int?>("SELECT TOP 1 SalesInvoiceID FROM dbo.SalesInvoices WHERE InvoiceNo = @invoiceNo AND ISNULL(IsDeleted,0) = 0 ORDER BY SalesInvoiceID DESC",
            new { invoiceNo = invoiceNo.Trim() });
    }

    public static async Task<int?> GetLastInvoiceIdAsync(int terminalId)
    {
        await using var cn = await Db.OpenAsync();
        return await cn.ExecuteScalarAsync<int?>("SELECT MAX(SalesInvoiceID) FROM dbo.SalesInvoices WHERE PCCreated = @terminalId AND NetTotal IS NOT NULL", new { terminalId });
    }

    public static async Task<IReadOnlyList<InvoiceSummaryDto>> ListInvoicesAsync(DateTime from, DateTime to, int? locationId, string? search)
    {
        await using var cn = await Db.OpenAsync();
        return (await cn.QueryAsync<InvoiceSummaryDto>(@"
            SELECT TOP 1000 s.SalesInvoiceID, s.InvoiceNo, s.TransactionDate, u.FullName AS Cashier, ISNULL(c.Name, s.CustomerName) AS Customer,
                   ISNULL(s.NetTotal, 0) AS NetTotal, CAST(ISNULL(s.IsCanceled,0) AS BIT) AS IsCanceled,
                   STUFF((SELECT ', ' + p.MethodName FROM dbo.vw_SalesPayments p WHERE p.SalesInvoiceID = s.SalesInvoiceID FOR XML PATH('')), 1, 2, '') AS Payments
            FROM dbo.SalesInvoices s
            LEFT JOIN dbo.UserProfile u ON u.UserProfileId = s.CreatedBy
            LEFT JOIN dbo.Customers c ON c.CustomerID = s.CustomerID
            WHERE ISNULL(s.IsDeleted,0) = 0 AND s.NetTotal IS NOT NULL
              AND s.TransactionDate >= @from AND s.TransactionDate < DATEADD(DAY,1,@to)
              AND (@locationId IS NULL OR ISNULL(s.LocationID,1) = @locationId)
              AND (@search IS NULL OR s.InvoiceNo LIKE '%' + @search + '%' OR c.Name LIKE '%' + @search + '%' OR s.CustomerName LIKE '%' + @search + '%')
            ORDER BY s.SalesInvoiceID DESC",
            new { from = from.Date, to = to.Date, locationId, search = string.IsNullOrWhiteSpace(search) ? null : search.Trim() })).ToList();
    }

    /// <summary>Cancels a saved bill: reverses exactly the stock batches it used and restores redeemed credit notes.</summary>
    public static Task CancelInvoiceAsync(int invoiceId, int userId, string reason, int terminalId) => Db.InTransactionAsync(async (cn, tx) =>
    {
        var inv = await cn.QueryFirstOrDefaultAsync<(string InvoiceNo, bool IsCanceled, int LocationID)>(
            "SELECT InvoiceNo, CAST(ISNULL(IsCanceled,0) AS BIT), ISNULL(LocationID,1) FROM dbo.SalesInvoices WITH (UPDLOCK) WHERE SalesInvoiceID = @invoiceId",
            new { invoiceId }, tx);
        if (inv.InvoiceNo == null) throw new AppException("Invoice not found.");
        if (inv.IsCanceled) throw new AppException("This invoice is already cancelled.");
        var returned = await cn.ExecuteScalarAsync<decimal>("SELECT ISNULL(SUM(ReturnedQty),0) FROM dbo.SalesInvoiceDetails WHERE SalesInvoiceID = @invoiceId", new { invoiceId }, tx);
        if (returned > 0) throw new AppException("Items of this invoice were already returned. It cannot be cancelled.");

        var mv = new MovementInfo(StockMovementTypes.SaleCancel, invoiceId, inv.InvoiceNo, userId, terminalId);
        var moves = (await cn.QueryAsync<(int ItemID, int LocationID, int? ItemStockID, decimal QtyOut, int? RefLineID)>(
            "SELECT ItemID, LocationID, ItemStockID, QtyOut, RefLineID FROM dbo.StockMovements WHERE MovementType = 'SALE' AND RefID = @invoiceId",
            new { invoiceId }, tx)).ToList();

        if (moves.Count > 0)
        {
            foreach (var m in moves)
                await StockLedger.AddAsync(cn, tx, m.ItemID, m.LocationID, m.QtyOut, m.ItemStockID, null, null, null, null, mv with { RefLineId = m.RefLineID });
        }
        else
        {   // bills created by the old system have no movements: restore from the detail lines
            var lines = await cn.QueryAsync<(int ItemID, decimal Quantity, int? StockID, bool IsService)>(@"
                SELECT d.ItemID, ISNULL(d.Quantity,0), d.StockID, CAST(ISNULL(i.IsService,0) AS BIT)
                FROM dbo.SalesInvoiceDetails d JOIN dbo.Item i ON i.ItemID = d.ItemID
                WHERE d.SalesInvoiceID = @invoiceId AND ISNULL(d.IsDeleted,0) = 0", new { invoiceId }, tx);
            foreach (var l in lines.Where(x => !x.IsService && x.Quantity > 0))
                await StockLedger.AddAsync(cn, tx, l.ItemID, inv.LocationID, l.Quantity, l.StockID, null, null, null, null, mv);
        }

        await cn.ExecuteAsync(@"
            UPDATE cn SET cn.Balance = cn.Balance + p.Amount
            FROM dbo.CreditNotes cn JOIN dbo.SalesInvoicePayments p ON p.CreditNoteID = cn.CreditNoteID
            WHERE p.SalesInvoiceID = @invoiceId AND ISNULL(p.IsDeleted,0) = 0", new { invoiceId }, tx);

        await cn.ExecuteAsync(@"UPDATE dbo.SalesInvoices SET IsCanceled = 1, CancelledBy = @userId, CancelledDate = GETDATE(),
                                CancelReason = @reason, ModifiedBy = @userId, PCModified = @terminalId WHERE SalesInvoiceID = @invoiceId",
            new { userId, reason, terminalId, invoiceId }, tx);

        await cn.ExecuteAsync("INSERT dbo.AuditLogs(UserID, PCCreated, Action, RefNo, Details) VALUES(@userId, @terminalId, 'INVOICE_CANCEL', @no, @reason)",
            new { userId, terminalId, no = inv.InvoiceNo, reason }, tx);
        return 0;
    });
}

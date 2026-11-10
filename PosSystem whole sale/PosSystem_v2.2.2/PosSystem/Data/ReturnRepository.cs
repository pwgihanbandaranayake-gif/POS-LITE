namespace PosSystem.Data;

public static class ReturnRepository
{

    public static async Task<InvoiceForReturnDto?> GetInvoiceForReturnAsync(string invoiceNo)
    {
        await using var cn = await Db.OpenAsync();
        var inv = await cn.QueryFirstOrDefaultAsync<InvoiceForReturnDto>(@"
            SELECT TOP 1 s.SalesInvoiceID, s.InvoiceNo, s.TransactionDate, s.CustomerID, ISNULL(c.Name, s.CustomerName) AS CustomerName,
                   ISNULL(s.NetTotal,0) AS NetTotal, ISNULL(s.LocationID,1) AS LocationID
            FROM dbo.SalesInvoices s LEFT JOIN dbo.Customers c ON c.CustomerID = s.CustomerID
            WHERE s.InvoiceNo = @invoiceNo AND ISNULL(s.IsDeleted,0) = 0 AND ISNULL(s.IsCanceled,0) = 0 AND s.NetTotal IS NOT NULL
            ORDER BY s.SalesInvoiceID DESC", new { invoiceNo = invoiceNo.Trim() });
        if (inv == null) return null;

        inv.Lines = (await cn.QueryAsync<ReturnableLineDto>(@"
            SELECT d.SalesInvoiceDetailID, d.ItemID, d.StockID AS ItemStockID, i.ItemCode, ISNULL(i.ItemName, i.ItemDescription) + CASE WHEN ISNULL(d.LineNote, '') = '' THEN '' ELSE ' (' + d.LineNote + ')' END AS ItemName,
                   ISNULL(d.Quantity,0) AS SoldQty, ISNULL(d.ReturnedQty,0) AS ReturnedQty,
                   CAST((ISNULL(d.NetAmount, ISNULL(d.Amount,0))
                        + CASE WHEN ISNULL(s.PricesIncludeTax,1) = 0 THEN ISNULL(d.TaxAmount,0) ELSE 0 END)
                        / NULLIF(d.Quantity,0) AS DECIMAL(18,2)) AS NetRate
            FROM dbo.SalesInvoiceDetails d
            JOIN dbo.SalesInvoices s ON s.SalesInvoiceID = d.SalesInvoiceID
            JOIN dbo.Item i ON i.ItemID = d.ItemID
            WHERE d.SalesInvoiceID = @id AND ISNULL(d.IsDeleted,0) = 0 AND ISNULL(d.Quantity,0) > 0
            ORDER BY ISNULL(d.LineNumber, d.SalesInvoiceDetailID)", new { id = inv.SalesInvoiceID })).ToList();
        return inv;
    }

    /// <summary>
    /// Return in one transaction: validate returnable qty, mark lines returned, put good items back into stock
    /// (damaged items are not restocked), and issue a credit note when the refund is not paid in cash.
    /// </summary>
    public static Task<SalesReturnResult> SaveSalesReturnAsync(SalesReturnRequest r) => Db.InTransactionAsync(async (cn, tx) =>
    {
        if (r.Lines.Count == 0 || r.Lines.All(l => l.Quantity <= 0)) throw new AppException("Nothing to return.");
        var returnNo = await Db.NextDocNoAsync(cn, tx, DocumentTypes.SalesReturn, r.LocationID);
        var total = Money.R(r.Lines.Where(l => l.Quantity > 0).Sum(l => l.Amount));

        var returnId = await cn.ExecuteScalarAsync<int>(@"
            INSERT dbo.SalesReturns(ReturnNo, ReturnDate, SalesInvoiceID, InvoiceNo, CustomerID, LocationID, SalesSessionID, TotalAmount,
                                    RefundMethod, Reason, CreatedBy, PCCreated)
            VALUES(@returnNo, GETDATE(), @SalesInvoiceID, @InvoiceNo, @CustomerID, @LocationID, @SessionID, @total, @rm, @Reason, @UserID, @TerminalID);
            SELECT CAST(SCOPE_IDENTITY() AS INT);",
            new { returnNo, r.SalesInvoiceID, r.InvoiceNo, r.CustomerID, r.LocationID, r.SessionID, total, rm = (byte)r.RefundMethod, r.Reason, r.UserID, r.TerminalID }, tx);

        foreach (var l in r.Lines.Where(x => x.Quantity > 0))
        {
            if (l.SalesInvoiceDetailID.HasValue)
            {
                var avail = await cn.ExecuteScalarAsync<decimal>(
                    "SELECT ISNULL(Quantity,0) - ISNULL(ReturnedQty,0) FROM dbo.SalesInvoiceDetails WITH (UPDLOCK) WHERE SalesInvoiceDetailID = @id",
                    new { id = l.SalesInvoiceDetailID }, tx);
                if (l.Quantity > avail) throw new AppException($"'{l.ItemName}': only {avail:0.###} can be returned.");
                await cn.ExecuteAsync("UPDATE dbo.SalesInvoiceDetails SET ReturnedQty = ISNULL(ReturnedQty,0) + @q WHERE SalesInvoiceDetailID = @id",
                    new { q = l.Quantity, id = l.SalesInvoiceDetailID }, tx);
            }

            int? stockId = null;
            var isService = await cn.ExecuteScalarAsync<bool>("SELECT CAST(ISNULL(IsService,0) AS BIT) FROM dbo.Item WHERE ItemID = @ItemID", new { l.ItemID }, tx);
            if (!l.IsDamaged && !isService)
                stockId = await StockLedger.AddAsync(cn, tx, l.ItemID, r.LocationID, l.Quantity, l.ItemStockID, null, null, null, null,
                    new MovementInfo(StockMovementTypes.SalesReturn, returnId, returnNo, r.UserID, r.TerminalID, l.SalesInvoiceDetailID));

            await cn.ExecuteAsync(@"
                INSERT dbo.SalesReturnDetails(SalesReturnID, SalesInvoiceDetailID, ItemID, Quantity, Rate, Amount, IsDamaged, ItemStockID)
                VALUES(@returnId, @SalesInvoiceDetailID, @ItemID, @Quantity, @Rate, @Amount, @IsDamaged, @stockId)",
                new { returnId, l.SalesInvoiceDetailID, l.ItemID, l.Quantity, l.Rate, l.Amount, l.IsDamaged, stockId }, tx);
        }

        string? cnNo = null;
        if (r.RefundMethod == RefundMethod.CreditNote)
        {
            cnNo = await Db.NextDocNoAsync(cn, tx, DocumentTypes.CreditNote, r.LocationID);
            var cnId = await cn.ExecuteScalarAsync<int>(@"
                INSERT dbo.CreditNotes(CreditNoteNo, CustomerID, LocationID, Amount, Balance, IssueDate, ExpiryDate, SalesReturnID, CreatedBy)
                VALUES(@cnNo, @CustomerID, @LocationID, @total, @total, GETDATE(),
                       CASE WHEN @days > 0 THEN DATEADD(DAY, @days, CAST(GETDATE() AS DATE)) END, @returnId, @UserID);
                SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new { cnNo, r.CustomerID, r.LocationID, total, days = r.CreditNoteValidDays, returnId, r.UserID }, tx);
            await cn.ExecuteAsync("UPDATE dbo.SalesReturns SET CreditNoteID = @cnId WHERE SalesReturnID = @returnId", new { cnId, returnId }, tx);
        }

        return new SalesReturnResult(returnId, returnNo, total, cnNo);
    });

    public static async Task<CreditNoteDto?> GetCreditNoteAsync(string creditNoteNo)
    {
        await using var cn = await Db.OpenAsync();
        return await cn.QueryFirstOrDefaultAsync<CreditNoteDto>(
            "SELECT CreditNoteID, CreditNoteNo, Amount, Balance, ExpiryDate, IsCancelled FROM dbo.CreditNotes WHERE CreditNoteNo = @no",
            new { no = creditNoteNo.Trim() });
    }
}

namespace PosSystem.Data;

/// <summary>GRN, purchase return, transfer and adjustment - all through the StockLedger, each in one transaction.</summary>
public static class StockRepository
{

    public static Task<StockDocResult> SaveAsync(StockDocRequest r)
    {
        if (r.Lines.Count == 0) throw new AppException("Add at least one item.");
        if (r.Kind != StockDocKind.Adjustment && r.Lines.Any(l => l.Quantity <= 0)) throw new AppException("Quantities must be greater than zero.");
        if (r.Kind == StockDocKind.Adjustment && r.Lines.Any(l => l.Quantity == 0)) throw new AppException("Adjustment quantity cannot be zero.");

        return Db.InTransactionAsync((cn, tx) => r.Kind switch
        {
            StockDocKind.Grn => SaveGrnAsync(cn, tx, r),
            StockDocKind.PurchaseReturn => SavePurchaseReturnAsync(cn, tx, r),
            StockDocKind.Transfer => SaveTransferAsync(cn, tx, r),
            StockDocKind.Adjustment => SaveAdjustmentAsync(cn, tx, r),
            _ => throw new ArgumentOutOfRangeException()
        });
    }

    private static async Task<StockDocResult> SaveGrnAsync(SqlConnection cn, SqlTransaction tx, StockDocRequest r)
    {
        if (r.SupplierID == null) throw new AppException("Select a supplier.");
        var no = await Db.NextDocNoAsync(cn, tx, DocumentTypes.Grn, r.LocationID);
        var total = Money.R(r.Lines.Sum(l => l.Amount));
        var net = Money.R(total - r.Discount);
        var paid = Math.Min(r.PaidAmount, net);

        var id = await cn.ExecuteScalarAsync<int>(@"
            INSERT dbo.PurchaseInvoices(SupplierID, InvoiceNo, RefNo, TransactionDate, TotalAmount, Discount, ServiceCharges, NetTotal,
                OtherExpences, Remarks, PaidAmount, BalanceAmount, IsDeleted, CreatedBy, DateCreated, PCCreated, LocationID)
            VALUES(@SupplierID, @SupplierInvoiceNo, @no, @DocDate, @total, @Discount, 0, @net, 0, @Remarks, @paid, @balance, 0,
                @UserID, GETDATE(), @TerminalID, @LocationID);
            SELECT CAST(SCOPE_IDENTITY() AS INT);",
            new { r.SupplierID, r.SupplierInvoiceNo, no, r.DocDate, total, r.Discount, net, r.Remarks, paid, balance = net - paid, r.UserID, r.TerminalID, r.LocationID }, tx);

        foreach (var l in r.Lines)
        {
            var mv = new MovementInfo(StockMovementTypes.Purchase, id, no, r.UserID, r.TerminalID);
            var stockId = await StockLedger.AddAsync(cn, tx, l.ItemID, r.LocationID, l.Quantity, null, l.CostPrice, l.SellingPrice,
                string.IsNullOrWhiteSpace(l.BatchNo) ? null : l.BatchNo.Trim(), l.ExpDate?.Date, mv, id);

            await cn.ExecuteAsync(@"
                INSERT dbo.PurchaseInvoiceDetails(PurchaseInvoiceID, ItemID, Price, Quantity, Amount, IsDeleted, CreatedBy, DateCreated, PCCreated,
                    SellingPrice, BatchNo, ExpDate, ItemStockID)
                VALUES(@id, @ItemID, @CostPrice, @Quantity, @Amount, 0, @UserID, GETDATE(), @TerminalID, @SellingPrice, @BatchNo, @ExpDate, @stockId)",
                new { id, l.ItemID, l.CostPrice, l.Quantity, l.Amount, r.UserID, r.TerminalID, l.SellingPrice, l.BatchNo, l.ExpDate, stockId }, tx);

            if (r.UpdateItemPrices)
            {
                var old = await cn.ExecuteScalarAsync<decimal>("SELECT ISNULL(SellingPrice,0) FROM dbo.Item WHERE ItemID = @ItemID", new { l.ItemID }, tx);
                await cn.ExecuteAsync("UPDATE dbo.Item SET CostPrice = @CostPrice, SellingPrice = @SellingPrice, DateModified = GETDATE(), ModifiedBy = @UserID WHERE ItemID = @ItemID",
                    new { l.CostPrice, l.SellingPrice, r.UserID, l.ItemID }, tx);
                if (old != l.SellingPrice)
                    await cn.ExecuteAsync(@"INSERT dbo.ItemPriceChangeLogs(RefNo, ItemID, ExistingPrice, NewPrice, IsDeleted, ModifiedBy, CreatedBy, DateCreated, PCCreated, HasOnlineSync)
                                            VALUES(@no, @ItemID, @old, @SellingPrice, 0, @u, @UserID, GETDATE(), @TerminalID, 0)",
                        new { no, l.ItemID, old, l.SellingPrice, u = r.UserID.ToString(), r.UserID, r.TerminalID }, tx);
            }
        }

        if (paid > 0)
            await cn.ExecuteAsync(@"INSERT dbo.PurchaseInvoicePayments(PurchaseInvoiceID, DatePaid, Amount, PayType, Remarks, IsDeleted, CreatedBy, DateCreated, PCCreated, PaymentNo)
                                    VALUES(@id, GETDATE(), @paid, @pt, 'Paid at GRN', 0, @UserID, GETDATE(), @TerminalID, @no)",
                new { id, paid, pt = r.PayMethod ?? "CASH", r.UserID, r.TerminalID, no }, tx);

        return new StockDocResult(id, no);
    }

    private static async Task<StockDocResult> SavePurchaseReturnAsync(SqlConnection cn, SqlTransaction tx, StockDocRequest r)
    {
        if (r.SupplierID == null) throw new AppException("Select a supplier.");
        var no = await Db.NextDocNoAsync(cn, tx, DocumentTypes.PurchaseReturn, r.LocationID);
        var total = Money.R(r.Lines.Sum(l => l.Amount));
        var id = await cn.ExecuteScalarAsync<int>(@"
            INSERT dbo.PurchaseReturns(ReturnNo, ReturnDate, SupplierID, LocationID, TotalAmount, Remarks, CreatedBy)
            VALUES(@no, @DocDate, @SupplierID, @LocationID, @total, @Remarks, @UserID);
            SELECT CAST(SCOPE_IDENTITY() AS INT);", new { no, r.DocDate, r.SupplierID, r.LocationID, total, r.Remarks, r.UserID }, tx);

        foreach (var l in r.Lines)
        {
            var taken = await StockLedger.DeductAsync(cn, tx, l.ItemID, r.LocationID, l.Quantity, l.ItemStockID, false, l.ItemName,
                new MovementInfo(StockMovementTypes.PurchaseReturn, id, no, r.UserID, r.TerminalID));
            foreach (var t in taken)
                await cn.ExecuteAsync(@"INSERT dbo.PurchaseReturnDetails(PurchaseReturnID, ItemID, ItemStockID, Quantity, Rate, Amount)
                                        VALUES(@id, @ItemID, @sid, @q, @CostPrice, @amt)",
                    new { id, l.ItemID, sid = t.ItemStockId, q = t.Qty, l.CostPrice, amt = Money.R(t.Qty * l.CostPrice) }, tx);
        }
        return new StockDocResult(id, no);
    }

    private static async Task<StockDocResult> SaveTransferAsync(SqlConnection cn, SqlTransaction tx, StockDocRequest r)
    {
        if (r.ToLocationID == null || r.ToLocationID == r.LocationID) throw new AppException("Select a different destination location.");
        var no = await Db.NextDocNoAsync(cn, tx, DocumentTypes.Transfer, r.LocationID);
        var id = await cn.ExecuteScalarAsync<int>(@"
            INSERT dbo.Transfers(TransferNo, TransactionDate, TotalAmount, ReqestedBy, Remarks, IsDeleted, CreatedBy, DateCreated, PCCreated, FromLocationID, ToLocationID)
            VALUES(@no, @DocDate, 0, @UserID, @Remarks, 0, @UserID, GETDATE(), @TerminalID, @LocationID, @ToLocationID);
            SELECT CAST(SCOPE_IDENTITY() AS INT);", new { no, r.DocDate, r.UserID, r.Remarks, r.TerminalID, r.LocationID, r.ToLocationID }, tx);

        decimal grand = 0;
        foreach (var l in r.Lines)
        {
            var taken = await StockLedger.DeductAsync(cn, tx, l.ItemID, r.LocationID, l.Quantity, l.ItemStockID, false, l.ItemName,
                new MovementInfo(StockMovementTypes.TransferOut, id, no, r.UserID, r.TerminalID));
            foreach (var t in taken)
            {
                var b = await cn.QueryFirstAsync<(decimal Selling, string? Batch, DateTime? Exp)>(
                    "SELECT ISNULL(SellingPrice,0), BatchNo, ExpDate FROM dbo.ItemStocks WHERE ItemStockID = @id", new { id = t.ItemStockId }, tx);
                var destId = await StockLedger.AddAsync(cn, tx, l.ItemID, r.ToLocationID.Value, t.Qty, null, t.Cost, b.Selling, b.Batch, b.Exp,
                    new MovementInfo(StockMovementTypes.TransferIn, id, no, r.UserID, r.TerminalID));
                var amt = Money.R(t.Qty * t.Cost);
                grand += amt;
                await cn.ExecuteAsync(@"INSERT dbo.TransferDetails(TransferID, ItemID, Rate, Quantity, Amount, IsDeleted, CreatedBy, DateCreated, PCCreated, HasOnlineSync, ItemStockID)
                                        VALUES(@id, @ItemID, @rate, @q, @amt, 0, @UserID, GETDATE(), @TerminalID, 0, @destId)",
                    new { id, l.ItemID, rate = t.Cost, q = t.Qty, amt, r.UserID, r.TerminalID, destId }, tx);
            }
        }
        await cn.ExecuteAsync("UPDATE dbo.Transfers SET TotalAmount = @grand WHERE TransferID = @id", new { grand, id }, tx);
        return new StockDocResult(id, no);
    }

    private static async Task<StockDocResult> SaveAdjustmentAsync(SqlConnection cn, SqlTransaction tx, StockDocRequest r)
    {
        var no = await Db.NextDocNoAsync(cn, tx, DocumentTypes.Adjustment, r.LocationID);
        var id = await cn.ExecuteScalarAsync<int>(@"
            INSERT dbo.StockAdjustments(AdjustmentNo, AdjustmentDate, LocationID, Reason, CreatedBy)
            VALUES(@no, @DocDate, @LocationID, @Remarks, @UserID);
            SELECT CAST(SCOPE_IDENTITY() AS INT);", new { no, r.DocDate, r.LocationID, r.Remarks, r.UserID }, tx);

        foreach (var l in r.Lines)
        {
            if (l.Quantity > 0)
            {
                var sid = await StockLedger.AddAsync(cn, tx, l.ItemID, r.LocationID, l.Quantity, l.ItemStockID, null, null, null, null,
                    new MovementInfo(StockMovementTypes.AdjustmentIn, id, no, r.UserID, r.TerminalID));
                await cn.ExecuteAsync("INSERT dbo.StockAdjustmentDetails(StockAdjustmentID, ItemID, ItemStockID, Quantity, CostPrice) VALUES(@id, @ItemID, @sid, @Quantity, @CostPrice)",
                    new { id, l.ItemID, sid, l.Quantity, l.CostPrice }, tx);
            }
            else
            {
                var taken = await StockLedger.DeductAsync(cn, tx, l.ItemID, r.LocationID, -l.Quantity, l.ItemStockID, true, l.ItemName,
                    new MovementInfo(StockMovementTypes.AdjustmentOut, id, no, r.UserID, r.TerminalID));
                foreach (var t in taken)
                    await cn.ExecuteAsync("INSERT dbo.StockAdjustmentDetails(StockAdjustmentID, ItemID, ItemStockID, Quantity, CostPrice) VALUES(@id, @ItemID, @sid, @q, @cost)",
                        new { id, l.ItemID, sid = t.ItemStockId, q = -t.Qty, cost = t.Cost }, tx);
            }
        }
        return new StockDocResult(id, no);
    }

    public static async Task<IReadOnlyList<OpenGrnDto>> GetOpenGrnsAsync(int supplierId)
    {
        await using var cn = await Db.OpenAsync();
        return (await cn.QueryAsync<OpenGrnDto>(@"
            SELECT PurchaseInvoiceID, RefNo, InvoiceNo, TransactionDate, ISNULL(NetTotal,0) AS NetTotal, ISNULL(PaidAmount,0) AS PaidAmount,
                   ISNULL(BalanceAmount,0) AS BalanceAmount
            FROM dbo.PurchaseInvoices WHERE SupplierID = @supplierId AND ISNULL(IsDeleted,0) = 0 AND ISNULL(BalanceAmount,0) > 0
            ORDER BY TransactionDate", new { supplierId })).ToList();
    }

    public static Task<string> PaySupplierAsync(SupplierPaymentRequest r) => Db.InTransactionAsync(async (cn, tx) =>
    {
        if (r.Amount <= 0) throw new AppException("Enter the amount.");
        var n = await cn.ExecuteAsync(@"UPDATE dbo.PurchaseInvoices SET PaidAmount = ISNULL(PaidAmount,0) + @Amount, BalanceAmount = ISNULL(BalanceAmount,0) - @Amount,
                                        ModifiedBy = @UserID WHERE PurchaseInvoiceID = @PurchaseInvoiceID AND ISNULL(BalanceAmount,0) >= @Amount",
            new { r.Amount, r.UserID, r.PurchaseInvoiceID }, tx);
        if (n == 0) throw new AppException("Amount is more than the GRN balance.");
        var no = await Db.NextDocNoAsync(cn, tx, DocumentTypes.SupplierPayment, r.LocationID);
        await cn.ExecuteAsync(@"INSERT dbo.PurchaseInvoicePayments(PurchaseInvoiceID, DatePaid, Amount, PayType, ChequeNo, ChequeDate, Remarks, IsDeleted, CreatedBy, DateCreated, PCCreated, PaymentNo)
                                VALUES(@PurchaseInvoiceID, GETDATE(), @Amount, @PayType, @ChequeNo, @ChequeDate, @Remarks, 0, @UserID, GETDATE(), @TerminalID, @no)",
            new { r.PurchaseInvoiceID, r.Amount, r.PayType, r.ChequeNo, r.ChequeDate, r.Remarks, r.UserID, r.TerminalID, no }, tx);
        return no;
    });
}

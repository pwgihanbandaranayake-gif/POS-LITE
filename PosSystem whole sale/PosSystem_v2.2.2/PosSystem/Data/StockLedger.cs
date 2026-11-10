namespace PosSystem.Data;

internal sealed record MovementInfo(string Type, int? RefId, string RefNo, int UserId, int TerminalId, int? RefLineId = null);

internal sealed record TakenBatch(int ItemStockId, decimal Qty, decimal Cost);

/// <summary>
/// The single place where ItemStocks quantities change. Every change also writes a StockMovements row,
/// so stock can always be explained (stock card report) and reversed (bill cancel).
/// Fixes the old system's SingleOrDefault-per-item bug (crashed with more than one batch) and
/// the refund "* 2" stock bug.
/// </summary>
internal static class StockLedger
{
    private sealed class BatchRow
    {
        public int ItemStockID { get; set; }
        public decimal CurrentQuantity { get; set; }
        public decimal CostPrice { get; set; }
    }

    public static async Task<List<TakenBatch>> DeductAsync(SqlConnection cn, SqlTransaction tx, int itemId, int locationId,
        decimal qty, int? preferredStockId, bool allowNegative, string itemName, MovementInfo mv)
    {
        var batches = (await cn.QueryAsync<BatchRow>(@"
            SELECT ItemStockID, ISNULL(CurrentQuantity,0) AS CurrentQuantity, ISNULL(CostPrice,0) AS CostPrice
            FROM dbo.ItemStocks WITH (UPDLOCK, ROWLOCK)
            WHERE ItemID = @itemId AND LocationID = @locationId AND ISNULL(IsDeleted,0) = 0
            ORDER BY CASE WHEN ItemStockID = @pref THEN 0 ELSE 1 END, ISNULL(ExpDate,'99991231'), ItemStockID",
            new { itemId, locationId, pref = preferredStockId ?? -1 }, tx)).ToList();

        var taken = new List<TakenBatch>();
        decimal remaining = qty;
        foreach (var b in batches)
        {
            if (remaining <= 0) break;
            if (b.CurrentQuantity <= 0) continue;
            var t = Math.Min(b.CurrentQuantity, remaining);
            taken.Add(new TakenBatch(b.ItemStockID, t, b.CostPrice));
            remaining -= t;
        }

        if (remaining > 0)
        {
            if (!allowNegative)
                throw new AppException($"Not enough stock for '{itemName}'. Short by {remaining:0.###}.");

            int stockId; decimal cost;
            if (batches.Count > 0) { stockId = batches[0].ItemStockID; cost = batches[0].CostPrice; }
            else
            {
                stockId = await CreateBatchAsync(cn, tx, itemId, locationId, null, null, null, null, mv, null);
                cost = await cn.ExecuteScalarAsync<decimal>("SELECT ISNULL(CostPrice,0) FROM dbo.Item WHERE ItemID = @itemId", new { itemId }, tx);
            }
            var idx = taken.FindIndex(t => t.ItemStockId == stockId);
            if (idx >= 0) taken[idx] = taken[idx] with { Qty = taken[idx].Qty + remaining };
            else taken.Add(new TakenBatch(stockId, remaining, cost));
        }

        foreach (var t in taken)
        {
            await cn.ExecuteAsync("UPDATE dbo.ItemStocks SET CurrentQuantity = ISNULL(CurrentQuantity,0) - @q WHERE ItemStockID = @id",
                new { q = t.Qty, id = t.ItemStockId }, tx);
            await WriteMovementAsync(cn, tx, itemId, locationId, t.ItemStockId, mv, 0, t.Qty, t.Cost);
        }
        return taken;
    }

    /// <summary>Adds stock: into the preferred batch, else a batch with identical cost/price/batch/expiry, else a new batch.</summary>
    public static async Task<int> AddAsync(SqlConnection cn, SqlTransaction tx, int itemId, int locationId, decimal qty,
        int? preferredStockId, decimal? cost, decimal? selling, string? batchNo, DateTime? expDate, MovementInfo mv,
        int? purchaseInvoiceId = null)
    {
        int? stockId = null;
        if (preferredStockId.HasValue)
            stockId = await cn.ExecuteScalarAsync<int?>(
                "SELECT ItemStockID FROM dbo.ItemStocks WHERE ItemStockID = @id AND ItemID = @itemId AND LocationID = @locationId AND ISNULL(IsDeleted,0) = 0",
                new { id = preferredStockId, itemId, locationId }, tx);

        if (stockId == null && cost.HasValue && selling.HasValue)
            stockId = await cn.ExecuteScalarAsync<int?>(@"
                SELECT TOP 1 ItemStockID FROM dbo.ItemStocks WITH (UPDLOCK)
                WHERE ItemID = @itemId AND LocationID = @locationId AND ISNULL(IsDeleted,0) = 0
                  AND ISNULL(CostPrice,0) = @cost AND ISNULL(SellingPrice,0) = @selling
                  AND ISNULL(BatchNo,'') = ISNULL(@batchNo,'')
                  AND ISNULL(ExpDate,'19000101') = ISNULL(@expDate,'19000101')
                ORDER BY ItemStockID DESC",
                new { itemId, locationId, cost, selling, batchNo, expDate }, tx);

        if (stockId == null && !cost.HasValue)
            stockId = await cn.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 ItemStockID FROM dbo.ItemStocks WHERE ItemID = @itemId AND LocationID = @locationId AND ISNULL(IsDeleted,0) = 0 ORDER BY ItemStockID DESC",
                new { itemId, locationId }, tx);

        stockId ??= await CreateBatchAsync(cn, tx, itemId, locationId, cost, selling, batchNo, expDate, mv, purchaseInvoiceId);

        await cn.ExecuteAsync("UPDATE dbo.ItemStocks SET CurrentQuantity = ISNULL(CurrentQuantity,0) + @qty WHERE ItemStockID = @id",
            new { qty, id = stockId }, tx);

        var c = cost ?? await cn.ExecuteScalarAsync<decimal>("SELECT ISNULL(CostPrice,0) FROM dbo.ItemStocks WHERE ItemStockID = @id", new { id = stockId }, tx);
        await WriteMovementAsync(cn, tx, itemId, locationId, stockId.Value, mv, qty, 0, c);
        return stockId.Value;
    }

    private static async Task<int> CreateBatchAsync(SqlConnection cn, SqlTransaction tx, int itemId, int locationId,
        decimal? cost, decimal? selling, string? batchNo, DateTime? expDate, MovementInfo mv, int? purchaseInvoiceId)
    {
        return await cn.ExecuteScalarAsync<int>(@"
            INSERT dbo.ItemStocks(ItemID, ExpDate, BatchNo, OpeningQuantity, CurrentQuantity, IsDeleted, CreatedBy, DateCreated,
                                  PCCreated, CostPrice, SellingPrice, LocationID, PurchaseInvoiceID)
            SELECT @itemId, @expDate, @batchNo, 0, 0, 0, @user, GETDATE(), @pc,
                   ISNULL(@cost, ISNULL(i.CostPrice,0)), ISNULL(@selling, ISNULL(i.SellingPrice,0)), @locationId, @purchaseInvoiceId
            FROM dbo.Item i WHERE i.ItemID = @itemId;
            SELECT CAST(SCOPE_IDENTITY() AS INT);",
            new { itemId, expDate, batchNo, user = mv.UserId.ToString(), pc = mv.TerminalId, cost, selling, locationId, purchaseInvoiceId }, tx);
    }

    public static Task WriteMovementAsync(SqlConnection cn, SqlTransaction tx, int itemId, int locationId, int? stockId,
        MovementInfo mv, decimal qtyIn, decimal qtyOut, decimal? cost) =>
        cn.ExecuteAsync(@"
            INSERT dbo.StockMovements(MovementDate, ItemID, LocationID, ItemStockID, MovementType, RefID, RefLineID, RefNo,
                                      QtyIn, QtyOut, CostPrice, CreatedBy, PCCreated)
            VALUES(GETDATE(), @itemId, @locationId, @stockId, @type, @refId, @refLineId, @refNo, @qtyIn, @qtyOut, @cost, @user, @pc)",
            new { itemId, locationId, stockId, type = mv.Type, refId = mv.RefId, refLineId = mv.RefLineId, refNo = mv.RefNo,
                  qtyIn, qtyOut, cost, user = mv.UserId, pc = mv.TerminalId }, tx);
}

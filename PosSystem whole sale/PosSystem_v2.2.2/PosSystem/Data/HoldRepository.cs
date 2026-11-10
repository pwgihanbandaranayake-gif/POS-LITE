namespace PosSystem.Data;

/// <summary>Held bills do NOT reserve stock - stock moves only when the bill is finally paid.</summary>
public static class HoldRepository
{

    public static Task<string> HoldAsync(HeldBillDto b, int userId, int terminalId) => Db.InTransactionAsync(async (cn, tx) =>
    {
        var no = await Db.NextDocNoAsync(cn, tx, DocumentTypes.Hold, b.LocationID);
        var id = await cn.ExecuteScalarAsync<int>(@"
            INSERT dbo.HeldBills(HoldNo, LocationID, PCCreated, CustomerID, CustomerName, BillDiscountType, BillDiscountValue, TotalAmount, Note, CreatedBy)
            VALUES(@no, @LocationID, @terminalId, @CustomerID, @CustomerName, @bdt, @BillDiscountValue, @TotalAmount, @Note, @userId);
            SELECT CAST(SCOPE_IDENTITY() AS INT);",
            new { no, b.LocationID, terminalId, b.CustomerID, b.CustomerName, bdt = (byte)b.BillDiscountType, b.BillDiscountValue, b.TotalAmount, b.Note, userId }, tx);
        foreach (var i in b.Items)
            await cn.ExecuteAsync(@"
                INSERT dbo.HeldBillItems(HeldBillID, LineNumber, ItemID, ItemStockID, Quantity, Rate, DiscountType, DiscountValue, LineNote)
                VALUES(@id, @LineNumber, @ItemID, @ItemStockID, @Quantity, @Rate, @dt, @DiscountValue, @LineNote)",
                new { id, i.LineNumber, i.ItemID, i.ItemStockID, i.Quantity, i.Rate, dt = (byte)i.DiscountType, i.DiscountValue, i.LineNote }, tx);
        return no;
    });

    public static async Task<IReadOnlyList<HeldBillDto>> ListAsync(int locationId)
    {
        await using var cn = await Db.OpenAsync();
        return (await cn.QueryAsync<HeldBillDto>(@"
            SELECT h.HeldBillID, h.HoldNo, h.LocationID, h.CustomerID, h.CustomerName, CAST(h.BillDiscountType AS INT) AS BillDiscountType,
                   h.BillDiscountValue, h.TotalAmount, h.Note, u.FullName AS CreatedByName, h.DateCreated,
                   (SELECT COUNT(*) FROM dbo.HeldBillItems i WHERE i.HeldBillID = h.HeldBillID) AS ItemCount
            FROM dbo.HeldBills h LEFT JOIN dbo.UserProfile u ON u.UserProfileId = h.CreatedBy
            WHERE h.LocationID = @locationId ORDER BY h.DateCreated", new { locationId })).ToList();
    }

    public static Task<HeldBillDto?> TakeAsync(int heldBillId) => Db.InTransactionAsync(async (cn, tx) =>
    {
        var h = await cn.QueryFirstOrDefaultAsync<HeldBillDto>(@"
            SELECT HeldBillID, HoldNo, LocationID, CustomerID, CustomerName, CAST(BillDiscountType AS INT) AS BillDiscountType,
                   BillDiscountValue, TotalAmount, Note, DateCreated
            FROM dbo.HeldBills WITH (UPDLOCK) WHERE HeldBillID = @heldBillId", new { heldBillId }, tx);
        if (h == null) return null;   // already recalled on another till
        h.Items = (await cn.QueryAsync<HeldBillItemDto>(@"
            SELECT LineNumber, ItemID, ItemStockID, Quantity, Rate, CAST(DiscountType AS INT) AS DiscountType, DiscountValue, LineNote
            FROM dbo.HeldBillItems WHERE HeldBillID = @heldBillId ORDER BY LineNumber", new { heldBillId }, tx)).ToList();
        await cn.ExecuteAsync("DELETE FROM dbo.HeldBills WHERE HeldBillID = @heldBillId", new { heldBillId }, tx);
        return h;
    });

    public static async Task DeleteAsync(int heldBillId)
    {
        await using var cn = await Db.OpenAsync();
        await cn.ExecuteAsync("DELETE FROM dbo.HeldBills WHERE HeldBillID = @heldBillId", new { heldBillId });
    }
}

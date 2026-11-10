namespace PosSystem.Data;

public static class ItemRepository
{

    private const string ItemSelect = @"
        SELECT {TOP} i.ItemID, i.ItemCode, i.BarCode, i.ItemName, i.ItemDescription, i.ItemCategoryID, i.BrandID, i.UnitTypeId, i.TaxID,
               ISNULL(i.CostPrice,0) AS CostPrice, ISNULL(i.SellingPrice,0) AS SellingPrice, ISNULL(i.OurPrice,0) AS OurPrice,
               ISNULL(i.ReorderLevel,0) AS ReorderLevel,
               CAST(ISNULL(i.IsService,0) AS BIT) AS IsService, i.IsWeighted, i.AllowDiscount, i.AllowPriceChange, i.ShowInTouch, i.TouchColor,
               CAST(ISNULL(i.IsDeleted,0) AS BIT) AS IsDeleted,
               cat.ItemCategoryDescription AS CategoryName, u.[Type] AS UnitName,
               ISNULL(CASE WHEN t.IsActive = 1 AND t.IsDeleted = 0 THEN t.Rate END, 0) AS TaxRate,
               ISNULL((SELECT SUM(ISNULL(s.CurrentQuantity,0)) FROM dbo.ItemStocks s
                       WHERE s.ItemID = i.ItemID AND s.LocationID = @loc AND ISNULL(s.IsDeleted,0) = 0), 0) AS StockOnHand
        FROM dbo.Item i
        LEFT JOIN dbo.ItemCategories cat ON cat.ItemCategoryId = i.ItemCategoryID
        LEFT JOIN dbo.UnitTypes u ON u.UnitTypeId = i.UnitTypeId
        LEFT JOIN dbo.Taxes t ON t.TaxID = i.TaxID
        WHERE ISNULL(i.IsDeleted,0) = 0";

    private static string Sel(int? top) => ItemSelect.Replace("{TOP}", top.HasValue ? $"TOP ({top.Value})" : "");

    public static async Task<Item?> FindByCodeAsync(string code, int locationId)
    {
        await using var cn = await Db.OpenAsync();
        return await cn.QueryFirstOrDefaultAsync<Item>(Sel(1) +
            " AND (i.BarCode = @code OR i.ItemCode = @code) ORDER BY CASE WHEN i.BarCode = @code THEN 0 ELSE 1 END",
            new { code = code.Trim(), loc = locationId });
    }

    public static async Task<Item?> GetByIdAsync(int itemId, int locationId)
    {
        await using var cn = await Db.OpenAsync();
        return await cn.QueryFirstOrDefaultAsync<Item>(Sel(1) + " AND i.ItemID = @itemId", new { itemId, loc = locationId });
    }

    public static async Task<IReadOnlyList<Item>> SearchAsync(string text, int locationId, int max = 100)
    {
        await using var cn = await Db.OpenAsync();
        var t = (text ?? "").Trim();
        return (await cn.QueryAsync<Item>(Sel(max) + @"
            AND (@t = '' OR i.ItemCode LIKE @t + '%' OR i.BarCode LIKE @t + '%' OR i.ItemName LIKE '%' + @t + '%' OR i.ItemDescription LIKE '%' + @t + '%')
            ORDER BY CASE WHEN i.ItemCode = @t OR i.BarCode = @t THEN 0 ELSE 1 END, ISNULL(i.ItemName, i.ItemDescription)",
            new { t, loc = locationId })).ToList();
    }

    public static async Task<IReadOnlyList<Item>> ListAsync(string? search, int? categoryId, int locationId)
    {
        await using var cn = await Db.OpenAsync();
        var t = (search ?? "").Trim();
        return (await cn.QueryAsync<Item>(Sel(2000) + @"
            AND (@t = '' OR i.ItemCode LIKE '%' + @t + '%' OR i.BarCode LIKE '%' + @t + '%' OR i.ItemName LIKE '%' + @t + '%' OR i.ItemDescription LIKE '%' + @t + '%')
            AND (@cat IS NULL OR i.ItemCategoryID = @cat)
            ORDER BY ISNULL(i.ItemName, i.ItemDescription)", new { t, cat = categoryId, loc = locationId })).ToList();
    }

    public static async Task<IReadOnlyList<StockBatch>> GetBatchesAsync(int itemId, int locationId, bool onlyInStock)
    {
        await using var cn = await Db.OpenAsync();
        return (await cn.QueryAsync<StockBatch>(@"
            SELECT ItemStockID, ItemID, LocationID, BatchNo, ExpDate, ISNULL(CurrentQuantity,0) AS CurrentQuantity,
                   ISNULL(CostPrice,0) AS CostPrice, ISNULL(SellingPrice,0) AS SellingPrice
            FROM dbo.ItemStocks WHERE ItemID = @itemId AND LocationID = @locationId AND ISNULL(IsDeleted,0) = 0
              AND (@onlyInStock = 0 OR CurrentQuantity > 0)
            ORDER BY ISNULL(ExpDate,'99991231'), ItemStockID", new { itemId, locationId, onlyInStock })).ToList();
    }

    public static Task<IReadOnlyList<LookupItem>> GetTouchCategoriesAsync() => LookupRepository.QueryLookupAsync(@"
        SELECT ItemCategoryId Id, ISNULL(TouchColor,'') Code, ItemCategoryDescription Name FROM dbo.ItemCategories
        WHERE ISNULL(IsDeleted,0) = 0 AND ShowInTouch = 1 ORDER BY SortOrder, ItemCategoryDescription");

    public static async Task<IReadOnlyList<Item>> GetTouchItemsAsync(int? categoryId, int locationId)
    {
        await using var cn = await Db.OpenAsync();
        var filter = categoryId.HasValue ? " AND i.ItemCategoryID = @categoryId" : " AND i.ShowInTouch = 1";
        return (await cn.QueryAsync<Item>(Sel(300) + filter + " ORDER BY ISNULL(i.ItemName, i.ItemDescription)",
            new { categoryId, loc = locationId })).ToList();
    }

    public static async Task<int> SaveAsync(Item it, bool applyPriceToStock, int byUser, int terminalId)
    {
        if (string.IsNullOrWhiteSpace(it.ItemCode)) throw new AppException("Item code is required.");
        if (string.IsNullOrWhiteSpace(it.ItemName)) throw new AppException("Item name is required.");
        if (it.SellingPrice < 0 || it.CostPrice < 0) throw new AppException("Prices cannot be negative.");

        return await Db.InTransactionAsync(async (cn, tx) =>
        {
            var dup = await cn.ExecuteScalarAsync<string?>(@"
                SELECT TOP 1 ISNULL(ItemName, ItemDescription) FROM dbo.Item
                WHERE ISNULL(IsDeleted,0) = 0 AND ItemID <> @ItemID
                  AND (ItemCode = @ItemCode OR (@BarCode IS NOT NULL AND @BarCode <> '' AND (BarCode = @BarCode OR ItemCode = @BarCode)))",
                new { it.ItemID, it.ItemCode, it.BarCode }, tx);
            if (dup != null) throw new AppException($"Code or barcode already used by '{dup}'.");

            var args = new
            {
                it.ItemID, it.ItemCode, BarCode = string.IsNullOrWhiteSpace(it.BarCode) ? null : it.BarCode.Trim(),
                it.ItemName, ItemDescription = string.IsNullOrWhiteSpace(it.ItemDescription) ? it.ItemName : it.ItemDescription,
                it.ItemCategoryID, it.BrandID, it.UnitTypeId, it.TaxID, it.CostPrice, it.SellingPrice, it.OurPrice, it.ReorderLevel,
                it.IsService, it.IsWeighted, it.AllowDiscount, it.AllowPriceChange, it.ShowInTouch, it.TouchColor, byUser, terminalId
            };

            if (it.ItemID == 0)
            {
                return await cn.ExecuteScalarAsync<int>(@"
                    INSERT dbo.Item(ItemCategoryID, ItemCode, ItemDescription, IsRowItem, UnitTypeId, DateCreated, DateModified, CreatedBy, IsDeleted,
                        IsService, BarCode, CostPrice, SellingPrice, ReorderLevel, IsPurchased, PCCreated, ItemName, OurPrice,
                        BrandID, TaxID, IsWeighted, AllowDiscount, AllowPriceChange, ShowInTouch, TouchColor, HasOnlineSync)
                    VALUES(@ItemCategoryID, @ItemCode, @ItemDescription, 0, @UnitTypeId, GETDATE(), GETDATE(), @byUser, 0,
                        @IsService, @BarCode, @CostPrice, @SellingPrice, @ReorderLevel, 1, @terminalId, @ItemName, @OurPrice,
                        @BrandID, @TaxID, @IsWeighted, @AllowDiscount, @AllowPriceChange, @ShowInTouch, @TouchColor, 0);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);", args, tx);
            }

            var oldPrice = await cn.ExecuteScalarAsync<decimal>("SELECT ISNULL(SellingPrice,0) FROM dbo.Item WHERE ItemID = @ItemID", new { it.ItemID }, tx);
            await cn.ExecuteAsync(@"
                UPDATE dbo.Item SET ItemCategoryID = @ItemCategoryID, ItemCode = @ItemCode, ItemDescription = @ItemDescription,
                    UnitTypeId = @UnitTypeId, DateModified = GETDATE(), ModifiedBy = @byUser, IsService = @IsService, BarCode = @BarCode,
                    CostPrice = @CostPrice, SellingPrice = @SellingPrice, ReorderLevel = @ReorderLevel, PCModified = @terminalId,
                    ItemName = @ItemName, OurPrice = @OurPrice, BrandID = @BrandID, TaxID = @TaxID, IsWeighted = @IsWeighted,
                    AllowDiscount = @AllowDiscount, AllowPriceChange = @AllowPriceChange, ShowInTouch = @ShowInTouch, TouchColor = @TouchColor
                WHERE ItemID = @ItemID", args, tx);

            if (oldPrice != it.SellingPrice)
            {
                await cn.ExecuteAsync(@"
                    INSERT dbo.ItemPriceChangeLogs(RefNo, ItemID, ExistingPrice, NewPrice, IsDeleted, ModifiedBy, CreatedBy, DateCreated, PCCreated, HasOnlineSync)
                    VALUES(@refNo, @ItemID, @oldPrice, @SellingPrice, 0, @byUserText, @byUser, GETDATE(), @terminalId, 0)",
                    new { refNo = applyPriceToStock ? "Item master (stock updated)" : "Item master", it.ItemID, oldPrice, it.SellingPrice,
                          byUserText = byUser.ToString(), byUser, terminalId }, tx);
                if (applyPriceToStock)
                    await cn.ExecuteAsync("UPDATE dbo.ItemStocks SET SellingPrice = @SellingPrice WHERE ItemID = @ItemID AND ISNULL(IsDeleted,0) = 0 AND CurrentQuantity <> 0",
                        new { it.SellingPrice, it.ItemID }, tx);
            }
            return it.ItemID;
        });
    }

    public static async Task DeleteAsync(int itemId, int byUser)
    {
        await using var cn = await Db.OpenAsync();
        await cn.ExecuteAsync("UPDATE dbo.Item SET IsDeleted = 1, ModifiedBy = @byUser, DateModified = GETDATE() WHERE ItemID = @itemId", new { itemId, byUser });
    }
}

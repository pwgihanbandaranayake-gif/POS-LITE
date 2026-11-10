namespace PosSystem.Data;

public static class LookupRepository
{

    private sealed class Row { public int Id { get; set; } public string? Code { get; set; } public string? Name { get; set; } }

    public static async Task<IReadOnlyList<LookupItem>> QueryLookupAsync(string sql, object? param = null)
    {
        await using var cn = await Db.OpenAsync();
        return (await cn.QueryAsync<Row>(sql, param)).Select(r => new LookupItem(r.Id, r.Code ?? "", r.Name ?? "")).ToList();
    }

    public static async Task<IReadOnlyList<Location>> GetLocationsAsync()
    {
        await using var cn = await Db.OpenAsync();
        return (await cn.QueryAsync<Location>("SELECT LocationID, LocationCode, LocationName, Address, Phone, IsActive FROM dbo.Locations WHERE IsDeleted = 0 ORDER BY LocationID")).ToList();
    }

    public static Task<IReadOnlyList<LookupItem>> GetCategoriesAsync() => QueryLookupAsync(
        "SELECT ItemCategoryId Id, ItemCategoryCode Code, ItemCategoryDescription Name FROM dbo.ItemCategories WHERE ISNULL(IsDeleted,0) = 0 ORDER BY ItemCategoryDescription");

    public static Task<IReadOnlyList<LookupItem>> GetBrandsAsync() => QueryLookupAsync(
        "SELECT BrandID Id, BrandCode Code, BrandName Name FROM dbo.Brands WHERE IsDeleted = 0 ORDER BY BrandName");

    public static Task<IReadOnlyList<LookupItem>> GetUnitsAsync() => QueryLookupAsync(
        "SELECT UnitTypeId Id, '' Code, [Type] Name FROM dbo.UnitTypes ORDER BY UnitTypeId");

    public static Task<IReadOnlyList<LookupItem>> GetTaxesAsync() => QueryLookupAsync(
        "SELECT TaxID Id, TaxCode Code, TaxName + ' (' + CAST(CAST(Rate AS DECIMAL(9,2)) AS VARCHAR(20)) + '%)' Name FROM dbo.Taxes WHERE IsDeleted = 0 AND IsActive = 1");

    public static Task<IReadOnlyList<LookupItem>> GetSuppliersAsync() => QueryLookupAsync(
        "SELECT SupplierID Id, SupCode Code, SupName Name FROM dbo.Suppliers WHERE ISNULL(IsDeleted,0) = 0 ORDER BY SupName");

    public static Task<IReadOnlyList<LookupItem>> GetEmployeesAsync() => QueryLookupAsync(
        "SELECT EmployeeID Id, EmployeeNo Code, EmployeeName Name FROM dbo.Employees WHERE ISNULL(Archived,0) = 0 AND ISNULL(IsInactive,0) = 0 ORDER BY EmployeeName");

    public static Task<IReadOnlyList<LookupItem>> GetUsersAsync() => QueryLookupAsync(
        "SELECT UserProfileId Id, userId Code, FullName Name FROM dbo.UserProfile WHERE ISNULL(IsDeleted,0) = 0 ORDER BY FullName");

    public static Task<IReadOnlyList<LookupItem>> GetRolesAsync() => QueryLookupAsync(
        "SELECT UserGroupID Id, '' Code, UserGroupName Name FROM dbo.UserGroups WHERE ISNULL(IsDeleted,0) = 0 ORDER BY UserGroupName");

    public static async Task<IReadOnlyList<PaymentMethod>> GetPaymentMethodsAsync()
    {
        await using var cn = await Db.OpenAsync();
        return (await cn.QueryAsync<PaymentMethod>(@"SELECT PaymentMethodID, Code, Name, Kind, RequiresReference, OpensDrawer, ShortcutKey, SortOrder, IsActive
                                                     FROM dbo.PaymentMethods WHERE IsDeleted = 0 ORDER BY SortOrder")).ToList();
    }

    private const string CustomerSelect = @"
        SELECT TOP (@max) c.CustomerID, c.Code, c.Name, c.Mobile, ISNULL(c.CreditLimit,0) AS CreditLimit,
               ISNULL(c.OpeningBalance,0)
               + ISNULL((SELECT SUM(p.Amount) FROM dbo.vw_SalesPayments p JOIN dbo.vw_ValidSales s ON s.SalesInvoiceID = p.SalesInvoiceID
                         WHERE s.CustomerID = c.CustomerID AND p.Kind = 3),0)
               - ISNULL((SELECT SUM(ISNULL(r.Amount,0) + ISNULL(r.AmountInCheque,0)) FROM dbo.ReceivedAmount r
                         WHERE r.CustomerID = c.CustomerID AND ISNULL(r.IsDeleted,0) = 0),0) AS Outstanding
        FROM dbo.Customers c WHERE ISNULL(c.IsDeleted,0) = 0";

    public static async Task<IReadOnlyList<CustomerInfoDto>> SearchCustomersAsync(string? text, int max = 50)
    {
        await using var cn = await Db.OpenAsync();
        var t = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        return (await cn.QueryAsync<CustomerInfoDto>(CustomerSelect +
            " AND (@t IS NULL OR c.Name LIKE '%' + @t + '%' OR c.Mobile LIKE '%' + @t + '%' OR c.Code LIKE '%' + @t + '%' OR c.NICNo LIKE '%' + @t + '%') ORDER BY c.Name",
            new { t, max })).ToList();
    }

    public static async Task<CustomerInfoDto?> GetCustomerAsync(int customerId)
    {
        await using var cn = await Db.OpenAsync();
        return await cn.QueryFirstOrDefaultAsync<CustomerInfoDto>(CustomerSelect + " AND c.CustomerID = @customerId", new { customerId, max = 1 });
    }

    public static async Task<int> QuickAddCustomerAsync(string name, string? mobile, int byUser)
    {
        await using var cn = await Db.OpenAsync();
        return await cn.ExecuteScalarAsync<int>(@"
            INSERT dbo.Customers(Code, Name, Mobile, IsDeleted, CreatedBy, DateCreated, CreditLimit, OpeningBalance)
            VALUES(NULL, @name, @mobile, 0, @byUser, GETDATE(), 0, 0);
            DECLARE @id INT = SCOPE_IDENTITY();
            UPDATE dbo.Customers SET Code = 'C' + RIGHT('00000' + CAST(@id AS VARCHAR(10)), 5) WHERE CustomerID = @id;
            SELECT @id;", new { name, mobile, byUser });
    }
}

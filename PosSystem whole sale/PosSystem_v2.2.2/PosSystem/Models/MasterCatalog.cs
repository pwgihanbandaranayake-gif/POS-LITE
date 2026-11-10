namespace PosSystem.Models;

public enum FieldKind { Text, MultiLine, Number, Bool, Lookup, Color }

public sealed class MasterField
{
    public string Column { get; init; } = "";
    public string Label { get; init; } = "";
    public FieldKind Kind { get; init; } = FieldKind.Text;
    public bool Required { get; init; }
    public bool Unique { get; init; }
    public int MaxLength { get; init; } = 200;
    public string? LookupSql { get; init; }   // must return Id, Code, Name
    public object? Default { get; init; }
}

/// <summary>
/// Metadata-driven master. One generic screen + one generic repository serve every simple master.
/// Column / table names come ONLY from this trusted catalog, never from user input.
/// </summary>
public sealed class MasterDefinition
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Icon { get; init; } = "\uE8FD";
    public string Permission { get; init; } = "";
    public string Table { get; init; } = "";
    public string KeyColumn { get; init; } = "";
    public bool KeyIsIdentity { get; init; } = true;
    public string? SoftDeleteColumn { get; init; } = "IsDeleted";
    /// <summary>SELECT with first column "Id"; may use @Search (NULL = all).</summary>
    public string ListSql { get; init; } = "";
    public IReadOnlyList<MasterField> Fields { get; init; } = Array.Empty<MasterField>();
    /// <summary>Extra columns written on insert. Tokens: "@User", "@Now", or a literal value.</summary>
    public IReadOnlyDictionary<string, object> InsertExtras { get; init; } = new Dictionary<string, object>();
    public IReadOnlyDictionary<string, object> UpdateExtras { get; init; } = new Dictionary<string, object>();
}

public static class MasterCatalog
{
    private const string User = "@User";
    private const string Now = "@Now";

    private static Dictionary<string, object> Ins(params (string, object)[] extra)
    {
        var d = new Dictionary<string, object> { ["DateCreated"] = Now, ["CreatedBy"] = User, ["IsDeleted"] = false };
        foreach (var (k, v) in extra) d[k] = v;
        return d;
    }

    public static readonly MasterDefinition Categories = new()
    {
        Key = "categories", Title = "Item Categories", Icon = "\uE8EC", Permission = Perm.MasterCategories,
        Table = "ItemCategories", KeyColumn = "ItemCategoryId",
        ListSql = @"SELECT ItemCategoryId AS Id, ItemCategoryCode AS [Code], ItemCategoryDescription AS [Category],
                    CASE WHEN ShowInTouch = 1 THEN 'Yes' ELSE 'No' END AS [Touch Screen], SortOrder AS [Sort]
                    FROM dbo.ItemCategories WHERE ISNULL(IsDeleted,0) = 0
                    AND (@Search IS NULL OR ItemCategoryCode LIKE '%' + @Search + '%' OR ItemCategoryDescription LIKE '%' + @Search + '%')
                    ORDER BY SortOrder, ItemCategoryDescription",
        Fields = new[]
        {
            new MasterField { Column = "ItemCategoryCode", Label = "Code", Required = true, Unique = true, MaxLength = 50 },
            new MasterField { Column = "ItemCategoryDescription", Label = "Category name", Required = true, MaxLength = 200 },
            new MasterField { Column = "ShowInTouch", Label = "Show on touch screen", Kind = FieldKind.Bool, Default = true },
            new MasterField { Column = "TouchColor", Label = "Touch tile colour", Kind = FieldKind.Color, MaxLength = 20, Default = "#0F766E" },
            new MasterField { Column = "SortOrder", Label = "Sort order", Kind = FieldKind.Number, Default = 0 },
        },
        InsertExtras = Ins(("DateModified", Now)),
        UpdateExtras = new Dictionary<string, object> { ["DateModified"] = Now, ["ModifiedBy"] = User }
    };

    public static readonly MasterDefinition Brands = new()
    {
        Key = "brands", Title = "Brands", Icon = "\uE734", Permission = Perm.MasterBrands,
        Table = "Brands", KeyColumn = "BrandID",
        ListSql = @"SELECT BrandID AS Id, BrandCode AS [Code], BrandName AS [Brand] FROM dbo.Brands
                    WHERE IsDeleted = 0 AND (@Search IS NULL OR BrandCode LIKE '%' + @Search + '%' OR BrandName LIKE '%' + @Search + '%')
                    ORDER BY BrandName",
        Fields = new[]
        {
            new MasterField { Column = "BrandCode", Label = "Code", Unique = true, MaxLength = 20 },
            new MasterField { Column = "BrandName", Label = "Brand name", Required = true, MaxLength = 100 },
        },
        InsertExtras = Ins()
    };

    public static readonly MasterDefinition Units = new()
    {
        Key = "units", Title = "Units of Measure", Icon = "\uE9D2", Permission = Perm.MasterUnits,
        Table = "UnitTypes", KeyColumn = "UnitTypeId", KeyIsIdentity = false, SoftDeleteColumn = null,
        ListSql = @"SELECT UnitTypeId AS Id, [Type] AS [Unit] FROM dbo.UnitTypes
                    WHERE (@Search IS NULL OR [Type] LIKE '%' + @Search + '%') ORDER BY UnitTypeId",
        Fields = new[] { new MasterField { Column = "Type", Label = "Unit", Required = true, Unique = true, MaxLength = 100 } }
    };

    public static readonly MasterDefinition Suppliers = new()
    {
        Key = "suppliers", Title = "Suppliers", Icon = "\uE7BF", Permission = Perm.MasterSuppliers,
        Table = "Suppliers", KeyColumn = "SupplierID",
        ListSql = @"SELECT SupplierID AS Id, SupCode AS [Code], SupName AS [Supplier], Mobile, TelephoneBusiness AS [Phone],
                    Email, CreditDays AS [Credit Days] FROM dbo.Suppliers WHERE ISNULL(IsDeleted,0) = 0
                    AND (@Search IS NULL OR SupCode LIKE '%' + @Search + '%' OR SupName LIKE '%' + @Search + '%' OR Mobile LIKE '%' + @Search + '%')
                    ORDER BY SupName",
        Fields = new[]
        {
            new MasterField { Column = "SupCode", Label = "Code", Required = true, Unique = true, MaxLength = 50 },
            new MasterField { Column = "SupName", Label = "Supplier name", Required = true, MaxLength = 50 },
            new MasterField { Column = "Line1", Label = "Address line 1", MaxLength = 50 },
            new MasterField { Column = "Line2", Label = "Address line 2", MaxLength = 50 },
            new MasterField { Column = "Line3", Label = "City", MaxLength = 50 },
            new MasterField { Column = "Mobile", Label = "Mobile", MaxLength = 50 },
            new MasterField { Column = "TelephoneBusiness", Label = "Office phone", MaxLength = 50 },
            new MasterField { Column = "Email", Label = "Email", MaxLength = 50 },
            new MasterField { Column = "CreditDays", Label = "Credit days", Kind = FieldKind.Number, Default = 0 },
            new MasterField { Column = "Remarks", Label = "Remarks", Kind = FieldKind.MultiLine, MaxLength = 255 },
        },
        InsertExtras = Ins(),
        UpdateExtras = new Dictionary<string, object> { ["ModifiedBy"] = User }
    };

    public static readonly MasterDefinition Customers = new()
    {
        Key = "customers", Title = "Customers", Icon = "\uE77B", Permission = Perm.MasterCustomers,
        Table = "Customers", KeyColumn = "CustomerID",
        ListSql = @"SELECT CustomerID AS Id, Code AS [Code], Name AS [Customer], Mobile, NICNo AS [NIC], Line3 AS [City],
                    CreditLimit AS [Credit Limit] FROM dbo.Customers WHERE ISNULL(IsDeleted,0) = 0
                    AND (@Search IS NULL OR Code LIKE '%' + @Search + '%' OR Name LIKE '%' + @Search + '%' OR Mobile LIKE '%' + @Search + '%' OR NICNo LIKE '%' + @Search + '%')
                    ORDER BY Name",
        Fields = new[]
        {
            new MasterField { Column = "Code", Label = "Code", Unique = true, MaxLength = 50 },
            new MasterField { Column = "Name", Label = "Customer name", Required = true, MaxLength = 255 },
            new MasterField { Column = "Mobile", Label = "Mobile", MaxLength = 50 },
            new MasterField { Column = "NICNo", Label = "NIC No", MaxLength = 50 },
            new MasterField { Column = "Line1", Label = "Address line 1", MaxLength = 255 },
            new MasterField { Column = "Line2", Label = "Address line 2", MaxLength = 255 },
            new MasterField { Column = "Line3", Label = "City", MaxLength = 255 },
            new MasterField { Column = "TelephoneHome", Label = "Phone", MaxLength = 50 },
            new MasterField { Column = "Email", Label = "Email", MaxLength = 255 },
            new MasterField { Column = "CreditLimit", Label = "Credit limit", Kind = FieldKind.Number, Default = 0 },
            new MasterField { Column = "OpeningBalance", Label = "Opening balance (owed)", Kind = FieldKind.Number, Default = 0 },
            new MasterField { Column = "Remarks", Label = "Remarks", Kind = FieldKind.MultiLine, MaxLength = 255 },
        },
        InsertExtras = Ins(),
        UpdateExtras = new Dictionary<string, object> { ["ModifiedBy"] = User }
    };

    public static readonly MasterDefinition Employees = new()
    {
        Key = "employees", Title = "Employees", Icon = "\uE716", Permission = Perm.MasterEmployees,
        Table = "Employees", KeyColumn = "EmployeeID", SoftDeleteColumn = "Archived",
        ListSql = @"SELECT EmployeeID AS Id, EmployeeNo AS [Emp No], EmployeeName AS [Name], EPFNo AS [EPF No],
                    CASE WHEN ISNULL(IsInactive,0) = 1 THEN 'Inactive' ELSE 'Active' END AS [Status]
                    FROM dbo.Employees WHERE ISNULL(Archived,0) = 0
                    AND (@Search IS NULL OR EmployeeNo LIKE '%' + @Search + '%' OR EmployeeName LIKE '%' + @Search + '%')
                    ORDER BY EmployeeName",
        Fields = new[]
        {
            new MasterField { Column = "EmployeeNo", Label = "Employee No", Required = true, Unique = true, MaxLength = 50 },
            new MasterField { Column = "EmployeeName", Label = "Name", Required = true, MaxLength = 200 },
            new MasterField { Column = "EPFNo", Label = "EPF No", MaxLength = 50 },
            new MasterField { Column = "IsInactive", Label = "Inactive", Kind = FieldKind.Bool, Default = false },
        },
        InsertExtras = new Dictionary<string, object> { ["DateCreated"] = Now, ["Archived"] = false, ["DateLastUpdated"] = Now },
        UpdateExtras = new Dictionary<string, object> { ["DateLastUpdated"] = Now }
    };

    public static readonly MasterDefinition Locations = new()
    {
        Key = "locations", Title = "Locations / Branches", Icon = "\uE707", Permission = Perm.MasterLocations,
        Table = "Locations", KeyColumn = "LocationID",
        ListSql = @"SELECT LocationID AS Id, LocationCode AS [Code], LocationName AS [Location], Address, Phone,
                    CASE WHEN IsActive = 1 THEN 'Active' ELSE 'Inactive' END AS [Status]
                    FROM dbo.Locations WHERE IsDeleted = 0
                    AND (@Search IS NULL OR LocationCode LIKE '%' + @Search + '%' OR LocationName LIKE '%' + @Search + '%')
                    ORDER BY LocationID",
        Fields = new[]
        {
            new MasterField { Column = "LocationCode", Label = "Code (used in bill no)", Required = true, Unique = true, MaxLength = 10 },
            new MasterField { Column = "LocationName", Label = "Location name", Required = true, MaxLength = 100 },
            new MasterField { Column = "Address", Label = "Address", Kind = FieldKind.MultiLine, MaxLength = 300 },
            new MasterField { Column = "Phone", Label = "Phone", MaxLength = 50 },
            new MasterField { Column = "IsActive", Label = "Active", Kind = FieldKind.Bool, Default = true },
        },
        InsertExtras = Ins()
    };

    public static readonly MasterDefinition Taxes = new()
    {
        Key = "taxes", Title = "Taxes", Icon = "\uE8C7", Permission = Perm.MasterTaxes,
        Table = "Taxes", KeyColumn = "TaxID",
        ListSql = @"SELECT TaxID AS Id, TaxCode AS [Code], TaxName AS [Tax], Rate AS [Rate %],
                    CASE WHEN IsActive = 1 THEN 'Active' ELSE 'Inactive' END AS [Status]
                    FROM dbo.Taxes WHERE IsDeleted = 0 AND (@Search IS NULL OR TaxCode LIKE '%' + @Search + '%' OR TaxName LIKE '%' + @Search + '%')",
        Fields = new[]
        {
            new MasterField { Column = "TaxCode", Label = "Code", Required = true, Unique = true, MaxLength = 20 },
            new MasterField { Column = "TaxName", Label = "Tax name", Required = true, MaxLength = 100 },
            new MasterField { Column = "Rate", Label = "Rate %", Kind = FieldKind.Number, Required = true, Default = 0 },
            new MasterField { Column = "IsActive", Label = "Active", Kind = FieldKind.Bool, Default = true },
        },
        InsertExtras = new Dictionary<string, object> { ["DateCreated"] = Now, ["IsDeleted"] = false }
    };

    public const string PaymentKindLookup =
        "SELECT 1 Id,'' Code,'Cash' Name UNION ALL SELECT 2,'','Card' UNION ALL SELECT 3,'','Credit (customer account)' " +
        "UNION ALL SELECT 4,'','Cheque' UNION ALL SELECT 5,'','Credit Note' UNION ALL SELECT 6,'','Bank Transfer' " +
        "UNION ALL SELECT 7,'','Mobile / QR' UNION ALL SELECT 8,'','Voucher' UNION ALL SELECT 9,'','Other'";

    public static readonly MasterDefinition PaymentMethods = new()
    {
        Key = "paymentmethods", Title = "Payment Methods", Icon = "\uE8C7", Permission = Perm.MasterPaymentMethods,
        Table = "PaymentMethods", KeyColumn = "PaymentMethodID",
        ListSql = @"SELECT PaymentMethodID AS Id, Code, Name AS [Payment Method], ShortcutKey AS [Key], SortOrder AS [Sort],
                    CASE WHEN IsActive = 1 THEN 'Active' ELSE 'Inactive' END AS [Status]
                    FROM dbo.PaymentMethods WHERE IsDeleted = 0 AND (@Search IS NULL OR Code LIKE '%' + @Search + '%' OR Name LIKE '%' + @Search + '%')
                    ORDER BY SortOrder",
        Fields = new[]
        {
            new MasterField { Column = "Code", Label = "Code", Required = true, Unique = true, MaxLength = 20 },
            new MasterField { Column = "Name", Label = "Display name", Required = true, MaxLength = 50 },
            new MasterField { Column = "Kind", Label = "Type", Kind = FieldKind.Lookup, Required = true, LookupSql = PaymentKindLookup, Default = 1 },
            new MasterField { Column = "RequiresReference", Label = "Ask reference no (card/cheque/txn)", Kind = FieldKind.Bool, Default = false },
            new MasterField { Column = "OpensDrawer", Label = "Open cash drawer", Kind = FieldKind.Bool, Default = false },
            new MasterField { Column = "ShortcutKey", Label = "Shortcut key (F1-F8)", MaxLength = 5 },
            new MasterField { Column = "SortOrder", Label = "Sort order", Kind = FieldKind.Number, Default = 0 },
            new MasterField { Column = "IsActive", Label = "Active", Kind = FieldKind.Bool, Default = true },
        },
        InsertExtras = new Dictionary<string, object> { ["IsDeleted"] = false }
    };

    public static IReadOnlyList<MasterDefinition> All { get; } = new[]
    {
        Categories, Brands, Units, Suppliers, Customers, Employees, Locations, Taxes, PaymentMethods
    };
}

namespace PosSystem.Models;

public class Item
{
    public int ItemID { get; set; }
    public string? ItemCode { get; set; }
    public string? BarCode { get; set; }
    public string? ItemName { get; set; }
    public string? ItemDescription { get; set; }
    public int? ItemCategoryID { get; set; }
    public int? BrandID { get; set; }
    public int? UnitTypeId { get; set; }
    public int? TaxID { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal OurPrice { get; set; }
    public decimal ReorderLevel { get; set; }
    public bool IsService { get; set; }
    public bool IsWeighted { get; set; }
    public bool AllowDiscount { get; set; } = true;
    public bool AllowPriceChange { get; set; }
    public bool ShowInTouch { get; set; }
    public string? TouchColor { get; set; }
    public bool IsDeleted { get; set; }

    // read-only helpers loaded by queries
    public string? CategoryName { get; set; }
    public string? UnitName { get; set; }
    public decimal TaxRate { get; set; }
    public decimal StockOnHand { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(ItemName) ? ItemDescription ?? ItemCode ?? "" : ItemName!;
}

public class StockBatch
{
    public int ItemStockID { get; set; }
    public int ItemID { get; set; }
    public int LocationID { get; set; }
    public string? BatchNo { get; set; }
    public DateTime? ExpDate { get; set; }
    public decimal CurrentQuantity { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellingPrice { get; set; }
}

public class Location
{
    public int LocationID { get; set; }
    public string LocationCode { get; set; } = "";
    public string LocationName { get; set; } = "";
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
}

public class PaymentMethod
{
    public int PaymentMethodID { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Kind { get; set; }
    public bool RequiresReference { get; set; }
    public bool OpensDrawer { get; set; }
    public string? ShortcutKey { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Terminal
{
    public int PcRegistrationID { get; set; }
    public string MachineCode { get; set; } = "";
    public string Description { get; set; } = "";
    public int LocationID { get; set; } = 1;
    public string? ReceiptPrinter { get; set; }
    public int ReceiptFormat { get; set; } = 1;
    public decimal PaperWidthMm { get; set; } = 65;
    public int CharsPerLine { get; set; } = 40;
    public int PrintEngine { get; set; } = 1;
    public string? A5Printer { get; set; }
    public bool A5Landscape { get; set; } = true;
    public bool AutoPrint { get; set; } = true;
    public int PrintCopies { get; set; } = 1;
    public bool OpenCashDrawer { get; set; } = true;
    public bool DefaultTouchMode { get; set; }
}

public class UserAccount
{
    public int UserProfileId { get; set; }
    public string? userId { get; set; }
    public string? FullName { get; set; }
    public string? Password { get; set; }
    public int? UserGroupID { get; set; }
    public string? UserGroupName { get; set; }
    public bool IsSuperUser { get; set; }
    public bool IsSystemAccessible { get; set; }
    public bool IsDeleted { get; set; }
    public int? EmployeId { get; set; }
    public int? DefaultLocationID { get; set; }
    public decimal MaxDiscountPercent { get; set; }
}

public class Role
{
    public int UserGroupID { get; set; }
    public string UserGroupName { get; set; } = "";
    public bool IsSuperUser { get; set; }
    public decimal MaxDiscountPercent { get; set; }
}

public class Permission
{
    public int FormRuleID { get; set; }
    public string FormRuleName { get; set; } = "";
    public string? FormName { get; set; }
    public string? Description { get; set; }
}

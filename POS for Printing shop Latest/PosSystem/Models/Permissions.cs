namespace PosSystem.Models;

/// <summary>Permission keys. Must match FormRules.FormRuleName seeded by 01_Upgrade_Schema.sql.</summary>
public static class Perm
{
    public const string MasterItems = "Master.Items";
    public const string MasterCategories = "Master.Categories";
    public const string MasterBrands = "Master.Brands";
    public const string MasterUnits = "Master.Units";
    public const string MasterSuppliers = "Master.Suppliers";
    public const string MasterCustomers = "Master.Customers";
    public const string MasterEmployees = "Master.Employees";
    public const string MasterLocations = "Master.Locations";
    public const string MasterTaxes = "Master.Taxes";
    public const string MasterPaymentMethods = "Master.PaymentMethods";

    public const string PosAccess = "POS.Access";
    public const string PosLineDiscount = "POS.LineDiscount";
    public const string PosBillDiscount = "POS.BillDiscount";
    public const string PosPriceOverride = "POS.PriceOverride";
    public const string PosVoidLine = "POS.VoidLine";
    public const string PosCancelBill = "POS.CancelBill";
    public const string PosHold = "POS.Hold";
    public const string PosCreditSale = "POS.CreditSale";
    public const string PosReprint = "POS.Reprint";
    public const string PosOpenDrawer = "POS.OpenDrawer";
    public const string PosChangeLocation = "POS.ChangeLocation";
    public const string PosCancelInvoice = "POS.CancelInvoice";

    public const string ReturnsSales = "Returns.Sales";
    public const string ReturnsPurchase = "Returns.Purchase";

    public const string StockGrn = "Stock.GRN";
    public const string StockTransfer = "Stock.Transfer";
    public const string StockAdjustment = "Stock.Adjustment";
    public const string StockPriceChange = "Stock.PriceChange";
    public const string StockViewCost = "Stock.ViewCost";
    public const string StockBarcode = "Stock.Barcode";

    public const string CashPaidOut = "Cash.PaidOut";
    public const string CashReceived = "Cash.Received";
    public const string CashSupplierPayment = "Cash.SupplierPayment";
    public const string SessionClose = "Session.Close";
    public const string SessionViewAll = "Session.ViewAll";

    public const string ReportsSales = "Reports.Sales";
    public const string ReportsStock = "Reports.Stock";
    public const string ReportsPurchase = "Reports.Purchase";
    public const string ReportsFinancial = "Reports.Financial";

    public const string SecurityUsers = "Security.Users";
    public const string SecurityRoles = "Security.Roles";
    public const string SettingsManage = "Settings.Manage";
    public const string SettingsTerminal = "Settings.Terminal";
}

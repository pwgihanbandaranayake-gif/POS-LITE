namespace PosSystem.Models;

using System.Globalization;


/// <summary>Strongly typed view over the AppSettings key/value table ("common details").</summary>
public sealed class PosSettings
{
    public string ShopName { get; set; } = "MY SUPERMARKET";
    public string Address1 { get; set; } = "";
    public string Address2 { get; set; } = "";
    public string Address3 { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Mobile { get; set; } = "";
    public string Email { get; set; } = "";
    public string Web { get; set; } = "";
    public string TaxRegNo { get; set; } = "";
    public string ReceiptHeaderNote { get; set; } = "";
    public string ReceiptFooter { get; set; } = "Thank you! Come again.";
    public bool ShowSavings { get; set; } = true;
    public bool ShowItemCode { get; set; }
    public bool TaxEnabled { get; set; }
    public bool PricesIncludeTax { get; set; } = true;
    public decimal RoundTo { get; set; }
    public bool AllowNegativeStock { get; set; } = true;
    public string CurrencySymbol { get; set; } = "Rs.";
    public int CreditNoteValidDays { get; set; } = 90;
    public int ReturnDaysLimit { get; set; } = 14;

    // receipt look
    /// <summary>Shop logo as base64 PNG (uploaded in Settings > Shop details).</summary>
    public string LogoBase64 { get; set; } = "";
    public bool ShowLogo { get; set; } = true;
    public bool ShowBarcode { get; set; } = true;
    public string PoweredBy { get; set; } = "";

    // barcode sticker (Inventory > Barcode stickers)
    public decimal LabelWidthIn { get; set; } = 2.0m;       // one sticker, inches
    public decimal LabelHeightIn { get; set; } = 1.5m;
    public bool LabelLandscape { get; set; } = true;        // landscape = wider than tall
    public int LabelColumns { get; set; } = 2;              // stickers across
    public int LabelRows { get; set; } = 1;                 // stickers down
    public decimal LabelMarginTopMm { get; set; } = 0;
    public decimal LabelMarginLeftMm { get; set; } = 0;
    public decimal LabelGapHMm { get; set; } = 2;           // gap between columns
    public decimal LabelGapVMm { get; set; } = 2;           // gap between rows
    public string LabelPrinter { get; set; } = "";
    public bool LabelRotate { get; set; }                   // turn 90 degrees for printers that feed sideways
    public bool LabelShowShopName { get; set; } = true;
    public bool LabelShowWeight { get; set; } = true;
    public bool LabelShowPrice { get; set; } = true;
    public bool LabelShowCode { get; set; } = true;
    public bool LabelShowDates { get; set; }

    private static readonly (string Key, Func<PosSettings, string> Get, Action<PosSettings, string> Set)[] Map =
    {
        ("Shop.Name", s => s.ShopName, (s, v) => s.ShopName = v),
        ("Shop.Address1", s => s.Address1, (s, v) => s.Address1 = v),
        ("Shop.Address2", s => s.Address2, (s, v) => s.Address2 = v),
        ("Shop.Address3", s => s.Address3, (s, v) => s.Address3 = v),
        ("Shop.Phone", s => s.Phone, (s, v) => s.Phone = v),
        ("Shop.Mobile", s => s.Mobile, (s, v) => s.Mobile = v),
        ("Shop.Logo", s => s.LogoBase64, (s, v) => s.LogoBase64 = v),
        ("Receipt.ShowLogo", s => s.ShowLogo.ToString(), (s, v) => s.ShowLogo = B(v)),
        ("Receipt.ShowBarcode", s => s.ShowBarcode.ToString(), (s, v) => s.ShowBarcode = B(v)),
        ("Receipt.PoweredBy", s => s.PoweredBy, (s, v) => s.PoweredBy = v),
        ("Label.WidthIn", s => s.LabelWidthIn.ToString(CultureInfo.InvariantCulture), (s, v) => s.LabelWidthIn = D(v)),
        ("Label.HeightIn", s => s.LabelHeightIn.ToString(CultureInfo.InvariantCulture), (s, v) => s.LabelHeightIn = D(v)),
        ("Label.Landscape", s => s.LabelLandscape.ToString(), (s, v) => s.LabelLandscape = B(v)),
        ("Label.Columns", s => s.LabelColumns.ToString(), (s, v) => s.LabelColumns = (int)D(v)),
        ("Label.Rows", s => s.LabelRows.ToString(), (s, v) => s.LabelRows = (int)D(v)),
        ("Label.MarginTopMm", s => s.LabelMarginTopMm.ToString(CultureInfo.InvariantCulture), (s, v) => s.LabelMarginTopMm = D(v)),
        ("Label.MarginLeftMm", s => s.LabelMarginLeftMm.ToString(CultureInfo.InvariantCulture), (s, v) => s.LabelMarginLeftMm = D(v)),
        ("Label.GapHMm", s => s.LabelGapHMm.ToString(CultureInfo.InvariantCulture), (s, v) => s.LabelGapHMm = D(v)),
        ("Label.GapVMm", s => s.LabelGapVMm.ToString(CultureInfo.InvariantCulture), (s, v) => s.LabelGapVMm = D(v)),
        ("Label.Printer", s => s.LabelPrinter, (s, v) => s.LabelPrinter = v),
        ("Label.Rotate", s => s.LabelRotate.ToString(), (s, v) => s.LabelRotate = B(v)),
        ("Label.ShowShopName", s => s.LabelShowShopName.ToString(), (s, v) => s.LabelShowShopName = B(v)),
        ("Label.ShowWeight", s => s.LabelShowWeight.ToString(), (s, v) => s.LabelShowWeight = B(v)),
        ("Label.ShowPrice", s => s.LabelShowPrice.ToString(), (s, v) => s.LabelShowPrice = B(v)),
        ("Label.ShowCode", s => s.LabelShowCode.ToString(), (s, v) => s.LabelShowCode = B(v)),
        ("Label.ShowDates", s => s.LabelShowDates.ToString(), (s, v) => s.LabelShowDates = B(v)),
        ("Shop.Email", s => s.Email, (s, v) => s.Email = v),
        ("Shop.Web", s => s.Web, (s, v) => s.Web = v),
        ("Shop.TaxRegNo", s => s.TaxRegNo, (s, v) => s.TaxRegNo = v),
        ("Receipt.HeaderNote", s => s.ReceiptHeaderNote, (s, v) => s.ReceiptHeaderNote = v),
        ("Receipt.Footer", s => s.ReceiptFooter, (s, v) => s.ReceiptFooter = v),
        ("Receipt.ShowSavings", s => s.ShowSavings.ToString(), (s, v) => s.ShowSavings = B(v)),
        ("Receipt.ShowItemCode", s => s.ShowItemCode.ToString(), (s, v) => s.ShowItemCode = B(v)),
        ("Tax.Enabled", s => s.TaxEnabled.ToString(), (s, v) => s.TaxEnabled = B(v)),
        ("Tax.PricesIncludeTax", s => s.PricesIncludeTax.ToString(), (s, v) => s.PricesIncludeTax = B(v)),
        ("Billing.RoundTo", s => s.RoundTo.ToString(CultureInfo.InvariantCulture), (s, v) => s.RoundTo = D(v)),
        ("Billing.AllowNegativeStock", s => s.AllowNegativeStock.ToString(), (s, v) => s.AllowNegativeStock = B(v)),
        ("Billing.CurrencySymbol", s => s.CurrencySymbol, (s, v) => s.CurrencySymbol = v),
        ("Billing.CreditNoteValidDays", s => s.CreditNoteValidDays.ToString(), (s, v) => s.CreditNoteValidDays = (int)D(v)),
        ("Billing.ReturnDaysLimit", s => s.ReturnDaysLimit.ToString(), (s, v) => s.ReturnDaysLimit = (int)D(v)),
    };

    private static bool B(string v) => bool.TryParse(v, out var b) && b;
    private static decimal D(string v) => decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;

    public static PosSettings FromDictionary(IDictionary<string, string> values)
    {
        var s = new PosSettings();
        foreach (var m in Map)
            if (values.TryGetValue(m.Key, out var v) && v != null) m.Set(s, v);
        return s;
    }

    public Dictionary<string, string> ToDictionary() => Map.ToDictionary(m => m.Key, m => m.Get(this) ?? "");

    /// <summary>Address lines + "Mob : ... Tel : ..." line, printed under the shop name.</summary>
    public IEnumerable<string> HeaderLines()
    {
        foreach (var l in new[] { Address1, Address2, Address3 })
            if (!string.IsNullOrWhiteSpace(l)) yield return l;
        var phones = new List<string>();
        if (!string.IsNullOrWhiteSpace(Mobile)) phones.Add("Mob : " + Mobile);
        if (!string.IsNullOrWhiteSpace(Phone)) phones.Add("Tel : " + Phone);
        if (phones.Count > 0) yield return string.Join(" ", phones);
    }
}

namespace PosSystem.Services;

/// <summary>Adds the shop details / logo to a document and sends it to the printer set for this till.</summary>
public static class ReceiptService
{
    /// <summary>Fills the header (logo, name, address, Mob/Tel) and footer from Settings.</summary>
    public static ReceiptData Decorate(ReceiptData r)
    {
        var s = Session.Settings;
        r.ShopName = s.ShopName;
        r.HeaderLines = s.HeaderLines().ToList();
        r.TaxRegNo = string.IsNullOrWhiteSpace(s.TaxRegNo) ? null : s.TaxRegNo;
        r.HeaderNote = string.IsNullOrWhiteSpace(s.ReceiptHeaderNote) ? null : s.ReceiptHeaderNote;
        r.Footer = s.ReceiptFooter;
        r.Currency = s.CurrencySymbol;
        r.TaxInclusive = s.PricesIncludeTax;
        r.LogoBase64 = s.ShowLogo && !string.IsNullOrEmpty(s.LogoBase64) ? s.LogoBase64 : null;
        r.ShowBarcode = s.ShowBarcode;
        r.PoweredBy = string.IsNullOrWhiteSpace(s.PoweredBy) ? null : s.PoweredBy;
        return r;
    }

    public static async Task<ReceiptData?> LoadInvoiceAsync(int invoiceId, bool reprint)
    {
        var r = await SalesRepository.GetReceiptAsync(invoiceId);
        if (r == null) return null;
        r.IsReprint = reprint;
        return Decorate(r);
    }

    public static async Task PrintInvoiceAsync(int invoiceId, bool reprint, ReceiptFormat? format = null, int? copies = null)
    {
        var r = await LoadInvoiceAsync(invoiceId, reprint);
        if (r == null) return;
        int n = Math.Max(1, copies ?? Session.Terminal.PrintCopies);
        for (int i = 0; i < n; i++) ReceiptPrinter.Print(r, Session.Terminal, format);
    }

    public static void Print(ReceiptData r, ReceiptFormat? format = null) => ReceiptPrinter.Print(Decorate(r), Session.Terminal, format);

    public static void Preview(ReceiptData r, ReceiptFormat? format = null) => ReceiptPrinter.Preview(Decorate(r), Session.Terminal, format);

    public static void OpenDrawer() => ReceiptPrinter.OpenCashDrawer(Session.Terminal);
}

using System.Text;

namespace PosSystem.Printing;

/// <summary>One line of a text receipt. Logo / Barcode lines are images, not text.</summary>
public sealed class TextLine
{
    public string Text { get; init; } = "";
    public bool Bold { get; init; }
    public bool Big { get; init; }        // double width + height (ESC/POS)
    public bool Reverse { get; init; }    // white text on black bar (ESC/POS)
    public bool IsLogo { get; init; }
    public bool IsBarcode { get; init; }
}

/// <summary>
/// Fixed-width receipt for the "Direct text / ESC-POS" print method (dot matrix or fast thermal).
/// Same layout as the printed bill:
///   logo, shop name, address, Mob/Tel
///   Invoice No / Cashier / Date
///   PRODUCT  PRICE  DISC  AMOUNT  (black bar)
///   item name / qty  price  disc  amount
///   Net Total, Paid Amount, Change, You Saved, Items Count, barcode, Thank You
/// </summary>
public static class TextReceipt
{
    public static List<TextLine> Build(ReceiptData r, int w)
    {
        w = Math.Clamp(w, 24, 80);
        var o = new List<TextLine>();
        string M(decimal v) => v.ToString("0.00");
        string N(decimal v) => v.ToString("N2");

        void Add(string text, bool bold = false) => o.Add(new TextLine { Text = text, Bold = bold });
        void Center(string text, bool bold = false) { foreach (var l in Wrap(text, w)) Add(Pad(l, w, center: true), bold); }
        void LR(string left, string right, bool bold = false) => Add(LeftRight(left, right, w), bold);
        void Dash() => Add(new string('-', w));
        void Solid() => Add(new string('_', w));

        // ---------- header ----------
        if (!string.IsNullOrEmpty(r.LogoBase64)) o.Add(new TextLine { IsLogo = true });
        foreach (var l in Wrap(r.ShopName.ToUpperInvariant(), w / 2))
            o.Add(new TextLine { Text = Pad(l, w / 2, center: true), Bold = true, Big = true });
        foreach (var h in r.HeaderLines) Center(h);
        if (!string.IsNullOrEmpty(r.TaxRegNo)) Center("VAT Reg No : " + r.TaxRegNo);
        if (!string.IsNullOrEmpty(r.HeaderNote)) Center(r.HeaderNote!);
        Solid();

        // ---------- document info ----------
        if (r.Title != "INVOICE") Center(r.Title, true);
        if (r.IsReprint) Center("*** DUPLICATE COPY ***", true);
        LR(r.Title == "INVOICE" ? "Invoice No.." : "No..", r.DocumentNo, true);
        LR("Cashier:", r.Cashier);
        LR("Date:", r.Date.ToString("dd/MM/yyyy hh:mm:ss tt"));
        if (!string.IsNullOrEmpty(r.Customer)) LR("Customer:", r.Customer!);
        if (!string.IsNullOrEmpty(r.ReferenceNo)) LR("Ref:", r.ReferenceNo!);

        // ---------- items ----------
        if (r.Lines.Count > 0)
        {
            var c = Columns(w);
            o.Add(new TextLine { Text = Row("PRODUCT", "PRICE", "DISC", "AMOUNT", c), Bold = true, Reverse = true });
            foreach (var l in r.Lines)
            {
                foreach (var nameLine in Wrap(l.Name.ToUpperInvariant(), w)) Add(nameLine);
                Add(Row(l.Qty.ToString("0.000"), M(l.Price), M(l.Discount), M(l.Amount), c));
            }
            Solid();

            // ---------- totals ----------
            if (r.BillDiscount != 0 || r.Tax != 0 || r.RoundOff != 0)
            {
                LR("Sub Total", N(r.GrossTotal - r.LineDiscount));
                if (r.BillDiscount != 0) LR(r.BillDiscountLabel ?? "Bill Discount", "-" + N(r.BillDiscount));
                if (r.Tax != 0) LR(r.TaxInclusive ? "Tax (included)" : "Tax", N(r.Tax));
                if (r.RoundOff != 0) LR("Round Off", N(r.RoundOff));
            }
            LR("Net Total", N(r.NetTotal), true);
            LR("Paid Amount", N(r.PaidAmount), true);
            if (r.Payments.Count > 1 || r.Payments.Any(p => p.Name != "Cash"))
                foreach (var p in r.Payments)
                    LR("  " + p.Name + (string.IsNullOrEmpty(p.Ref) ? "" : " (" + Mask(p.Ref) + ")"), N(p.Amount));
            LR("Change", N(r.Change));

            var saved = r.LineDiscount + r.BillDiscount;
            if (saved > 0)
            {
                Center("You Saved");
                Center($"{r.Currency}{N(saved)}", true);
            }
            Dash();
            LR("Items Count", r.ItemCount.ToString());
        }

        // ---------- extra lines (Z report, return slip, paid-out voucher) ----------
        if (r.ExtraLines.Count > 0)
        {
            Dash();
            foreach (var (label, value) in r.ExtraLines)
                if (label == "--") Dash(); else LR(label, value, label == label.ToUpperInvariant());
            Dash();
        }

        // ---------- footer ----------
        if (r.ShowBarcode && !string.IsNullOrEmpty(r.DocumentNo)) o.Add(new TextLine { IsBarcode = true, Text = r.DocumentNo });
        if (!string.IsNullOrEmpty(r.Footer)) Center(r.Footer!);
        if (!string.IsNullOrEmpty(r.PoweredBy)) { Dash(); Center(r.PoweredBy!); }
        return o;
    }

    // ---- column layout: qty | price | disc | amount ----
    private static int[] Columns(int w)
    {
        int amount = Math.Max(9, w * 27 / 100), price = Math.Max(8, w * 26 / 100), disc = Math.Max(7, w * 22 / 100);
        return new[] { w - amount - price - disc, price, disc, amount };
    }

    private static string Row(string a, string b, string c, string d, int[] col) =>
        Fit(a, col[0]).PadRight(col[0]) + Fit(b, col[1]).PadLeft(col[1]) + Fit(c, col[2]).PadLeft(col[2]) + Fit(d, col[3]).PadLeft(col[3]);

    private static string Fit(string s, int width) => s.Length > width ? s[..width] : s;

    public static string LeftRight(string left, string right, int w)
    {
        if (right.Length >= w) return right[..w];
        var space = w - right.Length;
        if (left.Length > space - 1) left = left[..Math.Max(0, space - 1)];
        return left.PadRight(space) + right;
    }

    private static string Pad(string s, int w, bool center)
    {
        s = s.Length > w ? s[..w] : s;
        return center ? new string(' ', (w - s.Length) / 2) + s : s;
    }

    public static string Mask(string reference) =>
        reference.Length > 4 && reference.All(char.IsDigit) ? "****" + reference[^4..] : reference;

    public static IEnumerable<string> Wrap(string text, int width)
    {
        foreach (var para in (text ?? "").Replace("\r", "").Split('\n'))
        {
            var sb = new StringBuilder();
            foreach (var word in para.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var wd = word;
                while (wd.Length > width)
                {
                    if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                    yield return wd[..width];
                    wd = wd[width..];
                }
                if (sb.Length + wd.Length + (sb.Length > 0 ? 1 : 0) > width) { yield return sb.ToString(); sb.Clear(); }
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(wd);
            }
            yield return sb.ToString();
        }
    }

    /// <summary>Converts the lines to printer bytes. thermal = ESC/POS styles + logo + barcode + cut; otherwise plain text.</summary>
    public static byte[] ToBytes(List<TextLine> lines, bool thermal, string? logoBase64, int widthDots)
    {
        var b = new List<byte> { 0x1B, 0x40 };                          // ESC @ initialise
        foreach (var l in lines)
        {
            if (l.IsLogo)
            {
                if (thermal && logoBase64 != null) b.AddRange(LogoHelper.EscPos(logoBase64, widthDots * 6 / 10));
                continue;
            }
            if (l.IsBarcode)
            {
                if (thermal) b.AddRange(Barcode128.EscPos(l.Text));
                continue;
            }
            if (thermal)
            {
                if (l.Bold) b.AddRange(new byte[] { 0x1B, 0x45, 1 });   // bold on
                if (l.Big) b.AddRange(new byte[] { 0x1D, 0x21, 0x11 });  // double width + height
                if (l.Reverse) b.AddRange(new byte[] { 0x1D, 0x42, 1 }); // white on black
            }
            b.AddRange(l.Text.Select(ch => ch < 128 ? (byte)ch : (byte)'?'));
            if (thermal)
            {
                if (l.Reverse) b.AddRange(new byte[] { 0x1D, 0x42, 0 });
                if (l.Big) b.AddRange(new byte[] { 0x1D, 0x21, 0x00 });
                if (l.Bold) b.AddRange(new byte[] { 0x1B, 0x45, 0 });
            }
            b.Add(0x0A);
        }
        if (thermal)
            b.AddRange(new byte[] { 0x1D, 0x56, 0x42, 0x90 });   // GS V 66 n: feed paper to the cutter + 144 dots (~18 mm), then cut
        else
            b.AddRange(new byte[] { 0x0A, 0x0A, 0x0A, 0x0A, 0x0A, 0x0A });
        return b.ToArray();
    }

    /// <summary>Plain text for the on-screen preview.</summary>
    public static string ToPreviewText(List<TextLine> lines, int w) => string.Join(Environment.NewLine, lines.Select(l =>
        l.IsLogo ? Pad("[ LOGO ]", w, true) :
        l.IsBarcode ? Pad("||||| " + l.Text + " |||||", w, true) :
        l.Big ? Pad(l.Text.Trim(), w, true) : l.Text));

    public static readonly byte[] DrawerKick = { 0x1B, 0x70, 0x00, 0x19, 0xFA };   // ESC p 0 25 250
}

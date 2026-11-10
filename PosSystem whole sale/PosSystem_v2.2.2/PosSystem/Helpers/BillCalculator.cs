namespace PosSystem.Helpers;

public sealed record LineInput(decimal Quantity, decimal UnitPrice, DiscountType DiscountType, decimal DiscountValue, decimal TaxRate);

public sealed record LineResult(
    decimal Gross,
    decimal LineDiscount,
    decimal BillDiscountShare,
    decimal Net,
    decimal Tax,
    decimal Total);

public sealed record TaxConfig(bool Enabled, bool PricesIncludeTax);

public sealed record SaleTotals(
    IReadOnlyList<LineResult> Lines,
    decimal GrossTotal,
    decimal LineDiscountTotal,
    decimal SubTotal,
    decimal BillDiscount,
    decimal TaxTotal,
    decimal RoundOff,
    decimal NetTotal,
    decimal TotalQuantity)
{
    public decimal TotalDiscount => LineDiscountTotal + BillDiscount;
    public static readonly SaleTotals Empty = new(Array.Empty<LineResult>(), 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Pure pricing logic: line discount (% or amount) -> bill discount (% or amount, apportioned to lines)
/// -> tax (inclusive / exclusive) -> optional round-off.
/// </summary>
public static class SaleCalculator
{
    public static decimal CalcDiscount(decimal baseAmount, DiscountType type, decimal value)
    {
        if (baseAmount <= 0 || value <= 0) return 0;
        return type switch
        {
            DiscountType.Percent => Money.R(baseAmount * Money.Clamp(value, 0, 100) / 100m),
            DiscountType.Amount => Money.R(Math.Min(value, baseAmount)),
            _ => 0
        };
    }

    public static SaleTotals Calculate(IReadOnlyList<LineInput> lines, DiscountType billType, decimal billValue, TaxConfig tax, decimal roundTo = 0)
    {
        if (lines.Count == 0) return SaleTotals.Empty;

        var gross = new decimal[lines.Count];
        var lineDisc = new decimal[lines.Count];
        var afterLine = new decimal[lines.Count];

        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            gross[i] = Money.R(l.Quantity * l.UnitPrice);
            lineDisc[i] = CalcDiscount(gross[i], l.DiscountType, l.DiscountValue);
            afterLine[i] = gross[i] - lineDisc[i];
        }

        decimal subTotal = afterLine.Sum();
        decimal billDiscount = CalcDiscount(subTotal, billType, billValue);

        // apportion bill discount by value; last line takes the remainder so sums match exactly
        var share = new decimal[lines.Count];
        decimal allocated = 0;
        int lastPositive = Array.FindLastIndex(afterLine, a => a > 0);
        for (int i = 0; i < lines.Count; i++)
        {
            if (billDiscount <= 0 || subTotal <= 0 || afterLine[i] <= 0) continue;
            share[i] = i == lastPositive
                ? billDiscount - allocated
                : Money.R(billDiscount * afterLine[i] / subTotal);
            allocated += share[i];
        }

        var results = new List<LineResult>(lines.Count);
        decimal taxTotal = 0, totalBeforeRound = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            decimal net = afterLine[i] - share[i];
            decimal t = 0;
            decimal total = net;
            if (tax.Enabled && lines[i].TaxRate > 0)
            {
                var r = lines[i].TaxRate;
                if (tax.PricesIncludeTax)
                    t = Money.R(net * r / (100m + r));
                else
                {
                    t = Money.R(net * r / 100m);
                    total = net + t;
                }
            }
            taxTotal += t;
            totalBeforeRound += total;
            results.Add(new LineResult(gross[i], lineDisc[i], share[i], net, t, total));
        }

        decimal rounded = Money.RoundTo(totalBeforeRound, roundTo);
        return new SaleTotals(
            results,
            gross.Sum(),
            lineDisc.Sum(),
            subTotal,
            billDiscount,
            taxTotal,
            rounded - totalBeforeRound,
            rounded,
            lines.Sum(l => l.Quantity));
    }
}

public sealed record TenderLine(PaymentKind Kind, decimal Amount);

public sealed record PaymentStatus(decimal Due, decimal Paid, decimal Balance, decimal Change, bool IsSettled);

/// <summary>
/// Multi-tender rules: non-cash tenders can never exceed the remaining due.
/// Only cash may be over-tendered; the excess is returned as change.
/// </summary>
public static class PaymentCalculator
{
    public static PaymentStatus Evaluate(decimal due, IEnumerable<TenderLine> tenders)
    {
        var list = tenders.ToList();
        decimal nonCash = list.Where(t => t.Kind != PaymentKind.Cash).Sum(t => t.Amount);
        decimal cash = list.Where(t => t.Kind == PaymentKind.Cash).Sum(t => t.Amount);
        decimal paid = nonCash + cash;
        decimal change = paid > due ? Money.R(paid - due) : 0;
        if (change > cash) change = cash;
        decimal balance = Money.R(Math.Max(0, due - paid));
        return new PaymentStatus(due, paid, balance, change, paid >= due);
    }

    public static decimal MaxNonCashAllowed(decimal due, IEnumerable<TenderLine> tenders)
    {
        decimal nonCash = tenders.Where(t => t.Kind != PaymentKind.Cash).Sum(t => t.Amount);
        return Money.R(Math.Max(0, due - nonCash));
    }
}

namespace PosSystem.Services;

/// <summary>Final checks before a bill is saved.</summary>
public static class SalesService
{
    public static async Task<SaleResult> CompleteSaleAsync(SaleRequest req)
    {
        if (req.Lines.Count == 0) throw new AppException("The bill has no items.");
        if (req.Lines.Any(l => l.Quantity <= 0)) throw new AppException("Quantity must be greater than zero.");

        var applied = Money.R(req.Payments.Sum(p => p.Amount));
        if (applied != Money.R(req.NetTotal))
            throw new AppException($"Payments ({applied:N2}) do not match bill total ({req.NetTotal:N2}).");
        if (req.Payments.Any(p => p.Amount < 0)) throw new AppException("Payment amounts cannot be negative.");

        // credit sale: customer, permission and credit limit
        var credit = req.Payments.Where(p => p.Kind == PaymentKind.Credit).Sum(p => p.Amount);
        if (credit > 0)
        {
            if (req.CustomerId == null) throw new AppException("Select a customer for a credit sale.");
            if (!Session.Has(Perm.PosCreditSale)) throw new AppException("You do not have permission for credit sales.");
            var cust = await LookupRepository.GetCustomerAsync(req.CustomerId.Value);
            if (cust != null && cust.CreditLimit > 0 && cust.Outstanding + credit > cust.CreditLimit)
                throw new AppException($"Credit limit exceeded. Limit {cust.CreditLimit:N2}, outstanding {cust.Outstanding:N2}.");
        }

        // credit notes: exists, not expired, enough balance
        foreach (var cn in req.Payments.Where(p => p.Kind == PaymentKind.CreditNote))
        {
            var note = await ReturnRepository.GetCreditNoteAsync(cn.Reference ?? "");
            if (note == null || note.IsCancelled) throw new AppException($"Credit note '{cn.Reference}' not found.");
            if (note.ExpiryDate.HasValue && note.ExpiryDate.Value.Date < DateTime.Today) throw new AppException($"Credit note {note.CreditNoteNo} has expired.");
            if (cn.Amount > note.Balance) throw new AppException($"Credit note {note.CreditNoteNo} balance is only {note.Balance:N2}.");
            cn.CreditNoteId = note.CreditNoteID;
        }

        return await SalesRepository.SaveSaleAsync(req);
    }
}

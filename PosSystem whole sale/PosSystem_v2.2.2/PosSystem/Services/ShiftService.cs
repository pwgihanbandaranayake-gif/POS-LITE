namespace PosSystem.Services;

/// <summary>Cashier shift: open with a cash float, close with a cash count and print the Z report.</summary>
public static class ShiftService
{
    public static async Task<bool> HasOpenShiftAsync()
    {
        Session.ShiftId = await SessionRepository.GetOpenSessionIdAsync(Session.UserId, Session.TerminalId);
        return Session.ShiftId.HasValue;
    }

    public static async Task OpenAsync(decimal openingFloat)
    {
        Session.ShiftId = await SessionRepository.OpenAsync(Session.UserId, Session.TerminalId, Session.LocationId, openingFloat);
    }

    public static Task<SessionSummaryDto> GetSummaryAsync(int shiftId) => SessionRepository.GetSummaryAsync(shiftId);

    public static async Task CloseAsync(int shiftId, decimal countedCash, string denominations)
    {
        await SessionRepository.CloseAsync(shiftId, countedCash, denominations, Session.UserId);
        if (Session.ShiftId == shiftId) Session.ShiftId = null;
    }

    public static ReceiptData ToReceipt(SessionSummaryDto s, decimal counted)
    {
        var r = new ReceiptData
        {
            Title = "SHIFT CLOSE (Z) REPORT",
            DocumentNo = "SESSION #" + s.SessionId,
            Date = s.EndTime ?? DateTime.Now,
            Cashier = s.Cashier, Terminal = s.Terminal, Location = s.Location,
        };
        void L(string a, decimal v) => r.ExtraLines.Add((a, v.ToString("N2")));
        r.ExtraLines.Add(("Start", s.StartTime.ToString("yyyy-MM-dd HH:mm")));
        r.ExtraLines.Add(("End", (s.EndTime ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm")));
        r.ExtraLines.Add(("Bills / Cancelled", $"{s.Bills} / {s.CancelledBills}"));
        L("Gross sales", s.GrossSales);
        L("Discounts", s.Discounts);
        L("NET SALES", s.NetSales);
        r.ExtraLines.Add(("--", ""));
        foreach (var m in s.ByMethod) L("  " + m.Method, m.Amount);
        r.ExtraLines.Add(("--", ""));
        L("Opening float", s.OpeningBalance);
        L("+ Cash sales", s.CashSales);
        L("+ Received", s.ReceivedPayments);
        L("- Paid outs", s.PaidOuts);
        L("- Cash refunds", s.CashRefunds);
        L("EXPECTED CASH", s.ExpectedCash);
        L("COUNTED CASH", counted);
        var diff = counted - s.ExpectedCash;
        L(diff >= 0 ? "EXCESS" : "SHORTAGE", Math.Abs(diff));
        return r;
    }
}

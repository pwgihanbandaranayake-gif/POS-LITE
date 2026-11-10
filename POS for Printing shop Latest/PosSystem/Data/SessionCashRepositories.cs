namespace PosSystem.Data;

public static class SessionRepository
{

    public static async Task<int?> GetOpenSessionIdAsync(int userId, int terminalId)
    {
        await using var cn = await Db.OpenAsync();
        return await cn.ExecuteScalarAsync<int?>(@"SELECT TOP 1 SalesSessionID FROM dbo.SalesSessions
            WHERE UserProfileID = @userId AND PCCreated = @terminalId AND IsClosed = 0 AND ISNULL(IsDeleted,0) = 0 ORDER BY SalesSessionID DESC",
            new { userId, terminalId });
    }

    public static async Task<int> OpenAsync(int userId, int terminalId, int locationId, decimal openingBalance)
    {
        await using var cn = await Db.OpenAsync();
        return await cn.ExecuteScalarAsync<int>(@"
            DECLARE @idx INT = 1 + (SELECT COUNT(*) FROM dbo.SalesSessions WHERE UserProfileID = @userId AND CAST(StartTime AS DATE) = CAST(GETDATE() AS DATE));
            INSERT dbo.SalesSessions(UserProfileID, UserDailyIndex, TransactionDate, StartTime, OpeningBalance, IsClosed, IsDeleted,
                                     CreatedBy, DateCreated, PCCreated, LocationID, NoOfCustomers, NoOfCancelBills)
            VALUES(@userId, @idx, GETDATE(), GETDATE(), @openingBalance, 0, 0, @userId, GETDATE(), @terminalId, @locationId, 0, 0);
            SELECT CAST(SCOPE_IDENTITY() AS INT);", new { userId, terminalId, locationId, openingBalance });
    }

    public static async Task<SessionSummaryDto> GetSummaryAsync(int sessionId)
    {
        await using var cn = await Db.OpenAsync();
        return await BuildSummaryAsync(cn, null, sessionId);
    }

    private static async Task<SessionSummaryDto> BuildSummaryAsync(SqlConnection cn, SqlTransaction? tx, int sessionId)
    {
        var s = await cn.QueryFirstAsync<SessionSummaryDto>(@"
            SELECT ss.SalesSessionID AS SessionId, ISNULL(u.FullName,'') AS Cashier, ISNULL(pc.Description,'') AS Terminal,
                   ISNULL(l.LocationName,'') AS Location, ss.StartTime, ss.EndTime, ISNULL(ss.OpeningBalance,0) AS OpeningBalance
            FROM dbo.SalesSessions ss
            LEFT JOIN dbo.UserProfile u ON u.UserProfileId = ss.UserProfileID
            LEFT JOIN dbo.PcRegistration pc ON pc.PcRegistrationID = ss.PCCreated
            LEFT JOIN dbo.Locations l ON l.LocationID = ISNULL(ss.LocationID,1)
            WHERE ss.SalesSessionID = @sessionId", new { sessionId }, tx);

        var agg = await cn.QueryFirstAsync<(int Bills, int Cancelled, decimal Gross, decimal Disc, decimal Net)>(@"
            SELECT ISNULL(SUM(CASE WHEN ISNULL(IsCanceled,0) = 0 THEN 1 ELSE 0 END),0), ISNULL(SUM(CASE WHEN ISNULL(IsCanceled,0) = 1 THEN 1 ELSE 0 END),0),
                   ISNULL(SUM(CASE WHEN ISNULL(IsCanceled,0) = 0 THEN TotalAmount END),0), ISNULL(SUM(CASE WHEN ISNULL(IsCanceled,0) = 0 THEN TotalDiscount END),0),
                   ISNULL(SUM(CASE WHEN ISNULL(IsCanceled,0) = 0 THEN NetTotal END),0)
            FROM dbo.SalesInvoices WHERE SalesSessionID = @sessionId AND ISNULL(IsDeleted,0) = 0 AND NetTotal IS NOT NULL", new { sessionId }, tx);
        s.Bills = agg.Bills; s.CancelledBills = agg.Cancelled; s.GrossSales = agg.Gross; s.Discounts = agg.Disc; s.NetSales = agg.Net;

        var byMethod = (await cn.QueryAsync<(string Method, int Kind, decimal Amount)>(@"
            SELECT p.MethodName, p.Kind, SUM(p.Amount) FROM dbo.vw_SalesPayments p
            JOIN dbo.SalesInvoices s ON s.SalesInvoiceID = p.SalesInvoiceID
            WHERE s.SalesSessionID = @sessionId AND ISNULL(s.IsCanceled,0) = 0 AND ISNULL(s.IsDeleted,0) = 0
            GROUP BY p.MethodName, p.Kind ORDER BY p.Kind", new { sessionId }, tx)).ToList();
        s.ByMethod = byMethod.Select(m => (m.Method, m.Amount)).ToList();
        s.CashSales = byMethod.Where(m => m.Kind == (int)PaymentKind.Cash).Sum(m => m.Amount);
        s.CardSales = byMethod.Where(m => m.Kind == (int)PaymentKind.Card).Sum(m => m.Amount);
        s.CreditSales = byMethod.Where(m => m.Kind == (int)PaymentKind.Credit).Sum(m => m.Amount);
        s.ChequeSales = byMethod.Where(m => m.Kind == (int)PaymentKind.Cheque).Sum(m => m.Amount);
        s.CreditNoteRedeemed = byMethod.Where(m => m.Kind == (int)PaymentKind.CreditNote).Sum(m => m.Amount);
        s.OtherSales = byMethod.Where(m => m.Kind > (int)PaymentKind.CreditNote).Sum(m => m.Amount);

        s.PaidOuts = await cn.ExecuteScalarAsync<decimal>("SELECT ISNULL(SUM(Amount),0) FROM dbo.PayOut WHERE SalesSessionID = @sessionId AND ISNULL(IsDeleted,0) = 0", new { sessionId }, tx);
        s.ReceivedPayments = await cn.ExecuteScalarAsync<decimal>(@"
            SELECT ISNULL(SUM(r.Amount),0) FROM dbo.ReceivedAmount r LEFT JOIN dbo.PaymentMethods pm ON pm.PaymentMethodID = r.PaymentMethodID
            WHERE r.SalesSessionID = @sessionId AND ISNULL(r.IsDeleted,0) = 0 AND (r.PaymentMethodID IS NULL OR pm.Kind = 1)", new { sessionId }, tx);
        s.CashRefunds = await cn.ExecuteScalarAsync<decimal>("SELECT ISNULL(SUM(TotalAmount),0) FROM dbo.SalesReturns WHERE SalesSessionID = @sessionId AND RefundMethod = 1 AND IsDeleted = 0", new { sessionId }, tx);
        return s;
    }

    public static Task CloseAsync(int sessionId, decimal countedCash, string denominations, int closedBy) => Db.InTransactionAsync(async (cn, tx) =>
    {
        var s = await BuildSummaryAsync(cn, tx, sessionId);
        var diff = countedCash - s.ExpectedCash;
        await cn.ExecuteAsync(@"
            UPDATE dbo.SalesSessions SET EndTime = GETDATE(), NoOfCustomers = @Bills, NoOfCancelBills = @CancelledBills,
                TotalCashAmount = @CashSales, TotalDiscountAmount = @Discounts, TotalCreditAmount = @CreditSales, TotalCardAmount = @CardSales,
                TotalChequeAmount = @ChequeSales, TotalOtherAmount = @other, TotalPaidOuts = @PaidOuts, TotalReceivedPayments = @ReceivedPayments,
                TotalReturnCash = @CashRefunds, TotalRefundAmount = @CashRefunds, TotalCashInHand = @countedCash, TotalCashInSystem = @expected,
                Excess = @excess, Shortage = @shortage, Denominations = @denominations, IsClosed = 1, ClosedBy = @closedBy, ModifiedBy = @closedBy
            WHERE SalesSessionID = @sessionId AND IsClosed = 0",
            new
            {
                s.Bills, s.CancelledBills, s.CashSales, s.Discounts, s.CreditSales, s.CardSales, s.ChequeSales,
                other = s.OtherSales + s.CreditNoteRedeemed, s.PaidOuts, s.ReceivedPayments, s.CashRefunds, countedCash,
                expected = s.ExpectedCash, excess = diff > 0 ? diff : 0, shortage = diff < 0 ? -diff : 0, denominations, closedBy, sessionId
            }, tx);
        return 0;
    });
}

public static class CashRepository
{

    public static Task<string> SaveAsync(CashEntryRequest r) => Db.InTransactionAsync(async (cn, tx) =>
    {
        if (r.Amount <= 0) throw new AppException("Enter an amount greater than zero.");
        if (r.IsPaidOut)
        {
            var no = await Db.NextDocNoAsync(cn, tx, DocumentTypes.PaidOut, r.LocationID);
            await cn.ExecuteAsync(@"
                INSERT dbo.PayOut(GivenBy, ApprovedBy, EmployeeID, Amount, PayOutCategory, Description, DateGiven, SalesSessionID,
                    DateCreated, DateModified, CreatedBy, IsDeleted, PCCreated, POType, HasSync, LocationID, PayOutNo)
                VALUES(@UserID, @ApprovedBy, @EmployeeID, @Amount, @Category, @Description, GETDATE(), @SessionID,
                    GETDATE(), GETDATE(), @UserID, 0, @TerminalID, 'PAIDOUT', 0, @LocationID, @no)",
                new { r.UserID, r.ApprovedBy, r.EmployeeID, r.Amount, r.Category, r.Description, r.SessionID, r.TerminalID, r.LocationID, no }, tx);
            return no;
        }
        else
        {
            var no = await Db.NextDocNoAsync(cn, tx, DocumentTypes.Received, r.LocationID);
            await cn.ExecuteAsync(@"
                INSERT dbo.ReceivedAmount(ReceivedBy, ApprovedBy, Amount, Description, DateReceived, SalesSessionID, DateCreated, DateModified,
                    CreatedBy, IsDeleted, PCCreated, CustomerID, RAType, HasSync, ChequeNo, Bank, LocationID, ReceiptNo, PaymentMethodID)
                VALUES(@UserID, @ApprovedBy, @Amount, @Description, GETDATE(), @SessionID, GETDATE(), GETDATE(),
                    @UserID, 0, @TerminalID, @CustomerID, @type, 0, @ChequeNo, @Bank, @LocationID, @no, @PaymentMethodID)",
                new { r.UserID, r.ApprovedBy, r.Amount, r.Description, r.SessionID, r.TerminalID, r.CustomerID,
                      type = r.CustomerID.HasValue ? "CUSTOMER" : (r.Category ?? "OTHER"), r.ChequeNo, r.Bank, r.LocationID, no, r.PaymentMethodID }, tx);
            return no;
        }
    });
}

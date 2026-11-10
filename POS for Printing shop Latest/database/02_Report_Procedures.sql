/* =====================================================================
   POS SYSTEM v2 - REPORT PROCEDURES (SQL Server 2012+)
   Every report is one stored procedure: rpt_*
   Common parameters: @FromDate, @ToDate (inclusive dates), @LocationID (NULL = all)
   ===================================================================== */
SET NOCOUNT ON;
GO

/* ---------- helper views ---------- */
IF OBJECT_ID('dbo.vw_SalesPayments','V') IS NOT NULL DROP VIEW dbo.vw_SalesPayments;
GO
CREATE VIEW dbo.vw_SalesPayments AS
SELECT p.SalesInvoicePaymentID, p.SalesInvoiceID, p.Amount, p.ReferenceNo,
       ISNULL(pm.Name, ISNULL(p.PayType,'CASH')) AS MethodName,
       ISNULL(pm.Kind, CASE ISNULL(p.PayType,'CASH')
                            WHEN 'CASH' THEN 1 WHEN 'CARD' THEN 2 WHEN 'CREDIT' THEN 3
                            WHEN 'CHEQUE' THEN 4 ELSE 9 END) AS Kind
FROM dbo.SalesInvoicePayments p
LEFT JOIN dbo.PaymentMethods pm ON pm.PaymentMethodID = p.PaymentMethodID
WHERE ISNULL(p.IsDeleted,0) = 0;
GO
IF OBJECT_ID('dbo.vw_ValidSales','V') IS NOT NULL DROP VIEW dbo.vw_ValidSales;
GO
CREATE VIEW dbo.vw_ValidSales AS
SELECT si.*, ISNULL(si.LocationID,1) AS LocID
FROM dbo.SalesInvoices si
WHERE ISNULL(si.IsDeleted,0) = 0 AND ISNULL(si.IsCanceled,0) = 0 AND si.NetTotal IS NOT NULL;
GO

/* ===================== SALES ===================== */

/* 1. Daily sales summary */
IF OBJECT_ID('dbo.rpt_DailySalesSummary','P') IS NOT NULL DROP PROCEDURE dbo.rpt_DailySalesSummary;
GO
CREATE PROCEDURE dbo.rpt_DailySalesSummary @FromDate DATETIME, @ToDate DATETIME, @LocationID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CONVERT(VARCHAR(10), CAST(s.TransactionDate AS DATE), 120) AS [Date],
           COUNT(*)                                   AS [Bills],
           SUM(ISNULL(s.TotalAmount,0))               AS [Gross],
           SUM(ISNULL(s.TotalDiscount,0))             AS [Discount],
           SUM(ISNULL(s.TotalTax,0))                  AS [Tax],
           SUM(ISNULL(s.NetTotal,0))                  AS [Net Sales],
           SUM(ISNULL(pay.Cash,0))                    AS [Cash],
           SUM(ISNULL(pay.Card,0))                    AS [Card],
           SUM(ISNULL(pay.Credit,0))                  AS [Credit],
           SUM(ISNULL(pay.Other,0))                   AS [Other],
           CAST(SUM(ISNULL(s.NetTotal,0)) / NULLIF(COUNT(*),0) AS DECIMAL(18,2)) AS [Avg Bill]
    FROM dbo.vw_ValidSales s
    OUTER APPLY (SELECT SUM(CASE WHEN Kind = 1 THEN Amount ELSE 0 END) AS Cash,
                        SUM(CASE WHEN Kind = 2 THEN Amount ELSE 0 END) AS Card,
                        SUM(CASE WHEN Kind = 3 THEN Amount ELSE 0 END) AS Credit,
                        SUM(CASE WHEN Kind NOT IN (1,2,3) THEN Amount ELSE 0 END) AS Other
                 FROM dbo.vw_SalesPayments p WHERE p.SalesInvoiceID = s.SalesInvoiceID) pay
    WHERE s.TransactionDate >= @FromDate AND s.TransactionDate < DATEADD(DAY,1,@ToDate)
      AND (@LocationID IS NULL OR s.LocID = @LocationID)
    GROUP BY CAST(s.TransactionDate AS DATE)
    ORDER BY CAST(s.TransactionDate AS DATE);
END
GO

/* 2. Invoice list */
IF OBJECT_ID('dbo.rpt_SalesInvoiceList','P') IS NOT NULL DROP PROCEDURE dbo.rpt_SalesInvoiceList;
GO
CREATE PROCEDURE dbo.rpt_SalesInvoiceList @FromDate DATETIME, @ToDate DATETIME, @LocationID INT = NULL, @UserID INT = NULL, @CustomerID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.InvoiceNo AS [Invoice No],
           CONVERT(VARCHAR(16), s.TransactionDate, 120) AS [Date Time],
           l.LocationCode AS [Location],
           u.FullName AS [Cashier],
           ISNULL(c.Name, s.CustomerName) AS [Customer],
           (SELECT COUNT(*) FROM dbo.SalesInvoiceDetails d WHERE d.SalesInvoiceID = s.SalesInvoiceID AND ISNULL(d.IsDeleted,0)=0) AS [Lines],
           ISNULL(s.TotalAmount,0)   AS [Gross],
           ISNULL(s.TotalDiscount,0) AS [Discount],
           ISNULL(s.TotalTax,0)      AS [Tax],
           ISNULL(s.NetTotal,0)      AS [Net],
           STUFF((SELECT ', ' + p.MethodName FROM dbo.vw_SalesPayments p WHERE p.SalesInvoiceID = s.SalesInvoiceID
                  FOR XML PATH('')), 1, 2, '') AS [Payment]
    FROM dbo.vw_ValidSales s
    LEFT JOIN dbo.UserProfile u ON u.UserProfileId = s.CreatedBy
    LEFT JOIN dbo.Customers c ON c.CustomerID = s.CustomerID
    LEFT JOIN dbo.Locations l ON l.LocationID = s.LocID
    WHERE s.TransactionDate >= @FromDate AND s.TransactionDate < DATEADD(DAY,1,@ToDate)
      AND (@LocationID IS NULL OR s.LocID = @LocationID)
      AND (@UserID IS NULL OR s.CreatedBy = @UserID)
      AND (@CustomerID IS NULL OR s.CustomerID = @CustomerID)
    ORDER BY s.TransactionDate;
END
GO

/* 3. Item wise sales */
IF OBJECT_ID('dbo.rpt_ItemWiseSales','P') IS NOT NULL DROP PROCEDURE dbo.rpt_ItemWiseSales;
GO
CREATE PROCEDURE dbo.rpt_ItemWiseSales @FromDate DATETIME, @ToDate DATETIME, @LocationID INT = NULL, @CategoryID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT i.ItemCode AS [Code], ISNULL(i.ItemName, i.ItemDescription) AS [Item], cat.ItemCategoryDescription AS [Category],
           SUM(d.Quantity) AS [Qty],
           SUM(ISNULL(d.Amount,0)) AS [Gross],
           SUM(ISNULL(d.DiscountVal,0) + ISNULL(d.BillDiscountShare,0)) AS [Discount],
           SUM(ISNULL(d.NetAmount,0)) AS [Net],
           CAST(SUM(ISNULL(d.NetAmount,0)) / NULLIF(SUM(d.Quantity),0) AS DECIMAL(18,2)) AS [Avg Price]
    FROM dbo.SalesInvoiceDetails d
    JOIN dbo.vw_ValidSales s ON s.SalesInvoiceID = d.SalesInvoiceID
    JOIN dbo.Item i ON i.ItemID = d.ItemID
    LEFT JOIN dbo.ItemCategories cat ON cat.ItemCategoryId = i.ItemCategoryID
    WHERE ISNULL(d.IsDeleted,0) = 0
      AND s.TransactionDate >= @FromDate AND s.TransactionDate < DATEADD(DAY,1,@ToDate)
      AND (@LocationID IS NULL OR s.LocID = @LocationID)
      AND (@CategoryID IS NULL OR i.ItemCategoryID = @CategoryID)
    GROUP BY i.ItemCode, ISNULL(i.ItemName, i.ItemDescription), cat.ItemCategoryDescription
    ORDER BY [Net] DESC;
END
GO

/* 4. Category wise sales */
IF OBJECT_ID('dbo.rpt_CategoryWiseSales','P') IS NOT NULL DROP PROCEDURE dbo.rpt_CategoryWiseSales;
GO
CREATE PROCEDURE dbo.rpt_CategoryWiseSales @FromDate DATETIME, @ToDate DATETIME, @LocationID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Total DECIMAL(18,2);
    SELECT ISNULL(cat.ItemCategoryDescription,'(No category)') AS [Category],
           COUNT(DISTINCT d.ItemID) AS [Items],
           SUM(d.Quantity) AS [Qty],
           SUM(ISNULL(d.NetAmount,0)) AS [Net Sales]
    INTO #c
    FROM dbo.SalesInvoiceDetails d
    JOIN dbo.vw_ValidSales s ON s.SalesInvoiceID = d.SalesInvoiceID
    JOIN dbo.Item i ON i.ItemID = d.ItemID
    LEFT JOIN dbo.ItemCategories cat ON cat.ItemCategoryId = i.ItemCategoryID
    WHERE ISNULL(d.IsDeleted,0) = 0
      AND s.TransactionDate >= @FromDate AND s.TransactionDate < DATEADD(DAY,1,@ToDate)
      AND (@LocationID IS NULL OR s.LocID = @LocationID)
    GROUP BY cat.ItemCategoryDescription;
    SELECT @Total = SUM([Net Sales]) FROM #c;
    SELECT *, CAST([Net Sales] * 100.0 / NULLIF(@Total,0) AS DECIMAL(9,2)) AS [Share %] FROM #c ORDER BY [Net Sales] DESC;
END
GO

/* 5. Hourly sales */
IF OBJECT_ID('dbo.rpt_HourlySales','P') IS NOT NULL DROP PROCEDURE dbo.rpt_HourlySales;
GO
CREATE PROCEDURE dbo.rpt_HourlySales @FromDate DATETIME, @ToDate DATETIME, @LocationID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT RIGHT('0' + CAST(DATEPART(HOUR, s.TransactionDate) AS VARCHAR(2)),2) + ':00 - '
         + RIGHT('0' + CAST(DATEPART(HOUR, s.TransactionDate) AS VARCHAR(2)),2) + ':59' AS [Hour],
           COUNT(*) AS [Bills],
           SUM(ISNULL(s.NetTotal,0)) AS [Net Sales],
           CAST(SUM(ISNULL(s.NetTotal,0)) / NULLIF(COUNT(*),0) AS DECIMAL(18,2)) AS [Avg Bill]
    FROM dbo.vw_ValidSales s
    WHERE s.TransactionDate >= @FromDate AND s.TransactionDate < DATEADD(DAY,1,@ToDate)
      AND (@LocationID IS NULL OR s.LocID = @LocationID)
    GROUP BY DATEPART(HOUR, s.TransactionDate)
    ORDER BY DATEPART(HOUR, s.TransactionDate);
END
GO

/* 6. Cashier wise sales */
IF OBJECT_ID('dbo.rpt_CashierWiseSales','P') IS NOT NULL DROP PROCEDURE dbo.rpt_CashierWiseSales;
GO
CREATE PROCEDURE dbo.rpt_CashierWiseSales @FromDate DATETIME, @ToDate DATETIME, @LocationID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT u.FullName AS [Cashier],
           SUM(CASE WHEN ISNULL(s.IsCanceled,0)=0 THEN 1 ELSE 0 END) AS [Bills],
           SUM(CASE WHEN ISNULL(s.IsCanceled,0)=1 THEN 1 ELSE 0 END) AS [Cancelled],
           SUM(CASE WHEN ISNULL(s.IsCanceled,0)=0 THEN ISNULL(s.TotalDiscount,0) ELSE 0 END) AS [Discount],
           SUM(CASE WHEN ISNULL(s.IsCanceled,0)=0 THEN ISNULL(s.NetTotal,0) ELSE 0 END) AS [Net Sales]
    FROM dbo.SalesInvoices s
    LEFT JOIN dbo.UserProfile u ON u.UserProfileId = s.CreatedBy
    WHERE ISNULL(s.IsDeleted,0) = 0 AND s.NetTotal IS NOT NULL
      AND s.TransactionDate >= @FromDate AND s.TransactionDate < DATEADD(DAY,1,@ToDate)
      AND (@LocationID IS NULL OR ISNULL(s.LocationID,1) = @LocationID)
    GROUP BY u.FullName
    ORDER BY [Net Sales] DESC;
END
GO

/* 7. Payment method summary */
IF OBJECT_ID('dbo.rpt_PaymentMethodSummary','P') IS NOT NULL DROP PROCEDURE dbo.rpt_PaymentMethodSummary;
GO
CREATE PROCEDURE dbo.rpt_PaymentMethodSummary @FromDate DATETIME, @ToDate DATETIME, @LocationID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.MethodName AS [Payment Method],
           COUNT(DISTINCT p.SalesInvoiceID) AS [Bills],
           SUM(p.Amount) AS [Amount]
    FROM dbo.vw_SalesPayments p
    JOIN dbo.vw_ValidSales s ON s.SalesInvoiceID = p.SalesInvoiceID
    WHERE s.TransactionDate >= @FromDate AND s.TransactionDate < DATEADD(DAY,1,@ToDate)
      AND (@LocationID IS NULL OR s.LocID = @LocationID)
    GROUP BY p.MethodName
    ORDER BY [Amount] DESC;
END
GO

/* 8. Discount report */
IF OBJECT_ID('dbo.rpt_DiscountReport','P') IS NOT NULL DROP PROCEDURE dbo.rpt_DiscountReport;
GO
CREATE PROCEDURE dbo.rpt_DiscountReport @FromDate DATETIME, @ToDate DATETIME, @LocationID INT = NULL, @UserID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.InvoiceNo AS [Invoice No], CONVERT(VARCHAR(16), s.TransactionDate, 120) AS [Date Time],
           u.FullName AS [Cashier],
           ISNULL(s.TotalAmount,0) AS [Gross],
           ISNULL(s.LineDiscountAmount, ISNULL(s.TotalDiscount,0)) AS [Item Discount],
           ISNULL(s.BillDiscountAmount,0) AS [Bill Discount],
           CASE ISNULL(s.BillDiscountType,0) WHEN 1 THEN CAST(s.BillDiscountValue AS VARCHAR(20)) + ' %' WHEN 2 THEN 'Amount' ELSE '' END AS [Bill Disc Type],
           ISNULL(s.TotalDiscount,0) AS [Total Discount],
           ISNULL(s.NetTotal,0) AS [Net]
    FROM dbo.vw_ValidSales s
    LEFT JOIN dbo.UserProfile u ON u.UserProfileId = s.CreatedBy
    WHERE ISNULL(s.TotalDiscount,0) > 0
      AND s.TransactionDate >= @FromDate AND s.TransactionDate < DATEADD(DAY,1,@ToDate)
      AND (@LocationID IS NULL OR s.LocID = @LocationID)
      AND (@UserID IS NULL OR s.CreatedBy = @UserID)
    ORDER BY s.TransactionDate;
END
GO

/* 9. Tax report */
IF OBJECT_ID('dbo.rpt_TaxReport','P') IS NOT NULL DROP PROCEDURE dbo.rpt_TaxReport;
GO
CREATE PROCEDURE dbo.rpt_TaxReport @FromDate DATETIME, @ToDate DATETIME, @LocationID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CONVERT(VARCHAR(10), CAST(s.TransactionDate AS DATE), 120) AS [Date],
           ISNULL(t.TaxName, 'Tax ' + CAST(d.TaxRate AS VARCHAR(10)) + '%') AS [Tax],
           CAST(d.TaxRate AS DECIMAL(9,2)) AS [Rate %],
           SUM(CASE WHEN ISNULL(s.PricesIncludeTax,1)=1 THEN ISNULL(d.NetAmount,0) - ISNULL(d.TaxAmount,0) ELSE ISNULL(d.NetAmount,0) END) AS [Taxable Value],
           SUM(ISNULL(d.TaxAmount,0)) AS [Tax Amount]
    FROM dbo.SalesInvoiceDetails d
    JOIN dbo.vw_ValidSales s ON s.SalesInvoiceID = d.SalesInvoiceID
    LEFT JOIN dbo.Taxes t ON t.TaxID = d.TaxID
    WHERE ISNULL(d.IsDeleted,0) = 0 AND ISNULL(d.TaxAmount,0) <> 0
      AND s.TransactionDate >= @FromDate AND s.TransactionDate < DATEADD(DAY,1,@ToDate)
      AND (@LocationID IS NULL OR s.LocID = @LocationID)
    GROUP BY CAST(s.TransactionDate AS DATE), ISNULL(t.TaxName, 'Tax ' + CAST(d.TaxRate AS VARCHAR(10)) + '%'), d.TaxRate
    ORDER BY CAST(s.TransactionDate AS DATE);
END
GO

/* 10. Sales returns */
IF OBJECT_ID('dbo.rpt_SalesReturns','P') IS NOT NULL DROP PROCEDURE dbo.rpt_SalesReturns;
GO
CREATE PROCEDURE dbo.rpt_SalesReturns @FromDate DATETIME, @ToDate DATETIME, @LocationID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT r.ReturnNo AS [Return No], CONVERT(VARCHAR(16), r.ReturnDate, 120) AS [Date Time],
           r.InvoiceNo AS [Invoice No], i.ItemCode AS [Code], ISNULL(i.ItemName, i.ItemDescription) AS [Item],
           d.Quantity AS [Qty], d.Rate AS [Rate], d.Amount AS [Amount],
           CASE WHEN d.IsDamaged = 1 THEN 'Damaged' ELSE 'Restocked' END AS [Stock],
           CASE r.RefundMethod WHEN 1 THEN 'Cash' ELSE 'Credit Note' END AS [Refund],
           u.FullName AS [User], r.Reason
    FROM dbo.SalesReturns r
    JOIN dbo.SalesReturnDetails d ON d.SalesReturnID = r.SalesReturnID
    JOIN dbo.Item i ON i.ItemID = d.ItemID
    LEFT JOIN dbo.UserProfile u ON u.UserProfileId = r.CreatedBy
    WHERE r.IsDeleted = 0
      AND r.ReturnDate >= @FromDate AND r.ReturnDate < DATEADD(DAY,1,@ToDate)
      AND (@LocationID IS NULL OR r.LocationID = @LocationID)
    ORDER BY r.ReturnDate;
END
GO

/* 11. Cancelled bills */
IF OBJECT_ID('dbo.rpt_CancelledBills','P') IS NOT NULL DROP PROCEDURE dbo.rpt_CancelledBills;
GO
CREATE PROCEDURE dbo.rpt_CancelledBills @FromDate DATETIME, @ToDate DATETIME, @LocationID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.InvoiceNo AS [Invoice No], CONVERT(VARCHAR(16), s.TransactionDate, 120) AS [Bill Date],
           u.FullName AS [Cashier], cu.FullName AS [Cancelled By],
           CONVERT(VARCHAR(16), s.CancelledDate, 120) AS [Cancelled On],
           s.CancelReason AS [Reason], ISNULL(s.NetTotal, s.TotalAmount) AS [Amount]
    FROM dbo.SalesInvoices s
    LEFT JOIN dbo.UserProfile u ON u.UserProfileId = s.CreatedBy
    LEFT JOIN dbo.UserProfile cu ON cu.UserProfileId = s.CancelledBy
    WHERE ISNULL(s.IsCanceled,0) = 1
      AND s.TransactionDate >= @FromDate AND s.TransactionDate < DATEADD(DAY,1,@ToDate)
      AND (@LocationID IS NULL OR ISNULL(s.LocationID,1) = @LocationID)
    ORDER BY s.TransactionDate;
END
GO

/* 12. Gross profit by item */
IF OBJECT_ID('dbo.rpt_GrossProfit','P') IS NOT NULL DROP PROCEDURE dbo.rpt_GrossProfit;
GO
CREATE PROCEDURE dbo.rpt_GrossProfit @FromDate DATETIME, @ToDate DATETIME, @LocationID INT = NULL, @CategoryID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT i.ItemCode AS [Code], ISNULL(i.ItemName, i.ItemDescription) AS [Item],
           SUM(d.Quantity) AS [Qty],
           SUM(ISNULL(d.NetAmount,0) - CASE WHEN ISNULL(s.PricesIncludeTax,1)=1 THEN ISNULL(d.TaxAmount,0) ELSE 0 END) AS [Revenue (ex tax)],
           SUM(d.Quantity * ISNULL(d.CostPrice, ISNULL(i.CostPrice,0))) AS [Cost],
           SUM(ISNULL(d.NetAmount,0) - CASE WHEN ISNULL(s.PricesIncludeTax,1)=1 THEN ISNULL(d.TaxAmount,0) ELSE 0 END
               - d.Quantity * ISNULL(d.CostPrice, ISNULL(i.CostPrice,0))) AS [Profit]
    INTO #p
    FROM dbo.SalesInvoiceDetails d
    JOIN dbo.vw_ValidSales s ON s.SalesInvoiceID = d.SalesInvoiceID
    JOIN dbo.Item i ON i.ItemID = d.ItemID
    WHERE ISNULL(d.IsDeleted,0) = 0
      AND s.TransactionDate >= @FromDate AND s.TransactionDate < DATEADD(DAY,1,@ToDate)
      AND (@LocationID IS NULL OR s.LocID = @LocationID)
      AND (@CategoryID IS NULL OR i.ItemCategoryID = @CategoryID)
    GROUP BY i.ItemCode, ISNULL(i.ItemName, i.ItemDescription);
    SELECT *, CAST([Profit] * 100.0 / NULLIF([Revenue (ex tax)],0) AS DECIMAL(9,2)) AS [Margin %] FROM #p ORDER BY [Profit] DESC;
END
GO

/* ===================== PURCHASE ===================== */

/* 13. Purchase summary (GRN) */
IF OBJECT_ID('dbo.rpt_PurchaseSummary','P') IS NOT NULL DROP PROCEDURE dbo.rpt_PurchaseSummary;
GO
CREATE PROCEDURE dbo.rpt_PurchaseSummary @FromDate DATETIME, @ToDate DATETIME, @LocationID INT = NULL, @SupplierID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.RefNo AS [GRN No], p.InvoiceNo AS [Supplier Inv], CONVERT(VARCHAR(10), p.TransactionDate, 120) AS [Date],
           s.SupName AS [Supplier], ISNULL(p.TotalAmount,0) AS [Total], ISNULL(p.Discount,0) AS [Discount],
           ISNULL(p.NetTotal,0) AS [Net], ISNULL(p.PaidAmount,0) AS [Paid], ISNULL(p.BalanceAmount,0) AS [Balance]
    FROM dbo.PurchaseInvoices p
    LEFT JOIN dbo.Suppliers s ON s.SupplierID = p.SupplierID
    WHERE ISNULL(p.IsDeleted,0) = 0
      AND p.TransactionDate >= @FromDate AND p.TransactionDate < DATEADD(DAY,1,@ToDate)
      AND (@LocationID IS NULL OR ISNULL(p.LocationID,1) = @LocationID)
      AND (@SupplierID IS NULL OR p.SupplierID = @SupplierID)
    ORDER BY p.TransactionDate;
END
GO

/* 14. Purchase detail */
IF OBJECT_ID('dbo.rpt_PurchaseDetail','P') IS NOT NULL DROP PROCEDURE dbo.rpt_PurchaseDetail;
GO
CREATE PROCEDURE dbo.rpt_PurchaseDetail @FromDate DATETIME, @ToDate DATETIME, @LocationID INT = NULL, @SupplierID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.RefNo AS [GRN No], CONVERT(VARCHAR(10), p.TransactionDate, 120) AS [Date], s.SupName AS [Supplier],
           i.ItemCode AS [Code], ISNULL(i.ItemName, i.ItemDescription) AS [Item], d.BatchNo AS [Batch],
           CONVERT(VARCHAR(10), d.ExpDate, 120) AS [Expiry],
           d.Quantity AS [Qty], d.Price AS [Cost], d.SellingPrice AS [Selling], d.Amount AS [Amount]
    FROM dbo.PurchaseInvoiceDetails d
    JOIN dbo.PurchaseInvoices p ON p.PurchaseInvoiceID = d.PurchaseInvoiceID
    JOIN dbo.Item i ON i.ItemID = d.ItemID
    LEFT JOIN dbo.Suppliers s ON s.SupplierID = p.SupplierID
    WHERE ISNULL(d.IsDeleted,0) = 0 AND ISNULL(p.IsDeleted,0) = 0
      AND p.TransactionDate >= @FromDate AND p.TransactionDate < DATEADD(DAY,1,@ToDate)
      AND (@LocationID IS NULL OR ISNULL(p.LocationID,1) = @LocationID)
      AND (@SupplierID IS NULL OR p.SupplierID = @SupplierID)
    ORDER BY p.TransactionDate, p.RefNo;
END
GO

/* 15. Purchase returns */
IF OBJECT_ID('dbo.rpt_PurchaseReturns','P') IS NOT NULL DROP PROCEDURE dbo.rpt_PurchaseReturns;
GO
CREATE PROCEDURE dbo.rpt_PurchaseReturns @FromDate DATETIME, @ToDate DATETIME, @LocationID INT = NULL, @SupplierID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT r.ReturnNo AS [Return No], CONVERT(VARCHAR(10), r.ReturnDate, 120) AS [Date], s.SupName AS [Supplier],
           i.ItemCode AS [Code], ISNULL(i.ItemName, i.ItemDescription) AS [Item],
           d.Quantity AS [Qty], d.Rate AS [Rate], d.Amount AS [Amount], r.Remarks
    FROM dbo.PurchaseReturns r
    JOIN dbo.PurchaseReturnDetails d ON d.PurchaseReturnID = r.PurchaseReturnID
    JOIN dbo.Item i ON i.ItemID = d.ItemID
    LEFT JOIN dbo.Suppliers s ON s.SupplierID = r.SupplierID
    WHERE r.IsDeleted = 0
      AND r.ReturnDate >= @FromDate AND r.ReturnDate < DATEADD(DAY,1,@ToDate)
      AND (@LocationID IS NULL OR r.LocationID = @LocationID)
      AND (@SupplierID IS NULL OR r.SupplierID = @SupplierID)
    ORDER BY r.ReturnDate;
END
GO

/* 16. Supplier outstanding */
IF OBJECT_ID('dbo.rpt_SupplierOutstanding','P') IS NOT NULL DROP PROCEDURE dbo.rpt_SupplierOutstanding;
GO
CREATE PROCEDURE dbo.rpt_SupplierOutstanding @ToDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.SupCode AS [Code], s.SupName AS [Supplier],
           ISNULL(g.Purchases,0) AS [Purchases], ISNULL(g.Paid,0) AS [Paid],
           ISNULL(r.Returns,0) AS [Returns],
           ISNULL(g.Purchases,0) - ISNULL(g.Paid,0) - ISNULL(r.Returns,0) AS [Outstanding]
    FROM dbo.Suppliers s
    OUTER APPLY (SELECT SUM(ISNULL(NetTotal,0)) Purchases, SUM(ISNULL(PaidAmount,0)) Paid
                 FROM dbo.PurchaseInvoices p WHERE p.SupplierID = s.SupplierID AND ISNULL(p.IsDeleted,0)=0
                   AND p.TransactionDate < DATEADD(DAY,1,@ToDate)) g
    OUTER APPLY (SELECT SUM(TotalAmount) Returns FROM dbo.PurchaseReturns r
                 WHERE r.SupplierID = s.SupplierID AND r.IsDeleted = 0 AND r.ReturnDate < DATEADD(DAY,1,@ToDate)) r
    WHERE ISNULL(s.IsDeleted,0) = 0
      AND (ISNULL(g.Purchases,0) - ISNULL(g.Paid,0) - ISNULL(r.Returns,0)) <> 0
    ORDER BY [Outstanding] DESC;
END
GO

/* ===================== CUSTOMERS / CASH ===================== */

/* 17. Customer outstanding (credit) */
IF OBJECT_ID('dbo.rpt_CustomerOutstanding','P') IS NOT NULL DROP PROCEDURE dbo.rpt_CustomerOutstanding;
GO
CREATE PROCEDURE dbo.rpt_CustomerOutstanding @ToDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    SELECT c.Code AS [Code], c.Name AS [Customer], c.Mobile,
           ISNULL(c.OpeningBalance,0) AS [Opening],
           ISNULL(cr.CreditSales,0) AS [Credit Sales],
           ISNULL(rc.Received,0) AS [Received],
           ISNULL(c.OpeningBalance,0) + ISNULL(cr.CreditSales,0) - ISNULL(rc.Received,0) AS [Outstanding],
           ISNULL(c.CreditLimit,0) AS [Credit Limit]
    FROM dbo.Customers c
    OUTER APPLY (SELECT SUM(p.Amount) CreditSales FROM dbo.vw_SalesPayments p
                 JOIN dbo.vw_ValidSales s ON s.SalesInvoiceID = p.SalesInvoiceID
                 WHERE s.CustomerID = c.CustomerID AND p.Kind = 3 AND s.TransactionDate < DATEADD(DAY,1,@ToDate)) cr
    OUTER APPLY (SELECT SUM(ISNULL(r.Amount,0) + ISNULL(r.AmountInCheque,0)) Received FROM dbo.ReceivedAmount r
                 WHERE r.CustomerID = c.CustomerID AND ISNULL(r.IsDeleted,0) = 0 AND r.DateReceived < DATEADD(DAY,1,@ToDate)) rc
    WHERE ISNULL(c.IsDeleted,0) = 0
      AND (ISNULL(c.OpeningBalance,0) + ISNULL(cr.CreditSales,0) - ISNULL(rc.Received,0)) <> 0
    ORDER BY [Outstanding] DESC;
END
GO

/* 18. Cashier sessions */
IF OBJECT_ID('dbo.rpt_SessionSummary','P') IS NOT NULL DROP PROCEDURE dbo.rpt_SessionSummary;
GO
CREATE PROCEDURE dbo.rpt_SessionSummary @FromDate DATETIME, @ToDate DATETIME, @LocationID INT = NULL, @UserID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ss.SalesSessionID AS [Session], u.FullName AS [Cashier],
           CONVERT(VARCHAR(16), ss.StartTime, 120) AS [Start], CONVERT(VARCHAR(16), ss.EndTime, 120) AS [End],
           ISNULL(ss.NoOfCustomers,0) AS [Bills],
           ISNULL(ss.OpeningBalance,0) AS [Opening],
           ISNULL(ss.TotalCashAmount,0) AS [Cash Sales],
           ISNULL(ss.TotalCardAmount,0) AS [Card],
           ISNULL(ss.TotalCreditAmount,0) AS [Credit],
           ISNULL(ss.TotalReceivedPayments,0) AS [Received],
           ISNULL(ss.TotalPaidOuts,0) AS [Paid Outs],
           ISNULL(ss.TotalReturnCash,0) AS [Cash Refunds],
           ISNULL(ss.TotalCashInSystem,0) AS [Expected Cash],
           ISNULL(ss.TotalCashInHand,0) AS [Counted Cash],
           ISNULL(ss.Excess,0) AS [Excess], ISNULL(ss.Shortage,0) AS [Shortage],
           CASE WHEN ss.IsClosed = 1 THEN 'Closed' ELSE 'OPEN' END AS [Status]
    FROM dbo.SalesSessions ss
    LEFT JOIN dbo.UserProfile u ON u.UserProfileId = ss.UserProfileID
    WHERE ISNULL(ss.IsDeleted,0) = 0
      AND ss.StartTime >= @FromDate AND ss.StartTime < DATEADD(DAY,1,@ToDate)
      AND (@LocationID IS NULL OR ISNULL(ss.LocationID,1) = @LocationID)
      AND (@UserID IS NULL OR ss.UserProfileID = @UserID)
    ORDER BY ss.StartTime;
END
GO

/* 19. Paid outs & received payments */
IF OBJECT_ID('dbo.rpt_CashInOut','P') IS NOT NULL DROP PROCEDURE dbo.rpt_CashInOut;
GO
CREATE PROCEDURE dbo.rpt_CashInOut @FromDate DATETIME, @ToDate DATETIME, @LocationID INT = NULL, @EmployeeID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM (
        SELECT 'PAID OUT' AS [Type], p.PayOutNo AS [Ref No], CONVERT(VARCHAR(16), p.DateGiven, 120) AS [Date Time],
               p.PayOutCategory AS [Category], e.EmployeeName AS [Employee], p.Description AS [Description], u.FullName AS [User],
               CAST(0 AS DECIMAL(18,2)) AS [In], ISNULL(p.Amount,0) AS [Out], p.DateGiven AS SortDate
        FROM dbo.PayOut p
        LEFT JOIN dbo.UserProfile u ON u.UserProfileId = p.CreatedBy
        LEFT JOIN dbo.Employees e ON e.EmployeeID = p.EmployeeID
        WHERE ISNULL(p.IsDeleted,0)=0 AND p.DateGiven >= @FromDate AND p.DateGiven < DATEADD(DAY,1,@ToDate)
          AND (@LocationID IS NULL OR ISNULL(p.LocationID,1) = @LocationID)
          AND (@EmployeeID IS NULL OR p.EmployeeID = @EmployeeID)
        UNION ALL
        SELECT 'RECEIVED', r.ReceiptNo, CONVERT(VARCHAR(16), r.DateReceived, 120),
               ISNULL(r.RAType,''), NULL, ISNULL(c.Name + ' - ','') + ISNULL(r.Description,''), u.FullName,
               ISNULL(r.Amount,0) + ISNULL(r.AmountInCheque,0), 0, r.DateReceived
        FROM dbo.ReceivedAmount r
        LEFT JOIN dbo.UserProfile u ON u.UserProfileId = r.CreatedBy
        LEFT JOIN dbo.Customers c ON c.CustomerID = r.CustomerID
        WHERE @EmployeeID IS NULL                                   -- receipts have no employee
          AND ISNULL(r.IsDeleted,0)=0 AND r.DateReceived >= @FromDate AND r.DateReceived < DATEADD(DAY,1,@ToDate)
          AND (@LocationID IS NULL OR ISNULL(r.LocationID,1) = @LocationID)
    ) x ORDER BY SortDate;
END
GO

/* ===================== STOCK ===================== */

/* 20. Current stock */
IF OBJECT_ID('dbo.rpt_CurrentStock','P') IS NOT NULL DROP PROCEDURE dbo.rpt_CurrentStock;
GO
CREATE PROCEDURE dbo.rpt_CurrentStock @LocationID INT = NULL, @CategoryID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT l.LocationCode AS [Location], i.ItemCode AS [Code], ISNULL(i.ItemName, i.ItemDescription) AS [Item],
           cat.ItemCategoryDescription AS [Category],
           SUM(ISNULL(s.CurrentQuantity,0)) AS [Qty],
           SUM(ISNULL(s.CurrentQuantity,0) * ISNULL(s.CostPrice, ISNULL(i.CostPrice,0))) AS [Cost Value],
           SUM(ISNULL(s.CurrentQuantity,0) * ISNULL(s.SellingPrice, ISNULL(i.SellingPrice,0))) AS [Sales Value]
    FROM dbo.ItemStocks s
    JOIN dbo.Item i ON i.ItemID = s.ItemID
    LEFT JOIN dbo.ItemCategories cat ON cat.ItemCategoryId = i.ItemCategoryID
    LEFT JOIN dbo.Locations l ON l.LocationID = s.LocationID
    WHERE ISNULL(s.IsDeleted,0) = 0 AND ISNULL(i.IsDeleted,0) = 0
      AND (@LocationID IS NULL OR s.LocationID = @LocationID)
      AND (@CategoryID IS NULL OR i.ItemCategoryID = @CategoryID)
    GROUP BY l.LocationCode, i.ItemCode, ISNULL(i.ItemName, i.ItemDescription), cat.ItemCategoryDescription
    HAVING SUM(ISNULL(s.CurrentQuantity,0)) <> 0
    ORDER BY l.LocationCode, [Item];
END
GO

/* 21. Re-order level */
IF OBJECT_ID('dbo.rpt_ReorderLevel','P') IS NOT NULL DROP PROCEDURE dbo.rpt_ReorderLevel;
GO
CREATE PROCEDURE dbo.rpt_ReorderLevel @LocationID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT i.ItemCode AS [Code], ISNULL(i.ItemName, i.ItemDescription) AS [Item],
           cat.ItemCategoryDescription AS [Category],
           ISNULL(st.Qty,0) AS [In Stock], ISNULL(i.ReorderLevel,0) AS [Re-order Level],
           ISNULL(i.ReorderLevel,0) - ISNULL(st.Qty,0) AS [Shortfall]
    FROM dbo.Item i
    LEFT JOIN dbo.ItemCategories cat ON cat.ItemCategoryId = i.ItemCategoryID
    OUTER APPLY (SELECT SUM(ISNULL(CurrentQuantity,0)) Qty FROM dbo.ItemStocks s
                 WHERE s.ItemID = i.ItemID AND ISNULL(s.IsDeleted,0)=0 AND (@LocationID IS NULL OR s.LocationID = @LocationID)) st
    WHERE ISNULL(i.IsDeleted,0) = 0 AND ISNULL(i.IsService,0) = 0 AND ISNULL(i.ReorderLevel,0) > 0
      AND ISNULL(st.Qty,0) <= i.ReorderLevel
    ORDER BY [Shortfall] DESC;
END
GO

/* 22. Stock movement (stock card) */
IF OBJECT_ID('dbo.rpt_StockMovement','P') IS NOT NULL DROP PROCEDURE dbo.rpt_StockMovement;
GO
CREATE PROCEDURE dbo.rpt_StockMovement @FromDate DATETIME, @ToDate DATETIME, @LocationID INT = NULL, @ItemID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CONVERT(VARCHAR(16), m.MovementDate, 120) AS [Date Time], l.LocationCode AS [Location],
           i.ItemCode AS [Code], ISNULL(i.ItemName, i.ItemDescription) AS [Item],
           m.MovementType AS [Type], m.RefNo AS [Ref No], m.QtyIn AS [In], m.QtyOut AS [Out],
           SUM(m.QtyIn - m.QtyOut) OVER (PARTITION BY m.ItemID, m.LocationID ORDER BY m.MovementDate, m.StockMovementID
                                          ROWS UNBOUNDED PRECEDING) AS [Running (in period)],
           u.FullName AS [User]
    FROM dbo.StockMovements m
    JOIN dbo.Item i ON i.ItemID = m.ItemID
    LEFT JOIN dbo.Locations l ON l.LocationID = m.LocationID
    LEFT JOIN dbo.UserProfile u ON u.UserProfileId = m.CreatedBy
    WHERE m.MovementDate >= @FromDate AND m.MovementDate < DATEADD(DAY,1,@ToDate)
      AND (@LocationID IS NULL OR m.LocationID = @LocationID)
      AND (@ItemID IS NULL OR m.ItemID = @ItemID)
    ORDER BY i.ItemCode, m.MovementDate, m.StockMovementID;
END
GO

/* 23. Expiry */
IF OBJECT_ID('dbo.rpt_ExpiryReport','P') IS NOT NULL DROP PROCEDURE dbo.rpt_ExpiryReport;
GO
CREATE PROCEDURE dbo.rpt_ExpiryReport @Days INT = 30, @LocationID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT l.LocationCode AS [Location], i.ItemCode AS [Code], ISNULL(i.ItemName, i.ItemDescription) AS [Item],
           s.BatchNo AS [Batch], CONVERT(VARCHAR(10), s.ExpDate, 120) AS [Expiry],
           DATEDIFF(DAY, CAST(GETDATE() AS DATE), s.ExpDate) AS [Days Left],
           s.CurrentQuantity AS [Qty], s.CurrentQuantity * ISNULL(s.CostPrice,0) AS [Cost Value]
    FROM dbo.ItemStocks s
    JOIN dbo.Item i ON i.ItemID = s.ItemID
    LEFT JOIN dbo.Locations l ON l.LocationID = s.LocationID
    WHERE ISNULL(s.IsDeleted,0) = 0 AND s.CurrentQuantity > 0 AND s.ExpDate IS NOT NULL
      AND s.ExpDate < DATEADD(DAY, @Days + 1, CAST(GETDATE() AS DATE))
      AND (@LocationID IS NULL OR s.LocationID = @LocationID)
    ORDER BY s.ExpDate;
END
GO

/* 24. Fast moving items */
IF OBJECT_ID('dbo.rpt_FastMovingItems','P') IS NOT NULL DROP PROCEDURE dbo.rpt_FastMovingItems;
GO
CREATE PROCEDURE dbo.rpt_FastMovingItems @FromDate DATETIME, @ToDate DATETIME, @LocationID INT = NULL, @TopN INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@TopN) i.ItemCode AS [Code], ISNULL(i.ItemName, i.ItemDescription) AS [Item],
           COUNT(DISTINCT d.SalesInvoiceID) AS [Bills], SUM(d.Quantity) AS [Qty Sold], SUM(ISNULL(d.NetAmount,0)) AS [Net Sales],
           (SELECT SUM(ISNULL(CurrentQuantity,0)) FROM dbo.ItemStocks st WHERE st.ItemID = i.ItemID AND ISNULL(st.IsDeleted,0)=0
               AND (@LocationID IS NULL OR st.LocationID = @LocationID)) AS [In Stock]
    FROM dbo.SalesInvoiceDetails d
    JOIN dbo.vw_ValidSales s ON s.SalesInvoiceID = d.SalesInvoiceID
    JOIN dbo.Item i ON i.ItemID = d.ItemID
    WHERE ISNULL(d.IsDeleted,0) = 0
      AND s.TransactionDate >= @FromDate AND s.TransactionDate < DATEADD(DAY,1,@ToDate)
      AND (@LocationID IS NULL OR s.LocID = @LocationID)
    GROUP BY i.ItemID, i.ItemCode, ISNULL(i.ItemName, i.ItemDescription)
    ORDER BY [Qty Sold] DESC;
END
GO

/* 25. Non moving (slow) items */
IF OBJECT_ID('dbo.rpt_NonMovingItems','P') IS NOT NULL DROP PROCEDURE dbo.rpt_NonMovingItems;
GO
CREATE PROCEDURE dbo.rpt_NonMovingItems @FromDate DATETIME, @ToDate DATETIME, @LocationID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT i.ItemCode AS [Code], ISNULL(i.ItemName, i.ItemDescription) AS [Item],
           st.Qty AS [In Stock], st.CostValue AS [Cost Value],
           (SELECT CONVERT(VARCHAR(10), MAX(s.TransactionDate), 120) FROM dbo.SalesInvoiceDetails d
              JOIN dbo.vw_ValidSales s ON s.SalesInvoiceID = d.SalesInvoiceID WHERE d.ItemID = i.ItemID) AS [Last Sold]
    FROM dbo.Item i
    CROSS APPLY (SELECT SUM(ISNULL(CurrentQuantity,0)) Qty, SUM(ISNULL(CurrentQuantity,0) * ISNULL(CostPrice,0)) CostValue
                 FROM dbo.ItemStocks s WHERE s.ItemID = i.ItemID AND ISNULL(s.IsDeleted,0)=0
                   AND (@LocationID IS NULL OR s.LocationID = @LocationID)) st
    WHERE ISNULL(i.IsDeleted,0) = 0 AND st.Qty > 0
      AND NOT EXISTS (SELECT 1 FROM dbo.SalesInvoiceDetails d JOIN dbo.vw_ValidSales s ON s.SalesInvoiceID = d.SalesInvoiceID
                      WHERE d.ItemID = i.ItemID AND s.TransactionDate >= @FromDate AND s.TransactionDate < DATEADD(DAY,1,@ToDate)
                        AND (@LocationID IS NULL OR s.LocID = @LocationID))
    ORDER BY st.CostValue DESC;
END
GO

/* 26. Stock transfers */
IF OBJECT_ID('dbo.rpt_StockTransfers','P') IS NOT NULL DROP PROCEDURE dbo.rpt_StockTransfers;
GO
CREATE PROCEDURE dbo.rpt_StockTransfers @FromDate DATETIME, @ToDate DATETIME, @LocationID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT t.TransferNo AS [Transfer No], CONVERT(VARCHAR(10), t.TransactionDate, 120) AS [Date],
           lf.LocationCode AS [From], lt.LocationCode AS [To],
           i.ItemCode AS [Code], ISNULL(i.ItemName, i.ItemDescription) AS [Item],
           d.Quantity AS [Qty], d.Rate AS [Cost], d.Amount AS [Value], t.Remarks
    FROM dbo.Transfers t
    JOIN dbo.TransferDetails d ON d.TransferID = t.TransferID
    JOIN dbo.Item i ON i.ItemID = d.ItemID
    LEFT JOIN dbo.Locations lf ON lf.LocationID = t.FromLocationID
    LEFT JOIN dbo.Locations lt ON lt.LocationID = t.ToLocationID
    WHERE ISNULL(t.IsDeleted,0) = 0 AND ISNULL(d.IsDeleted,0) = 0
      AND t.TransactionDate >= @FromDate AND t.TransactionDate < DATEADD(DAY,1,@ToDate)
      AND (@LocationID IS NULL OR t.FromLocationID = @LocationID OR t.ToLocationID = @LocationID)
    ORDER BY t.TransactionDate;
END
GO

/* 27. Stock adjustments */
IF OBJECT_ID('dbo.rpt_StockAdjustments','P') IS NOT NULL DROP PROCEDURE dbo.rpt_StockAdjustments;
GO
CREATE PROCEDURE dbo.rpt_StockAdjustments @FromDate DATETIME, @ToDate DATETIME, @LocationID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT a.AdjustmentNo AS [Adj No], CONVERT(VARCHAR(10), a.AdjustmentDate, 120) AS [Date], l.LocationCode AS [Location],
           i.ItemCode AS [Code], ISNULL(i.ItemName, i.ItemDescription) AS [Item],
           d.Quantity AS [Qty (+/-)], d.CostPrice AS [Cost], d.Quantity * ISNULL(d.CostPrice,0) AS [Value],
           a.Reason, u.FullName AS [User]
    FROM dbo.StockAdjustments a
    JOIN dbo.StockAdjustmentDetails d ON d.StockAdjustmentID = a.StockAdjustmentID
    JOIN dbo.Item i ON i.ItemID = d.ItemID
    LEFT JOIN dbo.Locations l ON l.LocationID = a.LocationID
    LEFT JOIN dbo.UserProfile u ON u.UserProfileId = a.CreatedBy
    WHERE a.IsDeleted = 0
      AND a.AdjustmentDate >= @FromDate AND a.AdjustmentDate < DATEADD(DAY,1,@ToDate)
      AND (@LocationID IS NULL OR a.LocationID = @LocationID)
    ORDER BY a.AdjustmentDate;
END
GO

/* 28. Price change log */
IF OBJECT_ID('dbo.rpt_PriceChangeLog','P') IS NOT NULL DROP PROCEDURE dbo.rpt_PriceChangeLog;
GO
CREATE PROCEDURE dbo.rpt_PriceChangeLog @FromDate DATETIME, @ToDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CONVERT(VARCHAR(16), p.DateCreated, 120) AS [Date Time], i.ItemCode AS [Code],
           ISNULL(i.ItemName, i.ItemDescription) AS [Item], p.ExistingPrice AS [Old Price], p.NewPrice AS [New Price],
           p.NewPrice - p.ExistingPrice AS [Change], u.FullName AS [Changed By], p.RefNo AS [Remarks]
    FROM dbo.ItemPriceChangeLogs p
    JOIN dbo.Item i ON i.ItemID = p.ItemID
    LEFT JOIN dbo.UserProfile u ON u.UserProfileId = p.CreatedBy
    WHERE ISNULL(p.IsDeleted,0) = 0 AND p.DateCreated >= @FromDate AND p.DateCreated < DATEADD(DAY,1,@ToDate)
    ORDER BY p.DateCreated;
END
GO

/* 29. Audit log (overrides, cancellations, drawer opens) */
IF OBJECT_ID('dbo.rpt_AuditLog','P') IS NOT NULL DROP PROCEDURE dbo.rpt_AuditLog;
GO
CREATE PROCEDURE dbo.rpt_AuditLog @FromDate DATETIME, @ToDate DATETIME, @UserID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CONVERT(VARCHAR(19), a.LogDate, 120) AS [Date Time], u.FullName AS [User], ap.FullName AS [Approved By],
           pc.Description AS [Terminal], a.Action, a.RefNo AS [Ref No], a.Details
    FROM dbo.AuditLogs a
    LEFT JOIN dbo.UserProfile u ON u.UserProfileId = a.UserID
    LEFT JOIN dbo.UserProfile ap ON ap.UserProfileId = a.ApprovedBy
    LEFT JOIN dbo.PcRegistration pc ON pc.PcRegistrationID = a.PCCreated
    WHERE a.LogDate >= @FromDate AND a.LogDate < DATEADD(DAY,1,@ToDate)
      AND (@UserID IS NULL OR a.UserID = @UserID)
    ORDER BY a.LogDate;
END
GO
PRINT 'Report procedures created.';
GO

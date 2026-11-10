/* =====================================================================
   POS - line note (e.g. flex size 2x4) printed after the item name
   Run once. Safe to run again.
   ===================================================================== */
IF COL_LENGTH('dbo.SalesInvoiceDetails', 'LineNote') IS NULL
    ALTER TABLE dbo.SalesInvoiceDetails ADD LineNote NVARCHAR(100) NULL;
GO
IF COL_LENGTH('dbo.HeldBillItems', 'LineNote') IS NULL
    ALTER TABLE dbo.HeldBillItems ADD LineNote NVARCHAR(100) NULL;
GO

/* =====================================================================
   POS SYSTEM v2 (WPF) - SCHEMA UPGRADE
   Target : SQL Server 2012+ (no CREATE OR ALTER / DROP IF EXISTS used)
   Safe   : Re-runnable. Upgrades the EXISTING POS database in place,
            keeps all current data (items, stock, invoices, users...).
   Run    : 01_Upgrade_Schema.sql  ->  02_Report_Procedures.sql
   ===================================================================== */
SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------------
   1. NEW MASTER TABLES
   --------------------------------------------------------------------- */
IF OBJECT_ID('dbo.Locations','U') IS NULL
CREATE TABLE dbo.Locations(
    LocationID    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Locations PRIMARY KEY,
    LocationCode  VARCHAR(10)   NOT NULL,
    LocationName  NVARCHAR(100) NOT NULL,
    Address       NVARCHAR(300) NULL,
    Phone         VARCHAR(50)   NULL,
    IsActive      BIT NOT NULL CONSTRAINT DF_Locations_IsActive DEFAULT(1),
    IsDeleted     BIT NOT NULL CONSTRAINT DF_Locations_IsDeleted DEFAULT(0),
    DateCreated   DATETIME NOT NULL CONSTRAINT DF_Locations_DateCreated DEFAULT(GETDATE()),
    CreatedBy     INT NULL
);
GO
IF OBJECT_ID('dbo.Brands','U') IS NULL
CREATE TABLE dbo.Brands(
    BrandID     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Brands PRIMARY KEY,
    BrandCode   VARCHAR(20)   NULL,
    BrandName   NVARCHAR(100) NOT NULL,
    IsDeleted   BIT NOT NULL CONSTRAINT DF_Brands_IsDeleted DEFAULT(0),
    DateCreated DATETIME NOT NULL CONSTRAINT DF_Brands_DateCreated DEFAULT(GETDATE()),
    CreatedBy   INT NULL
);
GO
IF OBJECT_ID('dbo.Taxes','U') IS NULL
CREATE TABLE dbo.Taxes(
    TaxID       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Taxes PRIMARY KEY,
    TaxCode     VARCHAR(20)  NOT NULL,
    TaxName     NVARCHAR(100) NOT NULL,
    Rate        DECIMAL(9,4) NOT NULL CONSTRAINT DF_Taxes_Rate DEFAULT(0),
    IsActive    BIT NOT NULL CONSTRAINT DF_Taxes_IsActive DEFAULT(1),
    IsDeleted   BIT NOT NULL CONSTRAINT DF_Taxes_IsDeleted DEFAULT(0),
    DateCreated DATETIME NOT NULL CONSTRAINT DF_Taxes_DateCreated DEFAULT(GETDATE())
);
GO
IF OBJECT_ID('dbo.PaymentMethods','U') IS NULL
CREATE TABLE dbo.PaymentMethods(
    PaymentMethodID   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentMethods PRIMARY KEY,
    Code              VARCHAR(20)  NOT NULL,
    Name              NVARCHAR(50) NOT NULL,
    Kind              INT NOT NULL,           -- 1 Cash,2 Card,3 Credit,4 Cheque,5 CreditNote,6 Bank,7 Mobile/QR,8 Voucher,9 Other
    RequiresReference BIT NOT NULL CONSTRAINT DF_PM_ReqRef DEFAULT(0),
    OpensDrawer       BIT NOT NULL CONSTRAINT DF_PM_Drawer DEFAULT(0),
    ShortcutKey       VARCHAR(5) NULL,
    SortOrder         INT NOT NULL CONSTRAINT DF_PM_Sort DEFAULT(0),
    IsActive          BIT NOT NULL CONSTRAINT DF_PM_IsActive DEFAULT(1),
    IsDeleted         BIT NOT NULL CONSTRAINT DF_PM_IsDeleted DEFAULT(0)
);
GO
IF OBJECT_ID('dbo.AppSettings','U') IS NULL
CREATE TABLE dbo.AppSettings(
    SettingKey   VARCHAR(100)   NOT NULL CONSTRAINT PK_AppSettings PRIMARY KEY,
    SettingValue NVARCHAR(MAX) NULL,
    DateModified DATETIME NOT NULL CONSTRAINT DF_AppSettings_DateModified DEFAULT(GETDATE())
);
GO
/* logo is stored as base64 text, so the value column must be NVARCHAR(MAX) */
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AppSettings') AND name = 'SettingValue' AND max_length <> -1)
    ALTER TABLE dbo.AppSettings ALTER COLUMN SettingValue NVARCHAR(MAX) NULL;
GO
IF OBJECT_ID('dbo.DocumentSequences','U') IS NULL
CREATE TABLE dbo.DocumentSequences(
    DocType    VARCHAR(10) NOT NULL,
    LocationID INT  NOT NULL,
    SeqDate    DATE NOT NULL,
    LastNo     INT  NOT NULL,
    CONSTRAINT PK_DocumentSequences PRIMARY KEY (DocType, LocationID, SeqDate)
);
GO
IF OBJECT_ID('dbo.StockMovements','U') IS NULL
CREATE TABLE dbo.StockMovements(
    StockMovementID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockMovements PRIMARY KEY,
    MovementDate    DATETIME NOT NULL,
    ItemID          INT NOT NULL,
    LocationID      INT NOT NULL,
    ItemStockID     INT NULL,
    MovementType    VARCHAR(20) NOT NULL,
    RefID           INT NULL,
    RefLineID       INT NULL,
    RefNo           VARCHAR(50) NULL,
    QtyIn           DECIMAL(18,3) NOT NULL CONSTRAINT DF_SM_QtyIn DEFAULT(0),
    QtyOut          DECIMAL(18,3) NOT NULL CONSTRAINT DF_SM_QtyOut DEFAULT(0),
    CostPrice       DECIMAL(18,2) NULL,
    CreatedBy       INT NULL,
    PCCreated       INT NULL
);
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_StockMovements_Item')
    CREATE INDEX IX_StockMovements_Item ON dbo.StockMovements(ItemID, LocationID, MovementDate);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_StockMovements_Ref')
    CREATE INDEX IX_StockMovements_Ref ON dbo.StockMovements(MovementType, RefID);
GO

/* Held bills (bill hold / recall) */
IF OBJECT_ID('dbo.HeldBills','U') IS NULL
CREATE TABLE dbo.HeldBills(
    HeldBillID        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HeldBills PRIMARY KEY,
    HoldNo            VARCHAR(50) NOT NULL,
    LocationID        INT NOT NULL,
    PCCreated         INT NULL,
    CustomerID        INT NULL,
    CustomerName      VARCHAR(200) NULL,
    BillDiscountType  TINYINT NOT NULL CONSTRAINT DF_HB_BDT DEFAULT(0),
    BillDiscountValue DECIMAL(18,2) NOT NULL CONSTRAINT DF_HB_BDV DEFAULT(0),
    TotalAmount       DECIMAL(18,2) NOT NULL CONSTRAINT DF_HB_Total DEFAULT(0),
    Note              NVARCHAR(200) NULL,
    CreatedBy         INT NULL,
    DateCreated       DATETIME NOT NULL CONSTRAINT DF_HB_Date DEFAULT(GETDATE())
);
GO
IF OBJECT_ID('dbo.HeldBillItems','U') IS NULL
CREATE TABLE dbo.HeldBillItems(
    HeldBillItemID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HeldBillItems PRIMARY KEY,
    HeldBillID     INT NOT NULL CONSTRAINT FK_HeldBillItems_HeldBills REFERENCES dbo.HeldBills(HeldBillID) ON DELETE CASCADE,
    LineNumber     INT NOT NULL,
    ItemID         INT NOT NULL,
    ItemStockID    INT NULL,
    Quantity       DECIMAL(18,3) NOT NULL,
    Rate           DECIMAL(18,2) NOT NULL,
    DiscountType   TINYINT NOT NULL CONSTRAINT DF_HBI_DT DEFAULT(0),
    DiscountValue  DECIMAL(18,2) NOT NULL CONSTRAINT DF_HBI_DV DEFAULT(0)
);
GO

/* Credit notes (issued on sales return, redeemable as a payment) */
IF OBJECT_ID('dbo.CreditNotes','U') IS NULL
CREATE TABLE dbo.CreditNotes(
    CreditNoteID  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CreditNotes PRIMARY KEY,
    CreditNoteNo  VARCHAR(50) NOT NULL,
    CustomerID    INT NULL,
    LocationID    INT NOT NULL,
    Amount        DECIMAL(18,2) NOT NULL,
    Balance       DECIMAL(18,2) NOT NULL,
    IssueDate     DATETIME NOT NULL,
    ExpiryDate    DATETIME NULL,
    SalesReturnID INT NULL,
    IsCancelled   BIT NOT NULL CONSTRAINT DF_CN_Cancel DEFAULT(0),
    CreatedBy     INT NULL
);
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_CreditNotes_No')
    CREATE UNIQUE INDEX UX_CreditNotes_No ON dbo.CreditNotes(CreditNoteNo);
GO

/* Sales returns */
IF OBJECT_ID('dbo.SalesReturns','U') IS NULL
CREATE TABLE dbo.SalesReturns(
    SalesReturnID  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesReturns PRIMARY KEY,
    ReturnNo       VARCHAR(50) NOT NULL,
    ReturnDate     DATETIME NOT NULL,
    SalesInvoiceID INT NULL,
    InvoiceNo      VARCHAR(50) NULL,
    CustomerID     INT NULL,
    LocationID     INT NOT NULL,
    SalesSessionID INT NULL,
    TotalAmount    DECIMAL(18,2) NOT NULL,
    RefundMethod   TINYINT NOT NULL,   -- 1 Cash, 2 Credit note
    CreditNoteID   INT NULL,
    Reason         NVARCHAR(200) NULL,
    IsDeleted      BIT NOT NULL CONSTRAINT DF_SR_Del DEFAULT(0),
    CreatedBy      INT NULL,
    PCCreated      INT NULL,
    DateCreated    DATETIME NOT NULL CONSTRAINT DF_SR_Date DEFAULT(GETDATE())
);
GO
IF OBJECT_ID('dbo.SalesReturnDetails','U') IS NULL
CREATE TABLE dbo.SalesReturnDetails(
    SalesReturnDetailID  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesReturnDetails PRIMARY KEY,
    SalesReturnID        INT NOT NULL CONSTRAINT FK_SRD_SR REFERENCES dbo.SalesReturns(SalesReturnID),
    SalesInvoiceDetailID INT NULL,
    ItemID               INT NOT NULL,
    Quantity             DECIMAL(18,3) NOT NULL,
    Rate                 DECIMAL(18,2) NOT NULL,
    Amount               DECIMAL(18,2) NOT NULL,
    IsDamaged            BIT NOT NULL CONSTRAINT DF_SRD_Dmg DEFAULT(0),
    ItemStockID          INT NULL
);
GO

/* Purchase returns (return to supplier) */
IF OBJECT_ID('dbo.PurchaseReturns','U') IS NULL
CREATE TABLE dbo.PurchaseReturns(
    PurchaseReturnID  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseReturns PRIMARY KEY,
    ReturnNo          VARCHAR(50) NOT NULL,
    ReturnDate        DATETIME NOT NULL,
    SupplierID        INT NOT NULL,
    PurchaseInvoiceID INT NULL,
    LocationID        INT NOT NULL,
    TotalAmount       DECIMAL(18,2) NOT NULL,
    Remarks           NVARCHAR(200) NULL,
    IsDeleted         BIT NOT NULL CONSTRAINT DF_PR_Del DEFAULT(0),
    CreatedBy         INT NULL,
    DateCreated       DATETIME NOT NULL CONSTRAINT DF_PR_Date DEFAULT(GETDATE())
);
GO
IF OBJECT_ID('dbo.PurchaseReturnDetails','U') IS NULL
CREATE TABLE dbo.PurchaseReturnDetails(
    PurchaseReturnDetailID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseReturnDetails PRIMARY KEY,
    PurchaseReturnID       INT NOT NULL CONSTRAINT FK_PRD_PR REFERENCES dbo.PurchaseReturns(PurchaseReturnID),
    ItemID                 INT NOT NULL,
    ItemStockID            INT NULL,
    Quantity               DECIMAL(18,3) NOT NULL,
    Rate                   DECIMAL(18,2) NOT NULL,
    Amount                 DECIMAL(18,2) NOT NULL
);
GO

/* Stock adjustments (damage, stock-take, wastage, found) */
IF OBJECT_ID('dbo.StockAdjustments','U') IS NULL
CREATE TABLE dbo.StockAdjustments(
    StockAdjustmentID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockAdjustments PRIMARY KEY,
    AdjustmentNo      VARCHAR(50) NOT NULL,
    AdjustmentDate    DATETIME NOT NULL,
    LocationID        INT NOT NULL,
    Reason            NVARCHAR(200) NULL,
    IsDeleted         BIT NOT NULL CONSTRAINT DF_SA_Del DEFAULT(0),
    CreatedBy         INT NULL,
    DateCreated       DATETIME NOT NULL CONSTRAINT DF_SA_Date DEFAULT(GETDATE())
);
GO
IF OBJECT_ID('dbo.StockAdjustmentDetails','U') IS NULL
CREATE TABLE dbo.StockAdjustmentDetails(
    StockAdjustmentDetailID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockAdjustmentDetails PRIMARY KEY,
    StockAdjustmentID       INT NOT NULL CONSTRAINT FK_SAD_SA REFERENCES dbo.StockAdjustments(StockAdjustmentID),
    ItemID                  INT NOT NULL,
    ItemStockID             INT NULL,
    Quantity                DECIMAL(18,3) NOT NULL,   -- + increase / - decrease
    CostPrice               DECIMAL(18,2) NULL
);
GO

IF OBJECT_ID('dbo.AuditLogs','U') IS NULL
CREATE TABLE dbo.AuditLogs(
    AuditLogID  BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY,
    LogDate     DATETIME NOT NULL CONSTRAINT DF_AL_Date DEFAULT(GETDATE()),
    UserID      INT NULL,
    ApprovedBy  INT NULL,
    PCCreated   INT NULL,
    Action      VARCHAR(50) NOT NULL,
    RefNo       VARCHAR(50) NULL,
    Details     NVARCHAR(1000) NULL
);
GO

/* ---------------------------------------------------------------------
   2. NEW COLUMNS ON EXISTING TABLES
   --------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Item','BrandID') IS NULL            ALTER TABLE dbo.Item ADD BrandID INT NULL;
IF COL_LENGTH('dbo.Item','TaxID') IS NULL              ALTER TABLE dbo.Item ADD TaxID INT NULL;
IF COL_LENGTH('dbo.Item','IsWeighted') IS NULL         ALTER TABLE dbo.Item ADD IsWeighted BIT NOT NULL CONSTRAINT DF_Item_IsWeighted DEFAULT(0);
IF COL_LENGTH('dbo.Item','AllowDiscount') IS NULL      ALTER TABLE dbo.Item ADD AllowDiscount BIT NOT NULL CONSTRAINT DF_Item_AllowDiscount DEFAULT(1);
IF COL_LENGTH('dbo.Item','AllowPriceChange') IS NULL   ALTER TABLE dbo.Item ADD AllowPriceChange BIT NOT NULL CONSTRAINT DF_Item_AllowPriceChange DEFAULT(0);
IF COL_LENGTH('dbo.Item','ShowInTouch') IS NULL        ALTER TABLE dbo.Item ADD ShowInTouch BIT NOT NULL CONSTRAINT DF_Item_ShowInTouch DEFAULT(0);
IF COL_LENGTH('dbo.Item','TouchColor') IS NULL         ALTER TABLE dbo.Item ADD TouchColor VARCHAR(20) NULL;
GO
IF COL_LENGTH('dbo.ItemCategories','ShowInTouch') IS NULL ALTER TABLE dbo.ItemCategories ADD ShowInTouch BIT NOT NULL CONSTRAINT DF_Cat_ShowInTouch DEFAULT(1);
IF COL_LENGTH('dbo.ItemCategories','TouchColor') IS NULL  ALTER TABLE dbo.ItemCategories ADD TouchColor VARCHAR(20) NULL;
IF COL_LENGTH('dbo.ItemCategories','SortOrder') IS NULL   ALTER TABLE dbo.ItemCategories ADD SortOrder INT NOT NULL CONSTRAINT DF_Cat_SortOrder DEFAULT(0);
GO
IF COL_LENGTH('dbo.ItemStocks','LocationID') IS NULL        ALTER TABLE dbo.ItemStocks ADD LocationID INT NOT NULL CONSTRAINT DF_ItemStocks_LocationID DEFAULT(1);
IF COL_LENGTH('dbo.ItemStocks','PurchaseInvoiceID') IS NULL ALTER TABLE dbo.ItemStocks ADD PurchaseInvoiceID INT NULL;
GO
IF COL_LENGTH('dbo.Customers','CreditLimit') IS NULL    ALTER TABLE dbo.Customers ADD CreditLimit DECIMAL(18,2) NOT NULL CONSTRAINT DF_Cust_CreditLimit DEFAULT(0);
IF COL_LENGTH('dbo.Customers','OpeningBalance') IS NULL ALTER TABLE dbo.Customers ADD OpeningBalance DECIMAL(18,2) NOT NULL CONSTRAINT DF_Cust_OpeningBalance DEFAULT(0);
IF COL_LENGTH('dbo.Suppliers','CreditDays') IS NULL     ALTER TABLE dbo.Suppliers ADD CreditDays INT NOT NULL CONSTRAINT DF_Sup_CreditDays DEFAULT(0);
GO
IF COL_LENGTH('dbo.UserProfile','DefaultLocationID') IS NULL ALTER TABLE dbo.UserProfile ADD DefaultLocationID INT NULL;
IF COL_LENGTH('dbo.UserGroups','MaxDiscountPercent') IS NULL ALTER TABLE dbo.UserGroups ADD MaxDiscountPercent DECIMAL(5,2) NOT NULL CONSTRAINT DF_UG_MaxDisc DEFAULT(0);
IF COL_LENGTH('dbo.FormRules','Description') IS NULL         ALTER TABLE dbo.FormRules ADD Description VARCHAR(200) NULL;
GO
IF COL_LENGTH('dbo.PcRegistration','LocationID') IS NULL       ALTER TABLE dbo.PcRegistration ADD LocationID INT NOT NULL CONSTRAINT DF_PC_LocationID DEFAULT(1);
IF COL_LENGTH('dbo.PcRegistration','ReceiptPrinter') IS NULL   ALTER TABLE dbo.PcRegistration ADD ReceiptPrinter NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.PcRegistration','ReceiptFormat') IS NULL    ALTER TABLE dbo.PcRegistration ADD ReceiptFormat INT NOT NULL CONSTRAINT DF_PC_ReceiptFormat DEFAULT(1);
IF COL_LENGTH('dbo.PcRegistration','PaperWidthMm') IS NULL     ALTER TABLE dbo.PcRegistration ADD PaperWidthMm DECIMAL(5,1) NOT NULL CONSTRAINT DF_PC_PaperWidth DEFAULT(65);
IF COL_LENGTH('dbo.PcRegistration','CharsPerLine') IS NULL     ALTER TABLE dbo.PcRegistration ADD CharsPerLine INT NOT NULL CONSTRAINT DF_PC_Chars DEFAULT(40);
IF COL_LENGTH('dbo.PcRegistration','PrintEngine') IS NULL      ALTER TABLE dbo.PcRegistration ADD PrintEngine INT NOT NULL CONSTRAINT DF_PC_Engine DEFAULT(1);
IF COL_LENGTH('dbo.PcRegistration','A5Printer') IS NULL        ALTER TABLE dbo.PcRegistration ADD A5Printer NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.PcRegistration','A5Landscape') IS NULL      ALTER TABLE dbo.PcRegistration ADD A5Landscape BIT NOT NULL CONSTRAINT DF_PC_A5Land DEFAULT(1);
IF COL_LENGTH('dbo.PcRegistration','AutoPrint') IS NULL        ALTER TABLE dbo.PcRegistration ADD AutoPrint BIT NOT NULL CONSTRAINT DF_PC_AutoPrint DEFAULT(1);
IF COL_LENGTH('dbo.PcRegistration','PrintCopies') IS NULL      ALTER TABLE dbo.PcRegistration ADD PrintCopies INT NOT NULL CONSTRAINT DF_PC_Copies DEFAULT(1);
IF COL_LENGTH('dbo.PcRegistration','OpenCashDrawer') IS NULL   ALTER TABLE dbo.PcRegistration ADD OpenCashDrawer BIT NOT NULL CONSTRAINT DF_PC_Drawer DEFAULT(1);
IF COL_LENGTH('dbo.PcRegistration','DefaultTouchMode') IS NULL ALTER TABLE dbo.PcRegistration ADD DefaultTouchMode BIT NOT NULL CONSTRAINT DF_PC_Touch DEFAULT(0);
GO
IF COL_LENGTH('dbo.SalesInvoices','LocationID') IS NULL         ALTER TABLE dbo.SalesInvoices ADD LocationID INT NULL;
IF COL_LENGTH('dbo.SalesInvoices','BillDiscountType') IS NULL   ALTER TABLE dbo.SalesInvoices ADD BillDiscountType TINYINT NULL;
IF COL_LENGTH('dbo.SalesInvoices','BillDiscountValue') IS NULL  ALTER TABLE dbo.SalesInvoices ADD BillDiscountValue DECIMAL(18,2) NULL;
IF COL_LENGTH('dbo.SalesInvoices','BillDiscountAmount') IS NULL ALTER TABLE dbo.SalesInvoices ADD BillDiscountAmount DECIMAL(18,2) NULL;
IF COL_LENGTH('dbo.SalesInvoices','LineDiscountAmount') IS NULL ALTER TABLE dbo.SalesInvoices ADD LineDiscountAmount DECIMAL(18,2) NULL;
IF COL_LENGTH('dbo.SalesInvoices','TotalTax') IS NULL           ALTER TABLE dbo.SalesInvoices ADD TotalTax DECIMAL(18,2) NULL;
IF COL_LENGTH('dbo.SalesInvoices','PricesIncludeTax') IS NULL   ALTER TABLE dbo.SalesInvoices ADD PricesIncludeTax BIT NULL;
IF COL_LENGTH('dbo.SalesInvoices','CancelledBy') IS NULL        ALTER TABLE dbo.SalesInvoices ADD CancelledBy INT NULL;
IF COL_LENGTH('dbo.SalesInvoices','CancelledDate') IS NULL      ALTER TABLE dbo.SalesInvoices ADD CancelledDate DATETIME NULL;
IF COL_LENGTH('dbo.SalesInvoices','CancelReason') IS NULL       ALTER TABLE dbo.SalesInvoices ADD CancelReason NVARCHAR(200) NULL;
GO
IF COL_LENGTH('dbo.SalesInvoiceDetails','LineNumber') IS NULL        ALTER TABLE dbo.SalesInvoiceDetails ADD LineNumber INT NULL;
IF COL_LENGTH('dbo.SalesInvoiceDetails','DiscountType') IS NULL      ALTER TABLE dbo.SalesInvoiceDetails ADD DiscountType TINYINT NULL;
IF COL_LENGTH('dbo.SalesInvoiceDetails','BillDiscountShare') IS NULL ALTER TABLE dbo.SalesInvoiceDetails ADD BillDiscountShare DECIMAL(18,2) NULL;
IF COL_LENGTH('dbo.SalesInvoiceDetails','TaxID') IS NULL             ALTER TABLE dbo.SalesInvoiceDetails ADD TaxID INT NULL;
IF COL_LENGTH('dbo.SalesInvoiceDetails','TaxRate') IS NULL           ALTER TABLE dbo.SalesInvoiceDetails ADD TaxRate DECIMAL(9,4) NULL;
IF COL_LENGTH('dbo.SalesInvoiceDetails','TaxAmount') IS NULL         ALTER TABLE dbo.SalesInvoiceDetails ADD TaxAmount DECIMAL(18,2) NULL;
IF COL_LENGTH('dbo.SalesInvoiceDetails','CostPrice') IS NULL         ALTER TABLE dbo.SalesInvoiceDetails ADD CostPrice DECIMAL(18,2) NULL;
IF COL_LENGTH('dbo.SalesInvoiceDetails','ReturnedQty') IS NULL       ALTER TABLE dbo.SalesInvoiceDetails ADD ReturnedQty DECIMAL(18,3) NOT NULL CONSTRAINT DF_SID_ReturnedQty DEFAULT(0);
GO
IF COL_LENGTH('dbo.SalesInvoicePayments','PaymentMethodID') IS NULL ALTER TABLE dbo.SalesInvoicePayments ADD PaymentMethodID INT NULL;
IF COL_LENGTH('dbo.SalesInvoicePayments','ReferenceNo') IS NULL     ALTER TABLE dbo.SalesInvoicePayments ADD ReferenceNo VARCHAR(50) NULL;
IF COL_LENGTH('dbo.SalesInvoicePayments','CreditNoteID') IS NULL    ALTER TABLE dbo.SalesInvoicePayments ADD CreditNoteID INT NULL;
GO
IF COL_LENGTH('dbo.SalesSessions','LocationID') IS NULL       ALTER TABLE dbo.SalesSessions ADD LocationID INT NULL;
IF COL_LENGTH('dbo.SalesSessions','IsClosed') IS NULL         ALTER TABLE dbo.SalesSessions ADD IsClosed BIT NOT NULL CONSTRAINT DF_SS_IsClosed DEFAULT(1);
IF COL_LENGTH('dbo.SalesSessions','ClosedBy') IS NULL         ALTER TABLE dbo.SalesSessions ADD ClosedBy INT NULL;
IF COL_LENGTH('dbo.SalesSessions','Denominations') IS NULL    ALTER TABLE dbo.SalesSessions ADD Denominations VARCHAR(1000) NULL;
IF COL_LENGTH('dbo.SalesSessions','TotalReturnCash') IS NULL  ALTER TABLE dbo.SalesSessions ADD TotalReturnCash DECIMAL(18,2) NULL;
IF COL_LENGTH('dbo.SalesSessions','TotalOtherAmount') IS NULL ALTER TABLE dbo.SalesSessions ADD TotalOtherAmount DECIMAL(18,2) NULL;
GO
IF COL_LENGTH('dbo.PurchaseInvoices','LocationID') IS NULL       ALTER TABLE dbo.PurchaseInvoices ADD LocationID INT NULL;
IF COL_LENGTH('dbo.PurchaseInvoiceDetails','BatchNo') IS NULL    ALTER TABLE dbo.PurchaseInvoiceDetails ADD BatchNo VARCHAR(50) NULL;
IF COL_LENGTH('dbo.PurchaseInvoiceDetails','ExpDate') IS NULL    ALTER TABLE dbo.PurchaseInvoiceDetails ADD ExpDate DATETIME NULL;
IF COL_LENGTH('dbo.PurchaseInvoiceDetails','ItemStockID') IS NULL ALTER TABLE dbo.PurchaseInvoiceDetails ADD ItemStockID INT NULL;
IF COL_LENGTH('dbo.PurchaseInvoicePayments','PaymentNo') IS NULL ALTER TABLE dbo.PurchaseInvoicePayments ADD PaymentNo VARCHAR(50) NULL;
GO
IF COL_LENGTH('dbo.PayOut','LocationID') IS NULL          ALTER TABLE dbo.PayOut ADD LocationID INT NULL;
IF COL_LENGTH('dbo.PayOut','PayOutNo') IS NULL            ALTER TABLE dbo.PayOut ADD PayOutNo VARCHAR(50) NULL;
IF COL_LENGTH('dbo.ReceivedAmount','LocationID') IS NULL  ALTER TABLE dbo.ReceivedAmount ADD LocationID INT NULL;
IF COL_LENGTH('dbo.ReceivedAmount','ReceiptNo') IS NULL   ALTER TABLE dbo.ReceivedAmount ADD ReceiptNo VARCHAR(50) NULL;
IF COL_LENGTH('dbo.ReceivedAmount','PaymentMethodID') IS NULL ALTER TABLE dbo.ReceivedAmount ADD PaymentMethodID INT NULL;
IF COL_LENGTH('dbo.Transfers','FromLocationID') IS NULL   ALTER TABLE dbo.Transfers ADD FromLocationID INT NULL;
IF COL_LENGTH('dbo.Transfers','ToLocationID') IS NULL     ALTER TABLE dbo.Transfers ADD ToLocationID INT NULL;
IF COL_LENGTH('dbo.TransferDetails','ItemStockID') IS NULL ALTER TABLE dbo.TransferDetails ADD ItemStockID INT NULL;
GO

/* ---------------------------------------------------------------------
   3. INDEXES FOR FAST BILLING / REPORTS
   --------------------------------------------------------------------- */
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_Item_BarCode')      CREATE INDEX IX_Item_BarCode ON dbo.Item(BarCode);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_Item_ItemCode')     CREATE INDEX IX_Item_ItemCode ON dbo.Item(ItemCode);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_ItemStocks_ItemLoc') CREATE INDEX IX_ItemStocks_ItemLoc ON dbo.ItemStocks(ItemID, LocationID) INCLUDE(CurrentQuantity, SellingPrice, CostPrice, ExpDate);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_SalesInvoices_No')   CREATE INDEX IX_SalesInvoices_No ON dbo.SalesInvoices(InvoiceNo);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_SalesInvoices_Date') CREATE INDEX IX_SalesInvoices_Date ON dbo.SalesInvoices(TransactionDate, LocationID);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_SID_Invoice')        CREATE INDEX IX_SID_Invoice ON dbo.SalesInvoiceDetails(SalesInvoiceID);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_SIP_Invoice')        CREATE INDEX IX_SIP_Invoice ON dbo.SalesInvoicePayments(SalesInvoiceID);
GO

/* ---------------------------------------------------------------------
   4. SEED DATA
   --------------------------------------------------------------------- */
IF NOT EXISTS(SELECT 1 FROM dbo.Locations)
BEGIN
    SET IDENTITY_INSERT dbo.Locations ON;
    INSERT dbo.Locations(LocationID, LocationCode, LocationName) VALUES(1,'MAIN',N'Main Store');
    SET IDENTITY_INSERT dbo.Locations OFF;
END
GO
UPDATE dbo.SalesInvoices    SET LocationID = 1 WHERE LocationID IS NULL;
UPDATE dbo.SalesSessions    SET LocationID = 1 WHERE LocationID IS NULL;
UPDATE dbo.PurchaseInvoices SET LocationID = 1 WHERE LocationID IS NULL;
UPDATE dbo.PayOut           SET LocationID = 1 WHERE LocationID IS NULL;
UPDATE dbo.ReceivedAmount   SET LocationID = 1 WHERE LocationID IS NULL;
GO
IF NOT EXISTS(SELECT 1 FROM dbo.Taxes)
    INSERT dbo.Taxes(TaxCode, TaxName, Rate) VALUES('VAT', N'VAT', 18), ('SSCL', N'SSCL', 2.5);
GO
IF NOT EXISTS(SELECT 1 FROM dbo.PaymentMethods)
INSERT dbo.PaymentMethods(Code, Name, Kind, RequiresReference, OpensDrawer, ShortcutKey, SortOrder) VALUES
 ('CASH',    N'Cash',          1, 0, 1, 'F1', 1),
 ('CARD',    N'Card',          2, 1, 0, 'F2', 2),
 ('CREDIT',  N'Credit',        3, 0, 0, 'F3', 3),
 ('CHEQUE',  N'Cheque',        4, 1, 1, 'F4', 4),
 ('CN',      N'Credit Note',   5, 1, 0, 'F5', 5),
 ('BANK',    N'Bank Transfer', 6, 1, 0, 'F6', 6),
 ('QR',      N'LankaQR / Mobile', 7, 1, 0, 'F7', 7),
 ('VOUCHER', N'Gift Voucher',  8, 1, 0, 'F8', 8);
GO
IF NOT EXISTS(SELECT 1 FROM dbo.UnitTypes)
INSERT dbo.UnitTypes(UnitTypeId, [Type]) VALUES (1,'Nos'),(2,'Kg'),(3,'g'),(4,'L'),(5,'ml'),(6,'Pack'),(7,'Box'),(8,'m');
GO

/* App settings - copy existing CompanyProfile so the receipt header keeps working */
DECLARE @Name NVARCHAR(500), @A1 NVARCHAR(100), @A2 NVARCHAR(100), @A3 NVARCHAR(100), @TP NVARCHAR(100), @Mail NVARCHAR(100), @Web NVARCHAR(100);
SELECT TOP 1 @Name = CompanyName, @A1 = Add1, @A2 = Add2, @A3 = Add3, @TP = TP, @Mail = [E-Mail], @Web = Web FROM dbo.CompanyProfile;

DECLARE @Defaults TABLE(K VARCHAR(100), V NVARCHAR(2000));
INSERT @Defaults VALUES
 ('Shop.Name',            ISNULL(@Name, N'MY SUPERMARKET')),
 ('Shop.Address1',        ISNULL(@A1, N'')),
 ('Shop.Address2',        ISNULL(@A2, N'')),
 ('Shop.Address3',        ISNULL(@A3, N'')),
 ('Shop.Phone',           ISNULL(@TP, N'')),
 ('Shop.Mobile',          N''),
 ('Shop.Logo',            N''),
 ('Receipt.ShowLogo',     N'True'),
 ('Receipt.ShowBarcode',  N'True'),
 ('Receipt.PoweredBy',    N''),
 ('Shop.Email',           ISNULL(@Mail, N'')),
 ('Shop.Web',             ISNULL(@Web, N'')),
 ('Shop.TaxRegNo',        N''),
 ('Receipt.HeaderNote',   N''),
 ('Receipt.Footer',       ISNULL((SELECT TOP 1 BillGreeting FROM dbo.Greetings WHERE IsDeleted = 0 ORDER BY GreetingID DESC), N'Thank you! Come again.')),
 ('Receipt.ShowSavings',  N'True'),
 ('Receipt.ShowItemCode', N'False'),
 ('Tax.Enabled',          N'False'),
 ('Tax.PricesIncludeTax', N'True'),
 ('Billing.RoundTo',      N'0'),
 ('Billing.AllowNegativeStock', N'True'),
 ('Billing.CurrencySymbol', N'Rs.'),
 ('Billing.CreditNoteValidDays', N'90'),
 ('Billing.ReturnDaysLimit', N'14');
INSERT dbo.AppSettings(SettingKey, SettingValue)
SELECT K, V FROM @Defaults d WHERE NOT EXISTS(SELECT 1 FROM dbo.AppSettings s WHERE s.SettingKey = d.K);
GO

/* Permissions (stored in the existing FormRules table) */
DECLARE @P TABLE(K VARCHAR(50), M VARCHAR(50), D VARCHAR(200));
INSERT @P VALUES
 ('Master.Items','Masters','Item master'),
 ('Master.Categories','Masters','Item categories'),
 ('Master.Brands','Masters','Brands'),
 ('Master.Units','Masters','Units of measure'),
 ('Master.Suppliers','Masters','Suppliers'),
 ('Master.Customers','Masters','Customers'),
 ('Master.Employees','Masters','Employees'),
 ('Master.Locations','Masters','Locations / branches'),
 ('Master.Taxes','Masters','Taxes'),
 ('Master.PaymentMethods','Masters','Payment methods'),
 ('POS.Access','POS','Open billing screen'),
 ('POS.LineDiscount','POS','Give item discount'),
 ('POS.BillDiscount','POS','Give bill discount'),
 ('POS.PriceOverride','POS','Change selling price at billing'),
 ('POS.VoidLine','POS','Void / remove an item from bill'),
 ('POS.CancelBill','POS','Clear the current (unsaved) bill'),
 ('POS.Hold','POS','Hold and recall bills'),
 ('POS.CreditSale','POS','Credit sales'),
 ('POS.Reprint','POS','Re-print bills'),
 ('POS.OpenDrawer','POS','Open cash drawer without sale'),
 ('POS.ChangeLocation','POS','Change billing location'),
 ('POS.CancelInvoice','POS','Cancel a saved invoice'),
 ('Returns.Sales','Returns','Sales returns / refunds'),
 ('Returns.Purchase','Returns','Purchase returns'),
 ('Stock.GRN','Inventory','Goods received (purchase)'),
 ('Stock.Transfer','Inventory','Stock transfer between locations'),
 ('Stock.Adjustment','Inventory','Stock adjustment'),
 ('Stock.PriceChange','Inventory','Change item prices'),
 ('Stock.ViewCost','Inventory','See cost prices'),
 ('Stock.Barcode','Inventory','Print barcode stickers'),
 ('Cash.PaidOut','Cash','Paid outs / expenses'),
 ('Cash.Received','Cash','Receive customer payments'),
 ('Cash.SupplierPayment','Cash','Supplier payments'),
 ('Session.Close','Cash','Close cashier session'),
 ('Session.ViewAll','Cash','View all cashier sessions'),
 ('Reports.Sales','Reports','Sales reports'),
 ('Reports.Stock','Reports','Stock reports'),
 ('Reports.Purchase','Reports','Purchase reports'),
 ('Reports.Financial','Reports','Profit, cash and outstanding reports'),
 ('Security.Users','Security','Manage users'),
 ('Security.Roles','Security','Manage roles and permissions'),
 ('Settings.Manage','Settings','Shop / billing / tax settings'),
 ('Settings.Terminal','Settings','Terminal and printer settings');
INSERT dbo.FormRules(FormRuleName, FormName, Description, DateCreated, IsDeleted)
SELECT K, M, D, GETDATE(), 0 FROM @P p WHERE NOT EXISTS(SELECT 1 FROM dbo.FormRules f WHERE f.FormRuleName = p.K);
UPDATE f SET f.FormName = p.M, f.Description = p.D FROM dbo.FormRules f JOIN @P p ON p.K = f.FormRuleName;
GO

/* Administrator role + user (password: admin123, re-hashed with PBKDF2 at first login) */
IF NOT EXISTS(SELECT 1 FROM dbo.UserGroups WHERE IsSuperUser = 1 AND ISNULL(IsDeleted,0) = 0)
    INSERT dbo.UserGroups(UserGroupName, IsSuperUser, IsSuperAdmin, IsAutoLogout, DateCreated, IsDeleted, MaxDiscountPercent)
    VALUES('Administrator', 1, 1, 0, GETDATE(), 0, 100);
IF NOT EXISTS(SELECT 1 FROM dbo.UserGroups WHERE UserGroupName = 'Cashier')
    INSERT dbo.UserGroups(UserGroupName, IsSuperUser, IsSuperAdmin, IsAutoLogout, DateCreated, IsDeleted, MaxDiscountPercent)
    VALUES('Cashier', 0, 0, 0, GETDATE(), 0, 5);
GO
IF NOT EXISTS(SELECT 1 FROM dbo.UserProfile WHERE userId = 'admin')
    INSERT dbo.UserProfile(userId, Password, FullName, DateCreated, IsDeleted, IsManger, IsSystemAccessible, UserGroupID, DefaultLocationID)
    SELECT 'admin', LOWER(CONVERT(VARCHAR(32), HASHBYTES('MD5', 'admin123'), 2)), 'System Administrator', GETDATE(), 0, 1, 1,
           (SELECT TOP 1 UserGroupID FROM dbo.UserGroups WHERE IsSuperUser = 1 ORDER BY UserGroupID), 1;
GO
/* Default cashier permissions */
DECLARE @Cashier INT = (SELECT TOP 1 UserGroupID FROM dbo.UserGroups WHERE UserGroupName = 'Cashier');
IF @Cashier IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.UserGroupFormRules WHERE UserGroupID = @Cashier)
    INSERT dbo.UserGroupFormRules(UserGroupID, FormRuleID, DateCreated, IsDeleted)
    SELECT @Cashier, FormRuleID, GETDATE(), 0 FROM dbo.FormRules
    WHERE FormRuleName IN ('POS.Access','POS.Hold','POS.LineDiscount','POS.Reprint','Cash.PaidOut','Cash.Received','Session.Close');
GO

/* Opening movement so the stock ledger starts from current balances */
IF NOT EXISTS(SELECT 1 FROM dbo.StockMovements)
    INSERT dbo.StockMovements(MovementDate, ItemID, LocationID, ItemStockID, MovementType, RefNo, QtyIn, QtyOut, CostPrice)
    SELECT GETDATE(), s.ItemID, s.LocationID, s.ItemStockID, 'OPENING', 'UPGRADE',
           CASE WHEN s.CurrentQuantity > 0 THEN s.CurrentQuantity ELSE 0 END,
           CASE WHEN s.CurrentQuantity < 0 THEN -s.CurrentQuantity ELSE 0 END,
           s.CostPrice
    FROM dbo.ItemStocks s WHERE ISNULL(s.IsDeleted,0) = 0 AND ISNULL(s.CurrentQuantity,0) <> 0 AND s.ItemID IS NOT NULL;
GO

/* ---------------------------------------------------------------------
   5. DOCUMENT NUMBERING (concurrency-safe, per location per day)
      Replaces GetNextBillNumber which could hand out duplicates.
      Format:  INV  ->  MAIN-261008-0001
               other -> SRN-MAIN-261008-0001
   --------------------------------------------------------------------- */
IF OBJECT_ID('dbo.usp_GetNextDocNo','P') IS NOT NULL DROP PROCEDURE dbo.usp_GetNextDocNo;
GO
CREATE PROCEDURE dbo.usp_GetNextDocNo
    @DocType    VARCHAR(10),
    @LocationID INT,
    @Date       DATETIME,
    @DocNo      VARCHAR(50) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @d DATE = CAST(@Date AS DATE), @n INT;

    UPDATE dbo.DocumentSequences WITH (UPDLOCK, HOLDLOCK)
       SET @n = LastNo = LastNo + 1
     WHERE DocType = @DocType AND LocationID = @LocationID AND SeqDate = @d;

    IF @@ROWCOUNT = 0
    BEGIN
        BEGIN TRY
            INSERT dbo.DocumentSequences(DocType, LocationID, SeqDate, LastNo) VALUES(@DocType, @LocationID, @d, 1);
            SET @n = 1;
        END TRY
        BEGIN CATCH
            UPDATE dbo.DocumentSequences WITH (UPDLOCK, HOLDLOCK)
               SET @n = LastNo = LastNo + 1
             WHERE DocType = @DocType AND LocationID = @LocationID AND SeqDate = @d;
        END CATCH
    END

    DECLARE @loc VARCHAR(10) = ISNULL((SELECT LocationCode FROM dbo.Locations WHERE LocationID = @LocationID), 'L' + CAST(@LocationID AS VARCHAR(5)));
    SET @DocNo = CASE WHEN @DocType = 'INV' THEN '' ELSE @DocType + '-' END
               + @loc + '-' + CONVERT(VARCHAR(6), @d, 12) + '-'
               + RIGHT('0000' + CAST(@n AS VARCHAR(10)), 4);
END
GO
PRINT 'Schema upgrade completed.';
GO

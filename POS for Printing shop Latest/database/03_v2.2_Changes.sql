/* =====================================================================
   POS v2.2 - SQL CHANGES ONLY (run once, safe to run again)
   - New permission: Stock.Barcode  (Inventory > Barcode stickers)
   - Sticker settings and language need NO table changes:
       sticker settings are saved in dbo.AppSettings (Label.*) automatically,
       language is saved on each computer (%LocalAppData%\PosSystem\ui.json).
   ===================================================================== */
SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.FormRules WHERE FormRuleName = 'Stock.Barcode')
    INSERT dbo.FormRules (FormRuleName, FormName, Description, DateCreated, IsDeleted)
    VALUES ('Stock.Barcode', 'Inventory', 'Print barcode stickers', GETDATE(), 0);
GO

/* Optional: give the permission to an existing role (administrators already have every permission).
INSERT dbo.UserGroupFormRules (UserGroupID, FormRuleID, DateCreated, IsDeleted)
SELECT g.UserGroupID, f.FormRuleID, GETDATE(), 0
FROM dbo.UserGroups g CROSS JOIN dbo.FormRules f
WHERE g.UserGroupName = 'Cashier' AND f.FormRuleName = 'Stock.Barcode'
  AND NOT EXISTS (SELECT 1 FROM dbo.UserGroupFormRules x WHERE x.UserGroupID = g.UserGroupID AND x.FormRuleID = f.FormRuleID);
*/

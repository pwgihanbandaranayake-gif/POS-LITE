# POS System v2.1: WPF (.NET 8), simple structure

One project, plain folders, no interfaces and no dependency injection.
Every repository and service is a **static class**, and every screen is just `new XxxViewModel()`.

## Folder structure

```
PosSystem.sln
database/
  01_Upgrade_Schema.sql        upgrades your existing DB (keeps data)
  02_Report_Procedures.sql     29 report procedures (rpt_*)
PosSystem/
  App.xaml(.cs)                start-up: Login -> Back office -> POS
  appsettings.json             connection string
  GlobalUsings.cs              common namespaces for all files

  Models/                      plain data classes
    Entities.cs                Item, Location, PaymentMethod, Terminal, UserAccount ...
    Dtos.cs                    SaleRequest, ReceiptData, HeldBillDto ...
    Enums.cs                   DiscountType, PaymentKind, ReceiptFormat ...
    PosSettings.cs             shop details + bill options (AppSettings table)
    Permissions.cs             permission keys (Perm.PosVoidLine ...)
    MasterCatalog.cs           field lists for the simple master screens
    ReportCatalog.cs           list of the 29 reports

  Data/                        SQL Server (Dapper) - one static class per area
    Db.cs                      connection + transaction helper + doc numbers
    StockLedger.cs             the only place stock quantities change
    ItemRepository.cs  SalesRepository.cs  HoldRepository.cs  ReturnRepository.cs
    StockRepository.cs UserRepository.cs   LookupRepository.cs TerminalRepository.cs
    SessionCashRepositories.cs  (shift + paid out/received)
    SimpleRepositories.cs       (settings, reports, masters)

  Services/                    app logic
    Session.cs                 logged-in user, till, location, shift, settings
    AuthService.cs             login, till registration, supervisor approval
    SalesService.cs            checks before a bill is saved
    ReceiptService.cs          adds shop details/logo and prints
    ShiftService.cs            open / close shift, Z report
    SettingsService.cs         load / save settings
    Dialogs.cs                 message boxes, prompts, OverrideService

  Printing/
    ReceiptLayout.cs           RollReceipt (thermal/dot-matrix) + A5Invoice
    TextReceipt.cs             same layout as plain text / ESC-POS
    Barcode128.cs              invoice number barcode
    LogoHelper.cs              logo load / print
    ReceiptPrinter.cs          sends to the printer set for this till
    ReportPrinter.cs  RawPrinter.cs

  Helpers/  Money, BillCalculator (discount/tax maths), PasswordHasher, ExcelExporter,
            Converters, Behaviors
  ViewModels/  Views/  Themes/
```

**How a screen talks to the database**
```
View (XAML)  --binding-->  ViewModel  --calls-->  XxxRepository (static)  --Dapper-->  SQL Server
e.g. Save button -> SaveCommand -> ItemRepository.SaveAsync(item, ...) -> INSERT/UPDATE dbo.Item
```

## Setup
1. Back up the database, then run `database/01_Upgrade_Schema.sql` and `database/02_Report_Procedures.sql` in SSMS.
   - Both scripts are safe to run again.
   - Re-run `01` even if you already ran the old one: it makes `AppSettings.SettingValue` NVARCHAR(MAX) so the logo can be stored.
2. Open `PosSystem.sln` in Visual Studio 2026 and set the server in `PosSystem/appsettings.json`.
3. Press F5 and log in. The script creates `admin` / `admin123` - change that password immediately.
4. Go to **Settings & printers → Shop details**:
   - shop name, address, **Mobile**, **Telephone**
   - **Choose image...** for the logo
   - footer ("Thank You !"), bottom line ("Powered by ..."), barcode on/off
5. Go to **Settings → This till and printers**: choose the printer and paper width, then press **Preview receipt**.
   The preview uses the same items as a real bill.

## Bill layout (roll printer)
```
               [ LOGO ]
     INDUNIL HARDWARE AND MOTORS
        Elapathwewa Junction
             Horowpothana
  Mob : 071 785 7772 Tel : 077 785 7772
 ______________________________________
 Invoice No..          MAIN-261016-0001
 Cashier:                        NIMAL
 Date:          16/10/2026 01:11:18 PM
 ██ PRODUCT     PRICE     DISC  AMOUNT ██
 W/B FLOOR PAINT OPEL 500ML
 1.000        1000.00   100.00  900.00
 CEMENT 50KG
 1.000        1950.00    25.00 1925.00
 ______________________________________
 Net Total                    3,575.00
 Paid Amount                  5,000.00
 Change                       1,425.00
              You Saved
              Rs.375.00
 - - - - - - - - - - - - - - - - - - -
 Items Count                         3
          ||||||||||||||||||
             Thank You !
 - - - - - - - - - - - - - - - - - - -
     Powered by ... (Settings > bottom line)
```

Extra lines appear automatically when they apply:
- Bill discount, Tax and Round off show above Net Total when they are used.
- For card, credit-note or split payments, each payment is listed under Paid Amount.

| Print method (Settings) | Logo | Barcode | Notes |
|---|---|---|---|
| Windows driver | yes | yes | any font, Sinhala/Tamil ok |
| Direct text / ESC-POS (thermal) | yes (GS v 0) | yes (GS k) | fastest |
| Direct text (dot matrix) | no | no | plain text, same columns |
| A5 half sheet | yes | no | logo top-left |

The barcode encodes the bill number. Scanning it in **Sales returns → Invoice no** loads the bill.

---------------------------------------------------------------------
## v2.2 changes

### 1. Barcode stickers  (Inventory > Barcode stickers)
- Scan or type an item code. Empty Enter or F2 opens item search. Then enter the number of stickers.
- **Add whole category** adds every item in a category.
- **Add blank sticker** works like the old "miscellaneous" barcode form: you type the code, description and price.
- In the list you can edit the description, weight, unit price, MFD / EXP dates and number of stickers.
- **Stickers = stock qty** sets each line's sticker count to its current stock.
- Each sticker shows: shop name (optional), description, weight, unit price (Rs. x / kg for loose items),
  a barcode of the item code (scan it at the till) and the code number. MFD / EXP dates are optional.
- Sticker settings are saved in AppSettings (`Label.*`):
  - **Size:** default 2 x 1.5 inch, landscape.
  - **Layout:** stickers across (horizontal), stickers down (vertical), gaps and margins.
  - **Printer:** the sticker printer, and "turn 90 degrees" for printers that feed sideways.
- **Skip used stickers on first sheet** lets you continue a half-used A4 sticker sheet.
- **Label roll printers (e.g. Xprinter XP-365B):** set the driver paper size to the page size shown under the preview.
  For 2 stickers across, that is the 2-sticker width.
- New permission `Stock.Barcode`: run `database/03_v2.2_Changes.sql`.

### 2. Language: English / සිංහල / தமிழ்
- Change it on the **sign-in screen** (top-left buttons) or in **Settings > Language > Apply language**.
  The system logs out and reopens in the new language.
- The language is saved per computer, so each till can use its own language.
- Menus, buttons, labels, messages, reports list and column headers, the touch screen and dialogs are all translated.
- Bills, stickers, printed reports and Excel exports stay in English.
  Raw ESC/POS and dot-matrix printers cannot print Sinhala or Tamil.
- **To correct a translation:** open `Helpers/Translations.cs`, find the English text and change the text on the right.
  Keep `{0}`, `{1}` - those are numbers and names filled in by the program.
- **Fonts:** Windows 10/11 "Nirmala UI" shows both Sinhala and Tamil. "Iskoola Pota" / "Latha" are the fallbacks.

---------------------------------------------------------------------
## v2.2 + Line note (flex / sticker sizes)

1. Run `database/04_LineNote.sql` once. It adds `LineNote` to `SalesInvoiceDetails` and `HeldBillItems`.

2. To add a note on the billing screen, use any of these:
   - **Ctrl+N**
   - double-click the line
   - the **Line note** button on the touch screen

   Type `2x4` and the line shows and prints as `FLEX BANNER (2x4)`.

3. Where the note appears:
   - The roll, A5 and text bills, and re-print / preview, because the note is saved with the bill line.
   - Hold / recall and returns keep the note.
   - The same item with different notes stays on separate lines.

---------------------------------------------------------------------
## v2.2.2 - Paid outs report: employee filter
- Reports > Paid Outs & Receipts has an **Employee** filter and an **Employee** column.
  - (All): all paid outs and receipts in the date range, as before.
  - An employee selected: only that employee's paid outs in the date range.
- Stored procedure `rpt_CashInOut` has a new parameter `@EmployeeID` (see database/02_Report_Procedures.sql).

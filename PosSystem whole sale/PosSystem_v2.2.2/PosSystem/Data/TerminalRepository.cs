namespace PosSystem.Data;

public static class TerminalRepository
{

    public static async Task<Terminal?> GetByMachineAsync(string machineCode)
    {
        await using var cn = await Db.OpenAsync();
        return await cn.QueryFirstOrDefaultAsync<Terminal>(@"
            SELECT PcRegistrationID, MachineCode, Description, LocationID, ReceiptPrinter, ReceiptFormat, PaperWidthMm, CharsPerLine,
                   PrintEngine, A5Printer, A5Landscape, AutoPrint, PrintCopies, OpenCashDrawer, DefaultTouchMode
            FROM dbo.PcRegistration WHERE MachineCode = @machineCode AND IsDeleted = 0", new { machineCode });
    }

    public static async Task<int> SaveAsync(Terminal t, int byUser)
    {
        await using var cn = await Db.OpenAsync();
        if (t.PcRegistrationID == 0)
            return await cn.ExecuteScalarAsync<int>(@"
                INSERT dbo.PcRegistration(MachineCode, Description, DateCreated, DateModified, CreatedBy, IsDeleted, LocationID,
                    ReceiptPrinter, ReceiptFormat, PaperWidthMm, CharsPerLine, PrintEngine, A5Printer, A5Landscape, AutoPrint,
                    PrintCopies, OpenCashDrawer, DefaultTouchMode)
                VALUES(@MachineCode, @Description, GETDATE(), GETDATE(), @byUser, 0, @LocationID,
                    @ReceiptPrinter, @ReceiptFormat, @PaperWidthMm, @CharsPerLine, @PrintEngine, @A5Printer, @A5Landscape, @AutoPrint,
                    @PrintCopies, @OpenCashDrawer, @DefaultTouchMode);
                SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new { t.MachineCode, t.Description, byUser, t.LocationID, t.ReceiptPrinter, t.ReceiptFormat, t.PaperWidthMm, t.CharsPerLine,
                      t.PrintEngine, t.A5Printer, t.A5Landscape, t.AutoPrint, t.PrintCopies, t.OpenCashDrawer, t.DefaultTouchMode });

        await cn.ExecuteAsync(@"
            UPDATE dbo.PcRegistration SET Description = @Description, LocationID = @LocationID, ReceiptPrinter = @ReceiptPrinter,
                ReceiptFormat = @ReceiptFormat, PaperWidthMm = @PaperWidthMm, CharsPerLine = @CharsPerLine, PrintEngine = @PrintEngine,
                A5Printer = @A5Printer, A5Landscape = @A5Landscape, AutoPrint = @AutoPrint, PrintCopies = @PrintCopies,
                OpenCashDrawer = @OpenCashDrawer, DefaultTouchMode = @DefaultTouchMode, DateModified = GETDATE(), ModifiedBy = @byUser
            WHERE PcRegistrationID = @PcRegistrationID",
            new { t.Description, t.LocationID, t.ReceiptPrinter, t.ReceiptFormat, t.PaperWidthMm, t.CharsPerLine, t.PrintEngine,
                  t.A5Printer, t.A5Landscape, t.AutoPrint, t.PrintCopies, t.OpenCashDrawer, t.DefaultTouchMode, byUser, t.PcRegistrationID });
        return t.PcRegistrationID;
    }
}

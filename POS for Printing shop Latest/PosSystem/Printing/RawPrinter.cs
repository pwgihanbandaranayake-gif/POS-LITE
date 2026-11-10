using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PosSystem.Printing;

/// <summary>Sends raw bytes (plain text / ESC-POS) directly to a Windows printer queue.</summary>
public static class RawPrinter
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private class DOCINFO
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string? pDocName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pDataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string src, out IntPtr hPrinter, IntPtr pd);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);
    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] DOCINFO di);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    public static void Send(string printerName, byte[] bytes, string docName = "POS")
    {
        if (!OpenPrinter(printerName.Normalize(), out var h, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot open printer '{printerName}'.");
        var p = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, p, bytes.Length);
            if (!StartDocPrinter(h, 1, new DOCINFO { pDocName = docName, pDataType = "RAW" }))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            StartPagePrinter(h);
            WritePrinter(h, p, bytes.Length, out _);
            EndPagePrinter(h);
            EndDocPrinter(h);
        }
        finally
        {
            Marshal.FreeCoTaskMem(p);
            ClosePrinter(h);
        }
    }
}

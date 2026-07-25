using System.ComponentModel;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using Zyron.Print.Infrastructure;

namespace Zyron.Print.Printing;

public sealed class RawPrinterService
{
    private readonly FileLogger _logger;

    public RawPrinterService(FileLogger logger) => _logger = logger;

    public IReadOnlyList<string> GetInstalledPrinters() =>
        PrinterSettings.InstalledPrinters.Cast<string>().OrderBy(name => name).ToArray();

    public void Print(string printerName, byte[] bytes, string documentName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
            throw new InvalidOperationException("Selecione uma impressora.");
        if (!GetInstalledPrinters().Contains(printerName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"A impressora '{printerName}' não está instalada.");

        if (!OpenPrinter(printerName, out var printerHandle, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível abrir a impressora.");

        try
        {
            var document = new DocInfo { DocumentName = documentName, DataType = "RAW" };
            if (StartDocPrinter(printerHandle, 1, document) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível iniciar o trabalho.");
            try
            {
                if (!StartPagePrinter(printerHandle))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível iniciar a página.");
                var unmanaged = Marshal.AllocCoTaskMem(bytes.Length);
                try
                {
                    Marshal.Copy(bytes, 0, unmanaged, bytes.Length);
                    if (!WritePrinter(printerHandle, unmanaged, bytes.Length, out var written) || written != bytes.Length)
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "O Windows não enviou todos os dados à impressora.");
                }
                finally
                {
                    Marshal.FreeCoTaskMem(unmanaged);
                    EndPagePrinter(printerHandle);
                }
            }
            finally
            {
                EndDocPrinter(printerHandle);
            }
        }
        finally
        {
            ClosePrinter(printerHandle);
        }
        _logger.Info($"Trabalho '{documentName}' enviado para '{printerName}' ({bytes.Length} bytes).");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private sealed class DocInfo
    {
        [MarshalAs(UnmanagedType.LPStr)] public string DocumentName = "";
        [MarshalAs(UnmanagedType.LPStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPStr)] public string DataType = "RAW";
    }

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string printerName, out IntPtr printerHandle, IntPtr defaults);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool ClosePrinter(IntPtr printerHandle);
    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int StartDocPrinter(IntPtr printerHandle, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DocInfo document);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool EndDocPrinter(IntPtr printerHandle);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool StartPagePrinter(IntPtr printerHandle);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool EndPagePrinter(IntPtr printerHandle);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr printerHandle, IntPtr bytes, int count, out int written);
}

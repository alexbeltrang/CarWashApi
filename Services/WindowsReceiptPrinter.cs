using System.Drawing;
using System.Drawing.Printing;
using System.Runtime.Versioning;

namespace Carwash.API.Services;

public interface IReceiptPrinter
{
    void Submit(string printerName, string text, int widthMm, string documentName);
}

public sealed class PrinterUnavailableException : IOException;

public sealed class UnavailableReceiptPrinter : IReceiptPrinter
{
    public void Submit(string printerName, string text, int widthMm, string documentName) => throw new PrinterUnavailableException();
}

[SupportedOSPlatform("windows")]
public sealed class WindowsReceiptPrinter : IReceiptPrinter
{
    public void Submit(string printerName, string text, int widthMm, string documentName)
    {
        if (string.IsNullOrWhiteSpace(printerName) || widthMm is not (58 or 80)) throw new PrinterUnavailableException();
        var snapshot = ReceiptPrintLayout.ReadSnapshot(text);
        using var logoStream = snapshot.LogoBase64 is null ? null : new MemoryStream(Convert.FromBase64String(snapshot.LogoBase64));
        using var logo = logoStream is null ? null : Image.FromStream(logoStream);
        using var document = new PrintDocument();
        document.PrinterSettings.PrinterName = printerName;
        document.PrinterSettings.Copies = 1;
        document.PrinterSettings.PrintToFile = false;
        if (!document.PrinterSettings.IsValid) throw new PrinterUnavailableException();
        document.PrintController = new StandardPrintController();
        document.DocumentName = documentName;
        var paper = document.PrinterSettings.PaperSizes.Cast<PaperSize>().FirstOrDefault(size => Is80Mm(size.Width));
        if (paper is not null) document.DefaultPageSettings.PaperSize = paper;
        else if (document.DefaultPageSettings.PaperSize.Width < 700 && widthMm == 80) throw new PrinterUnavailableException();
        document.DefaultPageSettings.Margins = new Margins(8, 8, 8, 8);
        var offset = 0;
        document.BeginPrint += (_, _) => offset = 0;
        document.PrintPage += (_, args) =>
        {
            var width = Math.Min(args.MarginBounds.Width, (int)Math.Round(widthMm * 100m / 25.4m) - 16);
            offset = DrawDocumentPage(args.Graphics!, new Rectangle(args.MarginBounds.Left, args.MarginBounds.Top, width, args.MarginBounds.Height), snapshot.Texto, logo, offset);
            args.HasMorePages = offset < snapshot.Texto.Length;
        };
        document.Print();
    }

    public static bool Is80Mm(int widthHundredthsOfInch) => Math.Abs(widthHundredthsOfInch - 315) <= 2;

    public static int DrawDocumentPage(Graphics graphics, Rectangle bounds, string text, Image? logo, int offset)
    {
        if (offset == 0 && logo is not null)
        {
            var scale = Math.Min(Math.Min(160f, bounds.Width) / logo.Width, Math.Min(100f, bounds.Height / 3f) / logo.Height);
            var width = logo.Width * scale;
            var height = logo.Height * scale;
            graphics.DrawImage(logo, bounds.Left + (bounds.Width - width) / 2f, bounds.Top, width, height);
            var used = (int)Math.Ceiling(height) + 10;
            bounds = new Rectangle(bounds.Left, bounds.Top + used, bounds.Width, bounds.Height - used);
        }
        return DrawPage(graphics, bounds, text, offset);
    }

    public static int DrawPage(Graphics graphics, Rectangle bounds, string text, int offset)
    {
        using var font = new Font("Consolas", 9);
        using var format = StringFormat.GenericTypographic;
        format.FormatFlags &= ~StringFormatFlags.NoClip;
        var remaining = text[offset..];
        graphics.MeasureString(remaining, font, bounds.Size, format, out var characters, out _);
        if (characters <= 0) throw new IOException("PRINT_LAYOUT_UNAVAILABLE");
        graphics.DrawString(remaining[..characters], font, Brushes.Black, bounds, format);
        return offset + characters;
    }
}
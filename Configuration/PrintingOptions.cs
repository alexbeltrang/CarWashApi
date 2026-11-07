namespace Carwash.API.Configuration;

public sealed class PrintingOptions
{
    public bool Enabled { get; set; }
    public string PrinterName { get; set; } = "";
}
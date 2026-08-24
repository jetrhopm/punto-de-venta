using Pos.Printing;
using System.Globalization;
using System.Printing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

namespace Pos.Desktop;

public sealed record TicketPrintProfile(string FontFamily, double FontSize, bool UseNormalTotals, int WidthMm);

public static class TicketWindowsPrinter
{
    private const double DipsPerMillimeter = 96d / 25.4d;

    public static TicketPrintProfile CurrentProfile => new(
        ApiClient.PrinterFontFamily,
        ApiClient.PrinterFontSize,
        ApiClient.UseNormalTotals,
        ApiClient.PrinterTicketWidthMm);

    public static string[] GetInstalledPrinters()
    {
        using var server = new LocalPrintServer();
        return server.GetPrintQueues()
            .Select(queue => queue.FullName)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static string[] GetInstalledFonts() => Fonts.SystemFontFamilies
        .Select(family => family.Source)
        .Distinct(StringComparer.CurrentCultureIgnoreCase)
        .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    public static TicketPdfData CreateSample(int widthMm) => new(
        "ABARROTES LA ESPERANZA",
        "Comercializadora La Esperanza, S.A. de C.V.",
        "XAXX010101000",
        "Av. Principal 123, Col. Centro",
        "55 1234 5678",
        "Servicio todos los dias",
        "Gracias por su compra",
        widthMm == 58 ? 58 : 80,
        Guid.Parse("00000124-0000-0000-0000-000000000000"),
        Guid.Parse("00000091-0000-0000-0000-000000000000"),
        "Caja principal",
        "Administrador",
        DateTimeOffset.Now,
        [
            new TicketPdfLine("Agua purificada 600 ml", 2m, 12.50m, 25m),
            new TicketPdfLine("Cafe tostado molido", 1m, 89.90m, 89.90m),
            new TicketPdfLine("Tomate por kilogramo", 0.750m, 32m, 24m)
        ],
        [new TicketPdfPayment("Cash", 138.90m, 200m, 61.10m)],
        138.90m);

    public static FrameworkElement CreateTicketVisual(TicketPdfData ticket, TicketPrintProfile profile)
    {
        var widthMm = profile.WidthMm == 58 ? 58 : 80;
        var pageWidth = widthMm * DipsPerMillimeter;
        var padding = widthMm == 58 ? 8d : 11d;
        var baseSize = profile.FontSize * 96d / 72d;
        var family = new FontFamily(string.IsNullOrWhiteSpace(profile.FontFamily) ? "Consolas" : profile.FontFamily);
        var root = new StackPanel
        {
            Width = pageWidth - (padding * 2d),
            Background = Brushes.White
        };
        TextElement.SetFontFamily(root, family);
        TextElement.SetFontSize(root, baseSize);

        if (ticket.SaleId == Guid.Empty)
        {
            root.Children.Add(Text(ticket.StoreName.ToUpperInvariant(), baseSize + 4d, FontWeights.Bold, TextAlignment.Center, new Thickness(0, 0, 0, 3)));
            root.Children.Add(Rule());
            root.Children.Add(Text("COMPROBANTE DE RETIRO", baseSize + 2d, FontWeights.Bold, TextAlignment.Center, new Thickness(0, 1, 0, 3)));
            root.Children.Add(Text("RETIRO DE EFECTIVO DE CAJA", baseSize, FontWeights.SemiBold, TextAlignment.Center, new Thickness(0, 0, 0, 6)));
            root.Children.Add(Metadata("Fecha", ticket.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"), baseSize));
            root.Children.Add(Metadata("Motivo", ticket.Lines.FirstOrDefault()?.Description ?? "Retiro de efectivo", baseSize));
            root.Children.Add(Rule());
            root.Children.Add(AmountLine(ticket, "TOTAL RETIRADO", ticket.Total, baseSize + 3d, FontWeights.Bold));
            root.Children.Add(Rule());
            AddOptionalCentered(root, string.IsNullOrWhiteSpace(ticket.Footer) ? "Conserve este comprobante" : ticket.Footer, baseSize);
            return new Border { Width = pageWidth, Padding = new Thickness(padding), Background = Brushes.White, Child = root };
        }

        root.Children.Add(Text(ticket.StoreName.ToUpperInvariant(), baseSize + 4d, FontWeights.Bold, TextAlignment.Center, new Thickness(0, 0, 0, 3)));
        AddOptionalCentered(root, ticket.LegalName, baseSize);
        AddOptionalCentered(root, string.IsNullOrWhiteSpace(ticket.TaxId) ? string.Empty : $"RFC: {ticket.TaxId}", baseSize);
        AddOptionalCentered(root, ticket.Address, baseSize);
        AddOptionalCentered(root, string.IsNullOrWhiteSpace(ticket.Phone) ? string.Empty : $"Tel: {ticket.Phone}", baseSize);
        AddOptionalCentered(root, ticket.Header, baseSize);
        root.Children.Add(Rule());
        root.Children.Add(Text("COMPROBANTE DE VENTA", baseSize + 1d, FontWeights.SemiBold, TextAlignment.Center, new Thickness(0, 1, 0, 4)));

        root.Children.Add(Metadata("Fecha", ticket.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"), baseSize));
        root.Children.Add(Metadata("Caja", ValueOrDefault(ticket.RegisterName, "Caja principal"), baseSize));
        root.Children.Add(Metadata("Cajero", ValueOrDefault(ticket.CashierName, "Administrador"), baseSize));
        root.Children.Add(Metadata("Turno", ShortId(ticket.ShiftId), baseSize));
        root.Children.Add(Metadata("Venta", FormatFolio(ticket.Folio, ticket.SaleId), baseSize));
        root.Children.Add(Rule());

        root.Children.Add(ProductHeader(baseSize));
        foreach (var line in ticket.Lines) root.Children.Add(ProductLine(ticket, line, baseSize));
        root.Children.Add(Rule());

        root.Children.Add(Text($"Articulos: {ticket.Lines.Sum(line => line.Quantity):0.###}", baseSize, FontWeights.Normal, TextAlignment.Left, new Thickness(0, 1, 0, 4)));
        var totalWeight = profile.UseNormalTotals ? FontWeights.Normal : FontWeights.Bold;
        root.Children.Add(AmountLine(ticket, "Subtotal", ticket.Total, baseSize + 1d, totalWeight));
        root.Children.Add(AmountLine(ticket, "TOTAL", ticket.Total, baseSize + 4d, totalWeight));
        foreach (var payment in ticket.Payments.Where(payment => payment.Amount > 0m))
            root.Children.Add(AmountLine(ticket, PaymentLabel(payment.Method), payment.Amount, baseSize, FontWeights.Normal));

        var received = ticket.Payments.Where(payment => payment.Method.Equals("Cash", StringComparison.OrdinalIgnoreCase)).Sum(payment => payment.Received);
        var change = ticket.Payments.Sum(payment => payment.Change);
        if (received > 0m) root.Children.Add(AmountLine(ticket, "Recibido", received, baseSize, FontWeights.Normal));
        if (received > 0m || change > 0m) root.Children.Add(AmountLine(ticket, "Cambio", change, baseSize + 1d, totalWeight));

        root.Children.Add(Rule());
        AddOptionalCentered(root, string.IsNullOrWhiteSpace(ticket.Footer) ? "Gracias por su compra" : ticket.Footer, baseSize);
        root.Children.Add(Text("Conserve este comprobante", Math.Max(7d, baseSize - 1d), FontWeights.Normal, TextAlignment.Center, new Thickness(0, 1, 0, 5)));

        return new Border
        {
            Width = pageWidth,
            Padding = new Thickness(padding),
            Background = Brushes.White,
            Child = root
        };
    }

    public static void Print(string printerName, TicketPdfData ticket, TicketPrintProfile profile, string jobName)
    {
        if (string.IsNullOrWhiteSpace(printerName)) throw new InvalidOperationException("Selecciona una impresora de Windows.");
        using var server = new LocalPrintServer();
        using var queue = server.GetPrintQueue(printerName);
        var visual = CreateTicketVisual(ticket, profile);
        var pageWidth = (profile.WidthMm == 58 ? 58d : 80d) * DipsPerMillimeter;
        visual.Measure(new Size(pageWidth, double.PositiveInfinity));
        var pageHeight = Math.Max(300d, visual.DesiredSize.Height);
        visual.Arrange(new Rect(0, 0, pageWidth, pageHeight));

        var page = new FixedPage { Width = pageWidth, Height = pageHeight, Background = Brushes.White };
        page.Children.Add(visual);
        var pageContent = new PageContent();
        ((IAddChild)pageContent).AddChild(page);
        var document = new FixedDocument();
        document.Pages.Add(pageContent);
        document.DocumentPaginator.PageSize = new Size(pageWidth, pageHeight);

        var ticketProfile = queue.DefaultPrintTicket.Clone();
        ticketProfile.PageOrientation = PageOrientation.Portrait;
        ticketProfile.PageMediaSize = new PageMediaSize(pageWidth, pageHeight);
        var validatedTicket = queue.MergeAndValidatePrintTicket(queue.DefaultPrintTicket, ticketProfile).ValidatedPrintTicket;
        var writer = PrintQueue.CreateXpsDocumentWriter(queue);
        writer.Write(document.DocumentPaginator, validatedTicket);
    }

    public static void OpenCashDrawer(string printerName, string model)
    {
        if (string.IsNullOrWhiteSpace(printerName)) throw new InvalidOperationException("Selecciona una impresora de Windows.");
        var pin = model.EndsWith("2", StringComparison.OrdinalIgnoreCase) ? (byte)1 : (byte)0;
        var pulse = new byte[] { 0x1B, 0x70, pin, 0x19, 0xFA };
        if (!OpenPrinter(printerName, out var handle, nint.Zero)) ThrowWin32("Windows no pudo abrir la cola de impresión");
        try
        {
            var document = new NativeDocInfo { DocName = "JetVenta - Apertura de cajón", DataType = "RAW" };
            if (StartDocPrinter(handle, 1, document) == 0) ThrowWin32("No se pudo iniciar el trabajo de apertura");
            try
            {
                if (!StartPagePrinter(handle)) ThrowWin32("No se pudo iniciar la página de apertura");
                try
                {
                    if (!WritePrinter(handle, pulse, pulse.Length, out var written) || written != pulse.Length) ThrowWin32("La impresora no aceptó el pulso del cajón");
                }
                finally { EndPagePrinter(handle); }
            }
            finally { EndDocPrinter(handle); }
        }
        finally { ClosePrinter(handle); }
    }

    public static void PrintCashMovement(string printerName, decimal amount, string type, string reason, string? providerName, TicketPrintProfile profile)
    {
        var detail = string.IsNullOrWhiteSpace(providerName) ? reason : $"{reason} ({providerName})";
        var movementLabel = type.Equals("Out", StringComparison.OrdinalIgnoreCase) ? "salida" : "entrada";
        var ticket = new TicketPdfData(
            "JETVENTA",
            Guid.Empty,
            DateTimeOffset.Now,
            [new TicketPdfLine(detail, 1m, amount, amount)],
            amount,
            amount,
            0m)
        {
            Header = $"Comprobante de {movementLabel} de efectivo",
            Footer = "Conserve este comprobante",
            WidthMm = profile.WidthMm
        };
        Print(printerName, ticket, profile, $"Movimiento de efectivo {DateTime.Now:yyyyMMddHHmmss}");
    }

    private static TextBlock Text(string value, double size, FontWeight weight, TextAlignment alignment, Thickness margin) => new()
    {
        Text = value,
        FontSize = size,
        FontWeight = weight,
        TextAlignment = alignment,
        TextWrapping = TextWrapping.Wrap,
        Margin = margin
    };

    private static void AddOptionalCentered(Panel root, string value, double size)
    {
        if (!string.IsNullOrWhiteSpace(value)) root.Children.Add(Text(value.Trim(), size, FontWeights.Normal, TextAlignment.Center, new Thickness(0, 0, 0, 2)));
    }

    private static Border Rule() => new() { Height = 1, Background = Brushes.Black, Margin = new Thickness(0, 5, 0, 5) };

    private static Grid Metadata(string label, string value, double size)
    {
        var grid = TwoColumnGrid(0.28d, 0.72d);
        grid.Margin = new Thickness(0, 0, 0, 1);
        AddCell(grid, label + ":", 0, size, FontWeights.SemiBold, TextAlignment.Left);
        AddCell(grid, value, 1, size, FontWeights.Normal, TextAlignment.Right);
        return grid;
    }

    private static Grid ProductHeader(double size)
    {
        var grid = ProductGrid();
        grid.Margin = new Thickness(0, 0, 0, 3);
        AddCell(grid, "Cant.", 0, size, FontWeights.Bold, TextAlignment.Left);
        AddCell(grid, "Descripcion", 1, size, FontWeights.Bold, TextAlignment.Left);
        AddCell(grid, "Precio", 2, size, FontWeights.Bold, TextAlignment.Right);
        AddCell(grid, "Importe", 3, size, FontWeights.Bold, TextAlignment.Right);
        return grid;
    }

    private static Grid ProductLine(TicketPdfData ticket, TicketPdfLine line, double size)
    {
        var grid = ProductGrid();
        grid.Margin = new Thickness(0, 1, 0, 2);
        AddCell(grid, line.Quantity.ToString("0.###", CultureInfo.InvariantCulture), 0, size, FontWeights.Normal, TextAlignment.Left);
        AddCell(grid, line.Description, 1, size, FontWeights.Normal, TextAlignment.Left);
        AddCell(grid, Money(ticket, line.UnitPrice), 2, size, FontWeights.Normal, TextAlignment.Right);
        AddCell(grid, Money(ticket, line.Total), 3, size, FontWeights.Normal, TextAlignment.Right);
        return grid;
    }

    private static Grid AmountLine(TicketPdfData ticket, string label, decimal amount, double size, FontWeight weight)
    {
        var grid = TwoColumnGrid(0.55d, 0.45d);
        grid.Margin = new Thickness(0, 1, 0, 1);
        AddCell(grid, label + ":", 0, size, weight, TextAlignment.Right);
        AddCell(grid, Money(ticket, amount), 1, size, weight, TextAlignment.Right);
        return grid;
    }

    private static Grid ProductGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.14d, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.43d, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.20d, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.23d, GridUnitType.Star) });
        return grid;
    }

    private static Grid TwoColumnGrid(double first, double second)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(first, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(second, GridUnitType.Star) });
        return grid;
    }

    private static void AddCell(Grid grid, string value, int column, double size, FontWeight weight, TextAlignment alignment)
    {
        var text = Text(value, size, weight, alignment, new Thickness(column == 0 ? 0 : 2, 0, 0, 0));
        Grid.SetColumn(text, column);
        grid.Children.Add(text);
    }

    private static string ValueOrDefault(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static string ShortId(Guid value) => value == Guid.Empty ? "N/D" : value.ToString("N")[..8].ToUpperInvariant();
    private static string FormatFolio(long folio, Guid saleId) => folio > 0 ? folio.ToString("N0", CultureInfo.CurrentCulture) : "N/D";
    private static string Money(TicketPdfData ticket, decimal value) => (string.IsNullOrWhiteSpace(ticket.CurrencySymbol) ? "$" : ticket.CurrencySymbol.Trim()) + value.ToString("#,##0.00", CultureInfo.InvariantCulture);
    private static string PaymentLabel(string method) => method.ToUpperInvariant() switch
    {
        "CASH" => "Efectivo",
        "CARD" => "Tarjeta",
        "TRANSFER" => "Transferencia",
        "CREDIT" => "Credito",
        _ => method
    };

    private static void ThrowWin32(string message) => throw new InvalidOperationException($"{message}. Código de Windows: {Marshal.GetLastWin32Error()}.");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class NativeDocInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string DocName = string.Empty;
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string DataType = "RAW";
    }

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool OpenPrinter(string printerName, out nint printer, nint defaults);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool ClosePrinter(nint printer);
    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)] private static extern int StartDocPrinter(nint printer, int level, [In] NativeDocInfo document);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool EndDocPrinter(nint printer);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool StartPagePrinter(nint printer);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool EndPagePrinter(nint printer);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool WritePrinter(nint printer, byte[] data, int count, out int written);
}

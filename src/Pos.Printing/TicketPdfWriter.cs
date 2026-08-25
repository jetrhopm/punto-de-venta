using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Pos.Printing;

public sealed record TicketPdfLine(string Description, decimal Quantity, decimal UnitPrice, decimal Total);
public sealed record TicketPdfPayment(string Method, decimal Amount, decimal Received, decimal Change);

[method: JsonConstructor]
public sealed record TicketPdfData(
    string StoreName,
    string LegalName,
    string TaxId,
    string Address,
    string Phone,
    string Header,
    string Footer,
    int WidthMm,
    Guid SaleId,
    Guid ShiftId,
    string RegisterName,
    string CashierName,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<TicketPdfLine> Lines,
    IReadOnlyList<TicketPdfPayment> Payments,
    decimal Total,
    string CurrencySymbol = "$",
    long Folio = 0,
    long ShiftNumber = 0)
{
    public TicketPdfData(string storeName, Guid saleId, DateTimeOffset createdAtUtc, IReadOnlyList<TicketPdfLine> lines, decimal total, decimal received, decimal change)
        : this(storeName, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, "Gracias por su compra", 80, saleId, Guid.Empty, "Caja principal", "Administrador", createdAtUtc, lines, [new TicketPdfPayment("Cash", total, received, change)], total) { }
}

public static class TicketPdfWriter
{
    private const decimal PointsPerMillimeter = 72m / 25.4m;

    public static byte[] Create(TicketPdfData ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var widthMm = ticket.WidthMm == 58 ? 58 : 80;
        var pageWidth = (decimal)widthMm * PointsPerMillimeter;
        var margin = widthMm == 58 ? 8m : 10m;
        var normalSize = widthMm == 58 ? 7m : 8m;
        var maxCharacters = Math.Max(28, (int)Math.Floor((pageWidth - (margin * 2m)) / (normalSize * 0.6m)));
        var rows = BuildRows(ticket, widthMm, maxCharacters, normalSize);
        var contentHeight = rows.Sum(row => row.IsRule ? row.SpaceAfter + 2m : row.FontSize + row.SpaceAfter);
        var pageHeight = Math.Max(216m, contentHeight + (margin * 2m) + 6m);
        var content = BuildContent(rows, pageWidth, pageHeight, margin);
        return BuildPdf(pageWidth, pageHeight, content, ticket.StoreName, ticket.SaleId);
    }

    private static List<LayoutRow> BuildRows(TicketPdfData ticket, int widthMm, int maxCharacters, decimal normalSize)
    {
        var rows = new List<LayoutRow>();
        var titleSize = widthMm == 58 ? 11m : 13m;
        var totalSize = widthMm == 58 ? 10m : 12m;

        AddWrapped(rows, ticket.StoreName.ToUpperInvariant(), maxCharacters, TextAlignment.Center, titleSize, true, 4m);
        if (!string.IsNullOrWhiteSpace(ticket.LegalName) && !string.Equals(ticket.LegalName.Trim(), ticket.StoreName.Trim(), StringComparison.OrdinalIgnoreCase))
            AddWrapped(rows, ticket.LegalName.ToUpperInvariant(), maxCharacters, TextAlignment.Center, normalSize, false, 2m);
        if (!string.IsNullOrWhiteSpace(ticket.TaxId)) AddWrapped(rows, $"RFC: {ticket.TaxId.ToUpperInvariant()}", maxCharacters, TextAlignment.Center, normalSize, false, 2m);
        if (!string.IsNullOrWhiteSpace(ticket.Address)) AddWrapped(rows, ticket.Address.ToUpperInvariant(), maxCharacters, TextAlignment.Center, normalSize, false, 2m);
        if (!string.IsNullOrWhiteSpace(ticket.Phone)) AddWrapped(rows, $"TEL: {ticket.Phone}", maxCharacters, TextAlignment.Center, normalSize, false, 2m);
        if (!string.IsNullOrWhiteSpace(ticket.Header)) AddWrapped(rows, ticket.Header, maxCharacters, TextAlignment.Center, normalSize, false, 3m);

        AddRule(rows);
        rows.Add(new LayoutRow("COMPROBANTE DE VENTA", normalSize + 1m, true, TextAlignment.Center, 4m));
        rows.Add(new LayoutRow($"FECHA: {ticket.CreatedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss}", normalSize, false, TextAlignment.Left, 2m));
        rows.Add(new LayoutRow($"CAJA: {ValueOrDefault(ticket.RegisterName, "CAJA PRINCIPAL")}", normalSize, false, TextAlignment.Left, 2m));
        rows.Add(new LayoutRow($"CAJERO: {ValueOrDefault(ticket.CashierName, "ADMINISTRADOR")}", normalSize, false, TextAlignment.Left, 2m));
        rows.Add(new LayoutRow($"TURNO: {FormatShiftNumber(ticket.ShiftNumber)}", normalSize, false, TextAlignment.Left, 2m));
        rows.Add(new LayoutRow($"VENTA: {FormatFolio(ticket.Folio, ticket.SaleId)}", normalSize, false, TextAlignment.Left, 3m));
        AddRule(rows);

        var quantityWidth = widthMm == 58 ? 5 : 6;
        var priceWidth = widthMm == 58 ? 8 : 10;
        var amountWidth = widthMm == 58 ? 9 : 10;
        var descriptionWidth = Math.Max(8, maxCharacters - quantityWidth - priceWidth - amountWidth - 3);
        rows.Add(new LayoutRow(
            $"{FitLeft("CANT", quantityWidth)} {FitLeft("DESCRIPCION", descriptionWidth)} {FitRight("PRECIO", priceWidth)} {FitRight("IMPORTE", amountWidth)}",
            normalSize,
            true,
            TextAlignment.Left,
            3m));

        foreach (var line in ticket.Lines)
        {
            var descriptionLines = Wrap(line.Description.ToUpperInvariant(), descriptionWidth).ToArray();
            if (descriptionLines.Length == 0) descriptionLines = ["PRODUCTO"];
            for (var index = 0; index < descriptionLines.Length; index++)
            {
                var quantity = index == 0 ? line.Quantity.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
                var price = index == 0 ? Money(ticket, line.UnitPrice) : string.Empty;
                var amount = index == 0 ? Money(ticket, line.Total) : string.Empty;
                rows.Add(new LayoutRow(
                    $"{FitRight(quantity, quantityWidth)} {FitLeft(descriptionLines[index], descriptionWidth)} {FitRight(price, priceWidth)} {FitRight(amount, amountWidth)}",
                    normalSize,
                    false,
                    TextAlignment.Left,
                    index == descriptionLines.Length - 1 ? 2m : 1m));
            }
        }

        AddRule(rows);
        var itemCount = ticket.Lines.Sum(line => line.Quantity);
        rows.Add(new LayoutRow($"ARTICULOS: {itemCount:0.###}", normalSize, false, TextAlignment.Left, 4m));
        rows.Add(new LayoutRow($"SUBTOTAL: {Money(ticket, ticket.Total)}", normalSize + 1m, true, TextAlignment.Right, 3m));
        rows.Add(new LayoutRow($"TOTAL: {Money(ticket, ticket.Total)}", totalSize, true, TextAlignment.Right, 5m));

        foreach (var payment in ticket.Payments.Where(payment => payment.Amount > 0m))
            rows.Add(new LayoutRow($"{PaymentLabel(payment.Method)}: {Money(ticket, payment.Amount)}", normalSize, false, TextAlignment.Right, 2m));

        var cashReceived = ticket.Payments.Where(payment => payment.Method.Equals("Cash", StringComparison.OrdinalIgnoreCase)).Sum(payment => payment.Received);
        var change = ticket.Payments.Sum(payment => payment.Change);
        if (cashReceived > 0m) rows.Add(new LayoutRow($"RECIBIDO: {Money(ticket, cashReceived)}", normalSize, false, TextAlignment.Right, 2m));
        if (change > 0m || cashReceived > 0m) rows.Add(new LayoutRow($"CAMBIO: {Money(ticket, change)}", normalSize + 1m, true, TextAlignment.Right, 4m));

        AddRule(rows);
        AddWrapped(rows, string.IsNullOrWhiteSpace(ticket.Footer) ? "GRACIAS POR SU COMPRA" : ticket.Footer, maxCharacters, TextAlignment.Center, normalSize, false, 3m);
        rows.Add(new LayoutRow("CONSERVE ESTE COMPROBANTE", normalSize - 0.5m, false, TextAlignment.Center, 4m));
        return rows;
    }

    private static string BuildContent(IReadOnlyList<LayoutRow> rows, decimal pageWidth, decimal pageHeight, decimal margin)
    {
        var content = new StringBuilder();
        var y = pageHeight - margin;
        foreach (var row in rows)
        {
            if (row.IsRule)
            {
                y -= 2m;
                content.Append("0.45 w ").Append(Number(margin)).Append(' ').Append(Number(y)).Append(" m ")
                    .Append(Number(pageWidth - margin)).Append(' ').Append(Number(y)).Append(" l S\n");
                y -= row.SpaceAfter;
                continue;
            }

            y -= row.FontSize;
            var text = Sanitize(row.Text ?? string.Empty);
            var textWidth = text.Length * row.FontSize * 0.6m;
            var x = row.Alignment switch
            {
                TextAlignment.Center => Math.Max(margin, (pageWidth - textWidth) / 2m),
                TextAlignment.Right => Math.Max(margin, pageWidth - margin - textWidth),
                _ => margin
            };
            content.Append("BT /").Append(row.Bold ? "F2" : "F1").Append(' ').Append(Number(row.FontSize)).Append(" Tf 1 0 0 1 ")
                .Append(Number(x)).Append(' ').Append(Number(y)).Append(" Tm (").Append(Escape(text)).Append(") Tj ET\n");
            y -= row.SpaceAfter;
        }
        return content.ToString();
    }

    private static byte[] BuildPdf(decimal pageWidth, decimal pageHeight, string content, string storeName, Guid saleId)
    {
        var contentBytes = Encoding.Latin1.GetBytes(content);
        var streamObject = new MemoryStream();
        WriteAscii(streamObject, $"<< /Length {contentBytes.Length} >>\nstream\n");
        streamObject.Write(contentBytes);
        WriteAscii(streamObject, "\nendstream");

        var mediaBox = $"[0 0 {Number(pageWidth)} {Number(pageHeight)}]";
        var objects = new List<byte[]>
        {
            Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"),
            Encoding.ASCII.GetBytes("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Encoding.ASCII.GetBytes($"<< /Type /Page /Parent 2 0 R /MediaBox {mediaBox} /CropBox {mediaBox} /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >>"),
            Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>"),
            Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Courier-Bold /Encoding /WinAnsiEncoding >>"),
            streamObject.ToArray(),
            Encoding.Latin1.GetBytes($"<< /Title ({Escape(Sanitize($"Ticket {ShortId(saleId)} - {storeName}"))}) /Creator (JetVenta) /Producer (JetVenta PDF) >>")
        };

        using var output = new MemoryStream();
        WriteAscii(output, "%PDF-1.4\n");
        output.Write([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]);
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(output.Position);
            WriteAscii(output, $"{index + 1} 0 obj\n");
            output.Write(objects[index]);
            WriteAscii(output, "\nendobj\n");
        }

        var xref = output.Position;
        WriteAscii(output, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) WriteAscii(output, $"{offset:0000000000} 00000 n \n");
        WriteAscii(output, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /Info 7 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    private static void AddWrapped(List<LayoutRow> rows, string value, int width, TextAlignment alignment, decimal fontSize, bool bold, decimal spaceAfter)
    {
        var wrapped = value.Replace("\r", string.Empty).Split('\n').SelectMany(line => Wrap(line, width)).ToArray();
        for (var index = 0; index < wrapped.Length; index++)
            rows.Add(new LayoutRow(wrapped[index], fontSize, bold, alignment, index == wrapped.Length - 1 ? spaceAfter : 1m));
    }

    private static IEnumerable<string> Wrap(string value, int width)
    {
        var remaining = value.Trim();
        if (remaining.Length == 0) yield break;
        while (remaining.Length > width)
        {
            var split = remaining.LastIndexOf(' ', width);
            if (split <= 0) split = width;
            yield return remaining[..split].TrimEnd();
            remaining = remaining[split..].TrimStart();
        }
        if (remaining.Length > 0) yield return remaining;
    }

    private static void AddRule(List<LayoutRow> rows) => rows.Add(new LayoutRow(null, 0m, false, TextAlignment.Left, 6m, true));
    private static string ValueOrDefault(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToUpperInvariant();
    private static string ShortId(Guid value) => value == Guid.Empty ? "N/D" : value.ToString("N")[..8].ToUpperInvariant();
    private static string FormatShiftNumber(long shiftNumber) => shiftNumber > 0 ? shiftNumber.ToString("N0", CultureInfo.CurrentCulture) : "N/D";
    private static string FormatFolio(long folio, Guid saleId) => folio > 0 ? folio.ToString("N0", CultureInfo.CurrentCulture) : "N/D";
    private static string Money(TicketPdfData ticket, decimal value) => (string.IsNullOrWhiteSpace(ticket.CurrencySymbol) ? "$" : ticket.CurrencySymbol.Trim()) + value.ToString("#,##0.00", CultureInfo.InvariantCulture);
    private static string PaymentLabel(string method) => method.ToUpperInvariant() switch { "CASH" => "EFECTIVO", "CARD" => "TARJETA", "TRANSFER" => "TRANSFERENCIA", "CREDIT" => "CREDITO", _ => method.ToUpperInvariant() };
    private static string FitLeft(string value, int width) => value.Length > width ? value[..width] : value.PadRight(width);
    private static string FitRight(string value, int width) => value.Length > width ? value[^width..] : value.PadLeft(width);
    private static string Number(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    private static string Sanitize(string value) => new(value.Select(character => character switch { '\r' or '\n' or '\t' => ' ', '–' or '—' => '-', _ when character <= 255 && !char.IsControl(character) => character, _ => '?' }).ToArray());
    private static void WriteAscii(Stream stream, string value) => stream.Write(Encoding.ASCII.GetBytes(value));

    private enum TextAlignment { Left, Center, Right }
    private sealed record LayoutRow(string? Text, decimal FontSize, bool Bold, TextAlignment Alignment, decimal SpaceAfter, bool IsRule = false);
}

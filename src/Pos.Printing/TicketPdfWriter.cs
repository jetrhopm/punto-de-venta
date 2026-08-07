using System.Text;

namespace Pos.Printing;

public sealed record TicketPdfLine(string Description, decimal Quantity, decimal UnitPrice, decimal Total);
public sealed record TicketPdfData(string StoreName, string Header, string Footer, int WidthMm, Guid SaleId, DateTimeOffset CreatedAtUtc, IReadOnlyList<TicketPdfLine> Lines, decimal Total, decimal Received, decimal Change)
{
    public TicketPdfData(string storeName, Guid saleId, DateTimeOffset createdAtUtc, IReadOnlyList<TicketPdfLine> lines, decimal total, decimal received, decimal change)
        : this(storeName, string.Empty, "Gracias por su compra", 80, saleId, createdAtUtc, lines, total, received, change) { }
}

public static class TicketPdfWriter
{
    public static byte[] Create(TicketPdfData ticket)
    {
        var lines = new List<string> { ticket.StoreName, ticket.Header, "TICKET DE VENTA", $"Folio: {ticket.SaleId}", ticket.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), "" };
        lines.AddRange(ticket.Lines.Select(line => $"{line.Quantity:0.###} x {line.Description}  ${line.Total:0.00}"));
        lines.AddRange(["", $"TOTAL: ${ticket.Total:0.00}", $"RECIBIDO: ${ticket.Received:0.00}", $"CAMBIO: ${ticket.Change:0.00}", "", ticket.Footer, "COPIA DIGITAL"]);
        var content = new StringBuilder("BT\n/F1 9 Tf\n12 TL\n");
        foreach (var line in lines) content.Append("(").Append(Escape(ToAscii(line))).Append(") Tj T*\n");
        content.Append("ET\n");
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 164 420] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content.ToString())} >>\nstream\n{content}endstream"
        };
        using var output = new MemoryStream();
        using var writer = new StreamWriter(output, Encoding.ASCII, leaveOpen: true);
        writer.WriteLine("%PDF-1.4"); writer.Flush();
        var offsets = new List<long> { 0 };
        foreach (var (value, index) in objects.Select((value, index) => (value, index + 1))) { offsets.Add(output.Position); writer.WriteLine($"{index} 0 obj"); writer.WriteLine(value); writer.WriteLine("endobj"); writer.Flush(); }
        var xref = output.Position; writer.WriteLine($"xref\n0 {objects.Count + 1}"); writer.WriteLine("0000000000 65535 f "); foreach (var offset in offsets.Skip(1)) writer.WriteLine($"{offset:0000000000} 00000 n "); writer.WriteLine($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF"); writer.Flush();
        return output.ToArray();
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    private static string ToAscii(string value) => new(value.Select(character => character switch { 'á' or 'Á' => 'A', 'é' or 'É' => 'E', 'í' or 'Í' => 'I', 'ó' or 'Ó' => 'O', 'ú' or 'Ú' => 'U', 'ñ' or 'Ñ' => 'N', _ when character <= 127 => character, _ => '?' }).ToArray());
}
